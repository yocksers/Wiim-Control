using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly FilePickerFileType ReportFileType = new("Wiim Control device report") { Patterns = ["*.json"] };
        private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

        private readonly TextBlock _lblReportStatus = new()
        {
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false
        };

        private async Task SaveDeviceReportAsync(Button button)
        {
            if (_owner._knownDevices.Count == 0)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return;
            }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save device report",
                SuggestedFileName = string.Create(CultureInfo.InvariantCulture, $"WiimControl-device-report-{DateTime.Now:yyyy-MM-dd}.json"),
                DefaultExtension = "json",
                FileTypeChoices = [ReportFileType]
            });
            if (file == null) return;

            button.IsEnabled = false;
            _lblReportStatus.IsVisible = true;
            try
            {
                var report = await _owner.BuildDeviceReportAsync(new Progress<string>(text => _lblReportStatus.Text = text));
                await using (var stream = await file.OpenWriteAsync())
                {
                    if (stream.CanSeek) stream.SetLength(0);
                    await JsonSerializer.SerializeAsync(stream, report, ReportJson);
                }
                _lblReportStatus.Text = $"Saved {file.Name}. Send it to the developer if you were asked for it.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _lblReportStatus.Text = string.Empty;
                _lblReportStatus.IsVisible = false;
                await Dialogs.MessageAsync(this, $"Couldn't save the report: {ex.Message}", _scale);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }
}
