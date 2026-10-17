using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboDelayAmp = new() { Width = 240 };
        private readonly StackPanel _delayRows = new();
        private Border? _delayCard;
        private int _delayVersion;

        private Border GroupDelayCard()
        {
            if (_delayCard != null) return _delayCard;
            ToolTip.SetTip(_cboDelayAmp, "The device whose group audio delay is shown");
            _cboDelayAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshGroupDelayAsync(); };
            _delayCard = Card("Group audio delay",
                Muted("How much each input is delayed when the device plays in a group, so all devices stay in sync. " +
                      "A lower delay keeps the sound in step with the picture, but too low can make some devices lose sound."),
                LabeledRow("Device", _cboDelayAmp),
                _delayRows);
            return _delayCard;
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
            var (delays, inputs) = await _owner.GetGroupDelaysAsync(ip);
            if (version != _delayVersion) return;
            SetRows(_delayRows, delays is { Count: > 0 }
                ? GroupDelayRows(ip, delays, inputs)
                : [Muted("This device has no group audio delay settings, or it couldn't be reached.")]);
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
                var combo = new ComboBox { Width = 200 };
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
