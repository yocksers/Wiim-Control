using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
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

        private Control BuildPresetsPage()
        {
            _cboPresetAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshPresetsAsync(); };
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
            return Page("Presets", _lblPresetStatus, [_cboPresetAmp, btnRefresh], body);
        }

        private void LoadPresetAmps()
        {
            string selected = (_followActive ? null : SelectedPresetAmpIp) ?? _owner._deviceIp;
            _cboPresetAmp.Items.Clear();
            foreach (var d in _owner._knownDevices) _cboPresetAmp.Items.Add(ComboItem(d.Ip, d.Name));
            int index = _owner._knownDevices.FindIndex(d => d.Ip == selected);
            _cboPresetAmp.SelectedIndex = index >= 0 ? index : _cboPresetAmp.Items.Count > 0 ? 0 : -1;
        }

        private async Task RefreshPresetsAsync()
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
            var presets = await _owner.GetPresetsAsync(ip);
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
                if (!await _owner.PlayPresetOnAsync(TargetOf(ip), number))
                    await Dialogs.MessageAsync(this, "The device didn't start the preset.", _scale);
                await Task.Delay(1500);
                if (IsVisible) await RefreshDeviceStatusesAsync();
            };
            if (preset.PictureUrl.Length > 0) _ = LoadPresetArtAsync(art, placeholder, preset.PictureUrl);
            return tile;
        }

        private async Task SavePlayingToPresetAsync(string ip, int number, PresetInfo? existing)
        {
            var item = await _owner.GetPlayingItemAsync(TargetOf(ip));
            if (item == null)
            {
                await Dialogs.MessageAsync(this, "Only music the device plays from its own queue can be saved, for example radio stations, the Library " +
                    "page or USB and network music. Music from Spotify, TIDAL Connect, AirPlay, Cast and the inputs can't be saved here.", _scale);
                return;
            }
            if (existing != null && !await Dialogs.ConfirmAsync(this, $"Replace preset {number} ({existing.Name}) with {item.Title}?", _scale)) return;
            string? name = await Dialogs.PromptAsync(this, $"Name for preset {number}:", item.Title, _scale);
            if (string.IsNullOrWhiteSpace(name)) return;
            await ChangePresetAsync(() => _owner.SavePresetAsync(ip, number, item, name.Trim()), $"Saving preset {number}…");
        }

        private async Task RenamePresetAsync(string ip, PresetInfo preset)
        {
            string? name = await Dialogs.PromptAsync(this, $"New name for preset {preset.Number}:", preset.Name, _scale);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == preset.Name) return;
            await ChangePresetAsync(() => _owner.RenamePresetAsync(ip, preset.Number, name.Trim()), $"Renaming preset {preset.Number}…");
        }

        private async Task ClearPresetAsync(string ip, PresetInfo preset)
        {
            if (!await Dialogs.ConfirmAsync(this, $"Clear preset {preset.Number} ({preset.Name})?", _scale)) return;
            await ChangePresetAsync(() => _owner.ClearPresetAsync(ip, preset.Number), $"Clearing preset {preset.Number}…");
        }

        private async Task ChangePresetAsync(Func<Task<bool>> change, string status)
        {
            _lblPresetStatus.Text = status;
            _presetTiles.IsEnabled = false;
            bool ok = await change();
            _presetTiles.IsEnabled = true;
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't save the change.", _scale);
            await RefreshPresetsAsync();
        }

        private async Task LoadPresetArtAsync(Image art, PathIcon placeholder, string url)
        {
            var bitmap = await _owner.GetArtworkAsync(url);
            if (bitmap == null) return;
            art.Source = bitmap;
            art.IsVisible = true;
            placeholder.IsVisible = false;
        }
    }
}
