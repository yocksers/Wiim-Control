using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace WiimControl;

[ClassInterface(ClassInterfaceType.None), ComVisible(true)]
[SupportedOSPlatform("windows")]
sealed class OutputDeviceMonitor : IMMNotificationClient, IOutputDeviceMonitor
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private const int DEVICE_STATE_ACTIVE  = 1;
    private const int eRender     = 0;
    private const int eMultimedia = 1;

    private static readonly PropertyKey PKEY_FriendlyName = new()
    {
        FormatId   = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        PropertyId = 14
    };

    private readonly IMMDeviceEnumerator _enumerator;
    private volatile bool _outputActive;
    private HashSet<string> _disabledDeviceIds;

    public bool IsEnabledOutputActive => _outputActive;

    public bool IsCurrentDeviceEnabled() => CheckDefaultDevice();

    public event Action<bool>? StatusChanged;
    public event Action? DefaultDeviceChanged;

    public OutputDeviceMonitor(IEnumerable<string>? disabledDeviceIds = null)
    {
        var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)
            ?? throw new InvalidOperationException("MMDeviceEnumerator COM class not found.");
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
        _disabledDeviceIds = new HashSet<string>(disabledDeviceIds ?? [], StringComparer.OrdinalIgnoreCase);
        _outputActive = CheckDefaultDevice();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void SetDisabledDevices(IEnumerable<string> deviceIds)
    {
        _disabledDeviceIds = new HashSet<string>(deviceIds, StringComparer.OrdinalIgnoreCase);
        var active = CheckDefaultDevice();
        if (active != _outputActive)
        {
            _outputActive = active;
            StatusChanged?.Invoke(active);
        }
    }

    public static List<(string Id, string Name)> GetActiveOutputDevices()
    {
        var result = new List<(string, string)>();
        var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
        if (type is null) return result;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
        try
        {
            if (enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var collection) != 0
                || collection is null) return result;
            try
            {
                if (collection.GetCount(out uint count) != 0) return result;
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0 || device is null) continue;
                    try
                    {
                        if (device.GetId(out var id) != 0) continue;
                        string name = id;
                        if (device.OpenPropertyStore(0, out var store) == 0 && store != null)
                        {
                            var key = PKEY_FriendlyName;
                            if (store.GetValue(ref key, out var pv) == 0)
                                name = pv.AsString() ?? id;
                        }
                        result.Add((id, name));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        catch { }
        finally { Marshal.ReleaseComObject(enumerator); }
        return result;
    }

    private bool CheckDefaultDevice()
    {
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0
                || device is null) return false;
            try
            {
                if (device.GetId(out var id) != 0) return false;
                return !_disabledDeviceIds.Contains(id);
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        catch { return false; }
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(int flow, int role, string id)
    {
        if (flow == eRender && role == eMultimedia)
        {
            DefaultDeviceChanged?.Invoke();
            var active = CheckDefaultDevice();
            if (active != _outputActive)
            {
                _outputActive = active;
                StatusChanged?.Invoke(active);
            }
        }
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(string id, int state) => 0;
    int IMMNotificationClient.OnDeviceAdded(string id)                   => 0;
    int IMMNotificationClient.OnDeviceRemoved(string id)                 => 0;
    int IMMNotificationClient.OnPropertyValueChanged(string id, PropertyKey k) => 0;

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        Marshal.ReleaseComObject(_enumerator);
    }
}
