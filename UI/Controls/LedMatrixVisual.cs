using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed class LedMatrixVisual : Control
{
    private const int BandCount = SpectrumEngine.BandCount;
    private const int Rows = 28;
    private const double Padding = 16;
    private const double DotMargin = 10;
    private const double DotSize = 0.36;
    private const double PeakMinimum = 0.03;

    private static readonly IBrush Unlit = new SolidColorBrush(Color.FromArgb(26, 240, 255, 250));
    private static readonly IBrush[] RowBrushes = Enumerable.Range(0, Rows)
        .Select(row => (IBrush)new SolidColorBrush(VisualTheme.GradientAt(row / (double)(Rows - 1)))).ToArray();

    private readonly SpectrumEngine _engine = new();
    private bool _showBackground = true;

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
        double cellWidth = (area.Width - DotMargin * 2) / BandCount;
        double cellHeight = (area.Height - DotMargin * 2) / Rows;
        double radius = Math.Max(1, Math.Min(cellWidth, cellHeight) * DotSize);
        double bottom = area.Bottom - DotMargin;
        var levels = _engine.Levels;
        var peaks = _engine.Peaks;
        for (int col = 0; col < BandCount; col++)
        {
            double x = area.Left + DotMargin + (col + 0.5) * cellWidth;
            int lit = Math.Min((int)Math.Round(levels[col] * Rows, MidpointRounding.ToEven), Rows);
            int peakRow = Math.Min((int)(peaks[col] * Rows), Rows - 1);
            bool showPeak = peakRow >= lit && peaks[col] > PeakMinimum;
            for (int row = 0; row < Rows; row++)
            {
                var brush = row < lit || (showPeak && row == peakRow) ? RowBrushes[row] : Unlit;
                context.DrawEllipse(brush, null, new Point(x, bottom - (row + 0.5) * cellHeight), radius, radius);
            }
        }
    }
}
