using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private Control FixedVolumeRow(string ip, bool on)
        {
            var toggle = new ToggleSwitch { IsChecked = on };
            bool reverting = false;
            toggle.IsCheckedChanged += async (_, _) =>
            {
                if (reverting) return;
                bool wanted = toggle.IsChecked == true;
                if (wanted && !await Dialogs.ConfirmAsync(this,
                        "Turning on fixed volume output sets the device volume to 100. This can make the sound suddenly much louder and could " +
                        "damage speakers or hearing. Only use it when an external amplifier, active speakers or a DAC controls the volume. " +
                        "Turn it on?", _scale))
                {
                    reverting = true;
                    toggle.IsChecked = false;
                    reverting = false;
                    return;
                }
                await ChangeOutputAsync(() => _owner.SetFixedVolumeAsync(ip, wanted));
            };
            var row = ToggleRow("Fixed volume output", toggle);
            ToolTip.SetTip(row, "Keeps the device at volume 100 so an external amplifier, active speakers or a DAC controls the volume");
            return row;
        }

        private Control OutputLevelRows(string ip, OutputLevel level)
        {
            var combo = new ComboBox { Width = WideFieldWidth };
            foreach (var choice in level.Choices) combo.Items.Add(ComboItem(choice, MillivoltsText(choice.Millivolts)));
            var current = level.Choices.FirstOrDefault(c => c.Index == level.Current);
            if (current == null) combo.Items.Insert(0, ComboItem(string.Empty, "Unknown"));
            combo.SelectedIndex = current == null ? 0 : level.Choices.IndexOf(current);
            string name = OutputLevelName(level.Mode);
            combo.SelectionChanged += async (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not OutputLevelChoice choice || choice == current) return;
                if (current == null || choice.Millivolts > current.Millivolts)
                {
                    string what = level.Mode == HeadphoneOutputMode ? "headphones" : "amplifier or active speakers";
                    if (!await Dialogs.ConfirmAsync(this,
                            $"Setting the {name.ToLowerInvariant()} to {MillivoltsText(choice.Millivolts)} makes the sound going to your {what} " +
                            "louder. Turn their volume down first if you're not sure. Change it?", _scale))
                    {
                        await RefreshOutputAsync(quiet: true);
                        return;
                    }
                }
                await ChangeOutputAsync(() => _owner.SetOutputLevelAsync(ip, level, choice.Index));
            };
            var row = LabeledRow(name, combo);
            ToolTip.SetTip(row, level.Mode == HeadphoneOutputMode
                ? "How strong the signal from the headphone output is. Choose a lower level for sensitive headphones."
                : "How strong the signal from the line out is. Match it to the input of your amplifier or active speakers: 2 Vrms is the " +
                  "usual full line level, lower levels suit inputs that distort or are too loud.");
            if (level.Mode == HeadphoneOutputMode) return row;
            var note = new TextBlock
            {
                Text = "If you hear some noise and distortion from your audio system, please select a lower level to resolve it.",
                Classes = { "muted", "small" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            return new StackPanel { Spacing = 4, Children = { row, note } };
        }

        private Border BuildOutputCard(string ip, OutputState state)
        {
            var rows = new List<Control>();
            if (state.Choices.Count > 0)
            {
                var combo = new ComboBox { Width = WideFieldWidth };
                foreach (var choice in state.Choices) combo.Items.Add(ComboItem(choice, choice.Name));
                int active = state.Choices.FindIndex(c => c.Mode == state.ActiveMode);
                if (active < 0)
                {
                    combo.Items.Insert(0, ComboItem(string.Empty, UnlistedOutputName(state.ActiveMode)));
                    active = 0;
                }
                combo.SelectedIndex = active;
                ToolTip.SetTip(combo, "Where the device sends its sound");
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ComboBoxItem)?.Tag is not OutputChoice choice || choice.Mode == state.ActiveMode) return;
                    await ChangeOutputAsync(() => _owner.SetOutputAsync(ip, choice));
                };
                rows.Add(LabeledRow("Output", combo));
            }

            if (state.FixedVolume is bool fixedVolume) rows.Add(FixedVolumeRow(ip, fixedVolume));
            foreach (var level in state.Levels ?? []) rows.Add(OutputLevelRows(ip, level));

            if (state.Usb is { } usb)
            {
                var rates = usb.Rates.Contains(usb.Rate) ? usb.Rates : [.. usb.Rates, usb.Rate];
                var bitsList = usb.Bits.Contains(usb.BitDepth) ? usb.Bits : [.. usb.Bits, usb.BitDepth];
                var rate = new ComboBox { Width = (WideFieldWidth - FieldGap) / 2 };
                foreach (int r in rates) rate.Items.Add(ComboItem(r, (r / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz"));
                rate.SelectedIndex = rates.IndexOf(usb.Rate);
                var depth = new ComboBox { Width = (WideFieldWidth - FieldGap) / 2 };
                foreach (int b in bitsList) depth.Items.Add(ComboItem(b, $"{b}-bit"));
                depth.SelectedIndex = bitsList.IndexOf(usb.BitDepth);
                async Task Apply()
                {
                    if ((rate.SelectedItem as ComboBoxItem)?.Tag is int r && (depth.SelectedItem as ComboBoxItem)?.Tag is int b && (r != usb.Rate || b != usb.BitDepth))
                        await ChangeOutputAsync(() => _owner.SetUsbResolutionAsync(ip, r, b));
                }
                rate.SelectionChanged += async (_, _) => await Apply();
                depth.SelectionChanged += async (_, _) => await Apply();
                var usbRow = LabeledRow("USB output resolution", new StackPanel { Orientation = Orientation.Horizontal, Spacing = FieldGap, Children = { rate, depth } });
                ToolTip.SetTip(usbRow, "The highest sample rate and bit depth sent to the USB DAC. The choices are what the DAC reports it supports.");
                rows.Add(usbRow);
            }

            if (state.LineOut is { } lineOut)
            {
                var simultaneous = OutputToggle("Simultaneous line out", lineOut.Enabled, on => _owner.SetSimultaneousLineOutAsync(ip, lineOut.Mode, on));
                ToolTip.SetTip(simultaneous, "Also plays the sound on the line out. The audio format then stays fixed to match the digital output.");
                rows.Add(simultaneous);
            }
            if (state.SwitchDelay is int switchDelay)
            {
                var slider = AmpSlider(0, Math.Max(1000, switchDelay), switchDelay, v => $"{v} ms", false, out var sliderRow);
                slider.ValueCommitted += async (_, _) => await ChangeOutputAsync(() => _owner.SetSwitchDelayAsync(ip, slider.Value));
                var delayRow = LabeledRow("Sample rate switch latency", sliderRow);
                ToolTip.SetTip(delayRow, "A short pause when the sample rate changes, which prevents a pop on some DACs");
                rows.Add(delayRow);
            }

            var find = Pill("Find speakers");
            var disconnect = Pill("Disconnect");
            disconnect.IsEnabled = state.BluetoothName.Length > 0 || state.BluetoothOut;
            ToolTip.SetTip(find, "Look for Bluetooth speakers and headphones and connect one. The device then plays through it.");
            find.Click += async (_, _) => await ChooseBluetoothSpeakerAsync(ip);
            disconnect.Click += async (_, _) => await ChangeOutputAsync(() => _owner.DisconnectBluetoothSpeakerAsync(ip));
            string bluetooth = state.BluetoothName.Length > 0 ? $"Connected to {state.BluetoothName}" : state.BluetoothOut ? "Connected" : "Not connected";
            rows.Add(LabeledRow("Bluetooth speaker", new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new TextBlock { Text = bluetooth, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, find, disconnect }
            }));
            var receiversButton = Pill("Receivers");
            ToolTip.SetTip(receiversButton, "Pick the DLNA speakers this device sends its sound to. Picking one switches DLNA out on; removing all switches it off.");
            receiversButton.Click += async (_, _) => await ChooseCastReceiversAsync(ip);
            var selectedNames = state.CastReceivers?.Where(r => r.Selected).Select(r => r.Name).ToList() ?? [];
            string castText = !state.CastOut ? "Off" : selectedNames.Count > 0 ? $"On, to {string.Join(", ", selectedNames)}" : "On";
            rows.Add(LabeledRow("DLNA out", new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new TextBlock { Text = castText, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, receiversButton }
            }));
            if (state.CastOut)
            {
                var profile = new ComboBox { Width = WideFieldWidth };
                foreach (var p in CastProfiles) profile.Items.Add(ComboItem(p, p.Name));
                profile.SelectedIndex = Array.FindIndex(CastProfiles, p => p.Key == state.CastProfileKey);
                ToolTip.SetTip(profile, "The format the sound is sent to the DLNA speaker in. It's used the next time casting starts.");
                profile.SelectionChanged += async (_, _) =>
                {
                    if ((profile.SelectedItem as ComboBoxItem)?.Tag is CastProfile chosen && chosen.Key != state.CastProfileKey)
                        await ChangeOutputAsync(() => _owner.SetCastProfileAsync(ip, chosen));
                };
                rows.Add(LabeledRow("DLNA transcoding", profile));
                var latency = new ComboBox { Width = WideFieldWidth };
                foreach (var (ms, name) in CastLatencies) latency.Items.Add(ComboItem(ms, name));
                latency.SelectedIndex = Array.FindIndex(CastLatencies, l => l.Milliseconds == state.CastLatency);
                ToolTip.SetTip(latency, "Higher latency gives fewer interruptions, but the sound starts later");
                latency.SelectionChanged += async (_, _) =>
                {
                    if ((latency.SelectedItem as ComboBoxItem)?.Tag is int ms && ms != state.CastLatency)
                        await ChangeOutputAsync(() => _owner.SetCastLatencyAsync(ip, ms));
                };
                rows.Add(LabeledRow("DLNA latency", latency));
            }
            return Card("Output", rows.ToArray());
        }

        private async Task ChooseCastReceiversAsync(string ip)
        {
            var status = new TextBlock
            {
                Text = "Looking for DLNA speakers… This can take a little while.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            var list = new StackPanel { Spacing = 6 };
            var content = new StackPanel
            {
                Spacing = 10, Width = 440,
                Children = { new TextBlock { Text = "DLNA out", Classes = { "heading" } }, status, new ScrollViewer { Content = list, MaxHeight = 320 } }
            };
            var boxes = new Dictionary<string, CheckBox>();
            var original = new Dictionary<string, bool>();
            void Show(List<CastReceiver> receivers)
            {
                foreach (var receiver in receivers)
                {
                    if (boxes.ContainsKey(receiver.Id)) continue;
                    original[receiver.Id] = receiver.Selected;
                    var box = new CheckBox { Content = receiver.Name, IsChecked = receiver.Selected };
                    boxes[receiver.Id] = box;
                    list.Children.Add(box);
                }
                status.Text = boxes.Count == 0
                    ? "Looking for DLNA speakers… This can take a little while."
                    : "Tick the speakers to send the sound to. Untick all to switch DLNA out off.";
            }
            using var cancel = new CancellationTokenSource();
            if (await _owner.GetCastReceiversAsync(ip) is { } known) Show(known);
            _ = Task.Run(async () =>
            {
                try
                {
                    var found = await _owner.FindCastReceiversAsync(ip, r => Dispatcher.UIThread.Post(() => Show(r)), cancel.Token);
                    if (found.Count == 0)
                        Dispatcher.UIThread.Post(() => { if (boxes.Count == 0) status.Text = "No DLNA speakers found. Check that they're switched on."; });
                }
                catch (OperationCanceledException) { }
            });
            bool ok = await Dialogs.ChooseAsync(this, content, "Save", _scale);
            cancel.Cancel();
            if (!ok) return;
            var changes = boxes.Where(b => (b.Value.IsChecked == true) != original[b.Key]).Select(b => (Id: b.Key, On: b.Value.IsChecked == true)).ToList();
            if (changes.Count == 0) return;
            await ChangeOutputAsync(async () =>
            {
                bool all = true;
                foreach (var (id, on) in changes.OrderByDescending(c => c.On)) all &= await _owner.SetCastReceiverAsync(ip, id, on);
                return all;
            });
        }

        private async Task ChooseBluetoothSpeakerAsync(string ip)
        {
            var status = new TextBlock { Text = "Looking for Bluetooth speakers… Put the speaker in pairing mode.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var list = new StackPanel { Spacing = 6 };
            var content = new StackPanel
            {
                Spacing = 10, Width = 440,
                Children = { new TextBlock { Text = "Connect a Bluetooth speaker", Classes = { "heading" } }, status, new ScrollViewer { Content = list, MaxHeight = 320 } }
            };
            var choices = new Dictionary<string, RadioButton>();
            void Show(IEnumerable<BluetoothSpeaker> speakers)
            {
                foreach (var speaker in speakers)
                {
                    if (choices.ContainsKey(speaker.Address)) continue;
                    var radio = new RadioButton { Content = speaker.Name, GroupName = "bt", Tag = speaker.Address };
                    ToolTip.SetTip(radio, speaker.Address);
                    choices[speaker.Address] = radio;
                    list.Children.Add(radio);
                }
            }
            using var cancel = new CancellationTokenSource();
            Show(await _owner.GetPairedSpeakersAsync(ip));
            _ = Task.Run(async () =>
            {
                List<BluetoothSpeaker> found;
                try { found = await _owner.ScanBluetoothSpeakersAsync(ip, cancel.Token); }
                catch (OperationCanceledException) { return; }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Show(found);
                    status.Text = choices.Count == 0
                        ? "No Bluetooth speakers found. Put the speaker in pairing mode and try again."
                        : "Pick the speaker to connect.";
                });
            });
            bool ok = await Dialogs.ChooseAsync(this, content, "Connect", _scale);
            cancel.Cancel();
            if (!ok || choices.Values.FirstOrDefault(r => r.IsChecked == true)?.Tag is not string address) return;
            await ChangeOutputAsync(() => _owner.ConnectBluetoothSpeakerAsync(ip, address));
        }
    }
}
