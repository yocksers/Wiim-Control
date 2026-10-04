using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace WiimControl;

sealed class X11Hotkeys : IGlobalHotkeys
{
    private const string LibX11 = "libX11.so.6";
    private const int KeyPress = 2;
    private const int GrabModeAsync = 1;
    private const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    private const uint RelevantModifiers = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;
    private static readonly uint[] IgnoredModifierCombinations = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern UIntPtr XStringToKeysym(string name);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);
    [DllImport(LibX11)] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow,
        int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LibX11)] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
    [DllImport(LibX11)] private static extern int XSync(IntPtr display, int discard);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(IntPtr handler);

    private static readonly XErrorHandler ErrorHandler = OnXError;
    private static bool _grabFailed;

    private readonly Action<int> _onPressed;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Dictionary<(int Keycode, uint Modifiers), int> _grabs = new();
    private readonly Thread? _thread;
    private volatile bool _stop;
    private IntPtr _display;
    private IntPtr _root;

    public X11Hotkeys(Action<int> onPressed)
    {
        _onPressed = onPressed;
        bool wayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Note = "Hotkeys need an X11 desktop session.";
            return;
        }
        if (wayland)
            Note = "You're using a Wayland session, where apps can't use system-wide hotkeys. " +
                   "They may only work while a Wiim Control window is active. Log in with an X11 session, " +
                   "or bind the keys to the commands on the General page instead.";
        _thread = new Thread(Run) { IsBackground = true, Name = "X11 hotkeys" };
        _thread.Start();
    }

    public string? Note { get; private set; }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        _grabFailed = true;
        return 0;
    }

    public Task<HashSet<int>> SetHotkeysAsync(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        var done = new TaskCompletionSource<HashSet<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_thread == null)
        {
            done.SetResult(hotkeys.Keys.ToHashSet());
            return done.Task;
        }
        _work.Enqueue(() => done.TrySetResult(Apply(hotkeys)));
        return done.Task;
    }

    private HashSet<int> Apply(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        var failed = new HashSet<int>();
        if (_display == IntPtr.Zero) return hotkeys.Keys.ToHashSet();

        foreach (var (keycode, modifiers) in _grabs.Keys)
            foreach (uint extra in IgnoredModifierCombinations)
                XUngrabKey(_display, keycode, modifiers | extra, _root);
        _grabs.Clear();
        XSync(_display, 0);

        foreach (var (id, hotkey) in hotkeys)
        {
            string? name = KeyMap.X11KeysymName(hotkey.Key);
            int keycode = name == null ? 0 : XKeysymToKeycode(_display, XStringToKeysym(name));
            uint modifiers = Modifiers(hotkey.Modifiers);
            if (keycode == 0 || _grabs.ContainsKey((keycode, modifiers)))
            {
                failed.Add(id);
                continue;
            }

            var previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorHandler));
            _grabFailed = false;
            foreach (uint extra in IgnoredModifierCombinations)
                XGrabKey(_display, keycode, modifiers | extra, _root, 0, GrabModeAsync, GrabModeAsync);
            XSync(_display, 0);
            XSetErrorHandler(previous);

            if (_grabFailed)
            {
                foreach (uint extra in IgnoredModifierCombinations)
                    XUngrabKey(_display, keycode, modifiers | extra, _root);
                XSync(_display, 0);
                failed.Add(id);
            }
            else
            {
                _grabs[(keycode, modifiers)] = id;
            }
        }
        return failed;
    }

    private static uint Modifiers(KeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= ShiftMask;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= ControlMask;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= Mod1Mask;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= Mod4Mask;
        return result;
    }

    private void Run()
    {
        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception)
        {
            _display = IntPtr.Zero;
        }
        if (_display == IntPtr.Zero)
        {
            Note = "Couldn't connect to the X11 display, so hotkeys aren't available.";
            while (!_stop)
            {
                while (_work.TryDequeue(out var job)) job();
                Thread.Sleep(50);
            }
            return;
        }
        _root = XDefaultRootWindow(_display);

        var xEvent = Marshal.AllocHGlobal(256);
        try
        {
            while (!_stop)
            {
                while (_work.TryDequeue(out var job)) job();
                while (XPending(_display) > 0)
                {
                    XNextEvent(_display, xEvent);
                    if (Marshal.ReadInt32(xEvent, 0) != KeyPress) continue;
                    uint state = (uint)Marshal.ReadInt32(xEvent, 80) & RelevantModifiers;
                    int keycode = Marshal.ReadInt32(xEvent, 84);
                    if (_grabs.TryGetValue((keycode, state), out int id)) _onPressed(id);
                }
                Thread.Sleep(15);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(xEvent);
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    public void Dispose() => _stop = true;
}
