using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class InputsPage(SettingsWindow window)
        {
            private readonly ComboBox _cboInputsAmp = new() { Width = DevicePickerWidth };
            private readonly TextBlock _lblInputsStatus = Subtitle();
            private readonly StackPanel _inputsRows = new();
            private readonly SemaphoreSlim _inputsGate = new(1, 1);
            private int _inputsVersion;

            public void FillDevices() => window.FillAmpCombo(_cboInputsAmp);

            public Control Build()
            {
                ToolTip.SetTip(_cboInputsAmp, "The device whose input settings are shown");
                _cboInputsAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshInputsAsync(); };
                var refresh = Pill("Refresh");
                refresh.Click += async (_, _) => await RefreshInputsAsync();
                var page = window.Page("Inputs", _lblInputsStatus, [_cboInputsAmp, refresh], _inputsRows);
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshInputsAsync();
                };
                return page;
            }

            public async Task RefreshInputsAsync(bool quiet = false)
            {
                int version = ++_inputsVersion;
                if (AmpOf(_cboInputsAmp) is not { } ip)
                {
                    SetRows(_inputsRows, [Muted("Add a WiiM device on the Devices page first.")]);
                    return;
                }
                if (!quiet) _lblInputsStatus.Text = "Loading…";
                var scenesTask = window._owner.GetInputScenesAsync(ip);
                var page = await window._owner.GetInputsPageAsync(ip);
                var scenes = await scenesTask;
                if (version != _inputsVersion) return;
                _lblInputsStatus.Text = page.Reachable ? string.Empty : "Not reachable";
                var cards = new List<Control>();
                if (!page.Reachable) cards.Add(Muted("Couldn't reach this device."));
                else
                {
                    if (page.Inputs is { Count: > 0 } inputs) cards.Add(BuildInputsCard(ip, inputs));
                    if (scenes != null && page.Inputs is { Count: > 0 } sceneInputs) cards.Add(BuildInputScenesCard(ip, scenes, sceneInputs));
                    if (page.Options is { } options && BuildInputSettingsCard(ip, options, page.Inputs) is { } settingsCard) cards.Add(settingsCard);
                    if (cards.Count == 0) cards.Add(Muted("This device has no input settings."));
                }
                SetRows(_inputsRows, cards);
            }

            private Border BuildInputsCard(string ip, List<InputVisibility> inputs)
            {
                var rows = new List<Control>
                {
                    Muted("Choose which inputs are shown when you pick an input or an EQ input. This is saved on the device, " +
                          "so the WiiM Home app shows the same inputs.")
                };
                foreach (var input in inputs)
                {
                    string mode = input.Mode;
                    if (mode == "wifi")
                    {
                        rows.Add(LabeledRow(window._owner.InputName(ip, mode), Info("Always shown")));
                        continue;
                    }
                    rows.Add(InputToggle(window._owner.InputName(ip, mode), input.Shown, async shown =>
                    {
                        bool ok = await window._owner.SetInputShownAsync(ip, mode, shown);
                        if (ok && ip == window._eq.EqIp) _ = window._eq.RefreshEqAsync();
                        return ok;
                    }));
                }
                return Card("Inputs", rows.ToArray());
            }

            private async Task ChangeInputsAsync(Func<Task<bool>> change)
            {
                await _inputsGate.WaitAsync();
                bool ok;
                try
                {
                    _lblInputsStatus.Text = "Saving…";
                    ok = await change();
                }
                finally { _inputsGate.Release(); }
                if (!ok) await Dialogs.MessageAsync(window, "The device didn't accept the change.", window._scale);
                if (_inputsGate.CurrentCount == 0) return;
                await RefreshInputsAsync(quiet: true);
            }

            private Grid InputToggle(string text, bool value, Func<bool, Task<bool>> apply)
            {
                var toggle = new ToggleSwitch { IsChecked = value };
                toggle.IsCheckedChanged += async (_, _) => await ChangeInputsAsync(() => apply(toggle.IsChecked == true));
                return ToggleRow(text, toggle);
            }
        }
    }
}
