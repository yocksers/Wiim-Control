using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double MascotUfoChance = 0.01;
        private const double MagicSeconds = 3.6;
        private const double PaintLead = 0.3;
        private const double PaintTravel = 2.0;
        private const double PaintSeconds = PaintLead + PaintTravel + 0.4;
        private const double CrateSeconds = 4.8;
        private const double CrateLeft = 815;
        private const double CrateTop = 470;
        private const double CrateWidth = 210;
        private const double CrateHeight = 130;
        private const double CrateOutside = MascotStageWidth + 120;
        private const double SleeveScale = 0.28;
        private const double UfoSeconds = 6.2;
        private const double UfoWidth = 240;
        private static readonly Point CoverMiddle = new(MascotCoverLeft + StageCover / 2, StageCover / 2);
        private static readonly Point UfoSpot = new(MascotDanceSpot + MascotWidth / 2, 70);
        private static readonly Point CrateSlot = new(CrateLeft + CrateWidth / 2, CrateTop + 42);

        private readonly ScaleTransform _mascotCoverScale = new();
        private readonly TranslateTransform _mascotCoverShift = new();
        private readonly ScaleTransform _mascotScale = new();
        private readonly Canvas _mascotFx = new() { IsHitTestVisible = false, ZIndex = 3 };
        private readonly Canvas _mascotFxBack = new() { IsHitTestVisible = false, ZIndex = -2 };
        private readonly Canvas _propHat = new() { Width = 64, Height = 56, IsVisible = false };
        private readonly Line _propWand = new() { Stroke = new SolidColorBrush(Color.Parse("#111216")), StrokeThickness = 5, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Ellipse _propWandTip = new() { Width = 10, Height = 10, Fill = Brushes.White, IsVisible = false };
        private readonly List<Ellipse> _smoke = [];
        private readonly Canvas _roller = new() { Width = 44, Height = 620, IsVisible = false };
        private readonly Line _rollerHandle = new() { Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 6, StrokeLineCap = PenLineCap.Round, IsVisible = false };
        private readonly Canvas _crateBack = new() { Width = CrateWidth, Height = CrateHeight + 24, IsVisible = false };
        private readonly Canvas _crateFront = new() { Width = CrateWidth, Height = CrateHeight, IsVisible = false };
        private readonly List<(Border Sleeve, RotateTransform Tilt)> _sleeves = [];
        private readonly Canvas _ufo = new() { Width = UfoWidth, Height = 110, IsVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform() };
        private readonly List<Ellipse> _ufoLights = [];
        private readonly Polygon _ufoBeam = new() { IsVisible = false };

        private void BuildMascotTricks(Canvas figure)
        {
            foreach (var prop in new Control[] { _propHat, _propWand, _propWandTip }) figure.Children.Add(prop);
            _npStage.Children.Add(_mascotFxBack);
            _npStage.Children.Add(_mascotFx);
            _mascot.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            _mascot.RenderTransform = _mascotScale;

            var black = new SolidColorBrush(Color.Parse("#111216"));
            _propHat.Children.Add(Place(new Rectangle { Width = 44, Height = 46, Fill = black }, 10, 8));
            _propHat.Children.Add(Place(new Rectangle { Width = 44, Height = 8, Fill = new SolidColorBrush(Color.Parse("#C0392B")) }, 10, 12));
            _propHat.Children.Add(Place(new Ellipse { Width = 64, Height = 14, Fill = new SolidColorBrush(Color.Parse("#2A2C30")) }, 0, 0));
            _propHat.Children.Add(Place(new Ellipse { Width = 42, Height = 8, Fill = Brushes.Black }, 11, 3));

            var smokeBrush = new SolidColorBrush(Color.Parse("#D8DADF"));
            for (int i = 0; i < 9; i++)
            {
                var puff = new Ellipse { Width = 200, Height = 200, Fill = smokeBrush, IsVisible = false };
                _smoke.Add(puff);
                _mascotFx.Children.Add(puff);
            }

            _roller.Children.Add(Place(new Rectangle { Width = 44, Height = 14, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.Parse("#70767E")) }, 0, 0));
            _roller.Children.Add(Place(new Rectangle
            {
                Width = 36, Height = 590, RadiusX = 16, RadiusY = 16,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#C9C2AE"), 0), new GradientStop(Color.Parse("#F4EFE0"), 0.45), new GradientStop(Color.Parse("#A9A290"), 1) }
                }
            }, 4, 15));
            _roller.Children.Add(Place(new Rectangle { Width = 44, Height = 14, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.Parse("#70767E")) }, 0, 606));
            _mascotFx.Children.Add(_rollerHandle);
            _mascotFx.Children.Add(_roller);

            _crateBack.Children.Add(new Rectangle { Width = CrateWidth, Height = CrateHeight + 24, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.Parse("#4E3420")) });
            string[] sleeveColors = ["#2E4057", "#8E3B46", "#C9A227", "#3D6B4F", "#5B5BF6"];
            for (int i = 0; i < sleeveColors.Length; i++)
            {
                var tilt = new RotateTransform(-6);
                var sleeve = new Border
                {
                    Width = 150, Height = 150, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.Parse(sleeveColors[i])),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
                    RenderTransformOrigin = new RelativePoint(1, 1, RelativeUnit.Relative), RenderTransform = tilt, IsVisible = false
                };
                _sleeves.Add((sleeve, tilt));
                _mascotFxBack.Children.Add(sleeve);
            }
            _mascotFxBack.Children.Insert(0, _crateBack);
            var wood = new SolidColorBrush(Color.Parse("#8B5E34"));
            _crateFront.Children.Add(new Rectangle { Width = CrateWidth, Height = CrateHeight, RadiusX = 4, RadiusY = 4, Fill = wood });
            foreach (double y in new[] { 42.0, 86.0 })
                _crateFront.Children.Add(Place(new Rectangle { Width = CrateWidth, Height = 3, Fill = new SolidColorBrush(Color.Parse("#5E3D20")) }, 0, y));
            _crateFront.Children.Add(Place(new Rectangle { Width = 70, Height = 16, RadiusX = 8, RadiusY = 8, Fill = new SolidColorBrush(Color.Parse("#4E3420")) }, 70, 14));
            _mascotFx.Children.Add(_crateFront);

            _ufoBeam.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(150, 170, 255, 200), 0), new GradientStop(Color.FromArgb(30, 170, 255, 200), 1) }
            };
            _mascotFx.Children.Add(_ufoBeam);
            _ufo.Children.Add(Place(new Ellipse
            {
                Width = 104, Height = 76,
                Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(230, 200, 245, 255), 0), new GradientStop(Color.FromArgb(160, 90, 170, 210), 1) } }
            }, 68, 0));
            _ufo.Children.Add(Place(new Ellipse
            {
                Width = UfoWidth, Height = 58,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#D9DDE3"), 0), new GradientStop(Color.Parse("#5A6068"), 1) }
                }
            }, 0, 42));
            _ufo.Children.Add(Place(new Ellipse { Width = 196, Height = 22, Fill = new SolidColorBrush(Color.Parse("#3A3E44")) }, 22, 64));
            for (int i = 0; i < 6; i++)
            {
                var light = new Ellipse { Width = 12, Height = 12 };
                _ufoLights.Add(light);
                _ufo.Children.Add(Place(light, 38 + i * 30, 69));
            }
            _mascotFx.Children.Add(_ufo);
        }

        private void HideMascotTricks()
        {
            foreach (var prop in new Control[] { _propHat, _propWand, _propWandTip, _roller, _rollerHandle, _crateBack, _crateFront, _ufo, _ufoBeam }) prop.IsVisible = false;
            foreach (var puff in _smoke) puff.IsVisible = false;
            foreach (var (sleeve, _) in _sleeves) sleeve.IsVisible = false;
        }

        private void PlaceCover(Point middle, double scale)
        {
            _mascotCoverScale.ScaleX = _mascotCoverScale.ScaleY = scale;
            _mascotCoverShift.X = middle.X - CoverMiddle.X;
            _mascotCoverShift.Y = middle.Y - CoverMiddle.Y;
        }

        private void ResetCoverPlacement()
        {
            PlaceCover(CoverMiddle, 1);
            _npCoverOld.Clip = null;
            _npCoverCard.Opacity = 1;
            _mascotScale.ScaleX = _mascotScale.ScaleY = 1;
        }

        private static Point Lerp(Point from, Point to, double p) => new(from.X + (to.X - from.X) * p, from.Y + (to.Y - from.Y) * p);

        private static double Phase(double s, double start, double length) => Math.Clamp((s - start) / length, 0, 1);

        private void ShowNewArt(double blend)
        {
            _npCoverOld.Opacity = 0;
            if (_npBackdropOld.Source != null) BlendBackdrops(1 - blend);
        }

        private void EndMascotTrick(MascotState next)
        {
            ResetCoverPlacement();
            HideMascotTricks();
            FinishMascotSwap();
            _mascotRunFrom = Canvas.GetLeft(_mascot);
            SetMascotState(next);
        }

        private double StepMascotMagic(double s)
        {
            Canvas.SetLeft(_mascot, MascotDanceSpot);
            var hatHand = Hand(false, 40, 46);
            Center(_propHat, new Point(hatHand.X, hatHand.Y + 18));
            double wand = s < 0.7 ? -100 - Ease(s / 0.7) * 60 : s < 1.0 ? -160 + Ease((s - 0.7) / 0.3) * 65 : s < 2.9 ? -95 + Math.Sin(s * 9) * 6 : -30;
            if (s < 2.9)
            {
                _propWand.StartPoint = Hand(true, wand, 44);
                _propWand.EndPoint = Hand(true, wand, 104);
                _propWand.IsVisible = true;
                Center(_propWandTip, _propWand.EndPoint);
            }
            double bow = s < 2.9 ? 0 : Math.Sin(Math.Min(1, (s - 2.9) / 0.7) * Math.PI);
            Pose(lean: 30 * bow, bounce: s < 1.0 ? 0 : -Math.Abs(Math.Sin(s * 5)) * 3, arms: (40, wand), legs: (0, 0));
            if (s is > 0.9 and < 1.3) _mascotEyes.ScaleY = 0.5;

            double puff = Phase(s, 1.0, 0.9);
            for (int i = 0; i < _smoke.Count; i++)
            {
                var cloud = _smoke[i];
                cloud.IsVisible = puff is > 0 and < 1;
                if (!cloud.IsVisible) continue;
                double angle = i * Math.PI * 2 / _smoke.Count;
                double reach = 40 + puff * 220;
                double size = 160 + puff * 140 + (i % 3) * 20;
                cloud.Width = cloud.Height = size;
                Canvas.SetLeft(cloud, CoverMiddle.X + Math.Cos(angle) * reach * 0.8 - size / 2);
                Canvas.SetTop(cloud, CoverMiddle.Y + Math.Sin(angle) * reach * 0.7 - size / 2);
                cloud.Opacity = puff < 0.3 ? puff / 0.3 * 0.95 : 0.95 * (1 - (puff - 0.3) / 0.7);
            }

            var hat = new Point(Canvas.GetLeft(_mascot) + hatHand.X, MascotOffsetTop() + hatHand.Y + 10);
            if (s < 1.25) PlaceCover(CoverMiddle, 1);
            else if (s < 1.6)
            {
                PlaceCover(hat, 0.01);
                ShowNewArt(0);
            }
            else
            {
                double grow = Ease(Phase(s, 1.6, 1.3));
                PlaceCover(Lerp(hat, CoverMiddle, grow), 0.03 + 0.97 * grow);
                ShowNewArt(grow);
            }
            if (s >= MagicSeconds) EndMascotTrick(MascotState.Dance);
            return 0;
        }

        private double StepMascotPaint(double s)
        {
            double p = Ease(Phase(s, PaintLead, PaintTravel));
            double x = p * StageCover;
            _npCoverOld.Clip = new RectangleGeometry(new Rect(x, 0, StageCover - x, StageCover));
            if (_npBackdropOld.Source != null) BlendBackdrops(1 - p);
            double rollerLeft = MascotCoverLeft + x - 22;
            Canvas.SetLeft(_roller, rollerLeft);
            Canvas.SetTop(_roller, -10);
            _roller.IsVisible = true;
            _roller.Opacity = s < PaintLead + PaintTravel ? 1 : Math.Max(0, 1 - (s - PaintLead - PaintTravel) / 0.3);
            double mascotLeft = MascotCoverLeft + x - 262;
            Canvas.SetLeft(_mascot, mascotLeft);
            bool moving = s >= PaintLead && s < PaintLead + PaintTravel;
            Pose(lean: 6, bounce: moving ? -Math.Abs(Math.Sin(s * 9)) * 4 : 0, arms: (-70, -118), legs: moving ? Run(s, 9, 28) : (0, 0));
            var hand = Hand(true, -118, 46);
            _rollerHandle.StartPoint = new Point(mascotLeft + hand.X, MascotOffsetTop() + hand.Y);
            _rollerHandle.EndPoint = new Point(rollerLeft + 22, 410);
            _rollerHandle.IsVisible = _roller.Opacity > 0.5;
            if (s >= PaintSeconds)
            {
                _mascotFromRight = true;
                EndMascotTrick(MascotState.RunOff);
            }
            return 0;
        }

        private double StepMascotCrate(double s)
        {
            double crateX = s < 0.6 ? CrateOutside + (CrateLeft - CrateOutside) * Ease(s / 0.6)
                : s < 4.2 ? CrateLeft : CrateLeft + (CrateOutside - CrateLeft) * Phase(s, 4.2, 0.6) * Phase(s, 4.2, 0.6);
            Canvas.SetLeft(_crateBack, crateX);
            Canvas.SetTop(_crateBack, CrateTop - 24);
            Canvas.SetLeft(_crateFront, crateX);
            Canvas.SetTop(_crateFront, CrateTop);
            _crateBack.IsVisible = _crateFront.IsVisible = true;
            for (int i = 0; i < _sleeves.Count; i++)
            {
                var (sleeve, tilt) = _sleeves[i];
                Canvas.SetLeft(sleeve, crateX + 30);
                Canvas.SetTop(sleeve, CrateTop - 40 + i * 4);
                sleeve.IsVisible = true;
                double flick = Phase(s, 1.7 + i * 0.22, 0.25);
                double back = Phase(s, 3.0, 0.3);
                tilt.Angle = -6 + 34 * Ease(flick) * (1 - back);
            }

            double push = Ease(Phase(s, 0.5, 1.0));
            double mascotX = MascotDanceSpot + (CrateLeft - 200 - MascotDanceSpot) * push;
            Canvas.SetLeft(_mascot, mascotX);
            var high = new Point(CrateSlot.X, CrateTop - 70);
            if (s < 1.3) PlaceCover(Lerp(CoverMiddle, high, Ease(Phase(s, 0.5, 0.8))), 1 + (SleeveScale - 1) * Ease(Phase(s, 0.5, 0.8)));
            else if (s < 3.0) PlaceCover(Lerp(high, CrateSlot, Ease(Phase(s, 1.3, 0.3))), SleeveScale);
            else if (s < 3.5) PlaceCover(Lerp(CrateSlot, new Point(CrateSlot.X, 330), Ease(Phase(s, 3.0, 0.5))), SleeveScale);
            else
            {
                double home = Ease(Phase(s, 3.5, 0.7));
                PlaceCover(Lerp(new Point(CrateSlot.X, 330), CoverMiddle, home), SleeveScale + (1 - SleeveScale) * home);
            }
            if (s >= 2.2) ShowNewArt(Phase(s, 2.2, 2.0));

            if (s < 0.5) Pose(lean: 0, bounce: 0, arms: (-95, -85), legs: (0, 0));
            else if (s < 1.5) Pose(lean: 14, bounce: -Math.Abs(Math.Sin(s * 14)) * 5, arms: (-95, -85), legs: Run(s, 16, 38));
            else if (s < 3.0)
            {
                Pose(lean: 18, bounce: 0, arms: (30, -60 + Math.Sin(s * 14) * 28), legs: (0, 0));
                _mascotGaze.Y = 5;
                _mascotGaze.X = 4;
            }
            else if (s < 4.2) Pose(lean: 0, bounce: -Math.Abs(Math.Sin(s * 6)) * 6, arms: (40, -165), legs: (0, 0));
            else CheerDance(s);
            if (s >= CrateSeconds)
            {
                _mascotFromRight = false;
                EndMascotTrick(MascotState.WalkOff);
            }
            return 0;
        }

        private double StepMascotUfo(double s)
        {
            double floor = MascotOffsetTop();
            double descend = Ease(Phase(s, 0, 1.0));
            double leave = Phase(s, 4.9, 0.7);
            var ufo = new Point(UfoSpot.X + leave * leave * 1800, -900 + (UfoSpot.Y + 900) * descend - leave * 700 + Math.Sin(s * 3) * 8);
            Canvas.SetLeft(_ufo, ufo.X - UfoWidth / 2);
            Canvas.SetTop(_ufo, ufo.Y - 55);
            _ufo.IsVisible = true;
            ((RotateTransform)_ufo.RenderTransform!).Angle = s is > 2.6 and < 3.3 ? Math.Sin(s * 40) * 6 : Math.Sin(s * 2) * 2;
            for (int i = 0; i < _ufoLights.Count; i++)
                _ufoLights[i].Fill = (int)(s * (s is > 2.6 and < 3.3 ? 16 : 5) + i) % 2 == 0 ? Brushes.Yellow : Palette.AccentText;

            double beam = s < 1.0 ? 0 : s < 1.3 ? (s - 1.0) / 0.3 : s < 2.6 ? 1 : s < 3.3 ? 0 : s < 4.4 ? 1 : Math.Max(0, 1 - (s - 4.4) / 0.4);
            _ufoBeam.IsVisible = beam > 0;
            _ufoBeam.Opacity = beam;
            _ufoBeam.Points = [new Point(ufo.X - 50, ufo.Y + 40), new Point(ufo.X + 50, ufo.Y + 40), new Point(ufo.X + 140, StageCover + 10), new Point(ufo.X - 140, StageCover + 10)];

            double up = s < 1.3 ? 0 : s < 2.6 ? Ease((s - 1.3) / 1.3) : s < 3.3 ? 1 : s < 4.4 ? 1 - Ease((s - 3.3) / 1.1) : 0;
            var inside = new Point(ufo.X, ufo.Y + 30);
            PlaceCover(Lerp(CoverMiddle, inside, up), 1 - 0.95 * up);
            if (s >= 2.8) ShowNewArt(Phase(s, 3.3, 1.1));
            double mascotX = MascotDanceSpot + (ufo.X - MascotWidth / 2 - MascotDanceSpot) * up;
            Canvas.SetLeft(_mascot, mascotX);
            _mascotScale.ScaleX = _mascotScale.ScaleY = 1 - 0.85 * up;
            _mascotShadow.Opacity = 1 - up;
            double offset = (ufo.Y - 40 - floor - MascotHeight / 2) * up;
            if (up > 0) Pose(lean: up * 540, bounce: 0, arms: (150, -150), legs: (Math.Sin(s * 20) * 25, -Math.Sin(s * 20) * 25));
            else if (s < 1.3)
            {
                Pose(lean: -6, bounce: 0, arms: (60, -60), legs: (0, 0));
                _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.3;
                _mascotGaze.Y = -6;
            }
            else
            {
                double daze = Math.Max(0, 1 - (s - 4.4) / 1.6);
                Pose(lean: Math.Sin(s * 7) * 12 * daze, bounce: 0, arms: (30 + Math.Sin(s * 7) * 15, -30 + Math.Sin(s * 7) * 15), legs: (0, 0));
                _mascotEyes.ScaleY = 0.4 + 0.6 * (1 - daze);
            }
            if (s >= UfoSeconds) EndMascotTrick(MascotState.Dance);
            return offset;
        }
    }
}
