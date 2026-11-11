using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
        private sealed partial class AmpPage(SettingsWindow window)
        {
            public const string SectionDeviceInfo = "Device info";
            public const string SectionNetwork = "Network";
            public const string SectionStandby = "Standby mode";
            public const string SectionAlarms = "Alarms";
            public const string SectionHardware = "Hardware settings";
            public const string SectionDisplay = "Screen";

            private string _ampSection = SectionDeviceInfo;
            private int _ampVersion;
            private (string Ip, AmpSettings Settings)? _ampCache;

            private string? SelectedAmpIp => (window._cboAmp.SelectedItem as ComboBoxItem)?.Tag as string;

            public string Section => _ampSection;

            public void FillDevices() => FillDeviceSettingsCombo(followActive: window._followActive);

            public Control Build()
            {
                window._cboAmp.SelectionChanged += async (_, _) => { if (!window._loading) await RefreshAmpAsync(); };
                var btnRefresh = Pill("Refresh");
                btnRefresh.Click += async (_, _) => await RefreshAmpAsync();
                var page = window.Page(SectionDeviceInfo, window._lblAmpStatus, [window._cboAmp, btnRefresh], window._ampList);
                window._sectionHandlers[page] = section =>
                {
                    _ampSection = section;
                    FillDeviceSettingsCombo(followActive: false);
                    bool cached = SelectedAmpIp is { } ip && _ampCache is { } cache && cache.Ip == ip;
                    SetRows(window._ampList, cached ? BuildAmpCards(_ampCache!.Value.Ip, _ampCache.Value.Settings) : []);
                    if (page.IsVisible) _ = RefreshAmpAsync(quiet: cached);
                };
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshAmpAsync(quiet: _ampCache?.Ip == SelectedAmpIp);
                };
                return page;
            }

            private void FillDeviceSettingsCombo(bool followActive)
            {
                var devices = window._owner._knownDevices.Where(d => _ampSection != SectionDisplay || window._owner.HasScreen(d.Ip)).ToList();
                string? selected = (followActive ? null : SelectedAmpIp) ?? window._owner._deviceIp;
                if (!devices.Any(d => d.Ip == selected))
                    selected = devices.Any(d => d.Ip == window._owner._deviceIp) ? window._owner._deviceIp : devices.Select(d => d.Ip).FirstOrDefault();
                bool wasLoading = window._loading;
                window._loading = true;
                window._cboAmp.Items.Clear();
                foreach (var d in devices) window._cboAmp.Items.Add(ComboItem(d.Ip, d.Name));
                int index = devices.FindIndex(d => d.Ip == selected);
                window._cboAmp.SelectedIndex = index >= 0 ? index : window._cboAmp.Items.Count > 0 ? 0 : -1;
                window._loading = wasLoading;
            }

            public async Task RefreshAmpAsync(bool quiet = false)
            {
                string? ip = SelectedAmpIp;
                if (ip == null)
                {
                    window._lblAmpStatus.Text = string.Empty;
                    SetRows(window._ampList, [Muted("Add a WiiM device on the Devices page first.")]);
                    return;
                }
                if (!quiet) window._lblAmpStatus.Text = "Loading…";
                int version = ++_ampVersion;
                var status = _ampSection == SectionDisplay ? window._owner.GetDeviceStatusAsync(window.TargetOf(ip)) : Task.FromResult<DeviceStatus?>(null);
                var settings = await window._owner.GetAmpSettingsAsync(ip, _screenInputIp == ip ? _screenInput : null, _ampSection == SectionExperimental,
                    _ampSection == SectionDeviceInfo);
                _screenSpotify = (await status)?.Mode == SpotifyMode;
                if (ip != SelectedAmpIp || version != _ampVersion) return;
                if (settings != null) _ampCache = (ip, settings);
                if (settings?.Screen != null && settings.ScreenUi != null && !window._owner.HasScreen(ip))
                {
                    window._owner.NoteScreen(ip);
                    window.UpdateDisplayNav();
                }
                window._lblAmpStatus.Text = settings == null ? "Not reachable" : string.Empty;
                SetRows(window._ampList, settings == null ? [Muted("Couldn't reach this device.")] : BuildAmpCards(ip, settings));
            }

            private DateTime _lastOwnChange;

            public void OnScreenChanged(string ip)
            {
                if (_ampSection == SectionDisplay && SelectedAmpIp == ip && DateTime.UtcNow - _lastOwnChange > OwnChangeQuiet &&
                    window._ampGate.CurrentCount > 0)
                    _ = RefreshAmpAsync(quiet: true);
            }

            private async Task ChangeAmpAsync(Func<Task<bool>> change)
            {
                _lastOwnChange = DateTime.UtcNow;
                await window._ampGate.WaitAsync();
                bool ok;
                try
                {
                    window._lblAmpStatus.Text = "Saving…";
                    ok = await change();
                }
                finally { window._ampGate.Release(); }
                if (!ok) await Dialogs.MessageAsync(window, "The device didn't accept the change.", window._scale);
                if (window._ampGate.CurrentCount == 0) return;
                await RefreshAmpAsync(quiet: true);
            }

            private Grid AmpToggle(string text, bool value, Func<bool, Task<bool>> apply)
            {
                var toggle = new ToggleSwitch { IsChecked = value };
                toggle.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(() => apply(toggle.IsChecked == true));
                return ToggleRow(text, toggle);
            }

            private Grid AmpChoice(string text, IEnumerable<(int Value, string Label)> items, int current, Func<int, Task<bool>> apply)
            {
                var combo = new ComboBox { Width = 260 };
                foreach (var (value, label) in items) combo.Items.Add(ComboItem(value, label));
                if (!combo.Items.OfType<ComboBoxItem>().Any(i => i.Tag is int v && v == current)) combo.Items.Add(ComboItem(current, $"Mode {current}"));
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => i.Tag is int v && v == current);
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ComboBoxItem)?.Tag is int selected && selected != current)
                        await ChangeAmpAsync(() => apply(selected));
                };
                return LabeledRow(text, combo);
            }

            private const int MainPageFirst = 1;
            private const int SecondPageFirst = 0;

            private static IEnumerable<(int Value, string Label)> StyleChoices(ScreenView view) =>
                view.Choices.Select((value, index) => (value, $"Style {index + 1}"));

            private const int SecondPageChoice = 1000;
            private const int VuMeterPage = 3;

            private static string? SecondPageStyleKey(int page) => page switch
            {
                0 => "sub_cover_view",
                2 => "eq_view",
                3 => "uvmeter_view",
                _ => null
            };

            private static string SecondPageName(int page) => page switch
            {
                0 => "Cover art",
                2 => "EQ visualization",
                3 => "VU meter",
                4 => "Cover art",
                5 => "Cover art and title",
                6 => "Spinning record",
                _ => $"Page {page}"
            };

            private Control BuildWallpaperPicker(string ip, WallpaperInfo info)
            {
                var tiles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                tiles.Children.Add(Muted("Loading the backgrounds from the device…"));
                _ = LoadWallpaperTilesAsync(ip, info, tiles);
                var upload = Pill(info.MaxNumber > 1 ? "Add your own pictures" : "Use your own picture");
                ToolTip.SetTip(upload, $"Upload pictures from this computer. They're cropped to fit the screen ({info.Width} × {info.Height}).");
                upload.Click += async (_, _) => await AddOwnWallpapersAsync(ip, info);
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                header.Children.Add(new TextBlock { Text = "Background", VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(upload, 1);
                header.Children.Add(upload);
                return new StackPanel
                {
                    Margin = new Thickness(0, 12, 0, 0),
                    Children =
                    {
                        header,
                        Muted(info.MaxNumber > 1
                            ? $"Click a background to use it. Ctrl+click to choose more than one (up to {info.MaxNumber}); they take turns on the screen."
                            : "Click a background to use it."),
                        tiles
                    }
                };
            }

            private async Task AddOwnWallpapersAsync(string ip, WallpaperInfo info)
            {
                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose pictures for the screen", AllowMultiple = info.MaxNumber > 1,
                    FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp", "*.gif"] }, FilePickerFileTypes.All]
                });
                if (files.Count == 0) return;
                if (files.Count > info.MaxNumber)
                {
                    await Dialogs.MessageAsync(window, $"The device can show up to {info.MaxNumber} backgrounds. Pick fewer pictures.", window._scale);
                    return;
                }
                var kept = info.MaxNumber > 1 ? info.Current.Skip(Math.Max(0, info.Current.Count + files.Count - info.MaxNumber)).ToList() : [];
                if (info.Current.Except(kept).Any(n => !IsBuiltInWallpaper(n)) && !await Dialogs.ConfirmAsync(window,
                        "Adding these pictures replaces a picture you uploaded before. The device deletes uploaded pictures when they're no " +
                        "longer used, so it will be gone. Continue?", window._scale)) return;
                var uploaded = new List<string>();
                var failed = new List<string>();
                foreach (var file in files)
                {
                    window._lblAmpStatus.Text = $"Uploading {file.Name}…";
                    byte[]? jpeg = null;
                    try
                    {
                        await using var stream = await file.OpenReadAsync();
                        using var memory = new MemoryStream();
                        await stream.CopyToAsync(memory);
                        var data = memory.ToArray();
                        jpeg = await Task.Run(() => PrepareWallpaper(data, info.Width, info.Height));
                    }
                    catch (Exception) { }
                    string? name = jpeg != null ? await window._owner.UploadWallpaperAsync(ip, jpeg) : null;
                    if (name != null) uploaded.Add(name);
                    else failed.Add(file.Name);
                }
                window._lblAmpStatus.Text = string.Empty;
                if (uploaded.Count > 0)
                {
                    await Task.Delay(1000);
                    var current = kept.Concat(uploaded).ToList();
                    if (current.Count > info.MaxNumber) current = current.Skip(current.Count - info.MaxNumber).ToList();
                    await ChangeAmpAsync(() => window._owner.SetWallpapersAsync(ip, info, current));
                }
                if (failed.Count > 0)
                    await Dialogs.MessageAsync(window, (failed.Count == 1 ? $"{failed[0]} couldn't" : $"{failed.Count} pictures couldn't") +
                        " be uploaded. Check that it's a picture file (JPEG, PNG, BMP, WebP or GIF) and that the device is reachable.", window._scale);
            }

            private static bool IsBuiltInWallpaper(string name) => IsBuiltInWallpaperName(name);

            private async Task LoadWallpaperTilesAsync(string ip, WallpaperInfo info, WrapPanel tiles)
            {
                var thumbnails = await window._owner.GetWallpaperThumbnailsAsync(ip, info);
                tiles.Children.Clear();
                if (thumbnails.Count == 0)
                {
                    tiles.Children.Add(Muted("Couldn't load the backgrounds from the device."));
                    return;
                }
                foreach (var (name, thumbnail) in thumbnails)
                {
                    bool selected = info.Current.Contains(name);
                    var tile = new Border
                    {
                        Width = 166, Height = 113, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(3), BorderBrush = selected ? Palette.Accent : Brushes.Transparent,
                        Background = Palette.Input, ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand),
                        Child = thumbnail != null
                            ? new Border { Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill }, CornerRadius = new CornerRadius(5) }
                            : new TextBlock
                            {
                                Text = "Picture missing on the device", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0), Classes = { "muted", "small" }, TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                            }
                    };
                    bool uploaded = !IsBuiltInWallpaper(name);
                    ToolTip.SetTip(tile, (selected ? "In use" : "Use this background") + (uploaded ? " (your own picture)" : string.Empty));
                    tile.Tapped += async (_, e) =>
                    {
                        var current = info.Current.ToList();
                        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && info.MaxNumber > 1)
                        {
                            if (!current.Remove(name)) current.Add(name);
                            if (current.Count == 0 || current.Count > info.MaxNumber) return;
                        }
                        else current = [name];
                        if (current.SequenceEqual(info.Current)) return;
                        var removedUploads = info.Current.Where(n => !IsBuiltInWallpaper(n) && !current.Contains(n)).ToList();
                        if (removedUploads.Count > 0 && !await Dialogs.ConfirmAsync(window,
                                "This removes a picture you uploaded yourself. The device deletes uploaded pictures when they're no longer " +
                                "used, so it will be gone and has to be uploaded again in the WiiM Home app. Continue?", window._scale)) return;
                        await ChangeAmpAsync(() => window._owner.SetWallpapersAsync(ip, info, current));
                    };
                    tiles.Children.Add(tile);
                }
            }

            private List<Control> BuildAmpCards(string ip, AmpSettings s)
            {
                var cards = new List<Control>();
                string empty;
                switch (_ampSection)
                {
                    case SectionNetwork:
                        if (s.Network is { } network) cards.Add(BuildNetworkCard(ip, network));
                        empty = "This device didn't report its network settings.";
                        break;
                    case SectionStandby:
                        if (s.Network?.StandbySeconds is int standby && standby > 0) cards.Add(BuildStandbyCard(ip, standby));
                        empty = "This device doesn't have a standby timer.";
                        break;
                    case SectionAlarms:
                        if (s.Alarms is { Count: > 0 } alarms) cards.Add(BuildAlarmsCard(ip, alarms));
                        empty = "This device doesn't have alarms.";
                        break;
                    case SectionHardware:
                        cards.Add(BuildHardwareCard(ip, s));
                        empty = string.Empty;
                        break;
                    case SectionExperimental:
                        return BuildExperimentalCards(ip, s.Experimental);
                    case SectionDisplay:
                        if (s.Screen is { } screen && (s.ScreenUi != null || window._owner.HasScreen(ip))) cards.Add(BuildScreenCard(ip, screen, s.ScreenUi, s.Wallpapers));
                        empty = "This device has no screen. The WiiM Ultra, Amp Ultra, Sound and Bar have one.";
                        break;
                    default:
                        if (s.Device is { } device) cards.Add(BuildDeviceCard(ip, device, s.AutoUpdate, s.Formats));
                        empty = "This device didn't report its details.";
                        break;
                }
                if (cards.Count == 0) cards.Add(Muted(empty));
                return cards;
            }

            private Border BuildHardwareCard(string ip, AmpSettings s)
            {
                var rows = new List<Control>();
                if (s.StatusLight is bool statusLight)
                    rows.Add(AmpToggle("Status light", statusLight, on => window._owner.SetStatusLightAsync(ip, on)));
                if (s.ButtonsLocked is bool locked)
                {
                    var buttonLock = AmpToggle("Lock the buttons on the device", locked, on => window._owner.SetButtonsLockedAsync(ip, on));
                    ToolTip.SetTip(buttonLock, "Stops the buttons and controls on the device itself from working, for example so children can't change anything. Wiim Control and the WiiM Home app still work.");
                    rows.Add(buttonLock);
                }
                if (s.PromptSounds is bool promptSounds)
                {
                    var prompts = AmpToggle("Prompt sounds", promptSounds, on => window._owner.SetPromptSoundsAsync(ip, on));
                    ToolTip.SetTip(prompts, "The device's sounds and voice prompts, for example when Bluetooth connects. Important prompts always play.");
                    rows.Add(prompts);
                }
                if (s.VolumeLightMode is int volumeLightMode)
                    rows.Add(AmpChoice("Volume lights", VolumeLightModes.Select(m => (m.Mode, m.Label)), volumeLightMode,
                        mode => window._owner.SetVolumeLightModeAsync(ip, mode)));
                var restart = Pill("Restart");
                ToolTip.SetTip(restart, "Restarts the device. It stops playing and is back after about a minute.");
                restart.Click += async (_, _) => await RestartDeviceAsync(ip);
                rows.Add(LabeledRow("Restart device", restart));
                return Card("Hardware", rows.ToArray());
            }
        }
    }
}
