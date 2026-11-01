using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double CdSize = 540;
        private const double CdLeft = 400;
        private const double CdDegreesPerSecond = 200;
        private const double CaseSpineWidth = 26;

        private readonly Panel _npCd = new() { Width = CdSize, Height = CdSize };
        private readonly Spinner _npCdSpin = new(CdSize, CdDegreesPerSecond);
        private readonly TranslateTransform _npCdSlide = new();
        private readonly Grid _npCase = new() { Width = StageCover, Height = StageCover, IsHitTestVisible = false };
        private readonly Rotate3DTransform _npCaseFlip = new() { Depth = 1500 };

        private static Path CdRing(double outer, double inner, IBrush? fill) => new()
        {
            Width = CdSize, Height = CdSize, Fill = fill, IsHitTestVisible = false,
            Data = new GeometryGroup
            {
                FillRule = FillRule.EvenOdd,
                Children =
                {
                    new EllipseGeometry(new Rect(CdSize / 2 - outer, CdSize / 2 - outer, outer * 2, outer * 2)),
                    new EllipseGeometry(new Rect(CdSize / 2 - inner, CdSize / 2 - inner, inner * 2, inner * 2))
                }
            }
        };

        private static ConicGradientBrush CdFoil(byte alpha) => new()
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, 216, 218, 224), 0),
                new GradientStop(Color.FromArgb(alpha, 170, 230, 240), 0.12),
                new GradientStop(Color.FromArgb(alpha, 236, 236, 240), 0.25),
                new GradientStop(Color.FromArgb(alpha, 240, 190, 225), 0.37),
                new GradientStop(Color.FromArgb(alpha, 200, 202, 210), 0.5),
                new GradientStop(Color.FromArgb(alpha, 245, 235, 170), 0.62),
                new GradientStop(Color.FromArgb(alpha, 236, 236, 240), 0.75),
                new GradientStop(Color.FromArgb(alpha, 180, 210, 245), 0.87),
                new GradientStop(Color.FromArgb(alpha, 216, 218, 224), 1)
            }
        };

        private void BuildCd()
        {
            double radius = CdSize / 2;
            double hole = radius * 0.125, printStart = radius * 0.33, printEnd = radius * 0.975;
            _npCd.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius), BoxShadow = BoxShadows.Parse("0 10 34 0 #A0000000"), IsHitTestVisible = false
            });
            var disc = _npCdSpin.Target;
            disc.Children.Add(CdRing(radius * 0.29, hole, new SolidColorBrush(Color.FromArgb(70, 230, 236, 240))));
            disc.Children.Add(CdRing(printStart, radius * 0.28, CdFoil(255)));
            disc.Children.Add(CdRing(radius, printEnd - 2, CdFoil(255)));
            var art = CdRing(printEnd, printStart, Palette.Input);
            var old = CdRing(printEnd, printStart, null);
            old.Opacity = 0;
            _npArtShapes.Add((art, old));
            disc.Children.Add(art);
            disc.Children.Add(old);
            disc.Children.Add(new Ellipse
            {
                Width = hole * 2 + 10, Height = hole * 2 + 10, Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            });
            _npCd.Children.Add(new Panel { ClipToBounds = true, IsHitTestVisible = false, Children = { disc } });
            _npCd.Children.Add(CdRing(radius, hole, new ConicGradientBrush
            {
                Angle = 20,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.08),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.18),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.58),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.68),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }
            }));
            _npCd.Children.Add(new Ellipse
            {
                Width = CdSize, Height = CdSize, Stroke = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), StrokeThickness = 1, IsHitTestVisible = false
            });
            _npCd.RenderTransform = _npCdSlide;

            _npCase.RenderTransform = _npCaseFlip;
            _npCase.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.18),
                        new GradientStop(Color.FromArgb(30, 255, 255, 255), 0.3),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.42),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7),
                        new GradientStop(Color.FromArgb(14, 255, 255, 255), 0.78),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.86)
                    }
                }
            });
            _npCase.Children.Add(new Border
            {
                Width = CaseSpineWidth, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5, 0, 0, 5), Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
            });
            foreach (double x in new[] { 9.0, 15.0 })
                _npCase.Children.Add(new Border
                {
                    Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x, 14, 0, 14),
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                });
            foreach (var edge in new[] { VerticalAlignment.Top, VerticalAlignment.Bottom })
                _npCase.Children.Add(new Border
                {
                    Width = CaseSpineWidth - 8, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = edge,
                    Margin = new Thickness(6, 24, 0, 24), CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                });
        }
    }
}
