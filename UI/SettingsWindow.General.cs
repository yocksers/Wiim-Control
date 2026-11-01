using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private sealed partial class GeneralPage(SettingsWindow window)
        {
            private readonly ToggleSwitch _chkSuppress = new();
            private readonly ToggleSwitch _chkVolumeKeys = new();
            private Border? _outputCard;
            private readonly ToggleSwitch _chkAutoStart = new();
            private readonly ToggleSwitch _chkStartMinimized = new();
            private readonly ToggleSwitch _chkForwardMedia = new();
            private readonly ToggleSwitch _chkMediaControls = new();
            private readonly ToggleSwitch _chkPortal = new();
            private readonly TextBlock _lblPortalStatus = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            private readonly StackPanel _outputList = new();

            private void ApplyVolumeKeysState(bool on)
            {
                _chkSuppress.IsEnabled = on;
                if (_outputCard == null) return;
                _outputCard.IsEnabled = on;
                _outputCard.Opacity = on ? 1 : 0.45;
            }

            private void UpdatePortalStatus() => Dispatcher.UIThread.Post(() =>
                _lblPortalStatus.Text = window._owner._linuxShortcuts?.Status ?? "Not available");

            public void Load()
            {
                _chkSuppress.IsChecked = window._owner._suppress;
                _chkVolumeKeys.IsChecked = window._owner._volumeKeys;
                ApplyVolumeKeysState(window._owner._volumeKeys);
                _chkAutoStart.IsChecked = IsAutoStartEnabled();
                _chkStartMinimized.IsChecked = window._owner._startMinimized;
                _chkForwardMedia.IsChecked = window._owner._forwardMediaKeys;
                _chkMediaControls.IsChecked = window._owner._mediaControls;
                _chkPortal.IsChecked = window._owner._linuxPortalShortcuts;
                UpdatePortalStatus();
                RefreshOutputList();
            }

            public Control Build()
            {
                _chkSuppress.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetSuppress(_chkSuppress.IsChecked == true); };
                _chkVolumeKeys.IsCheckedChanged += (_, _) =>
                {
                    if (window._loading) return;
                    window._owner.SetVolumeKeys(_chkVolumeKeys.IsChecked == true);
                    ApplyVolumeKeysState(_chkVolumeKeys.IsChecked == true);
                };
                var volumeKeysRow = ToggleRow("Volume keys control the WiiM device", _chkVolumeKeys);
                ToolTip.SetTip(volumeKeysRow, "When this is off, the keyboard's volume and mute keys change this computer's volume as usual. " +
                                              "Hotkeys you set on the Media keys page keep working.");
                _chkAutoStart.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetAutoStartSetting(_chkAutoStart.IsChecked == true); };
                _chkStartMinimized.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetStartMinimized(_chkStartMinimized.IsChecked == true); };
                ToolTip.SetTip(_chkStartMinimized, OperatingSystem.IsMacOS()
                    ? "Start in the menu bar without opening this window. Starting Wiim Control again while it runs opens the window."
                    : "Start in the tray without opening this window. Starting Wiim Control again while it runs opens the window.");
                string startMinimizedLabel = OperatingSystem.IsMacOS() ? "Start minimized (in the menu bar)" : "Start minimized (in the tray)";
                _chkMediaControls.IsCheckedChanged += (_, _) => { if (window._loading) return; window._owner.SetMediaControls(_chkMediaControls.IsChecked == true); };
                ToolTip.SetTip(_chkMediaControls, OperatingSystem.IsWindows()
                    ? "Shows the track in the media pop-up next to the Windows volume, with play/pause, next and previous for the device."
                    : OperatingSystem.IsMacOS()
                        ? "Shows the track in Now Playing in Control Center and the menu bar, with play/pause, next, previous and the position bar for the device."
                        : "Shows the track in the desktop's media controls (MPRIS), with play/pause, next, previous and the position bar for the device.");
                var mediaControlsRow = ToggleRow($"Show the device in the {MediaControlsName}", _chkMediaControls);
                _chkForwardMedia.IsCheckedChanged += async (_, _) =>
                {
                    if (window._loading) return;
                    window._owner.SetForwardMediaKeys(_chkForwardMedia.IsChecked == true);
                    if (!OperatingSystem.IsWindows() && _chkPortal.IsChecked == true)
                        await window._owner.SetLinuxPortalShortcutsAsync(true);
                };

                var cards = new StackPanel();
                if (OperatingSystem.IsWindows())
                {
                    cards.Children.Add(Card("Behavior",
                        volumeKeysRow,
                        ToggleRow("Suppress Windows volume OSD", _chkSuppress),
                        ToggleRow(AutoStart.Label, _chkAutoStart),
                        ToggleRow(startMinimizedLabel, _chkStartMinimized),
                        ToggleRow("Forward media keys (play/pause/next/prev) to Wiim", _chkForwardMedia),
                        mediaControlsRow));

                    var btnClear = Pill("Allow all outputs");
                    btnClear.Margin = new Thickness(0, 8, 0, 0);
                    btnClear.Click += (_, _) => { window._owner.ClearOutputDevices(); window.LoadFromOwner(); };
                    _outputCard = Card("Output devices",
                        Muted("Allow keyboard volume keys to control connected Wiim products directly on the Wiim."),
                        _outputList, btnClear);
                    cards.Children.Add(_outputCard);
                }
                else if (OperatingSystem.IsLinux())
                {
                    cards.Children.Add(Card("Behavior", ToggleRow(AutoStart.Label, _chkAutoStart), ToggleRow(startMinimizedLabel, _chkStartMinimized),
                        mediaControlsRow));
                    cards.Children.Add(BuildLinuxShortcutsCard());
                }
                else
                {
                    cards.Children.Add(Card("Behavior", ToggleRow(AutoStart.Label, _chkAutoStart), ToggleRow(startMinimizedLabel, _chkStartMinimized),
                        mediaControlsRow));
                    cards.Children.Add(Card("Volume and media keys",
                        Muted("macOS doesn't let apps take over the volume keys. Set your own shortcuts on the Media keys page, " +
                              "or run these commands from the Shortcuts app or Automator:"),
                        CommandList()));
                }

                var scale = new ComboBox { Width = FieldWidth };
                foreach (int percent in new[] { 80, 90, 100, 110, 125, 150, 175, 200 }) scale.Items.Add(ComboItem(percent, $"{percent}%"));
                var current = scale.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == window._owner._uiScalePercent);
                if (current == null)
                {
                    current = ComboItem(window._owner._uiScalePercent, $"{window._owner._uiScalePercent}%");
                    scale.Items.Add(current);
                }
                scale.SelectedItem = current;
                scale.SelectionChanged += (_, _) =>
                {
                    if ((scale.SelectedItem as ComboBoxItem)?.Tag is not int percent || percent == window._owner._uiScalePercent) return;
                    window._owner.SetUiScale(percent);
                    window.ApplyScale(percent);
                };
                cards.Children.Add(Card("Appearance", LabeledRow("Scale", scale), Muted("Makes this window bigger or smaller.")));
                var miniAuto = new ToggleSwitch { IsChecked = window._owner.MiniPlayerAuto };
                miniAuto.IsCheckedChanged += (_, _) => window._owner.MiniPlayerAuto = miniAuto.IsChecked == true;
                var miniOnTop = new ToggleSwitch { IsChecked = window._owner.MiniPlayerOnTop };
                miniOnTop.IsCheckedChanged += (_, _) => window._owner.MiniPlayerOnTop = miniOnTop.IsChecked == true;
                var compactOnTop = new ToggleSwitch { IsChecked = window._owner.MiniPlayerCompactOnTop };
                compactOnTop.IsCheckedChanged += (_, _) => window._owner.MiniPlayerCompactOnTop = compactOnTop.IsChecked == true;
                var miniReset = Pill("Reset positions");
                ToolTip.SetTip(miniReset, "Move the mini players back to the bottom-right corner of the screen, side by side at their normal size");
                miniReset.Click += (_, _) => window._owner.ResetMiniPlayerPlacement();
                cards.Children.Add(Card("Mini player",
                    ToggleRow("Open the mini player when the active device switches to Wi-Fi", miniAuto),
                    ToggleRow("Keep the mini player on top of other windows", miniOnTop),
                    ToggleRow("Keep the compact mini player on top of other windows", compactOnTop),
                    LabeledRow("Window positions and sizes", miniReset),
                    Muted("Open a mini player for any device with the mini player button on its card on the Devices page. You can have one open " +
                          "for each device. The button at the top of a mini player switches it between the full and the compact layout.")));
                var trackToasts = new ToggleSwitch { IsChecked = window._owner.TrackToasts };
                trackToasts.IsCheckedChanged += (_, _) => window._owner.TrackToasts = trackToasts.IsChecked == true;
                cards.Children.Add(Card("Song notifications",
                    ToggleRow("Show a notification when the song changes", trackToasts),
                    Muted("Shows the cover, title and artist of what the active device plays for a few seconds, in the same corner and size as " +
                          "the volume overlay (set on the Media keys page). Click it to open the mini player.")));
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
                var instantUpdates = new ToggleSwitch { IsChecked = window._owner.InstantUpdates };
                instantUpdates.IsCheckedChanged += (_, _) => window._owner.InstantUpdates = instantUpdates.IsChecked == true;
                var instantRow = ToggleRow("Instant updates from the devices", instantUpdates);
                ToolTip.SetTip(instantRow, "The devices tell Wiim Control right away when the track, playback or volume changes, instead of " +
                                           "Wiim Control asking every few seconds. Wiim Control listens on a network port for this, so your " +
                                           "firewall or macOS may ask for permission the first time.");
                ToolTip.SetTip(firmwareRow, "Looks for new device firmware every few hours and shows a notice when there is one. " +
                                            "Check now on the Device info page always works.");
                cards.Children.Add(Card("Updates",
                    ToggleRow("Check for new versions of Wiim Control", checkUpdates),
                    LabeledRow($"You have version {AppVersion}", checkNow),
                    Muted("When a new version is out, Wiim Control asks whether to download and install it."),
                    firmwareRow));
                cards.Children.Add(Card("Network", instantRow));
                cards.Children.Add(BuildBackupCard());

                if (!OperatingSystem.IsWindows()) return window.Page("General", null, [], cards);
                var refresh = Pill("Refresh");
                ToolTip.SetTip(refresh, "Reads the list of output devices again, for example after plugging in headphones or a USB sound card");
                refresh.Click += (_, _) => RefreshOutputList();
                var page = window.Page("General", null, [refresh], cards);
                page.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && page.IsVisible) RefreshOutputList(); };
                return page;
            }

            private void RefreshOutputList()
            {
                if (!OperatingSystem.IsWindows()) return;
                var toggles = new List<Control>();
                foreach (var (id, name) in ActiveOutputDevices())
                {
                    var toggle = new ToggleSwitch { IsChecked = window._owner.IsOutputEnabled(id) };
                    string deviceId = id;
                    toggle.IsCheckedChanged += (_, _) => { if (!window._loading) window._owner.SetOutputDeviceEnabled(deviceId, toggle.IsChecked == true); };
                    toggles.Add(ToggleRow(name, toggle));
                }
                if (toggles.Count == 0) toggles.Add(Muted("No playback devices found"));
                SetRows(_outputList, toggles);
            }

            private Border BuildLinuxShortcutsCard()
            {
                _chkPortal.IsCheckedChanged += async (_, _) =>
                {
                    if (window._loading) return;
                    _chkPortal.IsEnabled = false;
                    bool wanted = _chkPortal.IsChecked == true;
                    bool ok = await window._owner.SetLinuxPortalShortcutsAsync(wanted);
                    if (wanted && !ok)
                    {
                        window._loading = true;
                        _chkPortal.IsChecked = false;
                        window._loading = false;
                    }
                    _chkPortal.IsEnabled = true;
                    UpdatePortalStatus();
                };
                if (window._owner._linuxShortcuts is { } shortcuts)
                {
                    shortcuts.StatusChanged += UpdatePortalStatus;
                    window.Closed += (_, _) => shortcuts.StatusChanged -= UpdatePortalStatus;
                }

                return Card("Keyboard shortcuts",
                    ToggleRow("Use the desktop's global shortcuts for the volume keys", _chkPortal),
                    _lblPortalStatus,
                    ToggleRow("Include the media keys (play/pause, next, previous)", _chkForwardMedia),
                    Muted("You can also set your own shortcuts on the Media keys page. If your desktop doesn't support global shortcuts, " +
                          "bind these commands to the keys in its keyboard settings instead:"),
                    CommandList());
            }

            private static StackPanel CommandList()
            {
                string exe = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
                    ? appImage
                    : Environment.ProcessPath ?? "WiimControl";
                var commands = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var (argument, label) in new[]
                {
                    ("--volume-up", "Volume up"), ("--volume-down", "Volume down"), ("--mute", "Mute"),
                    ("--play-pause", "Play/pause"), ("--next", "Next track"), ("--previous", "Previous track")
                })
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), Margin = new Thickness(0, 3) };
                    row.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
                    var command = new SelectableTextBlock
                    {
                        Text = $"\"{exe}\" {argument}", FontFamily = new FontFamily("monospace"), FontSize = 12.5,
                        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(command, 1);
                    row.Children.Add(command);
                    commands.Children.Add(row);
                }
                return commands;
            }
        }
    }
}
