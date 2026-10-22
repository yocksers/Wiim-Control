namespace WiimControl;

sealed partial class WiimController
{
    internal static string ConfigDirectory => Path.GetDirectoryName(_cfgPath) ?? AppContext.BaseDirectory;

    private static readonly string _cfgPath =
        OperatingSystem.IsWindows() ? Path.Combine(AppContext.BaseDirectory, "wiim.cfg")
        : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Wiim Control", "wiim.cfg")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wiim-control", "wiim.cfg");

    private static bool IsAutoStartEnabled() => AutoStart.IsEnabled();

    private void LoadConfig()
    {
        if (!File.Exists(_cfgPath)) return;
        string[] lines;
        try { lines = File.ReadAllLines(_cfgPath); }
        catch (Exception ex)
        {
            ErrorLog.Write("Settings", ex);
            return;
        }
        foreach (var line in lines)
        {
            var idx = line.IndexOf('=');
            if (idx < 0) continue;
            var key = line[..idx].Trim().ToLowerInvariant();
            var val = line[(idx + 1)..].Trim();
            if (key == "ip")              _deviceIp = val;
            if (key == "deviceuuid")      _deviceUuid = val;
            if (key == "logstep")         _logStep = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "forwardmediakeys") _forwardMediaKeys = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "mediacontrols")   _mediaControls = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "libraryserver")   _libraryServer = val;
            if (key == "recent")          _recent = ParseRecent(val);
            if (key == "checkupdates")    _checkUpdates = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
            if (key == "firmwarechecks")  _firmwareChecks = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
            if (key == "instantupdates")  _instantUpdates = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
            if (key == "startminimized")  _startMinimized = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "nowplayingvinyl") _nowPlayingVinyl = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "miniplayerauto")  _miniPlayerAuto = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "miniplayers")     ParseMiniPlayerPlacements(val);
            if (key == "tracktoasts")     _trackToasts = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "artworkbackground") _artworkBackground = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
            if (key == "skippedversion")  _skippedVersion = val;
            if (key == "hiddeneqpresets") ParseNameSet(_hiddenEqPresets, val);
            if (key == "rooncore")       _roonCore = val;
            if (key == "roontokens")     ParseRoonTokens(val);
            if (key == "sharedfolders")  _sharedFolders = ParseSharedFolders(val);
            if (key == "hiddenpeqpresets") ParseNameSet(_hiddenPeqPresets, val);
            if (key == "lockeddevices")  ParseNameSet(_lockedDevices, val);
            if (key == "radiofavorites")  _radioFavorites = ParseRadioFavorites(val);
            if (key == "groupvolumekeys") _groupVolumeKeys = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "groupleader")     _groupLeaderIp = val;
            if (key == "linuxportalshortcuts") _linuxPortalShortcuts = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "hotkeys")         ParseHotkeys(val);
            if (key == "windowmaximized") _windowMaximized = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "sidebarhidden")   _sidebarHidden = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "windowsize" && val.Split('x') is [var w, var h] &&
                int.TryParse(w, out int width) && int.TryParse(h, out int height) && width > 0 && height > 0)
                _windowSize = new Avalonia.Size(width, height);
            if (key == "uiscale" && int.TryParse(val, out int scale) && scale is >= 50 and <= 300) _uiScalePercent = scale;
            if (key == "suppresswindowsosd") _suppress = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            if (key == "volumekeys")      _volumeKeys = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
            if (key == "volumestep" && int.TryParse(val, out int step) && step >= 1) _volumeStep = step;
            if (key == "osdcorner" && Enum.TryParse<OsdCorner>(val, true, out var corner)) _osdCorner = corner;
            if (key == "osddurationms" && int.TryParse(val, out int dur) && dur >= 200) _osdDurationMs = dur;
            if (key == "osdscale" && int.TryParse(val, out int osdScale) && osdScale is >= 50 and <= 300) _osdScalePercent = osdScale;
            if (key == "enabledoutputs" && val.Length > 0)
                _legacyEnabledOutputIds = new HashSet<string>(val.Split('|', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            if (key == "disabledoutputs")
            {
                _disabledOutputIds = new HashSet<string>(val.Split('|', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                _legacyEnabledOutputIds = null;
            }
            if (key == "knowndevices" && val.Length > 0)
                _knownDevices = val.Split('\u0002', StringSplitOptions.RemoveEmptyEntries)
                    .Select(entry => entry.Split('\u0001'))
                    .Where(parts => parts.Length is 2 or 3 && parts[1].Length > 0)
                    .Select(parts => new KnownDevice(parts[0], parts[1], parts.Length == 3 ? parts[2] : string.Empty))
                    .ToList();
        }
    }

    private readonly object _configGate = new();

    private void SaveConfig()
    {
        if (_configLocked) return;
        lock (_configGate) SaveConfigLocked();
    }

    private void SaveConfigLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cfgPath)!);
            WriteConfigFile(_cfgPath, [
                $"ip={_deviceIp}", $"deviceUuid={_deviceUuid}", $"logStep={_logStep}",
                $"forwardMediaKeys={_forwardMediaKeys}", $"mediaControls={_mediaControls}", $"groupVolumeKeys={_groupVolumeKeys}", $"groupLeader={_groupLeaderIp}",
                $"linuxPortalShortcuts={_linuxPortalShortcuts}", $"hotkeys={SerializeHotkeys()}",
                $"windowSize={(_windowSize is { } size ? $"{(int)size.Width}x{(int)size.Height}" : "")}", $"windowMaximized={_windowMaximized}", $"sidebarHidden={_sidebarHidden}",
                $"uiScale={_uiScalePercent}",
                $"suppressWindowsOSD={_suppress}", $"volumeKeys={_volumeKeys}", $"volumeStep={_volumeStep}",
                $"osdCorner={_osdCorner}", $"osdDurationMs={_osdDurationMs}", $"osdScale={_osdScalePercent}",
                $"disabledOutputs={string.Join('|', _disabledOutputIds)}",
                $"libraryServer={_libraryServer}", $"radioFavorites={SerializeRadioFavorites()}", $"recent={SerializeRecent()}",
                $"checkUpdates={_checkUpdates}", $"firmwareChecks={_firmwareChecks}", $"instantUpdates={_instantUpdates}", $"skippedVersion={_skippedVersion}", $"startMinimized={_startMinimized}", $"nowPlayingVinyl={_nowPlayingVinyl}",
                $"miniPlayerAuto={_miniPlayerAuto}",
                $"miniPlayers={SerializeMiniPlayerPlacements()}", $"trackToasts={_trackToasts}", $"artworkBackground={_artworkBackground}",
                $"hiddenEqPresets={string.Join('\u0001', _hiddenEqPresets)}", $"hiddenPeqPresets={string.Join('\u0001', _hiddenPeqPresets)}",
                $"lockedDevices={string.Join('\u0001', _lockedDevices)}",
                $"roonCore={_roonCore}", $"roonTokens={SerializeRoonTokens()}", $"sharedFolders={SerializeSharedFolders()}",
                $"knownDevices={string.Join('\u0002', _knownDevices.Select(d => string.Join('\u0001', d.Name, d.Ip, d.Uuid)))}"
            ]);
        }
        catch (Exception ex)
        {
            Notify($"Couldn't save settings: {ex.Message}");
        }
    }

    internal static void WriteConfigFile(string path, IEnumerable<string> lines)
    {
        string temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }

    private void SetSuppress(bool value) { _suppress = value; SaveConfig(); SyncTrayCheckboxes(); }
    private void SetVolumeKeys(bool value)
    {
        _volumeKeys = value;
        SaveConfig();
        UpdateVolumeKeyRegistration();
    }
    private void SetStartMinimized(bool value) { _startMinimized = value; SaveConfig(); }
    internal bool NowPlayingVinyl
    {
        get => _nowPlayingVinyl;
        set { _nowPlayingVinyl = value; SaveConfig(); }
    }
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
    private void SetOsdScale(int value) { _osdScalePercent = value; SaveConfig(); }
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

    private void SetSidebarHidden(bool hidden)
    {
        _sidebarHidden = hidden;
        SaveConfig();
    }

    private void SetUiScale(int percent)
    {
        _uiScalePercent = percent;
        SaveConfig();
    }
}
