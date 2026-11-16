using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WiimControl;

[SupportedOSPlatform("macos")]
sealed class MacAudioSamples : IAudioSamples
{
    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const uint Utf8 = 0x08000100;
    private const int BufferListHeader = 8;
    private const int BufferSize = 16;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IoProc(uint device, IntPtr now, IntPtr input, IntPtr inputTime, IntPtr output, IntPtr outputTime, IntPtr client);

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendTest(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(CoreAudio)] private static extern int AudioHardwareCreateProcessTap(IntPtr description, out uint tap);
    [DllImport(CoreAudio)] private static extern int AudioHardwareDestroyProcessTap(uint tap);
    [DllImport(CoreAudio)] private static extern int AudioHardwareCreateAggregateDevice(IntPtr description, out uint device);
    [DllImport(CoreAudio)] private static extern int AudioHardwareDestroyAggregateDevice(uint device);
    [DllImport(CoreAudio)] private static extern int AudioDeviceCreateIOProcID(uint device, IntPtr proc, IntPtr client, out IntPtr procId);
    [DllImport(CoreAudio)] private static extern int AudioDeviceDestroyIOProcID(uint device, IntPtr procId);
    [DllImport(CoreAudio)] private static extern int AudioDeviceStart(uint device, IntPtr procId);
    [DllImport(CoreAudio)] private static extern int AudioDeviceStop(uint device, IntPtr procId);
    [DllImport(CoreAudio)] private static extern int AudioObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier,
        ref uint dataSize, out double data);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    private const uint NominalSampleRate = 0x6E737274;
    private const uint ScopeGlobal = 0x676C6F62;

    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);
    [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayCreateMutable(IntPtr allocator, long capacity, IntPtr callBacks);
    [DllImport(CoreFoundation)] private static extern void CFArrayAppendValue(IntPtr array, IntPtr value);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);

    private static readonly Lazy<bool> TapsExist = new(() =>
    {
        try { return OperatingSystem.IsMacOSVersionAtLeast(14, 2) && objc_getClass("CATapDescription") != IntPtr.Zero; }
        catch (Exception) { return false; }
    });

    internal static bool Available => TapsExist.Value;

    private readonly IoProc _proc;
    private float[] _samples = new float[4096];
    private readonly SampleRing _ring = new(AudioSamples.RingSize);
    private float[] _leftSamples = new float[4096];
    private float[] _rightSamples = new float[4096];
    private int _sampleRate = 48000;
    private IntPtr _description;
    private uint _tap;
    private uint _device;
    private IntPtr _procId;

    private MacAudioSamples() => _proc = OnAudio;

    internal static MacAudioSamples? Open(string? outputId)
    {
        if (!Available) return null;
        var levels = new MacAudioSamples();
        if (levels.Start(outputId ?? MacAudioOutputs.DefaultId())) return levels;
        levels.Dispose();
        return null;
    }

    private static IntPtr Selector(string name) => sel_registerName(name);

    private static bool Responds(IntPtr target, string name) =>
        target != IntPtr.Zero && SendTest(target, Selector("respondsToSelector:"), Selector(name));

    private static IntPtr Call(IntPtr target, string name) => Responds(target, name) ? Send(target, Selector(name)) : IntPtr.Zero;

    private static IntPtr Text(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8);

    private static IntPtr Export(string name)
    {
        IntPtr library = NativeLibrary.Load(CoreFoundation);
        return NativeLibrary.GetExport(library, name);
    }

    private bool Start(string? outputUid)
    {
        IntPtr empty = Call(objc_getClass("NSArray"), "array");
        IntPtr allocated = Call(objc_getClass("CATapDescription"), "alloc");
        if (empty == IntPtr.Zero || !Responds(allocated, "initStereoGlobalTapButExcludeProcesses:")) return false;
        _description = Send(allocated, Selector("initStereoGlobalTapButExcludeProcesses:"), empty);
        if (_description == IntPtr.Zero) return false;
        if (Responds(_description, "setPrivate:")) SendBool(_description, Selector("setPrivate:"), true);
        if (AudioHardwareCreateProcessTap(_description, out _tap) != 0 || _tap == 0) return false;
        IntPtr tapUid = Call(Call(_description, "UUID"), "UUIDString");
        if (tapUid == IntPtr.Zero) return false;

        IntPtr keyCallBacks = Export("kCFTypeDictionaryKeyCallBacks");
        IntPtr valueCallBacks = Export("kCFTypeDictionaryValueCallBacks");
        IntPtr arrayCallBacks = Export("kCFTypeArrayCallBacks");
        IntPtr yes = Marshal.ReadIntPtr(Export("kCFBooleanTrue"));
        IntPtr no = Marshal.ReadIntPtr(Export("kCFBooleanFalse"));
        var owned = new List<IntPtr>();
        IntPtr Own(IntPtr value)
        {
            if (value != IntPtr.Zero) owned.Add(value);
            return value;
        }
        try
        {
            IntPtr description = Own(CFDictionaryCreateMutable(IntPtr.Zero, 0, keyCallBacks, valueCallBacks));
            void Set(IntPtr dictionary, string key, IntPtr value) => CFDictionarySetValue(dictionary, Own(Text(key)), value);
            Set(description, "name", Own(Text("Wiim Control VU meters")));
            Set(description, "uid", Own(Text("com.wiimcontrol.vu." + Guid.NewGuid().ToString("N"))));
            Set(description, "private", yes);
            Set(description, "stacked", no);
            Set(description, "tapautostart", yes);
            if (outputUid is { Length: > 0 })
            {
                IntPtr output = Own(Text(outputUid));
                Set(description, "master", output);
                IntPtr subDevice = Own(CFDictionaryCreateMutable(IntPtr.Zero, 0, keyCallBacks, valueCallBacks));
                Set(subDevice, "uid", output);
                IntPtr subDevices = Own(CFArrayCreateMutable(IntPtr.Zero, 0, arrayCallBacks));
                CFArrayAppendValue(subDevices, subDevice);
                Set(description, "subdevices", subDevices);
            }
            IntPtr subTap = Own(CFDictionaryCreateMutable(IntPtr.Zero, 0, keyCallBacks, valueCallBacks));
            Set(subTap, "uid", tapUid);
            Set(subTap, "drift", yes);
            IntPtr taps = Own(CFArrayCreateMutable(IntPtr.Zero, 0, arrayCallBacks));
            CFArrayAppendValue(taps, subTap);
            Set(description, "taps", taps);
            if (AudioHardwareCreateAggregateDevice(description, out _device) != 0 || _device == 0) return false;
        }
        finally
        {
            foreach (IntPtr value in owned) CFRelease(value);
        }

        var rateAddress = new PropertyAddress { Selector = NominalSampleRate, Scope = ScopeGlobal };
        uint rateSize = sizeof(double);
        if (AudioObjectGetPropertyData(_device, ref rateAddress, 0, IntPtr.Zero, ref rateSize, out double rate) == 0 && rate >= 8000)
            _sampleRate = (int)Math.Round(rate);
        if (AudioDeviceCreateIOProcID(_device, Marshal.GetFunctionPointerForDelegate(_proc), IntPtr.Zero, out _procId) != 0 || _procId == IntPtr.Zero)
            return false;
        return AudioDeviceStart(_device, _procId) == 0;
    }

    private int OnAudio(uint device, IntPtr now, IntPtr input, IntPtr inputTime, IntPtr output, IntPtr outputTime, IntPtr client)
    {
        try
        {
            if (input == IntPtr.Zero) return 0;
            int buffers = Marshal.ReadInt32(input);
            int frames = 0;
            for (int b = 0; b < buffers; b++)
            {
                IntPtr buffer = input + BufferListHeader + b * BufferSize;
                int channels = Marshal.ReadInt32(buffer);
                int bytes = Marshal.ReadInt32(buffer, 4);
                IntPtr data = Marshal.ReadIntPtr(buffer, 8);
                int count = bytes / sizeof(float);
                if (data == IntPtr.Zero || count <= 0 || channels <= 0) continue;
                if (_samples.Length < count) _samples = new float[count];
                Marshal.Copy(data, _samples, 0, count);
                if (b == 0)
                {
                    frames = count / channels;
                    if (_leftSamples.Length < frames)
                    {
                        _leftSamples = new float[frames];
                        _rightSamples = new float[frames];
                    }
                    for (int f = 0; f < frames; f++)
                    {
                        _leftSamples[f] = _samples[f * channels];
                        _rightSamples[f] = _samples[f * channels + Math.Min(1, channels - 1)];
                    }
                }
                else if (b == 1 && channels == 1)
                    for (int f = 0; f < Math.Min(frames, count); f++) _rightSamples[f] = _samples[f];
            }
            if (frames > 0) _ring.Push(_leftSamples, _rightSamples, frames);
        }
        catch (Exception) { }
        return 0;
    }

    public int SampleRate => _sampleRate;

    public void CopyRecent(float[] destination, AudioChannel channel = AudioChannel.Mix) => _ring.CopyRecent(destination, channel);

    public void Dispose()
    {
        if (_device != 0 && _procId != IntPtr.Zero)
        {
            AudioDeviceStop(_device, _procId);
            AudioDeviceDestroyIOProcID(_device, _procId);
        }
        if (_device != 0) AudioHardwareDestroyAggregateDevice(_device);
        if (_tap != 0) AudioHardwareDestroyProcessTap(_tap);
        if (_description != IntPtr.Zero) Call(_description, "release");
        _procId = IntPtr.Zero;
        _device = 0;
        _tap = 0;
        _description = IntPtr.Zero;
    }
}
