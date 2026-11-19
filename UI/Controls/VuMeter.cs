using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed class VuMeter : Control
{
    internal const double MeterWidth = 500;
    internal const double MeterHeight = 290;
    private const double FullScaleDb = 3;
    private const double SweepDegrees = 47;
    private const double Frequency = 1.7;
    private const double Damping = 0.78;
    private const double PinnedPosition = 1.06;
    private const double PeakHoldSeconds = 0.7;
    private const double ReferenceDb = 8;
    private const double PeakDb = FullScaleDb - 1;
    private static readonly double[] MajorDb = [-20, -10, -7, -5, -3, -2, -1, 0, 1, 2, 3];
    private static readonly double[] MinorDb = [-15, -8.5, -6, -4, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5];
    private static readonly (double Db, string Text)[] Labels = [(-20, "20"), (-10, "10"), (-7, "7"), (-5, "5"), (-3, "3"), (-2, "2"), (-1, "1"), (0, "0"), (1, "1"), (2, "2"), (3, "3")];
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#2A2118"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#C62D1F"));
    private static readonly IPen InkPen = new Pen(Ink, 2.2);
    private static readonly IPen TickPen = new Pen(Ink, 2);
    private static readonly IPen MinorPen = new Pen(Ink, 1.3);
    private static readonly IPen RedTickPen = new Pen(Red, 2.2);
    private static readonly IPen RedArcPen = new Pen(Red, 7);
    private static readonly IPen NeedlePen = new Pen(new SolidColorBrush(Color.Parse("#1B1612")), 2.6, lineCap: PenLineCap.Round);
    private static readonly IPen NeedleShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 3.4, lineCap: PenLineCap.Round);
    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Title = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private double _position;
    private double _velocity;
    private double _peakFor;

    public string Channel { get; init; } = string.Empty;

    public double Sensitivity { get; set; }

    internal static double PositionFor(double db) => Math.Pow(10, db / 20) / Math.Pow(10, FullScaleDb / 20);

    internal static double DbFor(float level, double sensitivity = 0) => level > 0.00001f ? 20 * Math.Log10(level) + ReferenceDb + sensitivity : -60;

    internal void Feed(float average, double seconds)
    {
        double db = DbFor(average, Sensitivity);
        double target = Math.Min(PinnedPosition + 0.04, PositionFor(db));
        double before = _position;
        double step = Math.Clamp(seconds, 0, 0.1);
        int slices = Math.Max(1, (int)Math.Ceiling(step / 0.008));
        double dt = step / slices;
        double omega = 2 * Math.PI * Frequency;
        for (int i = 0; i < slices; i++)
        {
            double accel = omega * omega * (target - _position) - 2 * Damping * omega * _velocity;
            _velocity += accel * dt;
            _position += _velocity * dt;
            if (_position < 0) { _position = 0; _velocity = 0; }
            if (_position > PinnedPosition) { _position = PinnedPosition; _velocity = Math.Min(0, _velocity); }
        }
        bool peakWasOn = _peakFor > 0;
        _peakFor = _position >= PositionFor(PeakDb) ? PeakHoldSeconds : Math.Max(0, _peakFor - step);
        if (Math.Abs(_position - before) > 0.0005 || peakWasOn != _peakFor > 0) InvalidateVisual();
    }

    internal void Reset()
    {
        _position = _velocity = _peakFor = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double scale = Math.Min(Bounds.Width / MeterWidth, Bounds.Height / MeterHeight);
        using var scaled = context.PushTransform(Matrix.CreateTranslation((Bounds.Width / scale - MeterWidth) / 2, (Bounds.Height / scale - MeterHeight) / 2) *
                                                 Matrix.CreateScale(scale, scale));
        const double w = MeterWidth, h = MeterHeight;
        var outer = new Rect(0, 0, w, h);
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#34343A"), 0), new GradientStop(Color.Parse("#16161A"), 1) }
        }, new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1), outer, 16, 16);

        var face = new Rect(18, 18, w - 36, h - 36);
        using (context.PushClip(new RoundedRect(face, 8)))
        {
            context.DrawRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.9, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.9, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FFF4D2"), 0), new GradientStop(Color.Parse("#F6DE9E"), 0.55), new GradientStop(Color.Parse("#D9B46A"), 1)
                }
            }, null, face);

            var pivot = new Point(w / 2, face.Bottom + 38);
            double radius = face.Height + 8;
            DrawScale(context, pivot, radius - 52);
            DrawText(context, "VU", Title, 30, Ink, new Point(w / 2, face.Bottom - 74));
            if (Channel.Length > 0) DrawText(context, Channel, Face, 15, Ink, new Point(face.Left + 26, face.Bottom - 36));
            context.DrawEllipse(_peakFor > 0 ? new SolidColorBrush(Color.Parse("#FF3B2A")) : new SolidColorBrush(Color.FromArgb(70, 120, 30, 20)),
                new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), new Point(face.Right - 26, face.Bottom - 40), 6, 6);
            DrawText(context, "PEAK", Face, 9, Ink, new Point(face.Right - 26, face.Bottom - 24));

            double angle = (_position * 2 - 1) * SweepDegrees * Math.PI / 180;
            var tip = new Point(pivot.X + Math.Sin(angle) * (radius - 40), pivot.Y - Math.Cos(angle) * (radius - 40));
            context.DrawLine(NeedleShadowPen, pivot + new Vector(3, 3), tip + new Vector(3, 3));
            context.DrawLine(NeedlePen, pivot, tip);

            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#2A2A2F"), 0), new GradientStop(Color.Parse("#141417"), 1) }
            }, null, new Rect(face.Left, face.Bottom - 16, face.Width, 16));
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#1C1C20")), new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1), pivot, 66, 66);

            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(46, 255, 255, 255), 0), new GradientStop(Color.FromArgb(10, 255, 255, 255), 0.45),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)
                }
            }, null, face);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 3), face.Inflate(1.5), 8, 8);
        }
    }

    private static Point OnArc(Point pivot, double radius, double position)
    {
        double angle = (position * 2 - 1) * SweepDegrees * Math.PI / 180;
        return new Point(pivot.X + Math.Sin(angle) * radius, pivot.Y - Math.Cos(angle) * radius);
    }

    private static StreamGeometry Arc(Point pivot, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(OnArc(pivot, radius, from), false);
        g.ArcTo(OnArc(pivot, radius, to), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geometry;
    }

    private static void DrawScale(DrawingContext context, Point pivot, double radius)
    {
        double zero = PositionFor(0);
        context.DrawGeometry(null, InkPen, Arc(pivot, radius, PositionFor(-20), zero));
        context.DrawGeometry(null, RedArcPen, Arc(pivot, radius + 3.5, zero, 1));
        foreach (double db in MajorDb)
        {
            double p = PositionFor(db);
            context.DrawLine(db > 0 ? RedTickPen : TickPen, OnArc(pivot, radius, p), OnArc(pivot, radius + 16, p));
        }
        foreach (double db in MinorDb) context.DrawLine(db > 0 ? RedTickPen : MinorPen, OnArc(pivot, radius, PositionFor(db)), OnArc(pivot, radius + 9, PositionFor(db)));
        foreach (var (db, text) in Labels)
            DrawText(context, text, Face, db is -2 or -1 or 1 or 2 ? 13 : 15, db > 0 ? Red : Ink, OnArc(pivot, radius + 30, PositionFor(db)));
        DrawText(context, "−", Face, 22, Ink, OnArc(pivot, radius + 30, PositionFor(-20) - 0.07));
        DrawText(context, "+", Face, 20, Red, OnArc(pivot, radius + 30, 1.07));
        for (int percent = 0; percent <= 100; percent += 20)
        {
            double p = percent / 100.0 * zero;
            if (percent > 0) context.DrawLine(MinorPen, OnArc(pivot, radius - 8, p), OnArc(pivot, radius, p));
        }
    }

    private static void DrawText(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, Point center)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }
}
