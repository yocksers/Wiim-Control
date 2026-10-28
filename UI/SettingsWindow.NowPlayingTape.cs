using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Path = Avalonia.Controls.Shapes.Path;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double CassetteWidth = 640;
        private const double CassetteHeight = 406;
        private const double CassetteLeft = 520;
        private const double TapeSpeed = 12000;
        private const double SpeedChangeToRetime = 0.05;

        private static readonly IBrush TapeBrush = new SolidColorBrush(Color.Parse("#7A5638"));

        private readonly Canvas _npCassette = new() { Width = CassetteWidth, Height = CassetteHeight };
        private TapeReels? _npCassetteReels;

        private sealed class Spinner(double size, double degreesPerSecond)
        {
            private float _angle;
            private DateTime _start;
            private bool _running;
            private double _speed = degreesPerSecond;

            public Panel Target { get; } = new() { Width = size, Height = size };

            public double Speed
            {
                set
                {
                    if (Math.Abs(value - _speed) < Math.Abs(_speed) * SpeedChangeToRetime) return;
                    bool running = _running;
                    Run(false);
                    _speed = value;
                    Run(running);
                }
            }

            public void Run(bool spin)
            {
                if (spin == _running || ElementComposition.GetElementVisual(Target) is not { } visual) return;
                if (_running) _angle = (float)((_angle + (DateTime.UtcNow - _start).TotalSeconds * _speed * Math.PI / 180) % (2 * Math.PI));
                _running = spin;
                visual.CenterPoint = new Vector3D(size / 2, size / 2, 0);
                visual.StopAnimation("RotationAngle");
                visual.RotationAngle = _angle;
                if (!spin) return;
                _start = DateTime.UtcNow;
                var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
                animation.InsertKeyFrame(0, _angle, new LinearEasing());
                animation.InsertKeyFrame(1, _angle + (_speed < 0 ? -MathF.Tau : MathF.Tau), new LinearEasing());
                animation.Duration = TimeSpan.FromSeconds(360 / Math.Abs(_speed));
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
                visual.StartAnimation("RotationAngle", animation);
            }
        }

        private sealed class TapeReels
        {
            private readonly Point[] _centers;
            private readonly double _core, _full;
            private readonly int _direction;
            private readonly Ellipse[] _packs = [Pack(), Pack()];
            private readonly Spinner[] _spinners;

            public double SupplyRadius { get; private set; }
            public double TakeUpRadius { get; private set; }
            public event Action? Wound;

            public TapeReels(Canvas canvas, Point supply, Point takeUp, double core, double full, int direction, double reelSize, Func<double, Control> reel)
            {
                _centers = [supply, takeUp];
                _core = core;
                _full = full;
                _direction = direction;
                _spinners = [new Spinner(reelSize, direction * TapeSpeed / core), new Spinner(reelSize, direction * TapeSpeed / core)];
                for (int i = 0; i < 2; i++)
                {
                    canvas.Children.Add(_packs[i]);
                    _spinners[i].Target.Children.Add(reel(reelSize));
                    var frame = new Border { Width = reelSize, Height = reelSize, ClipToBounds = true, Child = _spinners[i].Target };
                    canvas.Children.Add(Place(frame, _centers[i].X - reelSize / 2, _centers[i].Y - reelSize / 2));
                }
            }

            private static Ellipse Pack() => new()
            {
                Fill = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.Parse("#3E2C20"), 0.3), new GradientStop(Color.Parse("#22170F"), 1) }
                },
                Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), StrokeThickness = 1
            };

            public void Wind(double progress)
            {
                double area = _full * _full - _core * _core;
                double[] radii = [Math.Sqrt(_core * _core + area * (1 - progress)), Math.Sqrt(_core * _core + area * progress)];
                for (int i = 0; i < 2; i++)
                {
                    _packs[i].Width = _packs[i].Height = radii[i] * 2;
                    Place(_packs[i], _centers[i].X - radii[i], _centers[i].Y - radii[i]);
                    _spinners[i].Speed = _direction * TapeSpeed / radii[i];
                }
                SupplyRadius = radii[0];
                TakeUpRadius = radii[1];
                Wound?.Invoke();
            }

            public void Run(bool spin)
            {
                foreach (var spinner in _spinners) spinner.Run(spin);
            }
        }

        private static StreamGeometry ReelGeometry(double size, double hole, Action<StreamGeometryContext, Point> extra)
        {
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.SetFillRule(FillRule.EvenOdd);
            var center = new Point(size / 2, size / 2);
            Circle(context, center, size / 2);
            Circle(context, center, hole);
            extra(context, center);
            return geometry;
        }

        private static void Circle(StreamGeometryContext context, Point center, double radius)
        {
            context.BeginFigure(new Point(center.X + radius, center.Y), true);
            context.ArcTo(new Point(center.X - radius, center.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            context.ArcTo(new Point(center.X + radius, center.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            context.EndFigure(true);
        }

        private static Point Polar(Point center, double radius, double degrees) =>
            new(center.X + radius * Math.Cos(degrees * Math.PI / 180), center.Y + radius * Math.Sin(degrees * Math.PI / 180));

        private static void Sector(StreamGeometryContext context, Point center, double inner, double outer, double from, double to)
        {
            bool large = to - from > 180;
            context.BeginFigure(Polar(center, outer, from), true);
            context.ArcTo(Polar(center, outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
            context.LineTo(Polar(center, inner, to));
            context.ArcTo(Polar(center, inner, from), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
            context.EndFigure(true);
        }

        private static Control Place(Control control, double left, double top)
        {
            Canvas.SetLeft(control, left);
            Canvas.SetTop(control, top);
            return control;
        }

        private void BuildCassette() => _npCassetteReels = BuildCassetteShell(_npCassette);

        private TapeReels BuildCassetteShell(Canvas canvas)
        {
            canvas.Children.Add(new Border
            {
                Width = CassetteWidth, Height = CassetteHeight, CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), BorderThickness = new Thickness(1),
                BoxShadow = BoxShadows.Parse("0 12 40 0 #C0000000"),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#323338"), 0), new GradientStop(Color.Parse("#1B1C1F"), 1) }
                }
            });
            foreach (var (x, y) in new[] { (20.0, 20.0), (620.0, 20.0), (20.0, 386.0), (620.0, 386.0), (320.0, 302.0) })
                canvas.Children.Add(Place(new Ellipse
                {
                    Width = 14, Height = 14, Fill = new SolidColorBrush(Color.Parse("#111214")),
                    Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1
                }, x - 7, y - 7));

            var strip = new Border
            {
                Height = 64, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.Parse("#F1ECE0")),
                Padding = new Thickness(56, 8, 20, 8)
            };
            var side = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "A", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#F1ECE0")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            var title = new TextBlock
            {
                FontSize = 20, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#1E1E1E")), TextTrimming = TextTrimming.CharacterEllipsis
            };
            var artist = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#4A4A4A")), TextTrimming = TextTrimming.CharacterEllipsis };
            _npTitleCopies.Add(title);
            _npArtistCopies.Add(artist);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, artist } };
            var stripRow = new DockPanel { Children = { side, names } };
            DockPanel.SetDock(side, Dock.Left);
            strip.Child = stripRow;
            var art = new Rectangle { Width = 560, Height = 260, Fill = Palette.Input };
            var old = new Rectangle { Width = 560, Height = 260, Opacity = 0, IsHitTestVisible = false };
            _npArtShapes.Add((art, old));
            var label = new Border
            {
                Width = 560, Height = 260, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Child = new Panel
                {
                    Children =
                    {
                        art, old,
                        new Border { Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)) },
                        strip
                    }
                }
            };
            canvas.Children.Add(Place(label, 40, 26));

            var window = new Canvas { Width = 432, Height = 120 };
            var reels = new TapeReels(window, new Point(80, 60), new Point(352, 60), 40, 116, -1, 80, CassetteHub);
            reels.Wind(0.5);
            window.Children.Add(new Border
            {
                Width = 432, Height = 120, IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(46, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5),
                        new GradientStop(Color.FromArgb(18, 255, 255, 255), 1)
                    }
                }
            });
            canvas.Children.Add(Place(new Border
            {
                Width = 432, Height = 120, CornerRadius = new CornerRadius(60), ClipToBounds = true,
                Background = new SolidColorBrush(Color.Parse("#16110D")), Child = window
            }, 104, 108));
            canvas.Children.Add(Place(new Border
            {
                Width = 432, Height = 120, CornerRadius = new CornerRadius(60), IsHitTestVisible = false,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(2)
            }, 104, 108));

            var headArea = new Path
            {
                Data = Geometry.Parse("M 140,406 L 178,316 L 462,316 L 500,406 Z"), Fill = new SolidColorBrush(Color.Parse("#2A2B30")),
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1
            };
            canvas.Children.Add(headArea);
            canvas.Children.Add(Place(new Rectangle { Width = 330, Height = 5, Fill = TapeBrush }, 155, 396));
            foreach (var x in new[] { 214.0, 426.0 })
                canvas.Children.Add(Place(new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.Parse("#0C0C0E")) }, x - 11, 350));
            foreach (var x in new[] { 258.0, 296.0, 344.0, 382.0 })
                canvas.Children.Add(Place(new Rectangle
                {
                    Width = x is 296 or 344 ? 30 : 14, Height = 22, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.Parse("#0C0C0E"))
                }, x - (x is 296 or 344 ? 15 : 7), 362));
            return reels;
        }

        private static Control CassetteHub(double size)
        {
            var white = new SolidColorBrush(Color.Parse("#E9E5DA"));
            var hub = new Panel { Width = size, Height = size };
            hub.Children.Add(new Path
            {
                Data = ReelGeometry(size, size * 0.28, (context, center) => Sector(context, center, size * 0.42, size * 0.5, -8, 8)),
                Fill = white, Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), StrokeThickness = 1
            });
            var teeth = new StreamGeometry();
            using (var context = teeth.Open())
            {
                var center = new Point(size / 2, size / 2);
                for (int i = 0; i < 6; i++)
                {
                    double a = i * 60;
                    context.BeginFigure(Polar(center, size * 0.29, a - 9), true);
                    context.LineTo(Polar(center, size * 0.29, a + 9));
                    context.LineTo(Polar(center, size * 0.17, a + 6));
                    context.LineTo(Polar(center, size * 0.17, a - 6));
                    context.EndFigure(true);
                }
            }
            hub.Children.Add(new Path { Data = teeth, Fill = white });
            return hub;
        }

        private Control MetalReel(double size)
        {
            var reel = new Canvas { Width = size, Height = size };
            double r = size / 2;
            var art = new Ellipse { Width = r + 6, Height = r + 6, Fill = Palette.Input };
            var old = new Ellipse { Width = r + 6, Height = r + 6, Opacity = 0, IsHitTestVisible = false };
            _npArtShapes.Add((art, old));
            reel.Children.Add(Place(art, r / 2 - 3, r / 2 - 3));
            reel.Children.Add(Place(old, r / 2 - 3, r / 2 - 3));
            reel.Children.Add(new Path
            {
                Data = ReelGeometry(size, r * 0.5, (context, center) =>
                {
                    foreach (var a in new[] { -90.0, 30.0, 150.0 }) Sector(context, center, r * 0.56, r * 0.9, a - 42, a + 42);
                }),
                Fill = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.Parse("#D3D7DB"), 0), new GradientStop(Color.Parse("#9EA4AA"), 1) }
                },
                Stroke = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), StrokeThickness = 1
            });
            reel.Children.Add(Place(new Ellipse
            {
                Width = 32, Height = 32, Fill = new SolidColorBrush(Color.Parse("#2A2B2E")),
                Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), StrokeThickness = 1
            }, r - 16, r - 16));
            foreach (var a in new[] { -90.0, 30.0, 150.0 })
            {
                var tab = Polar(new Point(r, r), 9, a);
                reel.Children.Add(Place(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.Parse("#C8CCD0")) }, tab.X - 3, tab.Y - 3));
            }
            return reel;
        }
    }
}
