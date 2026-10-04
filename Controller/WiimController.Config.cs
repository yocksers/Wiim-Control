namespace WiimControl;

sealed partial class WiimController
{
    private static readonly string _cfgPath =
        OperatingSystem.IsWindows() ? Path.Combine(AppContext.BaseDirectory, "wiim.cfg")
        : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Wiim Control", "wiim.cfg")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wiim-control", "wiim.cfg");

    private static bool IsAutoStartEnabled() => AutoStart.IsEnabled();

    private void LoadConfig()
    {
        if (!File.Exists(_cfgPath)) return;
        foreach (var line in File.ReadAllLines(_cfgPath))
        {
            var idx = line.IndexOf('=');
            if (idx < 0) continue;
            var key = line[..idx].Trim().ToLowerInvariant();
            var val = line[(idx + 1)..].Trim();
            if (key == "ip")              _deviceIp = val;
            if (key == "deviceuuid")      _deviceUuid = val;
            if (key == "logstep")         _logStep = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "forwardmediakeys") _forwardMediaKeys = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "groupvolumekeys") _groupVolumeKeys = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "groupleader")     _groupLeaderIp = val;
            if (key == "linuxportalshortcuts") _linuxPortalShortcuts = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "hotkeys")         ParseHotkeys(val);
            if (key == "windowmaximized") _windowMaximized = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "windowsize" && val.Split('x') is [var w, var h] &&
                int.TryParse(w, out int width) && int.TryParse(h, out int height) && width > 0 && height > 0)
                _windowSize = new Avalonia.Size(width, height);
            if (key == "uiscale" && int.TryParse(val, out int scale) && scale is >= 50 and <= 300) _uiScalePercent = scale;
            if (key == "suppresswindowsosd") _suppress = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "volumestep" && int.TryParse(val, out int step) && step >= 1) _volumeStep = step;
            if (key == "osdcorner" && Enum.TryParse<OsdCorner>(val, true, out var corner)) _osdCorner = corner;
            if (key == "osddurationms" && int.TryParse(val, out int dur) && dur >= 200) _osdDurationMs = dur;
            if (key == "enabledoutputs" && val.Length > 0)
                _enabledOutputIds = new HashSet<string>(val.Split('|', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            if (key == "knowndevices" && val.Length > 0)
                _knownDevices = val.Split('\u0002', StringSplitOptions.RemoveEmptyEntries)
                    .Select(entry => entry.Split('\u0001'))
                    .Where(parts => parts.Length == 3)
                    .Select(parts => new KnownDevice(parts[0], parts[1], parts[2]))
                    .ToList();
        }
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cfgPath)!);
            File.WriteAllLines(_cfgPath, [
                $"ip={_deviceIp}", $"deviceUuid={_deviceUuid}", $"logStep={_logStep}",
                $"forwardMediaKeys={_forwardMediaKeys}", $"groupVolumeKeys={_groupVolumeKeys}", $"groupLeader={_groupLeaderIp}",
                $"linuxPortalShortcuts={_linuxPortalShortcuts}", $"hotkeys={SerializeHotkeys()}",
                $"windowSize={(_windowSize is { } size ? $"{(int)size.Width}x{(int)size.Height}" : "")}", $"windowMaximized={_windowMaximized}",
                $"uiScale={_uiScalePercent}",
                $"suppressWindowsOSD={_suppress}", $"volumeStep={_volumeStep}",
                $"osdCorner={_osdCorner}", $"osdDurationMs={_osdDurationMs}",
                $"enabledOutputs={string.Join('|', _enabledOutputIds)}",
                $"knownDevices={string.Join('\u0002', _knownDevices.Select(d => string.Join('\u0001', d.Name, d.Ip, d.Uuid)))}"
            ]);
        }
        catch (Exception ex)
        {
            Notify($"Couldn't save settings: {ex.Message}");
        }
    }

    private void SetSuppress(bool value) { _suppress = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetAutoStartSetting(bool value) { AutoStart.SetEnabled(value); SyncTrayCheckboxes(); }
    private void SetForwardMediaKeys(bool value)
    {
        _forwardMediaKeys = value;
        if (OperatingSystem.IsWindows()) (_shellWnd as ShellHookWindow)?.SetMediaKeysEnabled(value);
        SaveConfig();
        SyncTrayCheckboxes();
    }
    private void SetVolumeStep(int value) { _volumeStep = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetLogStep(bool value) { _logStep = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetOsdCorner(OsdCorner value) { _osdCorner = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetOsdDuration(int value) { _osdDurationMs = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetGroupVolumeKeys(bool value) { _groupVolumeKeys = value; SaveConfig(); }
    private void SetGroupLeader(string ip) { _groupLeaderIp = ip; SaveConfig(); }

    private async Task<bool> SetLinuxPortalShortcutsAsync(bool enabled)
    {
        _linuxPortalShortcuts = enabled;
        SaveConfig();
        if (_linuxShortcuts == null) return false;
        if (!enabled)
        {
            _linuxShortcuts.Unregister();
            return true;
        }
        bool registered = await _linuxShortcuts.RegisterAsync(_forwardMediaKeys);
        if (!registered)
        {
            _linuxPortalShortcuts = false;
            SaveConfig();
        }
        return registered;
    }

    private void SaveWindowState(Avalonia.Size size, bool maximized)
    {
        _windowSize = size;
        _windowMaximized = maximized;
        SaveConfig();
    }

    private void ChangeUiScale(int percent)
    {
        _settingsWindow?.Close();
        _uiScalePercent = percent;
        _windowSize = null;
        SaveConfig();
        ShowSettingsWindow();
    }
}
