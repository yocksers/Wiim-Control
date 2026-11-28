using System.Diagnostics;
using Avalonia.Controls;
using Tmds.DBus.Protocol;

namespace WiimControl;

sealed class OnboardKeyboard : ITouchKeyboard
{
    private const string Service = "org.onboard.Onboard";
    private const string KeyboardPath = "/org/onboard/Onboard/Keyboard";
    private const string KeyboardInterface = "org.onboard.Onboard.Keyboard";
    private static readonly TimeSpan StartPause = TimeSpan.FromSeconds(5);
    private Connection? _connection;
    private DateTime _startedAt = DateTime.MinValue;

    public static string? ProgramPath
    {
        get
        {
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                string path = Path.Combine(folder, "onboard");
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }

    public void Show(TopLevel top) => _ = CallAsync("Show", start: true);

    public void Hide(TopLevel top) => _ = CallAsync("Hide", start: false);

    private async Task CallAsync(string member, bool start)
    {
        try
        {
            if (await ConnectAsync() is not { } connection) return;
            if (!await RunningAsync(connection))
            {
                if (start) Start();
                return;
            }
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(Service, KeyboardPath, KeyboardInterface, member, null, MessageFlags.NoReplyExpected);
                connection.TrySendMessage(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        catch (Exception ex)
        {
            _connection?.Dispose();
            _connection = null;
            ErrorLog.Write("Onboard keyboard", ex);
        }
    }

    private async Task<Connection?> ConnectAsync()
    {
        if (_connection != null) return _connection;
        if (string.IsNullOrEmpty(Address.Session)) return null;
        var connection = new Connection(Address.Session);
        await connection.ConnectAsync();
        _connection = connection;
        return connection;
    }

    private static async Task<bool> RunningAsync(Connection connection)
    {
        MessageBuffer message;
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s", MessageFlags.None);
            writer.WriteString(Service);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
        return await connection.CallMethodAsync(message, (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
    }

    private void Start()
    {
        if (DateTime.UtcNow - _startedAt < StartPause || ProgramPath is not { } path) return;
        _startedAt = DateTime.UtcNow;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) { ErrorLog.Write("Onboard keyboard", ex); }
    }
}
