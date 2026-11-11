using System.Globalization;

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
        LoadConfigLines(lines);
    }

    internal void LoadConfigLines(IEnumerable<string> lines)
    {
        var settings = Settings().ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            int idx = line.IndexOf('=');
            if (idx >= 0 && settings.TryGetValue(line[..idx].Trim(), out var setting)) setting.Load(line[(idx + 1)..].Trim());
        }
    }

    internal IEnumerable<string> ConfigLines() => Settings().Where(s => s.Save != null).Select(s => $"{s.Key}={s.Save!()}");

    private sealed record Setting(string Key, Func<string>? Save, Action<string> Load);

    private static Setting Text(string key, Func<string> save, Action<string> load) => new(key, save, load);

    private static Setting Legacy(string key, Action<string> load) => new(key, null, load);

    private static Setting Flag(string key, Func<bool> get, Action<bool> set, bool onUnlessFalse = false) =>
        new(key, () => get().ToString(), value => set(onUnlessFalse ? !IsWord(value, "false") : IsWord(value, "true")));

    private static Setting Number(string key, Func<int> get, Action<int> set) =>
        new(key, () => get().ToString(CultureInfo.InvariantCulture), value =>
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) set(number);
        });

    private static Setting Choice<T>(string key, Func<T> get, Action<T> set) where T : struct, Enum =>
        new(key, () => get().ToString(), value => { if (Enum.TryParse<T>(value, true, out var choice)) set(choice); });

    private static Setting Names(string key, HashSet<string> names) => new(key, () => string.Join('\u0001', names), value => ParseNameSet(names, value));

    private static bool IsWord(string value, string word) => string.Equals(value, word, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> IdSet(string value) => new(value.Split('|', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    private IEnumerable<Setting> Settings() =>
    [
        Text("ip", () => _deviceIp, value => _deviceIp = value),
        Text("deviceUuid", () => _deviceUuid, value => _deviceUuid = value),
        Flag("logStep", () => _logStep, value => _logStep = value),
        Flag("forwardMediaKeys", () => _forwardMediaKeys, value => _forwardMediaKeys = value),
        Flag("mediaControls", () => _mediaControls, value => _mediaControls = value),
        Flag("groupVolumeKeys", () => _groupVolumeKeys, value => _groupVolumeKeys = value),
        Text("groupLeader", () => _groupLeaderIp, value => _groupLeaderIp = value),
        Flag("linuxPortalShortcuts", () => _linuxPortalShortcuts, value => _linuxPortalShortcuts = value),
        Text("hotkeys", SerializeHotkeys, ParseHotkeys),
        Text("windowSize", () => _windowSize is { } size ? $"{(int)size.Width}x{(int)size.Height}" : "", value =>
        {
            if (value.Split('x') is [var w, var h] && int.TryParse(w, out int width) && int.TryParse(h, out int height) && width > 0 && height > 0)
                _windowSize = new Avalonia.Size(width, height);
        }),
        Flag("windowMaximized", () => _windowMaximized, value => _windowMaximized = value),
        Flag("sidebarHidden", () => _sidebarHidden, value => _sidebarHidden = value),
        Text("theme", () => ThemeEngine.Serialize(_activeTheme), value => { if (ThemeEngine.ParseTheme(value) is { } theme) _activeTheme = theme; }),
        Text("themes", () => ThemeEngine.Serialize(_customThemes), value => _customThemes = ThemeEngine.ParseThemes(value)),
        Number("uiScale", () => _uiScalePercent, value => { if (value is >= 50 and <= 300) _uiScalePercent = value; }),
        Flag("suppressWindowsOSD", () => _suppress, value => _suppress = value),
        Flag("volumeKeys", () => _volumeKeys, value => _volumeKeys = value, onUnlessFalse: true),
        Number("volumeStep", () => _volumeStep, value => { if (value >= 1) _volumeStep = value; }),
        Choice("osdCorner", () => _osdCorner, value => _osdCorner = value),
        Number("osdDurationMs", () => _osdDurationMs, value => { if (value >= 200) _osdDurationMs = value; }),
        Number("osdScale", () => _osdScalePercent, value => { if (value is >= 50 and <= 300) _osdScalePercent = value; }),
        Legacy("enabledOutputs", value => { if (value.Length > 0) _legacyEnabledOutputIds = IdSet(value); }),
        Text("disabledOutputs", () => string.Join('|', _disabledOutputIds), value =>
        {
            _disabledOutputIds = IdSet(value);
            _legacyEnabledOutputIds = null;
        }),
        Flag("onlineArtwork", () => _onlineArtwork, value => _onlineArtwork = value),
        Text("computerInputs", () => string.Join('|', _computerInputs), value => _computerInputs = ComputerInputSet(value)),
        Text("computerOutputs", () => string.Join('|', _computerOutputs.Select(o => $"{o.Key}>{o.Value}")), ParseComputerOutputs),
        Text("libraryServer", () => _libraryServer, value => _libraryServer = value),
        Text("radioFavorites", SerializeRadioFavorites, value => _radioFavorites = ParseRadioFavorites(value)),
        Text("recent", SerializeRecent, value => _recent = ParseRecent(value)),
        Flag("checkUpdates", () => _checkUpdates, value => _checkUpdates = value, onUnlessFalse: true),
        Flag("firmwareChecks", () => _firmwareChecks, value => _firmwareChecks = value, onUnlessFalse: true),
        Flag("instantUpdates", () => _instantUpdates, value => _instantUpdates = value, onUnlessFalse: true),
        Text("skippedVersion", () => _skippedVersion, value => _skippedVersion = value),
        Flag("startMinimized", () => _startMinimized, value => _startMinimized = value),
        Legacy("compactDevices", value => { if (IsWord(value, "true")) _deviceLayout = DeviceLayout.Compact; }),
        Choice("deviceLayout", () => _deviceLayout, value => _deviceLayout = value),
        Legacy("nowPlayingVinyl", value => { if (IsWord(value, "true")) _nowPlayingStyle = NowPlayingStyle.Vinyl; }),
        new("nowPlayingStyle", () => _nowPlayingStyle.ToString(), value =>
        {
            if (IsWord(value, "AmbientControls")) _nowPlayingStyle = NowPlayingStyle.Ambient;
            else if (Enum.TryParse<NowPlayingStyle>(value, true, out var style)) _nowPlayingStyle = style;
        }),
        Text("nowPlayingBackdrop", () => SerializeStyleLevels(_nowPlayingBrightness), value => ParseStyleLevels(value, _nowPlayingBrightness)),
        Flag("nowPlayingLyrics", () => _nowPlayingLyrics, value => _nowPlayingLyrics = value),
        Flag("nowPlayingControls", () => _nowPlayingControls, value => _nowPlayingControls = value, onUnlessFalse: true),
        Number("lyricsDelay", () => _lyricsDelayMs, value => _lyricsDelayMs = Math.Clamp(value, -LyricsDelayLimitMs, LyricsDelayLimitMs)),
        Flag("miniPlayerAuto", () => _miniPlayerAuto, value => _miniPlayerAuto = value),
        Flag("miniPlayerOnTop", () => _miniPlayerOnTop, value => _miniPlayerOnTop = value),
        Flag("miniPlayerCompactOnTop", () => _miniPlayerCompactOnTop, value => _miniPlayerCompactOnTop = value),
        Text("miniPlayers", () => SerializeMiniPlayerPlacements(_miniPlayerPlacements), value => ParseMiniPlayerPlacements(value, _miniPlayerPlacements)),
        Text("miniPlayersCompact", () => SerializeMiniPlayerPlacements(_miniPlayerCompactPlacements),
            value => ParseMiniPlayerPlacements(value, _miniPlayerCompactPlacements)),
        Text("miniPlayerCompact", () => string.Join('|', _miniPlayerCompactIps),
            value => _miniPlayerCompactIps.UnionWith(value.Split('|', StringSplitOptions.RemoveEmptyEntries))),
        Flag("trackToasts", () => _trackToasts, value => _trackToasts = value),
        Flag("artworkBackground", () => _artworkBackground, value => _artworkBackground = value, onUnlessFalse: true),
        Names("hiddenEqPresets", _hiddenEqPresets),
        Names("hiddenPeqPresets", _hiddenPeqPresets),
        Names("lockedDevices", _lockedDevices),
        Names("collapsedSections", _collapsedSections),
        Text("roonCore", () => _roonCore, value => _roonCore = value),
        Text("roonTokens", SerializeRoonTokens, ParseRoonTokens),
        Text("sharedFolders", SerializeSharedFolders, value => _sharedFolders = ParseSharedFolders(value)),
        Text("embyServers", SerializeEmbyServers, value => _embyServers = ParseEmbyServers(value)),
        Text("embyDeviceId", () => _embyDeviceId, value => _embyDeviceId = value),
        Text("knownDevices", () => string.Join('\u0002', _knownDevices.Select(d => string.Join('\u0001', d.Name, d.Ip, d.Uuid))), value =>
        {
            if (value.Length == 0) return;
            _knownDevices = value.Split('\u0002', StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Split('\u0001'))
                .Where(parts => parts.Length is 2 or 3 && parts[1].Length > 0)
                .Select(parts => new KnownDevice(parts[0], parts[1], parts.Length == 3 ? parts[2] : string.Empty))
                .ToList();
        })
    ];

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
            WriteConfigFile(_cfgPath, ConfigLines());
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

    private void SetSuppress(bool value)
    {
        _suppress = value;
        SaveConfig();
        SyncTrayCheckboxes();
        UpdateVolumeKeyRegistration();
    }
    private void SetVolumeKeys(bool value)
    {
        _volumeKeys = value;
        SaveConfig();
        UpdateVolumeKeyRegistration();
    }
    private void SetStartMinimized(bool value) { _startMinimized = value; SaveConfig(); }
    private DeviceLayout _deviceLayout;
    internal bool CompactDevices => _deviceLayout == DeviceLayout.Compact;
    internal DeviceLayout DeviceLayout
    {
        get => _deviceLayout;
        set { _deviceLayout = value; SaveConfig(); }
    }
    internal NowPlayingStyle NowPlayingStyle
    {
        get => _nowPlayingStyle;
        set { _nowPlayingStyle = value; SaveConfig(); }
    }

    internal static int DefaultNowPlayingBrightness(NowPlayingStyle style) => style == NowPlayingStyle.Ambient ? 63 : 18;

    internal int NowPlayingBrightness(NowPlayingStyle style) =>
        _nowPlayingBrightness.TryGetValue(style, out int brightness) ? brightness : DefaultNowPlayingBrightness(style);

    internal void SetNowPlayingBrightness(NowPlayingStyle style, int brightness, bool save)
    {
        _nowPlayingBrightness[style] = Math.Clamp(brightness, 0, 100);
        if (save) SaveConfig();
    }

    private static void ParseStyleLevels(string value, Dictionary<NowPlayingStyle, int> levels)
    {
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (entry.Split(':') is [var name, var number] && Enum.TryParse<NowPlayingStyle>(name, true, out var style) &&
                int.TryParse(number, out int level))
                levels[style] = Math.Clamp(level, 0, 100);
    }

    private static string SerializeStyleLevels(Dictionary<NowPlayingStyle, int> levels) => string.Join(',', levels.Select(l => $"{l.Key}:{l.Value}"));
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

    private readonly HashSet<string> _collapsedSections = [];

    private bool IsSectionCollapsed(string name) => _collapsedSections.Contains(name);

    private void SetSectionCollapsed(string name, bool collapsed)
    {
        if (collapsed ? !_collapsedSections.Add(name) : !_collapsedSections.Remove(name)) return;
        SaveConfig();
    }

    private void SetUiScale(int percent)
    {
        _uiScalePercent = percent;
        SaveConfig();
    }
}
