using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace WiimControl;

sealed class MacLocalPlayer : ILocalPlayer
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AVFoundationPath = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
    private const uint TimeValid = 1, TimeIndefinite = 16;
    private const nint ItemFailed = 2;
    private const nint ControlPlaying = 2;
    private const double EndSlackSeconds = 0.3;

    [StructLayout(LayoutKind.Sequential)]
    private struct CMTime
    {
        public long Value;
        public int Timescale;
        public uint Flags;
        public long Epoch;
    }

    [DllImport(ObjC)] private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendFloat(nint receiver, nint selector, float arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(nint receiver, nint selector, byte arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendTime(nint receiver, nint selector, CMTime time);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CMTime SendReturnTime(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern CMTime SendReturnTimeStret(nint receiver, nint selector);

    private readonly nint _player;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _item;
    private bool _wantsPlay;
    private bool _ended;
    private string? _tempFile;
    private double _volume = 1;
    private bool _muted;

    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action? StateChanged;

    internal static bool Available()
    {
        try
        {
            return NativeLibrary.TryLoad(AVFoundationPath, out _) && objc_getClass("AVPlayer") != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public MacLocalPlayer()
    {
        NativeLibrary.Load(AVFoundationPath);
        _player = Send(Send(objc_getClass("AVPlayer"), Sel("alloc")), Sel("init"));
        if (_player == 0) throw new InvalidOperationException("AVPlayer isn't available.");
        _poll.Tick += (_, _) => Poll();
    }

    private static nint Sel(string name) => sel_registerName(name);

    private static CMTime ReadTime(nint receiver, string selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? SendReturnTime(receiver, Sel(selector)) : SendReturnTimeStret(receiver, Sel(selector));

    private static double Seconds(CMTime time) =>
        (time.Flags & TimeValid) != 0 && (time.Flags & TimeIndefinite) == 0 && time.Timescale > 0 ? (double)time.Value / time.Timescale : 0;

    public bool Playing => _item != 0 && _wantsPlay && !_ended;

    public TimeSpan Position => _item == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, Seconds(ReadTime(_player, "currentTime"))));

    public TimeSpan Duration => _item == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, Seconds(ReadTime(_item, "duration"))));

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            SendFloat(_player, Sel("setVolume:"), (float)_volume);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            SendBool(_player, Sel("setMuted:"), value ? (byte)1 : (byte)0);
        }
    }

    public void Open(Uri url)
    {
        nint text = NSString(url.AbsoluteUri);
        SetItem(Send(objc_getClass("NSURL"), Sel("URLWithString:"), text), null);
    }

    public void Open(Stream stream, string contentType)
    {
        string path = LocalTempFile.Write(stream, contentType);
        SetItem(Send(objc_getClass("NSURL"), Sel("fileURLWithPath:"), NSString(path)), path);
    }

    private void SetItem(nint url, string? tempFile)
    {
        if (url == 0) throw new ArgumentException("The address isn't valid.");
        nint item = Send(objc_getClass("AVPlayerItem"), Sel("playerItemWithURL:"), url);
        if (item == 0) throw new InvalidOperationException("AVPlayer couldn't open it.");
        Send(item, Sel("retain"));
        Send(_player, Sel("replaceCurrentItemWithPlayerItem:"), item);
        ReleaseItem();
        _item = item;
        LocalTempFile.Delete(_tempFile);
        _tempFile = tempFile;
        _wantsPlay = _ended = false;
        _poll.Start();
    }

    private void ReleaseItem()
    {
        if (_item != 0) Send(_item, Sel("release"));
        _item = 0;
    }

    private static nint NSString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public void Play()
    {
        if (_item == 0) return;
        _wantsPlay = true;
        _ended = false;
        Send(_player, Sel("play"));
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (_item == 0) return;
        _wantsPlay = false;
        Send(_player, Sel("pause"));
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        Send(_player, Sel("pause"));
        Send(_player, Sel("replaceCurrentItemWithPlayerItem:"), 0);
        ReleaseItem();
        _wantsPlay = false;
        _poll.Stop();
        LocalTempFile.Delete(_tempFile);
        _tempFile = null;
        StateChanged?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_item == 0) return;
        _ended = false;
        SendTime(_player, Sel("seekToTime:"), new CMTime { Value = (long)(position.TotalSeconds * 1000), Timescale = 1000, Flags = TimeValid });
    }

    private void Poll()
    {
        if (_item == 0) return;
        if (Send(_item, Sel("status")) == ItemFailed)
        {
            string text = "macOS couldn't play it";
            nint error = Send(_item, Sel("error"));
            nint description = error == 0 ? 0 : Send(error, Sel("localizedDescription"));
            if (description != 0 && Marshal.PtrToStringUTF8(Send(description, Sel("UTF8String"))) is { Length: > 0 } message) text = message;
            Stop();
            Failed?.Invoke(text);
            return;
        }
        if (!_wantsPlay || _ended || Send(_player, Sel("timeControlStatus")) == ControlPlaying) return;
        double duration = Seconds(ReadTime(_item, "duration"));
        if (duration <= 0 || Seconds(ReadTime(_player, "currentTime")) < duration - EndSlackSeconds) return;
        _ended = true;
        _wantsPlay = false;
        Ended?.Invoke();
    }

    public void Dispose()
    {
        _poll.Stop();
        Send(_player, Sel("pause"));
        Send(_player, Sel("replaceCurrentItemWithPlayerItem:"), 0);
        ReleaseItem();
        Send(_player, Sel("release"));
        LocalTempFile.Delete(_tempFile);
    }
}
