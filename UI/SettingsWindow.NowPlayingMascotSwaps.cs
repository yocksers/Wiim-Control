using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double MascotArgueChance = 0.03;
        private const double ShutterSeconds = 4.6;
        private const double ShutterSlat = 24;
        private const double SlotsSeconds = 4.9;
        private const int SlotsReels = 3;
        private const int SlotsFillers = 5;
        private const double SlotsReelWidth = StageCover / SlotsReels;
        private const double BinWidth = 92;
        private const double BinHeight = 112;
        private const double BinLeft = 884;
        private const double BinOutside = MascotStageWidth + 120;
        private const double BallSize = 58;
        private const double ArgueSeconds = 6.6;

        private readonly Canvas _shutter = new() { Width = StageCover, ClipToBounds = true, IsVisible = false, IsHitTestVisible = false };
        private readonly Canvas _shutterSlats = new() { Width = StageCover, Height = StageCover };
        private readonly Border _shutterBox = new()
        {
            Width = StageCover + 40, Height = 40, CornerRadius = new CornerRadius(8), IsVisible = false, IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#6A7078"), 0), new GradientStop(Color.Parse("#3A3E44"), 1) }
            }
        };
        private readonly Line _shutterCord = new() { Stroke = new SolidColorBrush(Color.Parse("#E2D3A8")), StrokeThickness = 3, IsVisible = false, IsHitTestVisible = false };

        private readonly Canvas _bin = new() { Width = BinWidth, Height = BinHeight, IsVisible = false, IsHitTestVisible = false, RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative), RenderTransform = new RotateTransform() };
        private readonly Canvas _paperBall = new() { Width = BallSize, Height = BallSize, IsVisible = false, IsHitTestVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform() };
        private readonly Ellipse _paperBallFace = new() { Width = BallSize, Height = BallSize };
        private bool _crumpleMiss;

        private readonly List<(Border Reel, Canvas Strip)> _reels = [];
        private readonly Border _slotsFrame = new()
        {
            Width = StageCover + 40, Height = StageCover + 40, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(16), IsVisible = false, IsHitTestVisible = false,
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#F6D365"), 0), new GradientStop(Color.Parse("#B8862B"), 0.5), new GradientStop(Color.Parse("#F6D365"), 1) }
            }
        };
        private readonly Border _slotsSign = new()
        {
            Width = 340, Height = 58, CornerRadius = new CornerRadius(29), Background = new SolidColorBrush(Color.Parse("#1C1408")), BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.Parse("#F6D365")), IsVisible = false, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "★  JACKPOT  ★", FontSize = 30, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.Parse("#F6D365")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        private readonly Line _slotsLever = new() { Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 8, StrokeLineCap = PenLineCap.Round, IsVisible = false, IsHitTestVisible = false };
        private readonly Ellipse _slotsKnob = new() { Width = 30, Height = 30, Fill = new SolidColorBrush(Color.Parse("#D0342C")), IsVisible = false, IsHitTestVisible = false };
        private RenderTargetBitmap? _slotsOld;

        private readonly Image _rival = new() { Width = MascotWidth, Height = MascotHeight, Stretch = Stretch.Fill, IsVisible = false, IsHitTestVisible = false, RenderTransformOrigin = new RelativePoint(0.5, 0.94, RelativeUnit.Relative) };
        private readonly RotateTransform _rivalLean = new();
        private readonly TranslateTransform _rivalBounce = new();
        private RenderTargetBitmap? _rivalPicture;
        private readonly TextBlock _glyphCurseMain = new() { Text = "#@%!", FontSize = 30, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")), IsVisible = false, IsHitTestVisible = false };
        private readonly TextBlock _glyphCurseRival = new() { Text = "&*$!", FontSize = 30, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")), IsVisible = false, IsHitTestVisible = false };
        private Control[] _swapProps = [];

        private void BuildMascotSwaps()
        {
            var metal = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#C7CCD3"), 0), new GradientStop(Color.Parse("#E9ECEF"), 0.4), new GradientStop(Color.Parse("#8E949C"), 1) }
            };
            _shutterSlats.Children.Add(new Rectangle { Width = StageCover, Height = StageCover, Fill = new SolidColorBrush(Color.Parse("#3A3E44")) });
            for (double y = 0; y < StageCover - ShutterSlat; y += ShutterSlat)
                _shutterSlats.Children.Add(Place(new Rectangle { Width = StageCover, Height = ShutterSlat - 2, Fill = metal }, 0, y));
            _shutterSlats.Children.Add(Place(new Rectangle { Width = StageCover, Height = ShutterSlat + 6, Fill = new SolidColorBrush(Color.Parse("#4A4F56")) }, 0, StageCover - ShutterSlat - 6));
            _shutterSlats.Children.Add(Place(new Rectangle { Width = 70, Height = 10, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.Parse("#22252A")) }, StageCover / 2 - 35, StageCover - 20));
            _shutter.Children.Add(_shutterSlats);

            _bin.Children.Add(new Polygon
            {
                Points = [new Point(4, 10), new Point(BinWidth - 4, 10), new Point(BinWidth - 14, BinHeight), new Point(14, BinHeight)],
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#6E747C"), 0), new GradientStop(Color.Parse("#A6ACB4"), 0.45), new GradientStop(Color.Parse("#5A6068"), 1) }
                }
            });
            foreach (double x in new[] { 26.0, 46.0, 66.0 })
                _bin.Children.Add(new Line { StartPoint = new Point(x, 22), EndPoint = new Point(x + (x - 46) * 0.15, BinHeight - 10), Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), StrokeThickness = 2 });
            _bin.Children.Add(Place(new Ellipse { Width = BinWidth, Height = 20, Fill = new SolidColorBrush(Color.Parse("#3A3E44")), Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 3 }, 0, 0));

            _paperBall.Children.Add(_paperBallFace);
            var crease = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            foreach (var (from, to) in new[] { (new Point(10, 20), new Point(30, 28)), (new Point(30, 28), new Point(44, 12)), (new Point(30, 28), new Point(26, 48)), (new Point(14, 40), new Point(26, 48)), (new Point(26, 48), new Point(48, 40)) })
                _paperBall.Children.Add(new Line { StartPoint = from, EndPoint = to, Stroke = crease, StrokeThickness = 2 });

            var fillers = new (string Symbol, string Back)[] { ("7", "#D0342C"), ("★", "#2E4057"), ("♪", "#3D6B4F"), ("♥", "#8E3B46"), ("$", "#C9A227"), ("BAR", "#5B5BF6") };
            for (int i = 0; i < SlotsReels; i++)
            {
                var strip = new Canvas { Width = SlotsReelWidth, Height = (SlotsFillers + 2) * StageCover };
                for (int k = 1; k <= SlotsFillers; k++)
                {
                    var (symbol, back) = fillers[(i * 2 + k) % fillers.Length];
                    strip.Children.Add(Place(new Border
                    {
                        Width = SlotsReelWidth, Height = StageCover, Background = new SolidColorBrush(Color.Parse(back)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), BorderThickness = new Thickness(0, 4),
                        Child = new TextBlock { Text = symbol, FontSize = symbol.Length > 1 ? 74 : 120, FontWeight = FontWeight.Black, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                    }, 0, k * StageCover));
                }
                var reel = new Border
                {
                    Width = SlotsReelWidth, Height = StageCover, ClipToBounds = true, IsVisible = false, IsHitTestVisible = false,
                    Child = new Canvas { Width = SlotsReelWidth, Height = StageCover, Children = { strip } },
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), BorderThickness = new Thickness(i == 0 ? 0 : 2, 0, 0, 0)
                };
                _reels.Add((reel, strip));
            }

            _rival.RenderTransform = new TransformGroup { Children = { new ScaleTransform(-1, 1), _rivalLean, _rivalBounce } };

            _mascotFxBack.Children.Add(_slotsFrame);
            foreach (var (reel, _) in _reels) _mascotFx.Children.Add(reel);
            foreach (var prop in new Control[] { _shutter, _shutterBox, _shutterCord, _paperBall, _bin, _slotsSign, _slotsLever, _slotsKnob, _rival, _glyphCurseMain, _glyphCurseRival })
                _mascotFx.Children.Add(prop);
            _swapProps =
            [
                _shutter, _shutterBox, _shutterCord, _paperBall, _bin, _slotsFrame, _slotsSign, _slotsLever, _slotsKnob, _rival, _glyphCurseMain, _glyphCurseRival,
                .. _reels.Select(r => (Control)r.Reel)
            ];
        }

        private void HideMascotSwaps() => HideAll(_swapProps);

        private RenderTargetBitmap? SnapshotCoverAsShown()
        {
            var size = _npCoverContent.Bounds.Size;
            if (size.Width < 1 || size.Height < 1) return null;
            try
            {
                var snapshot = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * JigsawSharpness), (int)Math.Ceiling(size.Height * JigsawSharpness)),
                    new Vector(96 * JigsawSharpness, 96 * JigsawSharpness));
                snapshot.Render(_npCoverContent);
                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        private bool PrepareSlots()
        {
            var old = SnapshotCoverAsShown();
            if (old == null || SnapshotNewCover() is not { } fresh)
            {
                old?.Dispose();
                return false;
            }
            _slotsOld?.Dispose();
            _slotsOld = old;
            for (int i = 0; i < _reels.Count; i++)
            {
                var (_, strip) = _reels[i];
                var crop = new RelativeRect(i / (double)SlotsReels, 0, 1.0 / SlotsReels, 1, RelativeUnit.Relative);
                while (strip.Children.Count > SlotsFillers) strip.Children.RemoveAt(strip.Children.Count - 1);
                strip.Children.Add(Place(new Border { Width = SlotsReelWidth, Height = StageCover, Background = new ImageBrush(fresh) { Stretch = Stretch.Fill, SourceRect = crop } }, 0, 0));
                strip.Children.Add(Place(new Border { Width = SlotsReelWidth, Height = StageCover, Background = new ImageBrush(old) { Stretch = Stretch.Fill, SourceRect = crop } }, 0, (SlotsFillers + 1) * StageCover));
            }
            return true;
        }

        private void PrepareCrumple()
        {
            _crumpleMiss = Random.Shared.Next(2) == 0;
            _paperBallFace.Fill = _npCoverOld.Source is Bitmap old ? new ImageBrush(old) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.Parse("#ECE8DC"));
        }

        private void PrepareRival()
        {
            try
            {
                Pose(lean: 0, bounce: 0, arms: (-95, -85), legs: (-8, 8));
                _mascotFacing.ScaleX = 1;
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1;
                _mascotGaze.X = 4;
                _mascotGaze.Y = 0;
                _mascotBrows.IsVisible = true;
                _mascotScale.ScaleX = _mascotScale.ScaleY = 1;
                var picture = new RenderTargetBitmap(new PixelSize((int)MascotWidth, (int)MascotHeight));
                _mascot.Measure(new Size(MascotWidth, MascotHeight));
                _mascot.Arrange(new Rect(0, 0, MascotWidth, MascotHeight));
                picture.Render(_mascot);
                _npStage.InvalidateArrange();
                _rivalPicture?.Dispose();
                _rivalPicture = picture;
                _rival.Source = picture;
            }
            catch
            {
                _rival.Source = null;
            }
            finally
            {
                _mascotBrows.IsVisible = _mascotAngry;
            }
        }

        private double StepMascotShutter(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double down = Ease(Phase(s, 0.5, 1.0));
            double up = Ease(Phase(s, 2.4, 0.7));
            double height = StageCover * down * (1 - up);
            double rattle = s is > 1.5 and < 2.3 ? Math.Sin(s * 48) * 4 * (1 - Phase(s, 1.5, 0.8)) : 0;
            double box = s < 0.4 ? Ease(s / 0.4) : s < 3.6 ? 1 : 1 - Ease(Phase(s, 3.6, 0.5));
            Canvas.SetLeft(_shutterBox, MascotCoverLeft - 20 + rattle);
            Canvas.SetTop(_shutterBox, -40 - 60 * (1 - box));
            _shutterBox.Opacity = box;
            _shutterBox.IsVisible = box > 0;
            _shutter.Height = Math.Max(0.1, height);
            Canvas.SetTop(_shutterSlats, height - StageCover);
            Canvas.SetLeft(_shutter, MascotCoverLeft + rattle);
            Canvas.SetTop(_shutter, 0);
            _shutter.IsVisible = height > 0.5;
            if (s >= 1.5) ShowNewArt(Phase(s, 1.5, 1.0));

            double arm = s < 0.5 ? -100 - 70 * Ease(s / 0.5) : s < 1.5 ? -170 + 110 * down : s < 2.4 ? -60 + Math.Sin(s * 30) * 10 : s < 3.1 ? -60 - 110 * up : -40;
            if (s < 3.1)
            {
                var hand = StageHand(true, arm, 46);
                _shutterCord.StartPoint = new Point(MascotCoverLeft + 40 + rattle, Math.Max(0, height - 6));
                _shutterCord.EndPoint = hand;
                _shutterCord.IsVisible = true;
            }
            if (s < 0.5) Pose(lean: 0, bounce: 0, arms: (30, arm), legs: (0, 0));
            else if (s < 1.5) Pose(lean: 8, bounce: 14 * down, arms: (40, arm), legs: (-10 * down, 10 * down));
            else if (s < 2.4)
            {
                Pose(lean: 10 + Math.Sin(s * 30) * 3, bounce: 12, arms: (40 + Math.Sin(s * 30) * 10, arm), legs: (-10, 10));
                _mascotEyes.ScaleY = 0.6;
            }
            else if (s < 3.1) Pose(lean: -6, bounce: -Math.Sin(up * Math.PI) * 50, arms: (120, arm), legs: (-16, 16));
            else
            {
                CheerDance(s);
                if (s < 3.4) (_mascotGaze.X, _mascotGaze.Y) = (6, -4);
            }
            if (s >= ShutterSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotCrumple(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double extra = _crumpleMiss ? 2.6 : 0;
            double hitAt = 2.3 + extra;
            double unfold = Phase(s, hitAt + 0.4, 1.1);
            double end = hitAt + 2.6;

            double binX = s < 0.5 ? BinOutside + (BinLeft - BinOutside) * Ease(s / 0.5) : s < end - 0.6 ? BinLeft : BinLeft + (BinOutside - BinLeft) * Math.Pow(Phase(s, end - 0.6, 0.6), 2);
            Canvas.SetLeft(_bin, binX);
            Canvas.SetTop(_bin, StageCover - BinHeight);
            _bin.IsVisible = true;
            double rimWobble = Math.Max(0, 1 - (s - hitAt) / 0.6);
            ((RotateTransform)_bin.RenderTransform!).Angle = s > hitAt ? Math.Sin((s - hitAt) * 30) * 6 * rimWobble : 0;
            var mouth = new Point(binX + BinWidth / 2, StageCover - BinHeight + 8);

            var grab = StageHand(true, -90, 46);
            if (s < 1.0)
            {
                double squeeze = Ease(Phase(s, 0.1, 0.9));
                PlaceCover(Lerp(CoverMiddle, grab, squeeze), 1 - 0.92 * squeeze);
                _npCoverTurn.Angle = Math.Sin(s * 23) * 25 * squeeze;
                _npCoverFit.ScaleX = 1 + 0.22 * Math.Sin(s * 37) * squeeze;
                _npCoverFit.ScaleY = 1 - 0.22 * Math.Sin(s * 31 + 1) * squeeze;
                Pose(lean: 8, bounce: -Math.Abs(Math.Sin(s * 14)) * 4, arms: (-60 + Math.Sin(s * 14) * 20, -90 - Math.Sin(s * 14) * 20), legs: (0, 0));
            }
            else if (s < hitAt + 0.4)
            {
                PlaceCover(CoverMiddle, 0.001);
                _npCoverTurn.Angle = 0;
                _npCoverFit.ScaleX = _npCoverFit.ScaleY = 1;
            }

            var ballTurn = (RotateTransform)_paperBall.RenderTransform!;
            void Ball(Point at, double spin)
            {
                Center(_paperBall, at);
                ballTurn.Angle = spin;
            }
            var release = StageHand(true, -170, 46);
            var rim = new Point(mouth.X - 34, mouth.Y + 4);
            (double WindUp, double Throw, Point Target)[] throws = _crumpleMiss ? [(1.0, 1.5, rim), (3.6, 4.1, mouth)] : [(1.0, 1.5, mouth)];
            var windUp = throws.FirstOrDefault(t => s >= t.WindUp && s < t.Throw);
            var thrown = throws.FirstOrDefault(t => s >= t.Throw && s < t.Throw + 0.8);
            bool bouncingBack = _crumpleMiss && s is >= 2.3 and < 3.6;

            if (s >= 1.0 && s < hitAt + 0.25)
            {
                _paperBall.ZIndex = 2;
                _bin.ZIndex = 1;
                if (windUp != default) Ball(StageHand(true, -90 + 150 * Phase(s, windUp.WindUp, 0.5), 46), 0);
                else if (thrown != default)
                {
                    double p = Phase(s, thrown.Throw, 0.8);
                    var at = Lerp(release, thrown.Target, p);
                    Ball(new Point(at.X, at.Y - Math.Sin(p * Math.PI) * 300), p * 720);
                }
                else if (bouncingBack)
                {
                    double back = Phase(s, 2.3, 1.3);
                    double bounce = Math.Abs(Math.Sin(back * Math.PI * 2.5)) * 120 * (1 - back);
                    var floor = Lerp(new Point(rim.X, StageCover - BallSize / 2), new Point(grab.X + 20, StageCover - BallSize / 2), Ease(back));
                    Ball(new Point(floor.X, floor.Y - bounce), -back * 900);
                }
                else if (s >= hitAt)
                {
                    _paperBall.ZIndex = 0;
                    _bin.ZIndex = 1;
                    Ball(new Point(mouth.X, mouth.Y + Phase(s, hitAt, 0.25) * 40), 720);
                }
            }

            if (s >= hitAt + 0.4)
            {
                ShowNewArt(unfold);
                double grow = Ease(unfold);
                PlaceCover(Lerp(grab, CoverMiddle, grow), 0.08 + 0.92 * grow);
                _npCoverTurn.Angle = Math.Sin(s * 20) * 20 * (1 - unfold);
                _npCoverFit.ScaleX = 1 + 0.25 * Math.Sin(s * 30) * (1 - unfold);
                _npCoverFit.ScaleY = 1 - 0.25 * Math.Sin(s * 26 + 1) * (1 - unfold);
            }

            if (s < 1.0) { }
            else if (windUp != default) Pose(lean: -8, bounce: 0, arms: (40, -90 + 150 * Phase(s, windUp.WindUp, 0.5)), legs: (0, 0));
            else if (thrown != default)
            {
                double p = Phase(s, thrown.Throw, 0.3);
                Pose(lean: -6 + 16 * p, bounce: 0, arms: (30, 60 - 230 * p), legs: (-10 * p, 10 * p));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -2);
            }
            else if (bouncingBack && s < 3.0)
            {
                Pose(lean: -4, bounce: 0, arms: (30, -165), legs: (0, 0));
                _mascotEyes.ScaleY = 0.2;
            }
            else if (bouncingBack)
            {
                double bend = Math.Sin(Phase(s, 3.2, 0.4) * Math.PI / 2);
                Pose(lean: 26 * bend, bounce: 0, arms: (30, -60 - 30 * bend), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (5, 5);
            }
            else if (s < hitAt + 0.4)
            {
                Pose(lean: 0, bounce: 0, arms: (40, -40), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (6, -2);
            }
            else if (s < hitAt + 1.5) Pose(lean: 4, bounce: 0, arms: (-80 - 40 * unfold, -100 - 40 * unfold), legs: (0, 0));
            else CheerDance(s);

            if (s >= end) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotSlots(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            double frame = Ease(Phase(s, 0, 0.4)) * (1 - Phase(s, SlotsSeconds - 0.6, 0.5));
            Canvas.SetLeft(_slotsFrame, MascotCoverLeft - 20);
            Canvas.SetTop(_slotsFrame, -20);
            _slotsFrame.Opacity = frame;
            _slotsFrame.IsVisible = frame > 0;

            double pull = s < 0.5 ? 0 : s < 0.9 ? Ease(Phase(s, 0.5, 0.4)) : 1 - Ease(Phase(s, 0.9, 0.5));
            double arm = -170 + 130 * pull;
            if (s < 3.3)
            {
                var knob = StageHand(true, arm, 48);
                var pivot = new Point(MascotCoverLeft + 70, StageCover - 40);
                _slotsLever.StartPoint = pivot;
                _slotsLever.EndPoint = knob;
                _slotsLever.IsVisible = true;
                Center(_slotsKnob, knob);
            }

            double reels = s < 0.9 ? 0 : s < 3.3 ? 1 : 1 - Phase(s, 3.3, 0.3);
            for (int i = 0; i < _reels.Count; i++)
            {
                var (reel, strip) = _reels[i];
                double stopAt = 1.9 + i * 0.5;
                double p = Phase(s, 0.9, stopAt - 0.9);
                double travel = (SlotsFillers + 1) * StageCover;
                double settle = p < 1 ? 1 - Math.Pow(1 - p, 3) : 1;
                double nudge = p >= 1 ? Math.Sin(Phase(s, stopAt, 0.25) * Math.PI) * 18 * (1 - Phase(s, stopAt, 0.25)) : 0;
                Canvas.SetTop(strip, -travel * (1 - settle) + nudge);
                Canvas.SetLeft(reel, MascotCoverLeft + i * SlotsReelWidth);
                Canvas.SetTop(reel, 0);
                reel.Opacity = reels;
                reel.IsVisible = reels > 0;
            }
            if (s >= 0.9) ShowNewArt(Phase(s, 0.9, 2.4));

            double jackpot = Phase(s, 3.0, 0.4);
            if (s >= 3.0)
            {
                _slotsSign.IsVisible = true;
                _slotsSign.Opacity = frame;
                Canvas.SetLeft(_slotsSign, CoverMiddle.X - 170);
                Canvas.SetTop(_slotsSign, -8 - Math.Sin(jackpot * Math.PI) * 10);
                for (int i = 0; i < _glyphSparkles.Count; i++)
                {
                    var sparkle = _glyphSparkles[i];
                    sparkle.IsVisible = s < 4.4;
                    sparkle.Opacity = 1 - Phase(s, 3.4, 1.0);
                    Canvas.SetLeft(sparkle, 10 + i * 70);
                    Canvas.SetTop(sparkle, -40 - (i % 2) * 40 - Phase(s, 3.0, 1.2) * 50);
                }
            }

            if (s < 0.5) Pose(lean: 0, bounce: 0, arms: (30, -100 - 70 * Ease(s / 0.5)), legs: (0, 0));
            else if (s < 1.4) Pose(lean: 10 * pull, bounce: 20 * pull, arms: (40, arm), legs: (-12 * pull, 12 * pull));
            else if (s < 3.0)
            {
                double stop = new[] { 1.9, 2.4, 2.9 }.Select(t => s - t).Where(d => d is >= 0 and < 0.25).DefaultIfEmpty(-1).Max();
                Pose(lean: 0, bounce: stop >= 0 ? -Math.Sin(stop / 0.25 * Math.PI) * 16 : -Math.Abs(Math.Sin(s * 14)) * 3, arms: (60, -60), legs: (0, 0));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.3;
                (_mascotGaze.X, _mascotGaze.Y) = (6, -3);
            }
            else if (s < 3.5) Pose(lean: 0, bounce: -Math.Sin(jackpot * Math.PI) * 60, arms: (165, -165), legs: (-20, 20));
            else CheerDance(s);
            if (s >= SlotsSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotArgue(double s)
        {
            double tug = s is >= 1.2 and < 3.2 ? Math.Sin((s - 1.2) * 7) * 46 * Math.Min(1, (s - 1.2) / 0.4) : 0;
            double fling = Phase(s, 3.2, 0.35);
            double fall = s < 3.2 ? 0 : s < 4.6 ? Ease(Phase(s, 3.2, 0.3)) : 1 - Ease(Phase(s, 4.6, 0.5));
            double leave = Phase(s, 5.4, 1.2);

            double rivalX = s < 0.6 ? MascotWhooshEnd + (MascotRightSpot - MascotWhooshEnd) * Ease(s / 0.6) : MascotRightSpot + tug + fall * 70 + leave * 600;
            double mainX = MascotDanceSpot + tug - fall * 70 - leave * 600;
            Canvas.SetLeft(_mascot, mainX);
            Canvas.SetLeft(_rival, rivalX);
            Canvas.SetTop(_rival, MascotOffsetTop());
            _rival.IsVisible = _rival.Source != null;
            _rivalLean.Angle = 82 * fall + (s is >= 1.2 and < 3.2 ? Math.Sin((s - 1.2) * 7) * 8 : 0);
            _rivalBounce.Y = s < 0.6 ? -Math.Abs(Math.Sin(s * 24)) * 8 : leave > 0 ? -Math.Abs(Math.Sin(s * 9)) * 4 : 0;
            _mascotBrows.IsVisible = s is >= 0.9 and < 3.3;

            if (s < 3.2) PlaceCover(new Point(CoverMiddle.X + tug, CoverMiddle.Y), 1);
            else if (s < 4.4)
            {
                double fly = Phase(s, 3.2, 0.5);
                double drop = BounceIn(Phase(s, 3.75, 0.65));
                double y = s < 3.75 ? CoverMiddle.Y - 900 * Ease(fly) : -900 + (CoverMiddle.Y + 900) * drop;
                PlaceCover(new Point(CoverMiddle.X, y), 1);
                _npCoverTurn.Angle = s < 3.75 ? fly * 360 : 0;
                if (s >= 3.6) ShowNewArt(Phase(s, 3.6, 0.6));
                StepLandingDust(s - (3.75 + 0.65 / 2.75));
            }
            else
            {
                PlaceCover(CoverMiddle, 1);
                _npCoverTurn.Angle = 0;
                ShowNewArt(1);
            }

            void Curse(TextBlock glyph, double x, double phase)
            {
                double show = Math.Max(0, Math.Sin(s * 5 + phase));
                glyph.IsVisible = show > 0.2;
                glyph.Opacity = show;
                Canvas.SetLeft(glyph, x + 40);
                Canvas.SetTop(glyph, MascotOffsetTop() - 50 - show * 14);
            }
            if (s is >= 1.0 and < 3.2)
            {
                Curse(_glyphCurseMain, mainX, 0);
                Curse(_glyphCurseRival, rivalX, Math.PI);
            }

            if (s < 0.6) Pose(lean: 0, bounce: 0, arms: (30, -30), legs: (0, 0));
            else if (s < 1.2)
            {
                Pose(lean: 0, bounce: -Math.Sin(Phase(s, 0.6, 0.3) * Math.PI) * 24, arms: (60, -60), legs: (0, 0));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.4;
                (_mascotGaze.X, _mascotGaze.Y) = (7, 0);
            }
            else if (s < 3.2) Pose(lean: -10 - Math.Sin((s - 1.2) * 7) * 8, bounce: 0, arms: (-95, -85), legs: (-16, 16));
            else if (s < 4.6)
            {
                Pose(lean: -82 * fall, bounce: 0, arms: (140, -140), legs: (-20 * fall, 20 * fall));
                _mascotEyes.ScaleY = 0.2;
            }
            else if (s < 5.4)
            {
                double shrug = Math.Max(0, Math.Sin((s - 4.8) * Math.PI * 1.6));
                Pose(lean: -82 * fall, bounce: -shrug * 8, arms: (60 + shrug * 20, -60 - shrug * 20), legs: (0, 0));
                (_mascotGaze.X, _mascotGaze.Y) = (7, -2);
            }
            else
            {
                _mascotFacing.ScaleX = -1;
                WalkPose(s, -1);
            }
            if (s >= ArgueSeconds)
            {
                _mascotExit = MascotExit.Back;
                _mascotFromRight = false;
                EndMascotTrick(MascotState.Exit);
            }
            return 0;
        }
    }
}
