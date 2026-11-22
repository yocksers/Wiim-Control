using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double LibraryPlayerWidth = 760;
        private const double LibraryPlayButtonSize = 44;
        private const double LibrarySideButtonSlot = 44;
        private const double LibraryVolumeWidth = 210;

        private readonly Border _libraryPlayer = new() { IsVisible = false, Margin = new Thickness(0, 12, 12, 0), Padding = new Thickness(16, 10, 16, 12) };
        private Control? _libraryPage;
        private string? _libraryPlayerKey;
        private string? _libraryPlayerIp;
        private NowPlayingControls? _libraryPlayerControls;
        private VolumeControls? _libraryVolume;

        private void ShowLibraryPlayer(string? ip)
        {
            string? key = ip == null ? null : $"{ip}|{_owner.IsDeviceLocked(ip)}";
            if (key == _libraryPlayerKey) return;
            _libraryPlayerKey = key;
            _libraryPlayerIp = ip;
            _libraryPlayerControls = null;
            _libraryVolume = null;
            _libraryPlayer.Child = ip == null ? null : BuildLibraryPlayer(ip);
            _libraryPlayer.IsVisible = ip != null;
            if (ip != null && _statuses.ContainsKey(ip)) ApplyLibraryPlayer(ip);
            if (ip != null && _libraryVolume != null) ApplyVolumeTo(ip, _libraryVolume);
        }

        private Control BuildLibraryPlayer(string ip)
        {
            _libraryPlayer.Classes.Set("card", true);
            (Button Button, PathIcon Icon) Side(StreamGeometry geometry, double size)
            {
                var (button, icon) = IconButton(geometry);
                icon.Width = icon.Height = size;
                button.VerticalAlignment = VerticalAlignment.Center;
                return (button, icon);
            }
            var (shuffle, shuffleIcon) = Side(Icons.Shuffle, 20);
            var (repeat, repeatIcon) = Side(Icons.Repeat, 20);
            var (previous, _) = Side(Icons.Previous, 24);
            var (next, _) = Side(Icons.Next, 24);
            var playPauseIcon = new PathIcon { Data = Icons.Play, Width = 18, Height = 18, Foreground = Palette.Window };
            Icons.SetPlayPause(playPauseIcon, false);
            var playPause = new Button
            {
                Content = playPauseIcon, Width = LibraryPlayButtonSize, Height = LibraryPlayButtonSize, CornerRadius = new CornerRadius(LibraryPlayButtonSize / 2),
                Background = Palette.Text, Padding = new Thickness(0), Margin = new Thickness(14, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(shuffle, "Shuffle");
            ToolTip.SetTip(previous, "Previous");
            ToolTip.SetTip(playPause, "Play or pause");
            ToolTip.SetTip(next, "Next");
            ToolTip.SetTip(repeat, "Repeat");
            shuffle.IsVisible = repeat.IsVisible = false;
            shuffle.Click += async (_, _) => await ToggleShuffleAsync(ip);
            repeat.Click += async (_, _) => await CycleRepeatAsync(ip);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");

            Border Slot(Control button) => new() { Width = LibrarySideButtonSlot, Child = button, Margin = new Thickness(6, 0) };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Slot(shuffle), Slot(previous), playPause, Slot(next), Slot(repeat) }
            };
            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            positionRow.Margin = default;
            _libraryVolume = CreateVolumeControls(ip);
            var volumeRow = VolumeRow(_libraryVolume);
            volumeRow.Margin = new Thickness(24, 0, 0, 0);
            volumeRow.Width = LibraryVolumeWidth;
            volumeRow.VerticalAlignment = positionRow.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(volumeRow, "Volume");
            var sliders = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0), MinHeight = 32 };
            sliders.Children.Add(positionRow);
            Grid.SetColumn(volumeRow, 1);
            sliders.Children.Add(volumeRow);

            var art = new ArtworkView { IsVisible = false };
            var title = new TextBlock();
            var artist = new TextBlock();
            var detail = new TextBlock();
            var playing = new PlayingIndicator();
            _libraryPlayerControls = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);
            buttons.IsEnabled = positionRow.IsEnabled = volumeRow.IsEnabled = !_owner.IsDeviceLocked(ip);
            return new StackPanel { MaxWidth = LibraryPlayerWidth + LibraryVolumeWidth, Children = { buttons, sliders } };
        }

        private void ApplyLibraryPlayer(string ip)
        {
            if (ip != _libraryPlayerIp || _libraryPlayerControls == null) return;
            ApplyNowPlayingTo(ip, _libraryPlayerControls);
        }

        private void AdvanceLibraryPlayer(string ip, int positionMs)
        {
            if (ip == _libraryPlayerIp && _libraryPlayerControls != null) ShowPosition(_libraryPlayerControls, positionMs);
        }
    }
}
