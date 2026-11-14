using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class EqPage
        {
            private readonly PeqBand[][] _rfChannels = [[], [], []];
            private readonly HashSet<(int Channel, int Index)> _rfDirty = [];
            private readonly DispatcherTimer _rfSendTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
            private readonly SemaphoreSlim _rfFlushGate = new(1, 1);
            private readonly Button _btnRfSave = Pill("Save", accent: true);
            private readonly Button _btnRfSaveAs = Pill("Save as");
            private readonly Button _btnRfUndo = Pill("Undo changes");
            private bool _rfSplit;
            private bool _rfUpdating;
            private bool _rfHooked;
            private string? _rfUnsaved;
            private string _rfProfile = string.Empty;
            private List<string> _rfProfiles = [];

            private string RoomFitEditKey(string profile) => $"{_roomFitIp}\n{profile}";

            private void HookRoomFitEditor()
            {
                if (_rfHooked) return;
                _rfHooked = true;
                _rfSendTimer.Tick += async (_, _) => await FlushRoomFitAsync();
                _btnRfSave.Click += async (_, _) => await SaveRoomFitAsync(_rfProfile);
                _btnRfSaveAs.Click += async (_, _) => await SaveRoomFitAsAsync();
                _btnRfUndo.Click += async (_, _) => await UndoRoomFitAsync();
                ToolTip.SetTip(_btnRfSave, "Save the changes in this RoomFit profile on the device");
                ToolTip.SetTip(_btnRfSaveAs, "Save the changes as a new RoomFit profile, and keep this one as it was");
                ToolTip.SetTip(_btnRfUndo, "Go back to the profile as it's saved on the device");
                window.Closed += (_, _) => _rfSendTimer.Stop();
            }

            private Border BuildRoomFitCurveCard(RoomFitState state)
            {
                HookRoomFitEditor();
                _rfDirty.Clear();
                _rfProfile = state.Active;
                _rfProfiles = state.Profiles;
                _rfSplit = state.Left != null && state.Right != null;
                var channels = _rfSplit
                    ? new[] { (Channel: 1, Label: "Left", Bands: state.Left!), (Channel: 2, Label: "Right", Bands: state.Right!) }
                    : [(Channel: 0, Label: string.Empty, Bands: state.Stereo)];
                foreach (var (channel, _, bands) in channels) _rfChannels[channel] = [.. bands];

                var content = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                        new RowDefinition(1, GridUnitType.Star) { MinHeight = 130, MaxHeight = 260 },
                        new RowDefinition(12, GridUnitType.Pixel),
                        new RowDefinition(1.2, GridUnitType.Star) { MinHeight = 90 }
                    }
                };
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                heading.Children.Add(new TextBlock { Text = $"Correction: {state.Active}", Classes = { "heading" }, VerticalAlignment = VerticalAlignment.Center });
                foreach (var button in new[] { _btnRfUndo, _btnRfSaveAs, _btnRfSave }) (button.Parent as Panel)?.Children.Remove(button);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _btnRfUndo, _btnRfSaveAs, _btnRfSave } };
                Grid.SetColumn(buttons, 1);
                heading.Children.Add(buttons);
                content.Children.Add(heading);
                var hint = Muted("Scroll over a graph to zoom in, hold Shift while scrolling to move along, and double-click it to see the whole range.");
                Grid.SetRow(hint, 1);
                content.Children.Add(hint);
                var graphs = new Grid { ColumnSpacing = 16 };
                var tables = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                for (int i = 0; i < channels.Length; i++)
                {
                    var (channel, label, _) = channels[i];
                    var (graph, table) = BuildRoomFitChannelEditor(channel);
                    graphs.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                    Control graphCell = graph;
                    if (label.Length > 0)
                    {
                        var caption = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                        DockPanel.SetDock(caption, Dock.Top);
                        graphCell = new DockPanel { Children = { caption, graph } };
                        tables.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0) });
                    }
                    Grid.SetColumn(graphCell, i);
                    graphs.Children.Add(graphCell);
                    tables.Children.Add(table);
                }
                Grid.SetRow(graphs, 2);
                content.Children.Add(graphs);
                var scroll = new ScrollViewer { Content = tables, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                Grid.SetRow(scroll, 4);
                content.Children.Add(scroll);
                ShowRoomFitSaved(_rfUnsaved != RoomFitEditKey(state.Active));
                var card = new Border { Child = content };
                card.Classes.Add("card");
                return card;
            }

            private (PeqGraph Graph, Control Table) BuildRoomFitChannelEditor(int channel)
            {
                var bands = _rfChannels[channel];
                var graph = new PeqGraph { Bands = bands, Editable = true };
                var filters = new ComboBox[bands.Length];
                var frequencies = new NumberBox[bands.Length];
                var gains = new NumberBox[bands.Length];
                var qs = new NumberBox[bands.Length];
                var rows = new Border[bands.Length];

                void ShowRow(int band)
                {
                    _rfUpdating = true;
                    var value = bands[band];
                    filters[band].SelectedIndex = Array.FindIndex(PeqFilterLabels, f => f.Filter == value.Filter);
                    frequencies[band].Value = value.Frequency;
                    gains[band].Value = value.Gain;
                    qs[band].Value = value.Q;
                    frequencies[band].IsEnabled = gains[band].IsEnabled = qs[band].IsEnabled = value.Filter != PeqFilter.Off;
                    _rfUpdating = false;
                }
                void Select(int band)
                {
                    graph.Selected = band;
                    for (int i = 0; i < rows.Length; i++) rows[i].Background = i == band ? SelectedRowBrush : Brushes.Transparent;
                }
                void Changed(int band, PeqBand value, bool fromGraph)
                {
                    if (_rfUpdating) return;
                    bands[band] = value;
                    if (fromGraph) ShowRow(band);
                    else
                    {
                        frequencies[band].IsEnabled = gains[band].IsEnabled = qs[band].IsEnabled = value.Filter != PeqFilter.Off;
                        graph.InvalidateVisual();
                    }
                    Select(band);
                    _rfDirty.Add((channel, band));
                    _rfUnsaved = RoomFitEditKey(_rfProfile);
                    ShowRoomFitSaved(false);
                    if (!_rfSendTimer.IsEnabled) _rfSendTimer.Start();
                }

                var header = PeqRowGrid();
                header.Margin = new Thickness(0, 6, 0, 4);
                string[] titles = ["", "Filter", "Frequency", "Gain", "Q"];
                for (int c = 0; c < titles.Length; c++)
                    PlaceCell(header, new TextBlock { Text = titles[c], Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center }, c);
                var table = new StackPanel { Children = { header } };
                for (int i = 0; i < bands.Length; i++)
                {
                    int band = i;
                    var filter = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    foreach (var (f, label) in PeqFilterLabels) filter.Items.Add(ComboItem(f, label));
                    var frequency = new NumberBox(PeqGraph.MinFrequency, PeqGraph.MaxFrequency, FormatFrequency,
                        (f, d) => PeqGraph.RoundFrequency(f * Math.Pow(2, d / 24.0)));
                    var gain = new NumberBox(PeqGraph.MinGain, PeqGraph.MaxGain, FormatGain, (g, d) => Math.Round(g + 0.5 * d, 1));
                    var q = new NumberBox(PeqGraph.MinQ, PeqGraph.MaxQ, FormatQ, PeqGraph.NudgeQ);
                    filters[band] = filter;
                    frequencies[band] = frequency;
                    gains[band] = gain;
                    qs[band] = q;
                    filter.SelectionChanged += (_, _) =>
                    {
                        if ((filter.SelectedItem as ComboBoxItem)?.Tag is PeqFilter f && f != bands[band].Filter) Changed(band, bands[band] with { Filter = f }, false);
                    };
                    frequency.ValueChanged += (_, _) => Changed(band, bands[band] with { Frequency = frequency.Value }, false);
                    gain.ValueChanged += (_, _) => Changed(band, bands[band] with { Gain = gain.Value }, false);
                    q.ValueChanged += (_, _) => Changed(band, bands[band] with { Q = q.Value }, false);
                    var number = new TextBlock
                    {
                        Text = (band + 1).ToString(CultureInfo.InvariantCulture), Classes = { "muted" },
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    var grid = PeqRowGrid();
                    PlaceCell(grid, number, 0);
                    PlaceCell(grid, filter, 1);
                    PlaceCell(grid, frequency, 2);
                    PlaceCell(grid, gain, 3);
                    PlaceCell(grid, q, 4);
                    var row = new Border { Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 3) };
                    row.AddHandler(GotFocusEvent, (_, _) => Select(band), RoutingStrategies.Bubble, handledEventsToo: true);
                    row.AddHandler(PointerPressedEvent, (_, _) => Select(band), RoutingStrategies.Tunnel, handledEventsToo: true);
                    rows[band] = row;
                    table.Children.Add(row);
                    ShowRow(band);
                }
                graph.BandChanged += (band, value) => Changed(band, value, true);
                graph.SelectedChanged += Select;
                return (graph, table);
            }

            private void ShowRoomFitSaved(bool saved)
            {
                _btnRfSave.IsEnabled = _btnRfUndo.IsEnabled = !saved;
                _lblRoomFitStatus.Text = (_chkRoomFit.IsChecked == true ? "On" : "Off") + (saved ? string.Empty : " · changed, not saved");
            }

            private async Task<bool> FlushRoomFitAsync()
            {
                _rfSendTimer.Stop();
                await _rfFlushGate.WaitAsync();
                try
                {
                    while (_rfDirty.Count > 0)
                    {
                        var dirty = _rfDirty.ToList();
                        _rfDirty.Clear();
                        foreach (var group in dirty.GroupBy(d => d.Channel))
                        {
                            var bands = group.Select(d => d.Index).Order().Select(i => (i, _rfChannels[group.Key][i])).ToList();
                            if (await window._owner.SetRoomFitBandsAsync(_roomFitIp, _rfSplit, group.Key, bands)) continue;
                            foreach (var item in dirty) _rfDirty.Add(item);
                            await Dialogs.MessageAsync(window, "The device didn't accept the change to the RoomFit filters.", window._scale);
                            return false;
                        }
                    }
                    return true;
                }
                finally { _rfFlushGate.Release(); }
            }

            private async Task SaveRoomFitAsync(string name)
            {
                if (name.Length == 0 || !await FlushRoomFitAsync()) return;
                _lblRoomFitStatus.Text = "Saving…";
                if (!await window._owner.SaveRoomFitProfileAsync(_roomFitIp, name, _chkRoomFit.IsChecked == true))
                {
                    await Dialogs.MessageAsync(window, "The device didn't save the RoomFit profile.", window._scale);
                    ShowRoomFitSaved(false);
                    return;
                }
                _rfUnsaved = null;
                await RefreshRoomFitAsync();
            }

            private async Task SaveRoomFitAsAsync()
            {
                string? name = (await Dialogs.PromptAsync(window, "Name for the new RoomFit profile:", $"{_rfProfile} (edited)", window._scale))?.Trim();
                if (string.IsNullOrEmpty(name)) return;
                if (_rfProfiles.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                    !await Dialogs.ConfirmAsync(window, $"There's already a RoomFit profile called \"{name}\". Replace it?", window._scale)) return;
                await SaveRoomFitAsync(name);
            }

            private async Task UndoRoomFitAsync()
            {
                _rfSendTimer.Stop();
                _rfDirty.Clear();
                if (_rfProfile.Length > 0 && !await window._owner.LoadRoomFitProfileAsync(_roomFitIp, _rfProfile, _chkRoomFit.IsChecked == true))
                {
                    await Dialogs.MessageAsync(window, "The device didn't reload the RoomFit profile.", window._scale);
                    return;
                }
                _rfUnsaved = null;
                await RefreshRoomFitAsync();
            }
        }
    }
}
