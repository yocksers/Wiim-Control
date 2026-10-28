using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly int[] DurationChoices = [750, 1000, 1500, 2000, 3000, 5000];

    private NativeMenuItem? _mnuDeviceStatus;
    private NativeMenuItem? _mnuOutputStatus;
    private NativeMenuItem? _mnuSuppress;
    private NativeMenuItem? _mnuAutoStart;
    private NativeMenuItem? _mnuForwardMedia;
    private NativeMenu? _mnuPresets;
    private NativeMenu? _mnuDevices;
    private NativeMenu? _mnuInputs;
    private List<string> _trayInputs = [];
    private string _trayInputsIp = string.Empty;
    private NativeMenu? _mnuOutputs;

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
        menu.Add(MenuAction("Play / Pause", () => _ = SendMediaCommandAsync("playpause")));
        menu.Add(MenuAction("Stop", () => _ = SendMediaCommandAsync("stop")));
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

        _mnuInputs = new NativeMenu();
        _mnuInputs.Opening += (_, _) =>
        {
            BuildInputsMenu();
            _ = UpdateTrayInputsAsync();
        };
        BuildInputsMenu();
        menu.Add(Submenu("Input", _mnuInputs));
        _mnuDevices = new NativeMenu();
        _mnuDevices.Opening += (_, _) => BuildDevicesMenu();
        BuildDevicesMenu();
        menu.Add(Submenu("Wiim Devices", _mnuDevices));

        _mnuPresets = new NativeMenu();
        for (int preset = 1; preset <= 12; preset++)
        {
            int p = preset;
            _mnuPresets.Add(MenuAction($"Preset {p}", () => _ = PlayPresetAsync(p)));
        }
        _mnuPresets.Opening += async (_, _) => await UpdatePresetNamesAsync();
        menu.Add(Submenu("Presets", _mnuPresets));
        menu.Add(BuildSleepMenu());
        if (PcAudioSupported) menu.Add(BuildPcAudioMenuItem());

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
        _ = UpdateTrayInputsAsync();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        if (_openingSettings) return;
        _openingSettings = true;
        try
        {
            var window = new SettingsWindow(this);
            window.Closed += (_, _) =>
            {
                if (_settingsWindow != window) return;
                _settingsWindow = null;
                UpdateNowPlayingInterval();
            };
            _settingsWindow = window;
            window.Show();
        }
        finally
        {
            _openingSettings = false;
        }
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
        _outputMonitor is null                                                ? "Output: monitor unavailable" :
        _disabledOutputIds.Count == 0 || _outputMonitor.IsEnabledOutputActive ? $"Controlling {DeviceNameOf(_deviceIp)}" :
                                                                                $"Not controlling {DeviceNameOf(_deviceIp)} (sound output not selected)";

    private void UpdateTrayStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateTrayStatus);
            return;
        }
        if (_tray != null) _tray.ToolTipText = TrayTooltip();
        if (_mnuDeviceStatus != null) _mnuDeviceStatus.Header = TrayText();
        if (_mnuOutputStatus != null) _mnuOutputStatus.Header = OutputStatusText();
    }

    internal void RefreshDeviceStatusUi()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshDeviceStatusUi);
            return;
        }
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
        var presets = await GetPresetsAsync(_deviceIp);
        var items = _mnuPresets.Items.OfType<NativeMenuItem>().ToList();
        for (int i = 0; i < items.Count; i++)
            items[i].Header = presets?.Presets.FirstOrDefault(p => p.Number == i + 1) is { } preset
                ? $"{i + 1}. {preset.Name}"
                : $"Preset {i + 1}";
    }

    private void BuildInputsMenu()
    {
        if (_mnuInputs == null) return;
        _mnuInputs.Items.Clear();
        string ip = _deviceIp;
        if (_trayInputsIp != ip || _trayInputs.Count == 0)
        {
            _mnuInputs.Add(Disabled("Loading…"));
            return;
        }
        string? current = _lastModes.TryGetValue(ip, out var known) ? InputOfMode(known.Mode) : null;
        bool locked = IsDeviceLocked(ip);
        foreach (var input in _trayInputs)
        {
            string target = input;
            var item = Radio(InputName(ip, target), IsSameInput(target, current), () => _ = SwitchInputFromTrayAsync(ip, target));
            item.IsEnabled = !locked;
            _mnuInputs.Add(item);
        }
    }

    private async Task UpdateTrayInputsAsync()
    {
        string ip = _deviceIp;
        if (string.IsNullOrEmpty(ip) || await GetInputsAsync(ip) is not { } inputs || ip != _deviceIp) return;
        Dispatcher.UIThread.Post(() =>
        {
            _trayInputs = inputs;
            _trayInputsIp = ip;
            BuildInputsMenu();
        });
    }

    private async Task SwitchInputFromTrayAsync(string ip, string input)
    {
        string leader = PlaybackTargetOf(ip);
        if (leader != ip && !await Dialogs.ConfirmAsync(null,
                $"{DeviceNameOf(ip)} is linked to {DeviceNameOf(leader)}. Switching its input takes it out of the group. Switch anyway?", UiScale))
            return;
        OnInputChosen(ip);
        if (!await SwitchInputAsync(ip, input)) Notify("The device didn't switch the input. Check that it's switched on and reachable.");
        _ = RefreshNowPlayingAsync();
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
            if (await AddDeviceAsync(null)) BuildDevicesMenu();
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
        _mnuOutputs.Add(Disabled("(none checked = the volume keys are left alone)"));
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
            _mnuOutputs.Add(Check(name, IsOutputEnabled(id), on => SetOutputDeviceEnabled(deviceId, on)));
        }

        _mnuOutputs.Add(new NativeMenuItemSeparator());
        _mnuOutputs.Add(MenuAction("Allow all outputs", () =>
        {
            ClearOutputDevices();
            BuildOutputDevicesMenu();
        }));
    }

    private readonly List<(int Minutes, NativeMenuItem Item)> _sleepItems = [];

    private NativeMenuItem BuildSleepMenu()
    {
        var sleepMenu = new NativeMenu();
        var header = Submenu("Sleep timer", sleepMenu);
        foreach (int minutes in SleepChoices.Prepend(0))
        {
            int m = minutes;
            var item = Radio(m == 0 ? "Off" : $"{m} minutes", m == 0, () => _ = SetTraySleepAsync(m));
            _sleepItems.Add((m, item));
            sleepMenu.Add(item);
        }
        sleepMenu.Opening += async (_, _) =>
        {
            int seconds = await GetSleepSecondsAsync(_deviceIp);
            header.Header = seconds > 0 ? $"Sleep timer ({(seconds + 59) / 60} min left)" : "Sleep timer";
            foreach (var (m, item) in _sleepItems) item.IsChecked = m == 0 && seconds == 0;
        };
        return header;
    }

    private async Task SetTraySleepAsync(int minutes)
    {
        bool ok = await SetSleepTimerAsync(_deviceIp, minutes);
        foreach (var (m, item) in _sleepItems) item.IsChecked = ok && m == minutes;
        Notify(!ok ? "The device didn't accept the sleep timer."
            : minutes > 0 ? $"Sleep timer: the device stops playing in {minutes} minutes."
            : "Sleep timer off.");
    }

    private DateTime _lastErrorNotice;

    internal void ReportError()
    {
        if (DateTime.UtcNow - _lastErrorNotice < TimeSpan.FromSeconds(10)) return;
        _lastErrorNotice = DateTime.UtcNow;
        Notify("Something went wrong. The details were saved to wiim-errors.log.");
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
    }
}
