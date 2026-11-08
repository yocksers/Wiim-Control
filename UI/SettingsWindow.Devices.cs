using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private Control BuildDevicesPage()
        {
            var btnDiscover = Pill("Discover");
            var btnAdd = Pill("Add Device");
            btnDiscover.Click += async (_, _) =>
            {
                btnDiscover.IsEnabled = false;
                btnDiscover.Content = "Searching…";
                int added = await _owner.DiscoverAndMergeDevicesAsync();
                btnDiscover.Content = "Discover";
                btnDiscover.IsEnabled = true;
                await Dialogs.MessageAsync(this, added > 0 ? $"Found {added} new Wiim device(s)" : "No new Wiim devices found", _scale);
                LoadFromOwner();
            };
            btnAdd.Click += async (_, _) =>
            {
                btnAdd.IsEnabled = false;
                try
                {
                    if (!await _owner.AddDeviceAsync(this)) return;
                }
                finally { btnAdd.IsEnabled = true; }
                LoadFromOwner();
                _ = _eq.RefreshEqAsync();
                _ = RefreshMultiroomAsync();
            };
            return Page("Devices", _lblDeviceCount, [LayoutButton(), GroupsButton(_devicesGroupMenuLabel, () => GroupMenuText(_owner._deviceIp)), btnDiscover, btnAdd], BuildDevicesBody(), scroll: false);
        }

        private readonly TextBlock _devicesGroupMenuLabel = new() { MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };

        private string? GroupNameOf(string ip)
        {
            if (_multiroom == null) return null;
            string leader = TargetOf(ip);
            if (ActiveSavedGroup(_multiroom, leader) is { } group) return group.Name;
            return _multiroom.MemberCount(leader) + _multiroom.OthersOf(leader).Count > 0 ? "Unsaved group" : null;
        }

        private string GroupMenuText(string? ip)
        {
            if (string.IsNullOrEmpty(ip) || _multiroom == null) return "Group";
            return GroupNameOf(ip) ?? "No group";
        }

        private void UpdateDevicesGroupMenuLabel()
        {
            if (_devicesGroupMenuLabel.Text != "Switching…") _devicesGroupMenuLabel.Text = GroupMenuText(_owner._deviceIp);
        }

        private Button GroupsButton(TextBlock label, Func<string> text)
        {
            label.Text = text();
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { label, new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12 } }
                }
            };
            button.Classes.Add("pill");
            ToolTip.SetTip(button, "Switch on groups");
            button.Click += async (_, _) => await ShowGroupsMenuAsync(button, label, text);
            return button;
        }

        private async Task ShowGroupsMenuAsync(Button button, TextBlock label, Func<string> text)
        {
            var choices = await LoadGroupChoicesAsync(button, label, text);
            if (choices.Count == 0) choices.Add(NoSavedGroupsChoice());
            ShowDropdown(button, choices);
        }

        private static DropdownChoice NoSavedGroupsChoice() => new("There are no saved groups yet", false, false, () => Task.CompletedTask);

        private async Task<List<DropdownChoice>> LoadGroupChoicesAsync(Button button, TextBlock label, Func<string> text)
        {
            button.IsEnabled = false;
            var savedGroups = _owner.GetSavedGroupsAsync();
            var snapshot = await _owner.GetMultiroomSnapshotAsync();
            _savedGroups = await savedGroups;
            _multiroom = snapshot;
            button.IsEnabled = true;
            label.Text = text();

            var active = _savedGroups.Where(saved => ActiveSavedGroup(snapshot, saved.LeaderIp)?.Id == saved.Id).ToList();
            return _savedGroups.Select(group => new DropdownChoice(group.Name, active.Contains(group), snapshot.Online.Contains(group.LeaderIp), async () =>
            {
                bool on = active.Contains(group);
                button.IsEnabled = false;
                label.Text = "Switching…";
                try { await ChangeGroupAsync(() => on ? _owner.SwitchOffSavedGroupAsync(group) : _owner.SwitchOnSavedGroupAsync(group)); }
                finally
                {
                    label.Text = text();
                    button.IsEnabled = true;
                }
            })).ToList();
        }

        internal void OnFirmwareChecked() => Dispatcher.UIThread.Post(() =>
        {
            RebuildDeviceCards();
            _ = RefreshDeviceStatusesAsync();
        });

        private async Task RenameDeviceAsync(KnownDevice device)
        {
            string? name = await Dialogs.PromptAsync(this,
                $"New name for {device.Name}. The device keeps it, so the WiiM Home app, AirPlay and other apps show it too:", device.Name, _scale);
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name == device.Name) return;
            if (name.Length > MaxDeviceNameLength)
            {
                await Dialogs.MessageAsync(this, $"Please use at most {MaxDeviceNameLength} characters.", _scale);
                return;
            }
            if (!await _owner.RenameDeviceAsync(device.Ip, name))
            {
                await Dialogs.MessageAsync(this, $"{device.Name} didn't accept the new name. Check that it's switched on and reachable.", _scale);
                return;
            }
            LoadFromOwner();
            _ = RefreshDeviceStatusesAsync();
            _ = RefreshMultiroomAsync();
        }

        private void MoveDeviceCard(KnownDevice device, int offset)
        {
            if (!_owner.MoveDevice(device, offset)) return;
            LoadFromOwner();
            _ = RefreshDeviceStatusesAsync();
        }

        private sealed record CachedCard(string Key, Panel Slot, Control? Members);

        private readonly List<(string Ip, CachedCard Card)> _cachedCards = [];
        private Grid? _compactGrid;

        private string CardKey(KnownDevice d, int position, int count)
        {
            var members = GroupMembers(d.Ip);
            var memberText = string.Join(",", members.Select(ip => $"{ip}:{MemberName(ip)}:{_owner.IsDeviceLocked(ip)}"));
            return $"{_owner.CompactDevices}|{d.Name}|{_owner.ModelOf(d.Ip)}|{d.Ip == _owner._deviceIp}|{members.Contains(_owner._deviceIp)}|" +
                $"{_owner.FirmwareUpdateFor(d.Ip)?.Summary}|{GroupDescription(d.Ip)?.Text}|{position == 0}|{position == count - 1}|{count > 1}|{memberText}";
        }

        private void RebuildDeviceCards()
        {
            UpdateDevicesGroupMenuLabel();
            UpdatePlayerBar();
            _positions.Clear();
            var devices = _owner._knownDevices.ToList();
            var cards = new List<(string Ip, CachedCard Card)>();
            for (int position = 0; position < devices.Count; position++)
            {
                var d = devices[position];
                if (IsFollower(d.Ip)) continue;
                string key = CardKey(d, position, devices.Count);
                var card = _cachedCards.FirstOrDefault(c => c.Ip == d.Ip).Card;
                if (card?.Key != key)
                {
                    var members = GroupMembers(d.Ip);
                    var slot = CardSlot(d.Ip, _owner.CompactDevices ? CompactDeviceCard(d) : DeviceCard(d, position, devices.Count));
                    card = new CachedCard(key, slot, !_owner.CompactDevices && members.Count > 1 ? BuildGroupMembersCard(members) : null);
                    if (card.Members != null && slot.Children.FirstOrDefault() is Border deviceCard)
                    {
                        deviceCard.Classes.Add("joinedTop");
                        card.Members.Classes.Add("joinedBottom");
                    }
                }
                cards.Add((d.Ip, card));
            }
            foreach (var (_, old) in _cachedCards)
                if (!cards.Any(c => c.Card == old)) ForgetCard(old);
            _cachedCards.Clear();
            _cachedCards.AddRange(cards);

            var rows = new List<Control>();
            if (_owner.CompactDevices && cards.Count > 0) rows.Add(CompactDeviceGrid([.. cards.Select(c => c.Card.Slot)]));
            else
            {
                _compactGrid = null;
                foreach (var (_, card) in cards)
                {
                    rows.Add(card.Slot);
                    if (card.Members != null) rows.Add(card.Members);
                }
            }
            if (rows.Count == 0) rows.Add(Muted("No devices yet. Use Discover or Add Device."));
            if (!_deviceList.Children.SequenceEqual(rows)) SetRows(_deviceList, rows);

            int n = _owner._knownDevices.Count;
            _lblDeviceCount.Text = $"{n} device{(n == 1 ? "" : "s")}";
            foreach (var ip in _volumeControls.Keys) ApplyVolume(ip);
            foreach (var ip in _nowPlayingControls.Keys) ApplyNowPlaying(ip);
            foreach (var ip in _channelButtons.Keys) ApplyChannel(ip);
        }

        private void ForgetCard(CachedCard card)
        {
            bool Inside(Control control) => control.GetSelfAndLogicalAncestors().Any(a => a == card.Slot || a == card.Members);
            Forget(_volumeControls, v => Inside(v.Slider));
            Forget(_groupVolumes, g => Inside(g.Controls.Slider));
            Forget(_nowPlayingControls, c => Inside(c.Art));
            Forget(_castButtons, c => Inside(c.Button));
            Forget(_inputLabels, Inside);
            Forget(_channelButtons, Inside);
            Forget(_deviceCards, c => Inside(c.Slot));
        }

        private static void Forget<T>(Dictionary<string, T> entries, Func<T, bool> inside)
        {
            foreach (var key in entries.Where(e => inside(e.Value)).Select(e => e.Key).ToList()) entries.Remove(key);
        }

        private Border DeviceCard(KnownDevice d, int position, int count)
        {
            var members = GroupMembers(d.Ip);
            bool active = d.Ip == _owner._deviceIp;
            bool locked = _owner.IsDeviceLocked(d.Ip);

            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            title.Children.Add(new TextBlock { Text = d.Name, Classes = { "device" } });
            var (rename, renameIcon) = IconButton(Icons.Edit);
            renameIcon.Width = renameIcon.Height = 14;
            rename.Width = rename.Height = 24;
            rename.Padding = new Thickness(0);
            rename.MinHeight = 0;
            rename.Margin = new Thickness(-6, 0, -4, 0);
            rename.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(rename, "Rename this device");
            rename.Click += async (_, _) => await RenameDeviceAsync(d);
            rename.IsEnabled = !locked;
            title.Children.Add(rename);
            if (active)
                title.Children.Add(new TextBlock { Text = "●  Active", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center });
            if (_owner.FirmwareUpdateFor(d.Ip) is { } update)
            {
                var badge = new TextBlock { Text = "⬆  Update available", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(badge, $"{update.Summary}. {FirmwareUpdateAdvice}");
                title.Children.Add(badge);
            }
            var info = new StackPanel();
            info.Children.Add(title);
            info.Children.Add(new TextBlock
            {
                Text = _owner.ModelOf(d.Ip) is { Length: > 0 } model ? $"{model}  ·  {d.Ip}" : d.Ip, Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 0)
            });
            if (GroupDescription(d.Ip) is { } group)
                info.Children.Add(new TextBlock { Text = group.Text, Foreground = group.Brush, Margin = new Thickness(0, 4, 0, 0) });

            var actions = new WrapPanel
            {
                ItemSpacing = 8, LineSpacing = 8, ItemsAlignment = WrapPanelItemsAlignment.End, VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0)
            };
            var input = InputButton(d.Ip);
            input.IsEnabled = !locked;
            actions.Children.Add(input);
            var (remove, _) = IconPill(Icons.Delete, "Remove this device from the list");
            remove.IsEnabled = !locked;
            remove.Click += async (_, _) =>
            {
                if (!await Dialogs.ConfirmAsync(this,
                        $"Remove {d.Name} from Wiim Control? The device itself isn't changed, and you can add it again with Discover or Add Device.",
                        _scale)) return;
                _owner.RemoveDevice(d);
                ReloadSoon(refreshEq: active);
            };
            var lockButton = LockButton(d.Ip, 18);
            var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            if (count > 1)
            {
                var (up, _) = IconButton(Icons.ArrowUp);
                var (down, _) = IconButton(Icons.ArrowDown);
                ToolTip.SetTip(up, "Move up");
                ToolTip.SetTip(down, "Move down");
                up.IsEnabled = position > 0;
                down.IsEnabled = position < count - 1;
                up.Click += (_, _) => MoveDeviceCard(d, -1);
                down.Click += (_, _) => MoveDeviceCard(d, 1);
                orderButtons.Children.Add(up);
                orderButtons.Children.Add(down);
            }
            orderButtons.Children.Add(lockButton);
            actions.Children.Insert(0, orderButtons);

            var (mini, _) = IconPill(Icons.MiniPlayer, "Open or close the mini player for this device");
            mini.Click += (_, _) => _owner.ToggleMiniPlayer(d.Ip);
            var playerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mini } };
            if (PcAudioSupported)
            {
                var cast = CastButton(d.Ip);
                cast.IsEnabled = !locked;
                playerButtons.Children.Add(cast);
            }
            actions.Children.Add(playerButtons);
            var (link, linkIcon) = IconPill(Icons.Link, null);
            HookLinkButton(link, linkIcon, d.Ip);
            link.IsEnabled = !locked;
            var linkShift = new TranslateTransform();
            link.RenderTransform = linkShift;
            remove.SizeChanged += (_, e) => linkShift.Y = e.NewSize.Height + 8;
            actions.Children.Add(new Panel { Children = { remove, link } });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            grid.Children.Add(info);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            var nowPlayingRow = BuildNowPlayingRow(d.Ip);
            if (locked && _nowPlayingControls.TryGetValue(d.Ip, out var playback))
            {
                if (playback.PlayPause.Parent is Control transport) transport.IsEnabled = false;
                playback.PositionRow.IsEnabled = false;
            }
            Grid.SetRow(nowPlayingRow, 1);
            Grid.SetColumnSpan(nowPlayingRow, 2);
            grid.Children.Add(nowPlayingRow);
            var volumeRow = members.Count > 1 ? BuildGroupVolumeRow(d.Ip, members) : BuildVolumeRow(d.Ip);
            if (members.Count == 1) volumeRow.IsEnabled = !locked;
            Grid.SetRow(volumeRow, 2);
            Grid.SetColumnSpan(volumeRow, 2);
            grid.Children.Add(volumeRow);

            var card = new Border { Child = grid };
            card.Classes.Add("card");
            if (active) card.Classes.Add("active");
            MakeCardSelectable(card, d, active, locked);
            return card;
        }

        private const double InputButtonWidth = 140;

        private Button InputButton(string ip)
        {
            var label = new TextBlock { Text = "Input", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chevron = new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(chevron, Dock.Right);
            var button = new Button
            {
                Width = InputButtonWidth, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new DockPanel { Children = { chevron, label } }
            };
            button.Classes.Add("pill");
            ToolTip.SetTip(button, "Choose the device's input");
            button.Click += async (_, _) => await ShowInputMenuAsync(ip, button);
            _inputLabels[ip] = label;
            return button;
        }

        private async Task ShowInputMenuAsync(string ip, Button button)
        {
            button.IsEnabled = false;
            var inputs = await _owner.GetInputsAsync(ip);
            button.IsEnabled = true;
            if (inputs == null)
            {
                await Dialogs.MessageAsync(this, "Couldn't get the inputs from the device. Check that it's switched on and reachable.", _scale);
                return;
            }
            string? current = _statuses.TryGetValue(ip, out var status) ? InputOfMode(status.Mode) : null;
            ShowDropdown(button, inputs.Select(input => new DropdownChoice(_owner.InputName(ip, input), IsSameInput(input, current), true,
                () => SwitchInputAsync(ip, input))).ToList());
        }

        private async Task SetPcAudioAsync(string ip, bool on)
        {
            if (!await _owner.SetPcAudioAsync(ip, on) && on)
            {
                await Dialogs.MessageAsync(this, "The device didn't start playing this computer's sound. Check that it's switched on and reachable.", _scale);
                return;
            }
            await Task.Delay(1500);
            await RefreshDeviceStatusesAsync();
        }

        internal void OnPcAudioChanged()
        {
            foreach (var ip in _castButtons.Keys.ToList()) ApplyCastButton(ip);
            foreach (var ip in _inputLabels.Keys.ToList()) ApplyNowPlaying(ip);
        }

        private readonly Dictionary<string, (Button Button, PathIcon Icon)> _castButtons = new();

        private static (Button Button, PathIcon Icon) IconPill(StreamGeometry geometry, string? tip)
        {
            var icon = new PathIcon { Data = geometry, Width = 18, Height = 18 };
            var button = new Button
            {
                Content = new Grid { Children = { new TextBlock { Text = " ", Opacity = 0 }, icon } },
                Padding = new Thickness(14, 8), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("pill");
            if (tip != null) ToolTip.SetTip(button, tip);
            return (button, icon);
        }

        private Button CastButton(string ip)
        {
            var (cast, castIcon) = IconPill(Icons.ComputerSound, null);
            cast.Click += async (_, _) =>
            {
                cast.IsEnabled = false;
                try { await SetPcAudioAsync(ip, !_owner.IsPlayingPcAudio(ip)); }
                finally { cast.IsEnabled = true; }
            };
            _castButtons[ip] = (cast, castIcon);
            ApplyCastButton(ip);
            return cast;
        }

        private void ApplyCastButton(string ip)
        {
            if (!_castButtons.TryGetValue(ip, out var c)) return;
            bool on = _owner.IsPlayingPcAudio(ip);
            c.Button.Classes.Set("accent", on);
            ToolTip.SetTip(c.Button, on
                ? "Stop playing this computer's sound on the device"
                : "Stream audio from this computer over network to the device");
        }

        private async Task SwitchInputAsync(string ip, string input)
        {
            _owner.OnInputChosen(ip);
            if (!await _owner.SwitchInputAsync(ip, input))
            {
                await Dialogs.MessageAsync(this, "The device didn't switch the input. Check that it's switched on and reachable.", _scale);
                return;
            }
            if (_inputLabels.TryGetValue(ip, out var label)) label.Text = _owner.InputName(ip, input);
            await Task.Delay(1500);
            await RefreshDeviceStatusesAsync();
            if (ip == _eq.EqIp && _eq.EqInputChoice == null) await _eq.RefreshEqAsync();
        }

        private const double CardArtSize = 92;
        private const double CardPlaceholderSize = 38;

        private static readonly string[] UncontrolledInputs = ["line-in", "line-in2", "optical", "co-axial", "HDMI", "phono", "PCUSB"];

        private bool CanOpenNowPlayingView(string ip, ArtworkView art)
        {
            bool known = _statuses.TryGetValue(TargetOf(ip), out var status);
            if (known && UncontrolledInputs.Contains(InputOfMode(status!.Mode))) return false;
            return art.Bitmap != null || known && (status!.Title.Length > 0 || status.IsPlaying);
        }

        private void HookCoverOpen(ArtworkView art, string ip, bool zoomTip = false)
        {
            art.Tapped += (_, e) =>
            {
                if (!CanOpenNowPlayingView(ip, art)) return;
                e.Handled = true;
                ShowNowPlayingView(ip);
            };
            void Apply()
            {
                bool open = CanOpenNowPlayingView(ip, art);
                art.Cursor = open ? new Cursor(StandardCursorType.Hand) : null;
                ToolTip.SetTip(art, !open ? null : zoomTip && art.Bitmap != null ? "Show the artwork full size" : "Open the big player");
            }
            art.BitmapChanged += Apply;
            art.PointerEntered += (_, _) => Apply();
        }

        private Button LockButton(string ip, double iconSize)
        {
            bool locked = _owner.IsDeviceLocked(ip);
            var (button, icon) = IconButton(locked ? Icons.Lock : Icons.LockOpen);
            icon.Width = icon.Height = iconSize;
            if (locked) icon.Foreground = Palette.AccentText;
            ToolTip.SetTip(button, locked
                ? "Locked. Volume, playback and other changes to this device are blocked here. Click to unlock."
                : "Lock this device so its volume, playback and other settings can't be changed here by accident");
            button.Click += (_, _) =>
            {
                _owner.SetDeviceLocked(ip, !locked);
                RebuildDeviceCards();
            };
            return button;
        }

        private ScaledContent ScaledPopup(Control content) => new() { Scale = _scale, Child = content };

        private void FillInputPills(WrapPanel panel, IEnumerable<string> inputs, string? selected, string? playing, Func<string, string> nameOf,
            Func<string, string> tipFor, Func<string, Task> choose)
        {
            panel.Children.Clear();
            foreach (var input in inputs)
            {
                string name = nameOf(input);
                var pill = Pill(input == playing ? $"•  {name}" : name, accent: input == selected);
                ToolTip.SetTip(pill, input == playing ? $"{name} is playing now" : tipFor(name));
                pill.Click += async (_, _) => { if (input != selected) await choose(input); };
                panel.Children.Add(pill);
            }
        }

        private Grid BuildNowPlayingRow(string ip)
        {
            var art = new ArtworkView { VerticalAlignment = VerticalAlignment.Center };
            HookCoverOpen(art, ip, zoomTip: true);
            var title = new TextBlock { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var detail = new TextBlock { Classes = { "dim", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var playing = new PlayingIndicator { Foreground = Palette.AccentText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var detailRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Children = { playing, detail } };
            DockPanel.SetDock(playing, Dock.Left);
            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, artist, detailRow, positionRow } };
            art.Width = art.Height = CardArtSize;
            art.PlaceholderSize = CardPlaceholderSize;

            var (shuffle, shuffleIcon) = IconButton(Icons.Shuffle);
            var (repeat, repeatIcon) = IconButton(Icons.Repeat);
            shuffle.IsVisible = repeat.IsVisible = false;
            shuffle.Click += async (_, _) => await ToggleShuffleAsync(ip);
            repeat.Click += async (_, _) => await CycleRepeatAsync(ip);
            var (previous, _) = IconButton(Icons.Previous);
            var (playPause, playPauseIcon) = IconButton(Icons.Play);
            var (next, _) = IconButton(Icons.Next);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { shuffle, previous, playPause, next, repeat }
            };
            _nowPlayingControls[ip] = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(art);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            return row;
        }

        private void ApplyFollowers()
        {
            foreach (var ip in _nowPlayingControls.Keys.Where(ip => TargetOf(ip) != ip).ToList()) ApplyNowPlaying(ip);
        }

        private void ApplyNowPlaying(string ip)
        {
            string inputText = !_statuses.TryGetValue(ip, out var known) ? "Input"
                : _owner.IsPlayingPcAudio(ip) && IsNetworkMode(known.Mode) ? "This computer"
                : InputOfMode(known.Mode) is { } input ? _owner.InputName(ip, input)
                : SourceName(known.Mode) is { Length: > 0 } name ? name
                : "Input";
            if (_inputLabels.TryGetValue(ip, out var inputLabel)) inputLabel.Text = inputText;
            if (_nowPlayingControls.TryGetValue(ip, out var card)) ApplyNowPlayingTo(ip, card);
            if (_npIp == ip && _npControls != null) ApplyNowPlayingTo(ip, _npControls);
            ApplyPlayerBarNowPlaying(ip, inputText);
        }

        private void ApplyNowPlayingTo(string ip, NowPlayingControls c)
        {
            if (!_statuses.TryGetValue(ip, out var status))
            {
                c.Title.Text = _volumes.Count == 0 && _statuses.Count == 0 ? "Loading…" : "Not reachable";
                c.Artist.Text = c.Detail.Text = string.Empty;
                c.Playing.IsVisible = false;
                c.PositionRow.IsVisible = c.Shuffle.IsVisible = c.Repeat.IsVisible = false;
                _positions.Remove(ip);
                c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = false;
                c.Art.ImageUrl = string.Empty;
                c.Art.Bitmap = null;
                c.Art.PlaceholderIcon = Icons.Music;
                return;
            }

            string leaderIp = TargetOf(ip);
            var leaderStatus = leaderIp != ip ? _statuses.GetValueOrDefault(leaderIp) : null;
            bool following = leaderStatus != null;
            if (leaderStatus != null) status = leaderStatus;
            string songBefore = $"{c.Title.Text}\n{c.Artist.Text}";
            (c.Title.Text, c.Artist.Text, c.Detail.Text) = _owner.NowPlayingTexts(status, leaderIp, following);
            bool sameSong = songBefore == $"{c.Title.Text}\n{c.Artist.Text}";
            c.Art.PlaceholderIcon = Icons.ForInput(InputOfMode(status.Mode));
            c.PlayPauseIcon.Data = status.IsPlaying ? Icons.Pause : Icons.Play;
            c.Playing.IsVisible = status.IsPlaying;
            c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = true;
            ApplyPlayback(ip, c, status);
            if (ReferenceEquals(c, _npControls))
            {
                UpdateLyricsTrack(status);
                UpdateGatefoldTrack(status);
            }
            bool deviceCopy = sameSong && c.Art.ImageUrl.Length > 0 && !IsOnDevice(c.Art.ImageUrl, leaderIp) && IsOnDevice(status.ArtUrl, leaderIp);
            if (status.ArtUrl != c.Art.ImageUrl && !deviceCopy)
            {
                c.Art.ImageUrl = status.ArtUrl;
                c.Art.Bitmap = null;
                if (status.ArtUrl.Length > 0) _ = LoadArtworkAsync(c.Art, status.ArtUrl);
            }
        }

        private static bool IsOnDevice(string url, string ip) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals(ip, StringComparison.OrdinalIgnoreCase);

        internal bool IsShowingArtwork(Avalonia.Media.Imaging.Bitmap bitmap) =>
            _nowPlayingControls.Values.Any(c => ReferenceEquals(c.Art.Bitmap, bitmap)) ||
            (_npControls != null && ReferenceEquals(_npControls.Art.Bitmap, bitmap)) ||
            ReferenceEquals(_npCover.Source, bitmap) || ReferenceEquals(_npCoverOld.Source, bitmap) || ReferenceEquals(_npPendingArt, bitmap) ||
            IsBackdrop(bitmap) ||
            _presets.ShowsImage(bitmap);

        private async Task LoadArtworkAsync(ArtworkView view, string url)
        {
            var image = await _owner.GetArtworkAsync(url);
            if (view.ImageUrl == url) view.Bitmap = image;
        }

        private async Task TransportAsync(string ip, string action)
        {
            await _owner.SendTransportAsync(TargetOf(ip), action);
            await Task.Delay(600);
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }
    }
}
