using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class InputsPage
        {
            private const int SceneNameLength = 40;

            private Border BuildInputScenesCard(string ip, RoutineContext scenes, List<InputVisibility> inputs)
            {
                var rows = new List<Control>
                {
                    Muted("A scene applies a group, a volume and a subwoofer setting when you switch to an input.")
                };
                foreach (var input in inputs)
                {
                    string mode = input.Mode;
                    var routine = scenes.Routines.FirstOrDefault(r => r["bindInput"]?.ToString() == mode);
                    bool set = routine != null && HasSceneSteps(routine);
                    var edit = Pill(set ? "Edit" : "Set up");
                    edit.VerticalAlignment = VerticalAlignment.Center;
                    ToolTip.SetTip(edit, $"Choose what happens when you switch to {window._owner.InputName(ip, mode)}");
                    edit.Click += async (_, _) => await EditSceneAsync(ip, scenes, mode, routine);
                    DockPanel.SetDock(edit, Dock.Right);
                    rows.Add(new DockPanel
                    {
                        Margin = new Thickness(0, 4),
                        Children =
                        {
                            edit,
                            new StackPanel
                            {
                                Margin = new Thickness(0, 0, 12, 0),
                                Children =
                                {
                                    new TextBlock { Text = window._owner.InputName(ip, mode) },
                                    new TextBlock
                                    {
                                        Text = set ? SceneSummary(scenes, routine!) : "Not set", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap
                                    }
                                }
                            }
                        }
                    });
                }
                return Card("Input scenes", rows.ToArray());
            }

            private static string SceneSummary(RoutineContext scenes, JsonObject routine)
            {
                var parts = new List<string>();
                if (routine["name"]?.ToString() is { Length: > 0 } name) parts.Add(name);
                if (scenes.HasEnabledSwitch && NodeInt(routine["enabled"]) == 0) parts.Add("Off");
                parts.Add(PlayOnText(scenes, SceneStep(routine, "nameGroup")));
                if (SceneStep(routine, "volume") is { } volume && volume["type"]?.ToString() == "custom" && NodeInt(volume["vol"]) is int vol)
                    parts.Add($"Volume {vol}");
                if (NodeInt(SceneStep(routine, "subwoofer")?["subOutput"]) is int sub) parts.Add(sub == 1 ? "Subwoofer on" : "Subwoofer off");
                return string.Join("  ·  ", parts);
            }

            private static string PlayOnText(RoutineContext scenes, JsonObject? group)
            {
                if (!scenes.PicksSavedGroup)
                {
                    var names = (group?["slavelist"] as JsonArray)?.OfType<JsonObject>().Select(s => s["name"]?.ToString()).OfType<string>().ToList() ?? [];
                    return names.Count == 0 ? $"Plays on {scenes.DeviceName}" : $"Plays on {scenes.DeviceName} + {string.Join(" + ", names)}";
                }
                if (group == null) return "Plays on the current group";
                if (group["group_id"]?.ToString() is not { Length: > 0 }) return $"Plays on {scenes.DeviceName} only";
                return $"Plays on {group["group_name"]?.ToString() ?? "a group"}";
            }

            private async Task EditSceneAsync(string ip, RoutineContext scenes, string input, JsonObject? routine)
            {
                bool set = routine != null && HasSceneSteps(routine);
                string inputName = window._owner.InputName(ip, input);
                var existingGroup = SceneStep(routine, "nameGroup");
                var existingVolume = SceneStep(routine, "volume");
                int? existingSub = NodeInt(SceneStep(routine, "subwoofer")?["subOutput"]);

                var nameBox = new TextBox
                {
                    Width = FieldWidth, MaxLength = SceneNameLength, Watermark = "For example TV",
                    Text = set ? routine!["name"]?.ToString() ?? string.Empty : string.Empty
                };
                var content = new StackPanel { Width = 480, Spacing = 2 };
                content.Children.Add(new TextBlock { Text = $"Input scene for {inputName}", Classes = { "heading" }, Margin = new Thickness(0, 0, 0, 4) });
                content.Children.Add(Muted($"Applied automatically when {scenes.DeviceName} switches to {inputName}."));
                content.Children.Add(LabeledRow("Name", nameBox));

                ToggleSwitch? enabled = null;
                if (scenes.HasEnabledSwitch)
                {
                    enabled = new ToggleSwitch { IsChecked = routine == null || NodeInt(routine["enabled"]) is null or 1 };
                    content.Children.Add(ToggleRow("Enabled", enabled));
                }

                var playOn = window.AddPlayOnPicker(content, ip, scenes, existingGroup,
                    "Choose where to play when switching to this input. Groups come from the saved groups that include this device.",
                    "Select devices to play in sync when switching to this input");
                var volume = AddCustomVolume(content, scenes, existingVolume,
                    existingVolume ?? (scenes.FixedVolume ? new JsonObject { ["type"] = "fixed", ["vol"] = scenes.MaxVolume } : null));
                var subwoofer = AddSubOutput(content, scenes, existingSub);

                if (!await window.ShowRoutineEditorAsync(content, () => nameBox.Text?.Trim() is { Length: > 0 } ? null : "Give the scene a name.")) return;
                string name = nameBox.Text!.Trim();
                var group = playOn();
                var volumeStep = volume();
                if (volumeStep?["type"] == null) volumeStep = null;
                int? sub = subwoofer();
                await ChangeInputsAsync(() => window._owner.SaveInputSceneAsync(ip, scenes, input, name, enabled?.IsChecked != false, group, volumeStep, sub));
            }
        }
    }
}
