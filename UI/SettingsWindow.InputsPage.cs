using Avalonia;
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
                var onlineArtwork = new ToggleSwitch { IsChecked = window._owner.OnlineArtwork };
                onlineArtwork.IsCheckedChanged += (_, _) => window._owner.OnlineArtwork = onlineArtwork.IsChecked == true;
                UpdateComputerProblem();
                var computerCard = Card("From this computer",
                    Muted("Enable artwork for music played on computer."),
                    _computerInputRows,
                    _computerProblem,
                    Muted("Enable looking up higher resolution artwork from external sites"),
                    ToggleRow("Look up artwork online", onlineArtwork),
                    Muted("Warning: when this is on, the artist, title and album of what this computer plays, or the channel and title of " +
                          "a video, are sent to Deezer and Apple to find the cover."));
                var page = window.Page("Inputs", _lblInputsStatus, [_cboInputsAmp, refresh], new StackPanel { Children = { _inputsRows, computerCard } });
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property != IsVisibleProperty || !page.IsVisible) return;
                    _ = RefreshComputerInputsAsync();
                    await RefreshInputsAsync();
                };
                return page;
            }

            private readonly StackPanel _computerInputRows = new();
            private readonly TextBlock _computerProblem = new()
            {
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Palette.AccentText, Margin = new Thickness(0, 4, 0, 8), IsVisible = false
            };

            public void UpdateComputerProblem()
            {
                string? problem = window._owner.ComputerPlaybackProblem;
                _computerProblem.Text = problem ?? string.Empty;
                _computerProblem.IsVisible = problem != null;
            }
            private int _computerInputsVersion;

            private static readonly string[] ComputerInputOrder = ["line-in", "line-in2", "bluetooth", "optical", "co-axial", "hdmi", "phono", "pcusb"];

            private async Task RefreshComputerInputsAsync()
            {
                int version = ++_computerInputsVersion;
                var owner = window._owner;
                var devices = owner._knownDevices.ToList();
                if (devices.Count == 0)
                {
                    SetRows(_computerInputRows, [Muted("Add a WiiM device on the Devices page first.")]);
                    return;
                }
                var outputsTask = Task.Run(ComputerOutputs.List);
                var inputs = await Task.WhenAll(devices.Select(d => owner.GetAllInputsAsync(d.Ip)));
                var outputs = await outputsTask;
                if (version != _computerInputsVersion) return;
                var modes = inputs.Where(list => list != null).SelectMany(list => list!).Select(input => PlainInput(input.Mode))
                    .Where(mode => mode != "wifi" && mode != "udisk").ToHashSet();
                if (modes.Count == 0) modes.UnionWith(["line-in", "bluetooth", "optical", "co-axial", "hdmi"]);
                modes.UnionWith(owner._computerInputs);
                var rows = new List<Control>();
                foreach (string mode in modes.OrderBy(m => Array.IndexOf(ComputerInputOrder, m) is int i and >= 0 ? i : int.MaxValue).ThenBy(m => m))
                {
                    var toggle = new ToggleSwitch { IsChecked = owner.IsComputerInput(mode) };
                    var row = ToggleRow(InputLabel(mode), toggle);
                    ToolTip.SetTip(row, $"{InputLabel(mode)} is connected to this computer, so show what this computer is playing");
                    rows.Add(row);
                    if (outputs.Count == 0)
                    {
                        toggle.IsCheckedChanged += (_, _) => owner.SetComputerInput(mode, toggle.IsChecked == true);
                        continue;
                    }
                    var output = new ComboBox { Width = WideFieldWidth };
                    output.Items.Add(ComboItem(string.Empty, "Any sound output"));
                    foreach (var (id, name) in outputs) output.Items.Add(ComboItem(id, name));
                    string? saved = owner.ComputerOutputFor(mode);
                    if (saved != null && !outputs.Any(o => string.Equals(o.Id, saved, StringComparison.OrdinalIgnoreCase)))
                        output.Items.Add(ComboItem(saved, "An output that isn't connected now"));
                    output.SelectedItem = output.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => string.Equals(i.Tag as string, saved ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    output.SelectionChanged += (_, _) => owner.SetComputerOutput(mode, (output.SelectedItem as ComboBoxItem)?.Tag as string);
                    var outputRow = LabeledRow($"{InputLabel(mode)} gets this computer's sound from", output);
                    outputRow.Margin = new Thickness(16, 0, 0, 6);
                    ToolTip.SetTip(outputRow, $"Only show and control this computer's music while this sound output is the computer's default. " +
                                              "With Any sound output it's always shown.");
                    outputRow.IsVisible = toggle.IsChecked == true;
                    toggle.IsCheckedChanged += (_, _) =>
                    {
                        owner.SetComputerInput(mode, toggle.IsChecked == true);
                        outputRow.IsVisible = toggle.IsChecked == true;
                    };
                    rows.Add(outputRow);
                }
                SetRows(_computerInputRows, rows);
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
                    if (mode == "wifi") continue;
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
