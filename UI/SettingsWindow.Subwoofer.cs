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

        private const string SectionHighPass = "High-pass filter";
        private const string SectionSubOut = "Sub out";

        private sealed record SubwooferState(string Ip, bool Reachable, SubwooferInfo? Sub, HighPassFilter? HighPass, List<PairedSub>? WiimSubs,
            bool WiredOutput)
        {
            public bool SubAvailable => WiredOutput || WiimSubs?.Any(s => s.Online) == true;
        }

        private string _subSection = SectionSubOut;
        private SubwooferState? _subCache;

        private Control BuildSubwooferPage()
        {
            ToolTip.SetTip(_cboSubAmp, "The device whose subwoofer settings are shown");
            _cboSubAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshSubwooferAsync(); };
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshSubwooferAsync();
            var page = Page(SectionSubOut, _lblSubStatus, [_cboSubAmp, refresh], _subRows);
            _sectionHandlers[page] = section =>
            {
                _subSection = section;
                FillSubwooferCombo(followActive: false);
                bool cached = _subCache != null && _subCache.Ip == AmpOf(_cboSubAmp);
                SetRows(_subRows, cached ? SubwooferCards(_subCache!) : []);
                if (page.IsVisible) _ = RefreshSubwooferAsync(quiet: cached);
            };
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property != IsVisibleProperty || !page.IsVisible) return;
                await CheckSubwooferDevicesAsync();
                await RefreshSubwooferAsync(quiet: _subCache?.Ip == AmpOf(_cboSubAmp));
            };
            return page;
        }

        private async Task RefreshSubwooferAsync(bool quiet = false)
        {
            int version = ++_subVersion;
            if (AmpOf(_cboSubAmp) is not { } ip)
            {
                _lblSubStatus.Text = string.Empty;
                SetRows(_subRows, [Muted(_owner._knownDevices.Count == 0 ? "Add a WiiM device on the Devices page first."
                    : _subSection == SectionHighPass ? "None of your devices has a high-pass filter."
                    : "None of your devices has a subwoofer output or can pair a WiiM subwoofer.")]);
                return;
            }
            if (!quiet) _lblSubStatus.Text = "Loading…";
            var (reachable, sub, highPass, wiimSubs, wired) = await _owner.GetSubwooferAsync(ip);
            if (version != _subVersion) return;
            _lblSubStatus.Text = reachable ? string.Empty : "Not reachable";
            var state = new SubwooferState(ip, reachable, sub, highPass, wiimSubs, wired);
            if (reachable) _subCache = state;
            SetRows(_subRows, SubwooferCards(state));
        }

        private List<Control> SubwooferCards(SubwooferState state)
        {
            if (!state.Reachable) return [Muted("Couldn't reach this device.")];
            const string noSubOut = "This device has no subwoofer output. The WiiM Amp, Amp Pro, Amp Ultra and Ultra have one.";
            string? waiting = state.SubAvailable ? null
                : state.WiimSubs is { Count: > 0 } ? "Your WiiM subwoofer is offline. These settings can be changed again when it's back."
                : "This device has no subwoofer output of its own. These settings are used with a WiiM subwoofer: pair one under Sub out to change them.";
            if (_subSection == SectionHighPass)
            {
                if (state.HighPass == null) return [Muted("This device doesn't have a high-pass filter.")];
                bool subActive = state.SubAvailable && state.Sub is { Enabled: true, Plugged: true };
                return [BuildHighPassCard(state.Ip, state.HighPass, subActive)];
            }
            List<Control> cards;
            if (!state.WiredOutput && state.WiimSubs != null)
            {
                cards = !state.SubAvailable
                    ? [Muted(state.WiimSubs.Count > 0
                        ? "Your WiiM subwoofer is offline. Its settings are shown here again when it's back."
                        : "This device has no subwoofer output of its own, but it can use a wireless WiiM Sub Pro. Pair one below; " +
                          "its settings are shown here once it's connected.")]
                    : state.Sub != null ? [BuildSubwooferCard(state.Ip, state.Sub)] : [];
            }
            else cards = state.Sub != null ? WithWaiting(BuildSubwooferCard(state.Ip, state.Sub), waiting) : [Muted(noSubOut)];
            if (state.WiimSubs != null) cards.Add(BuildWiimSubCard(state.Ip, state.WiimSubs, state.WiredOutput));
            return cards;
        }

        private static List<Control> WithWaiting(Control card, string? waiting)
        {
            if (waiting == null) return [card];
            card.IsEnabled = false;
            card.Opacity = 0.5;
            return [Muted(waiting), card];
        }

        private async Task CheckSubwooferDevicesAsync()
        {
            var devices = _owner._knownDevices.ToList();
            if (devices.All(d => _owner.SubwooferSupport(d.Ip) != null && _owner.HighPassSupport(d.Ip) != null)) return;
            await Task.WhenAll(devices.Select(d => _owner.CheckSubwooferSupportAsync(d.Ip)));
            FillSubwooferCombo(followActive: false);
        }

        private void FillSubwooferCombo(bool followActive)
        {
            var devices = _owner._knownDevices
                .Where(d => (_subSection == SectionHighPass ? _owner.HighPassSupport(d.Ip) : _owner.SubwooferSupport(d.Ip)) != false).ToList();
            string? selected = (followActive ? null : AmpOf(_cboSubAmp)) ?? _owner._deviceIp;
            if (!devices.Any(d => d.Ip == selected))
                selected = devices.Any(d => d.Ip == _owner._deviceIp) ? _owner._deviceIp : devices.Select(d => d.Ip).FirstOrDefault();
            bool wasLoading = _loading;
            _loading = true;
            _cboSubAmp.Items.Clear();
            foreach (var d in devices) _cboSubAmp.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == selected);
            _cboSubAmp.SelectedIndex = index >= 0 ? index : _cboSubAmp.Items.Count > 0 ? 0 : -1;
            _loading = wasLoading;
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
                    : "Stops the amplifier sending the deepest bass to the main speakers, which they can't play anyway. They play cleaner and " +
                      "stay better controlled when the music gets loud.")
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
