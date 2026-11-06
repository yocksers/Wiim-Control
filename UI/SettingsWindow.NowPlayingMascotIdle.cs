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
        private const double MascotSitOffset = -(CabinetTop + CabinetHeight - 6) - (StageCover - MascotHeight + 8);
        private const double MascotSummitOffset = -StageCover;
        private const double MascotKaraokeChance = 0.5;
        private const double FishingHeadroom = 230;
        private const double SummitHeadroom = 280;
        private static readonly TimeSpan MascotVolumeReactFor = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan MascotVolumeVisitGap = TimeSpan.FromSeconds(30);

        private readonly Canvas _propMic = new() { Width = 16, Height = 40, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(-35) };
        private readonly Line _propRod = new() { Stroke = new SolidColorBrush(Color.Parse("#6B4A2E")), StrokeThickness = 4, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Line _propFishLine = new() { Stroke = new SolidColorBrush(Color.FromArgb(200, 230, 232, 236)), StrokeThickness = 1.5, IsVisible = false };
        private readonly Ellipse _propBobber = new() { Width = 14, Height = 14, IsVisible = false, Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.Parse("#D0342C")) };
        private readonly TextBlock _propCatch = new() { Text = "♫", FontSize = 42, Foreground = Palette.AccentText, IsVisible = false };
        private readonly Canvas _propFlag = new() { Width = 70, Height = 100, IsVisible = false };
        private readonly Polygon _propFlagCloth = new() { Fill = Palette.Accent };
        private readonly Polygon _propPlane = new()
        {
            Points = [new Point(0, 0), new Point(54, 12), new Point(0, 24), new Point(12, 12)], Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 1, IsVisible = false,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform()
        };
        private readonly Polygon _propPlaneHeld = new()
        {
            Points = [new Point(0, 0), new Point(30, 7), new Point(0, 14), new Point(7, 7)], Fill = Brushes.White, IsVisible = false
        };
        private readonly Path _propRope = new() { Stroke = new SolidColorBrush(Color.Parse("#E4E7EB")), StrokeThickness = 4, IsVisible = false };
        private readonly Rectangle _propPlugLeft = new() { Width = 8, Height = 18, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.Parse("#C9A227")), IsVisible = false };
        private readonly Rectangle _propPlugRight = new() { Width = 8, Height = 18, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.Parse("#C9A227")), IsVisible = false };
        private readonly Line _propBroom = new() { Stroke = new SolidColorBrush(Color.Parse("#A0753F")), StrokeThickness = 5, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Polygon _propBristles = new() { Fill = new SolidColorBrush(Color.Parse("#D9B65C")), IsVisible = false };
        private readonly List<Ellipse> _dust = [];
        private Point _planeLast;
        private double _mascotHeadroom;
        private int _mascotVolumeTurn;
        private int? _mascotVolumeSeen;
        private DateTime _mascotVolumeAt = DateTime.MinValue;
        private DateTime _mascotVolumeVisit = DateTime.MinValue;

        private void BuildMascotIdle(Canvas figure)
        {
            _propMic.Children.Add(Place(new Rectangle { Width = 8, Height = 26, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.Parse("#15161A")) }, 4, 14));
            _propMic.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse("#9AA0A8")), Stroke = new SolidColorBrush(Color.Parse("#5A6068")), StrokeThickness = 1 });
            _propFlag.Children.Add(Place(new Rectangle { Width = 4, Height = 100, Fill = new SolidColorBrush(Color.Parse("#C8CDD4")) }, 0, 0));
            _propFlag.Children.Add(_propFlagCloth);
            for (int i = 0; i < 6; i++)
            {
                var puff = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(Color.FromArgb(120, 200, 190, 170)), IsVisible = false };
                _dust.Add(puff);
                figure.Children.Add(puff);
            }
            foreach (var prop in new Control[] { _propRod, _propFishLine, _propBobber, _propCatch, _propMic, _propPlaneHeld, _propRope, _propPlugLeft, _propPlugRight, _propBroom, _propBristles })
                figure.Children.Add(prop);
            _mascotFx.Children.Add(_propFlag);
            _mascotFx.Children.Add(_propPlane);
        }

        private static PathGeometry Curve(Point start, Point bend, Point end) => new()
        {
            Figures = [new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false, Segments = [new QuadraticBezierSegment { Point1 = bend, Point2 = end }] }]
        };

        private void HideMascotIdle()
        {
            foreach (var prop in new Control[] { _propRod, _propFishLine, _propBobber, _propCatch, _propMic, _propPlaneHeld, _propRope, _propPlugLeft, _propPlugRight, _propBroom, _propBristles })
                prop.IsVisible = false;
            foreach (var puff in _dust) puff.IsVisible = false;
        }

        private void ApplyMascotHeadroom(double headroom)
        {
            var margin = new Thickness(ShadowRoom.Left, ShadowRoom.Top + headroom, ShadowRoom.Right, ShadowRoom.Bottom);
            if (_npStage.Margin != margin) _npStage.Margin = margin;
        }

        private void HideMascotStageProps()
        {
            _propFlag.IsVisible = false;
            _propPlane.IsVisible = false;
        }

        private bool LyricsSinging()
        {
            if (!LyricsShown || _npControls?.Playing.IsVisible != true) return false;
            if (_npLyricLines.Count == 0) return _npLyricsPlainText.Text is { Length: > 0 };
            return _npLyricIndex >= 0 && _npLyricIndex < _npLyricLines.Count && _npLyricLines[_npLyricIndex].Text.Trim().Length > 0;
        }

        private void Sing(double s, bool singing)
        {
            double pump = singing ? Math.Abs(Math.Sin(s * 11)) * (0.6 + 0.4 * Math.Abs(Math.Sin(s * 2.3))) : 0;
            _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + pump * 0.16;
            var hand = Hand(true, -150, 44);
            Center(_propMic, new Point(hand.X - 4, hand.Y - 10));
        }

        private double MascotIdleLength() => _mascotVisit switch
        {
            MascotVisit.Fishing => 9.0,
            MascotVisit.Climb => 8.0,
            MascotVisit.SkipRope => 6.0,
            MascotVisit.PaperPlane => 6.2,
            MascotVisit.Sweep => 6.0,
            MascotVisit.Karaoke => 7.0,
            _ => 0
        };

        private double StepMascotIdle(double s)
        {
            return _mascotVisit switch
            {
                MascotVisit.Fishing => StepFishing(s),
                MascotVisit.Climb => StepClimb(s),
                MascotVisit.SkipRope => StepSkipRope(s),
                MascotVisit.PaperPlane => StepPaperPlane(s),
                MascotVisit.Sweep => StepSweep(s),
                _ => StepKaraoke(s)
            };
        }

        private double JumpBetween(double s, double start, double length, double fromX, double toX, double fromOffset, double toOffset, double height)
        {
            double p = Phase(s, start, length);
            Canvas.SetLeft(_mascot, fromX + (toX - fromX) * p);
            return fromOffset + (toOffset - fromOffset) * p - Math.Sin(p * Math.PI) * height;
        }

        private double StepFishing(double s)
        {
            double outward = -MascotSide;
            double sitX = _mascotFromRight ? MascotCoverLeft + StageCover - CabinetLeft - CabinetWidth - 6 : MascotCoverLeft + 6 - CabinetLeft;
            double offset;
            _mascotHeadroom = FishingHeadroom * Ease(Phase(s, 0.2, 0.7)) * (1 - Ease(Phase(s, 7.7, 0.7)));
            if (s < 0.35)
            {
                offset = 8 * Math.Sin(s / 0.35 * Math.PI / 2);
                Pose(lean: 0, bounce: 0, arms: (30, -30), legs: (14, -14));
                _mascotGaze.Y = -5;
            }
            else if (s < 1.1)
            {
                offset = JumpBetween(s, 0.35, 0.75, MascotSpot, sitX, 0, MascotSitOffset, 140);
                Pose(lean: 0, bounce: 0, arms: (150, -150), legs: (-20, 20));
                _mascotShadow.Opacity = 0;
            }
            else if (s < 7.6)
            {
                offset = MascotSitOffset;
                Canvas.SetLeft(_mascot, sitX);
                _mascotFacing.ScaleX = outward;
                _mascotShadow.Opacity = 0;
                double swing = Math.Sin(s * 2.4) * 6;
                bool cheer = s >= 6.4;
                double rodArm = s < 1.5 ? -110 - Ease((s - 1.1) / 0.4) * 50 : s < 1.8 ? -160 + Ease((s - 1.5) / 0.3) * 60 : s < 5.6 ? -100 : s < 6.4 ? -100 - Ease((s - 5.6) / 0.8) * 60 : -160;
                Pose(lean: 0, bounce: 0, arms: (cheer ? 150 + Math.Sin(s * 10) * 15 : 20, rodArm), legs: (-70 + swing, -60 - swing));
                var hand = Hand(true, rodArm, 46);
                double rodAngle = (rodArm + 70) * Math.PI / 180;
                var tip = new Point(hand.X + Math.Cos(rodAngle) * 120, hand.Y - Math.Sin(-rodAngle) * 120 - 40);
                _propRod.StartPoint = hand;
                _propRod.EndPoint = tip;
                _propRod.IsVisible = true;
                double water = StageCover - 40 - (MascotOffsetTop() + offset);
                double end = s < 1.8 ? tip.Y + 10 : s < 2.3 ? tip.Y + (water - tip.Y) * Ease((s - 1.8) / 0.5) : s < 5.6 ? water : s < 6.4 ? water + (tip.Y + 50 - water) * Ease((s - 5.6) / 0.8) : tip.Y + 50;
                double bob = s is >= 2.3 and < 5.6 ? Math.Sin(s * 3) * 4 + (s is > 4.6 and < 5.0 or > 5.2 and < 5.5 ? 14 : 0) : 0;
                _propFishLine.StartPoint = tip;
                _propFishLine.EndPoint = new Point(tip.X, end + bob);
                _propFishLine.IsVisible = true;
                if (s < 5.6) Center(_propBobber, new Point(tip.X, end + bob));
                else
                {
                    _propCatch.IsVisible = true;
                    Canvas.SetLeft(_propCatch, tip.X - 14 + Math.Sin(s * 12) * (s >= 6.4 ? 4 : 0));
                    Canvas.SetTop(_propCatch, end - 4);
                }
                _mascotGaze.Y = s < 5.6 ? 5 : 0;
                _mascotGaze.X = 4;
                if (cheer) _mascotEyes.ScaleY = 0.6;
            }
            else
            {
                offset = JumpBetween(s, 7.6, 0.8, sitX, MascotSpot, MascotSitOffset, 0, 90);
                _mascotShadow.Opacity = Phase(s, 7.6, 0.8);
                Pose(lean: 0, bounce: 0, arms: (140, -140), legs: s < 8.4 ? (-15, 15) : (0, 0));
            }
            return offset;
        }

        private double StepClimb(double s)
        {
            double wall = _mascotFromRight ? MascotCoverLeft + StageCover - CabinetLeft + 4 : MascotCoverLeft - CabinetLeft - CabinetWidth - 4;
            double summit = _mascotFromRight ? MascotCoverLeft + StageCover - MascotWidth - 30 : MascotCoverLeft + 30;
            double offset = 0;
            _mascotHeadroom = SummitHeadroom * Ease(Phase(s, 1.6, 2.2)) * (1 - Ease(Phase(s, 5.9, 0.7)));
            if (s < 0.4)
            {
                Canvas.SetLeft(_mascot, MascotSpot + (wall - MascotSpot) * Ease(s / 0.4));
                Pose(lean: 0, bounce: 0, arms: (20, -20), legs: (0, 0));
                _mascotGaze.Y = -6;
            }
            else if (s < 3.4)
            {
                double p = (s - 0.4) / 3.0;
                double step = Math.Sin(s * 6);
                offset = -(StageCover - 40) * (p + Math.Sin(p * Math.PI * 12) * 0.008);
                Canvas.SetLeft(_mascot, wall);
                Pose(lean: 6, bounce: 0, arms: (25 + step * 12, -175 + step * 22), legs: (-step * 30 - 10, step * 30 - 10));
                _mascotGaze.Y = -5;
            }
            else if (s < 4.2)
            {
                offset = JumpBetween(s, 3.4, 0.8, wall, summit, -(StageCover - 40), MascotSummitOffset, 40);
                Pose(lean: 18 * (1 - Phase(s, 3.4, 0.8)), bounce: 0, arms: (-40, -120), legs: (-30, 10));
            }
            else if (s < 5.8)
            {
                offset = MascotSummitOffset;
                Canvas.SetLeft(_mascot, summit);
                _mascotFacing.ScaleX = -MascotSide;
                double wave = Math.Sin(s * 10);
                Pose(lean: 0, bounce: -Math.Abs(wave) * 3, arms: (20, -150 + wave * 25), legs: (0, 0));
                _mascotEyes.ScaleY = 0.65;
                double plant = Ease(Phase(s, 4.2, 0.4));
                double pole = summit + MascotWidth / 2 + (_mascotFromRight ? 80 : -90);
                Canvas.SetLeft(_propFlag, pole);
                Canvas.SetTop(_propFlag, -100 * plant);
                _propFlag.IsVisible = true;
                double flap = Math.Sin(s * 9) * 6;
                _propFlagCloth.Points = [new Point(4, 4), new Point(60, 18 + flap), new Point(4, 36)];
            }
            else if (s < 6.6)
            {
                offset = JumpBetween(s, 5.8, 0.8, summit, MascotSpot, MascotSummitOffset, 0, 80);
                Pose(lean: 0, bounce: 0, arms: (150, -150), legs: (-15, 15));
                _mascotShadow.Opacity = Phase(s, 5.8, 0.8);
            }
            else Pose(lean: 0, bounce: 4 * Math.Sin(Math.Min(1, (s - 6.6) / 0.4) * Math.PI), arms: (40, -40), legs: (-10, 10));
            if (s >= 5.8 && s < 7.4) _propFlag.IsVisible = true;
            if (offset < -20) _mascotShadow.Opacity = 0;
            return offset;
        }

        private double StepSkipRope(double s)
        {
            bool skipping = s is > 0.4 and < 5.0;
            double turn = skipping ? (s - 0.4) * Math.PI * 2 * 1.5 : 0;
            double low = Math.Cos(turn);
            double hop = skipping ? Math.Pow(Math.Max(0, low), 2) * 46 : 0;
            double arm = 70 + Math.Sin(turn) * 12;
            Pose(lean: 0, bounce: 0, arms: (arm, -arm), legs: hop > 4 ? (-12, 12) : (0, 0));
            var left = Hand(false, arm, 46);
            var right = Hand(true, -arm, 46);
            double sag = skipping ? left.Y + 125 * low + 10 : 238;
            _propRope.Data = Curve(left, new Point(100, sag * 2 - left.Y), right);
            _propRope.IsVisible = true;
            Center(_propPlugLeft, left);
            Center(_propPlugRight, right);
            if (s >= 5.0) _mascotEyes.ScaleY = 0.55;
            _mascotShadow.Opacity = 1 - hop / 46 * 0.5;
            return -hop;
        }

        private double StepPaperPlane(double s)
        {
            double dir = MascotSide;
            if (s < 1.0)
            {
                Pose(lean: 6, bounce: 0, arms: (-35, 35), legs: (0, 0));
                _propPlaneHeld.IsVisible = true;
                Canvas.SetLeft(_propPlaneHeld, 86);
                Canvas.SetTop(_propPlaneHeld, 150);
                _mascotGaze.Y = 6;
                return 0;
            }
            double throwArm = s < 1.3 ? 150 - Ease((s - 1.0) / 0.3) * 270 : -120;
            if (s < 1.3)
            {
                Pose(lean: -8 * dir + 16 * dir * Phase(s, 1.0, 0.3), bounce: 0, arms: (20, throwArm), legs: (0, 0));
                return 0;
            }
            double u = Phase(s, 1.3, 3.8);
            var hand = Hand(true, -120, 46);
            double startX = Canvas.GetLeft(_mascot) + (dir > 0 ? hand.X : MascotWidth - hand.X);
            double startY = MascotOffsetTop() + hand.Y;
            double x = startX + dir * u * 1100;
            double y = startY - Math.Sin(u * Math.PI) * 260;
            if (u is > 0.32 and < 0.52)
            {
                double loop = (u - 0.32) / 0.2 * Math.PI * 2;
                x += dir * Math.Sin(loop) * 90;
                y -= (1 - Math.Cos(loop)) * 90;
            }
            var point = new Point(x, y);
            if (u < 1)
            {
                Canvas.SetLeft(_propPlane, x - 27);
                Canvas.SetTop(_propPlane, y - 12);
                _propPlane.IsVisible = true;
                var delta = point - _planeLast;
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 0.5) ((RotateTransform)_propPlane.RenderTransform!).Angle = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
                _planeLast = point;
            }
            else _propPlane.IsVisible = false;
            bool waving = s >= 4.8;
            Pose(lean: 0, bounce: 0, arms: waving ? (20, -150 + Math.Sin(s * 10) * 25) : (20, -175), legs: (0, 0));
            if (!waving)
            {
                _mascotGaze.X = 5;
                _mascotGaze.Y = -5;
            }
            return 0;
        }

        private double StepSweep(double s)
        {
            double dir = MascotSide;
            double travel = Math.Sin(Math.PI * Math.Min(1, s / 5.4)) * 70;
            Canvas.SetLeft(_mascot, MascotSpot + dir * travel);
            double stroke = Math.Sin(s * 5);
            var head = new Point(205 + stroke * 40, 236);
            var top = Hand(false, -60, 46);
            Pose(lean: 8, bounce: -Math.Abs(Math.Sin(s * 5)) * 2, arms: (-60, -30 - stroke * 12), legs: Run(s, 3, 10));
            _propBroom.StartPoint = new Point(top.X - 10, top.Y - 30);
            _propBroom.EndPoint = head;
            _propBroom.IsVisible = true;
            _propBristles.Points = [new Point(head.X - 6, head.Y - 6), new Point(head.X + 6, head.Y - 6), new Point(head.X + 22, head.Y + 6), new Point(head.X - 22, head.Y + 6)];
            _propBristles.IsVisible = true;
            for (int i = 0; i < _dust.Count; i++)
            {
                double age = (s * 1.6 + i / (double)_dust.Count) % 1;
                var puff = _dust[i];
                puff.IsVisible = s < 5.4;
                double size = 10 + age * 26;
                puff.Width = puff.Height = size;
                puff.Opacity = (1 - age) * 0.8;
                Canvas.SetLeft(puff, 230 + i * 7 + age * 40 - size / 2);
                Canvas.SetTop(puff, 236 - age * 40 - size / 2);
            }
            return 0;
        }

        private double StepKaraoke(double s)
        {
            bool singing = !LyricsShown || LyricsSinging();
            double sway = Math.Sin(s * 2.2);
            Canvas.SetLeft(_mascot, MascotSpot + sway * 10);
            Pose(lean: sway * 6, bounce: -Math.Abs(Math.Sin(s * 4.4)) * 4, arms: (60 + sway * 30, -150), legs: (sway * 8, -sway * 8));
            Sing(s, singing);
            _mascotEyes.ScaleY = singing ? 0.6 : 1;
            if (singing) StepMascotNotes(s);
            return 0;
        }

        private void MascotVolumeTurned(int volume)
        {
            if (_mascotVolumeSeen is { } before && volume != before)
            {
                _mascotVolumeTurn = Math.Sign(volume - before);
                _mascotVolumeAt = DateTime.UtcNow;
                if (_mascotState == MascotState.Gone && DateTime.UtcNow - _mascotVolumeVisit > MascotVolumeVisitGap)
                {
                    _mascotVolumeVisit = DateTime.UtcNow;
                    StartMascotEnter(MascotState.React, MascotWalkSpeed);
                }
            }
            _mascotVolumeSeen = volume;
        }

        private bool VolumeTurnedRecently => DateTime.UtcNow - _mascotVolumeAt < MascotVolumeReactFor;

        private bool VolumePose(double s)
        {
            if (!VolumeTurnedRecently) return false;
            if (_mascotVolumeTurn > 0)
            {
                Pose(lean: -8, bounce: -Math.Abs(Math.Sin(s * 16)) * 3, arms: (35 + Math.Sin(s * 30) * 6, -35 - Math.Sin(s * 30) * 6), legs: (12, -12));
                _mascotEyes.ScaleY = 0.45;
            }
            else
            {
                Pose(lean: 14, bounce: 0, arms: (20, -168), legs: (0, 0));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.2;
                _mascotGaze.X = 3;
            }
            return true;
        }

        private void VolumeWind(double s)
        {
            if (!VolumeTurnedRecently || _mascotVolumeTurn <= 0) return;
            double pump = 1 + Math.Abs(Math.Sin(s * 16)) * 0.24;
            if (pump > _mascotCone.ScaleX) _mascotCone.ScaleX = _mascotCone.ScaleY = pump;
        }
    }
}
