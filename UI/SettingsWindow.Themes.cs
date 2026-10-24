using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly StackPanel _themeRows = new();
        private bool _themesHooked;

        private static readonly (string Label, string Tip, Func<Theme, string> Get, Func<Theme, string, Theme> Set)[] ThemeColors =
        [
            ("Window background", "The colour behind everything", t => t.Window, (t, v) => t with { Window = v }),
            ("Menu", "The menu on the left", t => t.Sidebar, (t, v) => t with { Sidebar = v }),
            ("Cards", "The boxes that hold the settings", t => t.Card, (t, v) => t with { Card = v }),
            ("Buttons and fields", "Buttons, lists and text fields", t => t.Control, (t, v) => t with { Control = v }),
            ("Text", "The main text colour", t => t.Text, (t, v) => t with { Text = v }),
            ("Secondary text", "Descriptions and less important text", t => t.Muted, (t, v) => t with { Muted = v }),
            ("Accent", "Switches, sliders, selections and highlighted buttons", t => t.Accent, (t, v) => t with { Accent = v })
        ];

        private Control BuildThemesPage()
        {
            var page = Page("Themes", null, [], _themeRows);
            page.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && page.IsVisible) RebuildThemes(); };
            if (!_themesHooked)
            {
                ThemeEngine.Changed += () => { if (page.IsVisible) RebuildThemes(); };
                _themesHooked = true;
            }
            return page;
        }

        private static IBrush BrushOf(string hex) => new SolidColorBrush(ThemeEngine.ColorOf(hex, Colors.Gray));

        private Control ThemeTile(Theme theme, bool active)
        {
            var accent = new Border { Width = 34, Height = 10, CornerRadius = new CornerRadius(5), Background = BrushOf(theme.Accent) };
            var lines = new StackPanel
            {
                Spacing = 5, Margin = new Thickness(8),
                Children =
                {
                    new TextBlock { Text = "Aa", FontWeight = FontWeight.Bold, Foreground = BrushOf(theme.Text) },
                    new Border { Height = 4, Width = 60, CornerRadius = new CornerRadius(2), Background = BrushOf(theme.Muted), HorizontalAlignment = HorizontalAlignment.Left },
                    accent
                }
            };
            var card = new Border { Background = BrushOf(theme.Card), CornerRadius = new CornerRadius(6), Margin = new Thickness(6), Child = lines };
            var sidebar = new Border { Width = 26, Background = BrushOf(theme.Sidebar), CornerRadius = new CornerRadius(6, 0, 0, 6) };
            var preview = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            preview.Children.Add(sidebar);
            Grid.SetColumn(card, 1);
            preview.Children.Add(card);
            var frame = new Border
            {
                Width = 150, Height = 96, CornerRadius = new CornerRadius(8), Background = BrushOf(theme.Window), ClipToBounds = true, Child = preview,
                BorderThickness = new Thickness(3), BorderBrush = active ? Palette.Accent : Brushes.Transparent
            };
            var caption = new TextBlock
            {
                Text = theme.Name, Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = active ? Palette.AccentText : Palette.TextMuted
            };
            var tile = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 10), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                Children = { frame, caption }
            };
            ToolTip.SetTip(tile, active ? $"{theme.Name} (in use)" : $"Use {theme.Name}");
            tile.Tapped += (_, _) => { if (!active) _owner.UseTheme(theme); };
            return tile;
        }

        private void EditTheme(Func<Theme, Theme> change)
        {
            var current = ThemeEngine.Current;
            var updated = change(current);
            if (current.BuiltIn)
            {
                _owner.SaveTheme(updated with { Name = _owner.FreeThemeName("My theme") });
                return;
            }
            _owner.SaveTheme(updated, current.Name);
        }

        private Control ThemeColorButton(string label, string hex, Func<string, Theme> apply)
        {
            var swatch = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = BrushOf(hex),
                BorderBrush = Palette.TextDim, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { swatch, new TextBlock { Text = hex.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center } }
                }
            };
            button.Classes.Add("pill");
            button.Click += async (_, _) =>
            {
                if (await PickColorAsync(hex.TrimStart('#'), label) is { } picked) EditTheme(_ => apply("#" + picked.ToUpperInvariant()));
            };
            return button;
        }

        private void RebuildThemes()
        {
            var current = ThemeEngine.Current;
            var tiles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var theme in _owner.AllThemes) tiles.Children.Add(ThemeTile(theme, theme.Name == current.Name));
            var cards = new List<Control> { Card("Themes", Muted("Click a theme to use it."), tiles) };

            var rows = new List<Control>();
            if (current.BuiltIn)
                rows.Add(Muted($"{current.Name} is built in. Changing it saves your changes as a new theme."));
            foreach (var (label, tip, get, set) in ThemeColors)
            {
                var row = LabeledRow(label, ThemeColorButton(label, get(current), value => set(ThemeEngine.Current, value)));
                ToolTip.SetTip(row, tip);
                rows.Add(row);
            }
            var roundness = AmpSlider(ThemeEngine.MinRoundness, ThemeEngine.MaxRoundness, current.Roundness, v => $"{v}%", false, out var roundnessRow);
            roundness.ValueCommitted += (_, _) =>
            {
                if (roundness.Value != ThemeEngine.Current.Roundness) EditTheme(t => t with { Roundness = roundness.Value });
            };
            var roundnessLabel = LabeledRow("Rounded corners", roundnessRow);
            ToolTip.SetTip(roundnessLabel, "How round the corners of cards, buttons and fields are. 100% is the normal look.");
            rows.Add(roundnessLabel);

            var saveAs = Pill("Save as new theme");
            saveAs.Click += async (_, _) =>
            {
                string? name = await Dialogs.PromptAsync(this, "Name for the new theme:", _owner.FreeThemeName(ThemeEngine.Current.Name), _scale);
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                if (_owner.ThemeNameTaken(name))
                {
                    await Dialogs.MessageAsync(this, $"There's already a theme called {name}. Choose another name.", _scale);
                    return;
                }
                _owner.SaveTheme(ThemeEngine.Current with { Name = name });
            };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0), Children = { saveAs } };
            if (!current.BuiltIn)
            {
                var rename = Pill("Rename");
                rename.Click += async (_, _) =>
                {
                    string old = ThemeEngine.Current.Name;
                    string? name = await Dialogs.PromptAsync(this, "New name for the theme:", old, _scale);
                    if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) return;
                    name = name.Trim();
                    if (_owner.ThemeNameTaken(name, old))
                    {
                        await Dialogs.MessageAsync(this, $"There's already a theme called {name}. Choose another name.", _scale);
                        return;
                    }
                    _owner.SaveTheme(ThemeEngine.Current with { Name = name }, old);
                };
                var delete = Pill("Delete");
                delete.Click += async (_, _) =>
                {
                    string name = ThemeEngine.Current.Name;
                    if (!await Dialogs.ConfirmAsync(this, $"Delete the theme {name}? Wiim Control switches back to Dark.", _scale)) return;
                    _owner.DeleteTheme(name);
                };
                actions.Children.Add(rename);
                actions.Children.Add(delete);
            }
            rows.Add(actions);
            cards.Add(Card($"Edit {current.Name}", rows.ToArray()));
            SetRows(_themeRows, cards);
        }
    }
}
