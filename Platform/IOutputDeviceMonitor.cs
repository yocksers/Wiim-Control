namespace WiimControl;

interface IOutputDeviceMonitor : IDisposable
{
    bool IsEnabledOutputActive { get; }
    bool IsCurrentDeviceEnabled();
    void SetEnabledDevices(IEnumerable<string> deviceIds);
    event Action<bool>? StatusChanged;
    event Action<int>? ExternalVolumeStepDetected;
    event Action? ExternalMuteToggleDetected;
}
