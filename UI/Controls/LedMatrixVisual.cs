using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace WiimControl;

sealed class LedMatrixVisual : Control
{
    private const int BandCount = SpectrumEngine.BandCount;
    private const int Rows = 28;
    private const double Padding = 16;
    private const double DotMargin = 10;
    private const double DotSize = 0.36;
    private const double PeakMinimum = 0.03;

    private static readonly SKColor Unlit = new(240, 255, 250, 26);
    private static readonly SKColor[] RowColors = Enumerable.Range(0, Rows)
        .Select(row => Sk(VisualTheme.GradientAt(row / (double)(Rows - 1)))).ToArray();

    private readonly SpectrumEngine _engine = new();
    private bool _showBackground = true;

    public bool ShowBackground
    {
        get => _showBackground;
        set { _showBackground = value; InvalidateVisual(); }
    }

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

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
        var levels = _engine.Levels;
        var peaks = _engine.Peaks;
        var lit = new int[BandCount];
        var peakRows = new int[BandCount];
        for (int col = 0; col < BandCount; col++)
        {
            lit[col] = Math.Min((int)Math.Round(levels[col] * Rows, MidpointRounding.ToEven), Rows);
            int peakRow = Math.Min((int)(peaks[col] * Rows), Rows - 1);
            peakRows[col] = peakRow >= lit[col] && peaks[col] > PeakMinimum ? peakRow : -1;
        }
        context.Custom(new MatrixFrame(new Rect(0, 0, w, h), lit, peakRows, _showBackground ? Sk(VisualTheme.Background) : null));
    }

    private sealed class MatrixFrame(Rect bounds, int[] lit, int[] peakRows, SKColor? background) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            float w = (float)bounds.Width, h = (float)bounds.Height;
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            if (background is { } fill)
            {
                paint.Color = fill;
                canvas.DrawRoundRect(new SKRect(0, 0, w, h), 6, 6, paint);
            }
            float left = (float)Padding, top = (float)Padding, areaWidth = w - (float)Padding * 2, areaHeight = h - (float)Padding * 2;
            float margin = (float)DotMargin;
            float cellWidth = (areaWidth - margin * 2) / BandCount;
            float cellHeight = (areaHeight - margin * 2) / Rows;
            float radius = Math.Max(1, Math.Min(cellWidth, cellHeight) * (float)DotSize);
            float bottom = top + areaHeight - margin;
            for (int row = 0; row < Rows; row++)
            {
                float y = bottom - (row + 0.5f) * cellHeight;
                for (int col = 0; col < BandCount; col++)
                {
                    float x = left + margin + (col + 0.5f) * cellWidth;
                    paint.Color = row < lit[col] || row == peakRows[col] ? RowColors[row] : Unlit;
                    canvas.DrawCircle(x, y, radius, paint);
                }
            }
        }
    }
}
