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
        private readonly ComboBox _cboPresetAmp = new() { Width = 240 };
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
                    Muted("Click a preset to play it, or to run a routine (for example switching the output and EQ). " +
                          "Presets and routines are saved in the WiiM Home app; the device doesn't allow saving them from here.")
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
            _presetHint.Text = "No presets are saved on this device yet. Save your favourite stations and playlists as presets in the " +
                               "WiiM Home app, and they'll show up here.";
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
                text.Children.Add(new TextBlock { Text = preset.Source, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
            if (preset is { Detail.Length: > 0 })
                text.Children.Add(new TextBlock
                {
                    Text = preset.Detail, Classes = { "dim", "small" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 48,
                    Margin = new Thickness(0, 2, 0, 0)
                });

            var tile = new Border
            {
                Width = 170, Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12),
                Background = Palette.Card, Opacity = preset == null ? 0.55 : 1,
                Child = new StackPanel { Children = { picture, text } }
            };
            if (preset == null) return tile;

            tile.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(tile, preset.IsRoutine ? $"Run the routine {preset.Name}" : $"Play {preset.Name}");
            tile.PointerEntered += (_, _) => tile.Background = Palette.Input;
            tile.PointerExited += (_, _) => tile.Background = Palette.Card;
            tile.Tapped += async (_, _) =>
            {
                _lblPresetStatus.Text = preset.IsRoutine ? $"Running {preset.Name}…" : $"Playing {preset.Name}…";
                if (!await _owner.PlayPresetOnAsync(TargetOf(ip), number))
                    await Dialogs.MessageAsync(this, "The device didn't start the preset.", _scale);
                await Task.Delay(1500);
                if (IsVisible) await RefreshDeviceStatusesAsync();
            };
            if (preset.PictureUrl.Length > 0) _ = LoadPresetArtAsync(art, placeholder, preset.PictureUrl);
            return tile;
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
