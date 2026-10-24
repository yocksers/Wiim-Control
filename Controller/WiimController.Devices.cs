namespace WiimControl;

sealed partial class WiimController
{
    private static async Task<List<KnownDevice>> TryDiscoverDevicesAsync()
    {
        try { return await WiimDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception) { return []; }
    }

    internal static List<(string Id, string Name)> ActiveOutputDevices()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try { return OutputDeviceMonitor.GetActiveOutputDevices(); }
        catch (Exception) { return []; }
    }

    internal void SwitchDevice(KnownDevice d)
    {
        if (d.Ip == _deviceIp) return;
        _deviceIp     = d.Ip;
        _deviceUuid   = d.Uuid;
        _cachedVolume = null;
        _cachedMuted  = null;
        _cachedIp     = string.Empty;
        _nowPlayingText  = string.Empty;
        RefreshDeviceStatusUi();
        SaveConfig();
        _ = RefreshNowPlayingAsync();
        SyncPcAudioUi();
        _settingsWindow?.OnActiveDeviceChanged();
    }

    internal void AddManualDevice(string ip, string? name = null, string uuid = "")
    {
        var dev = new KnownDevice(name ?? $"WiiM device {_knownDevices.Count + 1}", ip, uuid);
        _knownDevices.Add(dev);
        SwitchDevice(dev);
        if (uuid.Length == 0) _ = FillMissingDeviceIdsAsync();
    }

    private static bool SameDeviceId(string a, string b) =>
        a.Length > 0 && b.Length > 0 && SameUuid(NormalizeUuid(a), NormalizeUuid(b));

    private static bool HasRealId(KnownDevice d) => d.Uuid.Length > 0 && d.Uuid != d.Ip;

    private async Task FillMissingDeviceIdsAsync()
    {
        bool changed = false;
        foreach (var device in _knownDevices.Where(d => !HasRealId(d)).ToList())
        {
            var uuid = await WiimDiscovery.FetchUuidAsync(device.Ip);
            if (uuid == null) continue;
            int index = _knownDevices.FindIndex(k => k.Ip == device.Ip);
            if (index < 0) continue;
            _knownDevices[index] = _knownDevices[index] with { Uuid = uuid };
            if (device.Ip == _deviceIp) _deviceUuid = uuid;
            changed = true;
        }
        if (changed) SaveConfig();
    }

    internal const int MaxDeviceNameLength = 50;

    internal async Task<bool> RenameDeviceAsync(string ip, string name)
    {
        string hex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(name));
        if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setHexDeviceName:{hex}")) &&
            !(_multiroomCache is { } group && group.LeaderOf.TryGetValue(ip, out var leader) &&
              await RenameGroupMemberAsync(leader, group.AddressOf(ip), name))) return false;
        int index = _knownDevices.FindIndex(k => k.Ip == ip);
        if (index >= 0) _knownDevices[index] = _knownDevices[index] with { Name = name };
        SaveConfig();
        RefreshDeviceStatusUi();
        return true;
    }

    internal bool MoveDevice(KnownDevice d, int offset)
    {
        int index = _knownDevices.FindIndex(k => k.Ip == d.Ip);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _knownDevices.Count) return false;
        _knownDevices.RemoveAt(index);
        _knownDevices.Insert(target, d);
        SaveConfig();
        return true;
    }

    internal void RemoveDevice(KnownDevice d)
    {
        _knownDevices.RemoveAll(k => k.Ip == d.Ip);
        CloseMiniPlayer(d.Ip);
        if (IsPlayingPcAudio(d.Ip)) StopPcAudio();
        if (d.Ip == _deviceIp && _knownDevices.Count > 0)
        {
            SwitchDevice(_knownDevices[0]);
            return;
        }
        if (d.Ip == _deviceIp)
        {
            _deviceIp = string.Empty;
            _deviceUuid = string.Empty;
            _cachedVolume = null;
            _cachedMuted = null;
            _cachedIp = string.Empty;
            _nowPlayingText = string.Empty;
        }
        RefreshDeviceStatusUi();
        SaveConfig();
    }

    internal async Task<int> DiscoverAndMergeDevicesAsync()
    {
        List<KnownDevice> found;
        try { found = await WiimDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3)); }
        catch { found = []; }

        int added = 0;
        var moves = new Dictionary<string, string>();
        foreach (var d in found)
        {
            int known = d.Uuid.Length > 0 ? _knownDevices.FindIndex(k => SameDeviceId(k.Uuid, d.Uuid)) : -1;
            if (known >= 0)
            {
                if (_knownDevices[known].Ip != d.Ip) moves[_knownDevices[known].Ip] = d.Ip;
                continue;
            }
            if (_knownDevices.Any(k => k.Ip == d.Ip) || moves.ContainsValue(d.Ip)) continue;
            _knownDevices.Add(d);
            added++;
        }
        bool changed = ApplyAddressMoves(moves);
        if (added > 0 || changed)
        {
            SaveConfig();
            RefreshDeviceStatusUi();
        }
        _ = FillMissingDeviceIdsAsync();
        return added;
    }

    private bool ApplyAddressMoves(Dictionary<string, string> moves)
    {
        if (moves.Count == 0) return false;
        var placements = moves.Keys.Where(_miniPlayerPlacements.ContainsKey).ToDictionary(ip => ip, ip => _miniPlayerPlacements[ip]);
        foreach (string oldIp in moves.Keys)
        {
            _miniPlayerPlacements.Remove(oldIp);
            CloseMiniPlayer(oldIp);
        }
        foreach (var (oldIp, placement) in placements) _miniPlayerPlacements[moves[oldIp]] = placement;
        for (int i = 0; i < _knownDevices.Count; i++)
            if (moves.TryGetValue(_knownDevices[i].Ip, out var newIp)) _knownDevices[i] = _knownDevices[i] with { Ip = newIp };
        if (moves.TryGetValue(_deviceIp, out var active)) _deviceIp = active;
        if (moves.TryGetValue(_groupLeaderIp, out var leader)) _groupLeaderIp = leader;
        return true;
    }

    internal bool IsOutputEnabled(string id) => !_disabledOutputIds.Contains(id);

    internal void SetOutputDeviceEnabled(string id, bool enabled)
    {
        if (enabled) _disabledOutputIds.Remove(id);
        else         _disabledOutputIds.Add(id);
        _outputMonitor?.SetDisabledDevices(_disabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
        UpdateVolumeKeyRegistration();
    }

    internal void ClearOutputDevices()
    {
        _disabledOutputIds.Clear();
        _outputMonitor?.SetDisabledDevices(_disabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
        UpdateVolumeKeyRegistration();
    }

    private void MigrateEnabledOutputs()
    {
        if (_legacyEnabledOutputIds is not { Count: > 0 } enabled || !OperatingSystem.IsWindows()) return;
        foreach (var (id, _) in OutputDeviceMonitor.GetActiveOutputDevices())
            if (!enabled.Contains(id)) _disabledOutputIds.Add(id);
        _legacyEnabledOutputIds = null;
        SaveConfig();
    }
}
