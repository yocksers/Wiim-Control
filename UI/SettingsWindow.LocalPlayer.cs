using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const string LocalPlayerKey = "this computer";

        private sealed record LocalPlayerControls(TextBlock Title, TextBlock Artist, PathIcon PlayPauseIcon, Button Shuffle, PathIcon ShuffleIcon,
            Button Repeat, PathIcon RepeatIcon, Button Previous, Button PlayPause, Button Next, Grid PositionRow, LevelSlider Position,
            TextBlock Elapsed, TextBlock Total, VolumeControls Volume);

        private LocalPlayerControls? _localControls;
        private bool _localHooked;
        private bool _localVolumeUpdating;

        private void HookLocalPlayer()
        {
            if (_localHooked || !LocalPlayerSupported) return;
            _localHooked = true;
            _owner.LocalPlayerChanged += OnLocalPlayerChanged;
            Closed += (_, _) => _owner.LocalPlayerChanged -= OnLocalPlayerChanged;
            PushLocalStatus();
        }

        private void OnLocalPlayerChanged()
        {
            ApplyLocalPlayer();
            PushLocalStatus();
        }

        private void PushLocalStatus()
        {
            var status = _owner.LocalStatus();
            _statuses[LocalIp] = status;
            bool busy = IsDraggingVolume(LocalIp) || _pendingVolumes.ContainsKey(LocalIp) || _volumeSending.Contains(LocalIp);
            if (!busy) _volumes[LocalIp] = status.Volume;
            ApplyNowPlaying(LocalIp);
            ApplyVolume(LocalIp);
        }

        private Control BuildLocalPlayer()
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
            ToolTip.SetTip(previous, "Previous");
            ToolTip.SetTip(playPause, "Play or pause");
            ToolTip.SetTip(next, "Next");
            shuffle.Click += (_, _) => _owner.ToggleLocalShuffle();
            repeat.Click += (_, _) => _owner.CycleLocalRepeat();
            previous.Click += (_, _) => _owner.LocalPrevious();
            playPause.Click += (_, _) => _owner.LocalPlayPause();
            next.Click += (_, _) => _owner.LocalNext();

            Border Slot(Control button) => new() { Width = LibrarySideButtonSlot, Child = button, Margin = new Thickness(6, 0) };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Slot(shuffle), Slot(previous), playPause, Slot(next), Slot(repeat) }
            };
            var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { title, artist } };
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            top.Children.Add(info);
            Grid.SetColumn(buttons, 1);
            top.Children.Add(buttons);

            var positionRow = BuildPositionRow(seconds =>
            {
                _owner.LocalSeek(seconds);
                return Task.CompletedTask;
            }, out var position, out var elapsed, out var total);
            positionRow.Margin = default;

            var (mute, muteIcon) = IconButton(Icons.Volume);
            var slider = new LevelSlider { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, WheelChangesValue = false };
            var value = new TextBlock { Classes = { "muted" }, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = slider.Value.ToString();
                if (!_localVolumeUpdating) _owner.SetLocalVolume(slider.Value);
            };
            mute.Click += (_, _) => _owner.ToggleLocalMute();
            var volume = new VolumeControls(slider, value, mute, muteIcon);
            var volumeRow = VolumeRow(volume);
            volumeRow.Margin = new Thickness(24, 0, 0, 0);
            volumeRow.Width = LibraryVolumeWidth;
            volumeRow.VerticalAlignment = positionRow.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(volumeRow, "Volume on this computer");
            var sliders = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0), MinHeight = 32 };
            sliders.Children.Add(positionRow);
            Grid.SetColumn(volumeRow, 1);
            sliders.Children.Add(volumeRow);

            _localControls = new LocalPlayerControls(title, artist, playPauseIcon, shuffle, shuffleIcon, repeat, repeatIcon, previous, playPause, next,
                positionRow, position, elapsed, total, volume);
            return new StackPanel { MaxWidth = LibraryPlayerWidth + LibraryVolumeWidth, Children = { top, sliders } };
        }

        private void ApplyLocalPlayer()
        {
            if (_localControls is not { } c) return;
            var track = _owner.LocalNowPlaying;
            var shown = _owner.LocalStatus();
            c.Title.Text = track == null ? "Nothing playing" : shown.Title;
            c.Artist.Text = track == null ? "Pick music above to play it on this computer"
                : string.Join("  ·  ", new[] { shown.Artist, shown.Album }.Where(t => t.Length > 0));
            ToolTip.SetTip(c.Title, track == null ? null : shown.Title);
            Icons.SetPlayPause(c.PlayPauseIcon, _owner.LocalPlaying);
            c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = track != null;

            bool shuffle = _owner.LocalShuffle;
            int repeat = _owner.LocalRepeat;
            SetActive(c.ShuffleIcon, shuffle);
            SetActive(c.RepeatIcon, repeat > 0);
            c.RepeatIcon.Data = repeat == 1 ? Icons.RepeatOnce : Icons.Repeat;
            ToolTip.SetTip(c.Shuffle, shuffle ? "Shuffle is on" : "Shuffle is off");
            ToolTip.SetTip(c.Repeat, repeat switch { 1 => "Repeat one", 2 => "Repeat all", _ => "Repeat is off" });

            int durationMs = _owner.LocalDurationMs;
            c.PositionRow.IsVisible = _owner.LocalPlayerActive && durationMs > 0;
            if (c.PositionRow.IsVisible)
            {
                c.Position.Maximum = Math.Max(1, durationMs / 1000);
                c.Total.Text = FormatTime(durationMs / 1000);
                ShowLocalPosition(c);
            }

            _localVolumeUpdating = true;
            if (!c.Volume.Slider.IsDragging) c.Volume.Slider.Value = _owner.LocalVolume;
            c.Volume.Value.Text = _owner.LocalVolume.ToString();
            _localVolumeUpdating = false;
            c.Volume.MuteIcon.Data = _owner.LocalMuted ? Icons.Mute : Icons.Volume;
            ToolTip.SetTip(c.Volume.Mute, _owner.LocalMuted ? "Unmute" : "Mute");
        }

        private void ShowLocalPosition(LocalPlayerControls c)
        {
            if (c.Position.IsDragging) return;
            int seconds = Math.Clamp(_owner.LocalPositionMs / 1000, 0, c.Position.Maximum);
            c.Position.Value = seconds;
            c.Elapsed.Text = FormatTime(seconds);
        }

        private void AdvanceLocalPlayer()
        {
            if (_localControls is not { } c || !_owner.LocalPlaying) return;
            if (!c.PositionRow.IsVisible && _owner.LocalDurationMs > 0) ApplyLocalPlayer();
            else ShowLocalPosition(c);
        }
    }
}
