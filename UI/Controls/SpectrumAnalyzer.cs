using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed class SpectrumAnalyzer : Control
{
    internal const int SampleCount = SpectrumEngine.SampleCount;
    private const int BandCount = SpectrumEngine.BandCount;
    private const double Padding = 16;
    private const double BarMargin = 8;
    private const double Gap = 3;
    private const double PeakLine = 3;

    private readonly SpectrumEngine _engine = new();
    private bool _showBackground = true;

    public string Label { get; init; } = string.Empty;

    public bool ShowBackground
    {
        get => _showBackground;
        set { _showBackground = value; InvalidateVisual(); }
    }

    internal void Feed(float[] samples, int sampleRate, double seconds)
    {
        if (_engine.Feed(samples, sampleRate, seconds)) InvalidateVisual();
    }

    internal void Reset()
    {
        _engine.Reset();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (_showBackground) context.DrawRectangle(new SolidColorBrush(VisualTheme.Background), null, new Rect(0, 0, w, h), 6, 6);
        var area = new Rect(Padding, Padding, w - Padding * 2, h - Padding * 2);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, area.Bottom, RelativeUnit.Absolute), EndPoint = new RelativePoint(0, area.Top, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(VisualTheme.GradientStop(0), 0), new GradientStop(VisualTheme.GradientStop(1), 0.5), new GradientStop(VisualTheme.GradientStop(2), 1)
            }
        };
        if (Label.Length > 0)
        {
            var text = new FormattedText(Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 18, new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
            context.DrawText(text, new Point(Padding + BarMargin, Padding));
        }
        var levels = _engine.Levels;
        var peaks = _engine.Peaks;
        double barWidth = Math.Max(2, (area.Width - BarMargin * 2) / BandCount - Gap);
        double baseline = area.Bottom - BarMargin;
        double maxHeight = area.Height - BarMargin * 2;
        double x = area.Left + BarMargin;
        for (int i = 0; i < BandCount; i++)
        {
            double barHeight = Math.Max(2, levels[i] * maxHeight);
            context.DrawRectangle(brush, null, new Rect(x, baseline - barHeight, barWidth, barHeight), 2, 2);
            double peakHeight = Math.Max(barHeight, peaks[i] * maxHeight);
            context.DrawRectangle(brush, null, new Rect(x, baseline - peakHeight - 2 - PeakLine, barWidth, PeakLine), 1, 1);
            x += barWidth + Gap;
        }
    }
}
