using System.Text;
using Tmds.DBus.Protocol;

namespace WiimControl;

sealed class LinuxMediaSession : IMediaSession, IMethodHandler
{
    private const string BusName = "org.mpris.MediaPlayer2.WiimControl";
    private const string ObjectPathName = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/>
          <method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/>
          <property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/>
          <property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/>
          <property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/>
          <method name="Previous"/>
          <method name="Pause"/>
          <method name="PlayPause"/>
          <method name="Stop"/>
          <method name="Play"/>
          <method name="Seek"><arg direction="in" type="x" name="Offset"/></method>
          <method name="SetPosition"><arg direction="in" type="o" name="TrackId"/><arg direction="in" type="x" name="Position"/></method>
          <method name="OpenUri"><arg direction="in" type="s" name="Uri"/></method>
          <signal name="Seeked"><arg type="x" name="Position"/></signal>
          <property name="PlaybackStatus" type="s" access="read"/>
          <property name="Rate" type="d" access="read"/>
          <property name="Metadata" type="a{sv}" access="read"/>
          <property name="Volume" type="d" access="readwrite"/>
          <property name="Position" type="x" access="read"/>
          <property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/>
          <property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/>
          <property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/>
          <property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read"/>
        </interface>
        """);

    private static readonly string[] RootProperties =
        ["CanQuit", "CanRaise", "HasTrackList", "Identity", "DesktopEntry", "SupportedUriSchemes", "SupportedMimeTypes"];
    private static readonly string[] PlayerProperties =
        ["PlaybackStatus", "Rate", "Metadata", "Volume", "Position", "MinimumRate", "MaximumRate",
         "CanGoNext", "CanGoPrevious", "CanPlay", "CanPause", "CanSeek", "CanControl"];

    private readonly Action<string> _onCommand;
    private readonly Action<int> _onSeek;
    private readonly Action _onRaise;
    private readonly Action<int> _onVolume;
    private readonly object _gate = new();
    private Connection? _connection;
    private bool _disposed;
    private DeviceStatus? _status;
    private DateTime _statusTime = DateTime.UtcNow;
    private string _trackKey = string.Empty;
    private int _trackNumber;

    public string Path => ObjectPathName;

    private LinuxMediaSession(Action<string> onCommand, Action<int> onSeek, Action onRaise, Action<int> onVolume)
    {
        _onCommand = onCommand;
        _onSeek = onSeek;
        _onRaise = onRaise;
        _onVolume = onVolume;
    }

    public static LinuxMediaSession? Create(Action<string> onCommand, Action<int> onSeek, Action onRaise, Action<int> onVolume)
    {
        if (string.IsNullOrEmpty(Address.Session)) return null;
        var session = new LinuxMediaSession(onCommand, onSeek, onRaise, onVolume);
        _ = session.StartAsync();
        return session;
    }

    private async Task StartAsync()
    {
        var connection = new Connection(Address.Session!);
        try
        {
            await connection.ConnectAsync();
            connection.AddMethodHandler(this);
            await connection.CallMethodAsync(RequestNameMessage(connection));
            lock (_gate)
            {
                if (!_disposed)
                {
                    _connection = connection;
                    return;
                }
            }
            connection.Dispose();
        }
        catch (Exception ex)
        {
            connection.Dispose();
            bool disposed;
            lock (_gate) disposed = _disposed;
            if (!disposed) ErrorLog.Write("Media controls", ex);
        }
    }

    private static MessageBuffer RequestNameMessage(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su",
            MessageFlags.None);
        writer.WriteString(BusName);
        writer.WriteUInt32(4);
        return writer.CreateMessage();
    }

    public void Update(DeviceStatus? status)
    {
        string? changedMetadata = null;
        bool statusChanged, volumeChanged;
        lock (_gate)
        {
            string key = status == null ? string.Empty : $"{status.Title}\n{status.Artist}\n{status.Album}\n{status.ArtUrl}\n{status.DurationMs}";
            if (key != _trackKey)
            {
                _trackKey = key;
                _trackNumber++;
                changedMetadata = key;
            }
            statusChanged = PlaybackStatus(_status) != PlaybackStatus(status) || (_status?.DurationMs > 0) != (status?.DurationMs > 0);
            volumeChanged = _status?.Volume.Volume != status?.Volume.Volume;
            _status = status;
            _statusTime = DateTime.UtcNow;
        }
        if (changedMetadata != null || statusChanged || volumeChanged) SendPropertiesChanged();
    }

    private static string PlaybackStatus(DeviceStatus? status) => status?.PlayState.ToLowerInvariant() switch
    {
        "play" or "load" => "Playing",
        "pause" => "Paused",
        _ => "Stopped"
    };

    private long PositionMicroseconds()
    {
        if (_status == null || _status.DurationMs <= 0) return 0;
        long ms = _status.PositionMs;
        if (_status.IsPlaying) ms += (long)(DateTime.UtcNow - _statusTime).TotalMilliseconds;
        ms = Math.Min(ms, _status.DurationMs);
        return Math.Max(0, ms) * 1000;
    }

    private string TrackId => $"/org/mpris/MediaPlayer2/WiimControl/Track{_trackNumber}";

    private Dictionary<string, VariantValue> Metadata()
    {
        var metadata = new Dictionary<string, VariantValue> { ["mpris:trackid"] = VariantValue.ObjectPath(new ObjectPath(TrackId)) };
        if (_status == null) return metadata;
        string title = _status.Title.Length > 0 ? _status.Title : WiimController.SourceName(_status.Mode);
        if (title.Length > 0) metadata["xesam:title"] = VariantValue.String(title);
        if (_status.Artist.Length > 0) metadata["xesam:artist"] = VariantValue.Array(new[] { _status.Artist });
        if (_status.Album.Length > 0) metadata["xesam:album"] = VariantValue.String(_status.Album);
        if (Uri.TryCreate(_status.ArtUrl, UriKind.Absolute, out _)) metadata["mpris:artUrl"] = VariantValue.String(_status.ArtUrl);
        if (_status.DurationMs > 0) metadata["mpris:length"] = VariantValue.Int64(_status.DurationMs * 1000L);
        return metadata;
    }

    private bool WriteProperty(ref MessageWriter writer, string name)
    {
        bool canSeek = _status?.DurationMs > 0;
        switch (name)
        {
            case "CanQuit" or "HasTrackList": writer.WriteVariantBool(false); return true;
            case "CanRaise" or "CanGoNext" or "CanGoPrevious" or "CanPlay" or "CanPause" or "CanControl": writer.WriteVariantBool(true); return true;
            case "CanSeek": writer.WriteVariantBool(canSeek); return true;
            case "Identity": writer.WriteVariantString("Wiim Control"); return true;
            case "DesktopEntry": writer.WriteVariantString("wiim-control"); return true;
            case "SupportedUriSchemes" or "SupportedMimeTypes":
                writer.WriteSignature("as");
                writer.WriteArray(Array.Empty<string>());
                return true;
            case "PlaybackStatus": writer.WriteVariantString(PlaybackStatus(_status)); return true;
            case "Rate" or "MinimumRate" or "MaximumRate": writer.WriteVariantDouble(1.0); return true;
            case "Volume": writer.WriteVariantDouble(_status != null ? _status.Volume.Volume / 100.0 : 0); return true;
            case "Position": writer.WriteVariantInt64(PositionMicroseconds()); return true;
            case "Metadata":
                writer.WriteSignature("a{sv}");
                writer.WriteDictionary(Metadata());
                return true;
            default: return false;
        }
    }

    private void WriteProperties(ref MessageWriter writer, IEnumerable<string> names)
    {
        var dictionary = writer.WriteDictionaryStart();
        foreach (var name in names)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteProperty(ref writer, name);
        }
        writer.WriteDictionaryEnd(dictionary);
    }

    private void SendPropertiesChanged()
    {
        Connection? connection;
        lock (_gate) connection = _connection;
        if (connection == null) return;
        try
        {
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(null, ObjectPathName, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
                writer.WriteString(PlayerInterface);
                lock (_gate) WriteProperties(ref writer, ["Metadata", "PlaybackStatus", "CanSeek", "Volume"]);
                writer.WriteArray(Array.Empty<string>());
                connection.TrySendMessage(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Media controls", ex);
        }
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        try
        {
            Handle(context);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Media controls", ex);
            if (!context.ReplySent && !context.NoReplyExpected) context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message);
        }
        return ValueTask.CompletedTask;
    }

    private void Handle(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectionXml]);
            return;
        }
        var request = context.Request;
        string iface = request.InterfaceAsString ?? string.Empty, member = request.MemberAsString ?? string.Empty;

        if (iface == PropertiesInterface)
        {
            var reader = request.GetBodyReader();
            string target = reader.ReadString();
            if (member == "Get")
            {
                string name = reader.ReadString();
                var writer = context.CreateReplyWriter("v");
                try
                {
                    bool known;
                    lock (_gate) known = WriteProperty(ref writer, name);
                    if (known) context.Reply(writer.CreateMessage());
                    else context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property {name}");
                }
                finally
                {
                    writer.Dispose();
                }
            }
            else if (member == "GetAll")
            {
                var writer = context.CreateReplyWriter("a{sv}");
                try
                {
                    var names = target == RootInterface ? RootProperties : target == PlayerInterface ? PlayerProperties : [];
                    lock (_gate) WriteProperties(ref writer, names);
                    context.Reply(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }
            }
            else if (member == "Set") SetProperty(context, target, reader.ReadString(), reader.ReadVariantValue());
            else ReplyEmpty(context);
            return;
        }

        switch (member)
        {
            case "Raise": _onRaise(); break;
            case "Play": _onCommand("resume"); break;
            case "Pause": _onCommand("pause"); break;
            case "PlayPause": _onCommand("playpause"); break;
            case "Next": _onCommand("next"); break;
            case "Previous": _onCommand("prev"); break;
            case "Stop": _onCommand("stop"); break;
            case "Seek":
            {
                long offset = request.GetBodyReader().ReadInt64();
                long position;
                lock (_gate) position = PositionMicroseconds();
                _onSeek((int)Math.Max(0, (position + offset) / 1_000_000));
                break;
            }
            case "SetPosition":
            {
                var reader = request.GetBodyReader();
                reader.ReadObjectPathAsString();
                _onSeek((int)Math.Max(0, reader.ReadInt64() / 1_000_000));
                break;
            }
        }
        ReplyEmpty(context);
    }

    private void SetProperty(MethodContext context, string target, string name, VariantValue value)
    {
        if (target == PlayerInterface && name == "Volume")
        {
            double level = value.Type == VariantValueType.Double ? value.GetDouble() : 0;
            _onVolume((int)Math.Round(Math.Clamp(level, 0, 1) * 100));
            ReplyEmpty(context);
            return;
        }
        if (!context.NoReplyExpected) context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", $"{name} can't be changed");
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected || context.ReplySent) return;
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    public void Dispose()
    {
        Connection? connection;
        lock (_gate)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
        }
        try { connection?.Dispose(); }
        catch (Exception) { }
    }
}
