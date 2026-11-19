using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double BarArtSize = 64;
        private const double BarTextMinWidth = 130;
        private const double BarPositionMinWidth = 100;
        private const double BarButtonWidth = 72;
        private const double BarTransportWidth = 144;
        private const double BarLeftMinWidth = BarArtSize + BarTextMinWidth + 30;
        private const double BarRightMinWidth = BarPositionMinWidth + 32 + 3 * (BarButtonWidth + 4);
        private const double BarMinWidth = 2 * (BarRightMinWidth > BarLeftMinWidth ? BarRightMinWidth : BarLeftMinWidth) + BarTransportWidth + 28;
        private const double PageSideMargins = 60;
        private const double SidebarWidth = 250;
        private const double HiddenSidebarWidth = 34;
        private const double BaseMinWidth = 960;

        private readonly Border _playerBar = new() { IsVisible = false, Margin = new Thickness(0, 12, 0, 0) };
        private Control? _queuePage;
        private string? _barIp;
        private string? _barKey;
        private NowPlayingControls? _barControls;
        private TextBlock? _barInputLabel;
        private PathIcon? _barInputIcon;
        private Flyout? _barVolumeFlyout;
        private readonly Slider _barVolumeSlider = new()
        {
            Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Height = 160, TickFrequency = 1, IsSnapToTickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        private readonly TextBlock _barVolumeValue = new() { Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly PathIcon _barVolumeIcon = new() { Data = Icons.Volume, Width = 22, Height = 22 };
        private readonly PathIcon _barMuteIcon = new() { Data = Icons.Volume, Width = 18, Height = 18 };
        private readonly TextBlock _barFlyoutValue = new() { HorizontalAlignment = HorizontalAlignment.Center };

        private Control BuildDevicesBody()
        {
            _deviceList.Margin = new Thickness(0, 0, 12, 0);
            var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            body.Children.Add(new ScrollViewer { Content = _deviceList });
            Grid.SetRow(_playerBar, 1);
            body.Children.Add(_playerBar);
            _playerBar.Classes.Add("card");
            _playerBar.Padding = new Thickness(14, 10);
            _barVolumeSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty || _barIp is not { } ip) return;
                _barVolumeValue.Text = _barFlyoutValue.Text = ((int)_barVolumeSlider.Value).ToString();
                if (_volumeUpdating) return;
                if (BarGroup is { } group)
                {
                    group.Controls.Slider.Value = (int)_barVolumeSlider.Value;
                    return;
                }
                _volumeTouched[ip] = DateTime.UtcNow;
                _pendingVolumes[ip] = (int)_barVolumeSlider.Value;
                if (!_volumeSendTimer.IsEnabled) _volumeSendTimer.Start();
                ApplyGroupVolume(ip);
            };
            var mute = new Button { Content = _barMuteIcon, HorizontalAlignment = HorizontalAlignment.Center };
            mute.Classes.Add("icon");
            ToolTip.SetTip(mute, "Mute");
            mute.Click += async (_, _) =>
            {
                if (BarGroup is { } group) await ToggleGroupMuteAsync(group);
                else if (_barIp is { } ip) await ToggleDeviceMuteAsync(ip);
            };
            _barVolumeFlyout = new Flyout
            {
                Placement = PlacementMode.Top,
                Content = ScaledPopup(new StackPanel { Spacing = 8, Margin = new Thickness(4, 6), Children = { _barFlyoutValue, _barVolumeSlider, mute } })
            };
            return body;
        }

        private void UpdatePlayerBar()
        {
            UpdateMinimumWidth();
            string? ip = _owner.CompactDevices && _owner._deviceIp is { Length: > 0 } active && _owner._knownDevices.Any(d => d.Ip == active) ? active : null;
            string? key = ip == null ? null : $"{ip}|{_owner.IsDeviceLocked(ip)}";
            if (key == _barKey) return;
            _barKey = key;
            _barIp = ip;
            _barControls = null;
            _barInputLabel = null;
            _playerBar.Child = ip == null ? null : BuildPlayerBar(ip);
            _playerBar.IsVisible = ip != null;
        }

        private void UpdateMinimumWidth()
        {
            double needed = _owner.CompactDevices ? (_owner._sidebarHidden ? HiddenSidebarWidth : SidebarWidth) + PageSideMargins + BarMinWidth : 0;
            double width = Math.Max(BaseMinWidth, needed) * _scale;
            if (Screens.ScreenFromVisual(this) is { } screen) width = Math.Min(width, screen.WorkingArea.Width / screen.Scaling);
            MinWidth = width;
            if (WindowState == WindowState.Normal && Width < MinWidth) Width = MinWidth;
        }

        private Control BuildPlayerBar(string ip)
        {
            bool locked = _owner.IsDeviceLocked(ip);
            var art = new ArtworkView { Width = BarArtSize, Height = BarArtSize, PlaceholderSize = 28, VerticalAlignment = VerticalAlignment.Center };
            HookCoverOpen(art, ip);
            var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 16, 0), MinWidth = BarTextMinWidth, Children = { title, artist } };

            (Button, PathIcon) Transport(StreamGeometry icon, double size)
            {
                var (button, path) = IconButton(icon);
                path.Width = path.Height = size;
                button.VerticalAlignment = VerticalAlignment.Center;
                return (button, path);
            }
            var (previous, _) = Transport(Icons.Previous, 26);
            var (next, _) = Transport(Icons.Next, 26);
            var playPauseIcon = new PathIcon { Data = Icons.Play, Width = 20, Height = 20, Foreground = Palette.Text };
            var playPause = new Button
            {
                Content = playPauseIcon, Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Palette.Accent, Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0)
            };
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var (shuffle, shuffleIcon) = IconButton(Icons.Shuffle);
            var (repeat, repeatIcon) = IconButton(Icons.Repeat);
            var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), VerticalAlignment = VerticalAlignment.Center };
            transport.Children.Add(previous);
            Grid.SetColumn(playPause, 1);
            transport.Children.Add(playPause);
            Grid.SetColumn(next, 2);
            transport.Children.Add(next);

            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            positionRow.Margin = new Thickness(20, 0, 12, 0);
            positionRow.VerticalAlignment = VerticalAlignment.Center;
            positionRow.MinWidth = BarPositionMinWidth;
            var detail = new TextBlock();
            var playing = new PlayingIndicator();
            _barControls = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);

            _barInputIcon = new PathIcon { Data = Icons.Speaker, Width = 22, Height = 22 };
            _barInputLabel = new TextBlock { Text = "Input", Classes = { "small" }, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
            var input = BarButton(_barInputIcon, _barInputLabel, "Choose the device's input");
            input.Click += async (_, _) => await ShowInputMenuAsync(ip, input);
            var queue = BarButton(new PathIcon { Data = Icons.Queue, Width = 22, Height = 22 }, new TextBlock { Text = "Queue", Classes = { "small" } }, "Show the queue");
            queue.Click += (_, _) => { if (_queuePage != null) ShowPage(_queuePage); };
            var volume = BarButton(_barVolumeIcon, _barVolumeValue, "Volume");
            volume.Flyout = _barVolumeFlyout;

            var left = new DockPanel { Children = { art, text } };
            DockPanel.SetDock(art, Dock.Left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { input, queue, volume } };
            var right = new DockPanel { Children = { buttons, positionRow } };
            DockPanel.SetDock(buttons, Dock.Right);
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            bar.Children.Add(left);
            Grid.SetColumn(transport, 1);
            bar.Children.Add(transport);
            Grid.SetColumn(right, 2);
            bar.Children.Add(right);
            transport.IsEnabled = positionRow.IsEnabled = input.IsEnabled = volume.IsEnabled = !locked;
            return bar;
        }

        private static Button BarButton(PathIcon icon, TextBlock label, string tip)
        {
            if (icon.Parent is Panel iconParent) iconParent.Children.Remove(icon);
            if (label.Parent is Panel labelParent) labelParent.Children.Remove(label);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 4, 0, 0);
            var button = new Button
            {
                Content = new StackPanel { Children = { icon, label } }, Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Classes.Add("icon");
            button.Width = BarButtonWidth;
            button.Height = double.NaN;
            button.CornerRadius = new CornerRadius(10);
            button.Padding = new Thickness(4, 6);
            label.MaxWidth = BarButtonWidth - 8;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(button, tip);
            return button;
        }

        private void ApplyPlayerBarNowPlaying(string ip, string inputText)
        {
            if (ip != _barIp || _barControls == null) return;
            ApplyNowPlayingTo(ip, _barControls);
            if (_barInputLabel != null) _barInputLabel.Text = inputText;
            if (_barInputIcon != null && _statuses.TryGetValue(ip, out var status)) _barInputIcon.Data = Icons.ForInput(InputOfMode(status.Mode)) is var icon && icon != Icons.Music ? icon : Icons.Speaker;
            ShowBarRemaining();
        }

        private void ShowBarRemaining()
        {
            if (_barControls is not { } c || !c.PositionRow.IsVisible) return;
            c.Total.Text = "-" + FormatTime(Math.Max(0, c.Position.Maximum - c.Position.Value));
        }

        private GroupVolume? BarGroup =>
            _barIp != null && _groupVolumes.TryGetValue(TargetOf(_barIp), out var group) && group.Members.Count > 1 ? group : null;

        private void ApplyPlayerBarVolume(string ip)
        {
            if (BarGroup is { } group)
            {
                if (ip != _barIp && !group.Members.Contains(ip)) return;
                if (!_barVolumeSlider.IsPointerOver)
                {
                    _volumeUpdating = true;
                    _barVolumeSlider.Value = group.Controls.Slider.Value;
                    _volumeUpdating = false;
                }
                _barVolumeValue.Text = _barFlyoutValue.Text = ((int)_barVolumeSlider.Value).ToString();
                _barVolumeIcon.Data = _barMuteIcon.Data = group.Controls.MuteIcon.Data;
                return;
            }
            if (ip != _barIp) return;
            if (_volumes.TryGetValue(ip, out var v))
            {
                bool busy = _pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip) || _barVolumeSlider.IsPointerOver;
                if (!busy)
                {
                    _volumeUpdating = true;
                    _barVolumeSlider.Value = v.Volume;
                    _volumeUpdating = false;
                }
                _barVolumeValue.Text = _barFlyoutValue.Text = ((int)_barVolumeSlider.Value).ToString();
                _barVolumeIcon.Data = _barMuteIcon.Data = v.Muted ? Icons.Mute : Icons.Volume;
            }
            else _barVolumeValue.Text = "–";
        }
    }
}
