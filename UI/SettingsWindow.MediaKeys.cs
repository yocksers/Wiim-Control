using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class MediaKeysPage(SettingsWindow window)
        {
            private readonly ToggleSwitch _chkLogStep = new();
            private readonly ToggleSwitch _chkGroupVolume = new();
            private readonly Stepper _numStep = new() { Minimum = 1, Maximum = 50, HorizontalAlignment = HorizontalAlignment.Right };
            private readonly ComboBox _cboCorner = ChoiceCombo(Enum.GetValues<OsdCorner>().Select(c => ComboItem(c, OsdCornerLabel(c))));
            private readonly ComboBox _cboDuration = ChoiceCombo(DurationChoices.Select(ms => ComboItem(ms, OsdDurationLabel(ms))));
            private readonly ComboBox _cboOsdScale = ChoiceCombo(new[] { 75, 90, 100, 110, 125, 150, 175, 200, 250 }.Select(p => ComboItem(p, $"{p}%")));
            private readonly StackPanel _hotkeyList = new();
            private readonly TextBlock _lblHotkeyNote = new()
            {
                Classes = { "accent" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), IsVisible = false
            };
            private HotkeyAction? _recording;
            private readonly StackPanel _outputList = new();
            private readonly StackPanel _outputBody = new();
            private readonly ToggleSwitch _chkVolumeKeys = new();
            private readonly ToggleSwitch _chkForwardMedia = new();

            public void Load()
            {
                _numStep.Value = Math.Clamp(window._owner._volumeStep, 1, 50);
                _chkLogStep.IsChecked = window._owner._logStep;
                _chkGroupVolume.IsChecked = window._owner._groupVolumeKeys;
                _cboCorner.SelectedItem = _cboCorner.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is OsdCorner c && c == window._owner._osdCorner);
                _cboDuration.SelectedItem = _cboDuration.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int ms && ms == window._owner._osdDurationMs);
                if (_cboDuration.SelectedIndex < 0 && _cboDuration.Items.Count > 0) _cboDuration.SelectedIndex = 2;
                var osdScale = _cboOsdScale.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == window._owner._osdScalePercent);
                if (osdScale == null)
                {
                    osdScale = ComboItem(window._owner._osdScalePercent, $"{window._owner._osdScalePercent}%");
                    _cboOsdScale.Items.Add(osdScale);
                }
                _cboOsdScale.SelectedItem = osdScale;
                RebuildHotkeys();
                _chkVolumeKeys.IsChecked = window._owner._volumeKeys;
                _chkForwardMedia.IsChecked = window._owner._forwardMediaKeys;
                RefreshOutputList();
            }

            public Control Build()
            {
                var cards = new StackPanel();
                cards.Children.Add(BuildVolumeKeysCard());
                if (OperatingSystem.IsWindows()) cards.Children.Add(BuildOutputCard());
                cards.Children.Add(BuildVolumeOverlayCard());
                cards.Children.Add(BuildShortcutsCard());
                if (!OperatingSystem.IsWindows()) return window.Page("Media keys", null, [], cards);
                var refresh = Pill("Refresh");
                ToolTip.SetTip(refresh, "Reads the list of output devices again, for example after plugging in headphones or a USB sound card");
                refresh.Click += (_, _) => RefreshOutputList();
                var page = window.Page("Media keys", null, [refresh], cards);
                page.PropertyChanged += (_, e) =>
                {
                    if (e.Property != IsVisibleProperty || !page.IsVisible) return;
                    RefreshOutputList();
                };
                return page;
            }

            private Border BuildOutputCard()
            {
                var btnClear = Pill("Allow all outputs");
                btnClear.Margin = new Thickness(0, 8, 0, 0);
                btnClear.HorizontalAlignment = HorizontalAlignment.Left;
                btnClear.Click += (_, _) => { window._owner.ClearOutputDevices(); window.LoadFromOwner(); };
                _outputBody.Children.Add(Muted("Allow Wiim device volume control with keyboard on these computer outputs."));
                _outputBody.Children.Add(_outputList);
                _outputBody.Children.Add(btnClear);
                return Card("Output devices", _outputBody);
            }

            private Control[] BuildKeyToggles()
            {
                if (!OperatingSystem.IsWindows()) return [];
                _chkVolumeKeys.IsCheckedChanged += (_, _) =>
                {
                    if (window._loading) return;
                    window._owner.SetVolumeKeys(_chkVolumeKeys.IsChecked == true);
                    ApplyOutputState();
                };
                var volumeKeysRow = ToggleRow("Volume keys control the WiiM device", _chkVolumeKeys);
                ToolTip.SetTip(volumeKeysRow, "When this is off, the keyboard's volume and mute keys change this computer's volume as usual. " +
                                              "Hotkeys you set below keep working.");
                _chkForwardMedia.IsCheckedChanged += (_, _) =>
                {
                    if (window._loading) return;
                    window._owner.SetForwardMediaKeys(_chkForwardMedia.IsChecked == true);
                };
                return
                [
                    volumeKeysRow,
                    Muted("Allow keyboard volume keys to control connected Wiim products directly on the Wiim."),
                    ToggleRow("Forward media keys (play/pause/next/prev) to Wiim", _chkForwardMedia)
                ];
            }

            private void ApplyOutputState()
            {
                bool on = window._owner._volumeKeys;
                _outputBody.IsEnabled = on;
                _outputBody.Opacity = on ? 1 : 0.45;
            }

            private void RefreshOutputList()
            {
                if (!OperatingSystem.IsWindows()) return;
                ApplyOutputState();
                var toggles = new List<Control>();
                foreach (var (id, name) in ActiveOutputDevices())
                {
                    var toggle = new ToggleSwitch { IsChecked = window._owner.IsOutputEnabled(id) };
                    string deviceId = id;
                    toggle.IsCheckedChanged += (_, _) => { if (!window._loading) window._owner.SetOutputDeviceEnabled(deviceId, toggle.IsChecked == true); };
                    toggles.Add(ToggleRow(name, toggle));
                }
                if (toggles.Count == 0) toggles.Add(Muted("No playback devices found"));
                SetRows(_outputList, toggles);
            }

            private Border BuildVolumeKeysCard()
            {
                _numStep.ValueChanged += (_, _) => { if (window._loading) return; window._owner.SetVolumeStep(_numStep.Value); };
                _chkLogStep.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetLogStep(_chkLogStep.IsChecked == true); };
                _chkGroupVolume.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetGroupVolumeKeys(_chkGroupVolume.IsChecked == true); };
                return Card("Volume keys",
                [
                    .. BuildKeyToggles(),
                    LabeledRow("Step size", _numStep),
                    ToggleRow("Logarithmic step (finer at low volume)", _chkLogStep),
                    ToggleRow("Volume keys also change linked devices", _chkGroupVolume)
                ]);
            }

            private Border BuildVolumeOverlayCard()
            {
                _cboCorner.SelectionChanged += (_, _) =>
                {
                    if (window._loading) return;
                    if ((_cboCorner.SelectedItem as ComboBoxItem)?.Tag is OsdCorner c) window._owner.SetOsdCorner(c);
                };
                _cboDuration.SelectionChanged += (_, _) =>
                {
                    if (window._loading) return;
                    if ((_cboDuration.SelectedItem as ComboBoxItem)?.Tag is int ms) window._owner.SetOsdDuration(ms);
                };
                _cboOsdScale.SelectionChanged += (_, _) =>
                {
                    if (window._loading || (_cboOsdScale.SelectedItem as ComboBoxItem)?.Tag is not int percent) return;
                    window._owner.SetOsdScale(percent);
                    var preview = window._volumes.TryGetValue(window._owner._deviceIp, out var current) ? current : new DeviceVolume(50, false);
                    window._owner.ShowVolumeOsd(preview.Volume, preview.Muted);
                };
                return Card("Volume overlay", LabeledRow("Position", _cboCorner), LabeledRow("Duration", _cboDuration),
                    LabeledRow("Size", _cboOsdScale));
            }

            private Border BuildShortcutsCard()
            {
                window.AddHandler(KeyDownEvent, OnRecordKeyDown, RoutingStrategies.Tunnel);
                window._owner.HotkeysChanged += RebuildHotkeys;
                window.Closed += (_, _) =>
                {
                    window._owner.HotkeysChanged -= RebuildHotkeys;
                    if (_recording != null) _ = window._owner.ApplyHotkeysAsync();
                };
                return Card("Shortcuts",
                    Muted($"Click a shortcut and press a key with Ctrl, Alt, Shift or {Hotkey.MetaName}, or an F-key on its own (Esc cancels). " +
                          "They work in every program, also when this window is closed."),
                    _lblHotkeyNote, _hotkeyList);
            }

            private void RebuildHotkeys()
            {
                string? note = window._owner.HotkeyNote;
                _lblHotkeyNote.Text = note ?? string.Empty;
                _lblHotkeyNote.IsVisible = note != null;

                var rows = new List<Control>();
                foreach (var (action, label, _) in HotkeyActions)
                {
                    var a = action;
                    bool hasKey = window._owner._hotkeys.TryGetValue(action, out var hotkey);
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = label } } };
                    if (hasKey && window._owner._failedHotkeys.Contains(action))
                        text.Children.Add(new TextBlock
                        {
                            Text = "Couldn't be set. Another program may already use this shortcut.",
                            Classes = { "small" }, Foreground = new SolidColorBrush(Color.Parse("#FF8A80")), TextWrapping = TextWrapping.Wrap
                        });

                    bool recordingThis = _recording == action;
                    var shortcut = Pill(recordingThis ? "Press keys…" : hasKey ? hotkey.Display : "Not set", accent: recordingThis);
                    shortcut.MinWidth = 180;
                    shortcut.HorizontalContentAlignment = HorizontalAlignment.Center;
                    shortcut.Margin = new Thickness(12, 0, 8, 0);
                    shortcut.Click += async (_, _) => await StartRecordingAsync(a);
                    var clear = Pill("Clear");
                    clear.IsEnabled = hasKey && _recording == null;
                    clear.Click += async (_, _) => await window._owner.SetHotkeyAsync(a, null);

                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4) };
                    row.Children.Add(text);
                    Grid.SetColumn(shortcut, 1);
                    row.Children.Add(shortcut);
                    Grid.SetColumn(clear, 2);
                    row.Children.Add(clear);
                    rows.Add(row);
                }
                SetRows(_hotkeyList, rows);
            }

            private async Task StartRecordingAsync(HotkeyAction action)
            {
                await Task.Yield();
                _recording = action;
                await window._owner.SuspendHotkeysAsync();
                RebuildHotkeys();
            }

            private async void OnRecordKeyDown(object? sender, KeyEventArgs e)
            {
                if (_recording is not { } action) return;
                e.Handled = true;
                if (e.Key == Key.Escape)
                {
                    _recording = null;
                    await window._owner.ApplyHotkeysAsync();
                    RebuildHotkeys();
                    return;
                }
                if (KeyMap.IsModifierKey(e.Key)) return;

                var hotkey = new Hotkey(e.Key, e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta));
                var duplicate = window._owner._hotkeys.Where(p => p.Key != action && p.Value.Equals(hotkey)).Select(p => (HotkeyAction?)p.Key).FirstOrDefault();
                string? problem =
                    !KeyMap.IsSupported(e.Key) ? "That key can't be used for a hotkey. Try a letter, number, arrow or F-key." :
                    !hotkey.HasModifier && !hotkey.IsFunctionKey ? $"Please add at least one of Ctrl, Alt, Shift or {Hotkey.MetaName}, or use an F-key." :
                    OperatingSystem.IsLinux() && e.Key is >= Key.F1 and <= Key.F12 &&
                        hotkey.Modifiers.HasFlag(KeyModifiers.Control) && hotkey.Modifiers.HasFlag(KeyModifiers.Alt)
                        ? "Ctrl + Alt + F1 to F12 are reserved by Linux for switching to text consoles. Please choose another combination." :
                    duplicate is { } other ? $"{hotkey.Display} is already used for \"{HotkeyActions.First(h => h.Action == other).Label}\"." :
                    null;
                if (problem != null)
                {
                    await Dialogs.MessageAsync(window, problem, window._scale);
                    return;
                }

                _recording = null;
                await window._owner.SetHotkeyAsync(action, hotkey);
                RebuildHotkeys();
            }
        }
    }
}
