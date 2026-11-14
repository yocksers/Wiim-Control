using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly IBrush GlassBarBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
        private readonly Button _npControlsButton = new();
        private const double UnderControlsScale = 0.8;
        private const double UnderControlsGap = 22;
        private readonly LayoutTransformControl _npUnder = new()
        {
            Width = StageCover, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false,
            LayoutTransform = new ScaleTransform(UnderControlsScale, UnderControlsScale)
        };
        private readonly StackPanel _npUnderPanel = new() { Width = StageCover / UnderControlsScale };

        private static bool CanPlaceControlsUnder(NowPlayingStyle style) =>
            style is NowPlayingStyle.Cover or NowPlayingStyle.Vinyl or NowPlayingStyle.Cassette or NowPlayingStyle.Cd or NowPlayingStyle.Gatefold
                or NowPlayingStyle.Mascot;

        private static string ControlsStyleName(NowPlayingControlsStyle style) => $"Style {(int)style + 1}";

        private Button BuildControlsButton()
        {
            _npControlsButton.Classes.Add("pill");
            ToolTip.SetTip(_npControlsButton, "Show or hide the playback controls, and choose how they look");
            _npControlsButton.Click += (_, _) =>
            {
                var choices = new List<DropdownChoice>
                {
                    DropdownAction("Show controls", () => SetNowPlayingControls(true, null), _owner.NowPlayingControlsShown),
                    DropdownAction("Hide controls", () => SetNowPlayingControls(false, null), !_owner.NowPlayingControlsShown),
                    DropdownHeading("Place")
                };
                bool canMove = CanPlaceControlsUnder(_owner.NowPlayingStyle);
                foreach (bool under in new[] { false, true })
                    choices.Add(new DropdownChoice(under ? "Under the artwork" : "On the artwork", canMove && under == _owner.NowPlayingControlsUnder, canMove,
                        () => SetControlsUnder(under)));
                choices.Add(DropdownHeading("Style"));
                choices.AddRange(Enum.GetValues<NowPlayingControlsStyle>().Select(style =>
                    DropdownAction(ControlsStyleName(style), () => SetNowPlayingControls(true, style), style == _owner.NowPlayingControlsStyle)));
                ShowDropdown(_npControlsButton, choices);
            };
            ApplyNowPlayingControls();
            return _npControlsButton;
        }

        private Task SetControlsUnder(bool under)
        {
            if (under != _owner.NowPlayingControlsUnder) _owner.NowPlayingControlsUnder = under;
            ArrangeNowPlayingParts();
            PlaceNowPlayingControls();
            LayoutLyrics();
            return Task.CompletedTask;
        }

        private Task SetNowPlayingControls(bool shown, NowPlayingControlsStyle? style)
        {
            if (style is { } chosen && chosen != _owner.NowPlayingControlsStyle)
            {
                _owner.NowPlayingControlsStyle = chosen;
                ArrangeNowPlayingParts();
            }
            if (shown != _owner.NowPlayingControlsShown) _owner.NowPlayingControlsShown = shown;
            ApplyNowPlayingControls();
            return Task.CompletedTask;
        }

        private void ApplyNowPlayingControls()
        {
            bool shown = _owner.NowPlayingControlsShown;
            _npControlsButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock { Text = shown ? "Controls" : "Controls hidden", VerticalAlignment = VerticalAlignment.Center },
                    new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            _npBigButtonBar.IsVisible = _npBigPrevious.IsVisible = _npBigNext.IsVisible = shown;
            if (_npParts is { } parts) parts.ButtonBar.IsVisible = parts.VolumeRow.IsVisible = shown;
            if (_npIp is { } ip && _npControls is { } controls && _statuses.ContainsKey(ip)) ApplyNowPlayingTo(ip, controls);
        }

        private void ApplyControlsStyle(NowPlayingParts p)
        {
            var style = _owner.NowPlayingControlsStyle;
            bool classic = style == NowPlayingControlsStyle.Classic;
            double gap = style switch { NowPlayingControlsStyle.Minimal => 14, NowPlayingControlsStyle.Glass => 12, _ => 18 };
            foreach (var (control, size) in new[] { (p.Shuffle, 20.0), (p.Repeat, 20.0), (p.Previous, 26.0), (p.Next, 26.0), (p.PlayPause, 34.0) })
                if (control is Button { Content: PathIcon icon }) icon.Width = icon.Height = size;

            p.Shuffle.HorizontalAlignment = classic ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            p.Repeat.HorizontalAlignment = classic ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            p.Previous.HorizontalAlignment = p.PlayPause.HorizontalAlignment = p.Next.HorizontalAlignment = HorizontalAlignment.Center;
            p.Shuffle.Margin = p.Repeat.Margin = p.PlayPause.Margin = classic ? default : new Thickness(gap / 2, 0);
            p.Previous.Margin = p.Next.Margin = classic ? new Thickness(gap, 0) : new Thickness(gap / 2, 0);

            bool glass = style == NowPlayingControlsStyle.Glass;
            foreach (var bar in new[] { p.ButtonBar, _npBigButtonBar })
            {
                bar.Background = glass ? GlassBarBrush : null;
                bar.CornerRadius = new CornerRadius(glass ? 40 : 0);
                bar.Padding = glass ? new Thickness(12, 6) : default;
                bar.HorizontalAlignment = glass ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            }

            bool slim = style == NowPlayingControlsStyle.Minimal;
            foreach (var slider in new[] { _npControls?.Position, _npVolume?.Slider })
            {
                if (slider == null) continue;
                slider.TrackThickness = slim ? 3 : 4;
                slider.KnobSize = slim ? 10 : 14;
            }
        }
    }
}
