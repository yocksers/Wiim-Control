using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WiimControl;

sealed class PeqGraph : Control
{
    public const double MinFrequency = 10, MaxFrequency = 22000, MinGain = -12, MaxGain = 12, MinQ = 0.01, MaxQ = 24;
    private const double SampleRate = 48000;
    private const double Left = 44, Right = 12, Top = 14, Bottom = 26, PointRadius = 11;
    private const double FullMin = 10, FullMax = 24000, MinZoomRatio = 3, ZoomStep = 0.8, PanStep = 0.1;
    private static readonly IPen GridPen = new Pen(Palette.Input, 1);
    private static readonly IPen MinorPen = new Pen(Palette.DropDown, 1);
    private static readonly IPen ZeroPen = new Pen(Palette.GraphZero, 1);
    private static readonly IPen CurvePen = new Pen(Palette.Text, 2);
    private static readonly IPen CurveDimPen = new Pen(Palette.TextDim, 2);
    private static readonly IPen BandPen = new Pen(Palette.AccentHover, 1.5);
    private static readonly IPen PointPen = new Pen(Palette.TextMuted, 1.5);

    private PeqBand[] _bands = [];
    private int _selected = -1;
    private int _dragging = -1;
    private double _axisMin = FullMin, _axisMax = FullMax;

    public event Action<int, PeqBand>? BandChanged;
    public event Action<int>? SelectedChanged;

    public PeqGraph()
    {
        Focusable = false;
        MinHeight = 120;
        ClipToBounds = true;
    }

    public PeqBand[] Bands
    {
        get => _bands;
        set { _bands = value; InvalidateVisual(); }
    }

    public bool Editable { get; set; } = true;

    public void ResetZoom()
    {
        (_axisMin, _axisMax) = (FullMin, FullMax);
        InvalidateVisual();
    }

    internal (double Min, double Max) VisibleRange => (_axisMin, _axisMax);

    internal void ZoomAt(double frequency, double factor)
    {
        double low = Math.Log10(_axisMin), high = Math.Log10(_axisMax), anchor = Math.Log10(frequency);
        double span = Math.Clamp((high - low) * factor, Math.Log10(MinZoomRatio), Math.Log10(FullMax / FullMin));
        double newLow = anchor - (anchor - low) / (high - low) * span;
        ShowSpan(newLow, newLow + span);
    }

    internal void Pan(int direction)
    {
        double low = Math.Log10(_axisMin), high = Math.Log10(_axisMax), shift = (high - low) * PanStep * direction;
        ShowSpan(low + shift, high + shift);
    }

    private void ShowSpan(double low, double high)
    {
        double span = high - low, fullLow = Math.Log10(FullMin), fullHigh = Math.Log10(FullMax);
        if (low < fullLow) (low, high) = (fullLow, fullLow + span);
        if (high > fullHigh) (low, high) = (fullHigh - span, fullHigh);
        (_axisMin, _axisMax) = (Math.Pow(10, Math.Max(low, fullLow)), Math.Pow(10, Math.Min(high, fullHigh)));
        InvalidateVisual();
    }

    public int Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    private Rect Plot => new(Left, Top, Math.Max(1, Bounds.Width - Left - Right), Math.Max(1, Bounds.Height - Top - Bottom));

    private double XOf(double f, Rect plot) =>
        plot.Left + Math.Log(f / _axisMin) / Math.Log(_axisMax / _axisMin) * plot.Width;

    private double FrequencyAt(double x, Rect plot) =>
        _axisMin * Math.Pow(_axisMax / _axisMin, (x - plot.Left) / plot.Width);

    private bool Visible(double f) => f >= _axisMin && f <= _axisMax;

    private IEnumerable<(double Frequency, bool Labelled)> Ticks()
    {
        double decades = Math.Log10(_axisMax / _axisMin);
        int[] labelled = decades <= 1.2 ? [1, 2, 3, 4, 5, 6, 7, 8, 9] : decades <= 2 ? [1, 2, 3, 5] : [1, 2, 5];
        for (double decade = Math.Pow(10, Math.Floor(Math.Log10(_axisMin))); decade <= _axisMax; decade *= 10)
            for (int m = 1; m <= 9; m++)
            {
                double f = decade * m;
                if (f >= _axisMin && f <= _axisMax) yield return (f, labelled.Contains(m));
            }
    }

    private static double YOf(double db, Rect plot) =>
        plot.Top + (MaxGain - Math.Clamp(db, MinGain - 1, MaxGain + 1)) / (MaxGain - MinGain) * plot.Height;

    private static double GainAt(double y, Rect plot) =>
        MaxGain - (y - plot.Top) / plot.Height * (MaxGain - MinGain);

