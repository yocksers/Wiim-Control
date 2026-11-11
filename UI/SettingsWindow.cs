using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow : Window
    {
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
        private Button? _displayNav;
        private event Action? Reloaded;
        private readonly PresetsPage _presets;
        private readonly AmpPage _amp;
        private readonly EqPage _eq;
        private readonly LibraryPage _library;

        private readonly TextBlock _lblDeviceCount = Subtitle();
        private readonly StackPanel _deviceList = new();

        private const double FieldWidth = 260;
        private const double DevicePickerWidth = 240;
        private const double WideFieldWidth = 360;
        private const double FieldGap = 8;
        private bool _loading;

        private string? _comboActive;
        private bool _followActive;
        private readonly List<(Control Page, Func<Task> Refresh)> _devicePages = [];
        private readonly Dictionary<string, TextBlock> _inputLabels = new();
        private bool _closeFlushing;
        private bool _closeFlushed;

        private readonly TextBlock _lblGroupStatus = Subtitle();
        private readonly StackPanel _groupList = new();
        private MultiroomSnapshot? _multiroom;
        private List<SavedGroup> _savedGroups = [];
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

            _presets = new PresetsPage(this);
            _amp = new AmpPage(this);
            _eq = new EqPage(this);
            _library = new LibraryPage(this);
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
                await Task.WhenAll(RefreshMultiroomAsync(), RefreshDeviceStatusesAsync(), _amp.RefreshAmpAsync(), _presets.RefreshPresetsAsync());
            };
            Closing += async (_, e) =>
            {
                if (_closeFlushed) return;
                if (_npIp != null && e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
                {
                    e.Cancel = true;
                    HideNowPlayingView();
                    return;
                }
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
                _volumeSendTimer.Stop();
                _volumePollTimer.Stop();
                _positionTimer.Stop();
            };
        }

        private void ApplyScale(int percent)
        {
            double scale = percent / 100.0, ratio = scale / _scale;
            _scale = scale;
            _scaler.Scale = scale;
            foreach (var popup in new Flyout?[] { _npBrightnessFlyout, _npLyricsDelayFlyout, _barVolumeFlyout })
                if (popup?.Content is ScaledContent scaled) scaled.Scale = scale;
            UpdateMinimumWidth();
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

        private static ComboBox ChoiceCombo(IEnumerable<ComboBoxItem> items)
        {
            var combo = new ComboBox { Width = FieldWidth };
            foreach (var item in items) combo.Items.Add(item);
            return combo;
        }

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

        private Control Page(string title, TextBlock? subtitle, Control[] actions, Control body, bool scroll = true, bool subtitleBelow = false,
            Control[]? secondRow = null)
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
                if (secondRow == null) Grid.SetColumnSpan(subtitle, 2);
                else subtitle.VerticalAlignment = VerticalAlignment.Center;
                header.Children.Add(subtitle);
            }
            if (secondRow != null)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0)
                };
                foreach (var control in secondRow) row.Children.Add(control);
                Grid.SetRow(row, 1);
                Grid.SetColumn(row, 1);
                header.Children.Add(row);
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
            var multiroomPage = new LazyPage(BuildMultiroomPage);
            Reloaded += _presets.LoadPresetAmps;
            var presetsPage = new LazyPage(_presets.Build);
            Reloaded += _library.FillDevices;
            var libraryPage = new LazyPage(_library.Build);
            var radio = new RadioPage(this);
            Reloaded += radio.FillDevices;
            var radioPage = new LazyPage(radio.Build);
            var recent = new RecentPage(this);
            Reloaded += recent.FillDevices;
            var recentPage = new LazyPage(recent.Build);
            var queue = new QueuePage(this);
            Reloaded += queue.FillDevices;
            var queuePage = new LazyPage(queue.Build);
            _queuePage = queuePage;
            Reloaded += _eq.FillEqAmpCombo;
            var eqPage = new LazyPage(_eq.Build, () => _ = _eq.RefreshEqAsync());
            _eqPageRef = eqPage;
            Reloaded += _eq.FillRoomFitAmpCombo;
            var roomFitPage = new LazyPage(_eq.BuildRoomFit, () => _ = _eq.RefreshRoomFitAsync());
            var mediaKeys = new MediaKeysPage(this);
            Reloaded += mediaKeys.Load;
            var mediaKeysPage = new LazyPage(mediaKeys.Build);
            var themesPage = new LazyPage(new ThemesPage(this).Build);
            var output = new OutputPage(this);
            Reloaded += output.FillDevices;
            var outputPage = new LazyPage(output.Build);
            _outputPageRef = outputPage;
            _outputView = output;
            var inputs = new InputsPage(this);
            Reloaded += inputs.FillDevices;
            var inputsPage = new LazyPage(inputs.Build);
            var subwoofer = new SubwooferPage(this);
            Reloaded += subwoofer.FillDevices;
            var subwooferPage = new LazyPage(subwoofer.Build);
            var audioSettings = new AudioSettingsPage(this);
            Reloaded += audioSettings.FillDevices;
            var audioSettingsPage = new LazyPage(audioSettings.Build);
            Reloaded += _amp.FillDevices;
            var ampPage = new LazyPage(_amp.Build);
            _ampPageRef = ampPage;
            var sleepPage = new LazyPage(new SleepPage(this).Build);
            var generalSettings = new GeneralPage(this);
            Reloaded += generalSettings.Load;
            var generalPage = new LazyPage(generalSettings.Build);
            _devicePages.AddRange([
                (presetsPage, _presets.RefreshPresetsAsync), (audioSettingsPage, audioSettings.RefreshAudioSettingsAsync), (outputPage, () => output.RefreshOutputAsync()),
                (inputsPage, () => inputs.RefreshInputsAsync()), (subwooferPage, () => subwoofer.RefreshSubwooferAsync()),
                (roomFitPage, _eq.RefreshRoomFitAsync)
            ]);

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 0), RenderTransform = new TranslateTransform(0, 6) };
            brand.Children.Add(new TextBlock { Text = "Wiim Control", Classes = { "small" } });
            brand.Children.Add(new TextBlock { Text = $"v{AppVersion}", Classes = { "small", "muted" } });

            var hideMenu = MenuButton(Icons.HideMenu, "Hide the menu", hidden: true);
            hideMenu.Margin = new Thickness(0, 0, 14, -12);
            DockPanel.SetDock(hideMenu, Dock.Right);
            var header = new StackPanel { Children = { new DockPanel { Children = { hideMenu, brand } }, _lblDeviceName, _lblDeviceIp } };
            DockPanel.SetDock(header, Dock.Top);
            var sidebarItems = new StackPanel();
            sidebarItems.Children.Add(NavSection("Devices", NavGroup(Nav("Devices", Icons.Speaker, devicesPage), Nav("Multiroom", Icons.Link, multiroomPage),
                Nav("Presets", Icons.Star, presetsPage))));
            sidebarItems.Children.Add(NavSection("Music", NavGroup(
                Nav("Library", Icons.Library, libraryPage), Nav("Radio", Icons.Radio, radioPage), Nav("Queue", Icons.Queue, queuePage),
                Nav("Recent", Icons.History, recentPage))));
            sidebarItems.Children.Add(NavSection("Audio", NavGroup(
                Nav("Equalizer", Icons.Equalizer, eqPage), Nav("RoomFit", Icons.Room, roomFitPage), Nav("Output", Icons.Output, outputPage), Nav("Inputs", Icons.Input, inputsPage),
                Nav(SubwooferPage.SectionHighPass, Icons.HighPass, subwooferPage, SubwooferPage.SectionHighPass), Nav("Audio settings", Icons.AudioSettings, audioSettingsPage),
                Nav(SubwooferPage.SectionSubOut, Icons.Speaker, subwooferPage, SubwooferPage.SectionSubOut))));
            var deviceInfoNav = Nav(AmpPage.SectionDeviceInfo, Icons.Info, ampPage, AmpPage.SectionDeviceInfo);
            deviceInfoNav.Click += (_, _) => _amp.CountDeviceInfoClick(ampPage);
            var general = NavGroup(
                deviceInfoNav, Nav(AmpPage.SectionNetwork, Icons.Wifi, ampPage, AmpPage.SectionNetwork),
                Nav(AmpPage.SectionStandby, Icons.Power, ampPage, AmpPage.SectionStandby));
            var automation = NavGroup(
                Nav(AmpPage.SectionAlarms, Icons.Alarm, ampPage, AmpPage.SectionAlarms), Nav("Sleep timer", Icons.Sleep, sleepPage));
            _displayNav = Nav(AmpPage.SectionDisplay, Icons.Display, ampPage, AmpPage.SectionDisplay);
            sidebarItems.Children.Add(NavSection("Hardware", NavGroup(Nav(AmpPage.SectionHardware, Icons.Wrench, ampPage, AmpPage.SectionHardware), _displayNav)));
            sidebarItems.Children.Add(NavSection("Automation", automation));
            sidebarItems.Children.Add(NavSection("General", general));
            sidebarItems.Children.Add(NavSection("Wiim Control", NavGroup(
                Nav("Media keys", Icons.Keyboard, mediaKeysPage), Nav("General", Icons.Settings, generalPage),
                Nav("Themes", Icons.Palette, themesPage))));

            var coffee = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 12, 0, 0),
                Children = { CoffeeButton(), WiimButton() }
            };
            DockPanel.SetDock(coffee, Dock.Bottom);
            var sidebar = new Border
            {
                Background = Palette.Sidebar, Width = 250, Padding = new Thickness(12, 16),
                Child = new DockPanel { Children = { header, coffee, MenuScroller(sidebarItems) } }
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

        private const double MenuScrollBarRoom = 9;

        private static ScrollViewer MenuScroller(Control items)
        {
            items.Margin = new Thickness(0, 0, MenuScrollBarRoom, 0);
            return new ScrollViewer { Content = items, AllowAutoHide = false, Classes = { "menu" } };
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
            UpdateMinimumWidth();
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

        private StackPanel NavSection(string name, Border group)
        {
            var chevron = new PathIcon { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(chevron, Dock.Right);
            var header = new Button
            {
                Classes = { "section" }, Margin = new Thickness(4, 22, 16, 6),
                Content = new DockPanel
                {
                    Children = { chevron, new TextBlock { Text = name, Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center } }
                }
            };
            void Apply(bool collapsed)
            {
                group.IsVisible = !collapsed;
                chevron.Data = collapsed ? Icons.ChevronRight : Icons.ChevronDown;
            }
            Apply(_owner.IsSectionCollapsed(name));
            header.Click += (_, _) =>
            {
                bool collapse = group.IsVisible;
                _owner.SetSectionCollapsed(name, collapse);
                Apply(collapse);
            };
            return new StackPanel { Children = { header, group } };
        }

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
            var shown = page is LazyPage lazy ? lazy.Page : page;
            if (section != null && _sectionHandlers.TryGetValue(shown, out var handler))
            {
                if (_pageTitles.TryGetValue(shown, out var title)) title.Text = section;
                handler(section);
            }
            foreach (var p in _pages.Select(p => p.Page).Distinct())
            {
                p.IsVisible = p == page;
                if (p is LazyPage { Child: { } child }) child.IsVisible = p == page;
            }
        }

        private void ReloadSoon(bool refreshEq) => Dispatcher.UIThread.Post(() =>
        {
            LoadFromOwner();
            if (refreshEq) _ = _eq.RefreshEqAsync();
            _ = RefreshMultiroomAsync();
            _ = _amp.RefreshAmpAsync();
        });

        private void UpdateDisplayNav()
        {
            if (_displayNav == null) return;
            bool any = _owner._knownDevices.Any(d => _owner.HasScreen(d.Ip));
            _displayNav.IsVisible = any;
            if (!any && _amp.Section == AmpPage.SectionDisplay && _pages.Any(p => p.Item == _displayNav && p.Page.IsVisible))
                ShowPage(_pages.First(p => p.Item == _displayNav).Page, AmpPage.SectionDeviceInfo);
        }

        private sealed class LazyPage(Func<Control> build, Action? built = null) : Decorator
        {
            public Control Page
            {
                get
                {
                    if (Child != null) return Child;
                    var page = build();
                    page.IsVisible = false;
                    Child = page;
                    built?.Invoke();
                    return page;
                }
            }
        }

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
            _lblDeviceIp.Text = _owner._deviceIp;
            RebuildDeviceCards();

            UpdateDisplayNav();
            Reloaded?.Invoke();

            _loading = false;
            _followActive = false;
            if (activeChanged) Dispatcher.UIThread.Post(RefreshVisibleDevicePages);
        }
    }
}
