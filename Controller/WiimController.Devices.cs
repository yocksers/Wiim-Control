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
        _settingsWindow?.OnActiveDeviceChanged();
    }

    internal void AddManualDevice(string ip)
    {
        var dev = new KnownDevice($"WiiM device {_knownDevices.Count + 1}", ip, string.Empty);
        _knownDevices.Add(dev);
        SwitchDevice(dev);
        _ = FillMissingDeviceIdsAsync();
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
        if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setHexDeviceName:{hex}"))) return false;
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
        bool changed = false;
        foreach (var d in found)
        {
            if (_knownDevices.Any(k => k.Ip == d.Ip)) continue;
            int moved = _knownDevices.FindIndex(k => SameDeviceId(k.Uuid, d.Uuid));
            if (moved >= 0)
            {
                bool wasActive = _knownDevices[moved].Ip == _deviceIp;
                _knownDevices[moved] = _knownDevices[moved] with { Ip = d.Ip };
                if (wasActive) _deviceIp = d.Ip;
                changed = true;
                continue;
            }
            _knownDevices.Add(d);
            added++;
        }
        if (added > 0 || changed)
        {
            SaveConfig();
            RefreshDeviceStatusUi();
        }
        _ = FillMissingDeviceIdsAsync();
        return added;
    }

    internal void SetOutputDeviceEnabled(string id, bool enabled)
    {
        if (enabled) _enabledOutputIds.Add(id);
        else         _enabledOutputIds.Remove(id);
        _outputMonitor?.SetEnabledDevices(_enabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
        UpdateVolumeKeyRegistration();
    }

    internal void ClearOutputDevices()
    {
        _enabledOutputIds.Clear();
        _outputMonitor?.SetEnabledDevices(_enabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
        UpdateVolumeKeyRegistration();
    }
}
