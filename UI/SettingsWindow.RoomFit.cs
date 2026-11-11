using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class EqPage
        {
            private readonly Button _btnRoomFitCurve = Pill("Show curve");
            private readonly Button _btnRoomFitDelete = Pill("Delete");
            private readonly ToggleSwitch _chkRoomFit = new() { VerticalAlignment = VerticalAlignment.Center };
            private readonly ComboBox _cboRoomFit = new() { Width = 240, PlaceholderText = "No profile selected" };
            private readonly ComboBox _cboRoomFitAmp = new() { Width = DevicePickerWidth };
            private readonly TextBlock _lblRoomFitStatus = Subtitle();
            private readonly Border _roomFitCard = new() { IsVisible = false };
            private readonly Border _roomFitMissing = new() { IsVisible = false };
            private string _roomFitIp = string.Empty;
            private bool _roomFitLoading;
            private bool _roomFitBuilt;
            private int _roomFitVersion;

            public Control BuildRoomFit()
            {
                var btnRefresh = Pill("Refresh");
                btnRefresh.Click += async (_, _) => await RefreshRoomFitAsync();
                var row = LabeledRow("RoomFit room correction", _chkRoomFit);
                var profileRow = LabeledRow("Profile", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { _btnRoomFitExport, _btnRoomFitImport, _btnRoomFitCurve, _btnRoomFitDelete, _cboRoomFit }
                });
                var card = Card(null, row, profileRow, Muted("Measured with the WiiM Home app. Applies to every input, on top of the EQ."));
                _roomFitCard.Child = card;
                var missing = Card(null, Muted("This device has no RoomFit room correction."));
                _roomFitMissing.Child = missing;
                ToolTip.SetTip(_btnRoomFitExport, "Save the active RoomFit profile to a file");
                ToolTip.SetTip(_btnRoomFitImport, "Load a RoomFit profile from a file and save it on the device");
                ToolTip.SetTip(_btnRoomFitDelete, "Delete the selected RoomFit profile from the device");
                ToolTip.SetTip(_cboRoomFit, "The RoomFit profile that's used");
                ToolTip.SetTip(_cboRoomFitAmp, "The device whose RoomFit is shown");
                _btnRoomFitExport.Click += async (_, _) => await ExportRoomFitAsync();
                _btnRoomFitImport.Click += async (_, _) => await ImportRoomFitAsync();
                _btnRoomFitCurve.Click += async (_, _) => await ShowRoomFitCurveAsync();
                _btnRoomFitDelete.Click += async (_, _) => await DeleteRoomFitProfileAsync();
                _chkRoomFit.IsCheckedChanged += async (_, _) =>
                {
                    if (_roomFitLoading) return;
                    if (!await window._owner.SetRoomFitEnabledAsync(_roomFitIp, _chkRoomFit.IsChecked == true)) await RefreshRoomFitAsync();
                };
                _cboRoomFit.SelectionChanged += async (_, _) =>
                {
                    _btnRoomFitDelete.IsEnabled = _cboRoomFit.SelectedItem is ComboBoxItem;
                    if (_roomFitLoading || (_cboRoomFit.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                    if (!await window._owner.LoadRoomFitProfileAsync(_roomFitIp, name, _chkRoomFit.IsChecked == true)) await RefreshRoomFitAsync();
                };
                _cboRoomFitAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshRoomFitAsync(); };
                _roomFitBuilt = true;
                return window.Page("RoomFit", _lblRoomFitStatus, [_cboRoomFitAmp, btnRefresh],
                    new StackPanel { Children = { _roomFitCard, _roomFitMissing } });
            }

            public void FillRoomFitAmpCombo()
            {
                string? chosen = AmpOf(_cboRoomFitAmp);
                string selected = chosen == null || window._followActive ? window._owner._deviceIp : chosen;
                _cboRoomFitAmp.Items.Clear();
                foreach (var d in window._owner._knownDevices) _cboRoomFitAmp.Items.Add(ComboItem(d.Ip, d.Name));
                int index = window._owner._knownDevices.FindIndex(d => d.Ip == selected);
                _cboRoomFitAmp.SelectedIndex = index >= 0 ? index : _cboRoomFitAmp.Items.Count > 0 ? 0 : -1;
            }

            public async Task RefreshRoomFitAsync()
            {
                if (!_roomFitBuilt) return;
                int version = ++_roomFitVersion;
                _lblRoomFitStatus.Text = "Loading…";
                string ip = AmpOf(_cboRoomFitAmp) ?? window._owner._deviceIp;
                var state = await window._owner.GetRoomFitStateAsync(ip);
                if (version != _roomFitVersion) return;
                _roomFitIp = ip;
                _roomFitLoading = true;
                _cboRoomFit.Items.Clear();
                if (state != null)
                {
                    foreach (var p in state.Profiles) _cboRoomFit.Items.Add(ComboItem(p, p));
                    _cboRoomFit.SelectedIndex = state.Profiles.IndexOf(state.Active);
                    _chkRoomFit.IsChecked = state.Enabled;
                }
                _roomFitCard.IsVisible = state != null;
                _roomFitMissing.IsVisible = state == null;
                _btnRoomFitDelete.IsEnabled = _cboRoomFit.SelectedItem is ComboBoxItem;
                _lblRoomFitStatus.Text = state == null ? string.Empty : state.Enabled ? "On" : "Off";
                _roomFitLoading = false;
            }

            private async Task DeleteRoomFitProfileAsync()
            {
                if ((_cboRoomFit.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                if (!await Dialogs.ConfirmAsync(window, $"Delete the RoomFit profile \"{name}\" from {window.DeviceName(_roomFitIp)}? This can't be undone.", window._scale))
                    return;
                if (!await window._owner.DeleteRoomFitProfileAsync(_roomFitIp, name))
                    await Dialogs.MessageAsync(window, "The device didn't delete the RoomFit profile.", window._scale);
                await RefreshRoomFitAsync();
            }

            private async Task ShowRoomFitCurveAsync()
            {
                var state = await window._owner.GetRoomFitStateAsync(_roomFitIp);
                if (state == null)
                {
                    await Dialogs.MessageAsync(window, "Couldn't read RoomFit from the device.", window._scale);
                    return;
                }

                string name = state.Active.Length > 0 ? state.Active : "No profile selected";
                var content = new StackPanel { Width = 680, Spacing = 6 };
                content.Children.Add(new TextBlock { Text = name, Classes = { "heading" } });
                content.Children.Add(Muted(state.Enabled
                    ? "The correction the device applies now. To see another profile, pick it in RoomFit profile first."
                    : "RoomFit is off, so the device doesn't apply this correction now."));
                var channels = state.Left is { } left && state.Right is { } right
                    ? new[] { ("Left", left), ("Right", right) }
                    : [(string.Empty, state.Stereo)];
                content.Children.Add(Muted("Scroll over a graph to zoom in, hold Shift while scrolling to move along, and double-click it to see the whole range."));
                var graphs = channels.Select(c => new PeqGraph { Bands = c.Item2, Height = 240, Editable = false }).ToArray();
                for (int i = 0; i < channels.Length; i++)
                {
                    var (label, bands) = channels[i];
                    if (label.Length > 0)
                        content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
                    content.Children.Add(graphs[i]);
                    content.Children.Add(RoomFitFilterTable(bands));
                }
                await Dialogs.ContentAsync(window, new ScrollViewer { Content = content, MaxHeight = 720 }, window._scale);
            }

            private static Control RoomFitFilterTable(PeqBand[] bands)
            {
                var active = bands.Select((b, i) => (Band: b, Number: i + 1)).Where(b => b.Band.Filter != PeqFilter.Off).ToList();
                if (active.Count == 0) return Muted("No filters.");

                var table = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,*,*,*"), Margin = new Thickness(0, 6, 0, 0) };
                void Cell(string text, int row, int column, bool muted = false)
                {
                    var cell = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2) };
                    if (muted) cell.Classes.Add("muted");
                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, column);
                    table.Children.Add(cell);
                }

                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                string[] titles = ["", "Filter", "Frequency", "Gain", "Q"];
                for (int c = 0; c < titles.Length; c++) Cell(titles[c], 0, c, muted: true);
                for (int r = 0; r < active.Count; r++)
                {
                    var (band, number) = active[r];
                    table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    Cell(number.ToString(System.Globalization.CultureInfo.InvariantCulture), r + 1, 0, muted: true);
                    Cell(PeqFilterLabels.FirstOrDefault(f => f.Filter == band.Filter).Label ?? "Peak", r + 1, 1);
                    Cell(FormatFrequency(band.Frequency), r + 1, 2);
                    Cell(FormatGain(band.Gain), r + 1, 3);
                    Cell(FormatQ(band.Q), r + 1, 4);
                }
                return table;
            }
        }
    }
}
