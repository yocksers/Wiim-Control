using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed class UpdatesPage(SettingsWindow window)
        {
            public Control Build()
            {
                var checkUpdates = new ToggleSwitch { IsChecked = window._owner._checkUpdates };
                checkUpdates.IsCheckedChanged += (_, _) => window._owner.SetCheckUpdates(checkUpdates.IsChecked == true);
                var checkNow = Pill("Check now");
                checkNow.Click += async (_, _) =>
                {
                    checkNow.IsEnabled = false;
                    try { await window._owner.CheckForUpdateAsync(manual: true); }
                    finally { checkNow.IsEnabled = true; }
                };
                var firmwareChecks = new ToggleSwitch { IsChecked = window._owner.FirmwareChecks };
                firmwareChecks.IsCheckedChanged += (_, _) => window._owner.FirmwareChecks = firmwareChecks.IsChecked == true;
                var firmwareRow = ToggleRow("Check WiiM devices for firmware updates", firmwareChecks);
                ToolTip.SetTip(firmwareRow, "Looks for new device firmware every few hours and shows a notice when there is one. " +
                                            "Check now on the Device info page always works.");
                var card = Card("Updates",
                    ToggleRow("Check for new versions of Wiim Control", checkUpdates),
                    LabeledRow($"You have version {AppVersion}", checkNow),
                    Muted("When a new version is out, Wiim Control asks whether to download and install it."),
                    firmwareRow);
                return window.Page("Updates", null, [], new StackPanel { Children = { card } });
            }
        }
    }
}
