using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private sealed record DropdownChoice(string Text, bool Selected, bool Enabled, Func<Task> Pick, bool Heading = false);

        private static DropdownChoice DropdownHeading(string text) => new(text, false, false, () => Task.CompletedTask, Heading: true);

        private static DropdownChoice DropdownAction(string text, Func<Task> pick, bool selected = false) => new(text, selected, true, pick);

        private void ShowDropdown(Control anchor, IReadOnlyList<DropdownChoice> choices)
        {
            var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, FlyoutPresenterClasses = { "dropdown" } };
            var list = new ListBox { MinWidth = Math.Max(200, anchor.Bounds.Width), Background = Brushes.Transparent };
            foreach (var choice in choices)
            {
                var item = new ListBoxItem
                {
                    Content = choice.Heading ? new TextBlock { Text = choice.Text, Classes = { "muted", "small" }, FontWeight = FontWeight.SemiBold } : choice.Text,
                    IsEnabled = choice.Enabled, Focusable = !choice.Heading
                };
                item.Tapped += async (_, _) =>
                {
                    flyout.Hide();
                    await choice.Pick();
                };
                list.Items.Add(item);
                if (choice.Selected) list.SelectedItem = item;
            }
            flyout.Content = ScaledPopup(list);
            flyout.ShowAt(anchor);
        }
    }
}
