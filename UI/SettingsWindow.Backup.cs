using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly TextBlock _lblBackupStatus = new()
        {
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false
        };
        private bool _backupBusy;

        private static readonly FilePickerFileType BackupFileType = new("Wiim Control backup") { Patterns = ["*.json"] };

        private Border BuildBackupCard()
        {
            var backup = Pill("Back up");
            var restore = Pill("Restore");
            var report = Pill("Device report");
            backup.Click += async (_, _) => await BackupAsync();
            restore.Click += async (_, _) => await RestoreAsync();
            report.Click += async (_, _) => await SaveDeviceReportAsync(report);
            ToolTip.SetTip(report, "Saves how your WiiM devices answer Wiim Control. No personal or identifiable details are included, such as network " +
                                   "names, addresses, device names or what's playing, and nothing on the devices is changed.");
            return Card("Backup and restore",
                Muted("Saves Wiim Control's settings and each device's own EQ presets, parametric EQ presets and RoomFit profiles to a file. " +
                      "Restore them after a reset or on a new PC, or copy presets from one device to another."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0), Children = { backup, restore, report } },
                Muted("Device report is only needed if you're asked for it, to help improve Wiim Control for WiiM models the developer doesn't own. " +
                      "No personal or identifiable details are shared."),
                _lblBackupStatus, _lblReportStatus);
        }

        private static string Plural(int count, string one, string many) =>
            count == 1 ? $"1 {one}" : string.Create(CultureInfo.InvariantCulture, $"{count} {many}");

        private static string Contents(AmpBackup amp)
        {
            var parts = new List<string>();
            if (amp.GraphicEq.Count > 0) parts.Add(Plural(amp.GraphicEq.Count, "EQ preset", "EQ presets"));
            if (amp.ParametricEq.Count > 0) parts.Add(Plural(amp.ParametricEq.Count, "parametric EQ preset", "parametric EQ presets"));
            if (amp.RoomFit.Count > 0) parts.Add(Plural(amp.RoomFit.Count, "RoomFit profile", "RoomFit profiles"));
            return parts.Count > 0 ? string.Join(", ", parts) : "no presets of its own";
        }

        private void ShowBackupStatus(string text)
        {
            _lblBackupStatus.Text = text;
            _lblBackupStatus.IsVisible = text.Length > 0;
        }

        private async Task BackupAsync()
        {
            if (_backupBusy) return;
            if (!await Dialogs.ConfirmAsync(this,
                    "To read the EQ presets, Wiim Control loads each one on an input that isn't playing and then puts that input back as it was.\n\n" +
                    "RoomFit profiles apply to every input, so if RoomFit is on, the sound may change for a moment while they're read. " +
                    "The current profile is put back afterwards.\n\nContinue?", _scale)) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save backup",
                SuggestedFileName = string.Create(CultureInfo.InvariantCulture, $"WiimControl-backup-{DateTime.Now:yyyy-MM-dd}.json"),
                DefaultExtension = "json",
                FileTypeChoices = [BackupFileType]
            });
            if (file == null) return;

            _backupBusy = true;
            try
            {
                var amps = new List<AmpBackup>();
                var missing = new List<string>();
                foreach (var device in _owner._knownDevices.ToList())
                {
                    ShowBackupStatus($"Reading {device.Name}…");
                    if (await _owner.BackupAmpAsync(device) is { } amp) amps.Add(amp);
                    else missing.Add(device.Name);
                }
                var backup = _owner.CreateBackupFile(amps);
                await using (var stream = await file.OpenWriteAsync())
                {
                    if (stream.CanSeek) stream.SetLength(0);
                    await JsonSerializer.SerializeAsync(stream, backup, BackupJson);
                }
                if (IsVisible && _eqDeviceIp.Length > 0) _ = RefreshEqAsync();

                var lines = new List<string> { $"Saved to {file.Name}:", "• Wiim Control's settings" };
                lines.AddRange(amps.Select(a => $"• {a.Name}: {Contents(a)}"));
                if (missing.Count > 0) lines.Add($"\nNot reachable, so not saved: {string.Join(", ", missing)}.");
                ShowBackupStatus(string.Create(CultureInfo.InvariantCulture, $"Backed up {DateTime.Now:t} to {file.Name}."));
                await Dialogs.MessageAsync(this, string.Join("\n", lines), _scale);
            }
            catch (Exception ex)
            {
                ShowBackupStatus(string.Empty);
                await Dialogs.MessageAsync(this, $"Couldn't save the backup: {ex.Message}", _scale);
            }
            finally
            {
                _backupBusy = false;
            }
        }

        private sealed record AmpRestoreChoice(AmpBackup Amp, CheckBox? Equalizer, CheckBox? RoomFit, ComboBox Target);

        private static CheckBox WrappedCheck(string text, bool isChecked) => new()
        {
            IsChecked = isChecked, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }
        };

        private async Task RestoreAsync()
        {
            if (_backupBusy) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Restore from backup", FileTypeFilter = [BackupFileType, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;

            BackupFile? backup;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                backup = ParseBackupFile(await reader.ReadToEndAsync());
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, $"Couldn't read the file: {ex.Message}", _scale);
                return;
            }
            if (backup == null)
            {
                await Dialogs.MessageAsync(this, "This file isn't a Wiim Control backup.", _scale);
                return;
            }
            await RestoreFromAsync(backup);
        }

        private async Task RestoreFromAsync(BackupFile backup)
        {
            var content = new StackPanel { Spacing = 10, Width = 480 };
            content.Children.Add(new TextBlock
            {
                Text = string.Create(CultureInfo.CurrentCulture, $"Backup from {backup.Created:g} (Wiim Control {backup.Version})"),
                Classes = { "heading" }, TextWrapping = TextWrapping.Wrap
            });
            var settings = WrappedCheck("Wiim Control's settings: devices, hotkeys, radio favourites and all other settings. " +
                                        "Wiim Control restarts to use them.", false);
            settings.IsEnabled = backup.Settings.Length > 0;
            content.Children.Add(settings);

            var choices = new List<AmpRestoreChoice>();
            var devices = _owner._knownDevices.ToList();
            foreach (var amp in backup.Amps.Where(a => a.Count > 0))
            {
                var target = new ComboBox { Width = 220 };
                target.Items.Add(ComboItem(string.Empty, "Don't restore"));
                foreach (var d in devices) target.Items.Add(ComboItem(d.Ip, d.Name));
                int match = devices.FindIndex(d => amp.Uuid.Length > 0 && d.Uuid == amp.Uuid);
                if (match < 0) match = devices.FindIndex(d => d.Ip == amp.Ip);
                target.SelectedIndex = match + 1;

                var box = new StackPanel { Spacing = 4 };
                box.Children.Add(new TextBlock { Text = $"{amp.Name}: {Contents(amp)}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                var equalizer = amp.GraphicEq.Count + amp.ParametricEq.Count > 0 ? WrappedCheck("EQ presets", true) : null;
                var roomFit = amp.RoomFit.Count > 0 ? WrappedCheck("RoomFit profiles", true) : null;
                if (equalizer != null) box.Children.Add(equalizer);
                if (roomFit != null) box.Children.Add(roomFit);
                box.Children.Add(LabeledRow("Restore to", target));
                content.Children.Add(new Border { Child = box, Classes = { "card" }, Margin = new Thickness(0) });
                choices.Add(new AmpRestoreChoice(amp, equalizer, roomFit, target));
            }
            if (choices.Count > 0 && devices.Count == 0)
                content.Children.Add(Muted("Add your devices first, or restore the settings first, to restore their presets."));
            if (choices.Count > 0) content.Children.Add(Muted("Presets and profiles with the same name are replaced."));

            if (!await Dialogs.ChooseAsync(this, new ScrollViewer { Content = content, MaxHeight = 560 }, "Restore", _scale)) return;

            _backupBusy = true;
            var lines = new List<string>();
            try
            {
                foreach (var choice in choices)
                {
                    if ((choice.Target.SelectedItem as ComboBoxItem)?.Tag is not string ip || ip.Length == 0) continue;
                    bool equalizer = choice.Equalizer?.IsChecked == true, roomFit = choice.RoomFit?.IsChecked == true;
                    if (!equalizer && !roomFit) continue;
                    string name = devices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
                    ShowBackupStatus($"Restoring to {name}…");
                    var result = await _owner.RestoreAmpAsync(ip, choice.Amp, equalizer, roomFit);
                    lines.Add(result.Failed == 0
                        ? $"• {name}: {Plural(result.Restored, "preset or profile", "presets and profiles")} restored"
                        : $"• {name}: {result.Restored} restored, {result.Failed} not accepted by the device");
                }
                ShowBackupStatus(string.Empty);
                if (IsVisible && _eqDeviceIp.Length > 0) await RefreshEqAsync();

                if (settings.IsChecked == true && _owner.WriteConfigText(backup.Settings))
                {
                    lines.Add(_owner.CanRestart
                        ? "• Wiim Control's settings: restored. Wiim Control restarts now."
                        : "• Wiim Control's settings: restored. Wiim Control closes now; start it again to use them.");
                    await Dialogs.MessageAsync(this, string.Join("\n", lines), _scale);
                    _owner.Restart();
                    return;
                }
                if (lines.Count == 0) lines.Add("Nothing was selected to restore.");
                await Dialogs.MessageAsync(this, string.Join("\n", lines), _scale);
            }
            finally
            {
                _backupBusy = false;
            }
        }
    }
}
