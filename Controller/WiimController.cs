using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly LowLevelKeyboardProc _proc;
    private readonly CancellationTokenSource _shutdown = new();
    private IntPtr          _hookID   = IntPtr.Zero;
    private readonly HttpClient _http;
    private TrayIcon?       _tray;
    private IDisposable?    _shellWnd;
    private IOutputDeviceMonitor? _outputMonitor;
    private LinuxShortcuts? _linuxShortcuts;
    private HashSet<string> _enabledOutputIds = new(StringComparer.OrdinalIgnoreCase);
    private string          _deviceIp = string.Empty;
    private string          _deviceUuid = string.Empty;
    private List<KnownDevice> _knownDevices = [];
    private SettingsWindow?  _settingsWindow;
    private int              _consecutiveFailures;
    private DateTime         _lastReconnectAttempt = DateTime.MinValue;
    private bool             _logStep;
    private bool             _forwardMediaKeys;
    private bool             _groupVolumeKeys;
    private string           _groupLeaderIp = string.Empty;
    private Avalonia.Size?   _windowSize;
    private bool             _windowMaximized;
    private int              _uiScalePercent = 100;
    private bool             _linuxPortalShortcuts;
    private bool            _suppress    = true;
    private int             _volumeStep  = 5;
    private DateTime        _lastCmd     = DateTime.MinValue;
    private OsdCorner       _osdCorner       = OsdCorner.BottomRight;
    private int             _osdDurationMs   = 1500;
    private int             _osdScalePercent = 100;
    private VolumeOsdWindow? _osd;
    private int?            _cachedVolume;
    private bool?           _cachedMuted;
    private DateTime        _cacheSyncUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    public WiimController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _proc = HookCallback;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    internal double UiScale => _uiScalePercent / 100.0;

    public async Task StartAsync()
    {
        LoadConfig();
        AutoStart.MigrateLegacy();

        if (_knownDevices.Count == 0 && !string.IsNullOrWhiteSpace(_deviceIp))
            _knownDevices.Add(new KnownDevice("Wiim Amp", _deviceIp, _deviceUuid));

        if (string.IsNullOrWhiteSpace(_deviceIp))
        {
            var picked = await PickOrEnterDeviceAsync(await TryDiscoverDevicesAsync());
            if (picked is not { } device)
            {
                _lifetime.Shutdown();
                return;
            }
            _deviceIp   = device.Ip;
            _deviceUuid = device.Uuid;
            _knownDevices.Add(device);
            SaveConfig();
        }

        if (OperatingSystem.IsWindows())
        {
            try { _outputMonitor = new OutputDeviceMonitor(_enabledOutputIds); }
            catch (Exception) { _outputMonitor = null; }
        }

        BuildTrayIcon();
        StartNowPlayingUpdates();
        _ = RefreshActiveMaxVolumeAsync();
        CommandChannel.StartServer(cmd => Dispatcher.UIThread.Post(() => HandleCommand(cmd)), _shutdown.Token);

        if (OperatingSystem.IsWindows())
        {
            _shellWnd = new ShellHookWindow(HandleKeyCommand, OnHotkeyPressed, _forwardMediaKeys);
            _hookID = InstallHook();

            if (_outputMonitor != null)
            {
                _outputMonitor.StatusChanged += _ => Dispatcher.UIThread.Post(UpdateTrayStatus);
                _outputMonitor.ExternalVolumeStepDetected += dir =>
                {
                    if (Debounce()) _ = ChangeVolumeAsync(dir);
                };
                _outputMonitor.ExternalMuteToggleDetected += () =>
                {
                    if (Debounce()) _ = ToggleMuteAsync();
                };
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            _linuxShortcuts = new LinuxShortcuts(cmd => Dispatcher.UIThread.Post(() => HandleCommand(cmd)));
            if (_linuxPortalShortcuts) _ = _linuxShortcuts.RegisterAsync(_forwardMediaKeys);
        }

        StartHotkeys();
        _ = FillMissingDeviceIdsAsync();
    }

    private void HandleKeyCommand(string cmd)
    {
        if (_outputMonitor != null && !_outputMonitor.IsCurrentDeviceEnabled()) return;
        if (!Debounce()) return;
        RunPlayerCommand(cmd);
    }

    internal void HandleCommand(string cmd)
    {
        if (cmd == "show")
        {
            ShowSettingsWindow();
            return;
        }
        RunPlayerCommand(cmd);
    }

    private void RunPlayerCommand(string cmd)
    {
        if (cmd is "vol++" or "vol--")
            _ = ChangeVolumeAsync(cmd == "vol++" ? 1 : -1);
        else if (cmd == "mute")
            _ = ToggleMuteAsync();
        else if (cmd is "playpause" or "next" or "prev" or "stop")
            _ = SendMediaCommandAsync(cmd);
    }

    internal void Exit()
    {
        _settingsWindow?.Close();
        _lifetime.Shutdown();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        if (_hookID != IntPtr.Zero) UnhookWindowsHookEx(_hookID);
        if (!ReferenceEquals(_globalHotkeys, _shellWnd)) _globalHotkeys?.Dispose();
        _shellWnd?.Dispose();
        _linuxShortcuts?.Dispose();
        _tray?.Dispose();
        _osd?.Close();
        _nowPlayingTimer.Stop();
        _http.Dispose();
        _outputMonitor?.Dispose();
    }
}
