using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private VolumeControls CreateVolumeControls(string ip)
        {
            var (mute, muteIcon) = IconButton(Icons.Volume);
            var slider = new LevelSlider { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, WheelChangesValue = false };
            var value = new TextBlock
            {
                Text = "–", Classes = { "muted" }, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = slider.Value.ToString();
                if (_volumeUpdating) return;
                _volumeTouched[ip] = DateTime.UtcNow;
                _pendingVolumes[ip] = slider.Value;
                if (!_volumeSendTimer.IsEnabled) _volumeSendTimer.Start();
                ApplyGroupVolume(ip);
            };
            mute.Click += async (_, _) => await ToggleDeviceMuteAsync(ip);
            return new VolumeControls(slider, value, mute, muteIcon);
        }

        private static Grid VolumeRow(VolumeControls c, Control? extra = null)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(c.Mute);
            Grid.SetColumn(c.Slider, 1);
            row.Children.Add(c.Slider);
            Grid.SetColumn(c.Value, 2);
            row.Children.Add(c.Value);
            if (extra != null)
            {
                Grid.SetColumn(extra, 3);
                row.Children.Add(extra);
            }
            return row;
        }

        private Grid BuildVolumeRow(string ip)
        {
            var controls = CreateVolumeControls(ip);
            _volumeControls[ip] = controls;
            var channel = BuildChannelButton(ip);
            channel.Margin = new Thickness(12, 0, 0, 0);
            return VolumeRow(controls, channel);
        }

        private bool IsDraggingVolume(string ip) =>
            (_volumeControls.TryGetValue(ip, out var c) && c.Slider.IsDragging) || (_npIp == ip && _npVolume?.Slider.IsDragging == true);

        private void ApplyVolume(string ip)
        {
            if (_volumeControls.TryGetValue(ip, out var card)) ApplyVolumeTo(ip, card);
            if (_npIp == ip && _npVolume != null) ApplyVolumeTo(ip, _npVolume);
            ApplyGroupVolume(ip);
            ApplyPlayerBarVolume(ip);
            if (_npIp != null && TargetOf(_npIp) == TargetOf(ip)) MascotVolumeChanged();
        }

        private void ApplyVolumeTo(string ip, VolumeControls c)
        {
            if (_volumes.TryGetValue(ip, out var v))
            {
                bool busy = IsDraggingVolume(ip) || _pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip);
                if (!busy)
                {
                    _volumeUpdating = true;
                    c.Slider.Value = v.Volume;
                    _volumeUpdating = false;
                }
                c.Value.Text = c.Slider.Value.ToString();
                c.Value.Classes.Set("muted", false);
                c.Slider.Muted = v.Muted;
                c.Slider.IsEnabled = c.Mute.IsEnabled = true;
                if (OtherFollowerOf(ip) != null) c.Mute.IsEnabled = false;
                c.MuteIcon.Data = v.Muted ? Icons.Mute : Icons.Volume;
            }
            else
            {
                c.Slider.IsEnabled = c.Mute.IsEnabled = false;
                c.Value.Text = "–";
                c.Value.Classes.Set("muted", true);
            }
        }

        private void FlushVolumes()
        {
            foreach (var (ip, volume) in _pendingVolumes.ToList())
            {
                if (_volumeSending.Contains(ip)) continue;
                _pendingVolumes.Remove(ip);
                _ = SendVolumeAsync(ip, volume);
            }
            if (_pendingVolumes.Count == 0) _volumeSendTimer.Stop();
        }

        private async Task FlushBeforeCloseAsync()
        {
            await _eq.SendPendingEqBandsAsync();
            FlushVolumes();
            while (_volumeSending.Count > 0 || _pendingVolumes.Count > 0)
            {
                await Task.Delay(30);
                FlushVolumes();
            }
        }

        private async Task SendVolumeAsync(string ip, int volume)
        {
            _volumeSending.Add(ip);
            try
            {
                bool sent = OtherFollowerOf(ip) is { } other
                    ? await _owner.SetFollowerVolumeAsync(other.Leader, ip, volume)
                    : await _owner.SetVolumeAsync(ip, volume);
                if (sent && _volumes.TryGetValue(ip, out var current))
                    _volumes[ip] = current with { Volume = volume };
            }
            finally
            {
                _volumeSending.Remove(ip);
                _volumeTouched[ip] = DateTime.UtcNow;
                if (_pendingVolumes.ContainsKey(ip)) FlushVolumes();
            }
        }

        private async Task ToggleDeviceMuteAsync(string ip)
        {
            if (!_volumes.TryGetValue(ip, out var current)) return;
            bool muted = !current.Muted;
            _volumeTouched[ip] = DateTime.UtcNow;
            if (!await _owner.SetMuteAsync(ip, muted)) return;
            _volumes[ip] = (_volumes.TryGetValue(ip, out var latest) ? latest : current) with { Muted = muted };
            _volumeTouched[ip] = DateTime.UtcNow;
            ApplyVolume(ip);
        }

        internal void OnVolumeEvent(string ip, int? volume, bool? muted)
        {
            bool known = _volumes.TryGetValue(ip, out var current);
            if ((volume ?? (known ? current.Volume : null)) is not int v) return;
            ShowVolume(ip, v, muted ?? (known && current.Muted));
        }

        internal void OnDeviceEvent()
        {
            if (IsVisible && WindowState != WindowState.Minimized) _ = RefreshDeviceStatusesAsync();
        }

        internal void ShowVolume(string ip, int volume, bool muted)
        {
            if (_pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip)) return;
            if (IsDraggingVolume(ip)) return;
            _volumes[ip] = new DeviceVolume(volume, muted);
            ApplyVolume(ip);
        }

        private async Task RefreshVolumesAsync()
        {
            if (_volumeOnlyPolling || _volumePolling) return;
            _volumeOnlyPolling = true;
            try
            {
                var started = DateTime.UtcNow;
                var ips = _owner._knownDevices.Select(d => d.Ip).Distinct().ToList();
                var others = RefreshOtherFollowerVolumesAsync(started);
                var volumes = await Task.WhenAll(ips.Select(ip => _owner.GetVolumeAsync(ip)));
                await others;
                for (int i = 0; i < ips.Count; i++)
                {
                    string ip = ips[i];
                    if (volumes[i] is not { } volume) continue;
                    if (_volumeTouched.TryGetValue(ip, out var touched) && touched > started.AddSeconds(-1)) continue;
                    ShowVolume(ip, volume.Volume, volume.Muted);
                }
            }
            finally
            {
                _volumeOnlyPolling = false;
            }
        }

        private async Task RefreshDeviceStatusesAsync()
        {
            if (_volumePolling) return;
            _volumePolling = true;
            try
            {
                var started = DateTime.UtcNow;
                var statuses = await _owner.GetAllDeviceStatusesAsync();
                AdoptLatestGroups();
                foreach (var (ip, status) in statuses)
                {
                    if (status != null) _statuses[ip] = status;
                    else _statuses.Remove(ip);
                    ApplyNowPlaying(ip);
                    _owner.NoteDeviceStatus(ip, status);
                    if (_volumeTouched.TryGetValue(ip, out var touched) && touched > started.AddSeconds(-1)) continue;
                    if (status != null) _volumes[ip] = status.Volume;
                    else _volumes.Remove(ip);
                    ApplyVolume(ip);
                }
                ApplyFollowers();
            }
            finally
            {
                _volumePolling = false;
            }
        }
    }
}
