using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int DashboardPresetColumns = 6;
        private const int DashboardTileColumns = 3;
        private const double DashboardGap = 12;
        private const double DashboardTileHeight = 78;
        private const double EntranceShift = 18;
        private static readonly TimeSpan EntranceTime = TimeSpan.FromMilliseconds(560);
        private static readonly TimeSpan TileFadeTime = TimeSpan.FromMilliseconds(200);
        private List<Control> _dashboardParts = [];
        private Grid? _dashboardPresetGrid;
        private Border? _dashboardPresetCard;
        private string? _dashboardPresetKey;

        private CachedCard? _dashboard;
        private string? _dashboardIp;
        private int _dashboardVersion;
        private readonly List<(string Input, Border Tile, TextBlock Signal)> _dashboardSources = [];
        private Grid? _dashboardOutputGrid;
        private Border? _dashboardOutputCard;

        private void ShowDashboard(List<Control> rows)
        {
            string? ip = _owner._knownDevices.Any(d => d.Ip == _owner._deviceIp) ? _owner._deviceIp : _owner._knownDevices.FirstOrDefault().Ip;
            if (ip == null)
            {
                ForgetDashboard();
                return;
            }
            var device = _owner._knownDevices.First(d => d.Ip == ip);
            string key = $"{ip}|{CardKey(device, 0, 1)}|{string.Join(",", _owner._knownDevices.Select(d => d.Ip + ":" + d.Name))}";
            bool otherDevice = _dashboardIp != null && _dashboardIp != ip;
            if (_dashboard?.Key != key)
            {
                ForgetDashboard();
                _dashboard = new CachedCard(key, BuildDashboard(device), null);
                _dashboardIp = ip;
            }
            rows.Add(_dashboard.Slot);
            if (otherDevice) Avalonia.Threading.Dispatcher.UIThread.Post(() => AnimateEntrance(_dashboardParts), Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private void ForgetDashboard()
        {
            if (_dashboard != null) ForgetCard(_dashboard);
            _dashboard = null;
            _dashboardIp = null;
            _dashboardSources.Clear();
            _dashboardParts = [];
            _dashboardPresetGrid = null;
            _dashboardPresetCard = null;
            _dashboardPresetKey = null;
            _dashboardOutputGrid = null;
            _dashboardOutputCard = null;
            _dashboardVersion++;
        }

        private void AnimateEntrance(List<Control> items)
        {
            if (!_deviceList.IsEffectivelyVisible || items.Count == 0) return;
            double stagger = items.Count > 1 ? Math.Min(0.08, 0.4 / (items.Count - 1)) : 0;
            double span = 1 - stagger * (items.Count - 1);
            var parts = items.Select(item =>
            {
                var shift = new TranslateTransform(0, EntranceShift);
                double opacity = item.Opacity;
                item.RenderTransform = shift;
                item.Opacity = 0;
                return (Item: item, Shift: shift, Opacity: opacity);
            }).ToList();
            void Step(double progress)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    double e = Ease(Math.Clamp((progress - i * stagger) / span, 0, 1));
                    parts[i].Item.Opacity = parts[i].Opacity * e;
                    parts[i].Shift.Y = EntranceShift * (1 - e);
                }
            }
            Animate(_deviceList, EntranceTime, Step, () =>
            {
                foreach (var (item, _, opacity) in parts)
                {
                    item.RenderTransform = null;
                    item.Opacity = opacity;
                }
            });
        }

        private Panel BuildDashboard(KnownDevice device)
        {
            string ip = device.Ip;
            bool locked = _owner.IsDeviceLocked(ip);
            int version = ++_dashboardVersion;
            _dashboardSources.Clear();

            var presetGrid = new Grid { Margin = new Thickness(-DashboardGap / 2) };
            for (int i = 0; i < DashboardPresetColumns; i++) presetGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            presetGrid.SizeChanged += (_, e) => SizePresetTiles(presetGrid, e.NewSize.Width);
            var presets = DashboardCard(Icons.Star, "Presets", presetGrid, Muted("Loading…"));
            _dashboardPresetGrid = presetGrid;
            _dashboardPresetCard = presets;
            _dashboardPresetKey = null;

            var sourceGrid = DashboardTileGrid();
            var sources = DashboardCard(Icons.Input, "Source", sourceGrid, Muted("Loading…"));
            var outputGrid = DashboardTileGrid();
            var outputs = DashboardCard(Icons.Output, "Output", outputGrid, Muted("Loading…"));
            _dashboardOutputGrid = outputGrid;
            _dashboardOutputCard = outputs;
            presets.IsEnabled = sources.IsEnabled = outputs.IsEnabled = !locked;

            var members = GroupMembers(ip);
            var nowPlaying = BuildNowPlayingRow(ip);
            nowPlaying.Margin = new Thickness(0);
            if (locked && _nowPlayingControls.TryGetValue(ip, out var playback))
            {
                if (playback.PlayPause.Parent is Control transport) transport.IsEnabled = false;
                playback.PositionRow.IsEnabled = false;
            }
            var volumeRow = members.Count > 1 ? BuildGroupVolumeRow(ip, members) : BuildVolumeRow(ip);
            if (members.Count == 1) volumeRow.IsEnabled = !locked;
            var controls = DashboardCard(Icons.Play, "Now playing", new StackPanel { Children = { nowPlaying, volumeRow } });

            var deviceCard = DashboardCard(Icons.Speaker, "Device", BuildDashboardDevice(device, locked));

            var upper = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, DashboardGap) };
            Place(upper, controls, 0, 0, new Thickness(0, 0, DashboardGap / 2, 0));
            Place(upper, deviceCard, 0, 1, new Thickness(DashboardGap / 2, 0, 0, 0));
            var lower = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, DashboardGap, 0, 0) };
            Place(lower, sources, 0, 0, new Thickness(0, 0, DashboardGap / 2, 0));
            Place(lower, outputs, 0, 1, new Thickness(DashboardGap / 2, 0, 0, 0));

            presets.Margin = new Thickness(0);
            var panel = new StackPanel { Children = { upper, presets, lower } };
            _dashboardParts = [controls, deviceCard, presets, sources, outputs];
            var slot = CardSlot(ip, panel);
            _ = LoadDashboardAsync(ip, version, presetGrid, presets, sourceGrid, sources, outputGrid, outputs);
            return slot;
        }

        private static void Place(Grid grid, Control child, int row, int column, Thickness margin)
        {
            child.Margin = margin;
            Grid.SetRow(child, row);
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        private static Border DashboardCard(StreamGeometry icon, string title, Control content, Control? waiting = null)
        {
            var heading = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12),
                Children =
                {
                    new PathIcon { Data = icon, Width = 14, Height = 14, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = title.ToUpperInvariant(), Classes = { "muted", "small" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            var stack = new StackPanel { Children = { heading } };
            if (waiting != null) stack.Children.Add(waiting);
            stack.Children.Add(content);
            var card = new Border { Child = stack, VerticalAlignment = VerticalAlignment.Stretch };
            card.Classes.Add("card");
            return card;
        }

        private static void SetWaiting(Border card, string? text)
        {
            if (card.Child is not StackPanel { Children.Count: >= 3 } stack || stack.Children[1] is not TextBlock waiting) return;
            waiting.Text = text ?? string.Empty;
            waiting.IsVisible = text != null;
        }

        private static Grid DashboardTileGrid()
        {
            var grid = new Grid { Margin = new Thickness(-DashboardGap / 4) };
            for (int i = 0; i < DashboardTileColumns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            return grid;
        }

        private static void FillTileGrid(Grid grid, List<Control> tiles, int columns)
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            for (int i = 0; i < (tiles.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < tiles.Count; i++)
            {
                Grid.SetColumn(tiles[i], i % columns);
                Grid.SetRow(tiles[i], i / columns);
                grid.Children.Add(tiles[i]);
            }
        }

        private static void SizePresetTiles(Grid grid, double width)
        {
            double size = Math.Max(40, width / DashboardPresetColumns - DashboardGap);
            foreach (var tile in grid.Children.OfType<Control>()) tile.Height = size;
        }

        private async Task LoadDashboardAsync(string ip, int version, Grid presetGrid, Border presetCard, Grid sourceGrid, Border sourceCard,
            Grid outputGrid, Border outputCard)
        {
            var presetsTask = _owner.GetPresetsAsync(ip);
            var inputsTask = _owner.GetInputsAsync(ip);
            var outputTask = _owner.GetOutputStateAsync(ip);
            var signalsTask = _owner.ReadInputSignalsAsync(ip);
            await Task.WhenAll(presetsTask, inputsTask, outputTask, signalsTask);
            if (version != _dashboardVersion) return;

            ShowDashboardPresets(ip, presetsTask.Result, presetGrid, presetCard, animate: true);

            if (inputsTask.Result is { Count: > 0 } inputs)
            {
                SetWaiting(sourceCard, null);
                var tiles = inputs.Select(input =>
                {
                    var tile = DashboardTile(input == "wifi" ? Icons.Wifi : Icons.ForInput(input), _owner.InputName(ip, input), false,
                        () => SwitchInputAsync(ip, input));
                    var signal = new TextBlock
                    {
                        FontSize = 11, Foreground = Palette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false,
                        Margin = new Thickness(0, -3, 0, 0)
                    };
                    if (tile.Child is StackPanel stack) stack.Children.Add(signal);
                    _dashboardSources.Add((input, tile, signal));
                    return (Control)tile;
                }).ToList();
                FillTileGrid(sourceGrid, tiles, DashboardTileColumns);
                ApplyDashboardSource(ip);
                AnimateEntrance(tiles);
            }
            else SetWaiting(sourceCard, inputsTask.Result == null ? "Couldn't read the inputs from the device." : "This device has no inputs to choose.");

            if (ShowDashboardOutputs(ip, outputTask.Result, outputGrid, outputCard) is { } outputTiles) AnimateEntrance(outputTiles);
        }

        internal void OnPresetsChanged(string ip)
        {
            if (_dashboardIp == ip) _ = RefreshDashboardPresetsAsync();
            _presets.OnPresetsChanged(ip);
        }

        private async Task RefreshDashboardPresetsAsync()
        {
            if (_dashboardIp is not { } ip || _dashboardPresetGrid is not { } grid || _dashboardPresetCard is not { } card) return;
            int version = _dashboardVersion;
            var presets = await _owner.GetPresetsAsync(ip);
            if (version == _dashboardVersion && ip == _dashboardIp) ShowDashboardPresets(ip, presets, grid, card, animate: false);
        }

        private void ShowDashboardPresets(string ip, PresetList? presets, Grid grid, Border card, bool animate)
        {
            if (presets == null)
            {
                if (grid.Children.Count == 0) SetWaiting(card, "Couldn't read the presets from the device.");
                return;
            }
            string key = $"{presets.Slots}|" + string.Join("|", presets.Presets.OrderBy(p => p.Number)
                .Select(p => $"{p.Number}\u0001{p.Name}\u0001{p.PictureUrl}\u0001{p.IsRoutine}"));
            if (key == _dashboardPresetKey) return;
            _dashboardPresetKey = key;
            SetWaiting(card, null);
            var tiles = new List<Control>();
            for (int number = 1; number <= presets.Slots; number++)
                tiles.Add(DashboardPresetTile(ip, number, presets.Presets.FirstOrDefault(p => p.Number == number)));
            FillTileGrid(grid, tiles, DashboardPresetColumns);
            SizePresetTiles(grid, grid.Bounds.Width);
            if (animate) AnimateEntrance(tiles);
        }

        private async Task RefreshDashboardOutputsAsync()
        {
            if (_dashboardIp is not { } ip || _dashboardOutputGrid is not { } grid || _dashboardOutputCard is not { } card) return;
            int version = _dashboardVersion;
            var state = await _owner.GetOutputStateAsync(ip);
            if (version == _dashboardVersion && ip == _dashboardIp && state != null) ShowDashboardOutputs(ip, state, grid, card);
        }

        private List<Control>? ShowDashboardOutputs(string ip, OutputState? state, Grid grid, Border card)
        {
            if (state is not { Choices.Count: > 0 })
            {
                grid.Children.Clear();
                SetWaiting(card, state == null ? "Couldn't read the output from the device." : "This device has one fixed output.");
                return null;
            }
            SetWaiting(card, null);
            var tiles = state.Choices.Select(choice => (Control)DashboardTile(OutputIcon(choice.Mode), choice.Name, choice.Mode == state.ActiveMode,
                async () =>
                {
                    if (choice.Mode == state.ActiveMode) return;
                    if (!await _owner.SetOutputAsync(ip, choice))
                        await Dialogs.MessageAsync(this, "The device didn't switch the output. Check that it's switched on and reachable.", _scale);
                    var updated = await _owner.GetOutputStateAsync(ip);
                    if (_dashboardIp == ip) ShowDashboardOutputs(ip, updated, grid, card);
                })).ToList();
            FillTileGrid(grid, tiles, DashboardTileColumns);
            return tiles;
        }

        private static StreamGeometry OutputIcon(string mode) => mode switch
        {
            "AUDIO_OUTPUT_SPEAKER_MODE" => Icons.Speaker,
            "AUDIO_OUTPUT_UAC_CARD_MODE" => Icons.Usb,
            "AUDIO_OUTPUT_SPDIF_MODE" => Icons.Toslink,
            "AUDIO_OUTPUT_AUX_MODE" or "AUDIO_OUTPUT_COAX_MODE" => Icons.Rca,
            _ => Icons.Output
        };

        private Border DashboardTile(StreamGeometry icon, string text, bool active, Func<Task> click)
        {
            var tile = new Border
            {
                Height = DashboardTileHeight, Margin = new Thickness(DashboardGap / 4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, Spacing = 6,
                    Children =
                    {
                        new PathIcon { Data = icon, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = text, Classes = { "small" }, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(6, 0) }
                    }
                }
            };
            SetTileActive(tile, active);
            tile.Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = TileFadeTime }];
            tile.PointerEntered += (_, _) => { if (tile.Tag is not true) tile.Background = Palette.InputHover; };
            tile.PointerExited += (_, _) => SetTileActive(tile, tile.Tag is true);
            bool busy = false;
            tile.Tapped += async (_, _) =>
            {
                if (busy) return;
                busy = true;
                try { await click(); }
                finally { busy = false; }
            };
            return tile;
        }

        private static void SetTileActive(Border tile, bool active)
        {
            tile.Tag = active;
            tile.Background = active ? Palette.AccentSoft : Palette.Input;
            tile.BorderBrush = active ? Palette.Accent : Brushes.Transparent;
        }

        private void ApplyDashboardSource(string ip)
        {
            if (ip != _dashboardIp || _dashboardSources.Count == 0) return;
            string? current = _statuses.TryGetValue(ip, out var status) ? InputOfMode(status.Mode) : null;
            foreach (var (input, tile, signal) in _dashboardSources)
            {
                SetTileActive(tile, IsSameInput(input, current));
                string? text = _owner.InputSignal(ip, input) switch
                {
                    "playing" => "Signal",
                    "nosound" => "No sound",
                    "detecting" => "Checking…",
                    _ => null
                };
                signal.Text = text ?? string.Empty;
                signal.IsVisible = text != null;
                ToolTip.SetTip(signal, text == null ? null : "Auto-sensing on this input reports this. It's switched on under Input settings.");
            }
        }

        private Control DashboardPresetTile(string ip, int number, PresetInfo? preset)
        {
            var tile = new Border { Margin = new Thickness(DashboardGap / 2), CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Palette.Input };
            if (preset == null)
            {
                tile.Opacity = 0.6;
                tile.Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, Spacing = 6,
                    Children =
                    {
                        new Border
                        {
                            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Palette.AccentSoft, HorizontalAlignment = HorizontalAlignment.Center,
                            Child = new TextBlock { Text = number.ToString(), Classes = { "small" }, Foreground = Palette.AccentText,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                        },
                        new TextBlock { Text = $"Preset {number}", Classes = { "dim", "small" }, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                };
                ToolTip.SetTip(tile, "Empty. Save something to it on the Presets page.");
                return tile;
            }

            var placeholder = new PathIcon
            {
                Data = preset.IsRoutine ? Icons.Settings : Icons.Music, Width = 30, Height = 30,
                Foreground = preset.IsRoutine ? Palette.AccentText : Palette.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var art = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
            var name = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 14, 8, 6),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(190, 0, 0, 0), 1) }
                },
                Child = new TextBlock { Text = preset.Name, Classes = { "small" }, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis }
            };
            var badge = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = number.ToString(), FontSize = 11, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            tile.Child = new Panel { Children = { placeholder, art, name, badge } };
            tile.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(tile, preset.IsRoutine ? $"Run the routine {preset.Name}" : $"Play {preset.Name}");
            tile.PointerEntered += (_, _) => tile.Opacity = 0.85;
            tile.PointerExited += (_, _) => tile.Opacity = 1;
            tile.Tapped += async (_, _) =>
            {
                if (!await _owner.PlayPresetOnAsync(TargetOf(ip), number))
                    await Dialogs.MessageAsync(this, "The device didn't start the preset.", _scale);
                await Task.Delay(1500);
                if (IsVisible) await RefreshDeviceStatusesAsync();
            };
            if (preset.PictureUrl.Length > 0) _ = LoadDashboardArtAsync(art, placeholder, preset.PictureUrl);
            return tile;
        }

        private async Task LoadDashboardArtAsync(Image art, PathIcon placeholder, string url)
        {
            var bitmap = await _owner.GetArtworkAsync(url);
            if (bitmap == null) return;
            art.Source = bitmap;
            art.IsVisible = true;
            placeholder.IsVisible = false;
        }

        private Control BuildDashboardDevice(KnownDevice device, bool locked)
        {
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var d in _owner._knownDevices) picker.Items.Add(ComboItem(d.Ip, GroupMembers(d.Ip).Count > 1 && !IsFollower(d.Ip) ? $"{d.Name} (group)" : d.Name));
            picker.SelectedIndex = _owner._knownDevices.FindIndex(d => d.Ip == device.Ip);
            ToolTip.SetTip(picker, "The device shown on this page");
            picker.SelectionChanged += (_, _) =>
            {
                if ((picker.SelectedItem as ComboBoxItem)?.Tag is string chosen && chosen != device.Ip &&
                    _owner._knownDevices.FirstOrDefault(d => d.Ip == chosen) is { Ip: not null } next)
                    _owner.SwitchDevice(next);
            };

            Button Action(StreamGeometry icon, string text, string tip)
            {
                var path = new PathIcon { Data = icon, Width = 16, Height = 16 };
                var button = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { path, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
                    },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
                };
                button.Classes.Add("pill");
                ToolTip.SetTip(button, tip);
                return button;
            }
            var link = Action(Icons.Link, "Link devices", string.Empty);
            if (link.Content is StackPanel { Children: [PathIcon linkIcon, ..] }) HookLinkButton(link, linkIcon, device.Ip);
            link.IsEnabled = !locked;
            var big = Action(Icons.FullScreen, "Big player", "Open the big player");
            big.Click += (_, _) => ShowNowPlayingView(device.Ip);
            var mini = Action(Icons.MiniPlayer, "Mini player", "Open or close the mini player for this device");
            mini.Click += (_, _) => _owner.ToggleMiniPlayer(device.Ip);

            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 12, 0, 0) };
            Place(buttons, link, 0, 0, new Thickness(0, 0, 4, 0));
            Place(buttons, big, 0, 1, new Thickness(4, 0, 4, 0));
            Place(buttons, mini, 0, 2, new Thickness(4, 0, 0, 0));

            return new StackPanel { Children = { picker, buttons } };
        }
    }
}
