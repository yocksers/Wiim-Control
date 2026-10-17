using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private readonly HashSet<string> _hiddenEqPresets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenPeqPresets = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> HiddenQuickPresets(bool parametric) => parametric ? _hiddenPeqPresets : _hiddenEqPresets;

    private static void ParseNameSet(HashSet<string> target, string value)
    {
        target.Clear();
        target.UnionWith(value.Split('\u0001', StringSplitOptions.RemoveEmptyEntries));
    }

    private void SetHiddenQuickPresets(bool parametric, IEnumerable<string> names)
    {
        var target = HiddenQuickPresets(parametric);
        target.Clear();
        target.UnionWith(names);
        SaveConfig();
    }

    private sealed partial class SettingsWindow
    {
        private readonly Button _btnEqQuick = Pill("Quick buttons…");
        private List<string> _eqCustomList = [];
        private List<string> _eqBuiltInList = [];

        private void HookQuickPresetButton()
        {
            ToolTip.SetTip(_btnEqQuick, "Choose which built-in presets get a quick button. Your own presets always have one.");
            _btnEqQuick.Click += async (_, _) => await ChooseQuickPresetsAsync();
        }

        private void SetPresetLists(IEnumerable<string> custom, IEnumerable<string> builtIn)
        {
            _eqCustomList = custom.ToList();
            _eqBuiltInList = builtIn.ToList();
            RebuildPresetChips();
        }

        private void RebuildPresetChips()
        {
            var hidden = _owner.HiddenQuickPresets(IsParametric);
            var names = _eqCustomList.Concat(_eqBuiltInList.Where(n => !hidden.Contains(n)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            _eqPresetChips.Children.Clear();
            foreach (var name in names)
            {
                var chip = Pill(name);
                chip.Tag = name;
                chip.MaxWidth = 180;
                chip.Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(chip, _eqCustomPresets.Contains(name) ? $"{name} (custom)" : name);
                chip.Click += (_, _) =>
                {
                    var item = _cboEqPreset.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == name);
                    if (item != null && !ReferenceEquals(_cboEqPreset.SelectedItem, item)) _cboEqPreset.SelectedItem = item;
                };
                _eqPresetChips.Children.Add(chip);
            }
            UpdatePresetChips();
        }

        private void UpdatePresetChips()
        {
            string? selected = SelectedEqPreset;
            foreach (var chip in _eqPresetChips.Children.OfType<Button>())
                chip.Classes.Set("current", chip.Tag as string == selected);
        }

        private async Task ChooseQuickPresetsAsync()
        {
            bool parametric = IsParametric;
            if (_eqBuiltInList.Count == 0)
            {
                await Dialogs.MessageAsync(this, "The device has no built-in presets for this EQ.", _scale);
                return;
            }
            var hidden = _owner.HiddenQuickPresets(parametric);
            var checks = _eqBuiltInList.Select(name => new CheckBox
            {
                Content = name, IsChecked = !hidden.Contains(name), Margin = new Thickness(0, 0, 16, 0), MinWidth = 170
            }).ToList();
            var grid = new UniformGrid { Columns = 3 };
            foreach (var check in checks) grid.Children.Add(check);

            var all = Pill("All");
            var none = Pill("None");
            all.Click += (_, _) => { foreach (var c in checks) c.IsChecked = true; };
            none.Click += (_, _) => { foreach (var c in checks) c.IsChecked = false; };

            string custom = _eqCustomList.Count == 0
                ? "You have no custom presets for this EQ yet; presets you save get a quick button automatically."
                : $"Your own presets ({string.Join(", ", _eqCustomList)}) always come first.";
            var content = new StackPanel
            {
                Spacing = 10, MaxWidth = 620,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Built-in {(parametric ? "parametric EQ" : "graphic EQ")} presets with a quick button", Classes = { "heading" }
                    },
                    new TextBlock { Text = custom, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                    new ScrollViewer { Content = grid, MaxHeight = 420 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, none } }
                }
            };
            if (!await Dialogs.ChooseAsync(this, content, "Save", _scale)) return;
            _owner.SetHiddenQuickPresets(parametric, checks.Where(c => c.IsChecked != true).Select(c => (string)c.Content!));
            if (parametric == IsParametric) RebuildPresetChips();
        }
    }
}
