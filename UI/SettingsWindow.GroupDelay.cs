using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboDelayAmp = new() { Width = DevicePickerWidth };
        private readonly StackPanel _delayRows = new();
        private Border? _delayCard;
        private int _delayVersion;
        private readonly StackPanel _syncRows = new();
        private Border? _syncCard;

        private Border GroupDelayCard()
        {
            if (_delayCard != null) return _delayCard;
            ToolTip.SetTip(_cboDelayAmp, "The device whose group audio delay is shown");
            _cboDelayAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshGroupDelayAsync(); };
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
            if (AmpOf(_cboDelayAmp) is not { } ip)
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
            double step = micro ? 0.1 : 1;
            string Format(double ms) => (micro ? ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(ms).ToString(
                System.Globalization.CultureInfo.InvariantCulture)) + " ms";
            var box = new NumberBox(-SyncDelayLimitMs, SyncDelayLimitMs, Format, (ms, direction) => Math.Round(ms + step * direction, 1))
            {
                Width = 140, Value = current
            };
            box.ValueChanged += async (_, _) =>
            {
                if (!await _owner.SetSyncDelayAsync(ip, box.Value, micro))
                    await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
                await RefreshGroupDelayAsync();
            };
            var row = LabeledRow("Extra delay", box);
            ToolTip.SetTip(row, "A positive value plays this device later, a negative value plays it earlier. " +
                                 $"From {-SyncDelayLimitMs:0} to {SyncDelayLimitMs:0} ms; use the arrow keys to nudge it.");
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
