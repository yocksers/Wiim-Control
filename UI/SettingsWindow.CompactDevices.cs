using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double CompactCardWidth = 320;
        private const double CompactArtSize = 48;
        private const double CompactGap = 12;
        private const double CompactInputWidth = 112;
        private const double CompactLinkRoom = 30;
        private static readonly IBrush MemberVolumeShade = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0));
        private readonly HashSet<string> _openGroupVolumes = [];

        private readonly Dictionary<string, (Panel Slot, Control Card)> _deviceCards = new();
        private static readonly TimeSpan CardMoveTime = TimeSpan.FromMilliseconds(380);

        private Button LayoutButton()
        {
            var (button, icon) = IconPill(Icons.ViewGrid, null);
            static DeviceLayout NextOf(DeviceLayout layout) => layout switch
            {
                DeviceLayout.Full => DeviceLayout.Compact,
                DeviceLayout.Compact => DeviceLayout.Dashboard,
                _ => DeviceLayout.Full
            };
            void Apply()
            {
                var next = NextOf(_owner.DeviceLayout);
                icon.Data = next switch { DeviceLayout.Compact => Icons.ViewGrid, DeviceLayout.Dashboard => Icons.ViewDashboard, _ => Icons.ViewRows };
                ToolTip.SetTip(button, next switch
                {
                    DeviceLayout.Compact => "Show the devices as small cards side by side",
                    DeviceLayout.Dashboard => "Show a dashboard for the active device",
                    _ => "Show the devices as full cards"
                });
            }
            Apply();
            button.Click += (_, _) =>
            {
                var from = _owner.DeviceLayout;
                var next = NextOf(from);
                var before = _deviceCards.ToDictionary(c => c.Key, c => CardBounds(c.Value.Card));
                _owner.DeviceLayout = next;
                Apply();
                RebuildDeviceCards();
                if (from != DeviceLayout.Dashboard && next != DeviceLayout.Dashboard) AnimateCardsFrom(before);
                else
                {
                    UpdateLayout();
                    AnimateEntrance(next == DeviceLayout.Dashboard ? _dashboardParts
                        : _compactGrid != null ? [.. _compactGrid.Children] : [.. _deviceList.Children]);
                }
                _ = RefreshDeviceStatusesAsync();
            };
            return button;
        }

        private Grid CompactDeviceGrid(List<Panel> slots)
        {
            if (_compactGrid == null)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, -CompactGap, 0) };
                grid.SizeChanged += (_, e) => ArrangeCompactGrid(grid, Math.Clamp((int)(e.NewSize.Width / CompactCardWidth), 1, 4));
                _compactGrid = grid;
            }
            if (_compactGrid.Children.SequenceEqual(slots)) return _compactGrid;
            _compactGrid.Children.Clear();
            foreach (var slot in slots)
            {
                slot.VerticalAlignment = VerticalAlignment.Top;
                _compactGrid.Children.Add(slot);
            }
            _compactGrid.ColumnDefinitions.Clear();
            ArrangeCompactGrid(_compactGrid, _deviceList.Bounds.Width > 0 ? Math.Clamp((int)((_deviceList.Bounds.Width + CompactGap) / CompactCardWidth), 1, 4) : 2);
            return _compactGrid;
        }

        private static void ArrangeCompactGrid(Grid grid, int columns)
        {
            if (grid.ColumnDefinitions.Count == columns) return;
            grid.ColumnDefinitions.Clear();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.RowDefinitions.Clear();
            for (int i = 0; i < (grid.Children.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < grid.Children.Count; i++)
            {
                Grid.SetColumn(grid.Children[i], i % columns);
                Grid.SetRow(grid.Children[i], i / columns);
            }
        }

        private StackPanel MemberVolumes(List<string> members)
        {
            var list = new StackPanel { Margin = new Thickness(-16, 12, -16, -12) };
            foreach (var ip in members)
            {
                list.Children.Add(new Border
                {
                    Background = MemberVolumeShade, Padding = new Thickness(16, 10, 16, 10),
                    BorderBrush = Palette.InputHover, BorderThickness = new Thickness(0, list.Children.Count == 0 ? 0 : 1, 0, 0),
                    Child = new StackPanel { Children = { MemberLabel(ip), MemberVolumeRow(ip, channel: false) } }
                });
            }
            return list;
        }

        private Control CompactDeviceCard(KnownDevice d)
        {
            var members = GroupMembers(d.Ip);
            bool active = members.Contains(_owner._deviceIp);
            bool locked = _owner.IsDeviceLocked(d.Ip);

            var name = new TextBlock { Text = members.Count > 1 ? $"{d.Name} +{members.Count - 1}" : d.Name, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var input = InputButton(d.Ip);
            input.IsEnabled = !locked;
            input.Padding = new Thickness(10, 3);
            input.Width = CompactInputWidth;
            input.MinHeight = 0;
            var lockButton = LockButton(d.Ip, 16);
            var (link, linkIcon) = IconButton(Icons.Link);
            linkIcon.Width = linkIcon.Height = 16;
            HookLinkButton(link, linkIcon, d.Ip);
            link.IsEnabled = !locked;
            var linkShift = new TranslateTransform();
            link.RenderTransform = linkShift;
            lockButton.SizeChanged += (_, e) => linkShift.Y = e.NewSize.Height;
            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, -4, -6, -4),
                Children = { input, new Panel { Children = { lockButton, link } } }
            };
            DockPanel.SetDock(tools, Dock.Right);
            var header = new DockPanel { Children = { tools, name } };

            var art = new ArtworkView { Width = CompactArtSize, Height = CompactArtSize, PlaceholderSize = 22, VerticalAlignment = VerticalAlignment.Center };
            HookCoverOpen(art, d.Ip);
            var title = new TextBlock
            {
                FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, CompactLinkRoom, 0)
            };
            var artist = new TextBlock { Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            var detail = new TextBlock();
            var playing = new PlayingIndicator { Foreground = Palette.AccentText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(playing, Dock.Left);
            detail.Classes.Add("dim");
            detail.Classes.Add("small");
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.VerticalAlignment = VerticalAlignment.Center;
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                Children = { title, artist, new DockPanel { Margin = new Thickness(0, 3, 0, 0), Children = { playing, detail } } }
            };
            var positionRow = BuildPositionRow(d.Ip, out var position, out var elapsed, out var total);
            var (shuffle, shuffleIcon) = IconButton(Icons.Shuffle);
            var (repeat, repeatIcon) = IconButton(Icons.Repeat);
            var (previous, _) = IconButton(Icons.Previous);
            var (playPause, playPauseIcon) = IconButton(Icons.Play);
            var (next, _) = IconButton(Icons.Next);
            _nowPlayingControls[d.Ip] = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);
            var nowPlaying = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
            nowPlaying.Children.Add(art);
            Grid.SetColumn(text, 1);
            nowPlaying.Children.Add(text);

            Grid volumeRow;
            if (members.Count > 1) volumeRow = BuildGroupVolumeRow(d.Ip, members);
            else
            {
                var volume = CreateVolumeControls(d.Ip);
                _volumeControls[d.Ip] = volume;
                volumeRow = VolumeRow(volume);
                volumeRow.IsEnabled = !locked;
            }
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { volumeRow } };
            var content = new StackPanel { Children = { header, nowPlaying, bottom } };
            if (members.Count > 1)
            {
                bool open = _openGroupVolumes.Contains(d.Ip);
                var chevron = new PathIcon { Data = open ? Icons.ChevronUp : Icons.ChevronDown, Width = 14, Height = 14 };
                var each = new Button { Content = chevron, Padding = new Thickness(14, 4), MinHeight = 0, Margin = new Thickness(6, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                each.Classes.Add("pill");
                each.Classes.Add("flat");
                ToolTip.SetTip(each, "Show or hide the volume of each device in the group");
                var perDevice = MemberVolumes(members);
                perDevice.IsVisible = open;
                each.Click += (_, _) =>
                {
                    bool show = !perDevice.IsVisible;
                    perDevice.IsVisible = show;
                    chevron.Data = show ? Icons.ChevronUp : Icons.ChevronDown;
                    if (show) _openGroupVolumes.Add(d.Ip);
                    else _openGroupVolumes.Remove(d.Ip);
                };
                Grid.SetColumn(each, 1);
                bottom.Children.Add(each);
                content.Children.Add(perDevice);
            }

            var card = new Border
            {
                Child = content, Margin = new Thickness(0, 0, CompactGap, CompactGap), Padding = new Thickness(16, 12), ClipToBounds = true
            };
            card.Classes.Add("card");
            if (active) card.Classes.Add("active");
            MakeCardSelectable(card, d, active, locked);
            return card;
        }

        private void MakeCardSelectable(Border card, KnownDevice d, bool active, bool locked)
        {
            if (active || locked) return;
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.Tapped += (_, e) =>
            {
                if (e.Handled || e.Source is not Visual source ||
                    source.FindAncestorOfType<Button>(includeSelf: true) != null || source.FindAncestorOfType<LevelSlider>(includeSelf: true) != null ||
                    source.FindAncestorOfType<RangeBase>(includeSelf: true) != null) return;
                _owner.SwitchDevice(d);
            };
        }

        private Panel CardSlot(string ip, Control card)
        {
            var slot = new Panel { Children = { card } };
            _deviceCards[ip] = (slot, card);
            return slot;
        }

        private Rect CardBounds(Control card) =>
            card.TranslatePoint(default, _deviceList) is { } origin ? new Rect(origin, card.Bounds.Size) : default;

        private void AnimateCardsFrom(Dictionary<string, Rect> before)
        {
            if (!_deviceList.IsEffectivelyVisible || before.Count == 0) return;
            UpdateLayout();
            var moves = new List<(Panel Slot, Control Card, Rect From, Rect To, TranslateTransform Shift)>();
            foreach (var (ip, (slot, card)) in _deviceCards)
            {
                var to = CardBounds(card);
                if (to.Width <= 0) continue;
                var shift = new TranslateTransform();
                if (!before.TryGetValue(ip, out var from) || from.Width <= 0)
                {
                    card.Opacity = 0;
                    moves.Add((slot, card, to, to, shift));
                    continue;
                }
                slot.Width = slot.Bounds.Width;
                slot.Height = slot.Bounds.Height;
                card.HorizontalAlignment = HorizontalAlignment.Left;
                card.VerticalAlignment = VerticalAlignment.Top;
                card.ClipToBounds = true;
                card.RenderTransform = shift;
                moves.Add((slot, card, from, to, shift));
            }
            void Step(double progress)
            {
                double e = Ease(progress);
                foreach (var (_, card, from, to, shift) in moves)
                {
                    if (from == to)
                    {
                        card.Opacity = e;
                        continue;
                    }
                    card.Width = from.Width + (to.Width - from.Width) * e;
                    card.Height = from.Height + (to.Height - from.Height) * e;
                    shift.X = (from.X - to.X) * (1 - e);
                    shift.Y = (from.Y - to.Y) * (1 - e);
                }
            }
            Animate(_deviceList, CardMoveTime, Step, () =>
            {
                foreach (var (slot, card, _, _, _) in moves)
                {
                    foreach (var property in new AvaloniaProperty[] { WidthProperty, HeightProperty, HorizontalAlignmentProperty, VerticalAlignmentProperty, ClipToBoundsProperty })
                        card.ClearValue(property);
                    card.RenderTransform = null;
                    card.Opacity = 1;
                    slot.ClearValue(WidthProperty);
                    slot.ClearValue(HeightProperty);
                }
            });
        }
    }
}
