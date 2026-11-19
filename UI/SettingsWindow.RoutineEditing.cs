using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const string OnlyPlayOnChoice = "only";
        private const string CurrentPlayOnChoice = "current";

        private Func<JsonObject?> AddPlayOnPicker(Panel content, string ip, RoutineContext context, JsonObject? existingGroup, string tip, string devicesText)
        {
            if (context.PicksSavedGroup)
            {
                var playOn = new ComboBox { Width = FieldWidth };
                playOn.Items.Add(ComboItem(OnlyPlayOnChoice, $"{context.DeviceName} only"));
                playOn.Items.Add(ComboItem(CurrentPlayOnChoice, "Current group (if available)"));
                foreach (var saved in context.Groups) playOn.Items.Add(ComboItem(saved, saved.Name));
                int selected = 1;
                if (existingGroup != null)
                {
                    string? groupName = existingGroup["group_name"]?.ToString();
                    if (existingGroup["group_id"]?.ToString() is not { Length: > 0 }) selected = 0;
                    else if (context.Groups.FindIndex(g => g.Name == groupName) is int found and >= 0) selected = found + 2;
                    else
                    {
                        playOn.Items.Add(ComboItem(existingGroup, groupName ?? "Saved group"));
                        selected = playOn.Items.Count - 1;
                    }
                }
                playOn.SelectedIndex = selected;
                int initial = selected;
                var row = LabeledRow("Play on", playOn);
                ToolTip.SetTip(row, tip);
                content.Children.Add(row);
                return () =>
                {
                    if (existingGroup != null && playOn.SelectedIndex == initial) return existingGroup;
                    return (playOn.SelectedItem as ComboBoxItem)?.Tag switch
                    {
                        OnlyPlayOnChoice => new JsonObject(),
                        SavedGroup saved => _owner.SceneGroupPayload(context, ip, saved),
                        JsonObject kept => kept,
                        _ => null
                    };
                };
            }

            content.Children.Add(new TextBlock { Text = devicesText, Classes = { "muted" }, Margin = new Thickness(0, 8, 0, 4) });
            var chosen = (existingGroup?["slavelist"] as JsonArray)?.OfType<JsonObject>().Select(s => s["uuid"]?.ToString()).ToList() ?? [];
            var checks = new List<(CheckBox Check, KnownDevice Device)>();
            foreach (var device in _owner._knownDevices.Where(d => d.Ip != ip && d.Uuid.Length > 0 && !SceneUuidsMatch(d.Uuid, context.DeviceUuid)))
            {
                var check = new CheckBox { Content = device.Name, IsChecked = chosen.Any(u => SceneUuidsMatch(u, device.Uuid)) };
                checks.Add((check, device));
                content.Children.Add(check);
            }
            if (checks.Count == 0) content.Children.Add(Muted("Add another WiiM device on the Devices page to play in sync."));
            return () =>
            {
                var devices = checks.Where(c => c.Check.IsChecked == true).Select(c => c.Device).ToList();
                return devices.Count > 0 ? SceneDevicesPayload(context, existingGroup, devices) : null;
            };
        }

        private static Func<JsonObject?> AddCustomVolume(Panel content, RoutineContext context, JsonObject? existingVolume, JsonObject? untouched)
        {
            bool custom = existingVolume?["type"]?.ToString() == "custom";
            int start = custom && NodeInt(existingVolume!["vol"]) is int saved ? saved : context.CurrentVolume;
            var toggle = new ToggleSwitch { IsChecked = custom };
            var slider = AmpSlider(0, 100, Math.Clamp(start, 0, 100), v => v.ToString(), false, out var sliderRow);
            var row = LabeledRow("   Volume", sliderRow);
            row.IsVisible = custom;
            bool touched = false;
            toggle.IsCheckedChanged += (_, _) =>
            {
                touched = true;
                row.IsVisible = toggle.IsChecked == true;
            };
            content.Children.Add(ToggleRow("Set custom volume", toggle));
            content.Children.Add(row);
            return () => toggle.IsChecked == true ? new JsonObject { ["type"] = "custom", ["vol"] = slider.Value } : touched ? null : untouched;
        }

        private static Func<int?> AddSubOutput(Panel content, RoutineContext context, int? existingSub)
        {
            if (context.SubwooferOn is not bool on) return () => existingSub;
            var combo = new ComboBox { Width = FieldWidth };
            combo.Items.Add(ComboItem(-1, on ? "Current (On)" : "Current (Off)"));
            combo.Items.Add(ComboItem(1, "On"));
            combo.Items.Add(ComboItem(0, "Off"));
            combo.SelectedIndex = existingSub switch { 1 => 1, 0 => 2, _ => 0 };
            content.Children.Add(LabeledRow("Sub output", combo));
            return () => (combo.SelectedItem as ComboBoxItem)?.Tag is int choice && choice >= 0 ? choice : null;
        }

        private async Task<bool> ShowRoutineEditorAsync(Control content, Func<string?> problem)
        {
            while (true)
            {
                bool save = await Dialogs.ChooseAsync(this, content, "Save", _scale);
                (content.Parent as Panel)?.Children.Remove(content);
                if (!save) return false;
                if (problem() is not { } message) return true;
                await Dialogs.MessageAsync(this, message, _scale);
            }
        }
    }
}
