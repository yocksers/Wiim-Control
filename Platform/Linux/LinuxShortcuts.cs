using Tmds.DBus.Protocol;

namespace WiimControl;

sealed class LinuxShortcuts : IDisposable
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";

    private delegate void WriteArguments(ref MessageWriter writer, string handleToken);

    private static readonly (string Id, string Description, string Trigger, string Command, bool Media)[] Shortcuts =
    [
        ("volume-up", "Wiim volume up", "XF86AudioRaiseVolume", "vol++", false),
        ("volume-down", "Wiim volume down", "XF86AudioLowerVolume", "vol--", false),
        ("mute", "Wiim mute", "XF86AudioMute", "mute", false),
        ("play-pause", "Wiim play/pause", "XF86AudioPlay", "playpause", true),
        ("next", "Wiim next track", "XF86AudioNext", "next", true),
        ("previous", "Wiim previous track", "XF86AudioPrev", "prev", true)
    ];

    private readonly Action<string> _onCommand;
    private Connection? _connection;
    private IDisposable? _activatedMatch;
    private string? _sessionHandle;

    public LinuxShortcuts(Action<string> onCommand) => _onCommand = onCommand;

    public string Status { get; private set; } = "Not set up";

    public event Action? StatusChanged;

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    public async Task<bool> RegisterAsync(bool includeMediaKeys = true)
    {
        Unregister();
        try
        {
            SetStatus("Waiting for the desktop to approve the shortcuts…");
            var address = Address.Session;
            if (string.IsNullOrEmpty(address))
            {
                SetStatus("Not available: no desktop session bus");
                return false;
            }
            _connection = new Connection(address);
            await _connection.ConnectAsync();

            _activatedMatch = await _connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = ShortcutsInterface, Member = "Activated", Path = PortalPath },
                (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    return (Session: reader.ReadObjectPathAsString(), Id: reader.ReadString());
                },
                (Exception? ex, (string Session, string Id) value, object? _, object? _) =>
                {
                    if (ex != null || value.Session != _sessionHandle) return;
                    var shortcut = Shortcuts.FirstOrDefault(s => s.Id == value.Id);
                    if (shortcut.Command != null) _onCommand(shortcut.Command);
                },
                null, null, false, ObserverFlags.None);

            string sessionToken = $"wiimcontrol{Environment.ProcessId}";
            var session = await CallWithResponseAsync("CreateSession", "a{sv}", (ref MessageWriter w, string handleToken) =>
            {
                var options = w.WriteDictionaryStart();
                w.WriteDictionaryEntryStart();
                w.WriteString("handle_token");
                w.WriteVariantString(handleToken);
                w.WriteDictionaryEntryStart();
                w.WriteString("session_handle_token");
                w.WriteVariantString(sessionToken);
                w.WriteDictionaryEnd(options);
            });
            if (session == null || !session.TryGetValue("session_handle", out var handleValue))
            {
                SetStatus("Not available: the desktop didn't create a shortcut session");
                return false;
            }
            _sessionHandle = handleValue.Type == VariantValueType.ObjectPath
                ? handleValue.GetObjectPathAsString()
                : handleValue.GetString();

            var wanted = Shortcuts.Where(s => includeMediaKeys || !s.Media).ToList();
            string sessionHandle = _sessionHandle;
            var bound = await CallWithResponseAsync("BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w, string handleToken) =>
            {
                w.WriteObjectPath(sessionHandle);
                var list = w.WriteArrayStart(DBusType.Struct);
                foreach (var s in wanted)
                {
                    w.WriteStructureStart();
                    w.WriteString(s.Id);
                    var properties = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("description");
                    w.WriteVariantString(s.Description);
                    w.WriteDictionaryEntryStart();
                    w.WriteString("preferred_trigger");
                    w.WriteVariantString(s.Trigger);
                    w.WriteDictionaryEnd(properties);
                }
                w.WriteArrayEnd(list);
                w.WriteString(string.Empty);
                var options = w.WriteDictionaryStart();
                w.WriteDictionaryEntryStart();
                w.WriteString("handle_token");
                w.WriteVariantString(handleToken);
                w.WriteDictionaryEnd(options);
            });
            if (bound == null)
            {
                SetStatus("Not set up: the shortcuts weren't approved");
                return false;
            }
            int count = bound.TryGetValue("shortcuts", out var shortcuts) ? shortcuts.Count : wanted.Count;
            SetStatus($"Active ({count} shortcut{(count == 1 ? "" : "s")})");
            return true;
        }
        catch (Exception ex)
        {
            bool unsupported = ex.Message.Contains("GlobalShortcuts", StringComparison.Ordinal) ||
                               ex.Message.Contains("ServiceUnknown", StringComparison.Ordinal) ||
                               ex.Message.Contains("UnknownMethod", StringComparison.Ordinal);
            SetStatus(unsupported
                ? "Your desktop doesn't support global shortcuts. Bind the keys to the commands below in your keyboard settings instead."
                : $"Not available: {ex.Message}");
            Unregister(keepStatus: true);
            return false;
        }
    }

    public void Unregister() => Unregister(keepStatus: false);

    private void Unregister(bool keepStatus)
    {
        if (_connection != null && _sessionHandle != null)
        {
            try
            {
                var writer = _connection.GetMessageWriter();
                try
                {
                    writer.WriteMethodCallHeader(PortalService, _sessionHandle, "org.freedesktop.portal.Session", "Close", null, MessageFlags.NoReplyExpected);
                    _connection.TrySendMessage(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }
            }
            catch (Exception) { }
        }
        _activatedMatch?.Dispose();
        _activatedMatch = null;
        _connection?.Dispose();
        _connection = null;
        _sessionHandle = null;
        if (!keepStatus) SetStatus("Not set up");
    }

    private static string NewToken() => "wiimcontrol" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Dictionary<string, VariantValue>?> CallWithResponseAsync(string member, string signature, WriteArguments writeArguments)
    {
        var connection = _connection ?? throw new InvalidOperationException("Not connected");
        string token = NewToken();
        string sender = (connection.UniqueName ?? string.Empty).TrimStart(':').Replace('.', '_');
        string expectedPath = $"{PortalPath}/request/{sender}/{token}";
        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listeners = new List<IDisposable>();
        try
        {
            listeners.Add(await ListenForResponseAsync(connection, expectedPath, response));

            MessageBuffer message;
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(PortalService, PortalPath, ShortcutsInterface, member, signature, MessageFlags.None);
                writeArguments(ref writer, token);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }
            string handle = await connection.CallMethodAsync(message, (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
            if (handle != expectedPath) listeners.Add(await ListenForResponseAsync(connection, handle, response));

            var finished = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (finished != response.Task) return null;
            var (code, results) = await response.Task;
            return code == 0 ? results : null;
        }
        finally
        {
            foreach (var listener in listeners) listener.Dispose();
        }
    }

    private static async Task<IDisposable> ListenForResponseAsync(Connection connection, string path,
        TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)> response) =>
        await connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = path },
            (Message m, object? _) =>
            {
                var reader = m.GetBodyReader();
                return (Code: reader.ReadUInt32(), Results: reader.ReadDictionaryOfStringToVariantValue());
            },
            (Exception? ex, (uint Code, Dictionary<string, VariantValue> Results) value, object? _, object? _) =>
            {
                if (ex != null) response.TrySetException(ex);
                else response.TrySetResult(value);
            },
            null, null, false, ObserverFlags.None);

    public void Dispose() => Unregister(keepStatus: true);
}
