using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double BigReelWidth = 1200;
        private const double BigReelHeight = 880;
        private const double BigReelSize = 420;
        private const double BigReelTopWidth = 720;
        private const double BigReelBottomWidth = 520;

        private readonly Canvas _npBigReel = new() { Width = BigReelWidth, Height = BigReelHeight };
        private readonly StackPanel _npReelTop = new() { Width = BigReelTopWidth };
        private readonly StackPanel _npReelBottom = new() { Width = BigReelBottomWidth };
        private TapeReels? _npBigReelReels;

        private void BuildBigReel()
        {
            var supply = new Point(300, 370);
            var takeUp = new Point(900, 370);
            var supplyTape = new Line { Stroke = TapeBrush, StrokeThickness = 3 };
            var takeUpTape = new Line { Stroke = TapeBrush, StrokeThickness = 3 };
            _npBigReel.Children.Add(supplyTape);
            _npBigReel.Children.Add(takeUpTape);
            _npBigReel.Children.Add(new Line { StartPoint = new Point(520, 652), EndPoint = new Point(680, 652), Stroke = TapeBrush, StrokeThickness = 3, ZIndex = 1 });

            var reels = _npBigReelReels = new TapeReels(_npBigReel, supply, takeUp, 118, 199, -1, BigReelSize, MetalReel);
            reels.Wound += () =>
            {
                supplyTape.StartPoint = new Point(supply.X, supply.Y + reels.SupplyRadius);
                supplyTape.EndPoint = new Point(508, 640);
                takeUpTape.StartPoint = new Point(takeUp.X, takeUp.Y + reels.TakeUpRadius);
                takeUpTape.EndPoint = new Point(692, 640);
            };
            reels.Wind(0.5);

            var chrome = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#E4E7EA"), 0), new GradientStop(Color.Parse("#8C9298"), 1) }
            };
            foreach (var x in new[] { 520.0, 680.0 })
                _npBigReel.Children.Add(Place(new Ellipse { Width = 24, Height = 24, Fill = chrome, Stroke = TapeBrush, StrokeThickness = 3 }, x - 12, 628));
            _npBigReel.Children.Add(Place(new Rectangle { Width = 28, Height = 34, RadiusX = 5, RadiusY = 5, Fill = chrome }, 586, 628));

            _npBigReel.Children.Add(Place(_npReelTop, (BigReelWidth - BigReelTopWidth) / 2, 40));
            _npBigReel.Children.Add(Place(_npReelBottom, (BigReelWidth - BigReelBottomWidth) / 2, 690));
        }
    }
}
