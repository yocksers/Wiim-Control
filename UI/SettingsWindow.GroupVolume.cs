using Avalonia;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed class GroupVolume(VolumeControls controls, List<string> members)
        {
            public VolumeControls Controls { get; } = controls;
            public List<string> Members { get; } = members;
            public int Shown { get; set; }
        }

        private readonly Dictionary<string, GroupVolume> _groupVolumes = new();

        private bool IsFollower(string ip) => _multiroom != null && _multiroom.LeaderOf.ContainsKey(ip);

        private List<string> GroupMembers(string leader)
        {
            if (_multiroom == null || _multiroom.MemberCount(leader) == 0 && _multiroom.OthersOf(leader).Count == 0) return [leader];
            return [leader, .. _owner._knownDevices.Select(d => d.Ip).Where(ip => _multiroom.LeaderOf.TryGetValue(ip, out var l) && l == leader),
                .. _multiroom.OthersOf(leader).Select(o => o.Address)];
        }

        private (string Leader, OtherFollower Follower)? OtherFollowerOf(string ip)
        {
            if (_multiroom?.OtherFollowers is not { } groups) return null;
            foreach (var (leader, others) in groups)
                if (others.FirstOrDefault(o => o.Address == ip) is { } follower) return (leader, follower);
            return null;
        }

        private bool IsFixedVolume(string ip) => _multiroom?.FixedVolume?.Contains(ip) == true;

        private bool FollowsGroupVolume(string ip) =>
            _volumes.TryGetValue(ip, out var volume) && !volume.Muted && !IsFixedVolume(ip) && !_owner.IsDeviceLocked(ip);

        private int CurrentVolume(string ip) =>
            _pendingVolumes.TryGetValue(ip, out int pending) ? pending : _volumes.TryGetValue(ip, out var v) ? v.Volume : 0;

        private Grid BuildGroupVolumeRow(string leader, List<string> members)
        {
            var (mute, muteIcon) = IconButton(Icons.Volume);
            var slider = new LevelSlider { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, WheelChangesValue = false };
            var value = new TextBlock
            {
                Text = "–", Classes = { "muted" }, MinWidth = 34, TextAlignment = Avalonia.Media.TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var group = new GroupVolume(new VolumeControls(slider, value, mute, muteIcon), members);
            _groupVolumes[leader] = group;
            slider.ValueChanged += (_, _) =>
            {
                value.Text = slider.Value.ToString();
                if (_volumeUpdating) return;
                int delta = slider.Value - group.Shown;
                group.Shown = slider.Value;
                if (delta == 0) return;
                foreach (var ip in members.Where(FollowsGroupVolume))
                    QueueVolume(ip, Math.Clamp(CurrentVolume(ip) + delta, 0, 100));
            };
            mute.Click += async (_, _) => await ToggleGroupMuteAsync(group);
            ToolTip.SetTip(slider, "Changes the volume of every device in the group by the same amount. Muted devices and devices with fixed volume output stay as they are.");
            ToolTip.SetTip(mute, "Mute or unmute every device in the group");
            var row = VolumeRow(group.Controls);
            row.IsEnabled = members.Any(ip => !_owner.IsDeviceLocked(ip));
            return row;
        }

        private Border BuildGroupMembersCard(List<string> members)
        {
            var rows = new List<Control>();
            foreach (var ip in members)
            {
                var row = BuildVolumeRow(ip);
                row.IsEnabled = !_owner.IsDeviceLocked(ip);
                row.Margin = new Thickness(0, 4, 0, 0);
                string name = OtherFollowerOf(ip) is { } other ? other.Follower.Name : DeviceName(ip);
                if (IsFixedVolume(ip)) name += "  ·  fixed volume output";
                var label = new TextBlock { Text = name };
                if (OtherFollowerOf(ip) != null)
                    ToolTip.SetTip(label, "This device isn't in your device list, so its volume is set through the group leader and it can only be muted with the whole group");
                rows.Add(new StackPanel
                {
                    Margin = new Thickness(0, rows.Count == 0 ? 0 : 12, 0, 0),
                    Children = { label, row }
                });
            }
            return Card("Volume per device", [.. rows]);
        }

        private void QueueVolume(string ip, int volume)
        {
            _volumeTouched[ip] = DateTime.UtcNow;
            _pendingVolumes[ip] = volume;
            if (!_volumeSendTimer.IsEnabled) _volumeSendTimer.Start();
            _volumeUpdating = true;
            if (_volumeControls.TryGetValue(ip, out var card)) card.Slider.Value = volume;
            if (_npIp == ip && _npVolume != null) _npVolume.Slider.Value = volume;
            _volumeUpdating = false;
        }

        private void ApplyGroupVolume(string ip)
        {
            if (!_groupVolumes.TryGetValue(OtherFollowerOf(ip)?.Leader ?? TargetOf(ip), out var group)) return;
            var c = group.Controls;
            var known = group.Members.Where(_volumes.ContainsKey).ToList();
            if (known.Count == 0)
            {
                c.Slider.IsEnabled = c.Mute.IsEnabled = false;
                c.Value.Text = "–";
                c.Value.Classes.Set("muted", true);
                return;
            }
            if (!c.Slider.IsDragging)
            {
                var steering = known.Where(FollowsGroupVolume).ToList();
                group.Shown = (steering.Count > 0 ? steering : known).Max(CurrentVolume);
                _volumeUpdating = true;
                c.Slider.Value = group.Shown;
                _volumeUpdating = false;
            }
            bool muted = known.All(m => _volumes[m].Muted);
            c.Value.Text = c.Slider.Value.ToString();
            c.Value.Classes.Set("muted", false);
            c.Slider.Muted = muted;
            c.Slider.IsEnabled = c.Mute.IsEnabled = true;
            c.MuteIcon.Data = muted ? Icons.Mute : Icons.Volume;
        }

        private async Task ToggleGroupMuteAsync(GroupVolume group)
        {
            var members = group.Members.Where(ip => _volumes.ContainsKey(ip) && !_owner.IsDeviceLocked(ip)).ToList();
            if (members.Count == 0) return;
            bool muted = !members.All(ip => _volumes[ip].Muted);
            var others = members.Where(ip => OtherFollowerOf(ip) != null).ToList();
            var own = members.Except(others).Select(async ip =>
            {
                _volumeTouched[ip] = DateTime.UtcNow;
                if (!await _owner.SetMuteAsync(ip, muted) || !_volumes.TryGetValue(ip, out var current)) return;
                _volumes[ip] = current with { Muted = muted };
                _volumeTouched[ip] = DateTime.UtcNow;
                ApplyVolume(ip);
            }).ToList();
            if (others.Count > 0)
                own.Add(Task.Run(async () =>
                {
                    foreach (var ip in others) _volumeTouched[ip] = DateTime.UtcNow;
                    if (!await _owner.SetAllFollowersMutedAsync(group.Members[0], muted)) return;
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var ip in others)
                        {
                            if (_volumes.TryGetValue(ip, out var current)) _volumes[ip] = current with { Muted = muted };
                            _volumeTouched[ip] = DateTime.UtcNow;
                            ApplyVolume(ip);
                        }
                    });
                }));
            await Task.WhenAll(own);
        }

        private async Task RefreshOtherFollowerVolumesAsync(DateTime started)
        {
            var leaders = _groupVolumes.Keys.Where(leader => _multiroom?.OthersOf(leader).Count > 0).ToList();
            foreach (var leader in leaders)
                foreach (var (address, _, volume, muted) in await _owner.GetFollowerStatesAsync(leader))
                {
                    if (volume is not int level || OtherFollowerOf(address) == null) continue;
                    if (_volumeTouched.TryGetValue(address, out var touched) && touched > started.AddSeconds(-1)) continue;
                    ShowVolume(address, level, muted);
                }
        }

        private static bool SameGroups(MultiroomSnapshot? a, MultiroomSnapshot? b) =>
            a != null && b != null && a.Online.SetEquals(b.Online) && a.LeaderOf.Count == b.LeaderOf.Count &&
            a.LeaderOf.All(p => b.LeaderOf.TryGetValue(p.Key, out var leader) && leader == p.Value) &&
            OtherAddresses(a).SequenceEqual(OtherAddresses(b)) && (a.FixedVolume ?? []).SetEquals(b.FixedVolume ?? []);

        private static IEnumerable<string> OtherAddresses(MultiroomSnapshot snapshot) =>
            (snapshot.OtherFollowers ?? []).SelectMany(p => p.Value.Select(o => $"{p.Key}>{o.Address}")).Order();

        private void AdoptLatestGroups()
        {
            if (_owner._multiroomCache is not { } latest || ReferenceEquals(latest, _multiroom)) return;
            bool changed = !SameGroups(latest, _multiroom);
            _multiroom = latest;
            if (!changed) return;
            RebuildMultiroom();
            RebuildDeviceCards();
        }
    }
}
