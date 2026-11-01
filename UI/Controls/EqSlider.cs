using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WiimControl;

sealed class EqSlider : Control, IRangeSlider
{
    private RangeSliderPeer? _peer;
    private static readonly IPen CenterPen = new Pen(Palette.TextDim, 1);
    private static readonly IPen FocusPen = new Pen(Palette.AccentHover, 2);
    private const double KnobSize = 16;
    private int _min, _max = 100, _value = 50;
    private bool _dragging;

    public event EventHandler? ValueChanged;
    public int Center { get; set; } = 50;
    public int Step { get; set; } = 1;

    int IRangeSlider.SmallStep => Math.Max(1, Step);

    void IRangeSlider.SetValueFromAutomation(int value) => Value = value;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => _peer = new RangeSliderPeer(this, this);

    public int Minimum { get => _min; set { _min = value; Value = _value; InvalidateVisual(); } }
    public int Maximum { get => _max; set { _max = value; Value = _value; InvalidateVisual(); } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            int old = _value;
            _value = v;
            InvalidateVisual();
            _peer?.RaiseValueChanged(old, v);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public EqSlider()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        MinWidth = 36;
        MinHeight = 120;
    }

    private double TrackTop => KnobSize / 2 + 2;
    private double TrackBottom => Bounds.Height - KnobSize / 2 - 2;
    private double YOf(int v) => TrackBottom - (v - _min) * (TrackBottom - TrackTop) / Math.Max(1, _max - _min);

    private int ValueAt(double y)
    {
        double raw = _min + (TrackBottom - y) * (_max - _min) / Math.Max(1, TrackBottom - TrackTop);
        return (int)Math.Round(raw / Step) * Step;
    }

    private static readonly Color LevelLow = Color.Parse("#3DD68C");
    private static readonly Color LevelMid = Color.Parse("#F2D24B");
    private static readonly Color LevelHigh = Color.Parse("#F0503C");

    private static IBrush LevelBrush(double x, double from, double to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(x, from, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(x, to, RelativeUnit.Absolute),
        GradientStops = { new GradientStop(LevelLow, 0), new GradientStop(LevelMid, 0.5), new GradientStop(LevelHigh, 1) }
    };

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double cx = Bounds.Width / 2, tw = 4;
        context.DrawRectangle(Palette.Track, null, new Rect(cx - tw / 2, TrackTop, tw, TrackBottom - TrackTop), tw / 2, tw / 2);

        double yCenter = YOf(Center), yValue = YOf(_value);
        context.DrawLine(CenterPen, new Point(cx - 9, yCenter), new Point(cx + 9, yCenter));
        if (Math.Abs(yValue - yCenter) > 0.5)
            context.DrawRectangle(IsEffectivelyEnabled ? LevelBrush(cx, yCenter, yValue < yCenter ? TrackTop : TrackBottom) : Palette.TextDim, null,
                new Rect(cx - tw / 2, Math.Min(yCenter, yValue), tw, Math.Abs(yValue - yCenter)), tw / 2, tw / 2);

        var knobCenter = new Point(cx, yValue);
        context.DrawEllipse(IsEffectivelyEnabled ? Brushes.White : Palette.TextDim, null, knobCenter, KnobSize / 2, KnobSize / 2);
        if (IsFocused) context.DrawEllipse(null, FocusPen, knobCenter, KnobSize / 2 + 3, KnobSize / 2 + 3);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            Value = Center;
            e.Handled = true;
            return;
        }
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
        Value = ValueAt(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Value = ValueAt(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Value += Math.Sign(e.Delta.Y) * Step;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: case Key.Right: Value += Step; break;
            case Key.Down: case Key.Left: Value -= Step; break;
            case Key.PageUp: Value += Step * 4; break;
            case Key.PageDown: Value -= Step * 4; break;
            case Key.Home: Value = _max; break;
            case Key.End: Value = _min; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty || change.Property == IsFocusedProperty) InvalidateVisual();
    }
}
