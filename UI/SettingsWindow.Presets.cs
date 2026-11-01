using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class PresetsPage(SettingsWindow window)
        {
            private readonly ComboBox _cboPresetAmp = new() { Width = DevicePickerWidth };
            private readonly TextBlock _lblPresetStatus = Subtitle();
            private readonly WrapPanel _presetTiles = new();
            private readonly List<Image> _presetImages = [];
            private readonly TextBlock _presetHint = new()
            {
                Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), IsVisible = false
            };

            private string? SelectedPresetAmpIp => (_cboPresetAmp.SelectedItem as ComboBoxItem)?.Tag as string;

            public bool ShowsImage(Bitmap bitmap) => _presetImages.Any(i => ReferenceEquals(i.Source, bitmap));

            public Control Build()
            {
                _cboPresetAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshPresetsAsync(); };
                var btnRefresh = Pill("Refresh");
                btnRefresh.Click += async (_, _) => await RefreshPresetsAsync();
                var body = new StackPanel
                {
                    Children =
                    {
                        _presetHint,
                        _presetTiles,
                        Muted("Click a preset to play it, or to run a routine (for example switching the output and EQ). Use the buttons under " +
                              "a preset to save what's playing to it, rename it or clear it. Routines are made in the WiiM Home app.")
                    }
                };
                return window.Page("Presets", _lblPresetStatus, [_cboPresetAmp, btnRefresh], body);
            }

            public void LoadPresetAmps()
            {
                string selected = (window._followActive ? null : SelectedPresetAmpIp) ?? window._owner._deviceIp;
                _cboPresetAmp.Items.Clear();
                foreach (var d in window._owner._knownDevices) _cboPresetAmp.Items.Add(ComboItem(d.Ip, d.Name));
                int index = window._owner._knownDevices.FindIndex(d => d.Ip == selected);
                _cboPresetAmp.SelectedIndex = index >= 0 ? index : _cboPresetAmp.Items.Count > 0 ? 0 : -1;
            }

            public async Task RefreshPresetsAsync()
            {
                string? ip = SelectedPresetAmpIp;
                if (ip == null)
                {
                    _lblPresetStatus.Text = string.Empty;
                    _presetTiles.Children.Clear();
                    _presetHint.Text = "Add a WiiM device on the Devices page first.";
                    _presetHint.IsVisible = true;
                    return;
                }
                _lblPresetStatus.Text = "Loading…";
                var presets = await window._owner.GetPresetsAsync(ip);
                if (ip != SelectedPresetAmpIp) return;

                _presetTiles.Children.Clear();
                _presetImages.Clear();
                if (presets == null)
                {
                    _lblPresetStatus.Text = "Not reachable";
                    _presetHint.Text = "Couldn't reach this device.";
                    _presetHint.IsVisible = true;
                    return;
                }
                _lblPresetStatus.Text = $"{presets.Presets.Count} of {presets.Slots} saved";
                _presetHint.Text = "No presets are saved on this device yet. Play a station or playlist and use the + button under a preset to save it.";
                _presetHint.IsVisible = presets.Presets.Count == 0;
                for (int number = 1; number <= presets.Slots; number++)
                    _presetTiles.Children.Add(BuildPresetTile(ip, number, presets.Presets.FirstOrDefault(p => p.Number == number)));
            }

            private Control BuildPresetTile(string ip, int number, PresetInfo? preset)
            {
                var art = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
                _presetImages.Add(art);
                var placeholder = new PathIcon
                {
                    Data = preset?.IsRoutine == true ? Icons.Settings : Icons.Music, Width = 34, Height = 34,
                    Foreground = preset?.IsRoutine == true ? Palette.AccentText : Palette.TextDim,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var picture = new Border
                {
                    Width = 146, Height = 146, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Palette.Input,
                    Child = new Panel { Children = { placeholder, art } }
                };
                var text = new StackPanel
                {
                    Margin = new Thickness(0, 8, 0, 0),
                    Children =
                    {
                        new TextBlock { Text = $"Preset {number}", Classes = { "dim", "small" } },
                        new TextBlock
                        {
                            Text = preset?.Name ?? "Empty", Classes = { preset == null ? "muted" : "heading" },
                            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
                        }
                    }
                };
                if (preset is { Source.Length: > 0 })
                    text.Children.Add(new TextBlock { Text = SourceLabel(preset.Source), Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
                if (preset is { Detail.Length: > 0 })
                    text.Children.Add(new TextBlock
                    {
                        Text = preset.Detail, Classes = { "dim", "small" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 48,
                        Margin = new Thickness(0, 2, 0, 0)
                    });

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(-8, 6, 0, -6) };
                Button Action(StreamGeometry icon, string tip, Func<Task> run)
                {
                    var (button, path) = IconButton(icon);
                    path.Width = path.Height = 16;
                    button.Width = button.Height = 30;
                    button.Padding = new Thickness(0);
                    ToolTip.SetTip(button, tip);
                    button.Click += async (_, _) => await run();
                    actions.Children.Add(button);
                    return button;
                }
                Action(Icons.Plus, preset == null ? "Save what's playing to this preset" : "Replace this preset with what's playing",
                    () => SavePlayingToPresetAsync(ip, number, preset));
                if (preset is { IsRoutine: false }) Action(Icons.Edit, "Rename this preset", () => RenamePresetAsync(ip, preset));
                if (preset != null) Action(Icons.Delete, "Clear this preset", () => ClearPresetAsync(ip, preset));
                text.Children.Add(actions);

                var tile = new Border
                {
                    Width = 170, Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12),
                    Background = Palette.Card, Opacity = preset == null ? 0.7 : 1,
                    Child = new StackPanel { Children = { picture, text } }
                };
                if (preset == null) return tile;

                tile.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(tile, preset.IsRoutine ? $"Run the routine {preset.Name}" : $"Play {preset.Name}");
                tile.PointerEntered += (_, _) => tile.Background = Palette.Input;
                tile.PointerExited += (_, _) => tile.Background = Palette.Card;
                tile.Tapped += async (_, e) =>
                {
                    if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null) return;
                    _lblPresetStatus.Text = preset.IsRoutine ? $"Running {preset.Name}…" : $"Playing {preset.Name}…";
                    if (!await window._owner.PlayPresetOnAsync(window.TargetOf(ip), number))
                        await Dialogs.MessageAsync(window, "The device didn't start the preset.", window._scale);
                    await Task.Delay(1500);
                    if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
                };
                if (preset.PictureUrl.Length > 0) _ = LoadPresetArtAsync(art, placeholder, preset.PictureUrl);
                return tile;
            }

            private async Task SavePlayingToPresetAsync(string ip, int number, PresetInfo? existing)
            {
                var item = await window._owner.GetPlayingItemAsync(window.TargetOf(ip));
                if (item == null)
                {
                    await Dialogs.MessageAsync(window, "Only music the device plays from its own queue can be saved, for example radio stations, the Library " +
                        "page or USB and network music. Music from Spotify, TIDAL Connect, AirPlay, Cast and the inputs can't be saved here.", window._scale);
                    return;
                }
                if (existing != null && !await Dialogs.ConfirmAsync(window, $"Replace preset {number} ({existing.Name}) with {item.Title}?", window._scale)) return;
                string? name = await Dialogs.PromptAsync(window, $"Name for preset {number}:", item.Title, window._scale);
                if (string.IsNullOrWhiteSpace(name)) return;
                await ChangePresetAsync(() => window._owner.SavePresetAsync(ip, number, item, name.Trim()), $"Saving preset {number}…");
            }

            private async Task RenamePresetAsync(string ip, PresetInfo preset)
            {
                string? name = await Dialogs.PromptAsync(window, $"New name for preset {preset.Number}:", preset.Name, window._scale);
                if (string.IsNullOrWhiteSpace(name) || name.Trim() == preset.Name) return;
                await ChangePresetAsync(() => window._owner.RenamePresetAsync(ip, preset.Number, name.Trim()), $"Renaming preset {preset.Number}…");
            }

            private async Task ClearPresetAsync(string ip, PresetInfo preset)
            {
                if (!await Dialogs.ConfirmAsync(window, $"Clear preset {preset.Number} ({preset.Name})?", window._scale)) return;
                await ChangePresetAsync(() => window._owner.ClearPresetAsync(ip, preset.Number), $"Clearing preset {preset.Number}…");
            }

            private async Task ChangePresetAsync(Func<Task<bool>> change, string status)
            {
                _lblPresetStatus.Text = status;
                _presetTiles.IsEnabled = false;
                bool ok = await change();
                _presetTiles.IsEnabled = true;
                if (!ok) await Dialogs.MessageAsync(window, "The device didn't save the change.", window._scale);
                await RefreshPresetsAsync();
            }

            private async Task LoadPresetArtAsync(Image art, PathIcon placeholder, string url)
            {
                var bitmap = await window._owner.GetArtworkAsync(url);
                if (bitmap == null) return;
                art.Source = bitmap;
                art.IsVisible = true;
                placeholder.IsVisible = false;
            }
        }
    }
}
