using System.Runtime.InteropServices;
using Avalonia.Input;

namespace WiimControl;

sealed class MacHotkeys : IGlobalHotkeys
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint EventHotKeyPressed = 5;
    private const uint EventParamDirectObject = 0x2D2D2D2D;
    private const uint TypeEventHotKeyID = 0x686B6964;
    private const uint Signature = 0x5749494D;
    private const uint CmdKey = 0x0100, ShiftKey = 0x0200, OptionKey = 0x0800, ControlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    private delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numTypes,
        EventTypeSpec[] typeList, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID hotKeyId,
        IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr theEvent, uint name, uint desiredType,
        IntPtr actualType, nuint bufferSize, IntPtr actualSize, out EventHotKeyID data);

    private readonly Action<int> _onPressed;
    private readonly EventHandlerProc _handler;
    private readonly Dictionary<int, IntPtr> _registered = new();
    private IntPtr _handlerRef;

    public MacHotkeys(Action<int> onPressed)
    {
        _onPressed = onPressed;
        _handler = OnHotKey;
        try
        {
            var spec = new[] { new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed } };
            int status = InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(_handler),
                1, spec, IntPtr.Zero, out _handlerRef);
            if (status != 0) Note = $"Couldn't set up hotkeys (error {status}).";
        }
        catch (Exception ex)
        {
            Note = $"Hotkeys aren't available: {ex.Message}";
        }
    }

    public string? Note { get; }

    private int OnHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        if (GetEventParameter(theEvent, EventParamDirectObject, TypeEventHotKeyID, IntPtr.Zero,
                (nuint)Marshal.SizeOf<EventHotKeyID>(), IntPtr.Zero, out var hotKeyId) == 0 && hotKeyId.Signature == Signature)
            _onPressed((int)hotKeyId.Id);
        return 0;
    }

    public Task<HashSet<int>> SetHotkeysAsync(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        var failed = new HashSet<int>();
        foreach (var hotKeyRef in _registered.Values) UnregisterEventHotKey(hotKeyRef);
        _registered.Clear();
        if (_handlerRef == IntPtr.Zero) return Task.FromResult(hotkeys.Keys.ToHashSet());

        foreach (var (id, hotkey) in hotkeys)
        {
            if (KeyMap.MacKeyCode(hotkey.Key) is not { } keyCode)
            {
                failed.Add(id);
                continue;
            }
            uint modifiers = 0;
            if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta)) modifiers |= CmdKey;
            if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ShiftKey;
            if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers |= OptionKey;
            if (hotkey.Modifiers.HasFlag(KeyModifiers.Control)) modifiers |= ControlKey;

            var hotKeyId = new EventHotKeyID { Signature = Signature, Id = (uint)id };
            if (RegisterEventHotKey(keyCode, modifiers, hotKeyId, GetApplicationEventTarget(), 0, out var hotKeyRef) == 0)
                _registered[id] = hotKeyRef;
            else
                failed.Add(id);
        }
        return Task.FromResult(failed);
    }

    public void Dispose()
    {
        foreach (var hotKeyRef in _registered.Values) UnregisterEventHotKey(hotKeyRef);
        _registered.Clear();
        if (_handlerRef != IntPtr.Zero) RemoveEventHandler(_handlerRef);
        _handlerRef = IntPtr.Zero;
    }
}
