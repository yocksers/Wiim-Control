using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double LinkArtSize = 64;

        private void HookLinkButton(Button button, PathIcon icon, string ip)
        {
            if (GroupMembers(TargetOf(ip)).Count > 1) icon.Foreground = Palette.AccentText;
            ToolTip.SetTip(button, $"Link other devices to {DeviceName(ip)} so they play the same music");
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try { await ShowLinkDevicesAsync(ip); }
                finally { button.IsEnabled = true; }
            };
        }

        private async Task ShowLinkDevicesAsync(string chosen)
        {
            var snapshot = await _owner.GetMultiroomSnapshotAsync();
            _multiroom = snapshot;
            string leader = snapshot.LeaderOf.TryGetValue(chosen, out var current) ? current : chosen;

            var art = new ArtworkView { Width = LinkArtSize, Height = LinkArtSize, PlaceholderSize = 28, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };
            var artist = new TextBlock { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300, Margin = new Thickness(0, 4, 0, 0) };
            if (_statuses.TryGetValue(leader, out var status))
            {
                (title.Text, artist.Text, _) = _owner.NowPlayingTexts(status, leader, following: false);
                if (status.ArtUrl.Length > 0)
                {
                    art.ImageUrl = status.ArtUrl;
                    _ = LoadArtworkAsync(art, status.ArtUrl);
                }
            }
            else title.Text = DeviceName(leader);
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(art);
            var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Children = { title, artist } };
            Grid.SetColumn(headerText, 1);
            header.Children.Add(headerText);

            var rows = new StackPanel { Width = 380 };
            var choices = new List<(string Ip, CheckBox Box)>();
            foreach (var device in _owner._knownDevices)
            {
                bool isLeader = device.Ip == leader;
                bool online = snapshot.Online.Contains(device.Ip);
                var box = new CheckBox
                {
                    IsChecked = isLeader || snapshot.LeaderOf.TryGetValue(device.Ip, out var lead) && lead == leader,
                    IsEnabled = !isLeader && online && !_owner.IsDeviceLocked(device.Ip), VerticalAlignment = VerticalAlignment.Center
                };
                string note = isLeader ? "Plays the music" : !online ? "Not reachable" : _owner.IsDeviceLocked(device.Ip) ? "Locked" : string.Empty;
                var name = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                    Children = { new TextBlock { Text = device.Name, TextTrimming = TextTrimming.CharacterEllipsis } }
                };
                if (note.Length > 0) name.Children.Add(new TextBlock { Text = note, Classes = { "muted", "small" } });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 52 };
                row.Children.Add(new PathIcon { Data = Icons.Speaker, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                Grid.SetColumn(box, 2);
                row.Children.Add(box);
                rows.Children.Add(new Border
                {
                    Child = row, BorderBrush = Palette.Input, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 0)
                });
                if (!isLeader) choices.Add((device.Ip, box));
            }
            var content = new StackPanel { Children = { header, rows } };
            if (!await Dialogs.ChooseAsync(this, content, "Done", _scale)) return;

            var changes = choices
                .Where(c => c.Box.IsEnabled && (c.Box.IsChecked == true) != (snapshot.LeaderOf.TryGetValue(c.Ip, out var lead) && lead == leader))
                .Select(c => (c.Ip, Linked: c.Box.IsChecked == true))
                .ToList();
            if (changes.Count == 0) return;
            await ChangeGroupAsync(async () =>
            {
                bool ok = true;
                foreach (var (ip, linked) in changes) ok &= await SetLinkedAsync(snapshot, ip, leader, linked);
                return ok;
            });
            RebuildDeviceCards();
            _ = RefreshDeviceStatusesAsync();
        }
    }
}
