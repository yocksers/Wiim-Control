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
        private const int EqBandCount = 10;
        private static readonly string[] EqBandLabels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
        private static readonly string[] EqBandParams =
            ["band31hz", "band63hz", "band125hz", "band250hz", "band500hz", "band1khz", "band2khz", "band4khz", "band8khz", "band16khz"];

        private readonly WiimController _owner;
        private double _scale;
        private readonly ScaledContent _scaler;
        private Size _normalSize;

        private readonly TextBlock _lblDeviceName = new() { Classes = { "device" }, Margin = new Thickness(4, 18, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _lblDeviceIp = new() { Classes = { "muted" }, Margin = new Thickness(4, 2, 4, 0) };
        private readonly List<(Button Item, Border Bar, Control Page, string? Section)> _pages = [];
        private readonly Dictionary<Control, TextBlock> _pageTitles = new();
        private readonly Dictionary<Control, Action<string>> _sectionHandlers = new();
        private readonly Panel _content = new();
        private Control? _devicesPage;

        private readonly TextBlock _lblDeviceCount = Subtitle();
        private readonly StackPanel _deviceList = new();
        private readonly ToggleSwitch _chkSuppress = new();
        private readonly ToggleSwitch _chkVolumeKeys = new();
        private Border? _outputCard;

        private void ApplyVolumeKeysState(bool on)
        {
            _chkSuppress.IsEnabled = on;
            if (_outputCard == null) return;
            _outputCard.IsEnabled = on;
            _outputCard.Opacity = on ? 1 : 0.45;
        }
        private readonly ToggleSwitch _chkAutoStart = new();
        private readonly ToggleSwitch _chkStartMinimized = new();
        private readonly ToggleSwitch _chkForwardMedia = new();
        private readonly ToggleSwitch _chkMediaControls = new();
        private readonly ToggleSwitch _chkLogStep = new();
        private readonly ToggleSwitch _chkGroupVolume = new();
        private readonly ToggleSwitch _chkPortal = new();
        private readonly TextBlock _lblPortalStatus = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        private readonly Stepper _numStep = new() { Minimum = 1, Maximum = 50, HorizontalAlignment = HorizontalAlignment.Right };
        private const double FieldWidth = 260;
        private const double DevicePickerWidth = 240;
        private const double WideFieldWidth = 360;
        private const double FieldGap = 8;
        private readonly ComboBox _cboCorner = new() { Width = FieldWidth };
        private readonly ComboBox _cboDuration = new() { Width = FieldWidth };
        private readonly ComboBox _cboOsdScale = new() { Width = FieldWidth };
        private readonly StackPanel _outputList = new();
        private bool _loading;

        private readonly ToggleSwitch _chkEqEnabled = new();
        private readonly ComboBox _cboEqPreset = new() { Width = 240, PlaceholderText = "Custom (not saved)" };
        private readonly TextBlock _lblEqSource = Subtitle();
        private readonly ComboBox _cboEqInput = new() { Width = 240 };
        private readonly ComboBox _cboEqAmp = new() { Width = DevicePickerWidth };
        private string? _comboActive;
        private bool _followActive;
        private readonly List<(Control Page, Func<Task> Refresh)> _devicePages = [];
        private readonly FitRow _eqPresetChips = new();
        private readonly Dictionary<string, TextBlock> _inputLabels = new();
        private Control? _eqInputRow;
        private string? _eqInputChoice;
        private int _eqRefreshVersion;
        private string _eqDeviceIp = string.Empty;
        private bool _eqLegacy;
        private readonly EqSlider[] _eqSliders = new EqSlider[EqBandCount];
        private readonly TextBlock[] _eqValueLabels = new TextBlock[EqBandCount];
        private readonly List<Control> _eqEditControls = [];
        private readonly DispatcherTimer _eqSendTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly HashSet<int> _eqDirtyBands = [];
        private List<EqBand> _eqBands = [];
        private bool _eqAvailable;
        private bool _eqLoading;
        private readonly SemaphoreSlim _eqFlushGate = new(1, 1);
        private bool _closeFlushing;
        private bool _closeFlushed;
        private readonly HashSet<string> _eqCustomPresets = new();
        private readonly HashSet<string> _eqBuiltInPresets = new(StringComparer.OrdinalIgnoreCase);
        private string _eqSource = string.Empty;
        private string _eqPluginUri = DefaultEqPlugin;
        private readonly Button _btnEqSaveCurrent = Pill("Save");
        private readonly Button _btnEqSave = Pill("Save as");
        private string? _eqBasePreset;
        private string _eqBaseKey = string.Empty;
        private readonly Button _btnEqRename = Pill("Rename");
        private readonly Button _btnEqDelete = Pill("Delete");
        private readonly ToggleSwitch _chkRoomFit = new();
        private readonly ComboBox _cboRoomFit = new() { Width = 240, PlaceholderText = "No profile selected" };
        private Control? _roomFitRow;

        private readonly TextBlock _lblGroupStatus = Subtitle();
        private readonly StackPanel _groupList = new();
        private MultiroomSnapshot? _multiroom;
        private bool _groupBusy;

        private readonly Dictionary<string, DeviceVolume> _volumes = new();
        private readonly Dictionary<string, VolumeControls> _volumeControls = new();
        private readonly Dictionary<string, int> _pendingVolumes = new();
        private readonly Dictionary<string, DateTime> _volumeTouched = new();
        private readonly HashSet<string> _volumeSending = new();
        private readonly DispatcherTimer _volumeSendTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly DispatcherTimer _volumePollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _volumeUpdating;
        private bool _volumePolling;

        private readonly Dictionary<string, DeviceStatus> _statuses = new();
        private readonly Dictionary<string, NowPlayingControls> _nowPlayingControls = new();
        private readonly ComboBox _cboAmp = new() { Width = DevicePickerWidth };
        private readonly StackPanel _ampList = new();
        private readonly TextBlock _lblAmpStatus = Subtitle();
        private readonly SemaphoreSlim _ampGate = new(1, 1);
        private bool _volumeOnlyPolling;
        private int _pollTick;
        private const int InstantPollTicks = 15;

        private readonly StackPanel _hotkeyList = new();
        private readonly TextBlock _lblHotkeyNote = new()
        {
            Classes = { "accent" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), IsVisible = false
        };
        private HotkeyAction? _recording;

        private sealed record VolumeControls(LevelSlider Slider, TextBlock Value, Button Mute, PathIcon MuteIcon);

        private sealed record NowPlayingControls(ArtworkView Art, TextBlock Title, TextBlock Artist, TextBlock Detail,
            PlayingIndicator Playing, Button PlayPause, PathIcon PlayPauseIcon, Button Previous, Button Next,
            Button Shuffle, PathIcon ShuffleIcon, Button Repeat, PathIcon RepeatIcon,
            Grid PositionRow, LevelSlider Position, TextBlock Elapsed, TextBlock Total);

        public SettingsWindow(WiimController owner)
        {
            _owner = owner;
            _scale = owner.UiScale;
            Title = "Wiim Control";
            Icon = new WindowIcon(AssetLoader.Open(new Uri(
                $"avares://WiimControl/Assets/{(OperatingSystem.IsWindows() ? "logo.ico" : "tray.png")}")));
            WindowChrome.Apply(this);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MinWidth = 960 * _scale;
            MinHeight = 640 * _scale;
            var size = owner._windowSize ?? new Size(1120 * _scale, 820 * _scale);
            Width = Math.Max(size.Width, MinWidth);
            Height = Math.Max(size.Height, MinHeight);
            _normalSize = new Size(Width, Height);

            _scaler = new ScaledContent { Scale = _scale, Child = BuildLayout() };
            Content = _scaler;
            LoadFromOwner();

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.F11 && _npIp != null) SetNowPlayingFullScreen(_npRestoreState == null);
                if (e.Key != Key.Escape) return;
                if (_npRestoreState != null) SetNowPlayingFullScreen(false);
                else if (_npIp != null) HideNowPlayingView();
                else Close();
            };
            _eqSendTimer.Tick += async (_, _) => await FlushEqBandsAsync();
            _volumeSendTimer.Tick += (_, _) => FlushVolumes();
            _positionTimer.Tick += (_, _) => AdvancePositions();
            _volumePollTimer.Tick += async (_, _) =>
            {
                if (_devicesPage is not { IsVisible: true } || WindowState == WindowState.Minimized) return;
                bool instant = _owner._knownDevices.Count > 0 && _owner._knownDevices.All(d => _owner.HasInstantUpdates(d.Ip));
                if (instant)
                {
                    if (++_pollTick % InstantPollTicks == 0) await RefreshDeviceStatusesAsync();
                    return;
                }
                if (++_pollTick % 3 == 0) await RefreshDeviceStatusesAsync();
                else await RefreshVolumesAsync();
            };
            SizeChanged += (_, _) => { if (WindowState == WindowState.Normal) _normalSize = new Size(Width, Height); };
            FitToScreen(center: false);
            if (owner._windowMaximized) WindowState = WindowState.Maximized;
            Opened += async (_, _) =>
            {
                ApplyArtworkBackground();
                _owner.UpdateNowPlayingInterval();
                _volumePollTimer.Start();
                _positionTimer.Start();
                await Task.WhenAll(RefreshEqAsync(), RefreshMultiroomAsync(), RefreshDeviceStatusesAsync(), RefreshAmpAsync(), RefreshPresetsAsync());
            };
            Closing += async (_, e) =>
            {
                if (_closeFlushed) return;
                e.Cancel = true;
                if (_closeFlushing) return;
                _closeFlushing = true;
                _owner.SaveWindowState(_normalSize, (_npRestoreState ?? WindowState) == WindowState.Maximized);
                var flush = FlushBeforeCloseAsync();
                await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(3)));
                _closeFlushed = true;
                Close();
            };
            Closed += (_, _) =>
            {
                _eqSendTimer.Stop();
                _peqSendTimer.Stop();
                _volumeSendTimer.Stop();
                _volumePollTimer.Stop();
                _positionTimer.Stop();
                _npSpinTimer.Stop();
                _sleepClock.Stop();
                ClearLibraryRows();
                ClearRadioResults();
                ClearRecentRows();
                if (_owner._linuxShortcuts != null) _owner._linuxShortcuts.StatusChanged -= UpdatePortalStatus;
                _owner.HotkeysChanged -= RebuildHotkeys;
                if (_recording != null) _ = _owner.ApplyHotkeysAsync();
            };
        }

        private void ApplyScale(int percent)
        {
            double scale = percent / 100.0, ratio = scale / _scale;
            _scale = scale;
            _scaler.Scale = scale;
            MinWidth = 960 * scale;
            MinHeight = 640 * scale;
            if (WindowState != WindowState.Normal) return;
            Width = Math.Max(Width * ratio, MinWidth);
            Height = Math.Max(Height * ratio, MinHeight);
            FitToScreen();
        }

        private void FitToScreen(bool center = true)
        {
            var screen = (center ? Screens.ScreenFromVisual(this) : Screens.ScreenFromPoint(Position)) ?? Screens.Primary;
            if (screen == null) return;
            double scaling = screen.Scaling;
            double maxWidth = screen.WorkingArea.Width / scaling, maxHeight = screen.WorkingArea.Height / scaling;
            MinWidth = Math.Min(MinWidth, maxWidth);
            MinHeight = Math.Min(MinHeight, maxHeight);
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);
            if (!center) return;
            Position = new PixelPoint(
                screen.WorkingArea.X + (int)((maxWidth - Width) * scaling / 2),
                screen.WorkingArea.Y + (int)((maxHeight - Height) * scaling / 2));
        }

        private static TextBlock Subtitle() => new()
        {
            Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 6)
        };

        private static Button Pill(string text, bool accent = false)
        {
            var button = new Button { Content = text };
            button.Classes.Add("pill");
            if (accent) button.Classes.Add("accent");
            return button;
        }

        internal static (Button Button, PathIcon Icon) IconButton(StreamGeometry geometry)
        {
            var icon = new PathIcon { Data = geometry, Width = 20, Height = 20 };
            var button = new Button { Content = icon };
            button.Classes.Add("icon");
            return (button, icon);
        }

        private static TextBlock Muted(string text) => new()
        {
            Text = text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6)
        };

        private static TextBlock Info(string text) => new()
        {
            Text = text, Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };

        private static ComboBoxItem ComboItem(object tag, string text) => new() { Content = text, Tag = tag };

        private static Grid LabeledRow(string text, Control field)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0)
            });
            Grid.SetColumn(field, 1);
            field.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(field);
            return row;
        }

        private static Grid ToggleRow(string text, ToggleSwitch toggle)
        {
            var label = new TextBlock
            {
                Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 16, 0), Cursor = new Cursor(StandardCursorType.Hand)
            };
            label.PointerPressed += (_, _) => { if (toggle.IsEffectivelyEnabled) toggle.IsChecked = toggle.IsChecked != true; };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            row.Children.Add(label);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        private static Border Card(string? heading, params Control[] rows)
        {
            var stack = new StackPanel();
            if (heading != null)
                stack.Children.Add(new TextBlock { Text = heading, Classes = { "heading" }, Margin = new Thickness(0, 0, 0, 10) });
            foreach (var r in rows) stack.Children.Add(r);
            var card = new Border { Child = stack };
            card.Classes.Add("card");
            return card;
        }

        private static void SetRows(Panel list, IEnumerable<Control> rows)
        {
            list.Children.Clear();
            foreach (var r in rows) list.Children.Add(r);
        }

        private Control Page(string title, TextBlock? subtitle, Control[] actions, Control body, bool scroll = true, bool subtitleBelow = false)
        {
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var titleText = new TextBlock { Text = title, Classes = { "title" } };
            titleRow.Children.Add(titleText);
            if (subtitle != null && !subtitleBelow) titleRow.Children.Add(subtitle);

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(titleRow);
            if (subtitle != null && subtitleBelow)
            {
                subtitle.Margin = new Thickness(0, 4, 0, 0);
                subtitle.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                Grid.SetRow(subtitle, 1);
                Grid.SetColumnSpan(subtitle, 2);
                header.Children.Add(subtitle);
            }
            if (actions.Length > 0)
            {
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                foreach (var a in actions) buttons.Children.Add(a);
                Grid.SetColumn(buttons, 1);
                header.Children.Add(buttons);
            }

            var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(30, 20, 30, 16) };
            page.Children.Add(header);
            if (scroll) body.Margin = new Thickness(body.Margin.Left, body.Margin.Top, body.Margin.Right + 12, body.Margin.Bottom);
            Control content = scroll ? new ScrollViewer { Content = body } : body;
            Grid.SetRow(content, 1);
            page.Children.Add(content);
            _pageTitles[page] = titleText;
            return page;
        }

        private Control BuildLayout()
        {
            var devicesPage = BuildDevicesPage();
            _devicesPage = devicesPage;
            var multiroomPage = BuildMultiroomPage();
            var presetsPage = BuildPresetsPage();
            var libraryPage = BuildLibraryPage();
            var radioPage = BuildRadioPage();
            var recentPage = BuildRecentPage();
            var queuePage = BuildQueuePage();
            var eqPage = BuildEqPage();
            var volumePage = BuildVolumePage();
            var outputPage = BuildOutputPage();
            var inputsPage = BuildInputsPage();
            var subwooferPage = BuildSubwooferPage();
            var audioSettingsPage = BuildAudioSettingsPage();
            var groupDelayPage = BuildGroupDelayPage();
            var ampPage = BuildAmpPage();
            var sleepPage = BuildSleepPage();
            var hotkeysPage = BuildHotkeysPage();
            var generalPage = BuildGeneralPage();
            _devicePages.AddRange([
                (presetsPage, RefreshPresetsAsync), (audioSettingsPage, RefreshAmpVolumeAsync), (outputPage, () => RefreshOutputAsync()),
                (inputsPage, () => RefreshInputsAsync()), (subwooferPage, () => RefreshSubwooferAsync()), (groupDelayPage, RefreshGroupDelayAsync)
            ]);

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 0) };
            brand.Children.Add(new TextBlock { Text = "Wiim Control", Classes = { "small" } });
            brand.Children.Add(new TextBlock { Text = $"v{AppVersion}", Classes = { "small", "muted" } });

            var hideMenu = MenuButton(Icons.HideMenu, "Hide the menu", hidden: true);
            hideMenu.Margin = new Thickness(0, -9, 14, -9);
            DockPanel.SetDock(hideMenu, Dock.Right);
            var sidebarItems = new StackPanel();
            sidebarItems.Children.Add(new DockPanel { Children = { hideMenu, brand } });
            sidebarItems.Children.Add(_lblDeviceName);
            sidebarItems.Children.Add(_lblDeviceIp);
            sidebarItems.Children.Add(SectionLabel("Devices"));
            sidebarItems.Children.Add(NavGroup(Nav("Devices", Icons.Speaker, devicesPage), Nav("Multiroom", Icons.Link, multiroomPage)));
            sidebarItems.Children.Add(SectionLabel("Music"));
            sidebarItems.Children.Add(NavGroup(
                Nav("Library", Icons.Library, libraryPage), Nav("Radio", Icons.Radio, radioPage), Nav("Queue", Icons.Queue, queuePage),
                Nav("Recent", Icons.History, recentPage)));
            sidebarItems.Children.Add(SectionLabel("Sound"));
            sidebarItems.Children.Add(NavGroup(
                Nav("Equalizer", Icons.Equalizer, eqPage), Nav("Output", Icons.Output, outputPage), Nav("Inputs", Icons.Input, inputsPage)));
            var deviceInfoNav = Nav(SectionDeviceInfo, Icons.Info, ampPage, SectionDeviceInfo);
            deviceInfoNav.Click += (_, _) => CountDeviceInfoClick(ampPage);
            var general = NavGroup(
                deviceInfoNav, Nav(SectionNetwork, Icons.Wifi, ampPage, SectionNetwork),
                Nav(SectionStandby, Icons.Power, ampPage, SectionStandby));
            var automation = NavGroup(
                Nav(SectionAlarms, Icons.Alarm, ampPage, SectionAlarms), Nav("Sleep timer", Icons.Sleep, sleepPage), Nav("Presets", Icons.Star, presetsPage));
            sidebarItems.Children.Add(SectionLabel("Audio"));
            sidebarItems.Children.Add(NavGroup(
                Nav(SectionHighPass, Icons.HighPass, subwooferPage, SectionHighPass), Nav("Audio settings", Icons.AudioSettings, audioSettingsPage),
                Nav("Group audio delay", Icons.Timer, groupDelayPage)));
            sidebarItems.Children.Add(SectionLabel("Subwoofer"));
            sidebarItems.Children.Add(NavGroup(
                Nav(SectionSubOut, Icons.Speaker, subwooferPage, SectionSubOut)));
            sidebarItems.Children.Add(SectionLabel("Hardware"));
            _displayNav = Nav(SectionDisplay, Icons.Display, ampPage, SectionDisplay);
            sidebarItems.Children.Add(NavGroup(Nav(SectionHardware, Icons.Wrench, ampPage, SectionHardware), _displayNav));
            sidebarItems.Children.Add(SectionLabel("Automation"));
            sidebarItems.Children.Add(automation);
            sidebarItems.Children.Add(SectionLabel("General"));
            sidebarItems.Children.Add(general);
            sidebarItems.Children.Add(SectionLabel("Wiim Control"));
            sidebarItems.Children.Add(NavGroup(
                Nav("Volume keys", Icons.Volume, volumePage), Nav("Hotkeys", Icons.Keyboard, hotkeysPage), Nav("General", Icons.Settings, generalPage)));

            var coffee = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 12, 0, 0),
                Children = { CoffeeButton(), WiimButton() }
            };
            DockPanel.SetDock(coffee, Dock.Bottom);
            var sidebar = new Border
            {
                Background = Palette.Sidebar, Width = 250, Padding = new Thickness(12, 16),
                Child = new DockPanel { Children = { coffee, new ScrollViewer { Content = sidebarItems, Classes = { "menu" } } } }
            };

            _showMenuButton = MenuButton(Icons.Menu, "Show the menu", hidden: false);
            _showMenuButton.HorizontalAlignment = HorizontalAlignment.Left;
            _showMenuButton.VerticalAlignment = VerticalAlignment.Top;
            _showMenuButton.Margin = new Thickness(10, 22, 0, 0);
            var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            root.Children.Add(sidebar);
            Grid.SetColumn(_content, 1);
            root.Children.Add(_content);
            Grid.SetColumn(_showMenuButton, 1);
            root.Children.Add(_showMenuButton);
            ShowPage(devicesPage);
            _sidebar = sidebar;
            ApplySidebarVisibility();
            return new Panel { Children = { _backdropA, _backdropB, _backdropScrim, root, BuildNowPlayingOverlay() } };
        }

        private Button MenuButton(StreamGeometry icon, string tip, bool hidden)
        {
            var (button, path) = IconButton(icon);
            path.Width = path.Height = 18;
            button.Width = button.Height = 32;
            button.Padding = new Thickness(0);
            button.MinHeight = 0;
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) =>
            {
                _owner.SetSidebarHidden(hidden);
                ApplySidebarVisibility();
            };
            return button;
        }

        private void ApplySidebarVisibility()
        {
            bool hidden = _owner._sidebarHidden;
            if (_sidebar != null) _sidebar.IsVisible = !hidden;
            if (_showMenuButton != null) _showMenuButton.IsVisible = hidden;
            _content.Margin = new Thickness(hidden ? 34 : 0, 0, 0, 0);
        }

        private static Border NavGroup(params Button[] items)
        {
            var list = new StackPanel();
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                {
                    var separator = new Border { Classes = { "navseparator" } };
                    separator.Bind(IsVisibleProperty, items[i].GetObservable(IsVisibleProperty));
                    list.Children.Add(separator);
                }
                list.Children.Add(items[i]);
            }
            return new Border { Classes = { "navgroup" }, Child = list };
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text, Classes = { "small", "muted" }, Margin = new Thickness(4, 22, 4, 6)
        };

        private Button Nav(string text, StreamGeometry icon, Control page, string? section = null)
        {
            var bar = new Border
            {
                Width = 4, Height = 22, CornerRadius = new CornerRadius(2), Background = Palette.Accent,
                VerticalAlignment = VerticalAlignment.Center, IsVisible = false
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("10,30,*"), Height = 44 };
            grid.Children.Add(bar);
            var iconView = new PathIcon { Data = icon, Width = 20, Height = 20, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(iconView, 1);
            grid.Children.Add(iconView);
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);

            var item = new Button { Content = grid };
            item.Classes.Add("nav");
            item.Click += (_, _) => ShowPage(page, section);
            if (!_content.Children.Contains(page))
            {
                page.IsVisible = false;
                _content.Children.Add(page);
            }
            _pages.Add((item, bar, page, section));
            return item;
        }

        private void ShowPage(Control page, string? section = null)
        {
            section ??= _pages.FirstOrDefault(p => p.Page == page).Section;
            foreach (var (item, bar, p, s) in _pages)
            {
                bool selected = p == page && s == section;
                item.Classes.Set("selected", selected);
                bar.IsVisible = selected;
            }
            if (section != null && _sectionHandlers.TryGetValue(page, out var handler))
            {
                if (_pageTitles.TryGetValue(page, out var title)) title.Text = section;
                handler(section);
            }
            foreach (var p in _pages.Select(p => p.Page).Distinct()) p.IsVisible = p == page;
        }

        private Control BuildDevicesPage()
        {
            var btnDiscover = Pill("Discover");
            var btnAdd = Pill("Add Device");
            btnDiscover.Click += async (_, _) =>
            {
                btnDiscover.IsEnabled = false;
                btnDiscover.Content = "Searching…";
                int added = await _owner.DiscoverAndMergeDevicesAsync();
                btnDiscover.Content = "Discover";
                btnDiscover.IsEnabled = true;
                await Dialogs.MessageAsync(this, added > 0 ? $"Found {added} new Wiim device(s)" : "No new Wiim devices found", _scale);
                LoadFromOwner();
            };
            btnAdd.Click += async (_, _) =>
            {
                btnAdd.IsEnabled = false;
                try
                {
                    if (!await _owner.AddDeviceAsync(this)) return;
                }
                finally { btnAdd.IsEnabled = true; }
                LoadFromOwner();
                _ = RefreshEqAsync();
                _ = RefreshMultiroomAsync();
            };
            return Page("Devices", _lblDeviceCount, [btnDiscover, btnAdd], _deviceList);
        }

        internal void OnFirmwareChecked() => Dispatcher.UIThread.Post(() =>
        {
            RebuildDeviceCards();
            _ = RefreshDeviceStatusesAsync();
        });

        private async Task RenameDeviceAsync(KnownDevice device)
        {
            string? name = await Dialogs.PromptAsync(this,
                $"New name for {device.Name}. The device keeps it, so the WiiM Home app, AirPlay and other apps show it too:", device.Name, _scale);
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name == device.Name) return;
            if (name.Length > MaxDeviceNameLength)
            {
                await Dialogs.MessageAsync(this, $"Please use at most {MaxDeviceNameLength} characters.", _scale);
                return;
            }
            if (!await _owner.RenameDeviceAsync(device.Ip, name))
            {
                await Dialogs.MessageAsync(this, $"{device.Name} didn't accept the new name. Check that it's switched on and reachable.", _scale);
                return;
            }
            LoadFromOwner();
            _ = RefreshDeviceStatusesAsync();
            _ = RefreshMultiroomAsync();
        }

        private void MoveDeviceCard(KnownDevice device, int offset)
        {
            if (!_owner.MoveDevice(device, offset)) return;
            LoadFromOwner();
            _ = RefreshDeviceStatusesAsync();
        }

        private void RebuildDeviceCards()
        {
            _volumeControls.Clear();
            _nowPlayingControls.Clear();
            _castButtons.Clear();
            _inputLabels.Clear();
            _positions.Clear();
            var cards = new List<Control>();
            var devices = _owner._knownDevices.ToList();
            for (int position = 0; position < devices.Count; position++)
            {
                var d = devices[position];
                bool active = d.Ip == _owner._deviceIp;
                bool locked = _owner.IsDeviceLocked(d.Ip);

                var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                title.Children.Add(new TextBlock { Text = d.Name, Classes = { "device" } });
                var (rename, renameIcon) = IconButton(Icons.Edit);
                renameIcon.Width = renameIcon.Height = 14;
                rename.Width = rename.Height = 24;
                rename.Padding = new Thickness(0);
                rename.MinHeight = 0;
                rename.Margin = new Thickness(-6, 0, -4, 0);
                rename.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(rename, "Rename this device");
                rename.Click += async (_, _) => await RenameDeviceAsync(d);
                rename.IsEnabled = !locked;
                title.Children.Add(rename);
                if (active)
                    title.Children.Add(new TextBlock { Text = "●  Active", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center });
                if (_owner.FirmwareUpdateFor(d.Ip) is { } update)
                {
                    var badge = new TextBlock { Text = "⬆  Update available", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
                    ToolTip.SetTip(badge, $"{update.Summary}. {FirmwareUpdateAdvice}");
                    title.Children.Add(badge);
                }
                var info = new StackPanel();
                info.Children.Add(title);
                info.Children.Add(new TextBlock
                {
                    Text = _owner.ModelOf(d.Ip) is { Length: > 0 } model ? $"{model}  ·  {d.Ip}" : d.Ip, Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 0)
                });
                if (GroupDescription(d.Ip) is { } group)
                    info.Children.Add(new TextBlock { Text = group.Text, Foreground = group.Brush, Margin = new Thickness(0, 4, 0, 0) });

                var actions = new WrapPanel
                {
                    ItemSpacing = 8, LineSpacing = 8, ItemsAlignment = WrapPanelItemsAlignment.End, VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0)
                };
                if (active)
                {
                    var test = Pill("Test connection");
                    test.Click += async (_, _) => await ShowConnectionTestAsync();
                    actions.Children.Add(test);
                }
                else
                {
                    var use = Pill("Use this device", accent: true);
                    use.Click += (_, _) => _owner.SwitchDevice(d);
                    actions.Children.Add(use);
                }
                var input = InputButton(d.Ip);
                input.IsEnabled = !locked;
                actions.Children.Add(input);
                var (remove, _) = IconPill(Icons.Delete, "Remove this device from the list");
                remove.IsEnabled = !locked;
                remove.Click += async (_, _) =>
                {
                    if (!await Dialogs.ConfirmAsync(this,
                            $"Remove {d.Name} from Wiim Control? The device itself isn't changed, and you can add it again with Discover or Add Device.",
                            _scale)) return;
                    _owner.RemoveDevice(d);
                    ReloadSoon(refreshEq: active);
                };
                var (lockButton, lockIcon) = IconButton(locked ? Icons.Lock : Icons.LockOpen);
                lockIcon.Width = lockIcon.Height = 18;
                if (locked) lockIcon.Foreground = Palette.AccentText;
                ToolTip.SetTip(lockButton, locked
                    ? "Locked. Volume, playback and other changes to this device are blocked here. Click to unlock."
                    : "Lock this device so its volume, playback and other settings can't be changed here by accident");
                lockButton.Click += (_, _) =>
                {
                    _owner.SetDeviceLocked(d.Ip, !locked);
                    RebuildDeviceCards();
                };
                var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                if (devices.Count > 1)
                {
                    var (up, _) = IconButton(Icons.ArrowUp);
                    var (down, _) = IconButton(Icons.ArrowDown);
                    ToolTip.SetTip(up, "Move up");
                    ToolTip.SetTip(down, "Move down");
                    up.IsEnabled = position > 0;
                    down.IsEnabled = position < devices.Count - 1;
                    up.Click += (_, _) => MoveDeviceCard(d, -1);
                    down.Click += (_, _) => MoveDeviceCard(d, 1);
                    orderButtons.Children.Add(up);
                    orderButtons.Children.Add(down);
                }
                orderButtons.Children.Add(lockButton);
                actions.Children.Insert(0, orderButtons);

                var (mini, _) = IconPill(Icons.MiniPlayer, "Open the mini player for this device");
                mini.Click += (_, _) => _owner.ShowMiniPlayer(d.Ip);
                var playerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mini } };
                if (PcAudioSupported)
                {
                    var cast = CastButton(d.Ip);
                    cast.IsEnabled = !locked;
                    playerButtons.Children.Add(cast);
                }
                actions.Children.Add(playerButtons);
                actions.Children.Add(remove);

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
                grid.Children.Add(info);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);
                var nowPlayingRow = BuildNowPlayingRow(d.Ip);
                if (locked && _nowPlayingControls.TryGetValue(d.Ip, out var playback))
                {
                    if (playback.PlayPause.Parent is Control transport) transport.IsEnabled = false;
                    playback.PositionRow.IsEnabled = false;
                }
                Grid.SetRow(nowPlayingRow, 1);
                Grid.SetColumnSpan(nowPlayingRow, 2);
                grid.Children.Add(nowPlayingRow);
                var volumeRow = BuildVolumeRow(d.Ip);
                volumeRow.IsEnabled = !locked;
                Grid.SetRow(volumeRow, 2);
                Grid.SetColumnSpan(volumeRow, 2);
                grid.Children.Add(volumeRow);

                var card = new Border { Child = grid };
                card.Classes.Add("card");
                if (active) card.Classes.Add("active");
                cards.Add(card);
            }
            if (cards.Count == 0) cards.Add(Muted("No devices yet. Use Discover or Add Device."));

            SetRows(_deviceList, cards);
            int n = _owner._knownDevices.Count;
            _lblDeviceCount.Text = $"{n} device{(n == 1 ? "" : "s")}";
            foreach (var ip in _volumeControls.Keys) ApplyVolume(ip);
            foreach (var ip in _nowPlayingControls.Keys) ApplyNowPlaying(ip);
            foreach (var ip in _channelButtons.Keys) ApplyChannel(ip);
        }

        private Button InputButton(string ip)
        {
            var label = new TextBlock { Text = "Input", VerticalAlignment = VerticalAlignment.Center };
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { label, new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12 } }
                }
            };
            button.Classes.Add("pill");
            ToolTip.SetTip(button, "Choose the device's input");
            button.Click += async (_, _) => await ShowInputMenuAsync(ip, button);
            _inputLabels[ip] = label;
            return button;
        }

        private async Task ShowInputMenuAsync(string ip, Button button)
        {
            button.IsEnabled = false;
            var inputs = await _owner.GetInputsAsync(ip);
            button.IsEnabled = true;
            if (inputs == null)
            {
                await Dialogs.MessageAsync(this, "Couldn't get the inputs from the device. Check that it's switched on and reachable.", _scale);
                return;
            }
            string? current = _statuses.TryGetValue(ip, out var status) ? InputOfMode(status.Mode) : null;
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var input in inputs)
            {
                var item = new MenuItem
                {
                    Header = _owner.InputName(ip, input), ToggleType = MenuItemToggleType.Radio,
                    IsChecked = IsSameInput(input, current)
                };
                string target = input;
                item.Click += async (_, _) => await SwitchInputAsync(ip, target);
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        }

        private async Task SetPcAudioAsync(string ip, bool on)
        {
            if (!await _owner.SetPcAudioAsync(ip, on) && on)
            {
                await Dialogs.MessageAsync(this, "The device didn't start playing this computer's sound. Check that it's switched on and reachable.", _scale);
                return;
            }
            await Task.Delay(1500);
            await RefreshDeviceStatusesAsync();
        }

        internal void OnPcAudioChanged()
        {
            foreach (var ip in _castButtons.Keys.ToList()) ApplyCastButton(ip);
            foreach (var ip in _inputLabels.Keys.ToList()) ApplyNowPlaying(ip);
        }

        private readonly Dictionary<string, (Button Button, PathIcon Icon)> _castButtons = new();

        private static (Button Button, PathIcon Icon) IconPill(StreamGeometry geometry, string? tip)
        {
            var icon = new PathIcon { Data = geometry, Width = 18, Height = 18 };
            var button = new Button
            {
                Content = new Grid { Children = { new TextBlock { Text = " ", Opacity = 0 }, icon } },
                Padding = new Thickness(14, 8), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("pill");
            if (tip != null) ToolTip.SetTip(button, tip);
            return (button, icon);
        }

        private Button CastButton(string ip)
        {
            var (cast, castIcon) = IconPill(Icons.Cast, null);
            cast.Click += async (_, _) =>
            {
                cast.IsEnabled = false;
                try { await SetPcAudioAsync(ip, !_owner.IsPlayingPcAudio(ip)); }
                finally { cast.IsEnabled = true; }
            };
            _castButtons[ip] = (cast, castIcon);
            ApplyCastButton(ip);
            return cast;
        }

        private void ApplyCastButton(string ip)
        {
            if (!_castButtons.TryGetValue(ip, out var c)) return;
            bool on = _owner.IsPlayingPcAudio(ip);
            c.Icon.Data = on ? Icons.CastConnected : Icons.Cast;
            c.Button.Classes.Set("accent", on);
            ToolTip.SetTip(c.Button, on
                ? "Stop casting audio from this computer"
                : "Chromecast audio from this computer");
        }

        private async Task SwitchInputAsync(string ip, string input)
        {
            _owner.OnInputChosen(ip);
            if (!await _owner.SwitchInputAsync(ip, input))
            {
                await Dialogs.MessageAsync(this, "The device didn't switch the input. Check that it's switched on and reachable.", _scale);
                return;
            }
            if (_inputLabels.TryGetValue(ip, out var label)) label.Text = _owner.InputName(ip, input);
            await Task.Delay(1500);
            await RefreshDeviceStatusesAsync();
            if (ip == EqIp && _eqInputChoice == null) await RefreshEqAsync();
        }

        private VolumeControls CreateVolumeControls(string ip)
        {
            var (mute, muteIcon) = IconButton(Icons.Volume);
            var slider = new LevelSlider { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, WheelChangesValue = false };
            var value = new TextBlock
            {
                Text = "–", Classes = { "muted" }, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = slider.Value.ToString();
                if (_volumeUpdating) return;
                _volumeTouched[ip] = DateTime.UtcNow;
                _pendingVolumes[ip] = slider.Value;
                if (!_volumeSendTimer.IsEnabled) _volumeSendTimer.Start();
            };
            mute.Click += async (_, _) => await ToggleDeviceMuteAsync(ip);
            return new VolumeControls(slider, value, mute, muteIcon);
        }

        private static Grid VolumeRow(VolumeControls c, Control? extra = null)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(c.Mute);
            Grid.SetColumn(c.Slider, 1);
            row.Children.Add(c.Slider);
            Grid.SetColumn(c.Value, 2);
            row.Children.Add(c.Value);
            if (extra != null)
            {
                Grid.SetColumn(extra, 3);
                row.Children.Add(extra);
            }
            return row;
        }

        private Grid BuildVolumeRow(string ip)
        {
            var controls = CreateVolumeControls(ip);
            _volumeControls[ip] = controls;
            var channel = BuildChannelButton(ip);
            channel.Margin = new Thickness(12, 0, 0, 0);
            return VolumeRow(controls, channel);
        }

        private bool IsDraggingVolume(string ip) =>
            (_volumeControls.TryGetValue(ip, out var c) && c.Slider.IsDragging) || (_npIp == ip && _npVolume?.Slider.IsDragging == true);

        private void ApplyVolume(string ip)
        {
            if (_volumeControls.TryGetValue(ip, out var card)) ApplyVolumeTo(ip, card);
            if (_npIp == ip && _npVolume != null) ApplyVolumeTo(ip, _npVolume);
        }

        private void ApplyVolumeTo(string ip, VolumeControls c)
        {
            if (_volumes.TryGetValue(ip, out var v))
            {
                bool busy = IsDraggingVolume(ip) || _pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip);
                if (!busy)
                {
                    _volumeUpdating = true;
                    c.Slider.Value = v.Volume;
                    _volumeUpdating = false;
                }
                c.Value.Text = c.Slider.Value.ToString();
                c.Value.Classes.Set("muted", false);
                c.Slider.Muted = v.Muted;
                c.Slider.IsEnabled = c.Mute.IsEnabled = true;
                c.MuteIcon.Data = v.Muted ? Icons.Mute : Icons.Volume;
            }
            else
            {
                c.Slider.IsEnabled = c.Mute.IsEnabled = false;
                c.Value.Text = "–";
                c.Value.Classes.Set("muted", true);
            }
        }

        private void FlushVolumes()
        {
            foreach (var (ip, volume) in _pendingVolumes.ToList())
            {
                if (_volumeSending.Contains(ip)) continue;
                _pendingVolumes.Remove(ip);
                _ = SendVolumeAsync(ip, volume);
            }
            if (_pendingVolumes.Count == 0) _volumeSendTimer.Stop();
        }

        private async Task FlushBeforeCloseAsync()
        {
            await SendPendingEqBandsAsync();
            FlushVolumes();
            while (_volumeSending.Count > 0 || _pendingVolumes.Count > 0)
            {
                await Task.Delay(30);
                FlushVolumes();
            }
        }

        private async Task SendVolumeAsync(string ip, int volume)
        {
            _volumeSending.Add(ip);
            try
            {
                if (await _owner.SetDeviceVolumeAsync(ip, volume) && _volumes.TryGetValue(ip, out var current))
                    _volumes[ip] = current with { Volume = volume };
            }
            finally
            {
                _volumeSending.Remove(ip);
                _volumeTouched[ip] = DateTime.UtcNow;
                if (_pendingVolumes.ContainsKey(ip)) FlushVolumes();
            }
        }

        private async Task ToggleDeviceMuteAsync(string ip)
        {
            if (!_volumes.TryGetValue(ip, out var current)) return;
            bool muted = !current.Muted;
            _volumeTouched[ip] = DateTime.UtcNow;
            if (!await _owner.SetDeviceMuteAsync(ip, muted)) return;
            _volumes[ip] = (_volumes.TryGetValue(ip, out var latest) ? latest : current) with { Muted = muted };
            _volumeTouched[ip] = DateTime.UtcNow;
            ApplyVolume(ip);
        }

        internal void OnVolumeEvent(string ip, int? volume, bool? muted)
        {
            bool known = _volumes.TryGetValue(ip, out var current);
            if ((volume ?? (known ? current.Volume : null)) is not int v) return;
            ShowVolume(ip, v, muted ?? (known && current.Muted));
        }

        internal void OnDeviceEvent(string ip)
        {
            if (IsVisible && WindowState != WindowState.Minimized) _ = RefreshDeviceStatusesAsync();
        }

        internal void ShowVolume(string ip, int volume, bool muted)
        {
            if (_pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip)) return;
            if (IsDraggingVolume(ip)) return;
            _volumes[ip] = new DeviceVolume(volume, muted);
            ApplyVolume(ip);
        }

        private async Task RefreshVolumesAsync()
        {
            if (_volumeOnlyPolling || _volumePolling) return;
            _volumeOnlyPolling = true;
            try
            {
                var started = DateTime.UtcNow;
                var ips = _owner._knownDevices.Select(d => d.Ip).Distinct().ToList();
                var volumes = await Task.WhenAll(ips.Select(ip => _owner.GetDeviceVolumeAsync(ip)));
                for (int i = 0; i < ips.Count; i++)
                {
                    string ip = ips[i];
                    if (volumes[i] is not { } volume) continue;
                    if (_volumeTouched.TryGetValue(ip, out var touched) && touched > started.AddSeconds(-1)) continue;
                    ShowVolume(ip, volume.Volume, volume.Muted);
                }
            }
            finally
            {
                _volumeOnlyPolling = false;
            }
        }

        private async Task RefreshDeviceStatusesAsync()
        {
            if (_volumePolling) return;
            _volumePolling = true;
            try
            {
                var started = DateTime.UtcNow;
                var statuses = await _owner.GetAllDeviceStatusesAsync();
                foreach (var (ip, status) in statuses)
                {
                    if (status != null) _statuses[ip] = status;
                    else _statuses.Remove(ip);
                    ApplyNowPlaying(ip);
                    _owner.NoteDeviceStatus(ip, status);
                    if (_volumeTouched.TryGetValue(ip, out var touched) && touched > started.AddSeconds(-1)) continue;
                    if (status != null) _volumes[ip] = status.Volume;
                    else _volumes.Remove(ip);
                    ApplyVolume(ip);
                }
                ApplyFollowers();
            }
            finally
            {
                _volumePolling = false;
            }
        }

        private const double CardArtSize = 92;
        private const double CardPlaceholderSize = 38;

        private Grid BuildNowPlayingRow(string ip)
        {
            var art = new ArtworkView { VerticalAlignment = VerticalAlignment.Center };
            art.Tapped += (_, _) => { if (art.Bitmap != null) ShowNowPlayingView(ip); };
            art.BitmapChanged += () =>
            {
                art.Cursor = art.Bitmap != null ? new Cursor(StandardCursorType.Hand) : null;
                ToolTip.SetTip(art, art.Bitmap != null ? "Show the artwork full size" : null);
            };
            var title = new TextBlock { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var detail = new TextBlock { Classes = { "dim", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var playing = new PlayingIndicator { Foreground = Palette.AccentText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var detailRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Children = { playing, detail } };
            DockPanel.SetDock(playing, Dock.Left);
            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, artist, detailRow, positionRow } };
            art.Width = art.Height = CardArtSize;
            art.PlaceholderSize = CardPlaceholderSize;

            var (shuffle, shuffleIcon) = IconButton(Icons.Shuffle);
            var (repeat, repeatIcon) = IconButton(Icons.Repeat);
            shuffle.IsVisible = repeat.IsVisible = false;
            shuffle.Click += async (_, _) => await ToggleShuffleAsync(ip);
            repeat.Click += async (_, _) => await CycleRepeatAsync(ip);
            var (previous, _) = IconButton(Icons.Previous);
            var (playPause, playPauseIcon) = IconButton(Icons.Play);
            var (next, _) = IconButton(Icons.Next);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { shuffle, previous, playPause, next, repeat }
            };
            _nowPlayingControls[ip] = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(art);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            return row;
        }

        private void ApplyFollowers()
        {
            foreach (var ip in _nowPlayingControls.Keys.Where(ip => TargetOf(ip) != ip).ToList()) ApplyNowPlaying(ip);
        }

        private void ApplyNowPlaying(string ip)
        {
            if (_inputLabels.TryGetValue(ip, out var inputLabel))
                inputLabel.Text = !_statuses.TryGetValue(ip, out var known) ? "Input"
                    : _owner.IsPlayingPcAudio(ip) && IsNetworkMode(known.Mode) ? "This computer"
                    : InputOfMode(known.Mode) is { } input ? _owner.InputName(ip, input)
                    : SourceName(known.Mode) is { Length: > 0 } name ? name
                    : "Input";
            if (_nowPlayingControls.TryGetValue(ip, out var card)) ApplyNowPlayingTo(ip, card);
            if (_npIp == ip && _npControls != null) ApplyNowPlayingTo(ip, _npControls);
        }

        private void ApplyNowPlayingTo(string ip, NowPlayingControls c)
        {
            if (!_statuses.TryGetValue(ip, out var status))
            {
                c.Title.Text = _volumes.Count == 0 && _statuses.Count == 0 ? "Loading…" : "Not reachable";
                c.Artist.Text = c.Detail.Text = string.Empty;
                c.Playing.IsVisible = false;
                c.PositionRow.IsVisible = c.Shuffle.IsVisible = c.Repeat.IsVisible = false;
                _positions.Remove(ip);
                c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = false;
                c.Art.ImageUrl = string.Empty;
                c.Art.Bitmap = null;
                c.Art.PlaceholderIcon = Icons.Music;
                return;
            }

            string leaderIp = TargetOf(ip);
            var leaderStatus = leaderIp != ip ? _statuses.GetValueOrDefault(leaderIp) : null;
            bool following = leaderStatus != null;
            if (leaderStatus != null) status = leaderStatus;
            (c.Title.Text, c.Artist.Text, c.Detail.Text) = _owner.NowPlayingTexts(status, leaderIp, following);
            c.Art.PlaceholderIcon = Icons.ForInput(InputOfMode(status.Mode));
            c.PlayPauseIcon.Data = status.IsPlaying ? Icons.Pause : Icons.Play;
            c.Playing.IsVisible = status.IsPlaying;
            c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = true;
            ApplyPlayback(ip, c, status);
            if (status.ArtUrl != c.Art.ImageUrl)
            {
                c.Art.ImageUrl = status.ArtUrl;
                c.Art.Bitmap = null;
                if (status.ArtUrl.Length > 0) _ = LoadArtworkAsync(c.Art, status.ArtUrl);
            }
        }

        internal bool IsShowingArtwork(Avalonia.Media.Imaging.Bitmap bitmap) =>
            _nowPlayingControls.Values.Any(c => ReferenceEquals(c.Art.Bitmap, bitmap)) ||
            (_npControls != null && ReferenceEquals(_npControls.Art.Bitmap, bitmap)) ||
            ReferenceEquals(_npCover.Source, bitmap) || ReferenceEquals(_npCoverOld.Source, bitmap) ||
            IsBackdrop(bitmap) ||
            _presetImages.Any(i => ReferenceEquals(i.Source, bitmap));

        private async Task LoadArtworkAsync(ArtworkView view, string url)
        {
            var image = await _owner.GetArtworkAsync(url);
            if (view.ImageUrl == url) view.Bitmap = image;
        }

        private async Task TransportAsync(string ip, string action)
        {
            await _owner.SendTransportAsync(TargetOf(ip), action);
            await Task.Delay(600);
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }

        private const string SectionDeviceInfo = "Device info";
        private const string SectionNetwork = "Network";
        private const string SectionStandby = "Standby mode";
        private const string SectionAlarms = "Alarms";
        private const string SectionHardware = "Hardware settings";
        private const string SectionDisplay = "Screen";

        private string _ampSection = SectionDeviceInfo;
        private int _ampVersion;
        private (string Ip, AmpSettings Settings)? _ampCache;

        private string? SelectedAmpIp => (_cboAmp.SelectedItem as ComboBoxItem)?.Tag as string;

        private Control BuildAmpPage()
        {
            _cboAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshAmpAsync(); };
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshAmpAsync();
            var page = Page(SectionDeviceInfo, _lblAmpStatus, [_cboAmp, btnRefresh], _ampList);
            _sectionHandlers[page] = section =>
            {
                _ampSection = section;
                FillDeviceSettingsCombo(followActive: false);
                bool cached = SelectedAmpIp is { } ip && _ampCache is { } cache && cache.Ip == ip;
                SetRows(_ampList, cached ? BuildAmpCards(_ampCache!.Value.Ip, _ampCache.Value.Settings) : []);
                if (page.IsVisible) _ = RefreshAmpAsync(quiet: cached);
            };
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshAmpAsync(quiet: _ampCache?.Ip == SelectedAmpIp);
            };
            return page;
        }

        private Button? _displayNav;

        private void FillDeviceSettingsCombo(bool followActive)
        {
            var devices = _owner._knownDevices.Where(d => _ampSection != SectionDisplay || _owner.HasScreen(d.Ip)).ToList();
            string? selected = (followActive ? null : SelectedAmpIp) ?? _owner._deviceIp;
            if (!devices.Any(d => d.Ip == selected))
                selected = devices.Any(d => d.Ip == _owner._deviceIp) ? _owner._deviceIp : devices.Select(d => d.Ip).FirstOrDefault();
            bool wasLoading = _loading;
            _loading = true;
            _cboAmp.Items.Clear();
            foreach (var d in devices) _cboAmp.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == selected);
            _cboAmp.SelectedIndex = index >= 0 ? index : _cboAmp.Items.Count > 0 ? 0 : -1;
            _loading = wasLoading;
        }

        private void UpdateDisplayNav()
        {
            if (_displayNav == null) return;
            bool any = _owner._knownDevices.Any(d => _owner.HasScreen(d.Ip));
            _displayNav.IsVisible = any;
            if (!any && _ampSection == SectionDisplay && _pages.Any(p => p.Item == _displayNav && p.Page.IsVisible))
                ShowPage(_pages.First(p => p.Item == _displayNav).Page, SectionDeviceInfo);
        }

        private async Task RefreshAmpAsync(bool quiet = false)
        {
            string? ip = SelectedAmpIp;
            if (ip == null)
            {
                _lblAmpStatus.Text = string.Empty;
                SetRows(_ampList, [Muted("Add a WiiM device on the Devices page first.")]);
                return;
            }
            if (!quiet) _lblAmpStatus.Text = "Loading…";
            int version = ++_ampVersion;
            var settings = await _owner.GetAmpSettingsAsync(ip, _screenInputIp == ip ? _screenInput : null, _ampSection == SectionExperimental,
                _ampSection == SectionDeviceInfo);
            if (ip != SelectedAmpIp || version != _ampVersion) return;
            if (settings != null) _ampCache = (ip, settings);
            if (settings?.Screen != null && settings.ScreenUi != null && !_owner.HasScreen(ip))
            {
                _owner.NoteScreen(ip);
                UpdateDisplayNav();
            }
            _lblAmpStatus.Text = settings == null ? "Not reachable" : string.Empty;
            SetRows(_ampList, settings == null ? [Muted("Couldn't reach this device.")] : BuildAmpCards(ip, settings));
        }

        private async Task ChangeAmpAsync(Func<Task<bool>> change)
        {
            await _ampGate.WaitAsync();
            bool ok;
            try
            {
                _lblAmpStatus.Text = "Saving…";
                ok = await change();
            }
            finally { _ampGate.Release(); }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
            if (_ampGate.CurrentCount == 0) return;
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
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose pictures for the screen", AllowMultiple = info.MaxNumber > 1,
                FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp", "*.gif"] }, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            if (files.Count > info.MaxNumber)
            {
                await Dialogs.MessageAsync(this, $"The device can show up to {info.MaxNumber} backgrounds. Pick fewer pictures.", _scale);
                return;
            }
            var kept = info.MaxNumber > 1 ? info.Current.Skip(Math.Max(0, info.Current.Count + files.Count - info.MaxNumber)).ToList() : [];
            if (info.Current.Except(kept).Any(n => !IsBuiltInWallpaper(n)) && !await Dialogs.ConfirmAsync(this,
                    "Adding these pictures replaces a picture you uploaded before. The device deletes uploaded pictures when they're no " +
                    "longer used, so it will be gone. Continue?", _scale)) return;
            var uploaded = new List<string>();
            var failed = new List<string>();
            foreach (var file in files)
            {
                _lblAmpStatus.Text = $"Uploading {file.Name}…";
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
                string? name = jpeg != null ? await _owner.UploadWallpaperAsync(ip, jpeg) : null;
                if (name != null) uploaded.Add(name);
                else failed.Add(file.Name);
            }
            _lblAmpStatus.Text = string.Empty;
            if (uploaded.Count > 0)
            {
                await Task.Delay(1000);
                var current = kept.Concat(uploaded).ToList();
                if (current.Count > info.MaxNumber) current = current.Skip(current.Count - info.MaxNumber).ToList();
                await ChangeAmpAsync(() => _owner.SetWallpapersAsync(ip, info, current));
            }
            if (failed.Count > 0)
                await Dialogs.MessageAsync(this, (failed.Count == 1 ? $"{failed[0]} couldn't" : $"{failed.Count} pictures couldn't") +
                    " be uploaded. Check that it's a picture file (JPEG, PNG, BMP, WebP or GIF) and that the device is reachable.", _scale);
        }

        private static bool IsBuiltInWallpaper(string name) => IsBuiltInWallpaperName(name);

        private async Task LoadWallpaperTilesAsync(string ip, WallpaperInfo info, WrapPanel tiles)
        {
            var thumbnails = await _owner.GetWallpaperThumbnailsAsync(ip, info);
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
                    if (removedUploads.Count > 0 && !await Dialogs.ConfirmAsync(this,
                            "This removes a picture you uploaded yourself. The device deletes uploaded pictures when they're no longer " +
                            "used, so it will be gone and has to be uploaded again in the WiiM Home app. Continue?", _scale)) return;
                    await ChangeAmpAsync(() => _owner.SetWallpapersAsync(ip, info, current));
                };
                tiles.Children.Add(tile);
            }
        }

        private static LevelSlider AmpSlider(int min, int max, int value, Func<int, string> format, bool fromCenter, out Control row)
        {
            var slider = new LevelSlider { Minimum = min, Maximum = max, FillFromCenter = fromCenter, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            slider.Value = value;
            var label = new TextBlock { Text = format(slider.Value), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            label.MinWidth = new[] { min, max, (min + max) / 2, value }.Select(v =>
            {
                var probe = new TextBlock { Text = format(v), FontSize = 14 };
                probe.Measure(Size.Infinity);
                return Math.Ceiling(probe.DesiredSize.Width);
            }).Max();
            slider.ValueChanged += (_, _) => label.Text = format(slider.Value);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Width = FieldWidth };
            grid.Children.Add(slider);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            row = grid;
            return slider;
        }

        private static string BalanceText(int value) =>
            value == 0 ? "Center" : value < 0 ? $"L {-value}" : $"R {value}";

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
                    if (s.Screen is { } screen && (s.ScreenUi != null || _owner.HasScreen(ip))) cards.Add(BuildScreenCard(ip, screen, s.ScreenUi, s.Wallpapers));
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
                rows.Add(AmpToggle("Status light", statusLight, on => _owner.SetStatusLightAsync(ip, on)));
            if (s.ButtonsLocked is bool locked)
            {
                var buttonLock = AmpToggle("Lock the buttons on the device", locked, on => _owner.SetButtonsLockedAsync(ip, on));
                ToolTip.SetTip(buttonLock, "Stops the buttons and controls on the device itself from working, for example so children can't change anything. Wiim Control and the WiiM Home app still work.");
                rows.Add(buttonLock);
            }
            if (s.PromptSounds is bool promptSounds)
            {
                var prompts = AmpToggle("Prompt sounds", promptSounds, on => _owner.SetPromptSoundsAsync(ip, on));
                ToolTip.SetTip(prompts, "The device's sounds and voice prompts, for example when Bluetooth connects. Important prompts always play.");
                rows.Add(prompts);
            }
            if (s.VolumeLightMode is int volumeLightMode)
                rows.Add(AmpChoice("Volume lights", VolumeLightModes.Select(m => (m.Mode, m.Label)), volumeLightMode,
                    mode => _owner.SetVolumeLightModeAsync(ip, mode)));
            var restart = Pill("Restart");
            ToolTip.SetTip(restart, "Restarts the device. It stops playing and is back after about a minute.");
            restart.Click += async (_, _) => await RestartDeviceAsync(ip);
            rows.Add(LabeledRow("Restart device", restart));
            return Card("Hardware", rows.ToArray());
        }

        private void ReloadSoon(bool refreshEq) => Dispatcher.UIThread.Post(() =>
        {
            LoadFromOwner();
            if (refreshEq) _ = RefreshEqAsync();
            _ = RefreshMultiroomAsync();
            _ = RefreshAmpAsync();
        });

        private async Task ShowConnectionTestAsync() =>
            await Dialogs.MessageAsync(this, await _owner.TestConnectionAsync(), _scale);

        private Control BuildMultiroomPage()
        {
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshMultiroomAsync();
            return Page("Multiroom", _lblGroupStatus, [btnRefresh], _groupList);
        }

        private async Task RefreshMultiroomAsync()
        {
            _lblGroupStatus.Text = "Checking…";
            var homeTheater = RefreshHomeTheaterAsync();
            _multiroom = await _owner.GetMultiroomSnapshotAsync();
            UpdateDisplayNav();
            RebuildMultiroom();
            RebuildDeviceCards();
            await RefreshChannelsAsync();
            await homeTheater;
        }

        private string DeviceName(string ip) => _owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;

        private (string Text, IBrush Brush)? GroupDescription(string ip)
        {
            if (_multiroom == null) return null;
            if (!_multiroom.Online.Contains(ip)) return ("Not reachable", Palette.TextDim);
            if (_multiroom.LeaderOf.TryGetValue(ip, out var leader)) return ($"Linked to {DeviceName(leader)}", Palette.AccentText);
            int members = _multiroom.MemberCount(ip);
            return members > 0 ? ($"Group leader  ·  {members} linked", Palette.AccentText) : null;
        }

        private string LinkStatus(MultiroomSnapshot snapshot, string ip)
        {
            if (!snapshot.Online.Contains(ip)) return "Not reachable";
            if (snapshot.LeaderOf.TryGetValue(ip, out var leader)) return $"Linked to {DeviceName(leader)}";
            int members = snapshot.MemberCount(ip);
            return members > 0 ? $"Group leader  ·  {members} linked" : "Not linked";
        }

        private void RebuildMultiroom()
        {
            var devices = _owner._knownDevices.ToList();
            var cards = new List<Control>();
            var snapshot = _multiroom;

            if (snapshot == null)
            {
                cards.Add(Muted("Checking which devices are linked…"));
            }
            else if (devices.Count < 2)
            {
                _lblGroupStatus.Text = "Not grouped";
                cards.Add(Card("Only one device", Muted("Add another WiiM device on the Devices page to play music in sync.")));
            }
            else
            {
                int groups = snapshot.LeaderOf.Values.Distinct().Count();
                _lblGroupStatus.Text = groups == 0 ? "Not grouped" : $"{groups} group{(groups == 1 ? "" : "s")}";
                string leaderIp = _owner.EffectiveGroupLeader;
                cards.Add(BuildLeaderChoiceCard(devices));
                cards.Add(BuildLeaderGroupCard(snapshot, leaderIp, devices));
                foreach (var otherLeader in snapshot.LeaderOf.Values.Distinct().Where(l => l != leaderIp))
                    cards.Add(BuildOtherGroupCard(snapshot, otherLeader));
            }
            if (devices.Count > 1) cards.Add(HomeTheaterCard());
            SetRows(_groupList, cards);
        }

        private Border BuildLeaderChoiceCard(List<KnownDevice> devices)
        {
            var combo = new ComboBox { Width = 320 };
            combo.Items.Add(ComboItem(string.Empty, $"Controlled device ({DeviceName(_owner._deviceIp)})"));
            foreach (var d in devices) combo.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == _owner._groupLeaderIp);
            combo.SelectedIndex = index >= 0 ? index + 1 : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string ip) return;
                _owner.SetGroupLeader(ip);
                Dispatcher.UIThread.Post(RebuildMultiroom);
            };
            return Card("Group leader", LabeledRow("Leader", combo), Muted("Linked devices play whatever the leader plays."));
        }

        private Border BuildLeaderGroupCard(MultiroomSnapshot snapshot, string leaderIp, List<KnownDevice> devices)
        {
            string leaderName = DeviceName(leaderIp);
            var rows = new List<Control>();

            if (!snapshot.Online.Contains(leaderIp))
            {
                rows.Add(Muted($"{leaderName} is not reachable."));
            }
            else if (snapshot.LeaderOf.TryGetValue(leaderIp, out var currentLeader))
            {
                rows.Add(Muted($"{leaderName} is linked to {DeviceName(currentLeader)}, so it can't lead a group."));
                var leave = Pill("Leave that group");
                leave.Click += async (_, _) =>
                    await ChangeGroupAsync(() => _owner.RemoveFromGroupAsync(currentLeader, snapshot.AddressOf(leaderIp)));
                rows.Add(leave);
            }
            else
            {
                foreach (var other in devices.Where(d => d.Ip != leaderIp))
                {
                    var o = other;
                    bool linkedHere = snapshot.LeaderOf.TryGetValue(o.Ip, out var current) && current == leaderIp;
                    var toggle = new ToggleSwitch { IsChecked = linkedHere, IsEnabled = snapshot.Online.Contains(o.Ip) };
                    toggle.IsCheckedChanged += async (_, _) =>
                        await ChangeGroupAsync(() => SetLinkedAsync(snapshot, o.Ip, leaderIp, toggle.IsChecked == true));
                    rows.Add(ToggleRow(o.Name, toggle));
                    var status = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 6),
                        Children = { new TextBlock { Text = linkedHere ? "Linked" : LinkStatus(snapshot, o.Ip), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center } }
                    };
                    rows.Add(status);
                    if (!linkedHere) continue;
                    var (rename, renameIcon) = IconButton(Icons.Edit);
                    renameIcon.Width = renameIcon.Height = 14;
                    rename.Width = rename.Height = 24;
                    rename.Padding = new Thickness(0);
                    rename.MinHeight = 0;
                    ToolTip.SetTip(rename, $"Rename {o.Name}");
                    rename.Click += async (_, _) =>
                    {
                        await RenameDeviceAsync(o);
                        await RefreshMultiroomAsync();
                    };
                    status.Children.Add(rename);
                    int channel = snapshot.Channels?.GetValueOrDefault(o.Ip) ?? ChannelStereo;
                    var channels = SegmentSwitch([(ChannelStereo, "Stereo"), (ChannelLeft, "Left"), (ChannelRight, "Right")],
                        channel is ChannelLeft or ChannelRight ? channel : ChannelStereo, FieldWidth,
                        value => ChangeGroupAsync(() => _owner.SetRoomChannelAsync(leaderIp, snapshot.AddressOf(o.Ip), value)));
                    var channelRow = LabeledRow("Plays", channels);
                    ToolTip.SetTip(channelRow, $"Whether {o.Name} plays both channels or only the left or right one in this group, for example to use two devices as a stereo pair");
                    channelRow.Margin = new Thickness(0, 0, 0, 10);
                    rows.Add(channelRow);
                }
                if (snapshot.MemberCount(leaderIp) > 0)
                {
                    var ungroup = Pill("Ungroup");
                    ungroup.Margin = new Thickness(0, 8, 0, 0);
                    ungroup.Click += async (_, _) => await ChangeGroupAsync(() => _owner.UngroupAsync(leaderIp));
                    rows.Add(ungroup);
                }
            }

            var card = Card($"Play along with {leaderName}", rows.ToArray());
            if (snapshot.MemberCount(leaderIp) > 0) card.Classes.Add("active");
            return card;
        }

        private Border BuildOtherGroupCard(MultiroomSnapshot snapshot, string leaderIp)
        {
            var members = snapshot.LeaderOf.Where(p => p.Value == leaderIp).Select(p => DeviceName(p.Key));
            var ungroup = Pill("Ungroup");
            ungroup.Click += async (_, _) => await ChangeGroupAsync(() => _owner.UngroupAsync(leaderIp));
            return Card($"Group led by {DeviceName(leaderIp)}", Muted($"Linked: {string.Join(", ", members)}"), ungroup);
        }

        private async Task<bool> SetLinkedAsync(MultiroomSnapshot snapshot, string memberIp, string leaderIp, bool linked)
        {
            if (snapshot.LeaderOf.TryGetValue(memberIp, out var current))
            {
                if (linked && current == leaderIp) return true;
                if (!await _owner.RemoveFromGroupAsync(current, snapshot.AddressOf(memberIp))) return false;
                if (!linked) return true;
            }
            else if (!linked)
            {
                return true;
            }
            return await _owner.JoinGroupAsync(memberIp, leaderIp);
        }

        private async Task ChangeGroupAsync(Func<Task<bool>> change)
        {
            if (_groupBusy) return;
            _groupBusy = true;
            _groupList.IsEnabled = false;
            _lblGroupStatus.Text = "Updating…";

            bool ok = await change();
            if (ok) await Task.Delay(2500);

            _groupBusy = false;
            _groupList.IsEnabled = true;
            if (!ok)
                await Dialogs.MessageAsync(this, "The device didn't accept the request. Check that the devices are switched on and reachable.", _scale);
            await RefreshMultiroomAsync();
        }

        private Control BuildEqPage()
        {
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshEqAsync();

            _btnEqSaveCurrent.Click += async (_, _) => await SaveCurrentEqPresetAsync();
            _btnEqSave.Click += async (_, _) => await SaveEqPresetAsync();
            _btnEqRename.Click += async (_, _) => await RenameEqPresetAsync();
            _btnEqDelete.Click += async (_, _) => await DeleteEqPresetAsync();
            HookEqFileButtons();
            HookQuickPresetButton();
            var presetButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { _btnEqSaveCurrent, _btnEqSave, _btnEqRename, _btnEqDelete, _btnEqExport, _btnEqImport, _btnEqQuick }
            };
            var presetPicker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 2) };
            _eqPresetChips.VerticalAlignment = VerticalAlignment.Center;
            _eqPresetChips.Margin = new Thickness(0, 0, 8, 0);
            presetPicker.Children.Add(_eqPresetChips);
            Grid.SetColumn(_cboEqPreset, 1);
            _cboEqPreset.VerticalAlignment = VerticalAlignment.Center;
            presetPicker.Children.Add(_cboEqPreset);
            _cboEqPreset.SelectionChanged += (_, _) => UpdatePresetChips();
            _eqTypeRow = BuildEqTypeSwitch();
            _eqInputRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, IsVisible = false,
                Children = { new TextBlock { Text = "Input", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, _cboEqInput }
            };
            var eqRow = LabeledRow("Enable EQ", new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Children = { _chkEqEnabled, _eqInputRow }
            });
            ToolTip.SetTip(_cboEqInput, "The input whose EQ is shown. Each input has its own EQ.");
            _chkRoomFit.VerticalAlignment = _chkEqEnabled.VerticalAlignment = VerticalAlignment.Center;
            _roomFitRow = LabeledRow("RoomFit room correction", new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { _chkRoomFit, _btnRoomFitExport, _btnRoomFitImport, _btnRoomFitCurve, _cboRoomFit }
            });
            _roomFitRow.IsVisible = false;
            _btnRoomFitCurve.Click += async (_, _) => await ShowRoomFitCurveAsync();
            ToolTip.SetTip(_roomFitRow, "Measured with the WiiM Home app. Applies to every input, on top of the EQ.");
            ToolTip.SetTip(_cboRoomFit, "The RoomFit profile that's used");
            var topCard = Card(null, _roomFitRow, eqRow, LabeledRow("Preset", presetButtons), presetPicker);

            var table = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", EqBandCount))),
                RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                MinHeight = 200
            };
            for (int i = 0; i < EqBandCount; i++)
            {
                int band = i;
                var slider = new EqSlider { Minimum = 2, Maximum = 98, Value = 50, Center = 50, Step = 2, Margin = new Thickness(0, 4, 0, 6) };
                var lblValue = new TextBlock { Text = FormatEqDb(50), HorizontalAlignment = HorizontalAlignment.Center };
                var lblFreq = new TextBlock { Text = EqBandLabels[i], Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                slider.ValueChanged += (_, _) => OnEqSliderChanged(band);
                _eqSliders[i] = slider;
                _eqValueLabels[i] = lblValue;
                Grid.SetColumn(slider, i);
                Grid.SetColumn(lblValue, i);
                Grid.SetRow(lblValue, 1);
                Grid.SetColumn(lblFreq, i);
                Grid.SetRow(lblFreq, 2);
                table.Children.Add(slider);
                table.Children.Add(lblValue);
                table.Children.Add(lblFreq);
            }

            var eqCard = new Border { Child = table, Transitions = [FadeIn()] };
            _geqEditor = eqCard;
            _peqEditor = BuildPeqEditor();
            _peqEditor.Transitions = [FadeIn()];

            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            body.Children.Add(topCard);
            var editorGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            editorGrid.Children.Add(_eqTypeRow);
            var editors = new Panel { Children = { eqCard, _peqEditor } };
            Grid.SetRow(editors, 1);
            editorGrid.Children.Add(editors);
            var editorCard = new Border { Child = editorGrid, Margin = new Thickness(0) };
            editorCard.Classes.Add("card");
            Grid.SetRow(editorCard, 1);
            body.Children.Add(editorCard);

            _eqEditControls.Add(_cboEqPreset);
            _eqEditControls.AddRange(_eqSliders);

            _chkEqEnabled.IsCheckedChanged += async (_, _) =>
            {
                if (_eqLoading) return;
                UpdateEqEnabledState();
                bool ok = _chkEqEnabled.IsChecked == true
                    ? await _owner.SetEqTypeAsync(_eqDeviceIp, EqTarget, _eqPluginUri)
                    : await _owner.TurnEqOffAsync(_eqDeviceIp, EqTarget, _eqPluginUri);
                if (!ok) await RefreshEqAsync();
            };
            _cboEqInput.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboEqInput.SelectedItem as ComboBoxItem)?.Tag is not string input || input == _eqSource) return;
                _eqInputChoice = input;
                await RefreshEqAsync();
            };
            _cboEqPreset.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                _eqSendTimer.Stop();
                _eqDirtyBands.Clear();
                _peqSendTimer.Stop();
                _peqDirty.Clear();
                if (_eqLegacy) await _owner.LoadEqPresetAsync(_eqDeviceIp, name);
                else await _owner.LoadSourcePresetAsync(_eqDeviceIp, _eqSource, _eqPluginUri, name);
                await RefreshEqAsync();
            };
            _chkRoomFit.IsCheckedChanged += async (_, _) =>
            {
                if (_eqLoading) return;
                if (!await _owner.SetRoomFitEnabledAsync(_eqDeviceIp, _chkRoomFit.IsChecked == true)) await RefreshEqAsync();
            };
            _cboRoomFit.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboRoomFit.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                if (!await _owner.LoadRoomFitProfileAsync(_eqDeviceIp, name, _chkRoomFit.IsChecked == true)) await RefreshEqAsync();
            };

            _chkEqEnabled.IsEnabled = false;
            UpdateEqEnabledState();
            ToolTip.SetTip(_cboEqAmp, "The device whose EQ is shown");
            _cboEqAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshEqAsync(); };
            return Page("Equalizer", _lblEqSource, [_cboEqAmp, btnRefresh], body, scroll: false);
        }

        private static Avalonia.Animation.DoubleTransition FadeIn() => new() { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) };

        private static string FormatEqDb(int value) =>
            ((value - 50) / 4.0).ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.InvariantCulture);

        private string? SelectedEqPreset => (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag as string;

        private string EqTarget => _eqLegacy ? string.Empty : _eqSource;

        private string EqIp => AmpOf(_cboEqAmp) ?? _owner._deviceIp;

        private void FillEqAmpCombo()
        {
            string? chosen = AmpOf(_cboEqAmp);
            string selected = chosen == null || _followActive ? _owner._deviceIp : chosen;
            _cboEqAmp.Items.Clear();
            foreach (var d in _owner._knownDevices) _cboEqAmp.Items.Add(ComboItem(d.Ip, d.Name));
            int index = _owner._knownDevices.FindIndex(d => d.Ip == selected);
            _cboEqAmp.SelectedIndex = index >= 0 ? index : _cboEqAmp.Items.Count > 0 ? 0 : -1;
        }

        private void UpdateEqEnabledState()
        {
            bool editable = _eqAvailable && _chkEqEnabled.IsChecked == true;
            foreach (var c in _eqEditControls) c.IsEnabled = editable;
            _eqPresetChips.IsEnabled = editable;
            _btnGraphicEq.IsEnabled = _btnParametricEq.IsEnabled = _cboEqInput.IsEnabled = _eqAvailable;
            UpdatePeqRowStates();
            bool customSelected = SelectedEqPreset is { } name && _eqCustomPresets.Contains(name);
            _btnEqSave.IsEnabled = _btnEqSaveCurrent.IsEnabled = editable;
            _btnEqRename.IsEnabled = _btnEqDelete.IsEnabled = editable && customSelected;
            UpdateEqFileButtons();
            ToolTip.SetTip(_btnEqSaveCurrent, BaseCustomPreset is { } preset
                ? $"Save the changes to \"{preset}\""
                : "Save the current EQ as a new preset");
        }

        private void OnEqSliderChanged(int band)
        {
            _eqValueLabels[band].Text = FormatEqDb(_eqSliders[band].Value);
            if (_eqLoading) return;
            _eqDirtyBands.Add(band);
            if (!_eqSendTimer.IsEnabled) _eqSendTimer.Start();
            MarkEqCustom();
        }

        private void MarkEqCustom()
        {
            if (_cboEqPreset.SelectedIndex >= 0)
            {
                _eqLoading = true;
                _cboEqPreset.SelectedIndex = -1;
                _eqLoading = false;
                UpdateEqEnabledState();
            }
        }

        private async Task<bool> SendPendingEqBandsAsync() => await FlushEqBandsAsync() & await FlushPeqAsync();

        private async Task<bool> IsValidPresetNameAsync(string name)
        {
            string? problem =
                name.Length > 30 ? "Please use at most 30 characters." :
                name.IndexOfAny([':', '"', '\\', '{', '}']) >= 0 ? "Please don't use : \" \\ { or } in the name." :
                _eqBuiltInPresets.Contains(name) ? $"\"{name}\" is a built-in preset. Please choose another name." :
                null;
            if (problem != null) await Dialogs.MessageAsync(this, problem, _scale);
            return problem == null;
        }

        private string? BaseCustomPreset => _eqBasePreset is { } name && _eqCustomPresets.Contains(name) ? name : null;

        private async Task SaveCurrentEqPresetAsync()
        {
            if (BaseCustomPreset is not { } name)
            {
                await SaveEqPresetAsync();
                return;
            }
            await SendPendingEqBandsAsync();
            if (!await _owner.SaveEqPresetAsync(_eqDeviceIp, name, EqTarget, _eqPluginUri, replacing: true))
                await Dialogs.MessageAsync(this, "The device didn't save the preset.", _scale);
            await RefreshEqAsync();
        }

        private async Task SaveEqPresetAsync()
        {
            string suggestion = BaseCustomPreset ?? string.Empty;
            string? name = await Dialogs.PromptAsync(this, "Save the current EQ on the device as a preset named:", suggestion, _scale);
            if (string.IsNullOrEmpty(name) || !await IsValidPresetNameAsync(name)) return;
            bool replacing = _eqCustomPresets.Contains(name);
            if (replacing && !await Dialogs.ConfirmAsync(this, $"Replace the preset \"{name}\" with the current EQ?", _scale)) return;

            await SendPendingEqBandsAsync();
            if (!await _owner.SaveEqPresetAsync(_eqDeviceIp, name, EqTarget, _eqPluginUri, replacing))
                await Dialogs.MessageAsync(this, "The device didn't save the preset.", _scale);
            await RefreshEqAsync();
        }

        private async Task RenameEqPresetAsync()
        {
            if (SelectedEqPreset is not { } oldName || !_eqCustomPresets.Contains(oldName)) return;
            string? name = await Dialogs.PromptAsync(this, $"New name for \"{oldName}\":", oldName, _scale);
            if (string.IsNullOrEmpty(name) || name == oldName || !await IsValidPresetNameAsync(name)) return;
            if (_eqCustomPresets.Contains(name))
            {
                await Dialogs.MessageAsync(this, $"There is already a preset called \"{name}\".", _scale);
                return;
            }
            if (!await _owner.RenameEqPresetAsync(_eqDeviceIp, oldName, name, _eqPluginUri))
                await Dialogs.MessageAsync(this, "The device didn't rename the preset.", _scale);
            else if (_eqBasePreset == oldName) _eqBasePreset = name;
            await RefreshEqAsync();
        }

        private async Task DeleteEqPresetAsync()
        {
            if (SelectedEqPreset is not { } name || !_eqCustomPresets.Contains(name)) return;
            if (!await Dialogs.ConfirmAsync(this, $"Delete the preset \"{name}\" from the device?", _scale)) return;
            if (!await _owner.DeleteEqPresetAsync(_eqDeviceIp, name, _eqPluginUri))
                await Dialogs.MessageAsync(this, "The device didn't delete the preset.", _scale);
            await RefreshEqAsync();
        }

        private async Task<bool> FlushEqBandsAsync()
        {
            _eqSendTimer.Stop();
            await _eqFlushGate.WaitAsync();
            try
            {
                while (_eqDirtyBands.Count > 0)
                {
                    var indices = _eqDirtyBands.Order().ToList();
                    var bands = indices.Select(i => new EqBand(i, EqBandParam(i), _eqSliders[i].Value)).ToList();
                    _eqDirtyBands.Clear();
                    bool ok = _eqLegacy
                        ? await _owner.SetEqBandsAsync(_eqDeviceIp, bands)
                        : await _owner.SetGeqBandsAsync(_eqDeviceIp, _eqSource, bands);
                    if (ok) continue;
                    foreach (int index in indices) _eqDirtyBands.Add(index);
                    return false;
                }
                return true;
            }
            finally { _eqFlushGate.Release(); }
        }

        private string EqBandParam(int index)
        {
            var known = _eqBands.FirstOrDefault(b => b.Index == index).ParamName;
            return string.IsNullOrEmpty(known) ? EqBandParams[index] : known;
        }

        private async Task RefreshEqAsync()
        {
            await FlushEqBandsAsync();
            await FlushPeqAsync();
            int version = ++_eqRefreshVersion;
            _lblEqSource.Text = "Loading…";
            string ip = EqIp;
            string? inputChoice = ip == _eqDeviceIp ? _eqInputChoice : null;
            var currentTask = _owner.GetEqStateAsync(ip);
            var modesTask = _owner.GetEqSourceModesAsync(ip);
            var inputsTask = _owner.GetInputsAsync(ip);
            var roomFitTask = _owner.GetRoomFitStateAsync(ip);
            await Task.WhenAll(currentTask, modesTask, inputsTask, roomFitTask);
            var current = currentTask.Result;
            var modes = current is { Source.Length: > 0 } ? modesTask.Result : null;
            bool legacy = modes == null;
            string currentInput = current?.Source ?? string.Empty;
            var shownInputs = !legacy ? inputsTask.Result : null;
            var inputs = modes?.Select(m => m.Source)
                .Where(i => shownInputs == null || i == currentInput || shownInputs.Contains(i, StringComparer.OrdinalIgnoreCase))
                .ToList() ?? [];
            if (currentInput.Length > 0 && !inputs.Contains(currentInput)) inputs.Insert(0, currentInput);
            string source = !legacy && inputChoice is { } choice && inputs.Contains(choice) ? choice : currentInput;
            var mode = modes?.FirstOrDefault(m => m.Source == source);

            var stateTask = legacy ? Task.FromResult(current) : _owner.GetGeqStateAsync(ip, source);
            var peqTask = legacy ? Task.FromResult<PeqState?>(null) : _owner.GetPeqStateAsync(ip, source);
            await Task.WhenAll(stateTask, peqTask);
            var state = stateTask.Result;
            var peq = peqTask.Result;
            bool enabled = mode?.Enabled ?? (state?.Enabled == true || peq?.Enabled == true);
            string plugin =
                peq == null ? DefaultEqPlugin
                : mode != null && (mode.PluginUri == PeqPlugin || mode.PluginUri == DefaultEqPlugin) && (enabled || source != _eqSource || ip != _eqDeviceIp) ? mode.PluginUri
                : peq.Enabled ? PeqPlugin
                : state?.Enabled == true ? DefaultEqPlugin
                : _eqPluginUri;
            var presets = await _owner.GetEqPresetListAsync(ip, plugin);
            string presetName = plugin == PeqPlugin ? peq?.Name ?? string.Empty : state?.Name ?? string.Empty;
            if (version != _eqRefreshVersion) return;

            _eqDeviceIp = ip;
            _eqInputChoice = inputChoice;
            _eqLoading = true;
            _eqLegacy = legacy;
            _eqAvailable = state != null;
            _eqSource = source;
            _eqPluginUri = plugin;
            string baseKey = $"{ip}|{source}|{plugin}";
            if (presetName.Length > 0) _eqBasePreset = presetName;
            else if (baseKey != _eqBaseKey) _eqBasePreset = null;
            _eqBaseKey = baseKey;
            _cboEqInput.Items.Clear();
            foreach (var input in inputs)
                _cboEqInput.Items.Add(ComboItem(input, input == currentInput ? $"{_owner.InputName(ip, input)}  (current)" : _owner.InputName(ip, input)));
            _cboEqInput.SelectedIndex = inputs.IndexOf(source);
            if (_eqInputRow != null) _eqInputRow.IsVisible = !legacy && inputs.Count > 0;
            if (_eqTypeRow != null) _eqTypeRow.IsVisible = peq != null;
            _eqCustomPresets.Clear();
            _eqCustomPresets.UnionWith(presets.Custom);
            _eqBuiltInPresets.Clear();
            _eqBuiltInPresets.UnionWith(presets.BuiltIn);
            _cboEqPreset.Items.Clear();
            foreach (var p in presets.Custom) _cboEqPreset.Items.Add(ComboItem(p, $"{p}  (custom)"));
            foreach (var p in presets.BuiltIn) _cboEqPreset.Items.Add(ComboItem(p, p));
            SetPresetLists(presets.Custom, presets.BuiltIn);
            if (state != null)
            {
                var names = presets.Custom.Concat(presets.BuiltIn).ToList();
                if (presetName.Length > 0 && !names.Contains(presetName))
                {
                    _cboEqPreset.Items.Add(ComboItem(presetName, presetName));
                    names.Add(presetName);
                }
                _cboEqPreset.SelectedIndex = presetName.Length > 0 ? names.IndexOf(presetName) : -1;
                _chkEqEnabled.IsChecked = enabled;
                _eqBands = state.Bands;
                foreach (var b in state.Bands.Where(b => b.Index >= 0 && b.Index < EqBandCount))
                {
                    var slider = _eqSliders[b.Index];
                    slider.Value = Math.Clamp(b.Value, slider.Minimum, slider.Maximum);
                }
                _lblEqSource.Text = legacy && state.Source.Length > 0 ? $"Input: {_owner.InputName(ip, state.Source)}" : string.Empty;
            }
            else
            {
                _lblEqSource.Text = "EQ unavailable";
            }
            ApplyPeqState(peq);
            ShowEqEditor();
            var roomFit = roomFitTask.Result;
            if (_roomFitRow != null) _roomFitRow.IsVisible = roomFit != null;
            _cboRoomFit.Items.Clear();
            if (roomFit != null)
            {
                foreach (var p in roomFit.Profiles) _cboRoomFit.Items.Add(ComboItem(p, p));
                _cboRoomFit.SelectedIndex = roomFit.Profiles.IndexOf(roomFit.Active);
                _chkRoomFit.IsChecked = roomFit.Enabled;
            }
            _eqLoading = false;

            _chkEqEnabled.IsEnabled = _eqAvailable;
            UpdateEqEnabledState();
        }

        private Control BuildVolumePage()
        {
            _numStep.ValueChanged += (_, _) => { if (_loading) return; _owner.SetVolumeStep(_numStep.Value); };
            _chkLogStep.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetLogStep(_chkLogStep.IsChecked == true); };
            _chkGroupVolume.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetGroupVolumeKeys(_chkGroupVolume.IsChecked == true); };
            var keysCard = Card("Volume keys",
                LabeledRow("Step size", _numStep),
                ToggleRow("Logarithmic step (finer at low volume)", _chkLogStep),
                ToggleRow("Volume keys also change linked devices", _chkGroupVolume));

            foreach (var c in Enum.GetValues<OsdCorner>()) _cboCorner.Items.Add(ComboItem(c, OsdCornerLabel(c)));
            foreach (int ms in DurationChoices) _cboDuration.Items.Add(ComboItem(ms, OsdDurationLabel(ms)));
            _cboCorner.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                if ((_cboCorner.SelectedItem as ComboBoxItem)?.Tag is OsdCorner c) _owner.SetOsdCorner(c);
            };
            _cboDuration.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                if ((_cboDuration.SelectedItem as ComboBoxItem)?.Tag is int ms) _owner.SetOsdDuration(ms);
            };
            foreach (int percent in new[] { 75, 90, 100, 110, 125, 150, 175, 200, 250 }) _cboOsdScale.Items.Add(ComboItem(percent, $"{percent}%"));
            _cboOsdScale.SelectionChanged += (_, _) =>
            {
                if (_loading || (_cboOsdScale.SelectedItem as ComboBoxItem)?.Tag is not int percent) return;
                _owner.SetOsdScale(percent);
                var preview = _volumes.TryGetValue(_owner._deviceIp, out var current) ? current : new DeviceVolume(50, false);
                _owner.ShowVolumeOsd(preview.Volume, preview.Muted);
            };
            var overlayCard = Card("Volume overlay", LabeledRow("Position", _cboCorner), LabeledRow("Duration", _cboDuration),
                LabeledRow("Size", _cboOsdScale));

            var cards = new StackPanel { Children = { keysCard, overlayCard } };
            return Page("Volume keys", null, [], cards);
        }

        private Control BuildAudioSettingsPage()
        {
            var body = new StackPanel();
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshAmpVolumeAsync();
            var page = Page("Audio settings", null, [_cboVolumeAmp, refresh], body);
            body.Children.Add(BuildAmpVolumeCard(page));
            body.Children.Add(_dynamicBassHost);
            return page;
        }

        private Control BuildGroupDelayPage()
        {
            var body = new StackPanel { Children = { GroupDelayCard(), SyncAudioCard() } };
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshGroupDelayAsync();
            var page = Page("Group audio delay", null, [_cboDelayAmp, refresh], body);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) await RefreshGroupDelayAsync();
            };
            return page;
        }

        private static Control CoffeeButton()
        {
            var badge = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0.85,
                Child = new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri("avares://WiimControl/Assets/bmc.png"))),
                    Height = 28, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(badge, "Buy me a coffee");
            badge.PointerEntered += (_, _) => badge.Opacity = 1;
            badge.PointerExited += (_, _) => badge.Opacity = 0.85;
            badge.Tapped += (_, _) => OpenUrl("https://buymeacoffee.com/yockser");
            return badge;
        }

        private static Control WiimButton()
        {
            var badge = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), ClipToBounds = true, Background = Brushes.Black,
                Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0.85,
                Child = new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri("avares://WiimControl/Assets/wiim.png"))),
                    Stretch = Stretch.UniformToFill
                }
            };
            ToolTip.SetTip(badge, "WiiM website");
            badge.PointerEntered += (_, _) => badge.Opacity = 1;
            badge.PointerExited += (_, _) => badge.Opacity = 0.85;
            badge.Tapped += (_, _) => OpenUrl("https://www.wiimhome.com/");
            return badge;
        }

        private Control BuildGeneralPage()
        {
            _chkSuppress.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetSuppress(_chkSuppress.IsChecked == true); };
            _chkVolumeKeys.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                _owner.SetVolumeKeys(_chkVolumeKeys.IsChecked == true);
                ApplyVolumeKeysState(_chkVolumeKeys.IsChecked == true);
            };
            var volumeKeysRow = ToggleRow("Volume keys control the WiiM device", _chkVolumeKeys);
            ToolTip.SetTip(volumeKeysRow, "When this is off, the keyboard's volume and mute keys change this computer's volume as usual. " +
                                          "Hotkeys you set on the Hotkeys page keep working.");
            _chkAutoStart.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetAutoStartSetting(_chkAutoStart.IsChecked == true); };
            _chkStartMinimized.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetStartMinimized(_chkStartMinimized.IsChecked == true); };
            ToolTip.SetTip(_chkStartMinimized, OperatingSystem.IsMacOS()
                ? "Start in the menu bar without opening this window. Starting Wiim Control again while it runs opens the window."
                : "Start in the tray without opening this window. Starting Wiim Control again while it runs opens the window.");
            string startMinimizedLabel = OperatingSystem.IsMacOS() ? "Start minimized (in the menu bar)" : "Start minimized (in the tray)";
            _chkMediaControls.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetMediaControls(_chkMediaControls.IsChecked == true); };
            ToolTip.SetTip(_chkMediaControls, OperatingSystem.IsWindows()
                ? "Shows the track in the media pop-up next to the Windows volume, with play/pause, next and previous for the device."
                : OperatingSystem.IsMacOS()
                    ? "Shows the track in Now Playing in Control Center and the menu bar, with play/pause, next, previous and the position bar for the device."
                    : "Shows the track in the desktop's media controls (MPRIS), with play/pause, next, previous and the position bar for the device.");
            var mediaControlsRow = ToggleRow($"Show the device in the {MediaControlsName}", _chkMediaControls);
            _chkForwardMedia.IsCheckedChanged += async (_, _) =>
            {
                if (_loading) return;
                _owner.SetForwardMediaKeys(_chkForwardMedia.IsChecked == true);
                if (!OperatingSystem.IsWindows() && _chkPortal.IsChecked == true)
                    await _owner.SetLinuxPortalShortcutsAsync(true);
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
                btnClear.Click += (_, _) => { _owner.ClearOutputDevices(); LoadFromOwner(); };
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
                    Muted("macOS doesn't let apps take over the volume keys. Set your own shortcuts on the Hotkeys page, " +
                          "or run these commands from the Shortcuts app or Automator:"),
                    CommandList()));
            }

            var scale = new ComboBox { Width = FieldWidth };
            foreach (int percent in new[] { 80, 90, 100, 110, 125, 150, 175, 200 }) scale.Items.Add(ComboItem(percent, $"{percent}%"));
            var current = scale.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == _owner._uiScalePercent);
            if (current == null)
            {
                current = ComboItem(_owner._uiScalePercent, $"{_owner._uiScalePercent}%");
                scale.Items.Add(current);
            }
            scale.SelectedItem = current;
            scale.SelectionChanged += (_, _) =>
            {
                if ((scale.SelectedItem as ComboBoxItem)?.Tag is not int percent || percent == _owner._uiScalePercent) return;
                _owner.SetUiScale(percent);
                ApplyScale(percent);
            };
            var artworkBackground = new ToggleSwitch { IsChecked = _owner.ArtworkBackground };
            artworkBackground.IsCheckedChanged += (_, _) => _owner.ArtworkBackground = artworkBackground.IsChecked == true;
            cards.Children.Add(Card("Appearance", LabeledRow("Scale", scale), Muted("Makes this window bigger or smaller."),
                ToggleRow("Use the artwork of what's playing as the background", artworkBackground),
                Muted("Shows a blurred copy of the active device's cover behind this window.")));
            var miniAuto = new ToggleSwitch { IsChecked = _owner.MiniPlayerAuto };
            miniAuto.IsCheckedChanged += (_, _) => _owner.MiniPlayerAuto = miniAuto.IsChecked == true;
            var miniReset = Pill("Reset positions");
            ToolTip.SetTip(miniReset, "Move the mini players back to the bottom-right corner of the screen, side by side at their normal size");
            miniReset.Click += (_, _) => _owner.ResetMiniPlayerPlacement();
            cards.Children.Add(Card("Mini player",
                ToggleRow("Open the mini player when the active device switches to Wi-Fi", miniAuto),
                LabeledRow("Window positions and sizes", miniReset),
                Muted("Open a mini player for any device with the mini player button on its card on the Devices page. You can have one open " +
                      "for each device.")));
            var trackToasts = new ToggleSwitch { IsChecked = _owner.TrackToasts };
            trackToasts.IsCheckedChanged += (_, _) => _owner.TrackToasts = trackToasts.IsChecked == true;
            cards.Children.Add(Card("Song notifications",
                ToggleRow("Show a notification when the song changes", trackToasts),
                Muted("Shows the cover, title and artist of what the active device plays for a few seconds, in the same corner and size as " +
                      "the volume overlay (set on the Volume keys page). Click it to open the mini player.")));
            var checkUpdates = new ToggleSwitch { IsChecked = _owner._checkUpdates };
            checkUpdates.IsCheckedChanged += (_, _) => _owner.SetCheckUpdates(checkUpdates.IsChecked == true);
            var checkNow = Pill("Check now");
            checkNow.Click += async (_, _) =>
            {
                checkNow.IsEnabled = false;
                try { await _owner.CheckForUpdateAsync(manual: true); }
                finally { checkNow.IsEnabled = true; }
            };
            var firmwareChecks = new ToggleSwitch { IsChecked = _owner.FirmwareChecks };
            firmwareChecks.IsCheckedChanged += (_, _) => _owner.FirmwareChecks = firmwareChecks.IsChecked == true;
            var firmwareRow = ToggleRow("Check WiiM devices for firmware updates", firmwareChecks);
            var instantUpdates = new ToggleSwitch { IsChecked = _owner.InstantUpdates };
            instantUpdates.IsCheckedChanged += (_, _) => _owner.InstantUpdates = instantUpdates.IsChecked == true;
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

            if (!OperatingSystem.IsWindows()) return Page("General", null, [], cards);
            var refresh = Pill("Refresh");
            ToolTip.SetTip(refresh, "Reads the list of output devices again, for example after plugging in headphones or a USB sound card");
            refresh.Click += (_, _) => RefreshOutputList();
            var page = Page("General", null, [refresh], cards);
            page.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && page.IsVisible) RefreshOutputList(); };
            return page;
        }

        private void RefreshOutputList()
        {
            if (!OperatingSystem.IsWindows()) return;
            var toggles = new List<Control>();
            foreach (var (id, name) in ActiveOutputDevices())
            {
                var toggle = new ToggleSwitch { IsChecked = _owner.IsOutputEnabled(id) };
                string deviceId = id;
                toggle.IsCheckedChanged += (_, _) => { if (!_loading) _owner.SetOutputDeviceEnabled(deviceId, toggle.IsChecked == true); };
                toggles.Add(ToggleRow(name, toggle));
            }
            if (toggles.Count == 0) toggles.Add(Muted("No playback devices found"));
            SetRows(_outputList, toggles);
        }

        private Border BuildLinuxShortcutsCard()
        {
            _chkPortal.IsCheckedChanged += async (_, _) =>
            {
                if (_loading) return;
                _chkPortal.IsEnabled = false;
                bool wanted = _chkPortal.IsChecked == true;
                bool ok = await _owner.SetLinuxPortalShortcutsAsync(wanted);
                if (wanted && !ok)
                {
                    _loading = true;
                    _chkPortal.IsChecked = false;
                    _loading = false;
                }
                _chkPortal.IsEnabled = true;
                UpdatePortalStatus();
            };
            if (_owner._linuxShortcuts != null) _owner._linuxShortcuts.StatusChanged += UpdatePortalStatus;

            return Card("Keyboard shortcuts",
                ToggleRow("Use the desktop's global shortcuts for the volume keys", _chkPortal),
                _lblPortalStatus,
                ToggleRow("Include the media keys (play/pause, next, previous)", _chkForwardMedia),
                Muted("You can also set your own shortcuts on the Hotkeys page. If your desktop doesn't support global shortcuts, " +
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

        private Control BuildHotkeysPage()
        {
            AddHandler(KeyDownEvent, OnRecordKeyDown, RoutingStrategies.Tunnel);
            _owner.HotkeysChanged += RebuildHotkeys;
            var intro = Card(null,
                Muted("Set your own keyboard shortcuts for Wiim Control. They work in every program, also when this window is closed."),
                Muted($"Click a shortcut and press the keys you want. Combine a letter, number, arrow or other key with at least one of " +
                      $"Ctrl, Alt, Shift or {Hotkey.MetaName}, or use an F-key on its own. Press Esc to cancel."),
                _lblHotkeyNote);
            return Page("Hotkeys", null, [], new StackPanel { Children = { intro, Card("Shortcuts", _hotkeyList) } });
        }

        private void RebuildHotkeys()
        {
            string? note = _owner.HotkeyNote;
            _lblHotkeyNote.Text = note ?? string.Empty;
            _lblHotkeyNote.IsVisible = note != null;

            var rows = new List<Control>();
            foreach (var (action, label, _) in HotkeyActions)
            {
                var a = action;
                bool hasKey = _owner._hotkeys.TryGetValue(action, out var hotkey);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = label } } };
                if (hasKey && _owner._failedHotkeys.Contains(action))
                    text.Children.Add(new TextBlock
                    {
                        Text = "Couldn't be set. Another program may already use this shortcut.",
                        Classes = { "small" }, Foreground = new SolidColorBrush(Color.Parse("#FF8A80")), TextWrapping = TextWrapping.Wrap
                    });

                bool recordingThis = _recording == action;
                var shortcut = Pill(recordingThis ? "Press keys…" : hasKey ? hotkey.Display : "Not set", accent: recordingThis);
                shortcut.MinWidth = 180;
                shortcut.HorizontalContentAlignment = HorizontalAlignment.Center;
                shortcut.Margin = new Thickness(12, 0, 8, 0);
                shortcut.Click += async (_, _) => await StartRecordingAsync(a);
                var clear = Pill("Clear");
                clear.IsEnabled = hasKey && _recording == null;
                clear.Click += async (_, _) => await _owner.SetHotkeyAsync(a, null);

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4) };
                row.Children.Add(text);
                Grid.SetColumn(shortcut, 1);
                row.Children.Add(shortcut);
                Grid.SetColumn(clear, 2);
                row.Children.Add(clear);
                rows.Add(row);
            }
            SetRows(_hotkeyList, rows);
        }

        private async Task StartRecordingAsync(HotkeyAction action)
        {
            await Task.Yield();
            _recording = action;
            await _owner.SuspendHotkeysAsync();
            RebuildHotkeys();
        }

        private async void OnRecordKeyDown(object? sender, KeyEventArgs e)
        {
            if (_recording is not { } action) return;
            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                _recording = null;
                await _owner.ApplyHotkeysAsync();
                RebuildHotkeys();
                return;
            }
            if (KeyMap.IsModifierKey(e.Key)) return;

            var hotkey = new Hotkey(e.Key, e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta));
            var duplicate = _owner._hotkeys.Where(p => p.Key != action && p.Value.Equals(hotkey)).Select(p => (HotkeyAction?)p.Key).FirstOrDefault();
            string? problem =
                !KeyMap.IsSupported(e.Key) ? "That key can't be used for a hotkey. Try a letter, number, arrow or F-key." :
                !hotkey.HasModifier && !hotkey.IsFunctionKey ? $"Please add at least one of Ctrl, Alt, Shift or {Hotkey.MetaName}, or use an F-key." :
                OperatingSystem.IsLinux() && e.Key is >= Key.F1 and <= Key.F12 &&
                    hotkey.Modifiers.HasFlag(KeyModifiers.Control) && hotkey.Modifiers.HasFlag(KeyModifiers.Alt)
                    ? "Ctrl + Alt + F1 to F12 are reserved by Linux for switching to text consoles. Please choose another combination." :
                duplicate is { } other ? $"{hotkey.Display} is already used for \"{HotkeyActions.First(h => h.Action == other).Label}\"." :
                null;
            if (problem != null)
            {
                await Dialogs.MessageAsync(this, problem, _scale);
                return;
            }

            _recording = null;
            await _owner.SetHotkeyAsync(action, hotkey);
            RebuildHotkeys();
        }

        private void UpdatePortalStatus() => Dispatcher.UIThread.Post(() =>
            _lblPortalStatus.Text = _owner._linuxShortcuts?.Status ?? "Not available");

        internal void OnActiveDeviceChanged() => ReloadSoon(refreshEq: true);

        private void RefreshVisibleDevicePages()
        {
            foreach (var (page, refresh) in _devicePages)
                if (page.IsVisible) _ = refresh();
        }

        private void LoadFromOwner()
        {
            _loading = true;
            _followActive = _comboActive != _owner._deviceIp;
            bool activeChanged = _followActive && _comboActive != null;
            _comboActive = _owner._deviceIp;

            var current = _owner._knownDevices.FirstOrDefault(d => d.Ip == _owner._deviceIp);
            _lblDeviceName.Text = current.Name ?? "No device";
            _lblDeviceIp.Text = _owner._deviceIp +
                (_owner._outputMonitor is { IsEnabledOutputActive: false } ? "  ·  inactive output" : "");
            RebuildDeviceCards();

            _chkSuppress.IsChecked = _owner._suppress;
            _chkVolumeKeys.IsChecked = _owner._volumeKeys;
            ApplyVolumeKeysState(_owner._volumeKeys);
            _chkAutoStart.IsChecked = IsAutoStartEnabled();
            _chkStartMinimized.IsChecked = _owner._startMinimized;
            _chkForwardMedia.IsChecked = _owner._forwardMediaKeys;
            _chkMediaControls.IsChecked = _owner._mediaControls;
            _chkPortal.IsChecked = _owner._linuxPortalShortcuts;
            _numStep.Value = Math.Clamp(_owner._volumeStep, 1, 50);
            _chkLogStep.IsChecked = _owner._logStep;
            _chkGroupVolume.IsChecked = _owner._groupVolumeKeys;
            UpdatePortalStatus();

            FillDeviceSettingsCombo(followActive: _followActive);
            UpdateDisplayNav();
            LoadPresetAmps();
            FillAmpCombo(_cboLibraryAmp);
            FillAmpCombo(_cboRadioAmp);
            FillAmpCombo(_cboRecentAmp);
            FillAmpCombo(_cboQueueAmp);
            FillAmpCombo(_cboVolumeAmp);
            FillSubwooferCombo(followActive: _followActive);
            FillAmpCombo(_cboOutputAmp);
            FillAmpCombo(_cboInputsAmp);
            FillAmpCombo(_cboDelayAmp);
            FillEqAmpCombo();
            _cboCorner.SelectedItem = _cboCorner.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is OsdCorner c && c == _owner._osdCorner);
            _cboDuration.SelectedItem = _cboDuration.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int ms && ms == _owner._osdDurationMs);
            if (_cboDuration.SelectedIndex < 0 && _cboDuration.Items.Count > 0) _cboDuration.SelectedIndex = 2;
            var osdScale = _cboOsdScale.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == _owner._osdScalePercent);
            if (osdScale == null)
            {
                osdScale = ComboItem(_owner._osdScalePercent, $"{_owner._osdScalePercent}%");
                _cboOsdScale.Items.Add(osdScale);
            }
            _cboOsdScale.SelectedItem = osdScale;

            RefreshOutputList();
            RebuildHotkeys();

            _loading = false;
            _followActive = false;
            if (activeChanged) Dispatcher.UIThread.Post(RefreshVisibleDevicePages);
        }
    }
}
