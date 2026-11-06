using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class PresetsPage
        {
            private const int RoutineNameLength = 40;
            private const int CustomDuration = -1;
            private static readonly (int Seconds, string Text)[] DurationChoices =
                [(0, "No limit"), (900, "15 min"), (1800, "30 min"), (3600, "1 hour"), (7200, "2 hours")];

            private static ComboBox RoutineCombo() => new() { Width = FieldWidth };

            private static void Select(ComboBox combo, Func<object?, bool> match, Func<ComboBoxItem>? missing)
            {
                int index = combo.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => match(i.Tag));
                if (index < 0 && missing != null)
                {
                    combo.Items.Add(missing());
                    index = combo.Items.Count - 1;
                }
                combo.SelectedIndex = Math.Max(index, 0);
            }

            private async Task EditRoutineAsync(string ip, int number)
            {
                string status = _lblPresetStatus.Text ?? string.Empty;
                _lblPresetStatus.Text = "Loading…";
                var editor = await window._owner.GetPresetRoutineEditorAsync(ip, number);
                _lblPresetStatus.Text = status;
                if (editor == null)
                {
                    await Dialogs.MessageAsync(window, "Couldn't read the preset from the device.", window._scale);
                    return;
                }

                var context = editor.Context;
                var routine = editor.Routine;
                var content = new StackPanel { Width = 500, Spacing = 2 };
                content.Children.Add(new TextBlock
                {
                    Text = editor.MusicKey ? $"Preset {number}: {editor.KeyName}" : $"Preset {number}", Classes = { "heading" },
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4)
                });
                content.Children.Add(Muted(editor.MusicKey
                    ? "These settings are applied when the preset starts playing."
                    : "Running the preset applies these settings. Anything left on Current or Any isn't changed."));

                var nameBox = new TextBox
                {
                    Width = FieldWidth, MaxLength = RoutineNameLength, Watermark = "For example Headphones",
                    Text = routine?["name"]?.ToString() ?? string.Empty
                };
                if (!editor.MusicKey) content.Children.Add(LabeledRow("Name", nameBox));

                Func<JsonObject?>? playOn = null;
                if (context.Version >= PresetTypeVersion)
                    playOn = window.AddPlayOnPicker(content, ip, context, SceneStep(routine, "nameGroup"),
                        "Choose where this preset plays when triggered. Groups come from the saved groups that include this device.",
                        "Select devices to play in sync when this preset runs");

                string? savedInput = SceneStep(routine, "audioInput")?["input"]?.ToString();
                var input = RoutineCombo();
                input.Items.Add(ComboItem(string.Empty, "Any"));
                foreach (var mode in editor.Inputs) input.Items.Add(ComboItem(mode, window._owner.InputName(ip, mode)));
                Select(input, tag => tag as string == (savedInput ?? string.Empty),
                    savedInput is { Length: > 0 } ? () => ComboItem(savedInput, window._owner.InputName(ip, savedInput)) : null);
                var inputRow = LabeledRow("Audio input", input);
                ToolTip.SetTip(inputRow, "Choose the audio input for this preset");
                content.Children.Add(inputRow);

                var savedOutput = SceneStep(routine, "audioOutput");
                string? savedMode = savedOutput?["output"]?.ToString();
                string savedHex = savedOutput?["devHexName"]?.ToString() ?? string.Empty;
                var output = RoutineCombo();
                output.Items.Add(ComboItem(string.Empty, "Follow current output"));
                foreach (var o in editor.Outputs) output.Items.Add(ComboItem(o, o.Title));
                Select(output, tag => tag is RoutineOutput o ? o.Mode == savedMode && string.Equals(o.HexName, savedHex, StringComparison.OrdinalIgnoreCase)
                        : savedMode is not { Length: > 0 },
                    savedMode is { Length: > 0 } ? () => ComboItem(new RoutineOutput(savedMode, savedHex, RoutineOutputName(savedMode, savedHex)),
                        RoutineOutputName(savedMode, savedHex)) : null);
                var outputRow = LabeledRow("Audio output", output);
                ToolTip.SetTip(outputRow, "Choose the audio output for this preset");
                content.Children.Add(outputRow);

                var savedEq = SceneStep(routine, "EQ");
                string? savedEqName = savedEq?["name"]?.ToString();
                string savedPlugin = savedEq?["pluginURI"]?.ToString() is { Length: > 0 } plugin ? plugin : DefaultEqPlugin;
                var eq = RoutineCombo();
                eq.Items.Add(ComboItem(string.Empty, "Current"));
                foreach (var e in editor.Eqs) eq.Items.Add(ComboItem(e, e.Title));
                Select(eq, tag => tag is RoutineEq e ? e.Name == savedEqName && e.PluginUri == savedPlugin : savedEqName == null,
                    savedEqName != null ? () => ComboItem(new RoutineEq(savedEqName, savedPlugin, savedEqName), savedEqName) : null);
                content.Children.Add(LabeledRow("EQ", eq));

                var savedVolume = SceneStep(routine, "volume");
                var volume = AddCustomVolume(content, context, savedVolume, savedVolume);

                int? savedSub = NodeInt(SceneStep(routine, "subwoofer")?["subOutput"]);
                var subwoofer = context.Version >= SubwooferStepVersion ? AddSubOutput(content, context, savedSub) : () => savedSub;

                int? savedDuration = NodeInt(SceneStep(routine, "aliveTime")?["duration"]);
                var duration = RoutineCombo();
                foreach (var (choiceSeconds, text) in DurationChoices) duration.Items.Add(ComboItem(choiceSeconds, text));
                duration.Items.Add(ComboItem(CustomDuration, "Custom"));
                var minutes = new NumericUpDown
                {
                    Minimum = 1, Maximum = 1440, Increment = 5, FormatString = "0", Width = FieldWidth,
                    Value = savedDuration is > 0 ? Math.Max(1, savedDuration.Value / 60) : 45
                };
                var minutesRow = LabeledRow("   Minutes", minutes);
                int durationIndex = Array.FindIndex(DurationChoices, c => c.Seconds == (savedDuration ?? 0));
                duration.SelectedIndex = durationIndex >= 0 ? durationIndex : duration.Items.Count - 1;
                minutesRow.IsVisible = (duration.SelectedItem as ComboBoxItem)?.Tag is CustomDuration;
                duration.SelectionChanged += (_, _) => minutesRow.IsVisible = (duration.SelectedItem as ComboBoxItem)?.Tag is CustomDuration;
                var durationRow = LabeledRow("Duration", duration);
                ToolTip.SetTip(durationRow, "Stop playing after this long");
                content.Children.Add(durationRow);
                content.Children.Add(minutesRow);

                int? screenDisable = NodeInt(SceneStep(routine, "screenController")?["disable"]) ?? editor.ScreenDisable;
                ToggleSwitch? screen = null;
                if (editor.ScreenDisable != null && screenDisable is int disabled)
                {
                    screen = new ToggleSwitch { IsChecked = disabled == 0 };
                    content.Children.Add(ToggleRow("Screen", screen));
                }

                int savedLoop = NodeInt(SceneStep(routine, "loopMode")?["mode"]) ?? 4;
                var repeat = new ToggleSwitch { IsChecked = savedLoop is 0 or 1 or 2 or 5 };
                var shuffle = new ToggleSwitch { IsChecked = savedLoop is 2 or 3 or 5 };
                if (editor.MusicKey)
                {
                    content.Children.Add(ToggleRow("Repeat", repeat));
                    content.Children.Add(ToggleRow("Shuffle", shuffle));
                }

                if (!await window.ShowRoutineEditorAsync(content, () =>
                        editor.MusicKey || nameBox.Text?.Trim() is { Length: > 0 } ? null : "Give the preset a name.")) return;

                string name = editor.MusicKey ? routine?["name"]?.ToString() is { Length: > 0 } kept ? kept : editor.KeyName : nameBox.Text!.Trim();
                if (name.Length == 0) name = $"Preset {number}";
                var steps = new List<(string Type, JsonObject Payload)>();
                if (volume() is { } volumeStep && volumeStep["vol"] != null) steps.Add(("volume", volumeStep));
                if ((input.SelectedItem as ComboBoxItem)?.Tag is string { Length: > 0 } inputMode)
                    steps.Add(("audioInput", new JsonObject { ["input"] = inputMode }));
                if ((output.SelectedItem as ComboBoxItem)?.Tag is RoutineOutput chosenOutput)
                    steps.Add(("audioOutput", new JsonObject { ["output"] = chosenOutput.Mode, ["devHexName"] = chosenOutput.HexName }));
                if ((eq.SelectedItem as ComboBoxItem)?.Tag is RoutineEq chosenEq)
                {
                    var payload = new JsonObject { ["name"] = chosenEq.Name, ["pluginURI"] = chosenEq.PluginUri };
                    if (context.Version >= PresetTypeVersion) payload["enable"] = 1;
                    steps.Add(("EQ", payload));
                }
                steps.Add(("loopMode", new JsonObject
                {
                    ["mode"] = editor.MusicKey ? LoopModeOf(repeat.IsChecked == true, shuffle.IsChecked == true) : savedLoop
                }));
                int? seconds = (duration.SelectedItem as ComboBoxItem)?.Tag switch
                {
                    CustomDuration => (int)(minutes.Value ?? 1) * 60,
                    int s and > 0 => s,
                    _ => null
                };
                if (seconds != null) steps.Add(("aliveTime", new JsonObject { ["duration"] = seconds }));
                if (subwoofer() is int sub) steps.Add(("subwoofer", new JsonObject { ["subOutput"] = sub }));
                if (context.Version >= PresetTypeVersion)
                {
                    int? screenValue = screen != null ? screen.IsChecked == true ? 0 : 1 : screenDisable;
                    if (screenValue != null) steps.Add(("screenController", new JsonObject { ["disable"] = screenValue }));
                    if (playOn?.Invoke() is { } group) steps.Add(("nameGroup", group));
                }

                await ChangePresetAsync(() => window._owner.SavePresetRoutineAsync(ip, editor, name, steps), $"Saving preset {number}…");
            }
        }
    }
}
