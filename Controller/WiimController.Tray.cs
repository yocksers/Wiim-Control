using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly int[] StepChoices = [1, 2, 3, 4, 5, 10];
    private static readonly int[] DurationChoices = [750, 1000, 1500, 2000, 3000, 5000];

    private NativeMenuItem? _mnuDeviceStatus;
    private NativeMenuItem? _mnuOutputStatus;
    private NativeMenuItem? _mnuSuppress;
    private NativeMenuItem? _mnuAutoStart;
    private NativeMenuItem? _mnuForwardMedia;
    private NativeMenuItem? _mnuLogStep;
    private NativeMenuItem? _mnuStep;
    private NativeMenu? _mnuPresets;
    private NativeMenu? _mnuDevices;
    private NativeMenu? _mnuOutputs;
    private readonly Dictionary<int, NativeMenuItem> _stepItems = new();
    private readonly Dictionary<OsdCorner, NativeMenuItem> _cornerItems = new();
    private readonly Dictionary<int, NativeMenuItem> _durationItems = new();

    private static NativeMenuItem Disabled(string header) => new(header) { IsEnabled = false };

    private static NativeMenuItem Check(string header, bool value, Action<bool> changed)
    {
        var item = new NativeMenuItem(header) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = value };
        item.Click += (_, _) =>
        {
            item.IsChecked = !item.IsChecked;
            changed(item.IsChecked);
        };
        return item;
    }

    private static NativeMenuItem Radio(string header, bool value, Action clicked)
    {
        var item = new NativeMenuItem(header) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = value };
        item.Click += (_, _) => clicked();
        return item;
    }

    private static NativeMenuItem MenuAction(string header, Action clicked)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => clicked();
        return item;
    }

    private static NativeMenuItem Submenu(string header, NativeMenu menu) => new(header) { Menu = menu };

    private void BuildTrayIcon()
    {
        var menu = new NativeMenu();

        _mnuDeviceStatus = Disabled(TrayText());
        menu.Add(_mnuDeviceStatus);
        if (_outputMonitor != null)
        {
            _mnuOutputStatus = Disabled(OutputStatusText());
            menu.Add(_mnuOutputStatus);
        }
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuAction("Settings…", ShowSettingsWindow));
        menu.Add(new NativeMenuItemSeparator());

        if (OperatingSystem.IsWindows())
        {
            _mnuSuppress = Check("Suppress Windows volume OSD", _suppress, SetSuppress);
            menu.Add(_mnuSuppress);
        }
        _mnuAutoStart = Check(AutoStart.Label, IsAutoStartEnabled(), SetAutoStartSetting);
        menu.Add(_mnuAutoStart);
        if (OperatingSystem.IsWindows())
        {
            _mnuForwardMedia = Check("Forward media keys (play/pause/next/prev) to Wiim", _forwardMediaKeys, SetForwardMediaKeys);
            menu.Add(_mnuForwardMedia);
        }

        if (_outputMonitor != null)
        {
            _mnuOutputs = new NativeMenu();
            _mnuOutputs.Opening += (_, _) => BuildOutputDevicesMenu();
            BuildOutputDevicesMenu();
            menu.Add(Submenu("Output devices", _mnuOutputs));
        }

        var stepMenu = new NativeMenu();
        foreach (int step in StepChoices)
        {
            int s = step;
            var item = Radio(s.ToString(), _volumeStep == s, () => SetVolumeStep(s));
            _stepItems[s] = item;
            stepMenu.Add(item);
        }
        stepMenu.Add(new NativeMenuItemSeparator());
        stepMenu.Add(MenuAction("Custom…", async () =>
        {
            var raw = await Dialogs.PromptAsync(null, $"Current step: {_volumeStep}\nEnter new step (1–50):", string.Empty, UiScale);
            if (int.TryParse(raw, out int v) && v is >= 1 and <= 50) SetVolumeStep(v);
        }));
        stepMenu.Add(new NativeMenuItemSeparator());
        _mnuLogStep = Check("Logarithmic step (finer at low volume)", _logStep, SetLogStep);
        stepMenu.Add(_mnuLogStep);
        _mnuStep = Submenu($"Volume step: {_volumeStep}", stepMenu);
        menu.Add(_mnuStep);

        var positionMenu = new NativeMenu();
        foreach (var corner in Enum.GetValues<OsdCorner>())
        {
            var c = corner;
            var item = Radio(OsdCornerLabel(c), _osdCorner == c, () => SetOsdCorner(c));
            _cornerItems[c] = item;
            positionMenu.Add(item);
        }
        var durationMenu = new NativeMenu();
        foreach (int ms in DurationChoices)
        {
            int d = ms;
            var item = Radio(OsdDurationLabel(d), _osdDurationMs == d, () => SetOsdDuration(d));
            _durationItems[d] = item;
            durationMenu.Add(item);
        }
        var overlayMenu = new NativeMenu();
        overlayMenu.Add(Submenu("Position", positionMenu));
        overlayMenu.Add(Submenu("Duration", durationMenu));
        menu.Add(Submenu("Volume overlay", overlayMenu));

        _mnuPresets = new NativeMenu();
        for (int preset = 1; preset <= 12; preset++)
        {
            int p = preset;
            _mnuPresets.Add(MenuAction($"Preset {p}", () => _ = PlayPresetAsync(p)));
        }
        _mnuPresets.Opening += async (_, _) => await UpdatePresetNamesAsync();
        menu.Add(Submenu("Presets", _mnuPresets));

        menu.Add(MenuAction("Test connection…", async () =>
        {
            try
            {
                var resp = await _http.GetAsync($"https://{_deviceIp}/httpapi.asp?command=getPlayerStatus");
                Notify($"Connected — HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex) { Notify($"Failed: {ex.Message}"); }
        }));

        _mnuDevices = new NativeMenu();
        _mnuDevices.Opening += (_, _) => BuildDevicesMenu();
        BuildDevicesMenu();
        menu.Add(Submenu("Wiim Devices", _mnuDevices));
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Disabled($"Version {AppVersion}"));
        menu.Add(MenuAction("Exit", Exit));

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri(
                $"avares://WiimControl/Assets/{(OperatingSystem.IsWindows() ? "logo.ico" : "tray.png")}"))),
            ToolTipText = TrayTooltip(),
            Menu = menu,
            IsVisible = true
        };
        _tray.Clicked += (_, _) => ShowSettingsWindow();
        if (Application.Current is { } app) TrayIcon.SetIcons(app, [_tray]);
        _ = UpdatePresetNamesAsync();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private static readonly string AppVersion =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0").Split('+')[0];

    private string TrayText() => $"Wiim Control v{AppVersion} [{_deviceIp}]{(_outputMonitor is { IsEnabledOutputActive: false } ? " (inactive output)" : "")}";

    private static string OsdCornerLabel(OsdCorner c) => c switch
    {
        OsdCorner.TopLeft     => "Top-left",
        OsdCorner.TopRight    => "Top-right",
        OsdCorner.BottomLeft  => "Bottom-left",
        OsdCorner.BottomRight => "Bottom-right",
        _                     => c.ToString()
    };

    private static string OsdDurationLabel(int ms) => ms % 1000 == 0 ? $"{ms / 1000}s" : $"{ms / 1000.0:0.0}s";

    private string OutputStatusText() =>
        _outputMonitor is null               ? "Output: monitor unavailable"                        :
        _enabledOutputIds.Count == 0         ? "Output: all devices allowed"                        :
        _outputMonitor.IsEnabledOutputActive ? "Output: active for current device"                  :
                                                "Output: inactive — current device not selected";

    private void UpdateTrayStatus()
    {
        if (_tray != null) _tray.ToolTipText = TrayTooltip();
        if (_mnuDeviceStatus != null) _mnuDeviceStatus.Header = TrayText();
        if (_mnuOutputStatus != null) _mnuOutputStatus.Header = OutputStatusText();
    }

    internal void RefreshDeviceStatusUi()
    {
        UpdateTrayStatus();
        BuildDevicesMenu();
    }

    private string TrayTooltip()
    {
        string text = TrayText();
        if (_nowPlayingText.Length > 0) text += "\n♪ " + _nowPlayingText;
        return text.Length <= 127 ? text : text[..126] + "…";
    }

    private async Task UpdatePresetNamesAsync()
    {
        if (_mnuPresets == null) return;
        var names = ParsePresetNames(await SendToDeviceAsync(_deviceIp, "getPresetInfo"));
        var items = _mnuPresets.Items.OfType<NativeMenuItem>().ToList();
        for (int i = 0; i < items.Count; i++)
            items[i].Header = names.TryGetValue(i + 1, out var name) ? $"{i + 1}. {name}" : $"Preset {i + 1}";
    }

    private void BuildDevicesMenu()
    {
        if (_mnuDevices == null) return;
        _mnuDevices.Items.Clear();

        foreach (var dev in _knownDevices)
        {
            var d = dev;
            _mnuDevices.Add(Radio($"{d.Name} ({d.Ip})", d.Ip == _deviceIp, () => SwitchDevice(d)));
        }

        if (_knownDevices.Count > 0) _mnuDevices.Add(new NativeMenuItemSeparator());

        _mnuDevices.Add(MenuAction("Discover devices…", async () =>
        {
            int added = await DiscoverAndMergeDevicesAsync();
            Notify(added > 0 ? $"Found {added} new Wiim device(s)" : "No new Wiim devices found");
            BuildDevicesMenu();
        }));
        _mnuDevices.Add(MenuAction("Add device manually…", async () =>
        {
            var ip = await Dialogs.PromptAsync(null, "Enter the Wiim Amp's IP address:", string.Empty, UiScale);
            if (!string.IsNullOrWhiteSpace(ip)) AddManualDevice(ip);
        }));

        if (_knownDevices.Count > 1)
        {
            _mnuDevices.Add(MenuAction("Remove current device", () =>
            {
                var current = _knownDevices.FirstOrDefault(k => k.Ip == _deviceIp);
                if (current.Ip != null) RemoveDevice(current);
            }));
        }
    }

    private void BuildOutputDevicesMenu()
    {
        if (_mnuOutputs == null) return;
        _mnuOutputs.Items.Clear();
        _mnuOutputs.Add(Disabled("Check outputs the Wiim should take over:"));
        _mnuOutputs.Add(Disabled("(none checked = allow all outputs)"));
        _mnuOutputs.Add(new NativeMenuItemSeparator());

        var devices = ActiveOutputDevices();
        if (devices.Count == 0)
        {
            _mnuOutputs.Add(Disabled("No playback devices found"));
            return;
        }

        foreach (var (id, name) in devices)
        {
            string deviceId = id;
            _mnuOutputs.Add(Check(name, _enabledOutputIds.Contains(id), on => SetOutputDeviceEnabled(deviceId, on)));
        }

        _mnuOutputs.Add(new NativeMenuItemSeparator());
        _mnuOutputs.Add(MenuAction("Allow all outputs (clear selection)", () =>
        {
            ClearOutputDevices();
            BuildOutputDevicesMenu();
        }));
    }

    private void Notify(string message) => Dispatcher.UIThread.Post(() =>
    {
        _osd ??= new VolumeOsdWindow();
        _osd.ShowMessage(message, _osdCorner, 3000, _osdScalePercent / 100.0);
    });

    private void ShowVolumeOsd(int volume, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        _osd ??= new VolumeOsdWindow();
        _osd.ShowVolume(volume, muted, _osdCorner, _osdDurationMs, _nowPlayingText, _osdScalePercent / 100.0);
    });

    private void SyncTrayCheckboxes()
    {
        if (_mnuSuppress     != null) _mnuSuppress.IsChecked     = _suppress;
        if (_mnuAutoStart    != null) _mnuAutoStart.IsChecked    = IsAutoStartEnabled();
        if (_mnuForwardMedia != null) _mnuForwardMedia.IsChecked = _forwardMediaKeys;
        if (_mnuLogStep      != null) _mnuLogStep.IsChecked      = _logStep;
        if (_mnuStep         != null) _mnuStep.Header            = $"Volume step: {_volumeStep}";
        foreach (var (step, item) in _stepItems) item.IsChecked = step == _volumeStep;
        foreach (var (corner, item) in _cornerItems) item.IsChecked = corner == _osdCorner;
        foreach (var (ms, item) in _durationItems) item.IsChecked = ms == _osdDurationMs;
    }
}
