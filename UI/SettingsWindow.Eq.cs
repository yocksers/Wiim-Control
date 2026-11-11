using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private sealed partial class EqPage(SettingsWindow window)
        {
            private const int EqBandCount = 10;
            private static readonly string[] EqBandLabels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
            private static readonly string[] EqBandParams =
                ["band31hz", "band63hz", "band125hz", "band250hz", "band500hz", "band1khz", "band2khz", "band4khz", "band8khz", "band16khz"];
            private readonly ToggleSwitch _chkEqEnabled = new();
            private readonly ComboBox _cboEqPreset = new() { Width = 240, PlaceholderText = "Custom (not saved)" };
            private readonly TextBlock _lblEqSource = Subtitle();
            private readonly WrapPanel _eqInputPills = new() { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 4) };
            private Control? _eqInputsRow;
            private readonly ComboBox _cboEqAmp = new() { Width = DevicePickerWidth };
            private readonly FitRow _eqPresetChips = new();
            private string? _eqInputChoice;
            private int _eqRefreshVersion;
            private string _eqDeviceIp = string.Empty;
            private bool _eqLegacy;
            private readonly EqSlider[] _eqSliders = new EqSlider[EqBandCount];
            private readonly TextBlock[] _eqValueLabels = new TextBlock[EqBandCount];
            private readonly List<Control> _eqEditControls = [];
            private readonly DispatcherTimer _eqSendTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
            private readonly HashSet<int> _eqDirtyBands = [];
            private List<EqBand> _eqBands = [];
            private bool _eqAvailable;
            private bool _eqLoading;
            private readonly SemaphoreSlim _eqFlushGate = new(1, 1);
            private readonly HashSet<string> _eqCustomPresets = new();
            private readonly HashSet<string> _eqBuiltInPresets = new(StringComparer.OrdinalIgnoreCase);
            private string _eqSource = string.Empty;
            private string _eqPluginUri = DefaultEqPlugin;
            private readonly Button _btnEqSaveCurrent = Pill("Save");
            private readonly Button _btnEqSave = Pill("Save as");
            private string? _eqBasePreset;
            private string _eqBaseKey = string.Empty;
            private readonly Button _btnEqRename = Pill("Rename");
            private readonly Button _btnEqDelete = Pill("Delete");
            private bool _eqBuilt;

            public Control Build()
            {
                var btnRefresh = Pill("Refresh");
                btnRefresh.Click += async (_, _) => await RefreshEqAsync();

                _btnEqSaveCurrent.Click += async (_, _) => await SaveCurrentEqPresetAsync();
                _btnEqSave.Click += async (_, _) => await SaveEqPresetAsync();
                _btnEqRename.Click += async (_, _) => await RenameEqPresetAsync();
                _btnEqDelete.Click += async (_, _) => await DeleteEqPresetAsync();
                HookEqFileButtons();
                HookQuickPresetButton();
                var presetButtons = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _btnEqSaveCurrent, _btnEqSave, _btnEqRename, _btnEqDelete, _btnEqExport, _btnEqImport, _btnEqQuick }
                };
                var presetPicker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 2) };
                _eqPresetChips.VerticalAlignment = VerticalAlignment.Center;
                _eqPresetChips.Margin = new Thickness(0, 0, 8, 0);
                presetPicker.Children.Add(_eqPresetChips);
                Grid.SetColumn(_cboEqPreset, 1);
                _cboEqPreset.VerticalAlignment = VerticalAlignment.Center;
                presetPicker.Children.Add(_cboEqPreset);
                _cboEqPreset.SelectionChanged += (_, _) => UpdatePresetChips();
                _eqTypeRow = BuildEqTypeSwitch();
                var eqRow = LabeledRow("Enable EQ", _chkEqEnabled);
                _chkEqEnabled.VerticalAlignment = VerticalAlignment.Center;
                _eqInputPills.HorizontalAlignment = HorizontalAlignment.Right;
                _eqInputsRow = LabeledRow("Inputs", _eqInputPills);
                _eqInputsRow.IsVisible = false;
                var topCard = Card(null, eqRow, _eqInputsRow, LabeledRow("Preset", presetButtons), presetPicker);

                var table = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", EqBandCount))),
                    RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                    MinHeight = 200
                };
                for (int i = 0; i < EqBandCount; i++)
                {
                    int band = i;
                    var slider = new EqSlider { Minimum = 2, Maximum = 98, Value = 50, Center = 50, Step = 2, Margin = new Thickness(0, 4, 0, 6) };
                    var lblValue = new TextBlock { Text = FormatEqDb(50), HorizontalAlignment = HorizontalAlignment.Center };
                    var lblFreq = new TextBlock { Text = EqBandLabels[i], Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                    slider.ValueChanged += (_, _) => OnEqSliderChanged(band);
                    _eqSliders[i] = slider;
                    _eqValueLabels[i] = lblValue;
                    Grid.SetColumn(slider, i);
                    Grid.SetColumn(lblValue, i);
                    Grid.SetRow(lblValue, 1);
                    Grid.SetColumn(lblFreq, i);
                    Grid.SetRow(lblFreq, 2);
                    table.Children.Add(slider);
                    table.Children.Add(lblValue);
                    table.Children.Add(lblFreq);
                }

                var eqCard = new Border { Child = table, Transitions = [FadeIn()] };
                _geqEditor = eqCard;
                _peqEditor = BuildPeqEditor();
                _peqEditor.Transitions = [FadeIn()];

                var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
                body.Children.Add(topCard);
                var editorGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
                editorGrid.Children.Add(_eqTypeRow);
                var editors = new Panel { Children = { eqCard, _peqEditor } };
                Grid.SetRow(editors, 1);
                editorGrid.Children.Add(editors);
                var editorCard = new Border { Child = editorGrid, Margin = new Thickness(0) };
                editorCard.Classes.Add("card");
                Grid.SetRow(editorCard, 1);
                body.Children.Add(editorCard);

                _eqEditControls.Add(_cboEqPreset);
                _eqEditControls.AddRange(_eqSliders);

                _chkEqEnabled.IsCheckedChanged += async (_, _) =>
                {
                    if (_eqLoading) return;
                    UpdateEqEnabledState();
                    bool ok = _chkEqEnabled.IsChecked == true
                        ? await window._owner.SetEqTypeAsync(_eqDeviceIp, EqTarget, _eqPluginUri)
                        : await window._owner.TurnEqOffAsync(_eqDeviceIp, EqTarget, _eqPluginUri);
                    if (!ok) await RefreshEqAsync();
                };
                _cboEqPreset.SelectionChanged += async (_, _) =>
                {
                    if (_eqLoading || (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                    _eqSendTimer.Stop();
                    _eqDirtyBands.Clear();
                    _peqSendTimer.Stop();
                    _peqDirty.Clear();
                    if (_eqLegacy) await window._owner.LoadEqPresetAsync(_eqDeviceIp, name);
                    else await window._owner.LoadSourcePresetAsync(_eqDeviceIp, _eqSource, _eqPluginUri, name);
                    await RefreshEqAsync();
                };

                _chkEqEnabled.IsEnabled = false;
                UpdateEqEnabledState();
                ToolTip.SetTip(_cboEqAmp, "The device whose EQ is shown");
                _cboEqAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshEqAsync(); };
                _eqSendTimer.Tick += async (_, _) => await FlushEqBandsAsync();
                window.Closed += (_, _) =>
                {
                    _eqSendTimer.Stop();
                    _peqSendTimer.Stop();
                };
                _eqBuilt = true;
                return window.Page("Equalizer", _lblEqSource, [_cboEqAmp, btnRefresh], body, scroll: false);
            }

            private static Avalonia.Animation.DoubleTransition FadeIn() => new() { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) };

            private static string FormatEqDb(int value) =>
                ((value - 50) / 4.0).ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.InvariantCulture);

            private string? SelectedEqPreset => (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag as string;

            private string EqTarget => _eqLegacy ? string.Empty : _eqSource;

            public string? EqInputChoice => _eqInputChoice;

            public string EqDeviceIp => _eqDeviceIp;

            public string EqIp => AmpOf(_cboEqAmp) ?? window._owner._deviceIp;

            public void FillEqAmpCombo()
            {
                string? chosen = AmpOf(_cboEqAmp);
                string selected = chosen == null || window._followActive ? window._owner._deviceIp : chosen;
                _cboEqAmp.Items.Clear();
                foreach (var d in window._owner._knownDevices) _cboEqAmp.Items.Add(ComboItem(d.Ip, d.Name));
                int index = window._owner._knownDevices.FindIndex(d => d.Ip == selected);
                _cboEqAmp.SelectedIndex = index >= 0 ? index : _cboEqAmp.Items.Count > 0 ? 0 : -1;
            }

            private void UpdateEqEnabledState()
            {
                bool editable = _eqAvailable && _chkEqEnabled.IsChecked == true;
                foreach (var c in _eqEditControls) c.IsEnabled = editable;
                _eqPresetChips.IsEnabled = editable;
                _btnGraphicEq.IsEnabled = _btnParametricEq.IsEnabled = _eqInputPills.IsEnabled = _eqAvailable;
                UpdatePeqRowStates();
                bool customSelected = SelectedEqPreset is { } name && _eqCustomPresets.Contains(name);
                _btnEqSave.IsEnabled = _btnEqSaveCurrent.IsEnabled = editable;
                _btnEqRename.IsEnabled = _btnEqDelete.IsEnabled = editable && customSelected;
                UpdateEqFileButtons();
                ToolTip.SetTip(_btnEqSaveCurrent, BaseCustomPreset is { } preset
                    ? $"Save the changes to \"{preset}\""
                    : "Save the current EQ as a new preset");
            }

            private void OnEqSliderChanged(int band)
            {
                _eqValueLabels[band].Text = FormatEqDb(_eqSliders[band].Value);
                if (_eqLoading) return;
                _eqDirtyBands.Add(band);
                if (!_eqSendTimer.IsEnabled) _eqSendTimer.Start();
                MarkEqCustom();
            }

            private void MarkEqCustom()
            {
                if (_cboEqPreset.SelectedIndex >= 0)
                {
                    _eqLoading = true;
                    _cboEqPreset.SelectedIndex = -1;
                    _eqLoading = false;
                    UpdateEqEnabledState();
                }
            }

            public async Task<bool> SendPendingEqBandsAsync() => await FlushEqBandsAsync() & await FlushPeqAsync();

            private async Task<bool> IsValidPresetNameAsync(string name)
            {
                string? problem =
                    name.Length > 30 ? "Please use at most 30 characters." :
                    name.IndexOfAny([':', '"', '\\', '{', '}']) >= 0 ? "Please don't use : \" \\ { or } in the name." :
                    _eqBuiltInPresets.Contains(name) ? $"\"{name}\" is a built-in preset. Please choose another name." :
                    null;
                if (problem != null) await Dialogs.MessageAsync(window, problem, window._scale);
                return problem == null;
            }

            private string? BaseCustomPreset => _eqBasePreset is { } name && _eqCustomPresets.Contains(name) ? name : null;

            private async Task SaveCurrentEqPresetAsync()
            {
                if (BaseCustomPreset is not { } name)
                {
                    await SaveEqPresetAsync();
                    return;
                }
                await SendPendingEqBandsAsync();
                if (!await window._owner.SaveEqPresetAsync(_eqDeviceIp, name, EqTarget, _eqPluginUri, replacing: true))
                    await Dialogs.MessageAsync(window, "The device didn't save the preset.", window._scale);
                await RefreshEqAsync();
            }

            private async Task SaveEqPresetAsync()
            {
                string suggestion = BaseCustomPreset ?? string.Empty;
                string? name = await Dialogs.PromptAsync(window, "Save the current EQ on the device as a preset named:", suggestion, window._scale);
                if (string.IsNullOrEmpty(name) || !await IsValidPresetNameAsync(name)) return;
                bool replacing = _eqCustomPresets.Contains(name);
                if (replacing && !await Dialogs.ConfirmAsync(window, $"Replace the preset \"{name}\" with the current EQ?", window._scale)) return;

                await SendPendingEqBandsAsync();
                if (!await window._owner.SaveEqPresetAsync(_eqDeviceIp, name, EqTarget, _eqPluginUri, replacing))
                    await Dialogs.MessageAsync(window, "The device didn't save the preset.", window._scale);
                await RefreshEqAsync();
            }

            private async Task RenameEqPresetAsync()
            {
                if (SelectedEqPreset is not { } oldName || !_eqCustomPresets.Contains(oldName)) return;
                string? name = await Dialogs.PromptAsync(window, $"New name for \"{oldName}\":", oldName, window._scale);
                if (string.IsNullOrEmpty(name) || name == oldName || !await IsValidPresetNameAsync(name)) return;
                if (_eqCustomPresets.Contains(name))
                {
                    await Dialogs.MessageAsync(window, $"There is already a preset called \"{name}\".", window._scale);
                    return;
                }
                if (!await window._owner.RenameEqPresetAsync(_eqDeviceIp, oldName, name, _eqPluginUri))
                    await Dialogs.MessageAsync(window, "The device didn't rename the preset.", window._scale);
                else if (_eqBasePreset == oldName) _eqBasePreset = name;
                await RefreshEqAsync();
            }

            private async Task DeleteEqPresetAsync()
            {
                if (SelectedEqPreset is not { } name || !_eqCustomPresets.Contains(name)) return;
                if (!await Dialogs.ConfirmAsync(window, $"Delete the preset \"{name}\" from the device?", window._scale)) return;
                if (!await window._owner.DeleteEqPresetAsync(_eqDeviceIp, name, _eqPluginUri))
                    await Dialogs.MessageAsync(window, "The device didn't delete the preset.", window._scale);
                await RefreshEqAsync();
            }

            private async Task<bool> FlushEqBandsAsync()
            {
                _eqSendTimer.Stop();
                await _eqFlushGate.WaitAsync();
                try
                {
                    while (_eqDirtyBands.Count > 0)
                    {
                        var indices = _eqDirtyBands.Order().ToList();
                        var bands = indices.Select(i => new EqBand(i, EqBandParam(i), _eqSliders[i].Value)).ToList();
                        _eqDirtyBands.Clear();
                        bool ok = _eqLegacy
                            ? await window._owner.SetEqBandsAsync(_eqDeviceIp, bands)
                            : await window._owner.SetGeqBandsAsync(_eqDeviceIp, _eqSource, bands);
                        if (ok) continue;
                        foreach (int index in indices) _eqDirtyBands.Add(index);
                        return false;
                    }
                    return true;
                }
                finally { _eqFlushGate.Release(); }
            }

            private string EqBandParam(int index)
            {
                var known = _eqBands.FirstOrDefault(b => b.Index == index).ParamName;
                return string.IsNullOrEmpty(known) ? EqBandParams[index] : known;
            }

            public async Task RefreshEqAsync()
            {
                if (!_eqBuilt) return;
                await FlushEqBandsAsync();
                await FlushPeqAsync();
                int version = ++_eqRefreshVersion;
                _lblEqSource.Text = "Loading…";
                string ip = EqIp;
                string? inputChoice = ip == _eqDeviceIp ? _eqInputChoice : null;
                var currentTask = window._owner.GetEqStateAsync(ip);
                var modesTask = window._owner.GetEqSourceModesAsync(ip);
                var inputsTask = window._owner.GetInputsAsync(ip);
                await Task.WhenAll(currentTask, modesTask, inputsTask);
                var current = currentTask.Result;
                var modes = current is { Source.Length: > 0 } ? modesTask.Result : null;
                bool legacy = modes == null;
                string currentInput = current?.Source ?? string.Empty;
                var shownInputs = !legacy ? inputsTask.Result : null;
                var inputs = modes?.Select(m => m.Source)
                    .Where(i => shownInputs == null || i == currentInput || shownInputs.Contains(i, StringComparer.OrdinalIgnoreCase))
                    .ToList() ?? [];
                if (currentInput.Length > 0 && !inputs.Contains(currentInput)) inputs.Insert(0, currentInput);
                string source = !legacy && inputChoice is { } choice && inputs.Contains(choice) ? choice : currentInput;
                var mode = modes?.FirstOrDefault(m => m.Source == source);

                var stateTask = legacy ? Task.FromResult(current) : window._owner.GetGeqStateAsync(ip, source);
                var peqTask = legacy ? Task.FromResult<PeqState?>(null) : window._owner.GetPeqStateAsync(ip, source);
                await Task.WhenAll(stateTask, peqTask);
                var state = stateTask.Result;
                var peq = peqTask.Result;
                bool enabled = mode?.Enabled ?? (state?.Enabled == true || peq?.Enabled == true);
                string plugin =
                    peq == null ? DefaultEqPlugin
                    : mode != null && (mode.PluginUri == PeqPlugin || mode.PluginUri == DefaultEqPlugin) && (enabled || source != _eqSource || ip != _eqDeviceIp) ? mode.PluginUri
                    : peq.Enabled ? PeqPlugin
                    : state?.Enabled == true ? DefaultEqPlugin
                    : _eqPluginUri;
                var presets = await window._owner.GetEqPresetListAsync(ip, plugin);
                string presetName = plugin == PeqPlugin ? peq?.Name ?? string.Empty : state?.Name ?? string.Empty;
                if (version != _eqRefreshVersion) return;

                _eqDeviceIp = ip;
                _eqInputChoice = inputChoice;
                _eqLoading = true;
                _eqLegacy = legacy;
                _eqAvailable = state != null;
                _eqSource = source;
                _eqPluginUri = plugin;
                string baseKey = $"{ip}|{source}|{plugin}";
                if (presetName.Length > 0) _eqBasePreset = presetName;
                else if (baseKey != _eqBaseKey) _eqBasePreset = null;
                _eqBaseKey = baseKey;
                window.FillInputPills(_eqInputPills, inputs, source, currentInput, input => window._owner.InputName(ip, input), name => $"EQ for {name}", async input =>
                {
                    if (_eqLoading) return;
                    _eqInputChoice = input;
                    await RefreshEqAsync();
                });
                if (_eqInputsRow != null) _eqInputsRow.IsVisible = !legacy && inputs.Count > 0;
                if (_eqTypeRow != null) _eqTypeRow.IsVisible = peq != null;
                _eqCustomPresets.Clear();
                _eqCustomPresets.UnionWith(presets.Custom);
                _eqBuiltInPresets.Clear();
                _eqBuiltInPresets.UnionWith(presets.BuiltIn);
                _cboEqPreset.Items.Clear();
                foreach (var p in presets.Custom) _cboEqPreset.Items.Add(ComboItem(p, $"{p}  (custom)"));
                foreach (var p in presets.BuiltIn) _cboEqPreset.Items.Add(ComboItem(p, p));
                SetPresetLists(presets.Custom, presets.BuiltIn);
                if (state != null)
                {
                    var names = presets.Custom.Concat(presets.BuiltIn).ToList();
                    if (presetName.Length > 0 && !names.Contains(presetName))
                    {
                        _cboEqPreset.Items.Add(ComboItem(presetName, presetName));
                        names.Add(presetName);
                    }
                    _cboEqPreset.SelectedIndex = presetName.Length > 0 ? names.IndexOf(presetName) : -1;
                    _chkEqEnabled.IsChecked = enabled;
                    _eqBands = state.Bands;
                    foreach (var b in state.Bands.Where(b => b.Index >= 0 && b.Index < EqBandCount))
                    {
                        var slider = _eqSliders[b.Index];
                        slider.Value = Math.Clamp(b.Value, slider.Minimum, slider.Maximum);
                    }
                    _lblEqSource.Text = legacy && state.Source.Length > 0 ? $"Input: {window._owner.InputName(ip, state.Source)}" : string.Empty;
                }
                else
                {
                    _lblEqSource.Text = "EQ unavailable";
                }
                ApplyPeqState(peq);
                ShowEqEditor();
                _eqLoading = false;

                _chkEqEnabled.IsEnabled = _eqAvailable;
                UpdateEqEnabledState();
            }
        }
    }
}
