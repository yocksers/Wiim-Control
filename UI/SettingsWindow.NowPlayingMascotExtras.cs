using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double ClawSeconds = 5.2;
        private const double ClawLift = 820;
        private const double ClawGripTop = -30;
        private const double ClawSize = 1.7;
        private const double ConsoleWidth = 56;
        private const double ConsoleHeight = 62;
        private const double SleeveSeconds = 3.8;
        private const double SleeveSize = 640;
        private const double JigsawSeconds = 5.2;
        private const int JigsawSide = 3;
        private const double JigsawPiece = StageCover / JigsawSide;
        private const double CatapultSeconds = 4.8;
        private const double CatapultLeft = 840;
        private const double CatapultOutside = MascotStageWidth + 80;
        private const double CatapultArm = 120;
        private const double CatapultPivotHeight = 80;
        private const double CatapultCoverScale = 0.18;
        private const double CatapultDropAt = 2.6;
        private const double CatapultDropSeconds = 0.9;
        private const double CatapultLanding = CatapultDropAt + CatapultDropSeconds / 2.75;
        private const double LandingDustSeconds = 1.3;
        private const int LandingDustPuffs = 24;
        private const double JuggleSeconds = 6.0;
        private const double KiteSeconds = 7.6;
        private const double BananaWalkSeconds = 0.4;
        private const double BananaSlideSpeed = 900;
        private const double TeleportSeconds = 1.6;
        private const double MascotGrabDistance = 8;
        private const double MascotGravity = 2600;
        private const double HighFiveSeconds = 1.5;

        private readonly Line _clawCable = new() { Stroke = new SolidColorBrush(Color.Parse("#8A9098")), StrokeThickness = 3, IsVisible = false };
        private readonly Canvas _claw = new()
        {
            Width = 120, Height = 100, IsVisible = false, RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(ClawSize, ClawSize)
        };
        private readonly Border _clawConsole = new()
        {
            Width = ConsoleWidth, Height = ConsoleHeight, CornerRadius = new CornerRadius(6, 6, 2, 2), IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#D0342C")), BorderBrush = new SolidColorBrush(Color.Parse("#7E1F1A")), BorderThickness = new Thickness(2)
        };
        private readonly Line _clawStick = new() { Stroke = new SolidColorBrush(Color.Parse("#2A2C30")), StrokeThickness = 6, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Ellipse _clawKnob = new() { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.Parse("#E8B829")), IsVisible = false };
        private readonly RotateTransform _clawLeft = new();
        private readonly RotateTransform _clawRight = new();
        private readonly Border _sleeve = new()
        {
            Width = SleeveSize, Height = SleeveSize, CornerRadius = new CornerRadius(4), IsVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#3A332C"), 0), new GradientStop(Color.Parse("#1C1815"), 1) }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(2)
        };
        private readonly Canvas _sleeveDisc = new() { Width = 560, Height = 560, IsVisible = false };
        private readonly List<(Border Piece, RotateTransform Turn, int Slot, double Spin, double Height)> _jigsaw = [];
        private readonly Canvas _catapult = new() { Width = 190, Height = 110, IsVisible = false };
        private readonly Rectangle _catapultArm = new()
        {
            Width = CatapultArm, Height = 10, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.Parse("#A0753F")), IsVisible = false,
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = new RotateTransform()
        };
        private readonly Ellipse _catapultCup = new() { Width = 48, Height = 22, Fill = new SolidColorBrush(Color.Parse("#6B4A2E")), IsVisible = false };
        private readonly Line _catapultRope = new() { Stroke = new SolidColorBrush(Color.Parse("#D9C9A3")), StrokeThickness = 2.5, IsVisible = false };
        private readonly List<Ellipse> _juggleBalls = [];
        private readonly List<(Ellipse Puff, double Along, double Rise, double Grow, double Delay, bool Front)> _landingDust = [];
        private readonly Canvas _kite = new() { Width = 70, Height = 90, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform() };
        private readonly Line _kiteString = new() { Stroke = new SolidColorBrush(Color.FromArgb(200, 230, 232, 236)), StrokeThickness = 1.5, IsVisible = false };
        private readonly Polyline _kiteTail = new() { Stroke = Palette.AccentText, StrokeThickness = 3, IsVisible = false };
        private readonly Canvas _bananaPeel = new() { Width = 60, Height = 26, IsVisible = false };
        private readonly Rectangle _teleportBeam = new()
        {
            Width = 170, Height = 1200, IsVisible = false, IsHitTestVisible = false,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 170, 220, 255), 0), new GradientStop(Color.FromArgb(170, 200, 235, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 170, 220, 255), 1)
                }
            }
        };
        private readonly TextBlock _glyphShout = new() { Text = "Hey!", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Brushes.White, IsVisible = false };
        private readonly TextBlock _glyphHighFive = new() { Text = "High five!", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Palette.AccentText, IsVisible = false };

        private Point _mascotPress;
        private Point _mascotGrab;
        private Point _mascotPointer;
        private bool _mascotPressing;
        private double _mascotHeldLastX;
        private double _mascotSwing;
        private double _mascotDropTop;
        private Point _catapultLaunch;
        private bool _mascotDone;
        private Control[] _extrasProps = [];
        private readonly Points[] _kiteTails = [[], []];
        private int _kiteTailTurn;

        private static void HideAll(Control[] props)
        {
            foreach (var prop in props)
                if (prop.IsVisible) prop.IsVisible = false;
        }

        private bool MascotHoldable => _mascotState is MascotState.Dance or MascotState.WalkOff or MascotState.React or MascotState.Poke or MascotState.Act
            or MascotState.Stroll or MascotState.Dizzy or MascotState.RunOff or MascotState.Exit or MascotState.HighFive;

        private void BuildMascotExtras(Canvas figure)
        {
            var metal = new SolidColorBrush(Color.Parse("#B8BDC4"));
            _claw.Children.Add(Place(new Rectangle { Width = 60, Height = 28, RadiusX = 8, RadiusY = 8, Fill = metal }, 30, 0));
            _claw.Children.Add(Place(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse("#5A6068")) }, 52, 6));
            foreach (var (turn, x, hookX) in new[] { (_clawLeft, 38.0, 0.0), (_clawRight, 74.0, -10.0) })
            {
                var prong = new Canvas
                {
                    Width = 8, Height = 64, RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative), RenderTransform = turn
                };
                prong.Children.Add(new Rectangle { Width = 8, Height = 64, RadiusX = 3, RadiusY = 3, Fill = metal });
                prong.Children.Add(Place(new Rectangle { Width = 18, Height = 8, RadiusX = 3, RadiusY = 3, Fill = metal }, hookX, 58));
                _claw.Children.Add(Place(prong, x, 22));
            }

            _sleeve.Child = new Canvas
            {
                Children =
                {
                    Place(new Ellipse { Width = 420, Height = 420, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 2 }, 108, 108),
                    Place(new TextBlock { Text = "♫", FontSize = 64, Foreground = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) }, 290, 270)
                }
            };
            _sleeveDisc.Children.Add(new Ellipse
            {
                Width = 560, Height = 560,
                Fill = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.Parse("#2A2A2E"), 0), new GradientStop(Color.Parse("#0D0D0F"), 0.6), new GradientStop(Color.Parse("#1E1E22"), 1) }
                }
            });
            foreach (double ring in new[] { 480.0, 400.0, 320.0 })
                _sleeveDisc.Children.Add(Place(new Ellipse { Width = ring, Height = ring, Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), StrokeThickness = 1 },
                    (560 - ring) / 2, (560 - ring) / 2));
            _sleeveDisc.Children.Add(Place(new Ellipse { Width = 170, Height = 170, Fill = Palette.Accent }, 195, 195));

            for (int slot = 0; slot < JigsawSide * JigsawSide; slot++)
            {
                var turn = new RotateTransform();
                var piece = new Border
                {
                    Width = JigsawPiece, Height = JigsawPiece, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = turn,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), BorderThickness = new Thickness(1.5)
                };
                _jigsaw.Add((piece, turn, slot, 0, 0));
            }

            var wood = new SolidColorBrush(Color.Parse("#8B5E34"));
            var dark = new SolidColorBrush(Color.Parse("#4E3420"));
            _catapult.Children.Add(Place(new Polygon { Points = [new Point(30, 74), new Point(60, CatapultPivotHeight - 50), new Point(90, 74)], Fill = dark }, 0, 0));
            _catapult.Children.Add(Place(new Rectangle { Width = 190, Height = 18, RadiusX = 4, RadiusY = 4, Fill = wood }, 0, 70));
            foreach (double x in new[] { 14.0, 142.0 })
                _catapult.Children.Add(Place(new Ellipse { Width = 34, Height = 34, Fill = dark, Stroke = wood, StrokeThickness = 4 }, x, 76));

            var colors = new IBrush[] { Palette.Accent, new SolidColorBrush(Color.Parse("#E8B829")), new SolidColorBrush(Color.Parse("#D0342C")) };
            foreach (var color in colors)
            {
                var ball = new Ellipse { Width = 24, Height = 24, Fill = color, IsVisible = false };
                _juggleBalls.Add(ball);
                figure.Children.Add(ball);
            }

            _kite.Children.Add(new Polygon { Points = [new Point(35, 0), new Point(70, 36), new Point(35, 36)], Fill = Palette.Accent });
            _kite.Children.Add(new Polygon { Points = [new Point(35, 0), new Point(0, 36), new Point(35, 36)], Fill = new SolidColorBrush(Color.Parse("#E8B829")) });
            _kite.Children.Add(new Polygon { Points = [new Point(0, 36), new Point(35, 90), new Point(35, 36)], Fill = new SolidColorBrush(Color.Parse("#D0342C")) });
            _kite.Children.Add(new Polygon { Points = [new Point(70, 36), new Point(35, 90), new Point(35, 36)], Fill = Palette.AccentText });

            _bananaPeel.Children.Add(new Polygon
            {
                Points = [new Point(0, 24), new Point(10, 8), new Point(22, 18), new Point(30, 2), new Point(38, 18), new Point(50, 8), new Point(60, 24)],
                Fill = new SolidColorBrush(Color.Parse("#F2D23C")), Stroke = new SolidColorBrush(Color.Parse("#B8962A")), StrokeThickness = 1.5
            });
            _bananaPeel.Children.Add(Place(new Rectangle { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.Parse("#5E4A1E")) }, 27, 0));

            foreach (var prop in new Control[] { _sleeveDisc, _sleeve, _clawCable, _claw, _clawConsole, _clawStick, _clawKnob, _catapult, _catapultRope, _catapultArm, _catapultCup, _kiteString, _kiteTail, _kite, _bananaPeel })
                _mascotFx.Children.Add(prop);
            _mascotFxBack.Children.Add(_teleportBeam);
            foreach (var (piece, _, _, _, _) in _jigsaw) _mascotFx.Children.Add(piece);
            for (int i = 0; i < LandingDustPuffs; i++)
            {
                var puff = new Ellipse
                {
                    IsVisible = false,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = { new GradientStop(Color.FromArgb(200, 214, 202, 178), 0), new GradientStop(Color.FromArgb(0, 214, 202, 178), 1) }
                    }
                };
                bool front = i % 3 == 0;
                double along = front ? (i % 2 == 0 ? 0.02 : 0.98) + (Random.Shared.NextDouble() - 0.5) * 0.08 : Random.Shared.NextDouble();
                _landingDust.Add((puff, along, 0.3 + Random.Shared.NextDouble() * 0.7, 0.7 + Random.Shared.NextDouble() * 0.6, Random.Shared.NextDouble() * 0.15, front));
                (front ? _mascotFx : _mascotFxBack).Children.Add(puff);
            }
            _mascotGlyphs.Children.Add(_glyphShout);
            _mascotGlyphs.Children.Add(_glyphHighFive);

            _mascot.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (_mascotDone || !e.GetCurrentPoint(_mascot).Properties.IsLeftButtonPressed) return;
                PokeMascot();
                if (_mascotDone) return;
                if (!MascotHoldable) return;
                _mascotPress = e.GetPosition(_npStage);
                _mascotGrab = e.GetPosition(_mascot);
                _mascotPressing = true;
                e.Pointer.Capture(_mascot);
            };
            _mascot.PointerMoved += (_, e) =>
            {
                if (!_mascotPressing) return;
                var at = e.GetPosition(_npStage);
                if (_mascotState != MascotState.Held)
                {
                    if (Point.Distance(at, _mascotPress) < MascotGrabDistance || !MascotHoldable) return;
                    StartMascotHold(at);
                }
                _mascotPointer = at;
            };
            _mascot.PointerReleased += (_, e) =>
            {
                EndMascotPress();
                e.Pointer.Capture(null);
            };
            _mascot.PointerCaptureLost += (_, _) => EndMascotPress();
            _extrasProps =
            [
                _clawCable, _claw, _clawConsole, _clawStick, _clawKnob, _sleeve, _sleeveDisc, _catapult, _catapultArm, _catapultCup, _catapultRope, _kite, _kiteString,
                _kiteTail, _bananaPeel, _teleportBeam, _glyphShout, _glyphHighFive, _glyphAlarm, .. _spinArcs,
                .. _jigsaw.Select(j => (Control)j.Piece), .. _juggleBalls, .. _landingDust.Select(d => (Control)d.Puff)
            ];
        }

        private void HideMascotExtras() => HideAll(_extrasProps);

        private Point StageHand(bool right, double angle, double reach = 44)
        {
            var hand = Hand(right, angle, reach);
            double x = _mascotFacing.ScaleX < 0 ? MascotWidth - hand.X : hand.X;
            return new Point(Canvas.GetLeft(_mascot) + x, Canvas.GetTop(_mascot) + hand.Y);
        }

        private RenderTargetBitmap? _jigsawSnapshot;
        private const double JigsawSharpness = 2;

        private Bitmap? SnapshotNewCover()
        {
            var size = _npCoverContent.Bounds.Size;
            if (size.Width < 1 || size.Height < 1) return null;
            double old = _npCoverOld.Opacity;
            _npCoverOld.Opacity = 0;
            try
            {
                var snapshot = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * JigsawSharpness), (int)Math.Ceiling(size.Height * JigsawSharpness)),
                    new Vector(96 * JigsawSharpness, 96 * JigsawSharpness));
                WithHeldTextShown(() =>
                {
                    snapshot.Render(_npCoverContent);
                    return true;
                });
                _jigsawSnapshot?.Dispose();
                _jigsawSnapshot = snapshot;
                return snapshot;
            }
            catch
            {
                return null;
            }
            finally
            {
                _npCoverOld.Opacity = old;
            }
        }

        private bool PrepareJigsaw()
        {
            if ((SnapshotNewCover() ?? _npCover.Source as Bitmap) is not { } art) return false;
            var order = Enumerable.Range(0, _jigsaw.Count).OrderBy(_ => Random.Shared.Next()).ToList();
            for (int i = 0; i < _jigsaw.Count; i++)
            {
                var (piece, turn, _, _, _) = _jigsaw[i];
                int slot = order[i];
                piece.Background = new ImageBrush(art)
                {
                    Stretch = Stretch.Fill,
                    SourceRect = new RelativeRect((slot % JigsawSide) / (double)JigsawSide, (slot / JigsawSide) / (double)JigsawSide,
                        1.0 / JigsawSide, 1.0 / JigsawSide, RelativeUnit.Relative)
                };
                _jigsaw[i] = (piece, turn, slot, Random.Shared.NextDouble() * 80 - 40, 260 + Random.Shared.NextDouble() * 200);
            }
            return true;
        }

        private static Point JigsawSlot(int slot) =>
            new(MascotCoverLeft + (slot % JigsawSide) * JigsawPiece, (slot / JigsawSide) * JigsawPiece);

        private double StepMascotClaw(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double lift = s < 1.4 ? 0 : s < 2.4 ? Ease(Phase(s, 1.4, 1.0)) : s < 3.6 ? 1 - Ease(Phase(s, 2.4, 1.2)) : 0;
            double sway = s is > 2.4 and < 3.6 ? Math.Sin(s * 7) * 14 * (1 - Phase(s, 2.4, 1.2)) : 0;
            double clawTop = s < 1.0 ? -320 + (ClawGripTop + 320) * Ease(Phase(s, 0, 1.0))
                : s < 3.8 ? ClawGripTop - lift * ClawLift
                : ClawGripTop - Ease(Phase(s, 3.8, 0.9)) * 400;
            double x = CoverMiddle.X + sway;
            Canvas.SetLeft(_claw, x - 60);
            Canvas.SetTop(_claw, clawTop);
            _claw.IsVisible = true;
            _clawCable.StartPoint = new Point(x, -1200);
            _clawCable.EndPoint = new Point(x, clawTop + 2);
            _clawCable.IsVisible = true;
            double grip = s < 1.0 ? 0 : s < 1.4 ? Ease(Phase(s, 1.0, 0.4)) : s < 3.6 ? 1 : 1 - Ease(Phase(s, 3.6, 0.3));
            _clawLeft.Angle = 32 - 28 * grip;
            _clawRight.Angle = -32 + 28 * grip;
            PlaceCover(new Point(CoverMiddle.X + sway, CoverMiddle.Y - lift * ClawLift), 1);
            if (s >= 2.4) ShowNewArt(Phase(s, 2.4, 1.2));

            if (s < 3.8)
            {
                double stick = s is > 1.0 and < 3.6 ? Math.Sin(s * 9) : 0;
                Pose(lean: 0, bounce: 0, arms: (35 + stick * 12, -150 + Math.Sin(s * 5) * 12), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (5, -6);
                var hand = StageHand(false, 35 + stick * 12, 46);
                double floor = MascotOffsetTop() + MascotHeight;
                Canvas.SetLeft(_clawConsole, hand.X - ConsoleWidth / 2);
                Canvas.SetTop(_clawConsole, floor - ConsoleHeight);
                _clawConsole.IsVisible = true;
                _clawStick.StartPoint = new Point(hand.X, floor - ConsoleHeight + 4);
                _clawStick.EndPoint = hand;
                _clawStick.IsVisible = true;
                Center(_clawKnob, hand);
            }
            else CheerDance(s);
            if (s >= ClawSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotSleeve(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double up = Ease(Phase(s, 0, 0.9));
            double down = Ease(Phase(s, 1.6, 1.0));
            double sleeveTop = StageCover + 60 + (-20 - StageCover - 60) * up + (StageCover + 80) * down;
            Canvas.SetLeft(_sleeve, MascotCoverLeft - 20);
            Canvas.SetTop(_sleeve, sleeveTop);
            _sleeve.IsVisible = true;
            _sleeve.Opacity = Math.Min(1, s / 0.2) * (1 - Phase(s, 2.6, 0.4));
            double peek = s is > 0.9 and < 1.6 ? Math.Sin(Phase(s, 0.9, 0.7) * Math.PI) : 0;
            Canvas.SetLeft(_sleeveDisc, MascotCoverLeft + 20 + peek * 300);
            Canvas.SetTop(_sleeveDisc, sleeveTop + 40);
            _sleeveDisc.IsVisible = peek > 0;
            if (s >= 0.9) ShowNewArt(down);

            if (s < 0.9) Pose(lean: -6, bounce: 0, arms: (-150, -160), legs: (0, 0));
            else if (s < 1.6)
            {
                Pose(lean: 0, bounce: -Math.Abs(Math.Sin(s * 8)) * 4, arms: (40, -40), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -4);
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.2;
            }
            else if (s < 2.6) Pose(lean: -14 * down, bounce: 0, arms: (-60, -70), legs: (-10, 10));
            else CheerDance(s);
            if (s >= SleeveSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotJigsaw(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            int count = _jigsaw.Count;
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                var (piece, turn, slot, spin, height) = _jigsaw[i];
                var target = JigsawSlot(slot);
                bool last = i == count - 1;
                double start = last ? 3.2 : 0.3 + i * 0.32, length = last ? 0.6 : 0.45;
                double p = Phase(s, start, length);
                if (s < (last ? 2.9 : start))
                {
                    piece.IsVisible = false;
                    continue;
                }
                piece.IsVisible = s < 4.4;
                if (last && s < start)
                {
                    var hand = StageHand(true, -150, 46);
                    Canvas.SetLeft(piece, hand.X - JigsawPiece / 2);
                    Canvas.SetTop(piece, hand.Y - JigsawPiece / 2 - 30);
                    piece.Width = piece.Height = JigsawPiece;
                    turn.Angle = -15;
                    continue;
                }
                var from = last ? StageHand(true, -150, 46) - new Point(JigsawPiece / 2, JigsawPiece / 2 + 30) : new Point(target.X, target.Y - height);
                double e = last ? Ease(p) : p * p;
                Canvas.SetLeft(piece, from.X + (target.X - from.X) * e);
                Canvas.SetTop(piece, from.Y + (target.Y - from.Y) * e - (last ? Math.Sin(p * Math.PI) * 160 : 0));
                turn.Angle = (last ? -15 : spin) * (1 - p);
                if (p >= 1) placed++;
            }
            if (_npBackdropOld.Source != null) BlendBackdrops(1 - placed / (double)count);
            if (s >= 4.3) ShowNewArt(1);

            if (s < 2.9)
            {
                Pose(lean: 0, bounce: Math.Sin(s * 2) * 2, arms: (30, -30), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -3);
            }
            else if (s < 3.2) Pose(lean: -8, bounce: 0, arms: (30, -150), legs: (0, 0));
            else if (s < 3.8) Pose(lean: 12, bounce: -Math.Sin(Phase(s, 3.2, 0.6) * Math.PI) * 40, arms: (60, -100 + Phase(s, 3.2, 0.6) * 60), legs: (-12, 12));
            else
            {
                CheerDance(s);
                for (int i = 0; i < _glyphSparkles.Count && s < 4.6; i++)
                {
                    var sparkle = _glyphSparkles[i];
                    sparkle.IsVisible = true;
                    sparkle.Opacity = 1 - Phase(s, 3.8, 0.8);
                    Canvas.SetLeft(sparkle, 10 + i * 70);
                    Canvas.SetTop(sparkle, -30 - (i % 2) * 40 - Phase(s, 3.8, 0.8) * 40);
                }
            }
            if (s >= JigsawSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private static double BounceIn(double p)
        {
            if (p < 1 / 2.75) return 7.5625 * p * p;
            if (p < 2 / 2.75) return 7.5625 * (p -= 1.5 / 2.75) * p + 0.75;
            if (p < 2.5 / 2.75) return 7.5625 * (p -= 2.25 / 2.75) * p + 0.9375;
            return 7.5625 * (p -= 2.625 / 2.75) * p + 0.984375;
        }

        private double StepMascotCatapult(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double catX = s < 0.6 ? CatapultOutside + (CatapultLeft - CatapultOutside) * Ease(s / 0.6)
                : s < 3.6 ? CatapultLeft : CatapultLeft + (CatapultOutside - CatapultLeft) * Phase(s, 3.6, 0.6) * Phase(s, 3.6, 0.6);
            Canvas.SetLeft(_catapult, catX);
            Canvas.SetTop(_catapult, StageCover - 110);
            _catapult.IsVisible = true;
            var pivot = new Point(catX + 60, StageCover - 110 + CatapultPivotHeight - 50);
            double swing = Ease(Phase(s, 1.45, 0.3));
            double angle = -15 + 125 * swing;
            double radians = angle * Math.PI / 180;
            var cup = new Point(pivot.X + Math.Cos(radians) * CatapultArm, pivot.Y - Math.Sin(radians) * CatapultArm);
            Canvas.SetLeft(_catapultArm, pivot.X);
            Canvas.SetTop(_catapultArm, pivot.Y - 5);
            ((RotateTransform)_catapultArm.RenderTransform!).Angle = -angle;
            _catapultArm.IsVisible = true;
            Center(_catapultCup, cup);

            var rest = new Point(pivot.X + Math.Cos(-15 * Math.PI / 180) * CatapultArm, pivot.Y + Math.Sin(15 * Math.PI / 180) * CatapultArm - 22);
            if (s < 0.6) PlaceCover(CoverMiddle, 1);
            else if (s < 1.4) PlaceCover(Lerp(CoverMiddle, rest, Ease(Phase(s, 0.6, 0.8))), 1 + (CatapultCoverScale - 1) * Ease(Phase(s, 0.6, 0.8)));
            else if (s < 1.68)
            {
                _catapultLaunch = new Point(cup.X, cup.Y - 22);
                PlaceCover(_catapultLaunch, CatapultCoverScale);
            }
            else if (s < 2.5)
            {
                double t = s - 1.68;
                PlaceCover(new Point(_catapultLaunch.X - 700 * t, _catapultLaunch.Y - 1900 * t + 0.5 * 1800 * t * t), CatapultCoverScale);
            }
            else
            {
                ShowNewArt(Phase(s, 2.5, 0.9));
                double drop = BounceIn(Phase(s, CatapultDropAt, CatapultDropSeconds));
                PlaceCover(new Point(CoverMiddle.X, -700 + (CoverMiddle.Y + 700) * drop), s < CatapultDropAt ? 0.01 : 1);
                StepLandingDust(s - CatapultLanding);
            }

            var hand = StageHand(true, -80, 46);
            _catapultRope.StartPoint = hand;
            _catapultRope.EndPoint = new Point(catX + 34, StageCover - 40);
            _catapultRope.IsVisible = s is > 0.6 and < 2.5;
            if (s < 1.4)
            {
                Pose(lean: 0, bounce: Math.Sin(s * 2) * 2, arms: (30, -80), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, 0);
            }
            else if (s < 1.75) Pose(lean: -18, bounce: 0, arms: (40, -60), legs: (-16, 16));
            else if (s < 3.3)
            {
                Pose(lean: -4, bounce: 0, arms: (60, -60), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (s < 2.5 ? -3 : 5, -6);
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.3;
            }
            else if (s < 3.7) Pose(lean: 0, bounce: -Math.Sin(Phase(s, 3.3, 0.4) * Math.PI) * 50, arms: (150, -150), legs: (-20, 20));
            else CheerDance(s);
            if (s >= CatapultSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private void StepLandingDust(double since)
        {
            foreach (var (puff, along, rise, grow, delay, front) in _landingDust)
            {
                double age = (since - delay) / LandingDustSeconds;
                if (age is < 0 or >= 1) continue;
                double spread = 1 - Math.Pow(1 - age, 3);
                double side = along - 0.5;
                double outward = Math.Sign(side) * (front ? 50 + 90 * rise : 70 + Math.Abs(side) * 2 * 260) * spread;
                double swirl = Math.Sin(age * Math.PI * 2.5 + along * 9) * 18 * spread;
                double size = (front ? 40 + 90 * spread : 50 + 130 * spread) * grow;
                double x = MascotCoverLeft + along * StageCover + outward + swirl;
                double y = StageCover - 12 - (front ? 15 + 60 * rise : 25 + 170 * rise) * spread + Math.Cos(age * Math.PI * 2 + along * 7) * 8 * spread;
                puff.Width = puff.Height = size;
                Canvas.SetLeft(puff, x - size / 2);
                Canvas.SetTop(puff, y - size / 2);
                puff.Opacity = Math.Min(1, age * 8) * (1 - age) * 0.9;
                puff.IsVisible = true;
            }
        }

        private double StepJuggle(double s)
        {
            double appear = Phase(s, 0, 0.4), finish = Phase(s, JuggleSeconds - 0.9, 0.5);
            double toss = Math.Sin(s * Math.PI * 2 * 1.5);
            var left = Hand(false, 140 + toss * 10, 46);
            var right = Hand(true, -140 + toss * 10, 46);
            for (int i = 0; i < _juggleBalls.Count; i++)
            {
                double u = (s * 0.5 + i / 3.0) % 1;
                Point flying;
                if (u < 0.7)
                {
                    double p = u / 0.7;
                    flying = new Point(left.X + (right.X - left.X) * p, left.Y - Math.Sin(p * Math.PI) * 210 * appear);
                }
                else
                {
                    double p = (u - 0.7) / 0.3;
                    flying = new Point(right.X + (left.X - right.X) * p, right.Y + Math.Sin(p * Math.PI) * 55);
                }
                Center(_juggleBalls[i], Lerp(flying, i % 2 == 0 ? left : right, finish));
                _juggleBalls[i].IsVisible = finish < 1;
            }
            if (finish < 1)
            {
                Pose(lean: 0, bounce: -Math.Abs(toss) * 3, arms: (140 + toss * 10, -140 + toss * 10), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (Math.Cos(s * Math.PI) * 4, -7);
            }
            else
            {
                double bow = Math.Sin(Phase(s, JuggleSeconds - 0.4, 0.4) * Math.PI);
                Pose(lean: 28 * bow * MascotSide, bounce: 0, arms: (20, -20), legs: (0, 0));
            }
            return 0;
        }

        private double StepKite(double s)
        {
            double offset = 0;
            double rise = Ease(Phase(s, 0, 1.6));
            double gust = Phase(s, 5.6, 0.5);
            double away = Phase(s, 6.1, 1.4);
            if (gust > 0 && away <= 0) offset = -Math.Sin(gust * Math.PI) * 60;
            double side = MascotSide;
            double baseX = Canvas.GetLeft(_mascot) + MascotWidth / 2 + side * 60;
            var hand = StageHand(false, 150, 46);
            var high = new Point(baseX + side * 40 + Math.Sin(s * 1.3) * 50, 30 + Math.Sin(s * 2.1) * 26);
            var kite = Lerp(hand, high, rise);
            kite = new Point(kite.X + side * away * away * 900, kite.Y - away * away * 700);
            Canvas.SetLeft(_kite, kite.X - 35);
            Canvas.SetTop(_kite, kite.Y - 45);
            ((RotateTransform)_kite.RenderTransform!).Angle = Math.Sin(s * 2.1) * 14;
            _kite.IsVisible = away < 1;
            var tail = _kiteTails[_kiteTailTurn ^= 1];
            tail.Clear();
            for (int i = 0; i <= 8; i++) tail.Add(new Point(kite.X + Math.Sin(s * 6 + i * 0.8) * 10, kite.Y + 45 + i * 13));
            _kiteTail.Points = tail;
            _kiteTail.IsVisible = away < 1;
            _kiteString.StartPoint = hand;
            _kiteString.EndPoint = new Point(kite.X, kite.Y + 40);
            _kiteString.IsVisible = away <= 0;

            if (s < 1.6) RunPose(s, side, 6);
            else if (away <= 0)
            {
                double tug = Math.Sin(s * 2.1);
                Pose(lean: -8 * side + tug * 4, bounce: 0, arms: (150 + tug * 10, -40), legs: gust > 0 ? (Math.Sin(s * 20) * 25, -Math.Sin(s * 20) * 25) : (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (3 * side, -6);
            }
            else
            {
                double shrug = Math.Max(0, Math.Sin(Phase(s, 6.2, 1.2) * Math.PI * 1.5));
                Pose(lean: 0, bounce: -shrug * 6, arms: (60 + shrug * 15, -60 - shrug * 15), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (3 * side, -7);
            }
            _mascotShadow.Opacity = offset < -10 ? 0.4 : 1;
            return offset;
        }

        private double StepBananaExit(double s, double dir)
        {
            double floor = MascotOffsetTop();
            double peelX = _mascotRunFrom + MascotWidth / 2 + dir * (MascotWalkSpeed * BananaWalkSeconds + 40);
            Canvas.SetLeft(_bananaPeel, peelX - 30);
            Canvas.SetTop(_bananaPeel, floor + MascotHeight - 30);
            _bananaPeel.IsVisible = true;
            double x;
            double offset = 0;
            if (s < BananaWalkSeconds)
            {
                x = _mascotRunFrom + dir * s * MascotWalkSpeed;
                WalkPose(s, dir);
            }
            else if (s < BananaWalkSeconds + 0.45)
            {
                double p = Phase(s, BananaWalkSeconds, 0.45);
                x = _mascotRunFrom + dir * (BananaWalkSeconds * MascotWalkSpeed + p * 60);
                offset = -Math.Sin(p * Math.PI) * 50 - 80 * Ease(p);
                double flail = Math.Sin(s * 30);
                Pose(lean: -dir * 100 * Ease(p), bounce: 0, arms: (150 + flail * 25, -150 - flail * 25), legs: (-dir * 70 * p, -dir * 50 * p));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.35;
            }
            else
            {
                double slide = s - BananaWalkSeconds - 0.45;
                x = _mascotRunFrom + dir * (BananaWalkSeconds * MascotWalkSpeed + 60 + slide * BananaSlideSpeed);
                offset = -80;
                Pose(lean: -dir * 100, bounce: 0, arms: (170, -170), legs: (-dir * 60, -dir * 40));
                _mascotEyes.ScaleY = 0.2;
                _mascotSpeed.Opacity = 1;
                _bananaPeel.Opacity = Math.Max(0, 1 - slide / 0.8);
            }
            if (s < BananaWalkSeconds + 0.45) _bananaPeel.Opacity = 1;
            Canvas.SetLeft(_mascot, x);
            _mascotFacing.ScaleX = dir;
            if (dir > 0 ? x >= MascotWhooshEnd : x <= MascotWalkTarget) LeaveMascotStage();
            return offset;
        }

        private double StepTeleportExit(double s)
        {
            Canvas.SetLeft(_mascot, _mascotRunFrom);
            double charge = Phase(s, 0, 0.6), squeeze = Ease(Phase(s, 0.6, 0.6)), fade = Phase(s, 1.2, 0.4);
            Pose(lean: 0, bounce: -Math.Abs(Math.Sin(s * 18)) * 3 * charge, arms: (150, -150), legs: (0, 0));
            _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1 + 0.3 * charge;
            for (int i = 0; i < _glyphSparkles.Count; i++)
            {
                var sparkle = _glyphSparkles[i];
                double turn = s * 6 + i * Math.PI * 2 / _glyphSparkles.Count;
                sparkle.IsVisible = fade < 1;
                sparkle.Opacity = charge * (1 - fade);
                Canvas.SetLeft(sparkle, 88 + Math.Cos(turn) * 105);
                Canvas.SetTop(sparkle, 100 + Math.Sin(turn) * 100);
            }
            _mascotScale.ScaleX = Math.Max(0.001, 1 - squeeze);
            _mascotScale.ScaleY = 1 + 0.4 * squeeze;
            _mascotShadow.Opacity = 1 - squeeze;
            Canvas.SetLeft(_teleportBeam, _mascotRunFrom + MascotWidth / 2 - _teleportBeam.Width / 2);
            Canvas.SetTop(_teleportBeam, StageCover - _teleportBeam.Height + 10);
            _teleportBeam.IsVisible = s > 0.4 && fade < 1;
            _teleportBeam.Opacity = Phase(s, 0.4, 0.3) * (1 - fade);
            if (s >= TeleportSeconds) LeaveMascotStage();
            return 0;
        }

        private void StartMascotHold(Point at)
        {
            ClearMascotNotes();
            HideMascotExtras();
            _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
            _mascotPointer = at;
            _mascotHeldLastX = at.X - _mascotGrab.X;
            _mascotSwing = 0;
            _mascot.ZIndex = 4;
            SetMascotState(MascotState.Held);
            UpdateMascotTimer();
        }

        private void EndMascotPress()
        {
            if (!_mascotPressing) return;
            _mascotPressing = false;
            if (_mascotState != MascotState.Held) return;
            _mascotDropTop = Canvas.GetTop(_mascot);
            SetMascotState(MascotState.Dropped);
        }

        private double StepMascotHeld(double s)
        {
            double floor = MascotOffsetTop();
            double x = Math.Clamp(_mascotPointer.X - _mascotGrab.X, -MascotWidth / 2, MascotStageWidth - MascotWidth / 2);
            double top = Math.Clamp(_mascotPointer.Y - _mascotGrab.Y, -320, floor);
            Canvas.SetLeft(_mascot, x);
            _mascotSwing = Math.Clamp(_mascotSwing * 0.85 + (x - _mascotHeldLastX) * 0.9, -45, 45);
            _mascotHeldLastX = x;
            double kick = Math.Sin(s * 11);
            Pose(lean: -_mascotSwing, bounce: 0, arms: (150 + Math.Sin(s * 14) * 18, -150 - Math.Sin(s * 14) * 18), legs: (kick * 28 + _mascotSwing * 0.6, -kick * 28 + _mascotSwing * 0.6));
            _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.3;
            _mascotBrows.IsVisible = true;
            _mascotShadow.Opacity = Math.Clamp(1 - (floor - top) / 120, 0, 1);
            _glyphShout.IsVisible = (int)(s * 1.4) % 2 == 0;
            Canvas.SetLeft(_glyphShout, 64);
            Canvas.SetTop(_glyphShout, -36 + Math.Sin(s * 8) * 4);
            return top;
        }

        private double StepMascotDropped(double s)
        {
            double floor = MascotOffsetTop();
            double height = Math.Max(0, floor - _mascotDropTop);
            double land = Math.Sqrt(2 * height / MascotGravity);
            if (s < land)
            {
                double top = _mascotDropTop + 0.5 * MascotGravity * s * s;
                double flail = Math.Sin(s * 26);
                Pose(lean: 0, bounce: 0, arms: (160 + flail * 20, -160 - flail * 20), legs: (flail * 30, -flail * 30));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.35;
                _mascotShadow.Opacity = Math.Clamp(1 - (floor - top) / 120, 0, 1);
                return top;
            }
            double after = s - land;
            double squash = Math.Max(0, Math.Sin(Math.Min(1, after / 0.25) * Math.PI));
            _mascotScale.ScaleY = 1 - 0.18 * squash;
            _mascotScale.ScaleX = 1 + 0.12 * squash;
            if (after < 0.6) _mascotEyes.ScaleY = 0.2;
            _mascotBrows.IsVisible = after > 0.4;
            Pose(lean: 0, bounce: 0, arms: (30, -30), legs: (-12, 12));
            if (after >= 0.9)
            {
                _mascot.ZIndex = 0;
                _mascotAngry = true;
                _mascotRunFrom = Canvas.GetLeft(_mascot);
                SetMascotState(MascotState.WalkOff);
            }
            return floor;
        }


        private void StepMascotHighFive(double s)
        {
            double reach = Ease(Phase(s, 0, 0.3));
            double slap = Phase(s, 0.3, 0.5);
            double hop = s is > 0.3 and < 0.75 ? Math.Sin(Phase(s, 0.3, 0.45) * Math.PI) * 30 : 0;
            Pose(lean: -6 * reach, bounce: -hop, arms: (30, -30 - 145 * reach), legs: (-hop * 0.4, hop * 0.4));
            _mascotEyes.ScaleY = s > 0.3 ? 0.45 : 1;
            _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + 0.1 * Math.Sin(slap * Math.PI);
            if (s > 0.3)
            {
                _glyphHighFive.IsVisible = true;
                _glyphHighFive.Opacity = Math.Min(1, slap * 3) * (1 - Phase(s, 1.1, 0.4));
                Canvas.SetLeft(_glyphHighFive, 40);
                Canvas.SetTop(_glyphHighFive, -50 - slap * 20);
                for (int i = 0; i < _glyphSparkles.Count; i++)
                {
                    var sparkle = _glyphSparkles[i];
                    double angle = -Math.PI / 2 + (i - 1) * 0.9;
                    sparkle.IsVisible = slap < 1;
                    sparkle.Opacity = 1 - slap;
                    Canvas.SetLeft(sparkle, 150 + Math.Cos(angle) * 70 * slap);
                    Canvas.SetTop(sparkle, 20 + Math.Sin(angle) * 70 * slap);
                }
            }
            if (s >= HighFiveSeconds)
            {
                _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
                _mascotRunFrom = Canvas.GetLeft(_mascot);
                _mascotExit = MascotExit.Back;
                SetMascotState(MascotState.Exit);
            }
        }
    }
}
