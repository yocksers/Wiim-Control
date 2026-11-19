using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int GroupNameLength = 40;

        private readonly TextBox _txtGroupName = new() { Width = 240, MaxLength = GroupNameLength, Watermark = "For example Downstairs" };
        private readonly ComboBox _cboGroupLeader = new() { Width = 240 };
        private readonly StackPanel _groupMemberRows = new() { Spacing = 2 };
        private readonly StackPanel _savedGroupRows = new();
        private readonly HashSet<string> _groupMemberChoice = [];
        private readonly Button _btnSaveGroup = new() { Content = "Save group", Margin = new Thickness(0, 10, 0, 0) };
        private Border? _makeGroupCard;
        private bool _groupFormFilling;

        private Border MakeGroupCard()
        {
            if (_makeGroupCard == null)
            {
                _btnSaveGroup.Classes.Add("pill");
                _btnSaveGroup.Click += async (_, _) => await SaveNewGroupAsync();
                _cboGroupLeader.SelectionChanged += (_, _) => { if (!_groupFormFilling) FillGroupMembers(); };
                _makeGroupCard = Card("Saved groups",
                    Muted("Groups are saved on their leader, so they also show up in the WiiM Home app. Switch one on from Groups on the Devices page."),
                    _savedGroupRows,
                    new TextBlock { Text = "New group", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 6) },
                    LabeledRow("Name", _txtGroupName),
                    LabeledRow("Leader", _cboGroupLeader),
                    new TextBlock { Text = "Plays along", Classes = { "muted" }, Margin = new Thickness(0, 8, 0, 4) },
                    _groupMemberRows,
                    _btnSaveGroup);
            }
            FillSavedGroupRows();
            FillGroupLeaders();
            return _makeGroupCard;
        }

        private void FillSavedGroupRows()
        {
            var rows = new List<Control>();
            if (_savedGroups.Count == 0) rows.Add(Muted("No groups are saved yet."));
            foreach (var group in _savedGroups)
            {
                var g = group;
                var (delete, deleteIcon) = IconButton(Icons.Delete);
                deleteIcon.Width = deleteIcon.Height = 16;
                delete.Width = delete.Height = 30;
                delete.Padding = new Thickness(0);
                delete.MinHeight = 0;
                delete.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(delete, $"Delete {g.Name}");
                delete.Click += async (_, _) => await DeleteSavedGroupAsync(g);
                DockPanel.SetDock(delete, Dock.Right);
                string members = g.Members.Count == 0 ? "no other devices" : string.Join(", ", g.Members.Select(m => m.Name));
                rows.Add(new DockPanel
                {
                    Margin = new Thickness(0, 4, 0, 4),
                    Children =
                    {
                        delete,
                        new StackPanel
                        {
                            Children =
                            {
                                new TextBlock { Text = g.Name },
                                new TextBlock
                                {
                                    Text = $"Led by {DeviceName(g.LeaderIp)}  ·  with {members}", Classes = { "muted", "small" },
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        }
                    }
                });
            }
            SetRows(_savedGroupRows, rows);
        }

        private void FillGroupLeaders()
        {
            var devices = _owner._knownDevices.Where(d => d.Uuid.Length > 0).ToList();
            string? chosen = (_cboGroupLeader.SelectedItem as ComboBoxItem)?.Tag as string;
            _groupFormFilling = true;
            _cboGroupLeader.Items.Clear();
            foreach (var d in devices) _cboGroupLeader.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == (chosen ?? _owner.EffectiveGroupLeader));
            _cboGroupLeader.SelectedIndex = index >= 0 ? index : devices.Count > 0 ? 0 : -1;
            _groupFormFilling = false;
            FillGroupMembers();
        }

        private string? ChosenGroupLeader => (_cboGroupLeader.SelectedItem as ComboBoxItem)?.Tag as string;

        private void FillGroupMembers()
        {
            string? leader = ChosenGroupLeader;
            var rows = new List<Control>();
            foreach (var d in _owner._knownDevices.Where(d => d.Ip != leader && d.Uuid.Length > 0))
            {
                string ip = d.Ip;
                var check = new CheckBox { Content = d.Name, IsChecked = _groupMemberChoice.Contains(ip) };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (check.IsChecked == true) _groupMemberChoice.Add(ip);
                    else _groupMemberChoice.Remove(ip);
                };
                rows.Add(check);
            }
            if (rows.Count == 0) rows.Add(Muted("Add another WiiM device on the Devices page to make a group."));
            SetRows(_groupMemberRows, rows);
        }

        private async Task SaveNewGroupAsync()
        {
            string name = _txtGroupName.Text?.Trim() ?? string.Empty;
            string? leader = ChosenGroupLeader;
            var members = _owner._knownDevices.Where(d => d.Ip != leader && d.Uuid.Length > 0 && _groupMemberChoice.Contains(d.Ip)).ToList();
            string? problem = name.Length == 0 ? "Give the group a name."
                : leader == null ? "Choose the device that leads the group."
                : members.Count == 0 ? "Choose at least one device to play along with the leader."
                : _savedGroups.Any(g => g.LeaderIp == leader && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                    ? $"{DeviceName(leader)} already has a group called {name}."
                    : null;
            if (problem != null)
            {
                await Dialogs.MessageAsync(this, problem, _scale);
                return;
            }

            _btnSaveGroup.IsEnabled = false;
            _btnSaveGroup.Content = "Saving…";
            bool ok = await _owner.SaveNewGroupAsync(leader!, name, members);
            _btnSaveGroup.Content = "Save group";
            _btnSaveGroup.IsEnabled = true;
            if (!ok)
            {
                await Dialogs.MessageAsync(this, $"{DeviceName(leader!)} didn't save the group. Check that it's switched on and reachable.", _scale);
                return;
            }
            _txtGroupName.Text = string.Empty;
            _groupMemberChoice.Clear();
            await ReloadSavedGroupsAsync();
        }

        private async Task DeleteSavedGroupAsync(SavedGroup group)
        {
            if (!await Dialogs.ConfirmAsync(this, $"Delete the group {group.Name}? It's also removed from the WiiM Home app. Devices that are playing together stay linked.", _scale))
                return;
            if (!await _owner.DeleteSavedGroupAsync(group))
            {
                await Dialogs.MessageAsync(this, $"{DeviceName(group.LeaderIp)} didn't delete the group. Check that it's switched on and reachable.", _scale);
                return;
            }
            await ReloadSavedGroupsAsync();
        }

        private async Task ReloadSavedGroupsAsync()
        {
            _savedGroups = await _owner.GetSavedGroupsAsync();
            if (_makeGroupCard != null)
            {
                FillSavedGroupRows();
                FillGroupMembers();
            }
            RebuildDeviceCards();
        }
    }
}
