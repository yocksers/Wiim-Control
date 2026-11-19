using Avalonia.Input;

namespace WiimControl;

enum HotkeyAction { VolumeUp, VolumeDown, Mute, PlayPause, Next, Previous, OpenSettings }

readonly record struct Hotkey(Key Key, KeyModifiers Modifiers)
{
    private static readonly (KeyModifiers Flag, string Name)[] ModifierOrder =
    [
        (KeyModifiers.Control, "Ctrl"), (KeyModifiers.Alt, "Alt"), (KeyModifiers.Shift, "Shift"), (KeyModifiers.Meta, "Meta")
    ];

    public static string MetaName =>
        OperatingSystem.IsWindows() ? "Win" : OperatingSystem.IsMacOS() ? "Cmd" : "Super";

    public bool HasModifier => (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)) != 0;

    public bool IsFunctionKey => Key is >= Key.F1 and <= Key.F24;

    public override string ToString()
    {
        var modifiers = Modifiers;
        return string.Join("+", ModifierOrder.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(Key.ToString()));
    }

    public string Display
    {
        get
        {
            var modifiers = Modifiers;
            return string.Join(" + ", ModifierOrder.Where(m => modifiers.HasFlag(m.Flag))
                .Select(m => m.Flag == KeyModifiers.Meta ? MetaName : m.Name)
                .Append(KeyMap.DisplayName(Key)));
        }
    }

    public static Hotkey? Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], out var key)) return null;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var match = ModifierOrder.FirstOrDefault(m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match.Name == null) return null;
            modifiers |= match.Flag;
        }
        return new Hotkey(key, modifiers);
    }
}

static class KeyMap
{
    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static bool IsSupported(Key key) => WindowsVirtualKey(key) != null;

    public static string DisplayName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        _ => key.ToString()
    };

    public static uint? WindowsVirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        Key.Multiply => 0x6A,
        Key.Add => 0x6B,
        Key.Subtract => 0x6D,
        Key.Divide => 0x6F,
        Key.OemPlus => 0xBB,
        Key.OemComma => 0xBC,
        Key.OemMinus => 0xBD,
        Key.OemPeriod => 0xBE,
        _ => null
    };

    public static string? X11KeysymName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"KP_{key - Key.NumPad0}",
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        Key.Space => "space",
        Key.PageUp => "Prior",
        Key.PageDown => "Next",
        Key.End => "End",
        Key.Home => "Home",
        Key.Left => "Left",
        Key.Up => "Up",
        Key.Right => "Right",
        Key.Down => "Down",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Multiply => "KP_Multiply",
        Key.Add => "KP_Add",
        Key.Subtract => "KP_Subtract",
        Key.Divide => "KP_Divide",
        Key.OemPlus => "plus",
        Key.OemComma => "comma",
        Key.OemMinus => "minus",
        Key.OemPeriod => "period",
        _ => null
    };

    private static readonly uint[] MacLetters =
    [
        0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E,
        0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06
    ];
    private static readonly uint[] MacDigits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];
    private static readonly uint[] MacKeypadDigits = [0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5B, 0x5C];
    private static readonly uint[] MacFunctionKeys =
    [
        0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F,
        0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A
    ];

    public static uint? MacKeyCode(Key key) => key switch
    {
        >= Key.A and <= Key.Z => MacLetters[key - Key.A],
        >= Key.D0 and <= Key.D9 => MacDigits[key - Key.D0],
        >= Key.NumPad0 and <= Key.NumPad9 => MacKeypadDigits[key - Key.NumPad0],
        >= Key.F1 and <= Key.F20 => MacFunctionKeys[key - Key.F1],
        Key.Space => 0x31,
        Key.PageUp => 0x74,
        Key.PageDown => 0x79,
        Key.End => 0x77,
        Key.Home => 0x73,
        Key.Left => 0x7B,
        Key.Up => 0x7E,
        Key.Right => 0x7C,
        Key.Down => 0x7D,
        Key.Delete => 0x75,
        Key.Multiply => 0x43,
        Key.Add => 0x45,
        Key.Subtract => 0x4E,
        Key.Divide => 0x4B,
        Key.OemPlus => 0x18,
        Key.OemComma => 0x2B,
        Key.OemMinus => 0x1B,
        Key.OemPeriod => 0x2F,
        _ => null
    };
}

interface IGlobalHotkeys : IDisposable
{
    string? Note { get; }
    Task<HashSet<int>> SetHotkeysAsync(IReadOnlyDictionary<int, Hotkey> hotkeys);
}
