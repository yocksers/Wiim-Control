using System.Globalization;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly string[] StandbySourceInputs = ["HDMI", "optical", "line-in", "line-in2", "co-axial", "phono"];

        private Border? BuildInputSettingsCard(string ip, InputSettings s, List<InputVisibility>? inputs)
        {
            var rows = new List<Control>();

            if (s.PreGainEnabled is bool preGainOn && s.PreGains.Count > 0)
            {
                var gains = s.PreGains.ToList();
                var toggle = new ToggleSwitch { IsChecked = preGainOn };
                toggle.IsCheckedChanged += async (_, _) => await ChangeInputsAsync(() => _owner.SetPreGainAsync(ip, toggle.IsChecked == true, gains));
                var row = ToggleRow("Pre-gain", toggle);
                ToolTip.SetTip(row, "Balance the level of the inputs, so they all play about as loud. Raising it too much can distort.");
                rows.Add(row);
                if (preGainOn)
                    foreach (var gain in gains.ToList())
                    {
                        var slider = AmpSlider((int)Math.Round(s.MinGain * 2), (int)Math.Round(s.MaxGain * 2), (int)Math.Round(gain.Gain * 2),
                            v => (v / 2.0).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB", true, out var sliderRow);
                        slider.ValueCommitted += async (_, _) =>
                        {
                            int index = gains.FindIndex(g => g.Mode == gain.Mode);
                            gains[index] = gain with { Gain = slider.Value / 2.0 };
                            await ChangeInputsAsync(() => _owner.SetPreGainAsync(ip, true, gains));
                        };
                        rows.Add(LabeledRow($"   {_owner.InputName(ip, gain.Name)}", sliderRow));
                    }
            }

            foreach (var (input, value) in new[] { ("optical", s.OpticalAutoSense), ("HDMI", s.HdmiAutoSense), ("line-in", s.LineInAutoSense) })
            {
                if (value is not bool on) continue;
                if (inputs != null && !inputs.Any(i => i.Mode == input)) continue;
                string mode = input;
                var row = InputToggle($"Auto-sensing of {_owner.InputName(ip, mode)}", on, enabled => _owner.SetAutoSenseAsync(ip, mode, enabled));
                ToolTip.SetTip(row, "Switch to this input automatically when a signal arrives on it");
                rows.Add(row);
            }

            if (s.LineInRate is int lineRate && s.LineInBits is int lineBits && (inputs == null || inputs.Any(i => i.Mode == "line-in")))
            {
                int[] rates = [44100, 48000, 96000, 192000];
                var rate = new ComboBox { Width = 120 };
                foreach (int r in rates) rate.Items.Add(ComboItem(r, (r / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz"));
                if (!rates.Contains(lineRate)) rate.Items.Add(ComboItem(lineRate, (lineRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz"));
                rate.SelectedItem = rate.Items.OfType<ComboBoxItem>().First(i => i.Tag is int v && v == lineRate);
                var depth = new ComboBox { Width = 110 };
                depth.Items.Add(ComboItem(16, "16-bit"));
                depth.Items.Add(ComboItem(32, "24-bit"));
                depth.SelectedIndex = lineBits >= 24 ? 1 : 0;
                async Task Apply()
                {
                    if ((rate.SelectedItem as ComboBoxItem)?.Tag is int r && (depth.SelectedItem as ComboBoxItem)?.Tag is int b && (r != lineRate || b != (lineBits >= 24 ? 32 : 16)))
                        await ChangeInputsAsync(() => _owner.SetLineInResolutionAsync(ip, r, b));
                }
                rate.SelectionChanged += async (_, _) => await Apply();
                depth.SelectionChanged += async (_, _) => await Apply();
                var row = LabeledRow("Line-in input resolution", new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { rate, depth }
                });
                ToolTip.SetTip(row, "The sample rate and bit depth the line input is recorded at");
                rows.Add(row);
            }

            if (s.TvRemoteStep is int tvStep)
            {
                var slider = AmpSlider(1, 10, Math.Clamp(tvStep, 1, 10), v => v.ToString(), false, out var sliderRow);
                slider.ValueCommitted += async (_, _) => await ChangeInputsAsync(() => _owner.SetTvRemoteStepAsync(ip, slider.Value));
                var row = LabeledRow("TV remote volume step", sliderRow);
                ToolTip.SetTip(row, "How much the volume changes per press of the TV remote's volume button (HDMI ARC)");
                rows.Add(row);
            }

            bool hasHdmi = inputs == null || inputs.Any(i => i.Mode == "HDMI");
            if (s.HdmiAudioOnly is bool hdmiOnly && hasHdmi)
            {
                var row = InputToggle("Use HDMI audio only in HDMI mode", hdmiOnly, on => _owner.SetHdmiAudioOnlyAsync(ip, on));
                ToolTip.SetTip(row, "The device only acts as the TV's speaker when it's already in HDMI mode");
                rows.Add(row);
            }
            if (s.TvSpeakers is bool tvSpeakers && hasHdmi)
            {
                var row = InputToggle("Use TV speakers when HDMI is inactive", tvSpeakers, on => _owner.SetTvSpeakersAsync(ip, on));
                bool available = s.HdmiAudioOnly != false;
                row.IsEnabled = available;
                row.Opacity = available ? 1 : 0.45;
                ToolTip.SetShowOnDisabled(row, true);
                ToolTip.SetTip(row, available
                    ? "When the device switches away from HDMI, the TV turns its own speakers on"
                    : "Switch on “Use HDMI audio only in HDMI mode” to use this");
                rows.Add(row);
            }

            if (s.StandbySourceEnabled is bool standbyOn)
            {
                var sources = (inputs?.Select(i => i.Mode) ?? []).Where(m => StandbySourceInputs.Contains(m)).ToList();
                if (s.StandbySource is { } current && !sources.Contains(current)) sources.Insert(0, current);
                if (sources.Count > 0)
                {
                    var combo = new ComboBox { Width = 260 };
                    combo.Items.Add(ComboItem(string.Empty, "Keep current (default)"));
                    foreach (var source in sources) combo.Items.Add(ComboItem(source, _owner.InputName(ip, source)));
                    combo.SelectedIndex = standbyOn && s.StandbySource is { } active ? sources.IndexOf(active) + 1 : 0;
                    combo.SelectionChanged += async (_, _) =>
                    {
                        if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string source) return;
                        await ChangeInputsAsync(() => _owner.SetStandbySourceAsync(ip, source.Length > 0 ? source : null));
                    };
                    var row = LabeledRow("Standby mode source", combo);
                    ToolTip.SetTip(row, "The input that still plays while the device is in standby, so sound from it starts at once");
                    rows.Add(row);
                }
            }

            if (s.PerSourceVolume is bool perSource)
            {
                var row = InputToggle("Per-source volume control", perSource, on => _owner.SetPerSourceVolumeAsync(ip, on));
                ToolTip.SetTip(row, "Remember a separate volume for each input");
                rows.Add(row);
            }

            return rows.Count > 0 ? Card("Input settings", rows.ToArray()) : null;
        }
    }
}
