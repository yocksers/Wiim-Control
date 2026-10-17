using System.Runtime.InteropServices;

namespace WiimControl;

sealed class MacMediaSession : IMediaSession
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string MediaPlayerPath = "/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer";
    private const string TargetClassName = "WiimControlRemoteCommandTarget";
    private const nint HandlerSuccess = 0, HandlerFailed = 200;
    private const nuint StatePlaying = 1, StatePaused = 2, StateStopped = 3;

    private delegate nint CommandHandler(nint self, nint selector, nint commandEvent);

    [DllImport(ObjC)] private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern nint objc_allocateClassPair(nint superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(nint cls);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool class_addMethod(nint cls, nint selector, nint imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendDouble(nint receiver, nint selector, double arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendUInt(nint receiver, nint selector, nuint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(nint receiver, nint selector, byte arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern double SendReturnDouble(nint receiver, nint selector);

    private static readonly CommandHandler Handler = OnRemoteCommand;
    private static readonly Dictionary<nint, string> CommandBySelector = new();
    private static nint _targetClass;
    private static MacMediaSession? _active;

    private static readonly (string Getter, string Action, string? Command)[] Commands =
    [
        ("playCommand", "wiimPlay:", "resume"),
        ("pauseCommand", "wiimPause:", "pause"),
        ("togglePlayPauseCommand", "wiimPlayPause:", "playpause"),
        ("nextTrackCommand", "wiimNext:", "next"),
        ("previousTrackCommand", "wiimPrevious:", "prev"),
        ("stopCommand", "wiimStop:", "stop"),
        ("changePlaybackPositionCommand", "wiimSeek:", null)
    ];

    private readonly Action<string> _onCommand;
    private readonly Action<int> _onSeek;
    private readonly nint _infoCenter;
    private readonly nint _commandCenter;
    private readonly nint _target;
    private readonly nint _keyTitle, _keyArtist, _keyAlbum, _keyDuration, _keyElapsed, _keyRate;
    private string _shown = string.Empty;

    private MacMediaSession(Action<string> onCommand, Action<int> onSeek)
    {
        _onCommand = onCommand;
        _onSeek = onSeek;
        nint library = NativeLibrary.Load(MediaPlayerPath);
        _keyTitle = Constant(library, "MPMediaItemPropertyTitle");
        _keyArtist = Constant(library, "MPMediaItemPropertyArtist");
        _keyAlbum = Constant(library, "MPMediaItemPropertyAlbumTitle");
        _keyDuration = Constant(library, "MPMediaItemPropertyPlaybackDuration");
        _keyElapsed = Constant(library, "MPNowPlayingInfoPropertyElapsedPlaybackTime");
        _keyRate = Constant(library, "MPNowPlayingInfoPropertyPlaybackRate");

        _infoCenter = Send(objc_getClass("MPNowPlayingInfoCenter"), sel_registerName("defaultCenter"));
        _commandCenter = Send(objc_getClass("MPRemoteCommandCenter"), sel_registerName("sharedCommandCenter"));
        if (_infoCenter == 0 || _commandCenter == 0) throw new InvalidOperationException("Now Playing isn't available.");

        _target = Send(Send(TargetClass(), sel_registerName("alloc")), sel_registerName("init"));
        foreach (var (getter, action, _) in Commands)
        {
            nint command = Send(_commandCenter, sel_registerName(getter));
            if (command == 0) continue;
            SendBool(command, sel_registerName("setEnabled:"), 1);
            Send(command, sel_registerName("addTarget:action:"), _target, sel_registerName(action));
        }
        _active = this;
    }

    public static MacMediaSession? Create(Action<string> onCommand, Action<int> onSeek)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 12, 2)) return null;
        return new MacMediaSession(onCommand, onSeek);
    }

    private static nint Constant(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out nint address) ? Marshal.ReadIntPtr(address) : 0;

    private static nint TargetClass()
    {
        if (_targetClass != 0) return _targetClass;
        nint existing = objc_getClass(TargetClassName);
        nint cls = existing != 0 ? existing : objc_allocateClassPair(objc_getClass("NSObject"), TargetClassName, 0);
        nint imp = Marshal.GetFunctionPointerForDelegate(Handler);
        foreach (var (_, action, command) in Commands)
        {
            nint selector = sel_registerName(action);
            CommandBySelector[selector] = command ?? string.Empty;
            if (existing == 0) class_addMethod(cls, selector, imp, "q@:@");
        }
        if (existing == 0) objc_registerClassPair(cls);
        _targetClass = cls;
        return cls;
    }

    private static nint OnRemoteCommand(nint self, nint selector, nint commandEvent)
    {
        try
        {
            var session = _active;
            if (session == null || !CommandBySelector.TryGetValue(selector, out var command)) return HandlerFailed;
            if (command.Length > 0) session._onCommand(command);
            else if (commandEvent != 0)
                session._onSeek((int)Math.Max(0, SendReturnDouble(commandEvent, sel_registerName("positionTime"))));
            return HandlerSuccess;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Media controls", ex);
            return HandlerFailed;
        }
    }

    private static nint NSString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static nint NSNumber(double value) => SendDouble(objc_getClass("NSNumber"), sel_registerName("numberWithDouble:"), value);

    private static void Put(nint dictionary, nint key, nint value)
    {
        if (key != 0 && value != 0) Send(dictionary, sel_registerName("setObject:forKey:"), value, key);
    }

    public void Update(DeviceStatus? status)
    {
        try
        {
            if (status == null)
            {
                if (_shown.Length == 0) return;
                _shown = string.Empty;
                Send(_infoCenter, sel_registerName("setNowPlayingInfo:"), 0);
                SendUInt(_infoCenter, sel_registerName("setPlaybackState:"), StateStopped);
                return;
            }

            string state = status.PlayState.ToLowerInvariant();
            bool playing = state is "play" or "load";
            nuint playbackState = playing ? StatePlaying : state == "pause" ? StatePaused : StateStopped;
            string title = status.Title.Length > 0 ? status.Title : WiimController.SourceName(status.Mode);

            nint info = Send(objc_getClass("NSMutableDictionary"), sel_registerName("dictionary"));
            if (title.Length > 0) Put(info, _keyTitle, NSString(title));
            if (status.Artist.Length > 0) Put(info, _keyArtist, NSString(status.Artist));
            if (status.Album.Length > 0) Put(info, _keyAlbum, NSString(status.Album));
            if (status.DurationMs > 0)
            {
                Put(info, _keyDuration, NSNumber(status.DurationMs / 1000.0));
                Put(info, _keyElapsed, NSNumber(Math.Min(status.PositionMs, status.DurationMs) / 1000.0));
            }
            Put(info, _keyRate, NSNumber(status.IsPlaying ? 1.0 : 0.0));
            Send(_infoCenter, sel_registerName("setNowPlayingInfo:"), info);
            SendUInt(_infoCenter, sel_registerName("setPlaybackState:"), playbackState);
            _shown = title;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Media controls", ex);
        }
    }

    public void Dispose()
    {
        if (_active == this) _active = null;
        try
        {
            foreach (var (getter, _, _) in Commands)
            {
                nint command = Send(_commandCenter, sel_registerName(getter));
                if (command == 0) continue;
                Send(command, sel_registerName("removeTarget:"), _target);
                SendBool(command, sel_registerName("setEnabled:"), 0);
            }
            Send(_infoCenter, sel_registerName("setNowPlayingInfo:"), 0);
            SendUInt(_infoCenter, sel_registerName("setPlaybackState:"), StateStopped);
            Send(_target, sel_registerName("release"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Media controls", ex);
        }
    }
}
