using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WiimControl;

[SupportedOSPlatform("macos")]
static class MacAudioOutputs
{
    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint SystemObject = 1;
    private const uint DefaultOutputDevice = 0x644F7574;
    private const uint Devices = 0x64657623;
    private const uint Streams = 0x73746D23;
    private const uint Name = 0x6C6E616D;
    private const uint DeviceUid = 0x75696420;
    private const uint ScopeGlobal = 0x676C6F62;
    private const uint ScopeOutput = 0x6F757470;
    private const uint ElementMain = 0;
    private const uint Utf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyDataSize(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier,
        out uint dataSize);

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier,
        ref uint dataSize, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern long CFStringGetLength(IntPtr text);

    [DllImport(CoreFoundation)]
    private static extern long CFStringGetMaximumSizeForEncoding(long length, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern byte CFStringGetCString(IntPtr text, byte[] buffer, long bufferSize, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    internal static List<(string Id, string Name)> List()
    {
        var outputs = new List<(string Id, string Name)>();
        foreach (uint device in DeviceIds())
        {
            var streams = new PropertyAddress { Selector = Streams, Scope = ScopeOutput, Element = ElementMain };
            if (AudioObjectGetPropertyDataSize(device, ref streams, 0, IntPtr.Zero, out uint size) != 0 || size == 0) continue;
            if (Text(device, DeviceUid) is not { Length: > 0 } uid) continue;
            outputs.Add((uid, Text(device, Name) is { Length: > 0 } name ? name : uid));
        }
        return outputs;
    }

    internal static string? DefaultId()
    {
        var address = new PropertyAddress { Selector = DefaultOutputDevice, Scope = ScopeGlobal, Element = ElementMain };
        uint size = sizeof(uint);
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, buffer) != 0) return null;
            uint device = (uint)Marshal.ReadInt32(buffer);
            return device == 0 ? null : Text(device, DeviceUid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static List<uint> DeviceIds()
    {
        var address = new PropertyAddress { Selector = Devices, Scope = ScopeGlobal, Element = ElementMain };
        if (AudioObjectGetPropertyDataSize(SystemObject, ref address, 0, IntPtr.Zero, out uint size) != 0 || size == 0) return [];
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, buffer) != 0) return [];
            var ids = new List<uint>();
            for (int i = 0; i < size / sizeof(uint); i++) ids.Add((uint)Marshal.ReadInt32(buffer, i * sizeof(uint)));
            return ids;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? Text(uint device, uint selector)
    {
        var address = new PropertyAddress { Selector = selector, Scope = ScopeGlobal, Element = ElementMain };
        uint size = (uint)IntPtr.Size;
        IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, buffer) != 0) return null;
            IntPtr text = Marshal.ReadIntPtr(buffer);
            if (text == IntPtr.Zero) return null;
            try
            {
                long length = CFStringGetMaximumSizeForEncoding(CFStringGetLength(text), Utf8) + 1;
                var bytes = new byte[Math.Clamp(length, 1, 4096)];
                if (CFStringGetCString(text, bytes, bytes.Length, Utf8) == 0) return null;
                int end = Array.IndexOf(bytes, (byte)0);
                return System.Text.Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
            }
            finally { CFRelease(text); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
