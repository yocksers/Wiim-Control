using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class AmpPage
        {
            private const int MaxScreenBrightness = 16;
            private string? _screenInput;
            private string? _screenInputIp;
            private const int SpotifyMode = 31;
            private bool _screenSpotify;

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
                Task<bool> ApplyScreen() => window._owner.SetScreenAsync(ip, screen, screenOn.IsChecked == true, autoBrightness.IsChecked == true, brightness.Value,
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
                if (ui != null) rows.AddRange(BuildScreenUiRows(ip, ui, wallpapers));
                else if (wallpapers != null) rows.Add(BuildWallpaperPicker(ip, wallpapers));
                return Card("Screen", rows.ToArray());
            }

            private static int _screenTab;

            private List<Control> BuildScreenUiRows(string ip, ScreenUi ui, WallpaperInfo? wallpapers)
            {
                var rows = new List<Control>();
                var clockTab = new List<Control>();
                var standbyTab = new List<Control>();
                var playbackTab = new List<Control>();
                ScreenView? View(string key) => ui.Views.TryGetValue(key, out var view) ? view : null;
                bool round = window._owner.IsRoundScreen(ip, ui);
                var current = ui.Views.ToDictionary(v => v.Key, v => v.Value.Value);
                Control Choice(string text, string key, IEnumerable<(int Value, string Label)> items, int value, Func<int, Task<bool>> apply) =>
                    StyleImageChoice(text, items.ToList(), value, v => ScreenPreviews.Name(key, v, round, ui.InputKey, current), round, apply);

                if (ui.Clock24 is bool clock24)
                    rows.Add(AmpToggle("24-hour clock", clock24, on => window._owner.SetScreenBehaviorAsync(ip, ui, "clock_24_hours", on ? 1 : 0, false)));
                if (ui.StandbyScreenOff is bool standbyOff)
                    rows.Add(AmpToggle("Turn the screen off in standby", standbyOff,
                        on => window._owner.SetScreenBehaviorAsync(ip, ui, "standby_turn_off_screen", on ? 1 : 0, false)));

                if (round && (ui.ClockBackground != null || ui.ClockColor != null) && View("clock_view") is { } soundClock)
                {
                    var clocks = ScreenPreviews.SoundClocks.Where(c => soundClock.Choices.Contains(c.Feature)).ToList();
                    var currentClock = ScreenPreviews.CurrentSoundClock(soundClock.Value, ui.ClockBackground, ui.ClockColor);
                    if (clocks.Count > 1)
                        clockTab.Add(StyleImageChoice("Clock style", clocks.Select(c => (c.Feature, c.Name)).ToList(), currentClock?.Feature ?? -1,
                            feature => ScreenPreviews.SoundClocks.FirstOrDefault(c => c.Feature == feature)?.Picture + ".png", true,
                            feature => window._owner.SetSoundClockAsync(ip, clocks.First(c => c.Feature == feature))));
                }
                else if (View("clock_view") is { Choices.Count: > 1 } clock)
                    clockTab.Add(Choice("Clock style", "clock_view", StyleChoices(clock), clock.Value, v => window._owner.SetScreenViewAsync(ip, ui, ("clock_view", v))));
                if (View("clock_view") is { } clockView && ui.ClockColors?.TryGetValue(clockView.Value, out var clockColor) == true)
                    clockTab.Add(BuildClockColorRow(ip, ui, clockView.Value, clockColor));
                if (wallpapers != null) clockTab.Add(BuildWallpaperPicker(ip, wallpapers));
                if (View("standby_view") is { Choices.Count: > 1 } standby)
                    standbyTab.Add(Choice("Standby screen", "standby_view", StyleChoices(standby), standby.Value, v => window._owner.SetScreenViewAsync(ip, ui, ("standby_view", v))));

                Control? inputPills = null;
                if (ui.Inputs is { Count: > 0 } inputs && ui.InputKey != null)
                {
                    var pills = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 4) };
                    window.FillInputPills(pills, inputs, ui.InputKey, ui.PlayingInputKey, key => window._owner.InputName(ip, InputOfUiKey(key)),
                        name => $"Playback screen settings for {name}", async key =>
                        {
                            _screenInput = key;
                            _screenInputIp = ip;
                            await RefreshAmpAsync(quiet: true);
                        });
                    playbackTab.Add(pills);
                    inputPills = pills;
                }
                if (ui.InputScreenOn is bool inputScreenOn)
                    playbackTab.Add(AmpToggle("Screen on for this input", inputScreenOn,
                        on => window._owner.SetScreenBehaviorAsync(ip, ui, "screen_on", on ? 1 : 0, true)));
                var main = View("main_music_view");
                var second = View("sub_music_view");
                bool showsSecond = ui.StartPage == SecondPageFirst && second != null;
                var screens = new List<(int Value, string Label)>();
                if (main != null) screens.AddRange(StyleChoices(main));
                if (second != null) screens.AddRange(second.Choices.Select(p => (SecondPageChoice + p, SecondPageName(p))));
                if (screens.Count > 1)
                {
                    int selectedScreen = showsSecond ? SecondPageChoice + second!.Value : main?.Value ?? -1;
                    playbackTab.Add(StyleImageChoice("Now playing screen", screens, selectedScreen,
                        v => v >= SecondPageChoice
                            ? ScreenPreviews.Name("sub_music_view", v - SecondPageChoice, round, ui.InputKey, current)
                            : ScreenPreviews.Name("main_music_view", v, round, ui.InputKey, current),
                        round, v =>
                        {
                            if (v < SecondPageChoice) return window._owner.SetPlaybackScreenAsync(ip, ui, MainPageFirst, ("main_music_view", v));
                            int page = v - SecondPageChoice;
                            var values = new List<(string, int)> { ("sub_music_view", page) };
                            if (SecondPageStyleKey(page) is { } styleKey && View(styleKey) is { } style) values.Add((styleKey, style.Value));
                            return window._owner.SetPlaybackScreenAsync(ip, ui, SecondPageFirst, values.ToArray());
                        }));
                }
                if (showsSecond && SecondPageStyleKey(second!.Value) is { } pageKey && View(pageKey) is { Choices.Count: > 1 } pageStyle)
                    playbackTab.Add(Choice($"{SecondPageName(second.Value)} style", pageKey, StyleChoices(pageStyle), pageStyle.Value,
                        v => window._owner.SetScreenViewAsync(ip, ui, (pageKey, v))));
                if (showsSecond && second!.Value == VuMeterPage && ui.VuMeterMode is int vuMode)
                {
                    var vuRow = LabeledRow("VU meter mode", SegmentSwitch([(1, "Output amplitude"), (0, "Original signal")], vuMode, WideFieldWidth,
                        mode => ChangeAmpAsync(() => window._owner.SetVuMeterModeAsync(ip, mode))));
                    ToolTip.SetTip(vuRow, "Output amplitude shows the effect of the device's volume on the VU meter. " +
                        "Original signal shows the track's own level, unaffected by the volume.");
                    playbackTab.Add(vuRow);
                }
                if (showsSecond && second!.Value == VuMeterPage && ui.HideVuMeterTitle is bool hideTitle)
                {
                    var titleRow = AmpToggle("Hide the title on the VU meter", hideTitle, on => window._owner.SetVuMeterTitleHiddenAsync(ip, ui, on));
                    ToolTip.SetTip(titleRow, "Hides the song title and artist on the VU meter page");
                    playbackTab.Add(titleRow);
                }
                if (ui.HideCoverIcon is bool hideIcon)
                {
                    var row = AmpToggle("Hide the icon on the cover art page", hideIcon,
                        on => window._owner.SetScreenBehaviorAsync(ip, ui, "hidden_sub_cover_view_icon", on ? 1 : 0, false));
                    ToolTip.SetTip(row, "Hides the source icon shown on the cover art page");
                    playbackTab.Add(row);
                }
                if (ui.InputKey != null && ui.InputKey != ui.PlayingInputKey)
                    playbackTab.Add(Muted("These are saved for that input and show the next time it plays."));

                if (_screenSpotify && ui.SpotifySeparateUi && (ui.InputKey == null || InputOfUiKey(ui.InputKey) == "wifi"))
                {
                    var message = Muted("Playback screen options are not available during Spotify playback.");
                    playbackTab = inputPills == null ? [message] : [inputPills, message];
                }
                var tabs = new List<(string Name, List<Control> Rows)> { ("Clock style", clockTab), ("Standby screen", standbyTab), ("Playback", playbackTab) }
                    .Where(tab => tab.Rows.Count > 0).ToList();
                if (tabs.Count == 0) return rows;
                int selected = Math.Clamp(_screenTab, 0, tabs.Count - 1);
                var host = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                void ShowTab(int index)
                {
                    host.Children.Clear();
                    foreach (var row in tabs[index].Rows) host.Children.Add(row);
                }
                var switcher = SegmentSwitch(tabs.Select((tab, i) => (i, tab.Name)).ToArray(), selected, FieldWidth, index =>
                {
                    _screenTab = index;
                    ShowTab(index);
                    return Task.CompletedTask;
                });
                switcher.Width = double.NaN;
                switcher.Margin = new Thickness(0, 14, 0, 0);
                rows.Add(switcher);
                ShowTab(selected);
                rows.Add(host);
                return rows;
            }

            private Control StyleImageChoice(string text, List<(int Value, string Label)> items, int current, Func<int, string?> preview, bool round,
                Func<int, Task<bool>> apply)
            {
                var files = items.Select(i => preview(i.Value)).ToList();
                var previews = files.Select(f => f is { } file && file != ScreenPreviews.DigitalClock ? ScreenPreviews.Load(file) : null).ToList();
                if (previews.All(p => p == null)) return AmpChoice(text, items, current, apply);
                double width = round ? 104 : 150, height = round ? 104 : 100;
                var tiles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                for (int i = 0; i < items.Count; i++)
                {
                    var (value, label) = items[i];
                    bool selected = value == current;
                    Control picture = previews[i] is { } bitmap
                        ? new Border { Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }, CornerRadius = new CornerRadius(round ? width / 2 : 5) }
                        : files[i] == ScreenPreviews.DigitalClock
                        ? new Border
                        {
                            Background = Brushes.Black, CornerRadius = new CornerRadius(5),
                            Child = new TextBlock
                            {
                                Text = "09:30", FontSize = 34, FontWeight = FontWeight.Light, Foreground = Brushes.White,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                        : new TextBlock
                        {
                            Text = label, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0)
                        };
                    var frame = new Border
                    {
                        Width = width + 6, Height = height + 6, CornerRadius = new CornerRadius(round ? (width + 6) / 2 : 8), BorderThickness = new Thickness(3),
                        BorderBrush = selected ? Palette.Accent : Brushes.Transparent, Background = Palette.Input, Child = picture
                    };
                    var caption = new TextBlock
                    {
                        Text = label, Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                        Foreground = selected ? Palette.AccentText : Palette.TextMuted
                    };
                    var tile = new StackPanel
                    {
                        Margin = new Thickness(0, 0, 10, 10), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                        Children = { frame, caption }
                    };
                    ToolTip.SetTip(tile, selected ? $"{label} (in use)" : $"Use {label}");
                    tile.Tapped += async (_, _) =>
                    {
                        if (value != current) await ChangeAmpAsync(() => apply(value));
                    };
                    tiles.Children.Add(tile);
                }
                return new StackPanel
                {
                    Margin = new Thickness(0, 6, 0, 2),
                    Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, tiles }
                };
            }

            private sealed record ClockColor(string Hex, string Name);

            private const double ClockSwatchWidth = 196;

            private Grid BuildClockColorRow(string ip, ScreenUi ui, int clockView, string current)
            {
                var combo = new ComboBox
                {
                    Width = 260,
                    ItemTemplate = new FuncDataTemplate<ClockColor>((color, _) =>
                    {
                        if (color.Hex.Length == 0) return new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center };
                        var swatch = new Border
                        {
                            Width = ClockSwatchWidth, Height = 18, CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Color.TryParse("#" + color.Hex, out var fill) ? fill : Colors.White),
                            BorderBrush = Palette.TextDim, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
                        };
                        ToolTip.SetTip(swatch, color.Name);
                        return swatch;
                    })
                };
                var choices = ClockColorChoices.Select(c => new ClockColor(c.Hex, c.Name)).ToList();
                if (!choices.Any(c => c.Hex.Equals(current, StringComparison.OrdinalIgnoreCase)))
                    choices.Add(new ClockColor(current.ToLowerInvariant(), $"#{current.ToUpperInvariant()}"));
                foreach (var choice in choices) combo.Items.Add(choice);
                combo.Items.Add(new ClockColor(string.Empty, "Custom…"));
                combo.SelectedIndex = choices.FindIndex(c => c.Hex.Equals(current, StringComparison.OrdinalIgnoreCase));
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ClockColor)?.Hex is not string hex || hex.Equals(current, StringComparison.OrdinalIgnoreCase)) return;
                    if (hex.Length == 0)
                    {
                        if (await window.PickColorAsync(current) is not { } picked)
                        {
                            await RefreshAmpAsync(quiet: true);
                            return;
                        }
                        hex = picked;
                    }
                    await ChangeAmpAsync(() => window._owner.SetClockColorAsync(ip, ui, clockView, hex));
                };
                var row = LabeledRow("Clock colour", combo);
                ToolTip.SetTip(row, "The colour of the clock for the current clock style");
                return row;
            }
        }
    }
}
