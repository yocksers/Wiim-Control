using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WiimControl;

sealed class LevelSlider : Control
{
    private static readonly IPen FocusPen = new Pen(Palette.AccentHover, 2);
    private const double KnobSize = 14;
    private int _min, _max = 100, _value;
    private bool _dragging, _muted;

    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    public LevelSlider()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        MinWidth = 160;
        Height = 28;
    }

    public int Minimum { get => _min; set { _min = value; Value = _value; InvalidateVisual(); } }
    public int Maximum { get => _max; set { _max = value; Value = _value; InvalidateVisual(); } }
    public bool FillFromCenter { get; set; }
    public bool IsDragging => _dragging;

    public bool Muted
    {
        get => _muted;
        set { _muted = value; InvalidateVisual(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private double TrackLeft => KnobSize / 2 + 2;
    private double TrackRight => Bounds.Width - KnobSize / 2 - 2;
    private double XOf(int v) => TrackLeft + (v - _min) * (TrackRight - TrackLeft) / Math.Max(1, _max - _min);
    private int ValueAt(double x) => _min + (int)Math.Round((x - TrackLeft) * (_max - _min) / Math.Max(1, TrackRight - TrackLeft));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double cy = Bounds.Height / 2, th = 4;
        context.DrawRectangle(Palette.Track, null, new Rect(TrackLeft, cy - th / 2, Math.Max(0, TrackRight - TrackLeft), th), th / 2, th / 2);

        double x = XOf(_value);
        double start = FillFromCenter ? XOf((_min + _max) / 2) : TrackLeft;
        if (Math.Abs(x - start) > 0.5)
            context.DrawRectangle(!IsEffectivelyEnabled || _muted ? Palette.TextDim : Palette.Fill, null,
                new Rect(Math.Min(start, x), cy - th / 2, Math.Abs(x - start), th), th / 2, th / 2);

        var knobCenter = new Point(x, cy);
        context.DrawEllipse(IsEffectivelyEnabled ? Brushes.White : Palette.TextDim, null, knobCenter, KnobSize / 2, KnobSize / 2);
        if (IsFocused) context.DrawEllipse(null, FocusPen, knobCenter, KnobSize / 2 + 3, KnobSize / 2 + 3);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
        Value = ValueAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Value = ValueAt(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasDragging = _dragging;
        _dragging = false;
        e.Pointer.Capture(null);
        if (wasDragging) ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int before = _value;
        Value += Math.Sign(e.Delta.Y) * 2;
        e.Handled = true;
        if (_value != before) ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int before = _value;
        switch (e.Key)
        {
            case Key.Up: case Key.Right: Value += 1; break;
            case Key.Down: case Key.Left: Value -= 1; break;
            case Key.PageUp: Value += 10; break;
            case Key.PageDown: Value -= 10; break;
            case Key.Home: Value = _min; break;
            case Key.End: Value = _max; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        if (_value != before) ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty || change.Property == IsFocusedProperty) InvalidateVisual();
    }
}
