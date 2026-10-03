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
        _nowPlayingText  = string.Empty;
        _activeMaxVolume = 100;
        RefreshDeviceStatusUi();
        SaveConfig();
        _ = RefreshActiveMaxVolumeAsync();
        _ = RefreshNowPlayingAsync();
    }

    internal void AddManualDevice(string ip)
    {
        var dev = new KnownDevice($"Wiim Amp {_knownDevices.Count + 1}", ip, string.Empty);
        _knownDevices.Add(dev);
        SwitchDevice(dev);
    }

    internal void RemoveDevice(KnownDevice d)
    {
        _knownDevices.RemoveAll(k => k.Ip == d.Ip);
        if (d.Ip == _deviceIp && _knownDevices.Count > 0) SwitchDevice(_knownDevices[0]);
        else { RefreshDeviceStatusUi(); SaveConfig(); }
    }

    internal async Task<int> DiscoverAndMergeDevicesAsync()
    {
        List<KnownDevice> found;
        try { found = await WiimDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3)); }
        catch { found = []; }

        int added = 0;
        foreach (var d in found)
        {
            if (_knownDevices.Any(k => k.Ip == d.Ip)) continue;
            _knownDevices.Add(d);
            added++;
        }
        if (added > 0) SaveConfig();
        return added;
    }

    internal void SetOutputDeviceEnabled(string id, bool enabled)
    {
        if (enabled) _enabledOutputIds.Add(id);
        else         _enabledOutputIds.Remove(id);
        _outputMonitor?.SetEnabledDevices(_enabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
    }

    internal void ClearOutputDevices()
    {
        _enabledOutputIds.Clear();
        _outputMonitor?.SetEnabledDevices(_enabledOutputIds);
        SaveConfig();
        UpdateTrayStatus();
    }
}
