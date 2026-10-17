using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly Dictionary<string, TextBlock> _sleepLabels = new();
        private readonly Dictionary<string, (int PositionMs, DateTime Fetched, bool Playing)> _positions = new();
        private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        private string TargetOf(string ip) =>
            _multiroom != null && _multiroom.LeaderOf.TryGetValue(ip, out var leader) ? leader : ip;

        private static string FormatTime(int seconds) =>
            seconds >= 3600
                ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

        private static bool IsNetworkSource(DeviceStatus status) => IsNetworkMode(status.Mode);

        private Grid BuildPositionRow(string ip, out LevelSlider slider, out TextBlock elapsed, out TextBlock total)
        {
            var position = new LevelSlider { Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
            var elapsedText = new TextBlock { Classes = { "dim", "small" }, MinWidth = 34, VerticalAlignment = VerticalAlignment.Center };
            var totalText = new TextBlock
            {
                Classes = { "dim", "small" }, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            position.ValueChanged += (_, _) => { if (position.IsDragging) elapsedText.Text = FormatTime(position.Value); };
            position.ValueCommitted += async (_, _) => await SeekAsync(ip, position.Value);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
            row.Children.Add(elapsedText);
            Grid.SetColumn(position, 1);
            row.Children.Add(position);
            Grid.SetColumn(totalText, 2);
            row.Children.Add(totalText);
            slider = position;
            elapsed = elapsedText;
            total = totalText;
            return row;
        }

        private async Task SeekAsync(string ip, int seconds)
        {
            if (_positions.TryGetValue(ip, out var p)) _positions[ip] = (seconds * 1000, DateTime.UtcNow, p.Playing);
            await _owner.SeekAsync(TargetOf(ip), seconds);
            await Task.Delay(800);
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }

        private async Task ToggleShuffleAsync(string ip)
        {
            if (!_statuses.TryGetValue(TargetOf(ip), out var status)) return;
            var (shuffle, repeat) = ParseLoop(status.Loop);
            await ChangeLoopAsync(ip, LoopMode(!shuffle, repeat));
        }

        private async Task CycleRepeatAsync(string ip)
        {
            if (!_statuses.TryGetValue(TargetOf(ip), out var status)) return;
            var (shuffle, repeat) = ParseLoop(status.Loop);
            await ChangeLoopAsync(ip, LoopMode(shuffle, repeat switch { 0 => 2, 2 => 1, _ => 0 }));
        }

        private async Task ChangeLoopAsync(string ip, int mode)
        {
            string target = TargetOf(ip);
            if (_statuses.TryGetValue(target, out var status))
            {
                _statuses[target] = status with { Loop = mode };
                ApplyNowPlaying(target);
                ApplyFollowers();
            }
            await _owner.SetLoopModeAsync(target, mode);
            await Task.Delay(600);
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }

        private void ApplyPlayback(string ip, NowPlayingControls c, DeviceStatus status)
        {
            bool network = IsNetworkSource(status);
            var (shuffle, repeat) = ParseLoop(status.Loop);
            c.Shuffle.IsVisible = c.Repeat.IsVisible = network;
            SetActive(c.ShuffleIcon, shuffle);
            SetActive(c.RepeatIcon, repeat > 0);
            c.RepeatIcon.Data = repeat == 1 ? Icons.RepeatOnce : Icons.Repeat;
            ToolTip.SetTip(c.Shuffle, shuffle ? "Shuffle is on" : "Shuffle is off");
            ToolTip.SetTip(c.Repeat, repeat switch { 1 => "Repeat one", 2 => "Repeat all", _ => "Repeat is off" });

            bool seekable = network && status.DurationMs > 0;
            c.PositionRow.IsVisible = seekable;
            if (seekable)
            {
                _positions[ip] = (status.PositionMs, DateTime.UtcNow, status.IsPlaying);
                c.Position.Maximum = Math.Max(1, status.DurationMs / 1000);
                c.Total.Text = FormatTime(status.DurationMs / 1000);
                ShowPosition(c, status.PositionMs);
            }
            else _positions.Remove(ip);

            if (_sleepLabels.TryGetValue(ip, out var sleep))
                sleep.Text = status.SleepSeconds > 0 ? $"Sleep · {(status.SleepSeconds + 59) / 60} min" : "Sleep";
        }

        private static void SetActive(PathIcon icon, bool active)
        {
            if (active) icon.Foreground = Palette.AccentText;
            else icon.ClearValue(PathIcon.ForegroundProperty);
        }

        private static void ShowPosition(NowPlayingControls c, int positionMs)
        {
            if (c.Position.IsDragging) return;
            int seconds = Math.Clamp(positionMs / 1000, 0, c.Position.Maximum);
            c.Position.Value = seconds;
            c.Elapsed.Text = FormatTime(seconds);
        }

        private void AdvancePositions()
        {
            foreach (var (ip, p) in _positions)
            {
                if (!p.Playing) continue;
                int positionMs = p.PositionMs + (int)(DateTime.UtcNow - p.Fetched).TotalMilliseconds;
                if (_nowPlayingControls.TryGetValue(ip, out var c)) ShowPosition(c, positionMs);
                if (_npIp == ip && _npControls != null) ShowPosition(_npControls, positionMs);
            }
        }

        private Button SleepButton(string ip)
        {
            var label = new TextBlock { Text = "Sleep", VerticalAlignment = VerticalAlignment.Center };
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { label, new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12 } }
                }
            };
            button.Classes.Add("pill");
            ToolTip.SetTip(button, "Sleep timer: the device stops playing when the time is up");
            button.Click += (_, _) => ShowSleepMenu(ip, button);
            _sleepLabels[ip] = label;
            return button;
        }

        private void ShowSleepMenu(string ip, Button button)
        {
            int current = _statuses.TryGetValue(ip, out var status) ? status.SleepSeconds : 0;
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            var off = new MenuItem { Header = "Off", ToggleType = MenuItemToggleType.Radio, IsChecked = current == 0 };
            off.Click += async (_, _) => await SetSleepAsync(ip, 0);
            menu.Items.Add(off);
            foreach (int minutes in SleepChoices)
            {
                var item = new MenuItem { Header = $"{minutes} minutes", ToggleType = MenuItemToggleType.Radio };
                int m = minutes;
                item.Click += async (_, _) => await SetSleepAsync(ip, m);
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        }

        private async Task SetSleepAsync(string ip, int minutes)
        {
            if (!await _owner.SetSleepTimerAsync(ip, minutes))
            {
                await Dialogs.MessageAsync(this, "The device didn't accept the sleep timer.", _scale);
                return;
            }
            await RefreshDeviceStatusesAsync();
        }
    }
}
