using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double IroningSeconds = 6.2;
        private const double PuppetSeconds = 6.8;
        private const double BumpSeconds = 5.0;
        private const double BumpTilt = 7;

        private readonly Canvas _propIron = new() { Width = 64, Height = 40, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new ScaleTransform() };
        private readonly Canvas _wrinkle = new() { Width = 90, Height = 120, IsVisible = false, IsHitTestVisible = false };
        private readonly List<Ellipse> _steam = [];
        private readonly Ellipse _spotlight = new()
        {
            Width = 400, Height = 330, IsVisible = false, IsHitTestVisible = false,
            Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(90, 255, 250, 225), 0), new GradientStop(Color.FromArgb(50, 255, 250, 225), 0.6), new GradientStop(Color.FromArgb(0, 255, 250, 225), 1) } }
        };
        private readonly Canvas _shadowDog = new() { Width = 160, Height = 110, IsVisible = false, IsHitTestVisible = false };
        private readonly RotateTransform _shadowJaw = new();
        private readonly Canvas _shadowBird = new() { Width = 170, Height = 90, IsVisible = false, IsHitTestVisible = false };
        private readonly RotateTransform _shadowWingLeft = new();
        private readonly RotateTransform _shadowWingRight = new();
        private readonly Canvas _shadowRabbit = new() { Width = 110, Height = 150, IsVisible = false, IsHitTestVisible = false };
        private readonly RotateTransform _shadowEarLeft = new();
        private readonly RotateTransform _shadowEarRight = new();
        private readonly TextBlock _glyphWoof = new() { Text = "Woof!", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, IsVisible = false, IsHitTestVisible = false };
        private Control[] _choreProps = [];

        private void BuildMascotChores()
        {
            var steel = new SolidColorBrush(Color.Parse("#C9CED6"));
            _propIron.Children.Add(new Polygon { Points = [new Point(2, 38), new Point(62, 38), new Point(54, 18), new Point(18, 12)], Fill = steel, Stroke = new SolidColorBrush(Color.Parse("#7A8088")), StrokeThickness = 1.5 });
            _propIron.Children.Add(new Polygon { Points = [new Point(4, 30), new Point(58, 30), new Point(52, 18), new Point(18, 13)], Fill = Palette.Accent });
            _propIron.Children.Add(Place(new Rectangle { Width = 30, Height = 8, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.Parse("#2A2C30")) }, 18, 0));
            _propIron.Children.Add(new Line { StartPoint = new Point(22, 6), EndPoint = new Point(22, 14), Stroke = new SolidColorBrush(Color.Parse("#2A2C30")), StrokeThickness = 4 });
            _propIron.Children.Add(new Line { StartPoint = new Point(44, 6), EndPoint = new Point(44, 14), Stroke = new SolidColorBrush(Color.Parse("#2A2C30")), StrokeThickness = 4 });

            var crease = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0));
            var shine = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            for (int i = 0; i < 3; i++)
            {
                var points = new Points();
                for (int k = 0; k <= 8; k++) points.Add(new Point(10 + i * 26 + (k % 2 == 0 ? 0 : 12), k * 14));
                _wrinkle.Children.Add(new Polyline { Points = points, Stroke = crease, StrokeThickness = 3.5 });
                var light = new Points(points.Select(p => new Point(p.X + 3, p.Y + 1)));
                _wrinkle.Children.Add(new Polyline { Points = light, Stroke = shine, StrokeThickness = 1.5 });
            }
            for (int i = 0; i < 5; i++)
            {
                var puff = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), IsVisible = false, IsHitTestVisible = false };
                _steam.Add(puff);
                _mascotFx.Children.Add(puff);
            }

            var shade = new SolidColorBrush(Color.FromArgb(205, 6, 6, 10));
            foreach (var shadow in new[] { _shadowDog, _shadowBird, _shadowRabbit })
            {
                shadow.RenderTransformOrigin = RelativePoint.Center;
                shadow.RenderTransform = new ScaleTransform(1.6, 1.6);
            }
            _shadowDog.Children.Add(new Polygon { Points = [new Point(20, 60), new Point(40, 30), new Point(52, 4), new Point(70, 30), new Point(120, 36), new Point(158, 52), new Point(150, 64), new Point(60, 72), new Point(26, 96)], Fill = shade });
            _shadowDog.Children.Add(Place(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromArgb(200, 255, 250, 225)) }, 96, 40));
            var jaw = new Canvas { Width = 90, Height = 20, RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = _shadowJaw };
            jaw.Children.Add(new Polygon { Points = [new Point(0, 0), new Point(86, 4), new Point(80, 16), new Point(0, 20)], Fill = shade });
            _shadowDog.Children.Add(Place(jaw, 62, 62));

            _shadowBird.Children.Add(Place(new Ellipse { Width = 60, Height = 26, Fill = shade }, 55, 40));
            _shadowBird.Children.Add(new Polygon { Points = [new Point(112, 46), new Point(132, 50), new Point(112, 56)], Fill = shade });
            foreach (var (turn, left) in new[] { (_shadowWingLeft, true), (_shadowWingRight, false) })
            {
                var wing = new Canvas { Width = 80, Height = 40, RenderTransformOrigin = new RelativePoint(left ? 1 : 0, 1, RelativeUnit.Relative), RenderTransform = turn };
                wing.Children.Add(new Polygon
                {
                    Points = left ? [new Point(80, 40), new Point(0, 4), new Point(20, 0), new Point(80, 30)] : [new Point(0, 40), new Point(80, 4), new Point(60, 0), new Point(0, 30)],
                    Fill = shade
                });
                _shadowBird.Children.Add(Place(wing, left ? 10 : 80, 12));
            }

            _shadowRabbit.Children.Add(Place(new Ellipse { Width = 70, Height = 58, Fill = shade }, 20, 84));
            foreach (var (turn, x) in new[] { (_shadowEarLeft, 26.0), (_shadowEarRight, 58.0) })
            {
                var ear = new Ellipse { Width = 22, Height = 74, Fill = shade, RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative), RenderTransform = turn };
                _shadowRabbit.Children.Add(Place(ear, x, 18));
            }

            foreach (var prop in new Control[] { _wrinkle, _propIron, _spotlight, _shadowDog, _shadowBird, _shadowRabbit, _glyphWoof }) _mascotFx.Children.Add(prop);
            _choreProps = [_propIron, _wrinkle, _spotlight, _shadowDog, _shadowBird, _shadowRabbit, _glyphWoof, .. _steam];
        }

        private void HideMascotChores() => HideAll(_choreProps);

        private double StepMascotIroning(double s)
        {
            double side = MascotSide;
            double edge = side > 0 ? MascotCoverLeft : MascotCoverLeft + StageCover;
            double ironing = Phase(s, 0.6, 3.6);
            double angle = s < 0.6 ? -150 + 70 * Ease(s / 0.6) : s < 4.2 ? -85 + Math.Sin((s - 0.6) * 5.5) * 30 : -60;
            Canvas.SetLeft(_wrinkle, side > 0 ? edge + 6 : edge - 6 - _wrinkle.Width);
            Canvas.SetTop(_wrinkle, 410);
            _wrinkle.IsVisible = s < 4.6;
            _wrinkle.Opacity = 1 - Ease(ironing);

            if (s < 4.2)
            {
                var hand = StageHand(true, angle, 50);
                var ironScale = (ScaleTransform)_propIron.RenderTransform!;
                ironScale.ScaleX = side * 1.35;
                ironScale.ScaleY = 1.35;
                Center(_propIron, new Point(hand.X + side * 14, hand.Y));
                for (int i = 0; i < _steam.Count && s > 0.6; i++)
                {
                    double age = (s * 1.4 + i / (double)_steam.Count) % 1;
                    var puff = _steam[i];
                    puff.Width = puff.Height = 10 + age * 26;
                    Canvas.SetLeft(puff, hand.X + side * (20 + age * 18) - puff.Width / 2 + Math.Sin(age * 6 + i) * 6);
                    Canvas.SetTop(puff, hand.Y - 26 - age * 70);
                    puff.Opacity = (1 - age) * 0.8;
                    puff.IsVisible = true;
                }
                bool pressing = s >= 0.6;
                Pose(lean: pressing ? 14 : 4, bounce: pressing ? -Math.Abs(Math.Sin(s * 11)) * 3 : 0, arms: (25, angle), legs: (0, 0));
                if (s < 0.6) (_mascotGaze.X, _mascotGaze.Y) = (6, 3);
            }
            else if (s < 5.0)
            {
                Canvas.SetLeft(_mascot, MascotSpot - side * Ease(Phase(s, 4.2, 0.4)) * 30);
                Pose(lean: -4, bounce: 0, arms: (30, -30), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, 3);
                for (int i = 0; i < _glyphSparkles.Count; i++)
                {
                    var sparkle = _glyphSparkles[i];
                    sparkle.IsVisible = true;
                    sparkle.Opacity = Math.Max(0, Math.Sin((s - 4.2) * 5 + i * 2.1));
                    Canvas.SetLeft(sparkle, 30 + i * 55);
                    Canvas.SetTop(sparkle, -40 - (i % 2) * 36);
                }
            }
            else
            {
                Canvas.SetLeft(_mascot, MascotSpot - side * 30);
                CheerDance(s);
            }
            return 0;
        }

        private double StepMascotPuppets(double s)
        {
            double side = MascotSide;
            var light = new Point(side > 0 ? MascotCoverLeft + 260 : MascotCoverLeft + StageCover - 260, 250);
            _spotlight.Opacity = Math.Min(1, s / 0.4) * (1 - Phase(s, PuppetSeconds - 0.5, 0.5));
            Center(_spotlight, light);

            double dog = Phase(s, 0.4, 1.9), bird = Phase(s, 2.4, 1.9), rabbit = Phase(s, 4.4, 1.9);
            static double Show(double p) => p <= 0 || p >= 1 ? 0 : Math.Min(1, Math.Min(p / 0.12, (1 - p) / 0.12));
            if (Show(dog) > 0)
            {
                double bark = Math.Max(0, Math.Sin(dog * Math.PI * 7));
                _shadowJaw.Angle = bark * 22;
                Center(_shadowDog, new Point(light.X, light.Y + 10));
                _shadowDog.Opacity = Show(dog);
                if (bark > 0.6)
                {
                    _glyphWoof.IsVisible = true;
                    _glyphWoof.Opacity = Show(dog);
                    Canvas.SetLeft(_glyphWoof, light.X + 120);
                    Canvas.SetTop(_glyphWoof, light.Y - 70);
                }
            }
            if (Show(bird) > 0)
            {
                double flap = Math.Sin(bird * Math.PI * 10) * 32;
                _shadowWingLeft.Angle = flap;
                _shadowWingRight.Angle = -flap;
                Center(_shadowBird, new Point(light.X - 60 + bird * 120, light.Y + 40 - bird * 90 + Math.Sin(bird * 12) * 8));
                _shadowBird.Opacity = Show(bird);
            }
            if (Show(rabbit) > 0)
            {
                double wiggle = Math.Sin(rabbit * Math.PI * 8);
                _shadowEarLeft.Angle = -8 + wiggle * 10;
                _shadowEarRight.Angle = 8 - wiggle * 10;
                double hop = Math.Abs(Math.Sin(rabbit * Math.PI * 3));
                Center(_shadowRabbit, new Point(light.X - 40 + rabbit * 80, light.Y + 30 - hop * 40));
                _shadowRabbit.Opacity = Show(rabbit);
            }

            if (s < PuppetSeconds - 0.6)
            {
                double move = Math.Sin(s * 9) * 8;
                Pose(lean: 6, bounce: -Math.Abs(Math.Sin(s * 4)) * 3, arms: (-150 + move, -120 - move), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -6);
            }
            else Pose(lean: 28 * Math.Sin(Phase(s, PuppetSeconds - 0.6, 0.6) * Math.PI), bounce: 0, arms: (10, -10), legs: (0, 0));
            return 0;
        }

        private double StepMascotBump(double s)
        {
            double side = MascotSide;
            double knock = s < 0.4 ? Ease(s / 0.4) : s < 2.4 ? 1 : 1 - Ease(Phase(s, 2.4, 0.5));
            Canvas.SetLeft(_mascot, MascotSpot - side * 50 * knock);

            double wobble = s < 1.4 ? Math.Exp(-s * 3.5) * Math.Sin(s * 22) * 6 : 0;
            double straighten = BackOut(Phase(s, 2.9, 0.6));
            double tilt = side * (BumpTilt * (1 - straighten) + wobble * (1 - straighten));
            _npCoverTurn.Angle = s < BumpSeconds - 0.05 ? tilt : 0;

            if (s < 0.12)
            {
                Pose(lean: 10 * side, bounce: 0, arms: (-70, -70), legs: (-20, 20));
                _mascotEyes.ScaleY = 0.2;
            }
            else if (s < 1.3)
            {
                double fall = Ease(Phase(s, 0.12, 0.35));
                Pose(lean: -82 * side * fall, bounce: 0, arms: (130, -130), legs: (-24 * fall, 24 * fall));
                _mascotEyes.ScaleY = 0.15;
                if (s > 0.5)
                    for (int i = 0; i < _glyphSparkles.Count; i++)
                    {
                        var star = _glyphSparkles[i];
                        double turn = s * 6 + i * Math.PI * 2 / _glyphSparkles.Count;
                        star.IsVisible = true;
                        star.Opacity = 1;
                        Canvas.SetLeft(star, (side > 0 ? 20 : 120) + Math.Cos(turn) * 50);
                        Canvas.SetTop(star, 150 + Math.Sin(turn) * 16);
                    }
            }
            else if (s < 1.8)
            {
                double up = Ease(Phase(s, 1.3, 0.5));
                Pose(lean: -82 * side * (1 - up), bounce: 0, arms: (40, -40), legs: (0, 0));
                _mascotEyes.ScaleY = 0.5 + 0.5 * up;
                _mascotLean.Angle += Math.Sin(s * 30) * 6 * up;
            }
            else if (s < 2.4)
            {
                double jolt = Phase(s, 1.9, 0.3);
                Pose(lean: -10 * side, bounce: -Math.Sin(jolt * Math.PI) * 30, arms: (150, -150), legs: (-12, 12));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.5;
                (_mascotGaze.X, _mascotGaze.Y) = (6, -4);
                var pop = (ScaleTransform)_glyphAlarm.RenderTransform!;
                pop.ScaleX = pop.ScaleY = jolt < 1 ? BackOut(jolt) : 1 + Math.Sin(s * 14) * 0.06;
                _glyphAlarm.IsVisible = true;
                _glyphAlarm.Opacity = 1;
                Canvas.SetLeft(_glyphAlarm, 88);
                Canvas.SetTop(_glyphAlarm, -96 - Math.Sin(jolt * Math.PI) * 30);
            }
            else if (s < 3.5)
            {
                if (s < 2.9) WalkPose(s, side);
                else Pose(lean: 12 * side, bounce: 0, arms: (40, -90 - 40 * Phase(s, 2.9, 0.4)), legs: (-12, 12));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -3);
            }
            else if (s < 4.1)
            {
                double wipe = Math.Sin(Phase(s, 3.5, 0.6) * Math.PI * 2);
                Pose(lean: -4, bounce: 0, arms: (30, -165 + wipe * 15), legs: (0, 0));
                _mascotEyes.ScaleY = 0.4;
            }
            else CheerDance(s);
            return 0;
        }
    }
}
