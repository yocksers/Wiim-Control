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
            private readonly Button _btnRoomFitDelete = Pill("Delete");
            private readonly ToggleSwitch _chkRoomFit = new() { VerticalAlignment = VerticalAlignment.Center };
            private readonly ComboBox _cboRoomFit = new() { Width = 240, PlaceholderText = "No profile selected" };
            private readonly ComboBox _cboRoomFitAmp = new() { Width = DevicePickerWidth };
            private readonly TextBlock _lblRoomFitStatus = Subtitle();
            private readonly Border _roomFitCard = new() { IsVisible = false };
            private readonly Border _roomFitMissing = new() { IsVisible = false };
            private readonly Border _roomFitCurve = new();
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
                    Children = { _btnRoomFitExport, _btnRoomFitImport, _btnRoomFitDelete, _cboRoomFit }
                });
                var card = Card(null, row, profileRow);
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
                    if (_rfUnsaved == RoomFitEditKey(_rfProfile) && name != _rfProfile)
                    {
                        if (!await Dialogs.ConfirmAsync(window, $"Your changes to \"{_rfProfile}\" aren't saved. Switch to \"{name}\" and lose them?",
                                window._scale))
                        {
                            _roomFitLoading = true;
                            _cboRoomFit.SelectedItem = _cboRoomFit.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == _rfProfile);
                            _roomFitLoading = false;
                            return;
                        }
                    }
                    _rfSendTimer.Stop();
                    _rfDirty.Clear();
                    _rfUnsaved = null;
                    if (!await window._owner.LoadRoomFitProfileAsync(_roomFitIp, name, _chkRoomFit.IsChecked == true))
                        await Dialogs.MessageAsync(window, "The device didn't switch to that RoomFit profile.", window._scale);
                    await RefreshRoomFitAsync();
                };
                _cboRoomFitAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshRoomFitAsync(); };
                _roomFitBuilt = true;
                var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
                Grid.SetRow(_roomFitMissing, 1);
                Grid.SetRow(_roomFitCurve, 2);
                body.Children.AddRange([_roomFitCard, _roomFitMissing, _roomFitCurve]);
                return window.Page("RoomFit", _lblRoomFitStatus, [_cboRoomFitAmp, btnRefresh], body, scroll: false);
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
                _roomFitCurve.Child = state is { Active.Length: > 0 } ? BuildRoomFitCurveCard(state) : null;
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
        }
    }
}
