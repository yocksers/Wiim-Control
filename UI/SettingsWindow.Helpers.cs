using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly TimeSpan SegmentSlideTime = TimeSpan.FromMilliseconds(220);

        private static LevelSlider AmpSlider(int min, int max, int value, Func<int, string> format, bool fromCenter, out Control row)
        {
            var slider = new LevelSlider { Minimum = min, Maximum = max, FillFromCenter = fromCenter, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            slider.Value = value;
            var label = new TextBlock { Text = format(slider.Value), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            label.MinWidth = new[] { min, max, (min + max) / 2, value }.Select(v =>
            {
                var probe = new TextBlock { Text = format(v), FontSize = 14 };
                probe.Measure(Size.Infinity);
                return Math.Ceiling(probe.DesiredSize.Width);
            }).Max();
            slider.ValueChanged += (_, _) => label.Text = format(slider.Value);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Width = FieldWidth };
            grid.Children.Add(slider);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            row = grid;
            return slider;
        }

        private static string BalanceText(int value) =>
            value == 0 ? "Center" : value < 0 ? $"L {-value}" : $"R {value}";

        private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        private static string ServerHost(string address) => address.EndsWith(":3483", StringComparison.Ordinal) ? address[..^5] : address;

        private async Task<string?> PickColorAsync(string currentHex, string title = "Clock colour")
        {
            var start = Color.TryParse("#" + currentHex, out var parsed) ? parsed : Colors.White;
            var wheel = new ColorSpectrum
            {
                Shape = ColorSpectrumShape.Ring, Components = ColorSpectrumComponents.HueSaturation,
                Width = 240, Height = 240, HorizontalAlignment = HorizontalAlignment.Center, Color = start
            };
            var brightness = new ColorSlider
            {
                ColorModel = ColorModel.Hsva, ColorComponent = ColorComponent.Component3, Orientation = Orientation.Horizontal,
                IsAlphaVisible = false, Height = 26, Color = start
            };
            var preview = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(start),
                BorderBrush = Palette.TextDim, BorderThickness = new Thickness(1)
            };
            var hexBox = new TextBox { Text = HexOf(start), Width = 110, MaxLength = 7, VerticalAlignment = VerticalAlignment.Center };
            var chosen = start;
            bool updating = false;
            void Show(Color color, Control? source)
            {
                if (updating) return;
                updating = true;
                chosen = Color.FromRgb(color.R, color.G, color.B);
                if (source != wheel) wheel.Color = chosen;
                if (source != brightness) brightness.Color = chosen;
                if (source != hexBox) hexBox.Text = HexOf(chosen);
                preview.Background = new SolidColorBrush(chosen);
                updating = false;
            }
            wheel.ColorChanged += (_, e) => Show(e.NewColor, wheel);
            brightness.ColorChanged += (_, e) => Show(e.NewColor, brightness);
            hexBox.TextChanged += (_, _) =>
            {
                string text = hexBox.Text?.Trim().TrimStart('#') ?? string.Empty;
                if (text.Length == 6 && Color.TryParse("#" + text, out var typed)) Show(typed, hexBox);
            };
            var bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { preview, new TextBlock { Text = "#", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -8, 0) }, hexBox }
            };
            var content = new StackPanel
            {
                Width = 300, Spacing = 14,
                Children = { new TextBlock { Text = title, Classes = { "heading" } }, wheel, brightness, bottom }
            };
            return await Dialogs.ChooseAsync(this, content, "OK", _scale) ? HexOf(chosen) : null;
        }

        private static string HexOf(Color color) =>
            $"{color.R:x2}{color.G:x2}{color.B:x2}";

        private static Control CoffeeButton()
        {
            var badge = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0.85,
                Child = new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri("avares://WiimControl/Assets/bmc.png"))),
                    Height = 28, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(badge, "Buy me a coffee");
            badge.PointerEntered += (_, _) => badge.Opacity = 1;
            badge.PointerExited += (_, _) => badge.Opacity = 0.85;
            badge.Tapped += (_, _) => OpenUrl("https://buymeacoffee.com/yockser");
            return badge;
        }

        private static Control WiimButton()
        {
            var badge = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), ClipToBounds = true, Background = Brushes.Black,
                Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0.85,
                Child = new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri("avares://WiimControl/Assets/wiim.png"))),
                    Stretch = Stretch.UniformToFill
                }
            };
            ToolTip.SetTip(badge, "WiiM website");
            badge.PointerEntered += (_, _) => badge.Opacity = 1;
            badge.PointerExited += (_, _) => badge.Opacity = 0.85;
            badge.Tapped += (_, _) => OpenUrl("https://www.wiimhome.com/");
            return badge;
        }

        private static readonly FilePickerFileType BackupFileType = new("Wiim Control backup") { Patterns = ["*.json"] };

        private static Border SegmentSwitch((int Value, string Label)[] options, int current, double width, Func<int, Task> change)
        {
            var grid = new Grid { ColumnSpacing = 3 };
            var buttons = new List<(int Value, Button Button)>();
            void Show(int selected)
            {
                foreach (var (value, button) in buttons) button.Classes.Set("selected", value == selected);
            }
            foreach (var (value, label) in options)
            {
                var button = Segment(label);
                button.Padding = new Thickness(10, 5);
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                Grid.SetColumn(button, buttons.Count);
                grid.Children.Add(button);
                buttons.Add((value, button));
                button.Click += async (_, _) =>
                {
                    if (value == current) return;
                    current = value;
                    Show(value);
                    await change(value);
                };
            }
            Show(current);
            var border = new Border { Child = grid, Width = width, Padding = new Thickness(3), CornerRadius = new CornerRadius(12) };
            border.Classes.Add("segments");
            AddSlidingThumb(border, grid, buttons.Select(b => b.Button).ToList());
            return border;
        }

        private static Button Segment(string text)
        {
            var button = new Button { Content = text };
            button.Classes.Add("segment");
            button.Classes.Add("solid");
            return button;
        }

        private static void AddSlidingThumb(Border container, Grid grid, IReadOnlyList<Button> buttons)
        {
            var thumb = new Border { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
            thumb.Classes.Add("segment-thumb");
            grid.Children.Insert(0, thumb);
            container.Classes.Add("sliding");
            bool placed = false;
            void Move()
            {
                int index = buttons.ToList().FindIndex(b => b.Classes.Contains("selected"));
                thumb.IsVisible = index >= 0 && buttons[index].Bounds.Width > 0;
                if (!thumb.IsVisible) return;
                var target = buttons[index].Bounds;
                thumb.Width = target.Width;
                thumb.Height = target.Height;
                thumb.Margin = new Thickness(0, target.Y, 0, 0);
                thumb.VerticalAlignment = VerticalAlignment.Top;
                thumb.RenderTransform = TransformOperations.Parse(
                    $"translateX({target.X.ToString(CultureInfo.InvariantCulture)}px)");
                if (!placed)
                {
                    placed = true;
                    thumb.Transitions =
                    [
                        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = SegmentSlideTime, Easing = new CubicEaseOut() }
                    ];
                }
            }
            void Dim() => thumb.Classes.Set("dim", !buttons.Any(b => b.IsEffectivelyEnabled));
            foreach (var button in buttons)
            {
                button.Classes.CollectionChanged += (_, _) => Move();
                button.PropertyChanged += (_, e) =>
                {
                    if (e.Property == BoundsProperty) Move();
                    else if (e.Property == IsEffectivelyEnabledProperty) Dim();
                };
            }
            Dim();
        }

        private static string? AmpOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private void FillAmpCombo(ComboBox combo)
        {
            string selected = (_followActive ? null : AmpOf(combo)) ?? _owner._deviceIp;
            combo.Items.Clear();
            foreach (var d in _owner._knownDevices) combo.Items.Add(ComboItem(d.Ip, d.Name));
            int index = _owner._knownDevices.FindIndex(d => d.Ip == selected);
            combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
        }

        private static StackPanel ShareField(string label, Control input) =>
            new() { Spacing = 4, Children = { new TextBlock { Text = label, Classes = { "muted", "small" } }, input } };
    }
}
