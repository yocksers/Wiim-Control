using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private void BuildMascot()
        {
            var dark = new SolidColorBrush(Color.Parse("#16171A"));
            _mascot.Children.Add(Place(_mascotShadow, 35, 226));

            for (int i = 0; i < 3; i++)
                _mascotSpeed.Children.Add(Place(new Rectangle
                {
                    Width = 70 - i * 14, Height = 4, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
                }, 4 - (70 - i * 14), 90 + i * 34));

            var figure = new Canvas
            {
                Width = MascotWidth, Height = MascotHeight, RenderTransformOrigin = new RelativePoint(0.5, 0.94, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { _mascotFacing, _mascotLean, _mascotBounce } }
            };
            figure.Children.Add(_mascotSpeed);
            var limbs = new SolidColorBrush(Color.Parse("#8A9098"));
            figure.Children.Add(Limb(_mascotLeftLeg, 76, 180, 10, 44, limbs, foot: true));
            figure.Children.Add(Limb(_mascotRightLeg, 124, 180, 10, 44, limbs, foot: true));

            figure.Children.Add(Place(new Rectangle
            {
                Width = CabinetWidth, Height = CabinetHeight, RadiusX = 14, RadiusY = 14, Stroke = new SolidColorBrush(Color.Parse("#0C0D0F")), StrokeThickness = 3,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#4A4E55"), 0), new GradientStop(Color.Parse("#2A2C30"), 0.55), new GradientStop(Color.Parse("#1A1B1E"), 1) }
                }
            }, CabinetLeft, CabinetTop));
            figure.Children.Add(Place(new Rectangle
            {
                Width = CabinetWidth - 16, Height = CabinetHeight - 16, RadiusX = 9, RadiusY = 9, Stroke = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), StrokeThickness = 1
            }, CabinetLeft + 8, CabinetTop + 8));
            foreach (var (x, y) in new[] { (16.0, 16.0), (CabinetWidth - 22, 16.0), (16.0, CabinetHeight - 22), (CabinetWidth - 22, CabinetHeight - 22) })
                figure.Children.Add(Place(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.Parse("#70767E")) }, CabinetLeft + x, CabinetTop + y));

            var cone = new Canvas { Width = 92, Height = 92, RenderTransformOrigin = RelativePoint.Center, RenderTransform = _mascotCone };
            cone.Children.Add(new Ellipse
            {
                Width = 92, Height = 92, Stroke = new SolidColorBrush(Color.Parse("#70767E")), StrokeThickness = 4,
                Fill = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.Parse("#34363B"), 0), new GradientStop(Color.Parse("#141518"), 0.85), new GradientStop(Color.Parse("#0B0B0D"), 1) }
                }
            });
            foreach (double ring in new[] { 70.0, 50.0 })
                cone.Children.Add(Place(new Ellipse { Width = ring, Height = ring, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 },
                    (92 - ring) / 2, (92 - ring) / 2));
            cone.Children.Add(Place(new Ellipse
            {
                Width = 28, Height = 28,
                Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#8A9098"), 0), new GradientStop(Color.Parse("#40444A"), 1) } }
            }, 32, 32));
            figure.Children.Add(Place(cone, 54, 100));

            var eyes = new Canvas { Width = 70, Height = 30, RenderTransformOrigin = RelativePoint.Center, RenderTransform = _mascotEyes };
            var pupils = new Canvas { Width = 70, Height = 30, RenderTransform = _mascotGaze };
            foreach (double x in new[] { 6.0, 40.0 })
            {
                eyes.Children.Add(Place(new Ellipse { Width = 24, Height = 28, Fill = Brushes.White }, x, 0));
                pupils.Children.Add(Place(new Ellipse { Width = 11, Height = 14, Fill = dark }, x + 9, 9));
                pupils.Children.Add(Place(new Ellipse { Width = 4, Height = 4, Fill = Brushes.White }, x + 15, 11));
            }
            eyes.Children.Add(pupils);
            figure.Children.Add(Place(eyes, 65, 58));
            foreach (var (from, to) in new[] { (new Point(4, -6), new Point(30, 4)), (new Point(40, 4), new Point(66, -6)) })
                _mascotBrows.Children.Add(new Line { StartPoint = from, EndPoint = to, Stroke = dark, StrokeThickness = 6, StrokeLineCap = PenLineCap.Round });
            figure.Children.Add(Place(_mascotBrows, 65, 58));
            figure.Children.Add(Limb(_mascotLeftArm, 24, 110, 8, 46, limbs, foot: false));
            figure.Children.Add(Limb(_mascotRightArm, 176, 110, 8, 46, limbs, foot: false));
            BuildMascotProps(figure);
            BuildMascotIdle(figure);
            _mascot.Children.Add(figure);
            _mascot.Children.Add(_mascotNotes);
            _mascotVisitTimer.Tick += (_, _) => CheckMascotVisit();
            _zipLayer.Children.Add(_zipWire);
            BuildMascotTricks(figure);
            BuildMascotExtras(figure);
            BuildMascotOops();
            BuildMascotSpin();
            BuildMascotSwaps();
            BuildMascotChores();
            BuildMascotSeason(figure);
            _mascotTimer.Host = _npOverlay;
            _mascotTimer.Tick += (_, _) => StepMascot();
            _npCoverCard.Tapped += (_, e) =>
            {
                if (NpStyle != NowPlayingStyle.Mascot || _mascotState is not (MascotState.Gone or MascotState.WalkOff)) return;
                if (e.Source is Visual source && (source.FindAncestorOfType<Button>(includeSelf: true) != null ||
                                                  source.FindAncestorOfType<RangeBase>(includeSelf: true) != null ||
                                                  source.FindAncestorOfType<LevelSlider>(includeSelf: true) != null)) return;
                _mascotEncore = true;
                _mascotAngry = false;
                StartMascotEnter(MascotState.Dance, MascotRushSpeed);
            };
        }

        private static Canvas Limb(RotateTransform turn, double x, double y, double thickness, double length, IBrush brush, bool foot)
        {
            var limb = new Canvas
            {
                Width = thickness, Height = length, RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative), RenderTransform = turn
            };
            limb.Children.Add(new Rectangle { Width = thickness, Height = length, RadiusX = thickness / 2, RadiusY = thickness / 2, Fill = brush });
            limb.Children.Add(foot
                ? Place(new Ellipse { Width = 28, Height = 13, Fill = new SolidColorBrush(Color.Parse("#5B5BF6")) }, thickness / 2 - 10, length - 7)
                : Place(new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 1 }, thickness / 2 - 9, length - 10));
            Canvas.SetLeft(limb, x - thickness / 2);
            Canvas.SetTop(limb, y);
            return limb;
        }

        private void ApplyMascotStyle(bool on)
        {
            _mascot.IsVisible = on;
            if (!on)
            {
                _mascotTimer.Stop();
                _mascotState = MascotState.Gone;
                _mascotAngry = false;
                _npCoverFlip.AngleY = 0;
                _mascot.ZIndex = 0;
                ClearMascotNotes();
                _mascotVisitTimer.Stop();
                _npCoverCard.ClearValue(Canvas.LeftProperty);
                _npCoverCard.ClearValue(Canvas.TopProperty);
                _npCoverCard.Opacity = 1;
                _zipLayer.IsVisible = false;
                HideMascotTricks();
                HideMascotStageProps();
                HideMascotSwaps();
                HideMascotChores();
                HideMascotSeason();
                UpdateMascotAmbient(false);
                ResetCoverPlacement();
                ApplyMascotHeadroom(0);
                return;
            }
            _mascotMoodSeen = CurrentMood();
            _mascotVolumeSeen = _npIp is { } ip && _volumes.TryGetValue(TargetOf(ip), out var level) ? (level.Muted ? 0 : level.Volume) : null;
            _mascotLastActive = _mascotLastArtChange = DateTime.UtcNow;
            StartMascotPush();
        }
    }
}
