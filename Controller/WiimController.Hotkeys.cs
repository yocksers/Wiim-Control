using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    internal static readonly (HotkeyAction Action, string Label, string Command)[] HotkeyActions =
    [
        (HotkeyAction.VolumeUp, "Volume up", "vol++"),
        (HotkeyAction.VolumeDown, "Volume down", "vol--"),
        (HotkeyAction.Mute, "Mute", "mute"),
        (HotkeyAction.PlayPause, "Play / pause", "playpause"),
        (HotkeyAction.Next, "Next track", "next"),
        (HotkeyAction.Previous, "Previous track", "prev"),
        (HotkeyAction.OpenSettings, "Open settings", "show")
    ];

    private readonly Dictionary<HotkeyAction, Hotkey> _hotkeys = new();
    private HashSet<HotkeyAction> _failedHotkeys = new();
    private IGlobalHotkeys? _globalHotkeys;

    internal event Action? HotkeysChanged;

    internal string? HotkeyNote => _globalHotkeys?.Note;

    private void StartHotkeys()
    {
        if (OperatingSystem.IsWindows()) _globalHotkeys = _shellWnd as IGlobalHotkeys;
        else if (OperatingSystem.IsMacOS()) _globalHotkeys = new MacHotkeys(OnHotkeyPressed);
        else _globalHotkeys = new X11Hotkeys(OnHotkeyPressed);
        _ = ApplyHotkeysAsync();
    }

    private void OnHotkeyPressed(int id) => Dispatcher.UIThread.Post(() =>
    {
        var action = HotkeyActions.FirstOrDefault(a => (int)a.Action == id);
        if (action.Command != null) HandleCommand(action.Command);
    });

    internal async Task ApplyHotkeysAsync()
    {
        if (_globalHotkeys == null) return;
        var failed = await _globalHotkeys.SetHotkeysAsync(_hotkeys.ToDictionary(p => (int)p.Key, p => p.Value));
        _failedHotkeys = failed.Select(id => (HotkeyAction)id).ToHashSet();
        Dispatcher.UIThread.Post(() => HotkeysChanged?.Invoke());
    }

    internal async Task SuspendHotkeysAsync()
    {
        if (_globalHotkeys != null) await _globalHotkeys.SetHotkeysAsync(new Dictionary<int, Hotkey>());
    }

    internal async Task SetHotkeyAsync(HotkeyAction action, Hotkey? hotkey)
    {
        if (hotkey is { } value) _hotkeys[action] = value;
        else _hotkeys.Remove(action);
        SaveConfig();
        await ApplyHotkeysAsync();
    }

    private string SerializeHotkeys() => string.Join('|', _hotkeys.Select(p => $"{p.Key}:{p.Value}"));

    private void ParseHotkeys(string value)
    {
        _hotkeys.Clear();
        foreach (var entry in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.IndexOf(':');
            if (colon <= 0) continue;
            if (Enum.TryParse<HotkeyAction>(entry[..colon], out var action) && Hotkey.Parse(entry[(colon + 1)..]) is { } hotkey)
                _hotkeys[action] = hotkey;
        }
    }
}
