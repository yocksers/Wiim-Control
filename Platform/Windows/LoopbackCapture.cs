using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WiimControl;

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[SupportedOSPlatform("windows")]
sealed class LoopbackCapture : IPcAudioCapture
{
    private static readonly Guid MMDeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
    private const int SharedMode = 0;
    private const int LoopbackFlag = 0x00020000;
    private const long BufferDuration = 2_000_000;
    private const uint SilentFlag = 0x2;
    private const int PollMs = 10;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stopping;
    private Exception? _startError;

    public int SampleRate { get; private set; }
    public const int Channels = 2;
    public const int BitsPerSample = 24;
    private const int BytesPerSample = BitsPerSample / 8;

    public event Action<byte[]>? Pcm;
    public event Action? Stopped;

    public LoopbackCapture()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "PC sound capture" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait();
        if (_startError != null) throw _startError;
    }

    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr format = IntPtr.Zero;
        bool started = false;
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClass)!)!;
            Check(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
            var clientId = AudioClientId;
            Check(device!.Activate(ref clientId, 1, IntPtr.Zero, out var clientObject));
            client = (IAudioClient)clientObject;
            Check(client.GetMixFormat(out format));
            var source = ReadFormat(format);
            SampleRate = source.Rate;
            Check(client.Initialize(SharedMode, LoopbackFlag, BufferDuration, 0, format, IntPtr.Zero));
            var captureId = CaptureClientId;
            Check(client.GetService(ref captureId, out var captureObject));
            capture = (IAudioCaptureClient)captureObject;
            Check(client.Start());
            started = true;
            _ready.Set();
            Pump(capture, source);
            client.Stop();
        }
        catch (Exception ex)
        {
            if (!started)
            {
                _startError = ex;
                _ready.Set();
            }
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
        }
        if (started && !_stopping) Stopped?.Invoke();
    }

    private void Pump(IAudioCaptureClient capture, SourceFormat source)
    {
        while (!_stopping)
        {
            Thread.Sleep(PollMs);
            while (true)
            {
                if (capture.GetNextPacketSize(out uint packet) != 0) return;
                if (packet == 0) break;
                if (capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _) != 0) break;
                var pcm = (flags & SilentFlag) != 0 ? new byte[frames * Channels * BytesPerSample] : Convert(data, frames, source);
                capture.ReleaseBuffer(frames);
                Pcm?.Invoke(pcm);
            }
        }
    }

    private sealed record SourceFormat(int Rate, int Channels, int Bits, bool Float);

    private static SourceFormat ReadFormat(IntPtr format)
    {
        int tag = Marshal.ReadInt16(format);
        int channels = Marshal.ReadInt16(format, 2);
        int rate = Marshal.ReadInt32(format, 4);
        int bits = Marshal.ReadInt16(format, 14);
        bool isFloat = tag == 3;
        if (tag == unchecked((short)0xFFFE))
        {
            var sub = new byte[16];
            Marshal.Copy(format + 24, sub, 0, 16);
            isFloat = new Guid(sub) == FloatSubFormat;
        }
        return new SourceFormat(rate, channels, bits, isFloat);
    }

    private static byte[] Convert(IntPtr data, uint frames, SourceFormat source)
    {
        int count = (int)frames * source.Channels;
        var samples = new double[count];
        switch (source)
        {
            case { Float: true, Bits: 32 }:
                var floats = new float[count];
                Marshal.Copy(data, floats, 0, count);
                for (int i = 0; i < count; i++) samples[i] = floats[i];
                break;
            case { Bits: 16 }:
                var shorts = new short[count];
                Marshal.Copy(data, shorts, 0, count);
                for (int i = 0; i < count; i++) samples[i] = shorts[i] / 32768.0;
                break;
            case { Bits: 32 }:
                var ints = new int[count];
                Marshal.Copy(data, ints, 0, count);
                for (int i = 0; i < count; i++) samples[i] = ints[i] / 2147483648.0;
                break;
            case { Bits: 24 }:
                var bytes = new byte[count * 3];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                for (int i = 0; i < count; i++)
                    samples[i] = (bytes[i * 3] | (bytes[i * 3 + 1] << 8) | ((sbyte)bytes[i * 3 + 2] << 16)) / 8388608.0;
                break;
        }

        var output = new byte[frames * Channels * BytesPerSample];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < Channels; c++)
            {
                double sample = samples[f * source.Channels + Math.Min(c, source.Channels - 1)];
                int value = (int)Math.Clamp(Math.Round(sample * 8388607), -8388608, 8388607);
                int at = (f * Channels + c) * BytesPerSample;
                output[at] = (byte)value;
                output[at + 1] = (byte)(value >> 8);
                output[at + 2] = (byte)(value >> 16);
            }
        return output;
    }

    private static void Check(int hresult)
    {
        if (hresult != 0) Marshal.ThrowExceptionForHR(hresult);
    }

    public void Dispose()
    {
        _stopping = true;
        if (Thread.CurrentThread != _thread) _thread.Join(1000);
        _ready.Dispose();
    }
}
