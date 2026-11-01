using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private Control BuildMultiroomPage()
        {
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshMultiroomAsync();
            return Page("Multiroom", _lblGroupStatus, [btnRefresh], _groupList);
        }

        private async Task RefreshMultiroomAsync()
        {
            _lblGroupStatus.Text = "Checking…";
            var homeTheater = RefreshHomeTheaterAsync();
            var savedGroups = _owner.GetSavedGroupsAsync();
            _multiroom = await _owner.GetMultiroomSnapshotAsync();
            _savedGroups = await savedGroups;
            UpdateDisplayNav();
            RebuildMultiroom();
            RebuildDeviceCards();
            await RefreshChannelsAsync();
            RebuildMultiroom();
            await homeTheater;
        }

        private string DeviceName(string ip) => _owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;

        private (string Text, IBrush Brush)? GroupDescription(string ip)
        {
            if (_multiroom == null) return null;
            if (!_multiroom.Online.Contains(ip)) return ("Not reachable", Palette.TextDim);
            string status = LinkStatus(_multiroom, ip);
            return status == "Not linked" ? null : (status, Palette.AccentText);
        }

        private string LinkStatus(MultiroomSnapshot snapshot, string ip)
        {
            if (!snapshot.Online.Contains(ip)) return "Not reachable";
            if (snapshot.LeaderOf.TryGetValue(ip, out var leader))
                return ActiveSavedGroup(snapshot, leader) is { } joined ? $"In {joined.Name}  ·  linked to {DeviceName(leader)}" : $"Linked to {DeviceName(leader)}";
            int members = snapshot.MemberCount(ip) + snapshot.OthersOf(ip).Count;
            if (members == 0) return "Not linked";
            return ActiveSavedGroup(snapshot, ip) is { } led ? $"{led.Name}  ·  Group leader  ·  {members} linked" : $"Group leader  ·  {members} linked";
        }

        private SavedGroup? ActiveSavedGroup(MultiroomSnapshot snapshot, string leaderIp) =>
            snapshot.MemberCount(leaderIp) + snapshot.OthersOf(leaderIp).Count == 0
                ? null
                : _savedGroups.FirstOrDefault(g => g.LeaderIp == leaderIp && g.Current);


        private void RebuildMultiroom()
        {
            var devices = _owner._knownDevices.ToList();
            var cards = new List<Control>();
            var snapshot = _multiroom;

            if (snapshot == null)
            {
                cards.Add(Muted("Checking which devices are linked…"));
            }
            else if (devices.Count < 2)
            {
                _lblGroupStatus.Text = "Not grouped";
                cards.Add(Card("Only one device", Muted("Add another WiiM device on the Devices page to play music in sync.")));
            }
            else
            {
                int groups = snapshot.LeaderOf.Values.Distinct().Count();
                _lblGroupStatus.Text = groups == 0 ? "Not grouped" : $"{groups} group{(groups == 1 ? "" : "s")}";
                string leaderIp = _owner.EffectiveGroupLeader;
                cards.Add(BuildLeaderChoiceCard(devices));
                cards.Add(BuildLeaderGroupCard(snapshot, leaderIp, devices));
                foreach (var otherLeader in snapshot.LeaderOf.Values.Distinct().Where(l => l != leaderIp))
                    cards.Add(BuildOtherGroupCard(snapshot, otherLeader));            }
            if (devices.Count > 1 && snapshot != null) cards.Add(MakeGroupCard());
            if (devices.Count > 1) cards.Add(HomeTheaterCard());
            SetRows(_groupList, cards);
        }

        private Border BuildLeaderChoiceCard(List<KnownDevice> devices)
        {
            var combo = new ComboBox { Width = 320 };
            combo.Items.Add(ComboItem(string.Empty, $"Controlled device ({DeviceName(_owner._deviceIp)})"));
            foreach (var d in devices) combo.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == _owner._groupLeaderIp);
            combo.SelectedIndex = index >= 0 ? index + 1 : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string ip) return;
                _owner.SetGroupLeader(ip);
                Dispatcher.UIThread.Post(RebuildMultiroom);
            };
            return Card("Group leader", LabeledRow("Leader", combo), Muted("Linked devices play whatever the leader plays."));
        }

        private Border BuildLeaderGroupCard(MultiroomSnapshot snapshot, string leaderIp, List<KnownDevice> devices)
        {
            string leaderName = DeviceName(leaderIp);
            var rows = new List<Control>();

            if (!snapshot.Online.Contains(leaderIp))
            {
                rows.Add(Muted($"{leaderName} is not reachable."));
            }
            else if (snapshot.LeaderOf.TryGetValue(leaderIp, out var currentLeader))
            {
                rows.Add(Muted($"{leaderName} is linked to {DeviceName(currentLeader)}, so it can't lead a group."));
                var leave = Pill("Leave that group");
                leave.Click += async (_, _) =>
                    await ChangeGroupAsync(() => _owner.RemoveFromGroupAsync(currentLeader, snapshot.AddressOf(leaderIp)));
                rows.Add(leave);
            }
            else
            {
                if (snapshot.MemberCount(leaderIp) > 0)
                {
                    rows.Add(new TextBlock { Text = leaderName, Margin = new Thickness(0, 6, 0, 0) });
                    rows.Add(new TextBlock { Text = "Leader", Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 6) });
                    var leaderChannel = _channels.GetValueOrDefault(leaderIp, ChannelStereo);
                    var leaderChannels = SegmentSwitch([(ChannelStereo, "Stereo"), (ChannelLeft, "Left"), (ChannelRight, "Right")],
                        leaderChannel is ChannelLeft or ChannelRight ? leaderChannel : ChannelStereo, FieldWidth,
                        value => ChangeGroupAsync(async () =>
                        {
                            bool ok = await _owner.SetChannelAsync(leaderIp, value);
                            if (ok) _channels[leaderIp] = value;
                            return ok;
                        }));
                    var leaderRow = LabeledRow("Plays", leaderChannels);
                    ToolTip.SetTip(leaderRow, $"Whether {leaderName} plays both channels or only the left or right one in this group, for example to use two devices as a stereo pair");
                    leaderRow.Margin = new Thickness(0, 0, 0, 10);
                    rows.Add(leaderRow);
                }
                foreach (var other in devices.Where(d => d.Ip != leaderIp))
                {
                    var o = other;
                    bool linkedHere = snapshot.LeaderOf.TryGetValue(o.Ip, out var current) && current == leaderIp;
                    var toggle = new ToggleSwitch { IsChecked = linkedHere, IsEnabled = snapshot.Online.Contains(o.Ip) };
                    toggle.IsCheckedChanged += async (_, _) =>
                        await ChangeGroupAsync(() => SetLinkedAsync(snapshot, o.Ip, leaderIp, toggle.IsChecked == true));
                    rows.Add(ToggleRow(o.Name, toggle));
                    var status = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 6),
                        Children = { new TextBlock { Text = linkedHere ? "Linked" : LinkStatus(snapshot, o.Ip), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center } }
                    };
                    rows.Add(status);
                    if (!linkedHere) continue;
                    var (rename, renameIcon) = IconButton(Icons.Edit);
                    renameIcon.Width = renameIcon.Height = 14;
                    rename.Width = rename.Height = 24;
                    rename.Padding = new Thickness(0);
                    rename.MinHeight = 0;
                    ToolTip.SetTip(rename, $"Rename {o.Name}");
                    rename.Click += async (_, _) =>
                    {
                        await RenameDeviceAsync(o);
                        await RefreshMultiroomAsync();
                    };
                    status.Children.Add(rename);
                    int channel = snapshot.Channels?.GetValueOrDefault(o.Ip) ?? ChannelStereo;
                    var channels = SegmentSwitch([(ChannelStereo, "Stereo"), (ChannelLeft, "Left"), (ChannelRight, "Right")],
                        channel is ChannelLeft or ChannelRight ? channel : ChannelStereo, FieldWidth,
                        value => ChangeGroupAsync(() => _owner.SetRoomChannelAsync(leaderIp, snapshot.AddressOf(o.Ip), value)));
                    var channelRow = LabeledRow("Plays", channels);
                    ToolTip.SetTip(channelRow, $"Whether {o.Name} plays both channels or only the left or right one in this group, for example to use two devices as a stereo pair");
                    channelRow.Margin = new Thickness(0, 0, 0, 10);
                    rows.Add(channelRow);
                }
                if (snapshot.MemberCount(leaderIp) > 0)
                {
                    var ungroup = Pill("Ungroup");
                    ungroup.Margin = new Thickness(0, 8, 0, 0);
                    ungroup.Click += async (_, _) => await ChangeGroupAsync(() => _owner.UngroupAsync(leaderIp));
                    rows.Add(ungroup);
                }
            }

            var card = Card($"Play along with {leaderName}", rows.ToArray());
            if (snapshot.MemberCount(leaderIp) > 0) card.Classes.Add("active");
            return card;
        }

        private Border BuildOtherGroupCard(MultiroomSnapshot snapshot, string leaderIp)
        {
            var members = snapshot.LeaderOf.Where(p => p.Value == leaderIp).Select(p => DeviceName(p.Key));
            var ungroup = Pill("Ungroup");
            ungroup.Click += async (_, _) => await ChangeGroupAsync(() => _owner.UngroupAsync(leaderIp));
            return Card($"Group led by {DeviceName(leaderIp)}", Muted($"Linked: {string.Join(", ", members)}"), ungroup);
        }

        private async Task<bool> SetLinkedAsync(MultiroomSnapshot snapshot, string memberIp, string leaderIp, bool linked)
        {
            if (snapshot.LeaderOf.TryGetValue(memberIp, out var current))
            {
                if (linked && current == leaderIp) return true;
                if (!await _owner.RemoveFromGroupAsync(current, snapshot.AddressOf(memberIp))) return false;
                if (!linked) return true;
            }
            else if (!linked)
            {
                return true;
            }
            return await _owner.JoinGroupAsync(memberIp, leaderIp);
        }

        private async Task ChangeGroupAsync(Func<Task<bool>> change)
        {
            if (_groupBusy) return;
            _groupBusy = true;
            _groupList.IsEnabled = false;
            _lblGroupStatus.Text = "Updating…";

            bool ok = await change();
            if (ok) await Task.Delay(2500);

            _groupBusy = false;
            _groupList.IsEnabled = true;
            if (!ok)
                await Dialogs.MessageAsync(this, "The device didn't accept the request. Check that the devices are switched on and reachable.", _scale);
            await RefreshMultiroomAsync();
        }
    }
}
