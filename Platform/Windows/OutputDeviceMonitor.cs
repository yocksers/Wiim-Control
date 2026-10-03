using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace WiimControl;

[ClassInterface(ClassInterfaceType.None), ComVisible(true)]
[SupportedOSPlatform("windows")]
sealed class OutputDeviceMonitor : IMMNotificationClient, IAudioEndpointVolumeCallback, IOutputDeviceMonitor
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioEndpointVolume  = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int CLSCTX_INPROC_SERVER = 1;
    private const int DEVICE_STATE_ACTIVE  = 1;
    private const int eRender     = 0;
    private const int eMultimedia = 1;

    private static readonly PropertyKey PKEY_FriendlyName = new()
    {
        FormatId   = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        PropertyId = 14
    };

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly Guid _selfContext = Guid.NewGuid();
    private IAudioEndpointVolume? _endpointVolume;
    private float _pinnedVolume;
    private bool  _pinnedMuted;
    private volatile bool _outputActive;
    private HashSet<string> _enabledDeviceIds;

    private bool NoRestriction => _enabledDeviceIds.Count == 0;

    public bool IsEnabledOutputActive => _outputActive;

    public bool IsCurrentDeviceEnabled() => CheckDefaultDevice();

    public event Action<bool>? StatusChanged;

    public event Action<int>? ExternalVolumeStepDetected;
    public event Action?      ExternalMuteToggleDetected;

    private readonly System.Threading.Timer _healTimer;

    public OutputDeviceMonitor(IEnumerable<string>? enabledDeviceIds = null)
    {
        var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)
            ?? throw new InvalidOperationException("MMDeviceEnumerator COM class not found.");
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
        _enabledDeviceIds = enabledDeviceIds != null
            ? new HashSet<string>(enabledDeviceIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _outputActive = CheckDefaultDevice();
        BindEndpointVolume();
        _enumerator.RegisterEndpointNotificationCallback(this);
        _healTimer = new System.Threading.Timer(_ =>
        {
            if (_endpointVolume is null) BindEndpointVolume();
        }, null, 5000, 5000);
    }

    public void SetEnabledDevices(IEnumerable<string> deviceIds)
    {
        _enabledDeviceIds = new HashSet<string>(deviceIds, StringComparer.OrdinalIgnoreCase);
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
                        if (device.OpenPropertyStore(0 /*STGM_READ*/, out var store) == 0 && store != null)
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
        catch { /* enumeration unavailable */ }
        finally { Marshal.ReleaseComObject(enumerator); }
        return result;
    }

    private void BindEndpointVolume()
    {
        if (_endpointVolume != null)
        {
            try { _endpointVolume.UnregisterControlChangeNotify(this); } catch { }
            Marshal.ReleaseComObject(_endpointVolume);
            _endpointVolume = null;
        }

        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0
                || device is null) return;

            var iid = IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out var obj) != 0
                || obj is not IAudioEndpointVolume epv) return;

            _endpointVolume = epv;
            _endpointVolume.GetMasterVolumeLevelScalar(out _pinnedVolume);
            _endpointVolume.GetMute(out _pinnedMuted);
            _endpointVolume.RegisterControlChangeNotify(this);
        }
        catch { /* endpoint volume control unavailable — revert feature disabled */ }
    }

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr pNotify)
    {
        if (pNotify == IntPtr.Zero) return 0;
        var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(pNotify);

        if (!CheckDefaultDevice() || data.EventContext == _selfContext)
        {
            _pinnedVolume = data.MasterVolume;
            _pinnedMuted  = data.Muted != 0;
            return 0;
        }

        bool volumeChanged = Math.Abs(data.MasterVolume - _pinnedVolume) > 0.0001f;
        bool muteChanged   = (data.Muted != 0) != _pinnedMuted;
        if (!volumeChanged && !muteChanged) return 0;

        int direction = data.MasterVolume > _pinnedVolume ? 1 : -1;

        try
        {
            var ctx = _selfContext;
            if (volumeChanged) _endpointVolume?.SetMasterVolumeLevelScalar(_pinnedVolume, ref ctx);
            if (muteChanged)   _endpointVolume?.SetMute(_pinnedMuted, ref ctx);
        }
        catch { }

        if (volumeChanged) ExternalVolumeStepDetected?.Invoke(direction);
        if (muteChanged)   ExternalMuteToggleDetected?.Invoke();
        return 0;
    }

    private bool CheckDefaultDevice()
    {
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0
                || device is null) return false;
            if (device.GetId(out var id) != 0) return false;
            return NoRestriction || _enabledDeviceIds.Contains(id);
        }
        catch { return false; }
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(int flow, int role, string id)
    {
        if (flow == eRender && role == eMultimedia)
        {
            var active = CheckDefaultDevice();
            BindEndpointVolume();
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
        _healTimer.Dispose();
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        if (_endpointVolume != null)
        {
            try { _endpointVolume.UnregisterControlChangeNotify(this); } catch { }
            Marshal.ReleaseComObject(_endpointVolume);
        }
        Marshal.ReleaseComObject(_enumerator);
    }
}
