namespace WiimControl;

sealed partial class WiimController
{
    private readonly HashSet<string> _lockedDevices = new(StringComparer.OrdinalIgnoreCase);

    private string LockKeyOf(string ip) =>
        _knownDevices.FirstOrDefault(k => k.Ip == ip) is { Ip: not null, Uuid: { Length: > 0 } uuid } ? uuid : ip;

    internal bool IsDeviceLocked(string ip) =>
        ip.Length > 0 && (_lockedDevices.Contains(LockKeyOf(ip)) || _lockedDevices.Contains(ip));

    internal void SetDeviceLocked(string ip, bool locked)
    {
        _lockedDevices.Remove(ip);
        if (locked) _lockedDevices.Add(LockKeyOf(ip));
        else _lockedDevices.Remove(LockKeyOf(ip));
        SaveConfig();
        if (_miniPlayers.TryGetValue(ip, out var window)) window.ApplyLock();
    }

    private bool BlockedByLock(params string[] ips)
    {
        if (ips.FirstOrDefault(IsDeviceLocked) is not { } ip) return false;
        Notify($"{DeviceNameOf(ip)} is locked. Unlock it on the Devices page to change it.");
        return true;
    }
}
