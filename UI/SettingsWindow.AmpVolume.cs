using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboVolumeAmp = new() { Width = 240 };
        private readonly StackPanel _ampVolumeRows = new();
        private readonly SemaphoreSlim _ampVolumeGate = new(1, 1);
        private int _ampVolumeVersion;
        private readonly Panel _dynamicBassHost = new();

        private Border BuildAmpVolumeCard(Control page)
        {
            ToolTip.SetTip(_cboVolumeAmp, "The device whose audio settings are shown. They're saved on the device.");
            _cboVolumeAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshAudioSettingsAsync(); };
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshAudioSettingsAsync();
            };
            return Card("Device volume", _ampVolumeRows);
        }

        private async Task RefreshAmpVolumeAsync()
        {
            int version = ++_ampVolumeVersion;
            if (AmpOf(_cboVolumeAmp) is not { } ip)
            {
                SetRows(_ampVolumeRows, [Muted("Add a WiiM device on the Devices page first.")]);
                return;
            }
            if (_ampVolumeRows.Children.Count == 0) SetRows(_ampVolumeRows, [Muted("Loading…")]);
            var settingsTask = _owner.GetVolumeSettingsAsync(ip);
            var bassTask = _owner.GetDynamicBassAsync(ip);
            await Task.WhenAll(settingsTask, bassTask);
            if (version != _ampVolumeVersion) return;
            var settings = settingsTask.Result;
            ShowDynamicBass(ip, bassTask.Result);
            var rows = settings == null ? [Muted("Couldn't reach this device.")] : AmpVolumeRows(ip, settings);
            if (rows.Count == 0) rows.Add(Muted("This device has no volume settings."));
            SetRows(_ampVolumeRows, rows);
        }

        private void ShowDynamicBass(string ip, DynamicBassInfo? bass)
        {
            _dynamicBassHost.Children.Clear();
            if (bass == null) return;
            var slider = AmpSlider(bass.Min, bass.Max, bass.Level, v => v > 0 ? $"+{v} dB" : $"{v} dB", false, out var row);
            slider.ValueCommitted += async (_, _) =>
            {
                int level = Math.Clamp(bass.Min + (int)Math.Round((slider.Value - bass.Min) / (double)bass.Step) * bass.Step, bass.Min, bass.Max);
                if (!await _owner.SetDynamicBassAsync(ip, level))
                    await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
                await RefreshAmpVolumeAsync();
            };
            _dynamicBassHost.Children.Add(Card("Dynamic Bass", LabeledRow("Bass boost", row),
                Muted("Extra bass from the device's own speakers. RoomFit switches it off while it measures and turns it back on afterwards.")));
        }

        private List<Control> AmpVolumeRows(string ip, AmpSettings s)
        {
            var rows = new List<Control>();
            if (s.MaxVolume is int maxVolume)
            {
                var slider = AmpSlider(1, 100, maxVolume, v => v.ToString(), false, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetMaxVolumeAsync(ip, slider.Value));
                rows.Add(LabeledRow("Maximum volume", row));
            }
            if (s.ButtonVolumeStep is int buttonStep)
            {
                var slider = AmpSlider(1, 10, Math.Clamp(buttonStep, 1, 10), v => v.ToString(), false, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetButtonVolumeStepAsync(ip, slider.Value));
                var label = LabeledRow("Volume step for the knob and buttons", row);
                ToolTip.SetTip(label, "How much the volume changes per click of the device's own volume knob or buttons");
                rows.Add(label);
            }
            if (s.RemoteVolumeStep is int remoteStep)
            {
                var slider = AmpSlider(1, 10, Math.Clamp(remoteStep, 1, 10), v => v.ToString(), false, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetRemoteVolumeStepAsync(ip, slider.Value));
                var label = LabeledRow("Volume step for the remote", row);
                ToolTip.SetTip(label, "How much the volume changes per press of the volume buttons on the WiiM remote");
                rows.Add(label);
            }
            if (s.AutoHeadroom is bool autoHeadroom)
            {
                var toggle = new ToggleSwitch { IsChecked = autoHeadroom };
                toggle.IsCheckedChanged += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetAutoHeadroomAsync(ip, toggle.IsChecked == true));
                var row = ToggleRow("Auto headroom", toggle);
                ToolTip.SetTip(row, "Lowers the output level automatically, based on the volume, EQ and RoomFit, to prevent clipping");
                rows.Add(row);
                if (s.HeadroomGain is double gain)
                    rows.Add(LabeledRow("Headroom adjustment", Info(gain.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB")));
            }
            if (s.Channel is int channel)
            {
                var channels = SegmentSwitch([(ChannelStereo, "Stereo"), (ChannelLeft, "Left"), (ChannelRight, "Right")],
                    channel is ChannelLeft or ChannelRight ? channel : ChannelStereo, FieldWidth,
                    chosen => ChangeAmpVolumeAsync(() => _owner.SetChannelAsync(ip, chosen)));
                var row = LabeledRow("Audio channel", channels);
                ToolTip.SetTip(row, "Which channel the device plays when it plays on its own. In a group, use the LR button on the Devices page.");
                rows.Add(row);
            }
            if (s.Balance is double balance)
            {
                var slider = AmpSlider(-100, 100, (int)Math.Round(balance * 100), BalanceText, true, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetBalanceAsync(ip, slider.Value / 100.0));
                rows.Add(LabeledRow("Balance", row));
            }
            if (s.Fade is bool fade)
            {
                var toggle = new ToggleSwitch { IsChecked = fade };
                toggle.IsCheckedChanged += async (_, _) => await ChangeAmpVolumeAsync(() => _owner.SetFadeAsync(ip, toggle.IsChecked == true));
                rows.Add(ToggleRow("Fade in and out when playback starts or stops", toggle));
            }
            return rows;
        }

        private async Task ChangeAmpVolumeAsync(Func<Task<bool>> change)
        {
            await _ampVolumeGate.WaitAsync();
            bool ok;
            try { ok = await change(); }
            finally { _ampVolumeGate.Release(); }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
            if (_ampVolumeGate.CurrentCount == 0) return;
            await RefreshAmpVolumeAsync();
        }
    }
}
