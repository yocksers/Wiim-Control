using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double PeekHidden = MascotRightSpot - 210;
        private const double PeekOut = MascotRightSpot - 50;
        private const double PeekSeconds = 3.0;
        private const double SkateSpeed = 650;
        private const double SkateOllieAt = 420;
        private const double SkateJumpSeconds = 0.55;
        private static readonly Point LeftShoulder = new(24, 110);
        private static readonly Point RightShoulder = new(176, 110);

        private readonly Border _propCloth = new() { Width = 28, Height = 20, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.Parse("#EDE6CC")), IsVisible = false };
        private readonly Line _propBaton = new() { Stroke = Brushes.White, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Border _propPhone = new()
        {
            Width = 20, Height = 32, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#15161A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#8A9098")), BorderThickness = new Thickness(2), IsVisible = false
        };
        private readonly Ellipse _propFlash = new() { Width = 90, Height = 90, IsVisible = false, Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) } } };
        private readonly Canvas _propBoard = new() { Width = 150, Height = 24, IsVisible = false };
        private readonly Canvas _propGuitar = new()
        {
            Width = 170, Height = 64, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(20)
        };
        private readonly Canvas _propBalloon = new() { Width = 60, Height = 72, IsVisible = false };
        private readonly Line _propString = new() { Stroke = new SolidColorBrush(Color.Parse("#C8CDD4")), StrokeThickness = 1.5, IsVisible = false };
        private readonly Polygon _propCape = new() { Fill = new SolidColorBrush(Color.Parse("#D0342C")), IsVisible = false };
        private const double FlySeconds = 1.6;
        private const double FlyHigh = 520;
        private const double FlyLow = 60;
        private readonly Canvas _mascotGlyphs = new() { IsHitTestVisible = false };
        private readonly TextBlock _glyphQuestion = new() { Text = "?", FontSize = 46, FontWeight = FontWeight.Bold, Foreground = Brushes.White, IsVisible = false };
        private readonly List<TextBlock> _glyphSparkles = [];

        private void BuildMascotProps(Canvas figure)
        {
            _propBoard.Children.Add(new Rectangle { Width = 150, Height = 11, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.Parse("#C8873A")) });
            foreach (double x in new[] { 18.0, 118.0 })
                _propBoard.Children.Add(Place(new Ellipse { Width = 15, Height = 15, Fill = new SolidColorBrush(Color.Parse("#2A2C30")) }, x, 9));
            _propBalloon.Children.Add(new Ellipse { Width = 60, Height = 72, Fill = Palette.Accent });
            _propBalloon.Children.Add(Place(new Ellipse { Width = 14, Height = 20, Fill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) }, 14, 12));
            _propBalloon.Children.Add(Place(new Polygon { Points = [new Point(26, 70), new Point(34, 70), new Point(30, 78)], Fill = Palette.Accent }, 0, 0));
            var wood = new SolidColorBrush(Color.Parse("#6B4A2E"));
            _propGuitar.Children.Add(Place(new Rectangle { Width = 112, Height = 9, RadiusX = 3, RadiusY = 3, Fill = wood }, 0, 27));
            _propGuitar.Children.Add(Place(new Rectangle { Width = 18, Height = 16, RadiusX = 4, RadiusY = 4, Fill = wood }, -6, 23));
            var red = new SolidColorBrush(Color.Parse("#C0392B"));
            _propGuitar.Children.Add(Place(new Ellipse { Width = 50, Height = 56, Fill = red }, 120, 4));
            _propGuitar.Children.Add(Place(new Ellipse { Width = 42, Height = 46, Fill = red }, 96, 9));
            _propGuitar.Children.Add(Place(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse("#1E1012")) }, 120, 24));
            figure.Children.Insert(Math.Max(0, figure.Children.Count - 2), _propGuitar);
            figure.Children.Insert(0, _propCape);
            foreach (var prop in new Control[] { _propBoard, _propString, _propBalloon, _propCloth, _propBaton, _propPhone, _propFlash }) figure.Children.Add(prop);
            for (int i = 0; i < 3; i++)
            {
                var sparkle = new TextBlock { Text = "✦", FontSize = 22 + i * 6, Foreground = Brushes.White, IsVisible = false };
                _glyphSparkles.Add(sparkle);
                _mascotGlyphs.Children.Add(sparkle);
            }
            _mascotGlyphs.Children.Add(_glyphQuestion);
            _mascot.Children.Add(_mascotGlyphs);
        }

        private void HideMascotProps()
        {
            foreach (var prop in new Control[] { _propCloth, _propBaton, _propPhone, _propFlash, _propBoard, _propBalloon, _propString, _propGuitar, _propCape, _glyphQuestion }) prop.IsVisible = false;
            foreach (var sparkle in _glyphSparkles) sparkle.IsVisible = false;
        }

        private static Point Hand(bool right, double angle, double reach = 44)
        {
            double r = angle * Math.PI / 180;
            var shoulder = right ? RightShoulder : LeftShoulder;
            return new Point(shoulder.X - Math.Sin(r) * reach, shoulder.Y + Math.Cos(r) * reach);
        }

        private static void Center(Control prop, Point at)
        {
            Canvas.SetLeft(prop, at.X - prop.Width / 2);
            Canvas.SetTop(prop, at.Y - prop.Height / 2);
            prop.IsVisible = true;
        }

        private MascotVisit? _mascotLastVisitKind;

        private MascotVisit PickMascotVisit()
        {
            bool paused = _npControls?.Playing.IsVisible != true;
            if (paused && _mascotLastVisitKind != MascotVisit.Paused) return MascotVisit.Paused;
            if (LyricsShown && !paused && _mascotLastVisitKind != MascotVisit.Karaoke && Random.Shared.NextDouble() < MascotKaraokeChance) return MascotVisit.Karaoke;
            var kinds = Enum.GetValues<MascotVisit>().Where(k => k is not (MascotVisit.Paused or MascotVisit.Karaoke)).ToArray();
            return PickOther(kinds, _mascotLastVisitKind ?? MascotVisit.Paused, _mascotLastVisitKind != null);
        }

        private static T PickOther<T>(T[] choices, T last, bool avoidLast) where T : struct, Enum
        {
            var pool = avoidLast && choices.Length > 1 ? choices.Where(c => !c.Equals(last)).ToArray() : choices;
            return pool[Random.Shared.Next(pool.Length)];
        }

        private void StartMascotVisit(MascotVisit visit)
        {
            _mascotVisit = visit;
            _mascotLastVisitKind = visit;
            _mascotFromRight = Random.Shared.Next(2) == 0;
            switch (visit)
            {
                case MascotVisit.Peekaboo:
                    SetMascotState(MascotState.Peek);
                    UpdateMascotTimer();
                    break;
                case MascotVisit.Skateboard:
                    _mascotRunFrom = _mascotFromRight ? MascotWhooshEnd : MascotEnterStart;
                    SetMascotState(MascotState.Skate);
                    UpdateMascotTimer();
                    break;
                case MascotVisit.Superhero:
                    _mascotRunFrom = _mascotFromRight ? MascotWhooshEnd : MascotEnterStart;
                    SetMascotState(MascotState.Fly);
                    UpdateMascotTimer();
                    break;
                default:
                    StartMascotEnter(MascotState.Act, MascotWalkSpeed, _mascotFromRight);
                    break;
            }
        }

        private double MascotActLength() => _mascotVisit switch
        {
            MascotVisit.Polish => 5.5,
            MascotVisit.AirGuitar => 5.0,
            MascotVisit.Conductor => 5.5,
            MascotVisit.Paused => 4.0,
            MascotVisit.Selfie => 3.4,
            MascotVisit.Balloon => 6.8,
            MascotVisit.Fishing or MascotVisit.Climb or MascotVisit.SkipRope or MascotVisit.PaperPlane or MascotVisit.Sweep or MascotVisit.Karaoke => MascotIdleLength(),
            _ => MascotActSeconds
        };

        private double StepMascotAct(double s)
        {
            double offset = 0;
            double beat = s * 8;
            switch (_mascotVisit)
            {
                case MascotVisit.Dance:
                    CheerDance(s);
                    break;
                case MascotVisit.Loud:
                {
                    double pump = Math.Abs(Math.Sin(beat));
                    Pose(lean: Math.Sin(beat / 2) * 5, bounce: -pump * 10, arms: (110 + pump * 40, -110 - pump * 40), legs: (pump * 10, -pump * 10));
                    _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + pump * 0.18;
                    _mascotEyes.ScaleY = 0.6;
                    StepMascotNotes(s);
                    break;
                }
                case MascotVisit.Polish:
                {
                    if (s < 3.5)
                    {
                        double wipe = -100 + Math.Sin(s * 11) * 22;
                        Pose(lean: 10, bounce: -Math.Abs(Math.Sin(s * 11)) * 3, arms: (25, wipe), legs: (0, 0));
                        Center(_propCloth, Hand(true, wipe, 48));
                    }
                    else
                    {
                        double admire = s - 3.5;
                        Canvas.SetLeft(_mascot, MascotSpot - MascotSide * Math.Min(1, admire / 0.4) * 30);
                        Pose(lean: -4, bounce: -Math.Abs(Math.Sin(admire * 6)) * 4, arms: (35, -35), legs: (0, 0));
                        _mascotEyes.ScaleY = 0.75;
                        for (int i = 0; i < _glyphSparkles.Count; i++)
                        {
                            var sparkle = _glyphSparkles[i];
                            sparkle.IsVisible = true;
                            sparkle.Opacity = Math.Max(0, Math.Sin(admire * 5 + i * 2.1));
                            Canvas.SetLeft(sparkle, _mascotFromRight ? -50 - i * 60 : 230 + i * 60);
                            Canvas.SetTop(sparkle, -60 - i * 70 + (i % 2) * 40);
                        }
                    }
                    break;
                }
                case MascotVisit.AirGuitar:
                {
                    if (s < 3.2)
                    {
                        double windmill = -(s * 400 % 360);
                        Pose(lean: -6 + Math.Sin(s * 4) * 4, bounce: -Math.Abs(Math.Sin(s * 4)) * 8, arms: (82, windmill), legs: (14, -14));
                        _mascotEyes.ScaleY = 0.5;
                        _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + Math.Abs(Math.Sin(s * 8)) * 0.1;
                    }
                    else
                    {
                        double rock = s - 3.2;
                        offset = Math.Min(1, rock / 0.3) * 22;
                        Pose(lean: -10 + Math.Sin(rock * 4) * 6, bounce: 0, arms: (160, -160), legs: (35, -35));
                        _mascotEyes.ScaleY = 0.4;
                    }
                    Canvas.SetLeft(_propGuitar, -12);
                    Canvas.SetTop(_propGuitar, 112);
                    _propGuitar.IsVisible = true;
                    break;
                }
                case MascotVisit.Conductor:
                {
                    if (s < 4.3)
                    {
                        double amp = 12 + s / 4.3 * 38;
                        double wave = -120 + Math.Sin(s * 6) * amp;
                        Pose(lean: Math.Sin(s * 3) * 4, bounce: -Math.Abs(Math.Sin(s * 6)) * 4, arms: (120 - Math.Sin(s * 3) * amp * 0.6, wave), legs: (0, 0));
                        var hand = Hand(true, wave, 44);
                        var tip = Hand(true, wave, 90);
                        _propBaton.StartPoint = hand;
                        _propBaton.EndPoint = tip;
                        _propBaton.IsVisible = true;
                    }
                    else
                    {
                        double bow = Math.Sin(Math.Min(1, (s - 4.3) / 1.2) * Math.PI);
                        Pose(lean: 32 * bow, bounce: 0, arms: (10, -10), legs: (0, 0));
                    }
                    break;
                }
                case MascotVisit.Paused:
                {
                    if (s < 1.6)
                    {
                        double knock = -95 + (Math.Sin(s * 22) > 0 ? -12 : 4);
                        Pose(lean: 6, bounce: 0, arms: (20, knock), legs: (0, 0));
                    }
                    else
                    {
                        double shrug = Math.Max(0, Math.Sin((s - 1.6) * Math.PI * 1.2));
                        Pose(lean: 0, bounce: -shrug * 6, arms: (60 + shrug * 15, -60 - shrug * 15), legs: (0, 0));
                        _glyphQuestion.IsVisible = true;
                        _glyphQuestion.Opacity = Math.Min(1, (s - 1.6) / 0.3);
                        Canvas.SetLeft(_glyphQuestion, 88);
                        Canvas.SetTop(_glyphQuestion, -30 - shrug * 6);
                    }
                    break;
                }
                case MascotVisit.Selfie:
                {
                    var phone = Hand(false, 145, 52);
                    Pose(lean: 10, bounce: Math.Sin(s * 2) * 2, arms: (145, -30), legs: (-10, 0));
                    (_mascotGaze.X, _mascotGaze.Y) = (-6, -6);
                    Center(_propPhone, phone);
                    if (s is > 1.6 and < 2.1)
                    {
                        Center(_propFlash, phone);
                        _propFlash.Opacity = 1 - (s - 1.6) / 0.5;
                    }
                    if (s is > 1.4 and < 1.75) _mascotEyes.ScaleY = 0.15;
                    break;
                }
                case MascotVisit.Balloon:
                {
                    double kick = Math.Sin(s * 12);
                    if (s < 5.0)
                    {
                        offset = s < 1.2 ? 0 : s < 3.2 ? -50 * Ease((s - 1.2) / 2) : -50 - 850 * Math.Pow((s - 3.2) / 1.8, 2);
                        var hand = Hand(false, 165, 44);
                        Pose(lean: Math.Sin(s * 2) * 4, bounce: 0, arms: (165, -30), legs: s < 1.2 ? (0, 0) : (kick * 20, -kick * 20));
                        Canvas.SetLeft(_propBalloon, -14 + Math.Sin(s * 2.3) * 6);
                        Canvas.SetTop(_propBalloon, -112);
                        _propBalloon.IsVisible = true;
                        _propString.StartPoint = new Point(16 + Math.Sin(s * 2.3) * 6, -34);
                        _propString.EndPoint = hand;
                        _propString.IsVisible = true;
                    }
                    else if (s < 5.8) offset = -900;
                    else if (s < 6.4)
                    {
                        double fall = (s - 5.8) / 0.6;
                        offset = -700 * (1 - fall * fall);
                        Pose(lean: 0, bounce: 0, arms: (150 + kick * 25, -150 - kick * 25), legs: (kick * 25, -kick * 25));
                    }
                    else Pose(lean: 0, bounce: 6 * Math.Sin(Math.Min(1, (s - 6.4) / 0.4) * Math.PI), arms: (40, -40), legs: (-10, 10));
                    _mascotShadow.Opacity = offset < -10 ? 0 : 1;
                    break;
                }
                case MascotVisit.Fishing or MascotVisit.Climb or MascotVisit.SkipRope or MascotVisit.PaperPlane or MascotVisit.Sweep or MascotVisit.Karaoke:
                    offset = StepMascotIdle(s);
                    break;
                default:
                    Pose(lean: 0, bounce: Math.Sin(s * 2) * 2, arms: (20, -150 + Math.Sin(s * 10) * 28), legs: (0, 0));
                    break;
            }
            return offset;
        }

        private enum MascotExit { Stroll, Back, Dash, Hop, Moonwalk, Sneak, Rocket }

        private MascotExit _mascotExit;
        private bool _mascotExited;

        private void FinishMascotAct()
        {
            ClearMascotNotes();
            _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
            _mascotRunFrom = MascotSpot;
            if (_mascotVisit == MascotVisit.Balloon)
            {
                SetMascotState(MascotState.RunOff);
                return;
            }
            _mascotExit = PickOther(Enum.GetValues<MascotExit>(), _mascotExit, _mascotExited);
            _mascotExited = true;
            SetMascotState(_mascotExit == MascotExit.Stroll ? MascotState.Stroll : MascotState.Exit);
        }

        private double StepMascotExit(double s)
        {
            double dir = _mascotFromRight ? 1 : -1;
            double offset = 0;
            double speed = 0;
            _mascotFacing.ScaleX = dir;
            switch (_mascotExit)
            {
                case MascotExit.Back:
                {
                    speed = MascotWalkSpeed;
                    WalkPose(s, dir);
                    break;
                }
                case MascotExit.Dash:
                {
                    speed = s < 0.25 ? 0 : MascotRushSpeed;
                    _mascotSpeed.Opacity = s < 0.25 ? 0 : 1;
                    if (s < 0.25) Pose(lean: -10 * dir, bounce: 0, arms: (40, -40), legs: (14, -14));
                    else RunPose(s, dir, 18);
                    break;
                }
                case MascotExit.Hop:
                {
                    speed = 240;
                    double hop = Math.Abs(Math.Sin(s * 5.5));
                    offset = -hop * 70;
                    Pose(lean: 6 * dir, bounce: 0, arms: (150 - hop * 30, -150 + hop * 30), legs: (hop * 18, -hop * 18));
                    _mascotShadow.Opacity = 1 - hop * 0.6;
                    break;
                }
                case MascotExit.Moonwalk:
                {
                    speed = 150;
                    _mascotFacing.ScaleX = -dir;
                    double glide = Math.Sin(s * 6);
                    Pose(lean: -5 * dir, bounce: -Math.Abs(glide) * 3, arms: (20 + glide * 15, -20 + glide * 15), legs: (glide > 0 ? 0 : 22, glide > 0 ? -22 : 0));
                    break;
                }
                case MascotExit.Sneak:
                {
                    speed = 110;
                    double step = Math.Sin(s * 4.5);
                    offset = 14;
                    Pose(lean: 10 * dir, bounce: -Math.Abs(step) * 10, arms: (150, -150), legs: (step * 35, -step * 35));
                    _mascotEyes.ScaleY = 0.6;
                    (_mascotGaze.X, _mascotGaze.Y) = (dir * 4, 0);
                    break;
                }
                case MascotExit.Rocket:
                {
                    if (s < 0.45)
                    {
                        offset = 16 * Math.Sin(s / 0.45 * Math.PI / 2);
                        Pose(lean: 0, bounce: 0, arms: (35, -35), legs: (22, -22));
                    }
                    else
                    {
                        double up = s - 0.45;
                        offset = -up * up * 2600 - up * 400;
                        Pose(lean: 0, bounce: 0, arms: (175, -175), legs: (-6, 6));
                        _mascotShadow.Opacity = Math.Max(0, 1 - up * 4);
                        _mascotSpeed.Opacity = 0;
                    }
                    if (offset < -900) LeaveMascotStage();
                    return offset;
                }
            }
            double x = _mascotRunFrom + dir * Math.Max(0, s - (_mascotExit == MascotExit.Dash ? 0.25 : 0)) * speed;
            Canvas.SetLeft(_mascot, x);
            if (dir > 0 ? x >= MascotWhooshEnd : x <= MascotWalkTarget) LeaveMascotStage();
            return offset;
        }

        private void LeaveMascotStage()
        {
            SetMascotState(MascotState.Gone);
            UpdateMascotTimer();
        }

        private void StepMascotPeek(double s)
        {
            _mascot.ZIndex = -1;
            double outFor = s < 0.5 ? Ease(s / 0.5) : s < PeekSeconds - 0.5 ? 1 : Ease((PeekSeconds - s) / 0.5);
            double hidden = _mascotFromRight ? PeekHidden : MascotCoverLeft + 10;
            double peek = _mascotFromRight ? PeekOut : MascotCoverLeft - MascotWidth + 50;
            double side = _mascotFromRight ? 1 : -1;
            _mascotFacing.ScaleX = side;
            Canvas.SetLeft(_mascot, hidden + (peek - hidden) * outFor);
            Pose(lean: 10 * outFor * side, bounce: 0, arms: (10, -10), legs: (0, 0));
            if (s is > 1.0 and < 1.12 or > 1.6 and < 1.72 or > 1.85 and < 1.97) _mascotEyes.ScaleY = 0.12;
            if (s >= PeekSeconds)
            {
                SetMascotState(MascotState.Gone);
                UpdateMascotTimer();
            }
        }

        private double StepMascotFly(double s)
        {
            double dir = _mascotFromRight ? -1 : 1;
            double end = _mascotFromRight ? MascotEnterStart : MascotWhooshEnd;
            double p = Math.Min(1, s / FlySeconds);
            double u = 2 * p - 1;
            double x = _mascotRunFrom + (end - _mascotRunFrom) * p;
            double offset = -FlyLow - FlyHigh * u * u;
            double vx = (end - _mascotRunFrom) / FlySeconds;
            double vy = -FlyHigh * 2 * u * 2 / FlySeconds;
            Canvas.SetLeft(_mascot, x);
            _mascotFacing.ScaleX = dir;
            double lean = Math.Atan2(vx, -vy) * 180 / Math.PI;
            Pose(lean: lean, bounce: 0, arms: (180, -12), legs: (0, 0));
            double wave = Math.Sin(s * 26) * 14, ripple = Math.Sin(s * 26 + 1.4) * 10;
            _propCape.Points = [new Point(40, 44), new Point(160, 44), new Point(176 + ripple, 250), new Point(100 + wave, 300), new Point(24 - ripple, 250)];
            _propCape.IsVisible = true;
            _mascotShadow.Opacity = Math.Max(0, 1 + (offset + FlyLow) / 200) * 0.7;
            if (p >= 1) LeaveMascotStage();
            return offset;
        }

        private void StepMascotSkate(double s)
        {
            double dir = _mascotFromRight ? -1 : 1;
            double x = _mascotRunFrom + dir * s * SkateSpeed;
            Canvas.SetLeft(_mascot, x);
            _mascotFacing.ScaleX = dir;
            double ollie = Math.Abs(SkateOllieAt - _mascotRunFrom) / SkateSpeed;
            Canvas.SetLeft(_propBoard, 25);
            Canvas.SetTop(_propBoard, 234);
            _propBoard.IsVisible = true;
            if (s < ollie) Pose(lean: 0, bounce: 0, arms: (60, -60), legs: (-10, 10));
            else if (s < ollie + SkateJumpSeconds)
            {
                double p = (s - ollie) / SkateJumpSeconds;
                Pose(lean: (-6 + 14 * p) * dir, bounce: -Math.Sin(p * Math.PI) * 90, arms: (130, -130), legs: (-20, 20));
            }
            else
            {
                double tumble = s - ollie - SkateJumpSeconds;
                double flail = Math.Sin(tumble * 30);
                Pose(lean: tumble * 620 * dir, bounce: -Math.Abs(Math.Sin(tumble * 8)) * 26, arms: (140 + flail * 30, -140 - flail * 30), legs: (flail * 30, -flail * 30));
                _mascotSpeed.Opacity = 1;
            }
            if (dir > 0 ? x >= MascotWhooshEnd : x <= MascotWalkTarget)
            {
                SetMascotState(MascotState.Gone);
                UpdateMascotTimer();
            }
        }
    }
}
