using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly IBrush SelectedRowBrush = Palette.NavSelectedHover;
        private static readonly (PeqFilter Filter, string Label)[] PeqFilterLabels =
            [(PeqFilter.Off, "Off"), (PeqFilter.LowShelf, "Low shelf"), (PeqFilter.Peak, "Peak"), (PeqFilter.HighShelf, "High shelf")];

        private readonly Button _btnGraphicEq = Segment("Graphic EQ");
        private readonly Button _btnParametricEq = Segment("Parametric EQ");
        private Control? _eqTypeRow;
        private Control? _geqEditor;
        private Control? _peqEditor;
        private readonly PeqGraph _peqGraph = new() { MinHeight = 130 };
        private readonly ComboBox _cboPeqChannels = new() { Width = 240 };
        private readonly Button _btnPeqLeft = Pill("Left");
        private readonly Button _btnPeqRight = Pill("Right");
        private readonly StackPanel _peqChannelButtons = new()
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly ComboBox[] _peqFilters = new ComboBox[PeqBandCount];
        private readonly NumberBox[] _peqFrequencies = new NumberBox[PeqBandCount];
        private readonly NumberBox[] _peqGains = new NumberBox[PeqBandCount];
        private readonly NumberBox[] _peqQs = new NumberBox[PeqBandCount];
        private readonly Border[] _peqRows = new Border[PeqBandCount];
        private readonly PeqBand[][] _peqChannels = [[.. PeqDefaults], [.. PeqDefaults], [.. PeqDefaults]];
        private readonly HashSet<(int Channel, int Index)> _peqDirty = [];
        private readonly DispatcherTimer _peqSendTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _peqSplit;
        private readonly SemaphoreSlim _peqFlushGate = new(1, 1);
        private int _peqChannel;
        private int _peqSelected = -1;

        private bool IsParametric => _eqPluginUri == PeqPlugin;
        private PeqBand[] PeqEdited => _peqChannels[_peqChannel];

        private static string FormatFrequency(double f) =>
            f < 1000 ? string.Create(CultureInfo.InvariantCulture, $"{f:0.0} Hz")
            : f < 10000 ? string.Create(CultureInfo.InvariantCulture, $"{f / 1000:0.00} kHz")
            : string.Create(CultureInfo.InvariantCulture, $"{f / 1000:0.0} kHz");

        private static string FormatGain(double g) => string.Create(CultureInfo.InvariantCulture, $"{g:0.0} dB");

        private static string FormatQ(double q) => q.ToString("0.00", CultureInfo.InvariantCulture);

        private static Grid PeqRowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("44,1.3*,*,*,*") };

        private static void PlaceCell(Grid grid, Control cell, int column)
        {
            cell.Margin = new Thickness(4, 0);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private Control BuildPeqEditor()
        {
            _peqSendTimer.Tick += async (_, _) => await FlushPeqAsync();

            _cboPeqChannels.Items.Add(ComboItem(false, "Same for both channels"));
            _cboPeqChannels.Items.Add(ComboItem(true, "Left and right separately"));
            _cboPeqChannels.SelectedIndex = 0;
            _cboPeqChannels.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboPeqChannels.SelectedItem as ComboBoxItem)?.Tag is not bool split || split == _peqSplit) return;
                await SendPendingEqBandsAsync();
                if (!await _owner.SetPeqSplitAsync(_eqDeviceIp, _eqSource, split))
                    await Dialogs.MessageAsync(this, "The device didn't change the channel setting.", _scale);
                _peqChannel = split ? 1 : 0;
                await RefreshEqAsync();
            };
            _btnPeqLeft.Click += (_, _) => ShowPeqChannel(1);
            _btnPeqRight.Click += (_, _) => ShowPeqChannel(2);
            _peqChannelButtons.Children.Add(new TextBlock { Text = "Editing", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
            _peqChannelButtons.Children.Add(_btnPeqLeft);
            _peqChannelButtons.Children.Add(_btnPeqRight);
            _peqChannelButtons.HorizontalAlignment = HorizontalAlignment.Left;
            _peqChannelButtons.Margin = new Thickness(16, 0, 0, 0);
            var btnReset = Pill("Reset");
            var btnImport = Pill("Import");
            ToolTip.SetTip(btnImport, "Load the filters from a Room EQ Wizard or AutoEQ filter file (.txt).");
            btnImport.Click += async (_, _) => await ImportPeqFileAsync();
            var editButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { btnImport, btnReset } };
            var channelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 4) };
            channelRow.Children.Add(_cboPeqChannels);
            Grid.SetColumn(_peqChannelButtons, 1);
            channelRow.Children.Add(_peqChannelButtons);
            Grid.SetColumn(editButtons, 2);
            channelRow.Children.Add(editButtons);
            var header = PeqRowGrid();
            header.Margin = new Thickness(0, 0, 0, 4);
            string[] titles = ["", "Filter", "Frequency", "Gain", "Q"];
            for (int c = 0; c < titles.Length; c++)
                PlaceCell(header, new TextBlock { Text = titles[c], Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center }, c);

            var table = new StackPanel { Children = { header } };
            for (int i = 0; i < PeqBandCount; i++) table.Children.Add(BuildPeqRow(i));

            _peqGraph.BandChanged += (i, band) => OnPeqBandChanged(i, band, fromGraph: true);
            _peqGraph.SelectedChanged += SelectPeqBand;

            btnReset.Click += async (_, _) =>
            {
                string which = _peqChannel switch { 1 => " of the left channel", 2 => " of the right channel", _ => string.Empty };
                if (!await Dialogs.ConfirmAsync(this, $"Reset all bands{which} to their default settings?", _scale)) return;
                for (int i = 0; i < PeqBandCount; i++)
                {
                    PeqEdited[i] = PeqDefaults[i];
                    _peqDirty.Add((_peqChannel, i));
                    UpdatePeqRow(i);
                }
                _peqGraph.InvalidateVisual();
                MarkEqCustom();
                if (!_peqSendTimer.IsEnabled) _peqSendTimer.Start();
            };

            table.Margin = new Thickness(0, 0, 14, 0);
            var scroll = new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(1, GridUnitType.Star) { MinHeight = 130, MaxHeight = 260 },
                    new RowDefinition(12, GridUnitType.Pixel), new RowDefinition(GridLength.Auto),
                    new RowDefinition(1.2, GridUnitType.Star) { MinHeight = 90 }
                }
            };
            grid.Children.Add(_peqGraph);
            Grid.SetRow(channelRow, 2);
            grid.Children.Add(channelRow);
            Grid.SetRow(scroll, 3);
            grid.Children.Add(scroll);
            var card = new Border { Child = grid, IsVisible = false };

            _eqEditControls.AddRange([_peqGraph, _cboPeqChannels, _btnPeqLeft, _btnPeqRight, btnImport, btnReset, .. _peqFilters]);
            _peqGraph.Bands = PeqEdited;
            UpdatePeqChannelButtons();
            return card;
        }

        private async Task ImportPeqFileAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import filters",
                FileTypeFilter = [new FilePickerFileType("Filter files (REW, AutoEQ)") { Patterns = ["*.txt"] }, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;

            string text;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                text = await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, $"Couldn't read the file: {ex.Message}", _scale);
                return;
            }

            var import = PeqFilterFile.Parse(text);
            var notes = new List<string>();
            if (import.Skipped.Count > 0)
                notes.Add($"Skipped, the parametric EQ has no matching filter type: {string.Join(", ", import.Skipped)}.");
            if (import.Bands.Count == 0)
            {
                await Dialogs.MessageAsync(this, string.Join("\n\n",
                    ["The file has no peak or shelf filters with a Q the device can use.", .. notes]), _scale);
                return;
            }

            var used = import.Bands.Take(PeqBandCount).ToList();
            if (import.Bands.Count > PeqBandCount)
                notes.Add($"The file has {import.Bands.Count} usable filters; the device has {PeqBandCount} bands, so only the first {PeqBandCount} are used.");
            if (import.Adjusted > 0)
                notes.Add($"{import.Adjusted} filter(s) were limited to the device's range (±12 dB, Q 0.01 to 24).");
            if (import.Preamp is { } preamp && preamp != 0)
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"The file's preamp of {preamp:0.0} dB isn't applied: the parametric EQ has no preamp. Lower the volume if you hear distortion."));
            string which = _peqChannel switch { 1 => " of the left channel", 2 => " of the right channel", _ => string.Empty };
            string question = $"Replace all bands{which} with the {used.Count} filter(s) from {files[0].Name}?";
            if (!await Dialogs.ConfirmAsync(this, string.Join("\n\n", [question, .. notes]), _scale)) return;

            for (int i = 0; i < PeqBandCount; i++)
            {
                PeqEdited[i] = i < used.Count ? used[i] : PeqDefaults[i] with { Filter = PeqFilter.Off };
                _peqDirty.Add((_peqChannel, i));
                UpdatePeqRow(i);
            }
            _peqGraph.InvalidateVisual();
            MarkEqCustom();
            if (!_peqSendTimer.IsEnabled) _peqSendTimer.Start();
        }

        private Border BuildPeqRow(int band)
        {
            var number = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1.5),
                BorderBrush = Palette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (band + 1).ToString(CultureInfo.InvariantCulture), FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            var filter = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var (f, label) in PeqFilterLabels) filter.Items.Add(ComboItem(f, label));
            var frequency = new NumberBox(PeqGraph.MinFrequency, PeqGraph.MaxFrequency, FormatFrequency,
                (f, d) => PeqGraph.RoundFrequency(f * Math.Pow(2, d / 24.0)));
            var gain = new NumberBox(PeqGraph.MinGain, PeqGraph.MaxGain, FormatGain, (g, d) => Math.Round(g + 0.5 * d, 1));
            var q = new NumberBox(PeqGraph.MinQ, PeqGraph.MaxQ, FormatQ, PeqGraph.NudgeQ);
            _peqFilters[band] = filter;
            _peqFrequencies[band] = frequency;
            _peqGains[band] = gain;
            _peqQs[band] = q;

            filter.SelectionChanged += (_, _) =>
            {
                if (_eqLoading || (filter.SelectedItem as ComboBoxItem)?.Tag is not PeqFilter f || f == PeqEdited[band].Filter) return;
                OnPeqBandChanged(band, PeqEdited[band] with { Filter = f }, fromGraph: false);
            };
            frequency.ValueChanged += (_, _) => OnPeqBandChanged(band, PeqEdited[band] with { Frequency = frequency.Value }, fromGraph: false);
            gain.ValueChanged += (_, _) => OnPeqBandChanged(band, PeqEdited[band] with { Gain = gain.Value }, fromGraph: false);
            q.ValueChanged += (_, _) => OnPeqBandChanged(band, PeqEdited[band] with { Q = q.Value }, fromGraph: false);

            var grid = PeqRowGrid();
            PlaceCell(grid, number, 0);
            PlaceCell(grid, filter, 1);
            PlaceCell(grid, frequency, 2);
            PlaceCell(grid, gain, 3);
            PlaceCell(grid, q, 4);
            var row = new Border { Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 3) };
            row.AddHandler(GotFocusEvent, (_, _) => SelectPeqBand(band), RoutingStrategies.Bubble, handledEventsToo: true);
            row.AddHandler(PointerPressedEvent, (_, _) => SelectPeqBand(band), RoutingStrategies.Tunnel, handledEventsToo: true);
            _peqRows[band] = row;
            return row;
        }

        private void OnPeqBandChanged(int band, PeqBand value, bool fromGraph)
        {
            if (_eqLoading) return;
            PeqEdited[band] = value;
            if (fromGraph) UpdatePeqRow(band);
            else
            {
                UpdatePeqRowState(band);
                _peqGraph.InvalidateVisual();
            }
            SelectPeqBand(band);
            _peqDirty.Add((_peqChannel, band));
            if (!_peqSendTimer.IsEnabled) _peqSendTimer.Start();
            MarkEqCustom();
        }

        private void UpdatePeqRow(int band)
        {
            bool wasLoading = _eqLoading;
            _eqLoading = true;
            var value = PeqEdited[band];
            _peqFilters[band].SelectedIndex = Array.FindIndex(PeqFilterLabels, f => f.Filter == value.Filter);
            _peqFrequencies[band].Value = value.Frequency;
            _peqGains[band].Value = value.Gain;
            _peqQs[band].Value = value.Q;
            _eqLoading = wasLoading;
            UpdatePeqRowState(band);
        }

        private void UpdatePeqRowState(int band)
        {
            bool editable = _eqAvailable && _chkEqEnabled.IsChecked == true && PeqEdited[band].Filter != PeqFilter.Off;
            _peqFrequencies[band].IsEnabled = _peqGains[band].IsEnabled = _peqQs[band].IsEnabled = editable;
        }

        private void UpdatePeqRowStates()
        {
            for (int i = 0; i < PeqBandCount; i++) UpdatePeqRowState(i);
        }

        private void SelectPeqBand(int band)
        {
            _peqSelected = band;
            _peqGraph.Selected = band;
            for (int i = 0; i < PeqBandCount; i++) _peqRows[i].Background = i == band ? SelectedRowBrush : Brushes.Transparent;
        }

        private void ShowPeqChannel(int channel)
        {
            _peqChannel = channel;
            _peqGraph.Bands = PeqEdited;
            for (int i = 0; i < PeqBandCount; i++) UpdatePeqRow(i);
            UpdatePeqChannelButtons();
        }

        private void UpdatePeqChannelButtons()
        {
            _peqChannelButtons.IsVisible = _peqSplit;
            _btnPeqLeft.Classes.Set("accent", _peqChannel == 1);
            _btnPeqRight.Classes.Set("accent", _peqChannel == 2);
        }

        private void ApplyPeqState(PeqState? peq)
        {
            if (peq != null)
            {
                _peqChannels[0] = [.. peq.Stereo];
                _peqChannels[1] = [.. peq.Left];
                _peqChannels[2] = [.. peq.Right];
                _peqSplit = peq.Split;
            }
            if (!_peqSplit) _peqChannel = 0;
            else if (_peqChannel == 0) _peqChannel = 1;
            _cboPeqChannels.SelectedIndex = _peqSplit ? 1 : 0;
            ShowPeqChannel(_peqChannel);
            SelectPeqBand(_peqSelected);
        }

        private static Button Segment(string text)
        {
            var button = new Button { Content = text };
            button.Classes.Add("segment");
            button.Classes.Add("solid");
            return button;
        }

        private static readonly TimeSpan SegmentSlideTime = TimeSpan.FromMilliseconds(220);

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

        private Control BuildEqTypeSwitch()
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
            grid.Children.Add(_btnGraphicEq);
            Grid.SetColumn(_btnParametricEq, 1);
            grid.Children.Add(_btnParametricEq);
            _btnGraphicEq.Click += async (_, _) => await SwitchEqTypeAsync(DefaultEqPlugin);
            _btnParametricEq.Click += async (_, _) => await SwitchEqTypeAsync(PeqPlugin);
            ToolTip.SetTip(_btnGraphicEq, "10-band graphic EQ");
            ToolTip.SetTip(_btnParametricEq, "Parametric EQ with filter type, frequency, gain and Q for each band");
            var border = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 14), IsVisible = false };
            border.Classes.Add("segments");
            AddSlidingThumb(border, grid, [_btnGraphicEq, _btnParametricEq]);
            return border;
        }

        private async Task SwitchEqTypeAsync(string plugin)
        {
            if (_eqLoading || plugin == _eqPluginUri) return;
            await SendPendingEqBandsAsync();
            _eqPluginUri = plugin;
            ShowEqEditor();
            if (_chkEqEnabled.IsChecked == true && !await _owner.SetEqTypeAsync(_eqDeviceIp, EqTarget, plugin))
                await Dialogs.MessageAsync(this, "The device didn't switch the EQ type.", _scale);
            await RefreshEqAsync();
        }

        private void ShowEqEditor()
        {
            _btnGraphicEq.Classes.Set("selected", !IsParametric);
            _btnParametricEq.Classes.Set("selected", IsParametric);
            var (shown, hidden) = IsParametric ? (_peqEditor, _geqEditor) : (_geqEditor, _peqEditor);
            if (hidden != null) hidden.IsVisible = false;
            if (shown == null || shown.IsVisible) return;
            shown.Opacity = 0;
            shown.IsVisible = true;
            Dispatcher.UIThread.Post(() => shown.Opacity = 1, DispatcherPriority.Background);
        }

        private async Task<bool> FlushPeqAsync()
        {
            _peqSendTimer.Stop();
            await _peqFlushGate.WaitAsync();
            try
            {
                while (_peqDirty.Count > 0)
                {
                    var dirty = _peqDirty.ToList();
                    var groups = dirty.GroupBy(d => d.Channel)
                        .Select(g => (Channel: g.Key, Bands: g.Select(d => d.Index).Order().Select(i => (i, _peqChannels[g.Key][i])).ToList()))
                        .ToList();
                    _peqDirty.Clear();
                    var failed = new List<int>();
                    foreach (var (channel, bands) in groups)
                        if (!await _owner.SetPeqBandsAsync(_eqDeviceIp, _eqSource, _peqSplit, channel, bands)) failed.Add(channel);
                    if (failed.Count == 0) continue;
                    foreach (var item in dirty.Where(d => failed.Contains(d.Channel))) _peqDirty.Add(item);
                    return false;
                }
                return true;
            }
            finally { _peqFlushGate.Release(); }
        }
    }
}