    public static double ResponseDb(PeqBand band, double frequency)
    {
        if (band.Filter == PeqFilter.Off || band.Gain == 0) return 0;
        double a = Math.Pow(10, band.Gain / 40);
        double w0 = 2 * Math.PI * Math.Min(band.Frequency, SampleRate * 0.49) / SampleRate;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * Math.Max(band.Q, MinQ));
        double b0, b1, b2, a0, a1, a2;
        switch (band.Filter)
        {
            case PeqFilter.LowShelf:
            {
                double s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + s);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - s);
                a0 = (a + 1) + (a - 1) * cos + s;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - s;
                break;
            }
            case PeqFilter.HighShelf:
            {
                double s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + s);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - s);
                a0 = (a + 1) - (a - 1) * cos + s;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - s;
                break;
            }
            default:
                b0 = 1 + alpha * a;
                b1 = -2 * cos;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2 * cos;
                a2 = 1 - alpha / a;
                break;
        }
        double w = 2 * Math.PI * Math.Min(frequency, SampleRate / 2) / SampleRate;
        var z1 = Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var h = (b0 + b1 * z1 + b2 * z2) / (a0 + a1 * z1 + a2 * z2);
        return 20 * Math.Log10(Math.Max(h.Magnitude, 1e-9));
    }

    private static FormattedText Label(string text, IBrush brush, double size = 11) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    private static string FrequencyLabel(double f) => f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}";

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var plot = Plot;
        bool enabled = IsEffectivelyEnabled;

        var ticks = Ticks().ToList();
        foreach (var (f, _) in ticks.Where(t => !t.Labelled))
        {
            double x = XOf(f, plot);
            context.DrawLine(MinorPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        foreach (var (f, _) in ticks.Where(t => t.Labelled))
        {
            double x = XOf(f, plot);
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Label(FrequencyLabel(f), Palette.TextMuted);
            context.DrawText(label, new Point(Math.Min(x - label.Width / 2, Bounds.Width - label.Width - 2), plot.Bottom + 6));
        }
        foreach (var db in new double[] { -12, -6, 0, 6, 12 })
        {
            double y = YOf(db, plot);
            context.DrawLine(db == 0 ? ZeroPen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Label(db > 0 ? $"+{db:0}" : $"{db:0}", Palette.TextMuted);
            context.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));
        }
        if (_bands.Length == 0) return;

        if (_selected >= 0 && _selected < _bands.Length && _bands[_selected].Filter != PeqFilter.Off && enabled)
            context.DrawGeometry(null, BandPen, Curve(plot, f => ResponseDb(_bands[_selected], f)));
        context.DrawGeometry(null, enabled ? CurvePen : CurveDimPen, Curve(plot, f => _bands.Sum(b => ResponseDb(b, f))));

        foreach (int i in Enumerable.Range(0, _bands.Length).OrderBy(i => i == _selected))
        {
            var band = _bands[i];
            if (band.Filter == PeqFilter.Off || !Visible(band.Frequency)) continue;
            var center = new Point(XOf(band.Frequency, plot), YOf(band.Gain, plot));
            bool selected = i == _selected && enabled;
            context.DrawEllipse(selected ? Palette.Accent : Palette.Input, selected ? null : PointPen, center, PointRadius, PointRadius);
            var number = Label((i + 1).ToString(CultureInfo.InvariantCulture), enabled ? Palette.Text : Palette.TextDim, 11);
            context.DrawText(number, new Point(center.X - number.Width / 2, center.Y - number.Height / 2));
        }
    }

    private StreamGeometry Curve(Rect plot, Func<double, double> response)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        bool first = true;
        for (double x = plot.Left; x <= plot.Right; x += 2)
        {
            var p = new Point(x, YOf(response(FrequencyAt(x, plot)), plot));
            if (first) ctx.BeginFigure(p, false);
            else ctx.LineTo(p);
            first = false;
        }
        ctx.EndFigure(false);
        return geometry;
    }

    private int PointAt(Point position)
    {
        var plot = Plot;
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < _bands.Length; i++)
        {
            if (_bands[i].Filter == PeqFilter.Off || !Visible(_bands[i].Frequency)) continue;
            double dx = XOf(_bands[i].Frequency, plot) - position.X, dy = YOf(_bands[i].Gain, plot) - position.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > PointRadius + 4) continue;
            if (distance < bestDistance || (i == _selected && distance <= bestDistance + 2))
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Select(int index)
    {
        if (_selected == index) return;
        Selected = index;
        SelectedChanged?.Invoke(index);
    }

    private void Change(int index, PeqBand band)
    {
        _bands[index] = band;
        InvalidateVisual();
        BandChanged?.Invoke(index, band);
    }

    public static double RoundFrequency(double f)
    {
        f = Math.Clamp(f, MinFrequency, MaxFrequency);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(f)) - 2);
        return Math.Round(f / magnitude) * magnitude;
    }

    public static double NudgeQ(double q, int direction)
    {
        double step = q < 1 || (q == 1 && direction < 0) ? 0.05 : q < 4 || (q == 4 && direction < 0) ? 0.1 : 0.5;
        return Math.Round(Math.Clamp(q + step * direction, MinQ, MaxQ), 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int index = Editable ? PointAt(e.GetPosition(this)) : -1;
        if (index < 0)
        {
            if (e.ClickCount == 2) ResetZoom();
            return;
        }
        Select(index);
        if (e.ClickCount == 2)
        {
            Change(index, _bands[index] with { Gain = 0 });
        }
        else
        {
            _dragging = index;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_dragging < 0)
        {
            Cursor = Editable && PointAt(position) >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            return;
        }
        var plot = Plot;
        double frequency = RoundFrequency(FrequencyAt(Math.Clamp(position.X, plot.Left, plot.Right), plot));
        double gain = Math.Round(Math.Clamp(GainAt(position.Y, plot), MinGain, MaxGain), 1);
        var band = _bands[_dragging];
        if (band.Frequency != frequency || band.Gain != gain) Change(_dragging, band with { Frequency = frequency, Gain = gain });
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : -e.Delta.X;
        if (delta == 0) return;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0 || e.Delta.Y == 0) Pan(-Math.Sign(delta));
        else ZoomAt(FrequencyAt(Math.Clamp(e.GetPosition(this).X, Plot.Left, Plot.Right), Plot), Math.Pow(ZoomStep, delta));
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }
}
