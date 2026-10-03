namespace WiimControl;

sealed partial class WiimController
{
    internal async Task<DeviceVolume?> GetDeviceVolumeAsync(string ip)
    {
        var json = await SendToDeviceAsync(ip, "getPlayerStatus").ConfigureAwait(false);
        if (!int.TryParse(JsonField(json, "vol"), out int volume)) return null;
        return new DeviceVolume(Math.Clamp(volume, 0, 100), JsonField(json, "mute") == "1");
    }

    internal async Task<Dictionary<string, DeviceVolume?>> GetAllDeviceVolumesAsync()
    {
        var devices = _knownDevices.Select(d => d.Ip).Distinct().ToList();
        var volumes = await Task.WhenAll(devices.Select(GetDeviceVolumeAsync)).ConfigureAwait(false);
        return devices.Zip(volumes).ToDictionary(p => p.First, p => p.Second);
    }

    internal async Task<bool> SetDeviceVolumeAsync(string ip, int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        bool ok = CommandSucceeded(await SendToDeviceAsync(ip, $"setPlayerCmd:vol:{volume}").ConfigureAwait(false));
        if (ok && ip == _deviceIp && _cachedVolume != null) _cachedVolume = volume;
        return ok;
    }

    internal async Task<bool> SetDeviceMuteAsync(string ip, bool muted)
    {
        bool ok = CommandSucceeded(await SendToDeviceAsync(ip, $"setPlayerCmd:mute:{(muted ? 1 : 0)}").ConfigureAwait(false));
        if (ok && ip == _deviceIp && _cachedMuted != null) _cachedMuted = muted;
        return ok;
    }
}
