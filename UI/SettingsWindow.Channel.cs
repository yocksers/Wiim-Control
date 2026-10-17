using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly Dictionary<string, int> _channels = new();
        private readonly Dictionary<string, Button> _channelButtons = new();
        private readonly HashSet<string> _channelChanging = new();

        private Button BuildChannelButton(string ip)
        {
            var button = new Button { Content = "LR", IsVisible = false };
            button.Classes.Add("channel");
            button.Click += async (_, _) => await CycleChannelAsync(ip);
            _channelButtons[ip] = button;
            return button;
        }

        private static int NextChannel(int channel) => channel switch
        {
            ChannelStereo => ChannelLeft,
            ChannelLeft => ChannelRight,
            _ => ChannelStereo
        };

        private static string ChannelLabel(int channel) => channel switch
        {
            ChannelLeft => "L",
            ChannelRight => "R",
            _ => "LR"
        };

        private static string ChannelText(int channel) => channel switch
        {
            ChannelLeft => "the left channel only",
            ChannelRight => "the right channel only",
            _ => "both channels (stereo)"
        };

        private bool IsGrouped(string ip) =>
            _multiroom != null && (_multiroom.LeaderOf.ContainsKey(ip) || _multiroom.MemberCount(ip) > 0);

        private void ApplyChannel(string ip)
        {
            if (!_channelButtons.TryGetValue(ip, out var button)) return;
            if (!_channels.TryGetValue(ip, out int channel))
            {
                button.IsVisible = false;
                return;
            }
            button.IsVisible = IsGrouped(ip) || channel != ChannelStereo;
            button.Content = ChannelLabel(channel);
            button.Classes.Set("single", channel != ChannelStereo);
            button.IsEnabled = !_channelChanging.Contains(ip);
            ToolTip.SetTip(button, $"Plays {ChannelText(channel)}. Click to play {ChannelText(NextChannel(channel))}.");
        }

        private async Task RefreshChannelsAsync()
        {
            var ips = _owner._knownDevices.Select(d => d.Ip).Where(ip => _multiroom == null || _multiroom.Online.Contains(ip)).ToList();
            var results = await Task.WhenAll(ips.Select(async ip => (Ip: ip, Channel: await _owner.GetChannelAsync(ip))));
            foreach (var (ip, channel) in results)
            {
                if (_channelChanging.Contains(ip)) continue;
                if (channel is int value) _channels[ip] = value;
                else _channels.Remove(ip);
                ApplyChannel(ip);
            }
        }

        private async Task CycleChannelAsync(string ip)
        {
            if (!_channels.TryGetValue(ip, out int current) || !_channelChanging.Add(ip)) return;
            int next = NextChannel(current);
            _channels[ip] = next;
            ApplyChannel(ip);
            bool ok = await _owner.SetChannelAsync(ip, next);
            int? confirmed = await _owner.GetChannelAsync(ip);
            _channelChanging.Remove(ip);
            if (confirmed is int value) _channels[ip] = value;
            else if (!ok) _channels[ip] = current;
            ApplyChannel(ip);
            if (!ok) await Dialogs.MessageAsync(this, $"{DeviceName(ip)} didn't change the channel. Check that it's switched on and reachable.", _scale);
        }
    }
}
