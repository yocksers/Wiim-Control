using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double BigDiscSize = 900;
        private const double BigLabelSize = 300;

        private sealed record NowPlayingParts(StackPanel Panel, TextBlock Title, TextBlock Artist, Control DetailRow, Control PositionRow, Grid Buttons,
            Control Shuffle, Control Previous, Control PlayPause, Control Next, Control Repeat, Control VolumeRow);

        private readonly Panel _npBigDisc = new() { Width = BigDiscSize, Height = BigDiscSize };
        private readonly Spinner _npBigLabelSpin = new(BigLabelSize, DegreesPerSecond);
        private readonly Rotate3DTransform _npBigDiscFlip = new() { Depth = 2200 };
        private readonly Canvas _npBigControls = new() { Width = BigDiscSize, Height = BigDiscSize };
        private readonly StackPanel _npBigTop = new() { Width = 560 };
        private readonly Border _npBigPrevious = new() { Width = 120, Height = 120 };
        private readonly Border _npBigNext = new() { Width = 120, Height = 120 };
        private readonly Grid _npBigButtons = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,*") };
        private readonly StackPanel _npBigBottom = new() { Width = 460 };
        private NowPlayingParts? _npParts;
        private NowPlayingStyle _npPartsLayout;

        private void BuildBigVinyl()
        {
            _npBigDisc.RenderTransform = _npBigDiscFlip;
            _npBigDisc.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(BigDiscSize / 2), BoxShadow = BoxShadows.Parse("0 16 60 0 #E0000000"),
                Background = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#2A2A2A"), 0),
                        new GradientStop(Color.Parse("#141414"), 0.45),
                        new GradientStop(Color.Parse("#0A0A0A"), 0.9),
                        new GradientStop(Color.Parse("#1C1C1C"), 1)
                    }
                }
            });
            for (double d = BigDiscSize - 24; d > BigLabelSize + 40; d -= 10)
                _npBigDisc.Children.Add(new Ellipse
                {
                    Width = d, Height = d, Stroke = new SolidColorBrush(Color.FromArgb((byte)(d % 30 < 10 ? 30 : 15), 255, 255, 255)),
                    StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            var label = new Ellipse { Width = BigLabelSize, Height = BigLabelSize, Fill = Palette.Input };
            var old = new Ellipse { Width = BigLabelSize, Height = BigLabelSize, Opacity = 0, IsHitTestVisible = false };
            _npArtShapes.Add((label, old));
            _npBigLabelSpin.Target.HorizontalAlignment = HorizontalAlignment.Center;
            _npBigLabelSpin.Target.VerticalAlignment = VerticalAlignment.Center;
            _npBigLabelSpin.Target.Children.Add(label);
            _npBigLabelSpin.Target.Children.Add(old);
            _npBigDisc.Children.Add(_npBigLabelSpin.Target);
            _npBigDisc.Children.Add(new Ellipse
            {
                Width = BigLabelSize, Height = BigLabelSize, Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), StrokeThickness = 3,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            _npBigDisc.Children.Add(new Ellipse
            {
                Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse("#0E0E0E")),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            _npBigDisc.Children.Add(new Ellipse
            {
                IsHitTestVisible = false,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25),
                        new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.45),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55),
                        new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.75),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.85)
                    }
                }
            });

            _npBigBottom.Children.Add(_npBigButtons);
            _npBigControls.Children.Add(Place(_npBigTop, (BigDiscSize - 560) / 2, 140));
            _npBigControls.Children.Add(Place(_npBigPrevious, 135, BigDiscSize / 2 - 60));
            _npBigControls.Children.Add(Place(_npBigNext, BigDiscSize - 255, BigDiscSize / 2 - 60));
            _npBigControls.Children.Add(Place(_npBigBottom, (BigDiscSize - 460) / 2, 642));
        }

        private void ArrangeNowPlayingParts()
        {
            if (_npParts is not { } p) return;
            var layout = _owner.NowPlayingStyle is NowPlayingStyle.BigVinyl or NowPlayingStyle.Reel ? _owner.NowPlayingStyle : NowPlayingStyle.Cover;
            if (layout == _npPartsLayout) return;
            _npPartsLayout = layout;
            foreach (var part in new Control[] { p.Title, p.Artist, p.DetailRow, p.PositionRow, p.Buttons, p.Shuffle, p.Previous, p.PlayPause, p.Next, p.Repeat, p.VolumeRow })
                Detach(part);
            bool centred = layout != NowPlayingStyle.Cover;
            p.Title.TextAlignment = p.Artist.TextAlignment = centred ? TextAlignment.Center : TextAlignment.Left;
            p.DetailRow.HorizontalAlignment = centred ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            if (layout == NowPlayingStyle.BigVinyl)
            {
                _npBigTop.Children.AddRange([p.Title, p.Artist, p.DetailRow]);
                _npBigPrevious.Child = p.Previous;
                _npBigNext.Child = p.Next;
                _npBigButtons.Children.AddRange([p.Shuffle, p.PlayPause, p.Repeat]);
                _npBigBottom.Children.AddRange([p.PositionRow, p.VolumeRow]);
                return;
            }
            p.Buttons.Children.AddRange([p.Shuffle, p.Previous, p.PlayPause, p.Next, p.Repeat]);
            if (layout == NowPlayingStyle.Reel)
            {
                _npReelTop.Children.AddRange([p.Title, p.Artist, p.DetailRow]);
                _npReelBottom.Children.AddRange([p.Buttons, p.PositionRow, p.VolumeRow]);
            }
            else p.Panel.Children.AddRange([p.Title, p.Artist, p.DetailRow, p.PositionRow, p.Buttons, p.VolumeRow]);
        }

        private void ClearBigVinylControls()
        {
            _npParts = null;
            _npPartsLayout = NowPlayingStyle.Cover;
            _npBigTop.Children.Clear();
            _npBigPrevious.Child = _npBigNext.Child = null;
            _npBigButtons.Children.Clear();
            while (_npBigBottom.Children.Count > 1) _npBigBottom.Children.RemoveAt(1);
            _npReelTop.Children.Clear();
            _npReelBottom.Children.Clear();
        }

        private static void Detach(Control control)
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
            else if (control.Parent is Decorator decorator) decorator.Child = null;
        }
    }
}
