using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboSubAmp = new() { Width = 220 };
        private readonly TextBlock _lblSubStatus = Subtitle();
        private readonly StackPanel _subRows = new();
        private readonly SemaphoreSlim _subGate = new(1, 1);
        private int _subVersion;

        private Control BuildSubwooferPage()
        {
            ToolTip.SetTip(_cboSubAmp, "The device whose subwoofer settings are shown");
            _cboSubAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshSubwooferAsync(); };
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshSubwooferAsync();
            var page = Page("Subwoofer", _lblSubStatus, [_cboSubAmp, refresh], _subRows);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshSubwooferAsync();
            };
            return page;
        }

        private async Task RefreshSubwooferAsync(bool quiet = false)
        {
            int version = ++_subVersion;
            if (AmpOf(_cboSubAmp) is not { } ip)
            {
                SetRows(_subRows, [Muted("Add a WiiM device on the Devices page first.")]);
                return;
            }
            if (!quiet) _lblSubStatus.Text = "Loading…";
            var (reachable, sub, highPass, wiimSubs) = await _owner.GetSubwooferAsync(ip);
            if (version != _subVersion) return;
            _lblSubStatus.Text = reachable ? string.Empty : "Not reachable";
            var cards = new List<Control>();
            if (!reachable) cards.Add(Muted("Couldn't reach this device."));
            else
            {
                if (wiimSubs != null) cards.Add(BuildWiimSubCard(ip, wiimSubs, sub != null));
                if (sub == null)
                    cards.Add(Muted("This device has no subwoofer output. The WiiM Amp, Amp Pro, Amp Ultra and Ultra have one."));
                else
                {
                    cards.Add(BuildSubwooferCard(ip, sub));
                    if (highPass != null) cards.Add(BuildHighPassCard(ip, highPass, sub.Enabled && sub.Plugged));
                }
            }
            SetRows(_subRows, cards);
        }

        private async Task ChangeSubAsync(Func<Task<bool>> change)
        {
            await _subGate.WaitAsync();
            bool ok;
            try
            {
                _lblSubStatus.Text = "Saving…";
                ok = await change();
            }
            finally { _subGate.Release(); }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
            if (_subGate.CurrentCount == 0) return;
            await RefreshSubwooferAsync(quiet: true);
        }

        private Grid SubToggle(string text, bool value, Func<bool, Task<bool>> apply)
        {
            var toggle = new ToggleSwitch { IsChecked = value };
            toggle.IsCheckedChanged += async (_, _) => await ChangeSubAsync(() => apply(toggle.IsChecked == true));
            return ToggleRow(text, toggle);
        }

        private Border BuildSubwooferCard(string ip, SubwooferInfo sub)
        {
            var rows = new List<Control>
            {
                SubToggle("Subwoofer output", sub.Enabled, on => _owner.SetSubwooferAsync(ip, "status", on ? 1 : 0)),
                LabeledRow("Subwoofer connected", Info(sub.Plugged ? "Yes" : "No"))
            };

            var level = AmpSlider(-15, 15, (int)Math.Round(sub.Level), v => Invariant($"{v:+0;-0;0} dB"), true, out var levelRow);
            level.ValueCommitted += async (_, _) => await ChangeSubAsync(() => _owner.SetSubwooferAsync(ip, "level", level.Value));
            rows.Add(LabeledRow("Level", levelRow));

            var cross = AmpSlider(30, 250, (int)Math.Round(sub.Crossover), v => $"{v} Hz", false, out var crossRow);
            cross.ValueCommitted += async (_, _) => await ChangeSubAsync(() => _owner.SetSubwooferAsync(ip, "cross", cross.Value));
            var crossLabel = LabeledRow("Crossover", crossRow);
            crossLabel.IsEnabled = !sub.Bypass;
            crossLabel.Opacity = sub.Bypass ? 0.45 : 1;
            rows.Add(crossLabel);

            var phase = new ComboBox { Width = 120 };
            phase.Items.Add(ComboItem(0, "0°"));
            phase.Items.Add(ComboItem(180, "180°"));
            phase.SelectedIndex = sub.Phase >= 90 ? 1 : 0;
            phase.SelectionChanged += async (_, _) =>
            {
                if ((phase.SelectedItem as ComboBoxItem)?.Tag is int value && value != (sub.Phase >= 90 ? 180 : 0))
                    await ChangeSubAsync(() => _owner.SetSubwooferAsync(ip, "phase", value));
            };
            rows.Add(LabeledRow("Phase", phase));

            if (sub.Delay is double delay)
            {
                var delaySlider = AmpSlider(-2000, 2000, (int)Math.Round(delay * 10), v => Invariant($"{v / 10.0:0.0} ms"), true, out var delayRow);
                delaySlider.ValueCommitted += async (_, _) =>
                    await ChangeSubAsync(() => _owner.SetSubwooferAsync(ip, "sub_delay", delaySlider.Value / 10.0));
                rows.Add(LabeledRow("Delay", delayRow));
            }

            var bypass = SubToggle("Subwoofer bypass mode", sub.Bypass, on => _owner.SetSubwooferAsync(ip, "sub_filter", on ? 0 : 1));
            ToolTip.SetTip(bypass, "Sends the sound to the subwoofer without the crossover, so it gets the full frequency range");
            rows.Add(bypass);

            var mainBass = SubToggle("Main speakers output bass", !sub.MainHighPass, on => _owner.SetSubwooferAsync(ip, "main_filter", on ? 0 : 1));
            ToolTip.SetTip(mainBass, "Lets the main speakers also play bass while the subwoofer is active");
            rows.Add(mainBass);
            return Card("Subwoofer", rows.ToArray());
        }

        private Border BuildHighPassCard(string ip, HighPassFilter filter, bool subwooferActive)
        {
            var rows = new List<Control>
            {
                Muted(subwooferActive
                    ? "A subwoofer is connected, so the subwoofer crossover filters the bass for the main speakers instead."
                    : "Keeps the deepest bass away from the main speakers, so they play cleaner and stay better controlled when the music gets loud. " +
                      "Useful when no subwoofer is connected.")
            };
            var toggle = SubToggle("High-pass filter", filter.Enabled, on => _owner.SetHighPassEnabledAsync(ip, on));
            var frequency = new ComboBox { Width = 120 };
            foreach (int hz in HighPassFrequencies.Append(filter.Frequency).Distinct().Order()) frequency.Items.Add(ComboItem(hz, $"{hz} Hz"));
            frequency.SelectedItem = frequency.Items.OfType<ComboBoxItem>().First(i => i.Tag is int hz && hz == filter.Frequency);
            frequency.SelectionChanged += async (_, _) =>
            {
                if ((frequency.SelectedItem as ComboBoxItem)?.Tag is int hz && hz != filter.Frequency)
                    await ChangeSubAsync(() => _owner.SetHighPassFrequencyAsync(ip, hz));
            };
            var frequencyRow = LabeledRow("Cut-off frequency", frequency);
            ToolTip.SetTip(frequencyRow, "Sound below this frequency is filtered out of the main speakers");
            frequencyRow.IsEnabled = filter.Enabled && !subwooferActive;
            frequencyRow.Opacity = frequencyRow.IsEnabled ? 1 : 0.45;
            toggle.IsEnabled = !subwooferActive;
            toggle.Opacity = subwooferActive ? 0.45 : 1;
            rows.Add(toggle);
            rows.Add(frequencyRow);
            return Card("High-pass filter", rows.ToArray());
        }
    }
}
