using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WiimControl;

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[SupportedOSPlatform("windows")]
static class EndpointVolume
{
    private static readonly Guid MMDeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int ClassContextAll = 23;
    private const int Render = 0;
    private const int Multimedia = 1;

    internal static DeviceVolume? Get() => Use<DeviceVolume?>(v =>
        v.GetMasterVolumeLevelScalar(out float level) == 0 && v.GetMute(out bool muted) == 0
            ? new DeviceVolume((int)Math.Round(level * 100), muted)
            : null);

    internal static bool SetVolume(int volume) => Use(v =>
    {
        var context = Guid.Empty;
        return v.SetMasterVolumeLevelScalar(volume / 100f, ref context) == 0;
    });

    internal static bool SetMuted(bool muted) => Use(v =>
    {
        var context = Guid.Empty;
        return v.SetMute(muted, ref context) == 0;
    });

    private static T? Use<T>(Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClass)!)!;
            if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out device) != 0 || device == null) return default;
            var id = EndpointVolumeId;
            if (device.Activate(ref id, ClassContextAll, IntPtr.Zero, out var volumeObject) != 0) return default;
            volume = (IAudioEndpointVolume)volumeObject;
            return action(volume);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            return default;
        }
        finally
        {
            if (volume != null) Marshal.ReleaseComObject(volume);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }
}
