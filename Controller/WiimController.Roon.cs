using System.Text.Json;

namespace WiimControl;

enum RoonState { NotFound, Connecting, WaitingForApproval, Connected }

sealed partial class WiimController
{
    private RoonConnection? _roon;
    private Task<RoonConnection?>? _roonConnecting;
    private int _roonGeneration;
    private string _roonCore = string.Empty;
    private readonly Dictionary<string, string> _roonTokens = new();
    internal RoonState RoonStatus { get; private set; } = RoonState.NotFound;
    internal event Action? RoonStatusChanged;

    private Dictionary<string, object?> RoonRegistration => new()
    {
        ["extension_id"] = "com.yocksers.wiimcontrol",
        ["display_name"] = "Wiim Control",
        ["display_version"] = AppVersion,
        ["publisher"] = "yocksers",
        ["email"] = "",
        ["website"] = "https://github.com/yocksers/Wiim-Control",
        ["required_services"] = new[] { "com.roonlabs.transport:2", "com.roonlabs.browse:1" },
        ["optional_services"] = new[] { "com.roonlabs.image:1" },
        ["provided_services"] = new[] { "com.roonlabs.ping:1" }
    };

    private void ParseRoonTokens(string value)
    {
        _roonTokens.Clear();
        foreach (var pair in value.Split('\u0002', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('\u0001') is [var core, var token]) _roonTokens[core] = token;
    }

    private string SerializeRoonTokens() => string.Join('\u0002', _roonTokens.Select(p => $"{p.Key}\u0001{p.Value}"));

    private void SetRoonStatus(RoonState state)
    {
        if (RoonStatus == state) return;
        RoonStatus = state;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RoonStatusChanged?.Invoke());
    }

    internal void UseRoonCore(string hostAndPort)
    {
        if (hostAndPort == _roonCore || hostAndPort.Length == 0) return;
        RoonConnection? old;
        lock (_roonLock)
        {
            _roonCore = hostAndPort;
            _roonGeneration++;
            _roonConnecting = null;
            old = _roon;
            _roon = null;
        }
        old?.Dispose();
        SaveConfig();
    }

    internal string RoonCoreName => _roon?.CoreName is { Length: > 0 } name ? name : "Roon";

    private readonly object _roonLock = new();

    internal Task<RoonConnection?> GetRoonAsync()
    {
        lock (_roonLock)
        {
            if (_roon is { IsOpen: true } open) return Task.FromResult<RoonConnection?>(open);
            return _roonConnecting ??= ConnectRoonAsync(_roonGeneration, _roonCore);
        }
    }

    private async Task<RoonConnection?> ConnectRoonAsync(int generation, string savedCore)
    {
        try
        {
            SetRoonStatus(RoonState.Connecting);
            RoonConnection? connection = null;
            if (savedCore.Split(':') is [var savedHost, var savedPort] && int.TryParse(savedPort, out int port))
                connection = await OpenRoonAsync(savedHost, port).ConfigureAwait(false);
            if (connection == null)
            {
                var cores = await RoonConnection.DiscoverAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                foreach (var core in cores)
                {
                    connection = await OpenRoonAsync(core.Host, core.Port).ConfigureAwait(false);
                    if (connection != null) break;
                }
            }
            bool current;
            lock (_roonLock) current = generation == _roonGeneration;
            if (!current)
            {
                connection?.Dispose();
                return await GetRoonAsync().ConfigureAwait(false);
            }
            if (connection == null)
            {
                SetRoonStatus(RoonState.NotFound);
                return null;
            }
            bool coreChanged;
            lock (_roonLock)
            {
                _roon = connection;
                coreChanged = _roonCore != $"{connection.Host}:{connection.Port}";
                if (coreChanged) _roonCore = $"{connection.Host}:{connection.Port}";
            }
            if (coreChanged) Avalonia.Threading.Dispatcher.UIThread.Post(SaveConfig);
            connection.Closed += () =>
            {
                if (_roon == connection) _roon = null;
                SetRoonStatus(RoonState.NotFound);
            };
            SetRoonStatus(connection.IsRegistered ? RoonState.Connected : RoonState.WaitingForApproval);
            _ = connection.Registered.ContinueWith(t => SetRoonStatus(t.Result ? RoonState.Connected : RoonState.NotFound), TaskScheduler.Default);
            return connection;
        }
        finally
        {
            lock (_roonLock)
                if (generation == _roonGeneration) _roonConnecting = null;
        }
    }

    private Task<RoonConnection?> OpenRoonAsync(string host, int port) =>
        RoonConnection.ConnectAsync(host, port, RoonRegistration, core => _roonTokens.GetValueOrDefault(core), (core, token) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _roonTokens[core] = token;
                SaveConfig();
            }));

    internal async Task<RoonConnection?> GetRegisteredRoonAsync(TimeSpan wait)
    {
        var roon = await GetRoonAsync().ConfigureAwait(false);
        if (roon == null) return null;
        if (roon.IsRegistered) return roon;
        var done = await Task.WhenAny(roon.Registered, Task.Delay(wait)).ConfigureAwait(false);
        return done == roon.Registered && roon.Registered.Result ? roon : null;
    }

    internal static JsonElement Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}
