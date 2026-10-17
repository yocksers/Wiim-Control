using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int MaxScreenBrightness = 16;
        private string? _screenInput;
        private string? _screenInputIp;

        private static readonly (string Hex, string Name)[] ClockColorChoices =
        [
            ("ffffff", "White"), ("ffe4b5", "Warm white"), ("ff3b30", "Red"), ("ff9500", "Orange"), ("ffcc00", "Yellow"),
            ("34c759", "Green"), ("5ac8fa", "Light blue"), ("007aff", "Blue"), ("af52de", "Purple"), ("ff2d55", "Pink")
        ];

        private Border BuildScreenCard(string ip, ScreenConfig screen, ScreenUi? ui, WallpaperInfo? wallpapers)
        {
            var rows = new List<Control>();
            var screenOn = new ToggleSwitch { IsChecked = screen.On };
            var autoBrightness = new ToggleSwitch { IsChecked = screen.AutoBrightness };
            var brightness = AmpSlider(1, Math.Max(MaxScreenBrightness, screen.Brightness), Math.Max(1, screen.Brightness), v => v.ToString(CultureInfo.InvariantCulture), false, out var brightnessRow);
            var wake = new ToggleSwitch { IsChecked = screen.WakeByTouch == true };
            Task<bool> ApplyScreen() => _owner.SetScreenAsync(ip, screen, screenOn.IsChecked == true, autoBrightness.IsChecked == true, brightness.Value,
                screen.WakeByTouch != null ? wake.IsChecked == true : null);
            screenOn.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
            autoBrightness.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
            brightness.ValueCommitted += async (_, _) => await ChangeAmpAsync(ApplyScreen);
            wake.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
            rows.Add(ToggleRow("Screen on", screenOn));
            var autoRow = ToggleRow("Adaptive brightness", autoBrightness);
            ToolTip.SetTip(autoRow, "Adjusts the screen brightness to the light in the room");
            rows.Add(autoRow);
            var brightnessLabel = LabeledRow("Screen brightness", brightnessRow);
            brightnessLabel.IsEnabled = !screen.AutoBrightness;
            brightnessLabel.Opacity = screen.AutoBrightness ? 0.45 : 1;
            rows.Add(brightnessLabel);
            if (screen.WakeByTouch != null)
            {
                var wakeRow = ToggleRow("Tap to wake the screen", wake);
                ToolTip.SetTip(wakeRow, "Touching the screen turns it on when it's off");
                rows.Add(wakeRow);
            }
            if (ui != null) rows.AddRange(BuildScreenUiRows(ip, ui));
            if (wallpapers != null) rows.Add(BuildWallpaperPicker(ip, wallpapers));
            return Card("Screen", rows.ToArray());
        }

        private List<Control> BuildScreenUiRows(string ip, ScreenUi ui)
        {
            var rows = new List<Control>();
            ScreenView? View(string key) => ui.Views.TryGetValue(key, out var view) ? view : null;

            if (ui.Clock24 is bool clock24)
                rows.Add(AmpToggle("24-hour clock", clock24, on => _owner.SetScreenBehaviorAsync(ip, ui, "clock_24_hours", on ? 1 : 0, false)));
            if (View("clock_view") is { Choices.Count: > 1 } clock)
                rows.Add(AmpChoice("Clock style", StyleChoices(clock), clock.Value, v => _owner.SetScreenViewAsync(ip, ui, ("clock_view", v))));
            if (View("clock_view") is { } clockView && ui.ClockColors?.TryGetValue(clockView.Value, out var clockColor) == true)
                rows.Add(BuildClockColorRow(ip, ui, clockView.Value, clockColor));
            if (View("standby_view") is { Choices.Count: > 1 } standby)
                rows.Add(AmpChoice("Standby screen", StyleChoices(standby), standby.Value, v => _owner.SetScreenViewAsync(ip, ui, ("standby_view", v))));
            if (ui.StandbyScreenOff is bool standbyOff)
                rows.Add(AmpToggle("Turn the screen off in standby", standbyOff,
                    on => _owner.SetScreenBehaviorAsync(ip, ui, "standby_turn_off_screen", on ? 1 : 0, false)));
            if (ui.HideCoverIcon is bool hideIcon)
            {
                var row = AmpToggle("Hide the icon on the cover art page", hideIcon,
                    on => _owner.SetScreenBehaviorAsync(ip, ui, "hidden_sub_cover_view_icon", on ? 1 : 0, false));
                ToolTip.SetTip(row, "Hides the source icon shown on the cover art page");
                rows.Add(row);
            }

            if (ui.Inputs is { Count: > 0 } inputs && ui.InputKey != null)
            {
                var combo = new ComboBox { Width = 240 };
                foreach (var key in inputs)
                    combo.Items.Add(ComboItem(key, _owner.InputName(ip, InputOfUiKey(key)) + (key == ui.PlayingInputKey ? " (playing now)" : string.Empty)));
                combo.SelectedIndex = inputs.IndexOf(ui.InputKey);
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string key || key == ui.InputKey) return;
                    _screenInput = key;
                    _screenInputIp = ip;
                    await RefreshAmpAsync(quiet: true);
                };
                var header = LabeledRow("Settings for input", combo);
                header.Margin = new Thickness(0, 12, 0, 0);
                ToolTip.SetTip(header, "The device keeps the layout and page settings separately for each input");
                rows.Add(header);
            }
            if (ui.InputScreenOn is bool inputScreenOn)
                rows.Add(AmpToggle("Screen on for this input", inputScreenOn,
                    on => _owner.SetScreenBehaviorAsync(ip, ui, "screen_on", on ? 1 : 0, true)));
            if (View("main_music_view") is { Choices.Count: > 1 } main)
                rows.Add(AmpChoice("Now playing layout", StyleChoices(main), main.Value, v => _owner.SetScreenViewAsync(ip, ui, ("main_music_view", v))));
            if (View("sub_music_view") is { } second)
            {
                if (second.Choices.Count > 1)
                    rows.Add(AmpChoice("Second page", second.Choices.Select(p => (p, SecondPageName(p))), second.Value, page =>
                    {
                        var values = new List<(string, int)> { ("sub_music_view", page) };
                        if (SecondPageStyleKey(page) is { } styleKey && View(styleKey) is { } style) values.Add((styleKey, style.Value));
                        return _owner.SetScreenViewAsync(ip, ui, values.ToArray());
                    }));
                if (SecondPageStyleKey(second.Value) is { } key && View(key) is { Choices.Count: > 1 } pageStyle)
                    rows.Add(AmpChoice($"{SecondPageName(second.Value)} style", StyleChoices(pageStyle), pageStyle.Value,
                        v => _owner.SetScreenViewAsync(ip, ui, (key, v))));
            }
            if (ui.StartPage is int startPage)
                rows.Add(AmpChoice("Show first when playing", [(0, "Now playing"), (1, "Second page")], startPage,
                    v => _owner.SetScreenBehaviorAsync(ip, ui, "playing_default_page", v, true)));
            if (ui.InputKey != null && ui.InputKey != ui.PlayingInputKey)
                rows.Add(Muted("These are saved for that input and show the next time it plays."));
            return rows;
        }

        private Grid BuildClockColorRow(string ip, ScreenUi ui, int clockView, string current)
        {
            var combo = new ComboBox { Width = 200 };
            Control Swatch(string hex, string name) => new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.TryParse("#" + hex, out var swatch) ? swatch : Colors.White),
                        BorderBrush = Palette.TextDim, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            var choices = ClockColorChoices.ToList();
            if (!choices.Any(c => c.Hex.Equals(current, StringComparison.OrdinalIgnoreCase))) choices.Add((current.ToLowerInvariant(), $"#{current.ToUpperInvariant()}"));
            foreach (var (hex, name) in choices) combo.Items.Add(new ComboBoxItem { Content = Swatch(hex, name), Tag = hex });
            combo.Items.Add(new ComboBoxItem { Content = "Custom…", Tag = string.Empty });
            combo.SelectedIndex = choices.FindIndex(c => c.Hex.Equals(current, StringComparison.OrdinalIgnoreCase));
            combo.SelectionChanged += async (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string hex || hex.Equals(current, StringComparison.OrdinalIgnoreCase)) return;
                if (hex.Length == 0)
                {
                    string? typed = await Dialogs.PromptAsync(this, "Enter a colour as six hex digits, for example ff9500:", current, _scale);
                    hex = typed?.Trim().TrimStart('#') ?? string.Empty;
                    if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    {
                        if (typed != null) await Dialogs.MessageAsync(this, "That isn't a colour. Use six hex digits, for example ff9500.", _scale);
                        await RefreshAmpAsync(quiet: true);
                        return;
                    }
                }
                await ChangeAmpAsync(() => _owner.SetClockColorAsync(ip, ui, clockView, hex));
            };
            var row = LabeledRow("Clock colour", combo);
            ToolTip.SetTip(row, "The colour of the clock for the current clock style");
            return row;
        }
    }
}
