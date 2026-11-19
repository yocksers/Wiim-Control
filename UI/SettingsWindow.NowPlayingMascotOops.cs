using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double OopsFlipSeconds = 0.9;
        private const double OopsShockAt = 1.5;
        private const double OopsTurnAt = 2.3;
        private const double OopsTurnSeconds = 0.6;
        private const double OopsSeconds = 4.0;

        private readonly RotateTransform _npCoverTurn = new();
        private readonly ScaleTransform _npCoverFit = new();
        private readonly TextBlock _glyphAlarm = new()
        {
            Text = "!", FontSize = 72, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.Parse("#E8B829")), IsVisible = false,
            RenderTransformOrigin = new Avalonia.RelativePoint(0.5, 1, Avalonia.RelativeUnit.Relative), RenderTransform = new ScaleTransform()
        };

        private void BuildMascotOops()
        {
            _mascotGlyphs.Children.Add(_glyphAlarm);
        }

        private static double BackOut(double p)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(p - 1, 3) + c1 * Math.Pow(p - 1, 2);
        }

        private double StepMascotOops(double s)
        {
            double side = MascotSide;
            Canvas.SetLeft(_mascot, MascotSpot);
            _mascotFacing.ScaleX = side;
            double offset = 0;

            double flip = Math.Min(1, s / OopsFlipSeconds);
            double angle = Ease(flip) * 180;
            bool front = angle < 90;
            _npCoverFlip.AngleY = flip >= 1 ? 0 : front ? angle : angle - 180;
            _npCoverOld.Opacity = front ? 1 : 0;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
            if (_npBackdropOld.Source != null) BlendBackdrops(1 - Ease(flip));

            double turn = Phase(s, OopsTurnAt, OopsTurnSeconds);
            _npCoverTurn.Angle = front && flip < 1 ? 0 : 180 + side * 180 * BackOut(turn);
            double radians = _npCoverTurn.Angle * Math.PI / 180;
            _npCoverFit.ScaleX = _npCoverFit.ScaleY = 1 / (Math.Abs(Math.Cos(radians)) + Math.Abs(Math.Sin(radians)));

            if (s < OopsFlipSeconds)
            {
                double swing = Math.Min(1, flip * 2.5);
                Pose(lean: 10 * (1 - flip), bounce: -Math.Sin(flip * Math.PI) * 50, arms: (120, -40 - swing * 110), legs: (-18 * (1 - flip), 18 * (1 - flip)));
            }
            else if (s < OopsShockAt)
            {
                double look = Phase(s, OopsFlipSeconds, 0.3);
                Pose(lean: 7 * look, bounce: 0, arms: (25, -25), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6 * look, -4 * look);
            }
            else if (s < OopsTurnAt)
            {
                double jolt = Phase(s, OopsShockAt, 0.35);
                double shake = Math.Sin(s * 40) * (1 - Phase(s, OopsShockAt + 0.3, 0.5));
                Pose(lean: -12 + shake * 3, bounce: -Math.Sin(jolt * Math.PI) * 34, arms: (150 + shake * 12, -150 - shake * 12), legs: (-14, 14));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.5;
                (_mascotGaze.X, _mascotGaze.Y) = (6, -2);
                _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + 0.12 * Math.Sin(jolt * Math.PI);
                var pop = (ScaleTransform)_glyphAlarm.RenderTransform!;
                pop.ScaleX = pop.ScaleY = jolt < 1 ? BackOut(jolt) : 1 + Math.Sin(s * 14) * 0.06;
                _glyphAlarm.IsVisible = true;
                _glyphAlarm.Opacity = 1;
                Canvas.SetLeft(_glyphAlarm, 88);
                Canvas.SetTop(_glyphAlarm, -96 - Math.Sin(jolt * Math.PI) * 34);
            }
            else if (s < OopsTurnAt + OopsTurnSeconds)
            {
                _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
                double swipe = Ease(Math.Min(1, turn * 1.6));
                offset = -Math.Sin(turn * Math.PI) * 40;
                Pose(lean: 10 * swipe, bounce: 0, arms: (60, -40 - 140 * swipe), legs: (-16 * swipe, 16 * swipe));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.2;
                (_mascotGaze.X, _mascotGaze.Y) = (6, -4);
                _glyphAlarm.IsVisible = turn < 0.4;
                _glyphAlarm.Opacity = 1 - turn / 0.4;
                Canvas.SetLeft(_glyphAlarm, 88);
                Canvas.SetTop(_glyphAlarm, -96);
            }
            else
            {
                double relief = s - OopsTurnAt - OopsTurnSeconds;
                if (relief < 0.5)
                {
                    Pose(lean: -4, bounce: 0, arms: (30, -30), legs: (0, 0));
                    _mascotEyes.ScaleY = 0.35;
                }
                else CheerDance(s);
            }

            if (s >= OopsSeconds)
            {
                _npCoverTurn.Angle = 0;
                _npCoverFit.ScaleX = _npCoverFit.ScaleY = 1;
                _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
                _mascotExit = MascotExit.Back;
                EndMascotTrick(MascotState.Exit);
            }
            return offset;
        }
    }
}
