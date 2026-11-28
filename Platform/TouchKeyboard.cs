using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WiimControl;

interface ITouchKeyboard
{
    void Show(TopLevel top);
    void Hide(TopLevel top);
}

static class TouchKeyboard
{
    private const double FieldGap = 16;
    private static ITouchKeyboard? _keyboard;
    private static bool _installed;
    private static bool _enabled;
    private static bool _shown;
    private static TopLevel? _shownOn;
    private static readonly HashSet<TopLevel> Watched = [];
    private static readonly Dictionary<Control, Thickness> Padded = [];

    public static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static void SetEnabled(bool on)
    {
        if (!Supported) return;
        Install();
        _enabled = on;
        if (!on && _shown && _shownOn != null) HideOn(_shownOn);
    }

    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        _keyboard = CreateKeyboard();
        InputElement.PointerReleasedEvent.AddClassHandler<TextBox>((box, _) => Dispatcher.UIThread.Post(() => Pressed(box)),
            RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.LostFocusEvent.AddClassHandler<TextBox>((box, _) => Dispatcher.UIThread.Post(() => Left(box)));
    }

    private static ITouchKeyboard? CreateKeyboard()
    {
#if WINDOWS
        return new WindowsTouchKeyboard();
#else
        return OperatingSystem.IsLinux() ? new OnboardKeyboard() : null;
#endif
    }

    private static bool Editable(object? element) => element is TextBox { IsEnabled: true, IsReadOnly: false };

    private static TopLevel? OwnerOf(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual);
        while (top is PopupRoot { ParentTopLevel: { } parent }) top = parent;
        return top;
    }

    private static void Pressed(TextBox box)
    {
        if (!_enabled || _keyboard == null || !Editable(box) || !box.IsFocused) return;
        if (OwnerOf(box) is not { } top) return;
        Watch(top);
        _keyboard.Show(top);
        _shown = true;
        _shownOn = top;
        if (top.InputPane is { State: InputPaneState.Open } pane) KeepInView(box, top, pane);
    }

    private static void Left(TextBox box)
    {
        if (!_enabled || !_shown || _shownOn == null) return;
        var focused = TopLevel.GetTopLevel(box)?.FocusManager?.GetFocusedElement();
        if (Editable(focused)) return;
        HideOn(_shownOn);
    }

    private static void HideOn(TopLevel top)
    {
        _keyboard?.Hide(top);
        _shown = false;
        _shownOn = null;
        RestorePadding();
    }

    private static void Watch(TopLevel top)
    {
        if (top.InputPane is not { } pane || !Watched.Add(top)) return;
        pane.StateChanged += (_, e) =>
        {
            if (e.NewState == InputPaneState.Closed)
            {
                _shown = false;
                RestorePadding();
                return;
            }
            if (top.FocusManager?.GetFocusedElement() is TextBox box && Editable(box) && OwnerOf(box) == top) KeepInView(box, top, pane);
        };
        top.Closed += (_, _) => Watched.Remove(top);
    }

    private static void KeepInView(TextBox box, TopLevel top, IInputPane pane)
    {
        var covered = pane.OccludedRect;
        if (covered.Height <= 0 || TopLevel.GetTopLevel(box) != top) return;
        if (box.TranslatePoint(new Point(0, box.Bounds.Height), top) is not { } bottom) return;
        double overlap = bottom.Y + FieldGap - covered.Top;
        if (overlap <= 0 || box.FindAncestorOfType<ScrollViewer>() is not { Content: Control content } scroller) return;
        if (!Padded.ContainsKey(content))
        {
            var margin = content.Margin;
            Padded[content] = margin;
            content.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + covered.Height);
        }
        scroller.UpdateLayout();
        double most = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        scroller.Offset = new Vector(scroller.Offset.X, Math.Min(most, scroller.Offset.Y + overlap));
    }

    private static void RestorePadding()
    {
        foreach (var (content, margin) in Padded) content.Margin = margin;
        Padded.Clear();
    }
}
