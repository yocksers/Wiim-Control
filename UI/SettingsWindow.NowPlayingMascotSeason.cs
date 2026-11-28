using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int SnowFlakes = 30;
        private const double SnowTop = -120;
        private const double SnowBottom = StageCover + 10;
        private const int FireworkRockets = 4;
        private const int FireworkSparks = 18;

        private readonly Canvas _santaHat = new() { Width = 124, Height = 76, IsVisible = false, RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative), RenderTransform = new RotateTransform(-8) };
        private const double PartyHatWidth = 72;
        private const double PartyHatHeight = 92;
        private readonly Canvas _partyHat = new() { Width = PartyHatWidth, Height = PartyHatHeight, IsVisible = false, RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative), RenderTransform = new RotateTransform(12) };
        private readonly Canvas _snowLayer = new() { IsHitTestVisible = false, IsVisible = false, ZIndex = 5 };
        private readonly List<(Ellipse Flake, double Speed, double Phase, double Sway)> _snow = [];
        private readonly FrameTimer _mascotAmbientTimer = new();
        private DateTime _mascotAmbientLast = DateTime.UtcNow;
        private DateTime _mascotSeasonCheckedAt = DateTime.MinValue;
        private bool _mascotChristmas;
        private bool _mascotNewYearsEve;

        private readonly List<(Rectangle Rocket, Line Trail)> _rockets = [];
        private readonly List<Ellipse> _sparks = [];
        private readonly TextBlock _glyphNewYear = new() { Text = "Happy New Year!", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#E8B829")), IsVisible = false };
        private readonly Ellipse _matchFlame = new() { Width = 12, Height = 16, IsVisible = false, Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#FFF3B0"), 0), new GradientStop(Color.Parse("#FF8A1E"), 0.6), new GradientStop(Color.FromArgb(0, 255, 90, 0), 1) } } };
        private Control[] _seasonProps = [];

        private void CheckMascotSeason(bool force = false)
        {
            if (!force && DateTime.UtcNow - _mascotSeasonCheckedAt < TimeSpan.FromSeconds(30)) return;
            _mascotSeasonCheckedAt = DateTime.UtcNow;
            var now = DateTime.Now;
            bool holidays = _owner._mascotHolidays;
            _mascotChristmas = holidays && now is { Month: 12, Day: >= 1 and <= 30 };
            _mascotNewYearsEve = holidays && now is { Month: 12, Day: 31 };
        }

        internal void OnMascotHolidaysChanged()
        {
            CheckMascotSeason(force: true);
            ApplySeasonLook();
            UpdateMascotTimer();
        }

        private void BuildMascotSeason(Canvas figure)
        {
            var red = new SolidColorBrush(Color.Parse("#C62828"));
            var white = new SolidColorBrush(Color.Parse("#F4F4F2"));
            _santaHat.Children.Add(new Polygon
            {
                Points = [new Point(14, 60), new Point(104, 60), new Point(92, 30), new Point(110, 46), new Point(118, 40), new Point(76, 2), new Point(40, 10)], Fill = red
            });
            _santaHat.Children.Add(Place(new Rectangle { Width = 116, Height = 22, RadiusX = 11, RadiusY = 11, Fill = white }, 2, 54));
            _santaHat.Children.Add(Place(new Ellipse { Width = 24, Height = 24, Fill = white }, 104, 30));
            figure.Children.Add(Place(_santaHat, 38, -38));
            BuildPartyHat();
            figure.Children.Add(Place(_partyHat, 100 - PartyHatWidth / 2, 34 - PartyHatHeight));

            for (int i = 0; i < SnowFlakes; i++)
            {
                double size = 3 + Random.Shared.NextDouble() * 5;
                var flake = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(120 + Random.Shared.Next(110)), 255, 255, 255)) };
                Canvas.SetLeft(flake, Random.Shared.NextDouble() * MascotStageWidth);
                Canvas.SetTop(flake, SnowTop + Random.Shared.NextDouble() * (SnowBottom - SnowTop));
                _snow.Add((flake, 28 + Random.Shared.NextDouble() * 40, Random.Shared.NextDouble() * Math.PI * 2, 10 + Random.Shared.NextDouble() * 18));
                _snowLayer.Children.Add(flake);
            }
            _npStage.Children.Add(_snowLayer);

            var colors = new[] { "#FF5A5F", "#FFD166", "#06D6A0", "#4CC9F0", "#C77DFF", "#FFFFFF" };
            for (int i = 0; i < FireworkRockets; i++)
            {
                var rocket = new Rectangle { Width = 6, Height = 18, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.Parse("#E0E0E0")), IsVisible = false };
                var trail = new Line { Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 200, 120)), StrokeThickness = 3, StrokeLineCap = PenLineCap.Round, IsVisible = false };
                _rockets.Add((rocket, trail));
                _mascotFx.Children.Add(trail);
                _mascotFx.Children.Add(rocket);
                var color = Color.Parse(colors[i % colors.Length]);
                for (int j = 0; j < FireworkSparks; j++)
                {
                    var spark = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(j % 4 == 0 ? Colors.White : color), IsVisible = false };
                    _sparks.Add(spark);
                    _mascotFx.Children.Add(spark);
                }
            }
            figure.Children.Add(_matchFlame);
            _mascotGlyphs.Children.Add(_glyphNewYear);
            _seasonProps = [_glyphNewYear, _matchFlame, .. _sparks, .. _rockets.Select(r => (Control)r.Rocket), .. _rockets.Select(r => (Control)r.Trail)];

            _mascotAmbientTimer.Host = _npOverlay;
            _mascotAmbientTimer.Tick += (_, _) => StepMascotAmbient();
            CheckMascotSeason(force: true);
        }

        private void BuildPartyHat()
        {
            const double top = 10, w = PartyHatWidth, h = PartyHatHeight;
            double Half(double y) => w / 2 * (y - top) / (h - top);
            Point[] Band(double y1, double y2) => [new Point(w / 2 - Half(y1), y1), new Point(w / 2 + Half(y1), y1), new Point(w / 2 + Half(y2), y2), new Point(w / 2 - Half(y2), y2)];
            _partyHat.Children.Add(new Polygon { Points = [new Point(w / 2, top), new Point(w, h), new Point(0, h)], Fill = new SolidColorBrush(Color.Parse("#5B5BF6")) });
            var stripe = new SolidColorBrush(Color.Parse("#FFD166"));
            foreach (double y in new[] { 30.0, 52.0, 74.0 })
                _partyHat.Children.Add(new Polygon { Points = Band(y, y + 10), Fill = stripe });
            var dot = new SolidColorBrush(Color.Parse("#FF5A5F"));
            foreach (var (x, y) in new[] { (36.0, 44.0), (26.0, 66.0), (46.0, 66.0), (36.0, 87.0) })
                _partyHat.Children.Add(Place(new Ellipse { Width = 6, Height = 6, Fill = dot }, x - 3, y - 3));
            foreach (double angle in new[] { -60.0, -20.0, 20.0, 60.0 })
            {
                double rad = (angle - 90) * Math.PI / 180;
                _partyHat.Children.Add(new Line
                {
                    StartPoint = new Point(w / 2, top), EndPoint = new Point(w / 2 + Math.Cos(rad) * 14, top + Math.Sin(rad) * 14),
                    Stroke = new SolidColorBrush(Color.Parse(angle < 0 ? "#06D6A0" : "#FF5A5F")), StrokeThickness = 3, StrokeLineCap = PenLineCap.Round
                });
            }
            _partyHat.Children.Add(Place(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.Parse("#F4F4F2")) }, w / 2 - 7, top - 7));
        }

        private void HideMascotSeason() => HideAll(_seasonProps);

        private void UpdateMascotAmbient(bool shown)
        {
            CheckMascotSeason();
            bool snowing = shown && _mascotChristmas;
            if (_snowLayer.IsVisible != snowing) _snowLayer.IsVisible = snowing;
            bool run = snowing;
            if (run && !_mascotAmbientTimer.IsEnabled)
            {
                _mascotAmbientLast = DateTime.UtcNow;
                _mascotAmbientTimer.Start();
            }
            else if (!run && _mascotAmbientTimer.IsEnabled) _mascotAmbientTimer.Stop();
        }

        private void StepMascotAmbient()
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.1, (now - _mascotAmbientLast).TotalSeconds);
            _mascotAmbientLast = now;
            CheckMascotSeason();
            if (_snowLayer.IsVisible != _mascotChristmas) _snowLayer.IsVisible = _mascotChristmas;
            if (_mascotChristmas)
            {
                double t = (now - DateTime.UnixEpoch).TotalSeconds;
                foreach (var (flake, speed, phase, sway) in _snow)
                {
                    double y = Canvas.GetTop(flake) + speed * dt;
                    double x = Canvas.GetLeft(flake) + Math.Sin(t * 0.9 + phase) * sway * dt;
                    if (y > SnowBottom)
                    {
                        y = SnowTop;
                        x = Random.Shared.NextDouble() * MascotStageWidth;
                    }
                    Canvas.SetTop(flake, y);
                    Canvas.SetLeft(flake, x);
                }
            }
            if (!_mascotChristmas) _mascotAmbientTimer.Stop();
        }

        private void ApplySeasonLook()
        {
            CheckMascotSeason();
            if (_santaHat.IsVisible != _mascotChristmas) _santaHat.IsVisible = _mascotChristmas;
            if (_partyHat.IsVisible != _mascotNewYearsEve) _partyHat.IsVisible = _mascotNewYearsEve;
        }

        private const double FireworksSeconds = 8.4;
        private static readonly double[] FireworkLaunches = [0.9, 2.3, 3.6, 4.6];

        private double StepMascotFireworks(double s)
        {
            double side = MascotSide;
            double coverEdge = side > 0 ? MascotCoverLeft : MascotCoverLeft + StageCover;
            double[] across = [170, 330, 460, 260];
            double[] heights = [150, 110, 190, 70];
            bool cheering = false;
            for (int i = 0; i < _rockets.Count; i++)
            {
                var (rocket, trail) = _rockets[i];
                double launch = FireworkLaunches[i];
                double x = coverEdge + side * across[i];
                double rise = Phase(s, launch, 0.75);
                if (s >= launch && rise < 1)
                {
                    double y = StageCover - (StageCover - heights[i]) * (1 - Math.Pow(1 - rise, 2));
                    Canvas.SetLeft(rocket, x - 3);
                    Canvas.SetTop(rocket, y - 9);
                    rocket.IsVisible = true;
                    trail.StartPoint = new Point(x, y + 8);
                    trail.EndPoint = new Point(x + Math.Sin(s * 30) * 2, Math.Min(StageCover, y + 70));
                    trail.IsVisible = true;
                }
                double burst = Phase(s, launch + 0.75, 1.5);
                if (burst is > 0 and < 1)
                {
                    cheering = true;
                    for (int j = 0; j < FireworkSparks; j++)
                    {
                        var spark = _sparks[i * FireworkSparks + j];
                        double angle = j * Math.PI * 2 / FireworkSparks + i;
                        double reach = (1 - Math.Pow(1 - burst, 3)) * (110 + (j % 3) * 18);
                        Canvas.SetLeft(spark, x + Math.Cos(angle) * reach - 5.5);
                        Canvas.SetTop(spark, heights[i] + Math.Sin(angle) * reach + burst * burst * 70 - 5.5);
                        spark.Opacity = burst < 0.6 ? 1 : 1 - (burst - 0.6) / 0.4;
                        spark.IsVisible = true;
                    }
                }
            }

            double light = FireworkLaunches.Select(l => s - (l - 0.5)).Where(d => d is >= 0 and < 0.5).DefaultIfEmpty(-1).Max();
            if (light >= 0)
            {
                Pose(lean: 18, bounce: 6, arms: (30, -35 - 20 * Math.Sin(light / 0.5 * Math.PI)), legs: (0, 0));
                Center(_matchFlame, Hand(true, -35 - 20 * Math.Sin(light / 0.5 * Math.PI), 50));
                _matchFlame.Opacity = 0.7 + 0.3 * Math.Sin(s * 40);
            }
            else if (s >= 6.2)
            {
                CheerDance(s);
                _glyphNewYear.IsVisible = true;
                _glyphNewYear.Opacity = Math.Min(1, (s - 6.2) / 0.3) * (1 - Phase(s, FireworksSeconds - 0.4, 0.4));
                Canvas.SetLeft(_glyphNewYear, side > 0 ? -10 : -90);
                Canvas.SetTop(_glyphNewYear, (_partyHat.IsVisible ? -112 : -70) - Math.Abs(Math.Sin(s * 6)) * 6);
            }
            else if (cheering)
            {
                Pose(lean: -6, bounce: -Math.Abs(Math.Sin(s * 8)) * 10, arms: (150 + Math.Sin(s * 8) * 15, -150 - Math.Sin(s * 8) * 15), legs: (0, 0));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.3;
                (_mascotGaze.X, _mascotGaze.Y) = (5, -7);
            }
            else
            {
                Pose(lean: 0, bounce: Math.Sin(s * 2) * 2, arms: (30, -30), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (5, -5);
            }
            return 0;
        }
    }
}
