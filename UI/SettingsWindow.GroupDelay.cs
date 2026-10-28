using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly StackPanel _delayRows = new();
        private Border? _delayCard;
        private int _delayVersion;
        private readonly StackPanel _syncRows = new();
        private Border? _syncCard;

        private Border GroupDelayCard()
        {
            if (_delayCard != null) return _delayCard;
            _delayCard = Card("Group audio delay",
                Muted("How much each input is delayed when the device plays in a group, so all devices stay in sync. " +
                      "A lower delay keeps the sound in step with the picture, but too low can make some devices lose sound."),
                _delayRows);
            return _delayCard;
        }

        private Border SyncAudioCard()
        {
            _syncCard ??= Card("Sync audio",
                Muted("Fine-tunes this device's timing when it plays in a group, so it lines up with the other rooms."), _syncRows);
            _syncCard.IsVisible = false;
            return _syncCard;
        }

        private async Task RefreshGroupDelayAsync()
        {
            int version = ++_delayVersion;
            if (AmpOf(_cboVolumeAmp) is not { } ip)
            {
                SetRows(_delayRows, []);
                return;
            }
            if (_delayRows.Children.Count == 0) SetRows(_delayRows, [Muted("Loading…")]);
            var delaysTask = _owner.GetGroupDelaysAsync(ip);
            var syncTask = _owner.GetSyncAudioAsync(ip);
            var (delays, inputs) = await delaysTask;
            var sync = await syncTask;
            if (version != _delayVersion) return;
            SetRows(_delayRows, delays is { Count: > 0 } ? GroupDelayRows(ip, delays, inputs)
                : sync.LegacyGroupDelay is int legacy ? [LegacyGroupDelayRow(ip, legacy)]
                : [Muted("This device has no group audio delay settings, or it couldn't be reached.")]);
            if (_syncCard != null) _syncCard.IsVisible = sync.ExtraDelayMs != null;
            SetRows(_syncRows, sync.ExtraDelayMs is double extra ? [SyncDelayRow(ip, extra, sync.MicroDelay)] : []);
        }

        private Grid LegacyGroupDelayRow(string ip, int current)
        {
            var combo = new ComboBox { Width = FieldWidth };
            foreach (int ms in LegacyGroupDelays.Append(current).Distinct().OrderByDescending(ms => ms)) combo.Items.Add(ComboItem(ms, $"{ms} ms"));
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => i.Tag is int ms && ms == current);
            combo.SelectionChanged += async (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not int ms || ms == current) return;
                if (!await _owner.SetLegacyGroupDelayAsync(ip, ms))
                    await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
                await RefreshGroupDelayAsync();
            };
            return LabeledRow("All inputs", combo);
        }

        private Grid SyncDelayRow(string ip, double current, bool micro)
        {
            double step = micro ? 0.01 : 1;
            string Format(double ms) => (micro ? ms.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(ms).ToString(
                System.Globalization.CultureInfo.InvariantCulture)) + " ms";
            double Nudge(double ms, int direction) => Math.Round(ms + step * direction, 2);
            var box = new NumberBox(-SyncDelayLimitMs, SyncDelayLimitMs, Format, Nudge) { Value = current };
            int limit = (int)SyncDelayLimitMs;
            var slider = new LevelSlider
            {
                Minimum = -limit, Maximum = limit, FillFromCenter = true, MinWidth = 0, Value = (int)Math.Round(current)
            };
            async Task Apply(double ms)
            {
                if (!await _owner.SetSyncDelayAsync(ip, ms, micro))
                    await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
                await RefreshGroupDelayAsync();
            }
            box.ValueChanged += async (_, _) => await Apply(box.Value);
            slider.ValueChanged += (_, _) => { if (slider.IsDragging) box.Value = slider.Value; };
            slider.ValueCommitted += async (_, _) => await Apply(slider.Value);
            Button StepButton(StreamGeometry icon, int direction, string tip)
            {
                var (button, _) = IconButton(icon);
                ToolTip.SetTip(button, tip);
                button.Click += async (_, _) => await Apply(Math.Clamp(Nudge(box.Value, direction), -SyncDelayLimitMs, SyncDelayLimitMs));
                return button;
            }
            var minus = StepButton(Icons.Minus, -1, micro ? "0.01 ms earlier" : "1 ms earlier");
            var plus = StepButton(Icons.Plus, 1, micro ? "0.01 ms later" : "1 ms later");
            var steps = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = FieldGap };
            steps.Children.Add(minus);
            Grid.SetColumn(box, 1);
            steps.Children.Add(box);
            Grid.SetColumn(plus, 2);
            steps.Children.Add(plus);
            var range = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            range.Children.Add(new TextBlock { Text = $"{-limit} ms", Classes = { "muted", "small" } });
            var high = new TextBlock { Text = $"{limit} ms", Classes = { "muted", "small" }, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(high, 1);
            range.Children.Add(high);
            var reset = Pill("Reset to default");
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.IsEnabled = current != 0;
            ToolTip.SetTip(reset, "Sets the extra delay back to 0 ms");
            reset.Click += async (_, _) => await Apply(0);
            var field = new StackPanel { Width = FieldWidth, Spacing = 6, Children = { steps, slider, range, reset } };
            var row = LabeledRow("Extra delay", field);
            ToolTip.SetTip(row, "A positive value plays this device later, a negative value plays it earlier. " +
                                 $"From {-limit} to {limit} ms; use the arrow keys to nudge it.");
            return row;
        }

        private List<Control> GroupDelayRows(string ip, List<GroupDelay> delays, List<InputVisibility>? inputs)
        {
            var rows = new List<Control>();
            if (inputs == null || inputs.Any(i => i.Mode == "wifi"))
                rows.Add(LabeledRow(_owner.InputName(ip, "wifi"), Info(GroupDelayName(800))));
            var shown = inputs == null ? delays
                : inputs.Select(i => delays.FirstOrDefault(d => d.Input.Equals(i.Mode, StringComparison.OrdinalIgnoreCase))).OfType<GroupDelay>();
            foreach (var delay in shown)
            {
                string input = delay.Input;
                var combo = new ComboBox { Width = FieldWidth };
                foreach (int ms in GroupDelayLevels.Select(l => l.Milliseconds).Append(delay.Milliseconds).Distinct().OrderByDescending(ms => ms))
                    combo.Items.Add(ComboItem(ms, GroupDelayName(ms)));
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => i.Tag is int ms && ms == delay.Milliseconds);
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ComboBoxItem)?.Tag is not int ms || ms == delay.Milliseconds) return;
                    if (!await _owner.SetGroupDelayAsync(ip, input, ms))
                        await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
                    await RefreshGroupDelayAsync();
                };
                rows.Add(LabeledRow(_owner.InputName(ip, input), combo));
            }
            return rows;
        }
    }
}
