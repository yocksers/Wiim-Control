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
    private HashSet<string> _disabledOutputIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _legacyEnabledOutputIds;
    private string          _deviceIp = string.Empty;
    private string          _deviceUuid = string.Empty;
    private List<KnownDevice> _knownDevices = [];
    private SettingsWindow?  _settingsWindow;
    private bool             _openingSettings;
    private bool             _startMinimized;
    private bool             _nowPlayingVinyl;
    private int              _consecutiveFailures;
    private DateTime         _lastReconnectAttempt = DateTime.MinValue;
    private bool             _logStep;
    private bool             _forwardMediaKeys;
    private bool             _groupVolumeKeys;
    private string           _groupLeaderIp = string.Empty;
    private Avalonia.Size?   _windowSize;
    private bool             _windowMaximized;
    private bool             _sidebarHidden;
    private int              _uiScalePercent = 100;
    private bool             _linuxPortalShortcuts;
    private bool            _suppress    = true;
    private bool            _volumeKeys  = true;
    private int             _volumeStep  = 5;
    private DateTime        _lastCmd     = DateTime.MinValue;
    private OsdCorner       _osdCorner       = OsdCorner.BottomRight;
    private int             _osdDurationMs   = 1500;
    private int             _osdScalePercent = 100;
    private VolumeOsdWindow? _osd;
    private int?            _cachedVolume;
    private string          _cachedIp = string.Empty;
    private readonly SemaphoreSlim _volumeGate = new(1, 1);
    private bool?           _cachedMuted;
    private DateTime        _cacheSyncUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    public WiimController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _proc = HookCallback;
        _http = CreateHttpClient();
    }

    internal double UiScale => _uiScalePercent / 100.0;

    public async Task StartAsync()
    {
        LoadConfig();
        ThemeEngine.Apply(_activeTheme);
        ErrorLog.Cleanup();
        DeleteOldInstallers();
        AutoStart.MigrateLegacy();

        if (_knownDevices.Count == 0 && !string.IsNullOrWhiteSpace(_deviceIp))
            _knownDevices.Add(new KnownDevice("WiiM device", _deviceIp, _deviceUuid));

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
            MigrateEnabledOutputs();
            try { _outputMonitor = new OutputDeviceMonitor(_disabledOutputIds); }
            catch (Exception) { _outputMonitor = null; }
        }

        BuildTrayIcon();
        StartNowPlayingUpdates();
        StartInstantUpdates();
        UpdateMediaSession();
        StartFirmwareChecks();
        StartUpdateChecks();
        CommandChannel.StartServer(cmd => Dispatcher.UIThread.Post(() => HandleCommand(cmd)), _shutdown.Token);

        if (OperatingSystem.IsWindows())
        {
            _shellWnd = new ShellHookWindow(HandleKeyCommand, OnHotkeyPressed, _forwardMediaKeys,
                () => Dispatcher.UIThread.Post(Exit));
            _hookID = InstallHook();

            if (_outputMonitor != null)
            {
                _outputMonitor.DefaultDeviceChanged += () => Dispatcher.UIThread.Post(OnDefaultOutputChanged);
                _outputMonitor.StatusChanged += _ => Dispatcher.UIThread.Post(() =>
                {
                    UpdateTrayStatus();
                    UpdateVolumeKeyRegistration();
                });
                UpdateVolumeKeyRegistration();
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            _linuxShortcuts = new LinuxShortcuts(cmd => Dispatcher.UIThread.Post(() => HandleMediaKey(cmd)));
            if (_linuxPortalShortcuts) _ = _linuxShortcuts.RegisterAsync(_forwardMediaKeys);
        }
        else if (OperatingSystem.IsMacOS() &&
                 Avalonia.Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) Dispatcher.UIThread.Post(ShowSettingsWindow);
            };
        }

        StartHotkeys();
        _ = FillMissingDeviceIdsAsync();
        if (_roonTokens.Count > 0) _ = GetRoonAsync();
        if (!_startMinimized && _lifetime.Args?.Contains(AutoStart.MinimizedArgument) != true) ShowSettingsWindow();
    }

    private void UpdateVolumeKeyRegistration()
    {
        if (OperatingSystem.IsWindows())
            (_shellWnd as ShellHookWindow)?.SetVolumeKeysEnabled(_volumeKeys && _suppress && (_outputMonitor?.IsEnabledOutputActive ?? true) &&
                !IsBluetoothInput(_deviceIp));
    }

    private void HandleKeyCommand(string cmd)
    {
        if (_outputMonitor != null && !_outputMonitor.IsEnabledOutputActive) return;
        if (!_volumeKeys && cmd is "vol++" or "vol--" or "mute") return;
        if (cmd is "vol++" or "vol--" or "mute" && IsBluetoothInput(_deviceIp)) return;
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
        if (cmd is "vol++" or "vol--" or "mute" && IsBluetoothFromThisComputer(_deviceIp))
        {
            _ = ChangeComputerVolumeAsync(cmd);
            return;
        }
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
        CloseMiniPlayers();
        StopPcAudio();
        try { StopInstantUpdatesAsync().Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
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
        _trackToast?.Close();
        StopPcAudio();
        _nowPlayingTimer.Stop();
        _firmwareTimer.Stop();
        _updateTimer.Stop();
        _mediaSession?.Dispose();
        _http.Dispose();
        _webHttp.Dispose();
        _outputMonitor?.Dispose();
        DisposeArtworkCaches();
    }
}
