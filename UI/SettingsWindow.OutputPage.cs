using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboOutputAmp = new() { Width = 220 };
        private readonly TextBlock _lblOutputStatus = Subtitle();
        private readonly StackPanel _outputRows = new();
        private readonly SemaphoreSlim _outputGate = new(1, 1);
        private int _outputVersion;

        private Control BuildOutputPage()
        {
            ToolTip.SetTip(_cboOutputAmp, "The device whose output settings are shown");
            _cboOutputAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshOutputAsync(); };
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshOutputAsync();
            var page = Page("Output", _lblOutputStatus, [_cboOutputAmp, refresh], _outputRows);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshOutputAsync();
            };
            return page;
        }

        private async Task RefreshOutputAsync(bool quiet = false)
        {
            int version = ++_outputVersion;
            if (AmpOf(_cboOutputAmp) is not { } ip)
            {
                SetRows(_outputRows, [Muted("Add a WiiM device on the Devices page first.")]);
                return;
            }
            if (!quiet) _lblOutputStatus.Text = "Loading…";
            var page = await _owner.GetOutputPageAsync(ip);
            if (version != _outputVersion) return;
            _lblOutputStatus.Text = page.Reachable ? string.Empty : "Not reachable";
            var cards = new List<Control>();
            if (!page.Reachable) cards.Add(Muted("Couldn't reach this device."));
            else
            {
                if (page.Output is { } state) cards.Add(BuildOutputCard(ip, state));
                else if (page.OutputName is { } name) cards.Add(Card("Output", LabeledRow("Output", Info(name))));
                if (page.FilterMode is { } mode && page.Filters.Count > 0) cards.Add(BuildDigitalFilterCard(ip, mode, page.Filters, page.Filter));
                if (cards.Count == 0) cards.Add(Muted("This device has no output settings."));
            }
            SetRows(_outputRows, cards);
        }

        private Border BuildDigitalFilterCard(string ip, string mode, List<DigitalFilter> filters, int? current)
        {
            var combo = new ComboBox { Width = 360 };
            foreach (var f in filters) combo.Items.Add(ComboItem(f.Index, f.Name));
            combo.SelectedIndex = filters.FindIndex(f => f.Index == current);
            combo.SelectionChanged += async (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is int index && index != current)
                    await ChangeOutputAsync(() => _owner.SetDigitalFilterAsync(ip, mode, index));
            };
            return Card("Digital filter",
                Muted("How the device's DAC filters the sound. The choices come from the device and differ between models."),
                LabeledRow("Digital filter", combo));
        }

        private async Task ChangeOutputAsync(Func<Task<bool>> change)
        {
            await _outputGate.WaitAsync();
            bool ok;
            try
            {
                _lblOutputStatus.Text = "Saving…";
                ok = await change();
            }
            finally { _outputGate.Release(); }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
            if (_outputGate.CurrentCount == 0) return;
            await RefreshOutputAsync(quiet: true);
        }

        private Grid OutputToggle(string text, bool value, Func<bool, Task<bool>> apply)
        {
            var toggle = new ToggleSwitch { IsChecked = value };
            toggle.IsCheckedChanged += async (_, _) => await ChangeOutputAsync(() => apply(toggle.IsChecked == true));
            return ToggleRow(text, toggle);
        }
    }
}
