namespace WiimControl;

interface IOutputDeviceMonitor : IDisposable
{
    bool IsEnabledOutputActive { get; }
    bool IsCurrentDeviceEnabled();
    void SetDisabledDevices(IEnumerable<string> deviceIds);
    event Action<bool>? StatusChanged;
    event Action? DefaultDeviceChanged;
}
