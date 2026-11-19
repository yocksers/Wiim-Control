using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private void WalkPose(double s, double dir)
        {
            double stride = Math.Sin(s * 9);
            Pose(lean: 4 * dir, bounce: -Math.Abs(stride) * 4, arms: (stride * 25, stride * 25), legs: Run(s, 9, 28));
        }

        private void RunPose(double s, double dir, double lean = 16)
        {
            double stride = Math.Sin(s * 24);
            Pose(lean: lean * dir, bounce: -Math.Abs(stride) * 8, arms: (-60 + stride * 50, 60 - stride * 50), legs: Run(s, 24, 45));
        }

        private static (double Left, double Right) Run(double seconds, double speed, double swing) =>
            (Math.Sin(seconds * speed) * swing, -Math.Sin(seconds * speed) * swing);

        private void CheerDance(double s, double jump = 0)
        {
            double beat = s * 6;
            double sway = Math.Sin(beat / 2);
            double open = Math.Sin(beat);
            Canvas.SetLeft(_mascot, Canvas.GetLeft(_mascot) + sway * 16);
            Pose(lean: sway * 7, bounce: -Math.Abs(open) * 14 + jump, arms: (145 + open * 18, -145 - open * 18), legs: (open * 14, -open * 14));
            _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + Math.Abs(open) * 0.08;
        }

        private void Pose(double lean, double bounce, (double Left, double Right) arms, (double Left, double Right) legs)
        {
            _mascotLean.Angle = lean;
            _mascotBounce.Y = bounce;
            _mascotLeftArm.Angle = arms.Left;
            _mascotRightArm.Angle = arms.Right;
            _mascotLeftLeg.Angle = legs.Left;
            _mascotRightLeg.Angle = legs.Right;
        }
    }
}
