using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private Border BuildWiimSubCard(string ip, List<PairedSub> paired, bool hasSubOutput)
        {
            var rows = new List<Control>
            {
                Muted(paired.Count == 0
                    ? "Connect a WiiM Sub Pro to this device, wired with an RCA cable or wireless over Wi-Fi."
                    : "The WiiM Sub Pro connected to this device.")
            };
            foreach (var sub in paired)
            {
                var unpair = Pill("Unpair");
                string name = sub.Name.Length > 0 ? sub.Name : "WiiM Sub Pro";
                ToolTip.SetTip(unpair, $"Disconnect {name} from this device");
                unpair.Click += async (_, _) =>
                {
                    if (!await Dialogs.ConfirmAsync(this, $"Unpair {name} from this device?", _scale)) return;
                    await ChangeSubAsync(() => _owner.ForgetSubAsync(ip, sub.Uuid));
                };
                var state = Info($"{(sub.Wired ? "Wired" : "Wireless")} · {(sub.Online ? "Online" : "Offline")}");
                state.VerticalAlignment = VerticalAlignment.Center;
                rows.Add(LabeledRow(name, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { state, unpair } }));
            }
            if (paired.Count == 0)
            {
                var add = Pill("Add WiiM subwoofer", accent: true);
                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Click += async (_, _) => await AddWiimSubAsync(ip, hasSubOutput);
                rows.Add(add);
            }
            return Card("WiiM subwoofer", rows.ToArray());
        }

        private async Task AddWiimSubAsync(string ip, bool hasSubOutput)
        {
            _lblSubStatus.Text = "Looking for WiiM subwoofers…";
            var (subs, devices) = await _owner.FindWiimSubsAsync();
            _lblSubStatus.Text = string.Empty;
            if (subs.Count == 0)
            {
                await Dialogs.MessageAsync(this,
                    "No WiiM Sub Pro was found. Check that it's switched on and connected to the same Wi-Fi network, then try again.", _scale);
                return;
            }
            string thisUuid = devices.FirstOrDefault(d => d.Ip == ip)?.Uuid ?? string.Empty;
            string thisName = devices.FirstOrDefault(d => d.Ip == ip)?.Name ?? "this device";
            DeviceIdentity? Master(SubCandidate s) => devices.FirstOrDefault(d => d.Uuid.Equals(s.PairedTo, StringComparison.OrdinalIgnoreCase));

            var combo = new ComboBox { Width = 360 };
            foreach (var s in subs)
            {
                string note = s.PairedTo.Length == 0 || s.PairedTo.Equals(thisUuid, StringComparison.OrdinalIgnoreCase) ? string.Empty
                    : $" (paired with {Master(s)?.Name ?? "another device"})";
                combo.Items.Add(ComboItem(s, $"{s.Name} · {s.Ip}{note}"));
            }
            combo.SelectedIndex = 0;
            var wired = new RadioButton
            {
                GroupName = "WiimSubConnection", IsChecked = hasSubOutput, IsVisible = hasSubOutput,
                Content = new TextBlock { Text = "Wired (recommended): connect an RCA cable from this device's SUB OUT to the Sub Pro", TextWrapping = TextWrapping.Wrap, MaxWidth = 330 }
            };
            var wireless = new RadioButton
            {
                GroupName = "WiimSubConnection", IsChecked = !hasSubOutput,
                Content = new TextBlock { Text = "Wireless over Wi-Fi", TextWrapping = TextWrapping.Wrap, MaxWidth = 330 }
            };
            var content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Add WiiM subwoofer", Classes = { "heading" } },
                    new TextBlock { Text = $"Choose the WiiM Sub Pro to connect to {thisName}.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Classes = { "muted" } },
                    ShareField("Subwoofer", combo),
                    ShareField("Connection", new StackPanel { Spacing = 6, Children = { wired, wireless } })
                }
            };
            if (!await Dialogs.ChooseAsync(this, content, "Connect", _scale)) return;
            if ((combo.SelectedItem as ComboBoxItem)?.Tag is not SubCandidate sub) return;

            if (sub.PairedTo.Length > 0 && !sub.PairedTo.Equals(thisUuid, StringComparison.OrdinalIgnoreCase))
            {
                if (Master(sub) is not { } master)
                {
                    await Dialogs.MessageAsync(this,
                        $"{sub.Name} is paired with another device that wasn't found. Switch that device on, or unpair the subwoofer from it first.", _scale);
                    return;
                }
                if (!await Dialogs.ConfirmAsync(this, $"{sub.Name} is paired with {master.Name}. Unpair it from {master.Name} and connect it to {thisName}?", _scale))
                    return;
                _lblSubStatus.Text = $"Unpairing {sub.Name} from {master.Name}…";
                if (!await _owner.ForgetSubAsync(master.Ip, sub.Uuid))
                {
                    _lblSubStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(this, $"{master.Name} didn't unpair {sub.Name}. Try again, or restart both devices.", _scale);
                    return;
                }
            }

            bool useWire = wired.IsChecked == true;
            _lblSubStatus.Text = $"Connecting {sub.Name} to {thisName}… This may take a few seconds.";
            bool ok = await _owner.PairSubAsync(ip, sub, useWire);
            _lblSubStatus.Text = string.Empty;
            await Dialogs.MessageAsync(this, ok
                ? $"{sub.Name} is connected to {thisName}." + (useWire ? " Make sure the RCA cable goes from the SUB OUT to the Sub Pro." : string.Empty)
                : $"Couldn't connect {sub.Name} to {thisName}. Restart both devices, then try again.", _scale);
            await RefreshSubwooferAsync(quiet: true);
        }
    }
}
