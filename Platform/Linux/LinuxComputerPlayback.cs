using Tmds.DBus.Protocol;

namespace WiimControl;

sealed class LinuxComputerPlayback : IComputerPlayback
{
    private const string PlayerPrefix = "org.mpris.MediaPlayer2.";
    private const string OwnName = "org.mpris.MediaPlayer2.WiimControl";
    private const string PlayerPath = "/org/mpris/MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const long MaxArtBytes = 8 * 1024 * 1024;
    private Connection? _connection;
    private string? _shown;
    private string _shownTrackId = string.Empty;
    private int _shownPositionMs;
    private DateTime _shownPositionAt;
    private bool _shownPlaying;
    private string _artKey = string.Empty;
    private byte[]? _art;

    public async Task<ComputerTrack?> ReadAsync()
    {
        if (await ConnectAsync() is not { } connection) return null;
        try
        {
            var names = await connection.CallMethodAsync(ListNamesMessage(connection),
                (Message m, object? _) => m.GetBodyReader().ReadArrayOfString(), null);
            var players = new List<(string Name, string Status, Dictionary<string, VariantValue> Properties)>();
            foreach (string name in names.Where(IsOtherPlayer).Order(StringComparer.Ordinal))
            {
                try
                {
                    var properties = await connection.CallMethodAsync(GetAllMessage(connection, name),
                        (Message m, object? _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue(), null);
                    players.Add((name, Text(properties, "PlaybackStatus"), properties));
                }
                catch (Exception) { }
            }
            var chosen = players.FirstOrDefault(p => p.Status == "Playing" && TrackOf(p.Name, p.Properties, true) != null);
            if (chosen.Name == null && _shown != null) chosen = players.FirstOrDefault(p => p.Name == _shown && p.Status == "Paused");
            _shown = chosen.Name;
            if (chosen.Name == null) return null;
            var track = TrackOf(chosen.Name, chosen.Properties, chosen.Status == "Playing");
            _shownTrackId = TrackIdOf(chosen.Properties);
            if (track != null) (_shownPositionMs, _shownPositionAt, _shownPlaying) = (track.PositionMs, track.PositionAt, track.Playing);
            return await WithArtAsync(track);
        }
        catch (Exception)
        {
            Disconnect();
            return null;
        }
    }

    public async Task<bool> ControlAsync(string action)
    {
        if (_shown is not { } name || await ConnectAsync() is not { } connection) return false;
        string member = action switch { "next" => "Next", "prev" => "Previous", _ => "PlayPause" };
        try
        {
            await connection.CallMethodAsync(PlayerCommandMessage(connection, name, member));
            return true;
        }
        catch (Exception) { return false; }
    }

    public async Task<bool> SeekAsync(int positionMs)
    {
        if (_shown is not { } name || await ConnectAsync() is not { } connection) return false;
        long target = Math.Max(0, positionMs) * 1000L;
        try
        {
            if (_shownTrackId.Length > 0 && _shownTrackId != "/org/mpris/MediaPlayer2/TrackList/NoTrack")
                await connection.CallMethodAsync(SetPositionMessage(connection, name, _shownTrackId, target));
            else
            {
                long now = (_shownPositionMs + (_shownPlaying ? (long)(DateTime.UtcNow - _shownPositionAt).TotalMilliseconds : 0)) * 1000L;
                await connection.CallMethodAsync(SeekMessage(connection, name, target - now));
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    private static string TrackIdOf(Dictionary<string, VariantValue> properties)
    {
        if (!properties.TryGetValue("Metadata", out var metadataValue)) return string.Empty;
        var metadata = Plain(metadataValue);
        return metadata.Type == VariantValueType.Dictionary ? Text(metadata.GetDictionary<string, VariantValue>(), "mpris:trackid") : string.Empty;
    }

    private async Task<Connection?> ConnectAsync()
    {
        if (_connection != null) return _connection;
        if (string.IsNullOrEmpty(Address.Session)) return null;
        var connection = new Connection(Address.Session!);
        try
        {
            await connection.ConnectAsync();
            _connection = connection;
            return connection;
        }
        catch (Exception)
        {
            connection.Dispose();
            return null;
        }
    }

    private void Disconnect()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private static ComputerTrack? TrackOf(string name, Dictionary<string, VariantValue> properties, bool playing)
    {
        if (!properties.TryGetValue("Metadata", out var metadataValue)) return null;
        var metadata = Plain(metadataValue);
        if (metadata.Type != VariantValueType.Dictionary) return null;
        var fields = metadata.GetDictionary<string, VariantValue>();
        string title = Text(fields, "xesam:title");
        if (title.Length == 0) return null;
        string artist = Strings(fields, "xesam:artist") is { Length: > 0 } artists ? artists : Strings(fields, "xesam:albumArtist");
        string album = Text(fields, "xesam:album");
        int duration = (int)Math.Clamp(Number(fields, "mpris:length") / 1000, 0, int.MaxValue);
        int position = duration > 0 ? (int)Math.Clamp(Number(properties, "Position") / 1000, 0, duration) : 0;
        string artUrl = Text(fields, "mpris:artUrl");
        return new ComputerTrack($"{name}\n{title}\n{artist}\n{album}\n{artUrl}", title, artist, album, playing, position, duration,
            DateTime.UtcNow, artUrl, null);
    }

    private async Task<ComputerTrack?> WithArtAsync(ComputerTrack? track)
    {
        if (track == null) return null;
        if (track.Key != _artKey)
        {
            _artKey = track.Key;
            _art = await ReadLocalArtAsync(track.ArtUrl);
        }
        bool web = track.ArtUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   track.ArtUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        return track with { ArtUrl = web ? track.ArtUrl : string.Empty, Art = _art };
    }

    private static bool IsOtherPlayer(string name) =>
        name.StartsWith(PlayerPrefix, StringComparison.Ordinal) && name != OwnName && !name.StartsWith(OwnName + ".", StringComparison.Ordinal);

    private static async Task<byte[]?> ReadLocalArtAsync(string url)
    {
        if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            var file = new FileInfo(uri.LocalPath);
            return file.Exists && file.Length is > 0 and <= MaxArtBytes ? await File.ReadAllBytesAsync(file.FullName) : null;
        }
        catch (Exception) { return null; }
    }

    private static VariantValue Plain(VariantValue value) => value.Type == VariantValueType.Variant ? Plain(value.GetVariantValue()) : value;

    private static string Text(Dictionary<string, VariantValue> values, string key) =>
        values.TryGetValue(key, out var value) && Plain(value) is { Type: VariantValueType.String or VariantValueType.ObjectPath } text
            ? text.Type == VariantValueType.String ? text.GetString() : text.GetObjectPathAsString()
            : string.Empty;

    private static string Strings(Dictionary<string, VariantValue> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return string.Empty;
        var plain = Plain(value);
        if (plain.Type == VariantValueType.String) return plain.GetString();
        if (plain.Type != VariantValueType.Array || plain.ItemType != VariantValueType.String) return string.Empty;
        return string.Join(", ", plain.GetArray<string>().Where(s => s.Length > 0));
    }

    private static long Number(Dictionary<string, VariantValue> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return 0;
        var plain = Plain(value);
        return plain.Type switch
        {
            VariantValueType.Int64 => plain.GetInt64(),
            VariantValueType.UInt64 => (long)Math.Min(plain.GetUInt64(), long.MaxValue),
            VariantValueType.Int32 => plain.GetInt32(),
            VariantValueType.UInt32 => plain.GetUInt32(),
            VariantValueType.Double => (long)plain.GetDouble(),
            _ => 0
        };
    }

    private static MessageBuffer ListNamesMessage(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, MessageFlags.None);
        return writer.CreateMessage();
    }

    private static MessageBuffer GetAllMessage(Connection connection, string destination)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, PlayerPath, "org.freedesktop.DBus.Properties", "GetAll", "s", MessageFlags.None);
        writer.WriteString(PlayerInterface);
        return writer.CreateMessage();
    }

    private static MessageBuffer PlayerCommandMessage(Connection connection, string destination, string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, PlayerPath, PlayerInterface, member, null, MessageFlags.None);
        return writer.CreateMessage();
    }

    private static MessageBuffer SetPositionMessage(Connection connection, string destination, string trackId, long positionUs)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, PlayerPath, PlayerInterface, "SetPosition", "ox", MessageFlags.None);
        writer.WriteObjectPath(trackId);
        writer.WriteInt64(positionUs);
        return writer.CreateMessage();
    }

    private static MessageBuffer SeekMessage(Connection connection, string destination, long offsetUs)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, PlayerPath, PlayerInterface, "Seek", "x", MessageFlags.None);
        writer.WriteInt64(offsetUs);
        return writer.CreateMessage();
    }

    public void Dispose()
    {
        Disconnect();
        _shown = null;
    }
}
