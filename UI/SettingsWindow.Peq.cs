using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly IBrush SelectedRowBrush = new SolidColorBrush(Color.Parse("#2C2C55"));
        private static readonly (PeqFilter Filter, string Label)[] PeqFilterLabels =
            [(PeqFilter.Off, "Off"), (PeqFilter.LowShelf, "Low shelf"), (PeqFilter.Peak, "Peak"), (PeqFilter.HighShelf, "High shelf")];

        private readonly ComboBox _cboEqType = new() { Width = 240 };
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
        private bool _peqSending;
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
                if (!await _owner.SetPeqSplitAsync(_eqSource, split))
                    await Dialogs.MessageAsync(this, "The amp didn't change the channel setting.", _scale);
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
            var channelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 4) };
            channelRow.Children.Add(_cboPeqChannels);
            Grid.SetColumn(_peqChannelButtons, 1);
            channelRow.Children.Add(_peqChannelButtons);
            Grid.SetColumn(btnReset, 2);
            channelRow.Children.Add(btnReset);
            var hint = new TextBlock
            {
                Text = "Applies to the selected input. Drag a point in the graph to change its frequency and gain, " +
                       "scroll over it to change Q and double-click it to reset its gain.",
                Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10)
            };

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
                    new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                    new RowDefinition(1.2, GridUnitType.Star) { MinHeight = 90 }
                }
            };
            grid.Children.Add(_peqGraph);
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);
            Grid.SetRow(channelRow, 2);
            grid.Children.Add(channelRow);
            Grid.SetRow(scroll, 3);
            grid.Children.Add(scroll);
            var card = new Border { Child = grid, IsVisible = false };
            card.Classes.Add("card");

            _eqEditControls.AddRange([_peqGraph, _cboPeqChannels, _btnPeqLeft, _btnPeqRight, btnReset, .. _peqFilters]);
            _peqGraph.Bands = PeqEdited;
            UpdatePeqChannelButtons();
            return card;
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

        private void ShowEqEditor()
        {
            if (_geqEditor != null) _geqEditor.IsVisible = !IsParametric;
            if (_peqEditor != null) _peqEditor.IsVisible = IsParametric;
        }

        private async Task FlushPeqAsync()
        {
            if (_peqSending) return;
            _peqSendTimer.Stop();
            if (_peqDirty.Count == 0) return;

            var groups = _peqDirty.GroupBy(d => d.Channel)
                .Select(g => (Channel: g.Key, Bands: g.Select(d => d.Index).Order().Select(i => (i, _peqChannels[g.Key][i])).ToList()))
                .ToList();
            _peqDirty.Clear();

            _peqSending = true;
            try
            {
                foreach (var (channel, bands) in groups) await _owner.SetPeqBandsAsync(_eqSource, _peqSplit, channel, bands);
            }
            finally { _peqSending = false; }
        }
    }
}
