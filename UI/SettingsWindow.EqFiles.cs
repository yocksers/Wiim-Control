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
        private readonly Button _btnEqExport = Pill("Export");
        private readonly Button _btnEqImport = Pill("Import");
        private readonly Button _btnRoomFitExport = Pill("Export");
        private readonly Button _btnRoomFitImport = Pill("Import");

        private void HookEqFileButtons()
        {
            ToolTip.SetTip(_btnEqExport, "Save this EQ to a file");
            ToolTip.SetTip(_btnEqImport, "Load an EQ from a file, save it on the device and use it on this input");
            ToolTip.SetTip(_btnRoomFitExport, "Save the active RoomFit profile to a file");
            ToolTip.SetTip(_btnRoomFitImport, "Load a RoomFit profile from a file and save it on the device");
            _btnEqExport.Click += async (_, _) => await ExportEqAsync();
            _btnEqImport.Click += async (_, _) => await ImportEqAsync();
            _btnRoomFitExport.Click += async (_, _) => await ExportRoomFitAsync();
            _btnRoomFitImport.Click += async (_, _) => await ImportRoomFitAsync();
        }

        private void UpdateEqFileButtons() =>
            _btnEqExport.IsEnabled = _btnEqImport.IsEnabled = _eqAvailable && !_eqLegacy && _eqSource.Length > 0;

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return clean.Length > 0 ? clean : "EQ";
        }

        private async Task SavePresetFileAsync(BackupFile file, string suggestedName, string what)
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export {what}", SuggestedFileName = SafeFileName(suggestedName) + ".json",
                DefaultExtension = "json", FileTypeChoices = [BackupFileType]
            });
            if (target == null) return;
            try
            {
                await using var stream = await target.OpenWriteAsync();
                if (stream.CanSeek) stream.SetLength(0);
                await JsonSerializer.SerializeAsync(stream, file, BackupJson);
                _lblEqSource.Text = $"Saved {target.Name}";
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, $"Couldn't save the file: {ex.Message}", _scale);
            }
        }

        private async Task<BackupFile?> OpenPresetFileAsync(string what)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Import {what}", FileTypeFilter = [BackupFileType, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return null;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                if (ParseBackupFile(await reader.ReadToEndAsync()) is { } backup) return backup;
                await Dialogs.MessageAsync(this, "This file isn't a Wiim Control EQ or backup file.", _scale);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, $"Couldn't read the file: {ex.Message}", _scale);
            }
            return null;
        }

        private async Task<EqPresetBackup?> PickPresetAsync(BackupFile file, Func<AmpBackup, List<EqPresetBackup>> select, string what, string where)
        {
            var choices = file.Amps.SelectMany(a => select(a).Select(p => (Amp: a.Name, Preset: p))).ToList();
            if (choices.Count == 0) return null;
            bool several = file.Amps.Count(a => select(a).Count > 0) > 1;
            if (choices.Count == 1)
            {
                var only = choices[0].Preset;
                return await Dialogs.ConfirmAsync(this, $"Import the {what} “{only.Name}” and {where}?", _scale) ? only : null;
            }
            var combo = new ComboBox { Width = 320, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (amp, preset) in choices) combo.Items.Add(ComboItem(preset, several ? $"{preset.Name}  ({amp})" : preset.Name));
            combo.SelectedIndex = 0;
            var content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = $"The file has {choices.Count} {what}s. Pick the one to import; it's saved on the device and {where}.",
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
                    combo
                }
            };
            return await Dialogs.ChooseAsync(this, content, "Import", _scale) ? (combo.SelectedItem as ComboBoxItem)?.Tag as EqPresetBackup : null;
        }

        private async Task<string?> ConfirmPresetNameAsync(string name, IEnumerable<string> existing, string what)
        {
            if (!existing.Contains(name)) return name;
            int choice = await Dialogs.ButtonsAsync(this, $"There's already a {what} called “{name}” on this device.", _scale,
                "Replace it", "Use another name", "Cancel");
            if (choice == 0) return name;
            if (choice != 1) return null;
            var other = await Dialogs.PromptAsync(this, $"Name for the imported {what}:", name + " 2", _scale);
            return string.IsNullOrWhiteSpace(other) ? null : other.Trim();
        }

        private async Task ExportEqAsync()
        {
            if (!_btnEqExport.IsEnabled) return;
            await SendPendingEqBandsAsync();
            string? name = SelectedEqPreset;
            if (string.IsNullOrEmpty(name))
            {
                name = await Dialogs.PromptAsync(this, "This EQ isn't saved as a preset. Name for the file:", "My EQ", _scale);
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
            }
            var preset = await _owner.ReadCurrentEqAsync(_eqDeviceIp, _eqSource, _eqPluginUri, name);
            if (preset == null)
            {
                await Dialogs.MessageAsync(this, "Couldn't read the EQ from the device.", _scale);
                return;
            }
            await SavePresetFileAsync(_owner.SinglePresetFile(_eqDeviceIp, preset, IsParametric ? "parametric" : "graphic"), name,
                IsParametric ? "parametric EQ" : "EQ");
        }

        private async Task ImportEqAsync()
        {
            if (!_btnEqImport.IsEnabled) return;
            bool parametric = IsParametric;
            string what = parametric ? "parametric EQ preset" : "EQ preset";
            if (await OpenPresetFileAsync(parametric ? "parametric EQ" : "EQ") is not { } file) return;
            string input = _owner.InputName(_eqDeviceIp, _eqSource);
            Func<AmpBackup, List<EqPresetBackup>> select = parametric ? a => a.ParametricEq : a => a.GraphicEq;
            if (!file.Amps.Any(a => select(a).Count > 0))
            {
                bool other = file.Amps.Any(a => (parametric ? a.GraphicEq : a.ParametricEq).Count > 0);
                await Dialogs.MessageAsync(this, other
                    ? $"This file has {(parametric ? "graphic" : "parametric")} EQ presets only. Switch Type to {(parametric ? "Graphic EQ" : "Parametric EQ")} first, then import."
                    : $"This file has no {what}s.", _scale);
                return;
            }
            if (await PickPresetAsync(file, select, what, $"used on {input}") is not { } preset) return;
            if (await ConfirmPresetNameAsync(preset.Name, _eqCustomPresets.Concat(_eqBuiltInPresets), what) is not { } name) return;
            await SendPendingEqBandsAsync();
            _lblEqSource.Text = "Importing…";
            if (!await _owner.ImportEqPresetAsync(_eqDeviceIp, _eqSource, _eqPluginUri, preset with { Name = name }))
                await Dialogs.MessageAsync(this, "The device didn't accept the EQ.", _scale);
            await RefreshEqAsync();
        }

        private async Task ExportRoomFitAsync()
        {
            var profile = await _owner.ReadActiveRoomFitAsync(_eqDeviceIp);
            if (profile == null)
            {
                await Dialogs.MessageAsync(this, "Couldn't read the active RoomFit profile from the device.", _scale);
                return;
            }
            await SavePresetFileAsync(_owner.SinglePresetFile(_eqDeviceIp, profile, "roomfit"), profile.Name, "RoomFit profile");
        }

        private async Task ImportRoomFitAsync()
        {
            if (await OpenPresetFileAsync("RoomFit profile") is not { } file) return;
            if (!file.Amps.Any(a => a.RoomFit.Count > 0))
            {
                await Dialogs.MessageAsync(this, "This file has no RoomFit profiles.", _scale);
                return;
            }
            if (await PickPresetAsync(file, a => a.RoomFit, "RoomFit profile", "made the active profile") is not { } profile) return;
            var existing = (await _owner.GetRoomFitStateAsync(_eqDeviceIp))?.Profiles ?? [];
            if (await ConfirmPresetNameAsync(profile.Name, existing, "RoomFit profile") is not { } name) return;
            _lblEqSource.Text = "Importing…";
            if (!await _owner.ImportRoomFitAsync(_eqDeviceIp, profile with { Name = name }))
                await Dialogs.MessageAsync(this, "The device didn't accept the RoomFit profile.", _scale);
            await RefreshEqAsync();
        }
    }
}
