using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double SpinWindUp = 0.5;
        private const double SpinSeconds = 2.8;
        private const double SpinTurns = 5;
        private const double SpinPullBack = 12;
        private const double SpinSettle = 0.6;
        private const double SpinTotalSeconds = 5.2;
        private const double SpinArcRadius = 330;
        private const double SpinArcSweep = 70;

        private readonly Path[] _spinArcs = [SpinArc(0), SpinArc(180)];

        private static Path SpinArc(double start)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                double from = (start - SpinArcSweep / 2) * Math.PI / 180, to = (start + SpinArcSweep / 2) * Math.PI / 180;
                context.BeginFigure(new Point(Math.Cos(from) * SpinArcRadius, Math.Sin(from) * SpinArcRadius), false);
                context.ArcTo(new Point(Math.Cos(to) * SpinArcRadius, Math.Sin(to) * SpinArcRadius), new Size(SpinArcRadius, SpinArcRadius), 0, false,
                    SweepDirection.Clockwise);
                context.EndFigure(false);
            }
            return new Path
            {
                Data = geometry, Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), StrokeThickness = 7, StrokeLineCap = PenLineCap.Round,
                IsVisible = false, IsHitTestVisible = false, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
                RenderTransform = new RotateTransform()
            };
        }

        private void BuildMascotSpin()
        {
            foreach (var arc in _spinArcs) _mascotFx.Children.Add(arc);
        }

        private double StepMascotSpin(double s)
        {
            double side = MascotSide;
            Canvas.SetLeft(_mascot, MascotSpot);
            _mascotFacing.ScaleX = side;
            double offset = 0;

            double windUp = Ease(Phase(s, 0, SpinWindUp));
            double p = Phase(s, SpinWindUp, SpinSeconds);
            double spun = 1 - Math.Pow(1 - p, 2.5);
            double speed = s < SpinWindUp ? 0 : Math.Pow(1 - p, 1.5) * Math.Min(1, (s - SpinWindUp) / 0.15);
            double settle = s - SpinWindUp - SpinSeconds;
            double wobble = settle > 0 ? 8 * Math.Sin(settle * 18) * Math.Exp(-settle * 6) : 0;
            double angle = s < SpinWindUp ? -SpinPullBack * windUp : -SpinPullBack + (SpinTurns * 360 + SpinPullBack) * spun + wobble;
            _npCoverTurn.Angle = side * angle;
            _npCoverFit.ScaleX = _npCoverFit.ScaleY = 1 - 0.29 * Math.Min(1, speed * 1.6);

            bool changed = spun >= 0.5;
            _npCoverOld.Opacity = changed ? 0 : 1;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
            if (changed) ShowNewArt(Phase(spun, 0.5, 0.3));

            for (int i = 0; i < _spinArcs.Length; i++)
            {
                var arc = _spinArcs[i];
                arc.IsVisible = speed > 0.15;
                arc.Opacity = Math.Min(1, speed * 1.4);
                Canvas.SetLeft(arc, CoverMiddle.X);
                Canvas.SetTop(arc, CoverMiddle.Y);
                ((RotateTransform)arc.RenderTransform!).Angle = side * (angle - 40);
            }

            double look = side * angle * Math.PI / 180;
            if (s < SpinWindUp)
            {
                Pose(lean: 8 * windUp, bounce: 0, arms: (30, -90 + 20 * windUp), legs: (-8 * windUp, 8 * windUp));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -3);
            }
            else if (s < SpinWindUp + 0.35)
            {
                double swipe = Ease(Phase(s, SpinWindUp, 0.35));
                Pose(lean: 8 - 18 * swipe, bounce: -Math.Sin(swipe * Math.PI) * 20, arms: (30 + 40 * swipe, -70 - 100 * swipe), legs: (-14 * swipe, 14 * swipe));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.2;
            }
            else if (settle < 0)
            {
                double hop = Math.Abs(Math.Sin(s * 7)) * speed;
                Pose(lean: Math.Sin(look) * 8 * speed, bounce: -hop * 18, arms: (120 + hop * 30, -120 - hop * 30), legs: (-10 * hop, 10 * hop));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1 + 0.35 * speed;
                (_mascotGaze.X, _mascotGaze.Y) = (Math.Cos(look) * 5, Math.Sin(look) * 5);
            }
            else if (settle < SpinSettle + 0.9)
            {
                double fade = 1 - Phase(settle, 0, SpinSettle + 0.9);
                double sway = Math.Sin(settle * 6) * 14 * fade;
                Pose(lean: sway, bounce: 0, arms: (35 + sway, -35 + sway), legs: (sway * 0.5, -sway * 0.5));
                _mascotEyes.ScaleY = 0.2;
                (_mascotGaze.X, _mascotGaze.Y) = (Math.Cos(settle * 9) * 4, Math.Sin(settle * 9) * 3);
            }
            else CheerDance(s);

            if (s >= SpinTotalSeconds)
            {
                _npCoverTurn.Angle = 0;
                _npCoverFit.ScaleX = _npCoverFit.ScaleY = 1;
                _mascotExit = MascotExit.Back;
                EndMascotTrick(MascotState.Exit);
            }
            return offset;
        }
    }
}
