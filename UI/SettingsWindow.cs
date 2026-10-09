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
        private const int EqBandCount = 10;
        private static readonly string[] EqBandLabels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
        private static readonly string[] EqBandParams =
            ["band31hz", "band63hz", "band125hz", "band250hz", "band500hz", "band1khz", "band2khz", "band4khz", "band8khz", "band16khz"];

        private readonly WiimController _owner;
        private double _scale;
        private readonly LayoutTransformControl _scaler;
        private Size _normalSize;

        private readonly TextBlock _lblDeviceName = new() { Classes = { "device" }, Margin = new Thickness(4, 18, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _lblDeviceIp = new() { Classes = { "muted" }, Margin = new Thickness(4, 2, 4, 0) };
        private readonly List<(Button Item, Border Bar, Control Page)> _pages = [];
        private readonly Panel _content = new();
        private Control? _devicesPage;

        private readonly TextBlock _lblDeviceCount = Subtitle();
        private readonly StackPanel _deviceList = new();
        private readonly ToggleSwitch _chkSuppress = new();
        private readonly ToggleSwitch _chkAutoStart = new();
        private readonly ToggleSwitch _chkForwardMedia = new();
        private readonly ToggleSwitch _chkLogStep = new();
        private readonly ToggleSwitch _chkGroupVolume = new();
        private readonly ToggleSwitch _chkPortal = new();
        private readonly TextBlock _lblPortalStatus = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        private readonly Stepper _numStep = new() { Minimum = 1, Maximum = 50, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly ComboBox _cboCorner = new() { Width = 190 };
        private readonly ComboBox _cboDuration = new() { Width = 190 };
        private readonly StackPanel _outputList = new();
        private bool _loading;

        private readonly ToggleSwitch _chkEqEnabled = new();
        private readonly ComboBox _cboEqPreset = new() { Width = 240, PlaceholderText = "Custom (not saved)" };
        private readonly TextBlock _lblEqSource = Subtitle();
        private readonly ComboBox _cboEqInput = new() { Width = 240 };
        private readonly Dictionary<string, TextBlock> _inputLabels = new();
        private Control? _eqInputRow;
        private string? _eqInputChoice;
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
        private bool _eqSending;
        private readonly HashSet<string> _eqCustomPresets = new();
        private readonly HashSet<string> _eqBuiltInPresets = new(StringComparer.OrdinalIgnoreCase);
        private string _eqSource = string.Empty;
        private string _eqPluginUri = DefaultEqPlugin;
        private readonly Button _btnEqSave = Pill("Save as preset…");
        private readonly Button _btnEqRename = Pill("Rename…");
        private readonly Button _btnEqDelete = Pill("Delete");

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
        private readonly DispatcherTimer _volumePollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private bool _volumeUpdating;
        private bool _volumePolling;

        private readonly Dictionary<string, DeviceStatus> _statuses = new();
        private readonly Dictionary<string, NowPlayingControls> _nowPlayingControls = new();
        private readonly ComboBox _cboAmp = new() { Width = 240 };
        private readonly StackPanel _ampList = new();
        private readonly TextBlock _lblAmpStatus = Subtitle();
        private bool _ampBusy;

        private readonly StackPanel _hotkeyList = new();
        private readonly TextBlock _lblHotkeyNote = new()
        {
            Classes = { "accent" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), IsVisible = false
        };
        private HotkeyAction? _recording;

        private sealed record VolumeControls(LevelSlider Slider, TextBlock Value, Button Mute, PathIcon MuteIcon);

        private sealed record NowPlayingControls(ArtworkView Art, TextBlock Title, TextBlock Artist, TextBlock Detail,
            Button PlayPause, PathIcon PlayPauseIcon, Button Previous, Button Next);

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

            _scaler = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(_scale, _scale),
                Child = BuildLayout()
            };
            Content = _scaler;
            LoadFromOwner();

            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            _eqSendTimer.Tick += async (_, _) => await FlushEqBandsAsync();
            _volumeSendTimer.Tick += (_, _) => FlushVolumes(force: false);
            _volumePollTimer.Tick += async (_, _) =>
            {
                if (_devicesPage is { IsVisible: true } && WindowState != WindowState.Minimized) await RefreshDeviceStatusesAsync();
            };
            SizeChanged += (_, _) => { if (WindowState == WindowState.Normal) _normalSize = new Size(Width, Height); };
            Opened += async (_, _) =>
            {
                FitToScreen();
                if (owner._windowMaximized) WindowState = WindowState.Maximized;
                _volumePollTimer.Start();
                await Task.WhenAll(RefreshEqAsync(), RefreshMultiroomAsync(), RefreshDeviceStatusesAsync(), RefreshAmpAsync());
            };
            Closing += (_, _) =>
            {
                _ = FlushEqBandsAsync();
                _ = FlushPeqAsync();
                FlushVolumes(force: true);
                _owner.SaveWindowState(_normalSize, WindowState == WindowState.Maximized);
            };
            Closed += (_, _) =>
            {
                _eqSendTimer.Stop();
                _peqSendTimer.Stop();
                _volumeSendTimer.Stop();
                _volumePollTimer.Stop();
                if (_owner._linuxShortcuts != null) _owner._linuxShortcuts.StatusChanged -= UpdatePortalStatus;
                _owner.HotkeysChanged -= RebuildHotkeys;
                if (_recording != null) _ = _owner.ApplyHotkeysAsync();
            };
        }

        private void ApplyScale(int percent)
        {
            double scale = percent / 100.0, ratio = scale / _scale;
            _scale = scale;
            _scaler.LayoutTransform = new ScaleTransform(scale, scale);
            MinWidth = 960 * scale;
            MinHeight = 640 * scale;
            if (WindowState != WindowState.Normal) return;
            Width = Math.Max(Width * ratio, MinWidth);
            Height = Math.Max(Height * ratio, MinHeight);
            FitToScreen();
        }

        private void FitToScreen()
        {
            var screen = Screens.ScreenFromVisual(this) ?? Screens.Primary;
            if (screen == null) return;
            double scaling = screen.Scaling;
            double maxWidth = screen.WorkingArea.Width / scaling, maxHeight = screen.WorkingArea.Height / scaling;
            MinWidth = Math.Min(MinWidth, maxWidth);
            MinHeight = Math.Min(MinHeight, maxHeight);
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);
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

        private static (Button Button, PathIcon Icon) IconButton(StreamGeometry geometry)
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

        private Control Page(string title, TextBlock? subtitle, Control[] actions, Control body, bool scroll = true)
        {
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = title, Classes = { "title" } });
            if (subtitle != null) titleRow.Children.Add(subtitle);

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(titleRow);
            if (actions.Length > 0)
            {
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                foreach (var a in actions) buttons.Children.Add(a);
                Grid.SetColumn(buttons, 1);
                header.Children.Add(buttons);
            }

            var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(30, 20, 30, 16) };
            page.Children.Add(header);
            Control content = scroll ? new ScrollViewer { Content = body, Padding = new Thickness(0, 0, 12, 0) } : body;
            Grid.SetRow(content, 1);
            page.Children.Add(content);
            return page;
        }

        private Control BuildLayout()
        {
            var devicesPage = BuildDevicesPage();
            _devicesPage = devicesPage;
            var multiroomPage = BuildMultiroomPage();
            var eqPage = BuildEqPage();
            var volumePage = BuildVolumePage();
            var ampPage = BuildAmpPage();
            var hotkeysPage = BuildHotkeysPage();
            var generalPage = BuildGeneralPage();

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 0) };
            brand.Children.Add(new TextBlock { Text = "Wiim Control", Classes = { "small" } });
            brand.Children.Add(new TextBlock { Text = $"v{AppVersion}", Classes = { "small", "muted" } });

            var sidebarItems = new StackPanel();
            sidebarItems.Children.Add(brand);
            sidebarItems.Children.Add(_lblDeviceName);
            sidebarItems.Children.Add(_lblDeviceIp);
            sidebarItems.Children.Add(SectionLabel("Devices"));
            sidebarItems.Children.Add(Nav("Devices", Icons.Speaker, devicesPage));
            sidebarItems.Children.Add(Nav("Multiroom", Icons.Link, multiroomPage));
            sidebarItems.Children.Add(SectionLabel("Sound"));
            sidebarItems.Children.Add(Nav("Equalizer", Icons.Equalizer, eqPage));
            sidebarItems.Children.Add(Nav("Volume", Icons.Volume, volumePage));
            sidebarItems.Children.Add(SectionLabel("Settings"));
            sidebarItems.Children.Add(Nav("Amp settings", Icons.Wrench, ampPage));
            sidebarItems.Children.Add(Nav("Hotkeys", Icons.Keyboard, hotkeysPage));
            sidebarItems.Children.Add(Nav("General", Icons.Settings, generalPage));

            var sidebar = new Border
            {
                Background = Palette.Sidebar, Width = 250, Padding = new Thickness(12, 16),
                Child = new ScrollViewer { Content = sidebarItems }
            };

            var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            root.Children.Add(sidebar);
            Grid.SetColumn(_content, 1);
            root.Children.Add(_content);
            ShowPage(devicesPage);
            return root;
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text, Classes = { "small", "muted" }, Margin = new Thickness(4, 22, 4, 6)
        };

        private Button Nav(string text, StreamGeometry icon, Control page)
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
            item.Click += (_, _) => ShowPage(page);
            page.IsVisible = false;
            _content.Children.Add(page);
            _pages.Add((item, bar, page));
            return item;
        }

        private void ShowPage(Control page)
        {
            foreach (var (item, bar, p) in _pages)
            {
                bool selected = p == page;
                item.Classes.Set("selected", selected);
                bar.IsVisible = selected;
                p.IsVisible = selected;
            }
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
                var ip = await Dialogs.PromptAsync(this, "Enter the Wiim Amp's IP address:", string.Empty, _scale);
                if (string.IsNullOrWhiteSpace(ip)) return;
                _owner.AddManualDevice(ip);
                LoadFromOwner();
                _ = RefreshEqAsync();
                _ = RefreshMultiroomAsync();
            };
            return Page("Devices", _lblDeviceCount, [btnDiscover, btnAdd], _deviceList);
        }

        private void RebuildDeviceCards()
        {
            _volumeControls.Clear();
            _nowPlayingControls.Clear();
            _inputLabels.Clear();
            var cards = new List<Control>();
            foreach (var dev in _owner._knownDevices)
            {
                var d = dev;
                bool active = d.Ip == _owner._deviceIp;

                var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                title.Children.Add(new TextBlock { Text = d.Name, Classes = { "device" } });
                if (active)
                    title.Children.Add(new TextBlock { Text = "●  Active", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center });
                var info = new StackPanel();
                info.Children.Add(title);
                info.Children.Add(new TextBlock { Text = d.Ip, Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 0) });
                if (GroupDescription(d.Ip) is { } group)
                    info.Children.Add(new TextBlock { Text = group.Text, Foreground = group.Brush, Margin = new Thickness(0, 4, 0, 0) });

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                actions.Children.Add(InputButton(d.Ip));
                if (active)
                {
                    var test = Pill("Test connection");
                    test.Click += async (_, _) => await ShowConnectionTestAsync();
                    actions.Children.Add(test);
                }
                else
                {
                    var use = Pill("Use this device", accent: true);
                    use.Click += (_, _) => { _owner.SwitchDevice(d); ReloadSoon(refreshEq: true); };
                    actions.Children.Add(use);
                }
                var remove = Pill("Remove");
                remove.Click += (_, _) => { _owner.RemoveDevice(d); ReloadSoon(refreshEq: active); };
                actions.Children.Add(remove);

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
                grid.Children.Add(info);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);
                var nowPlayingRow = BuildNowPlayingRow(d.Ip);
                Grid.SetRow(nowPlayingRow, 1);
                Grid.SetColumnSpan(nowPlayingRow, 2);
                grid.Children.Add(nowPlayingRow);
                var volumeRow = BuildVolumeRow(d.Ip);
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
            ToolTip.SetTip(button, "Choose the amp's input");
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
                await Dialogs.MessageAsync(this, "Couldn't get the inputs from the amp. Check that it's switched on and reachable.", _scale);
                return;
            }
            string? current = _statuses.TryGetValue(ip, out var status) ? InputOfMode(status.Mode) : null;
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var input in inputs)
            {
                var item = new MenuItem
                {
                    Header = InputLabel(input), ToggleType = MenuItemToggleType.Radio,
                    IsChecked = string.Equals(input, current, StringComparison.OrdinalIgnoreCase)
                };
                string target = input;
                item.Click += async (_, _) => await SwitchInputAsync(ip, target);
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        }

        private async Task SwitchInputAsync(string ip, string input)
        {
            if (!await _owner.SwitchInputAsync(ip, input))
            {
                await Dialogs.MessageAsync(this, "The amp didn't switch the input. Check that it's switched on and reachable.", _scale);
                return;
            }
            if (_inputLabels.TryGetValue(ip, out var label)) label.Text = InputLabel(input);
            await Task.Delay(1500);
            await RefreshDeviceStatusesAsync();
            if (ip == _owner._deviceIp && _eqInputChoice == null) await RefreshEqAsync();
        }

        private Grid BuildVolumeRow(string ip)
        {
            var (mute, muteIcon) = IconButton(Icons.Volume);
            var slider = new LevelSlider { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
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
            _volumeControls[ip] = new VolumeControls(slider, value, mute, muteIcon);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(mute);
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            return row;
        }

        private void ApplyVolume(string ip)
        {
            if (!_volumeControls.TryGetValue(ip, out var c)) return;
            if (_volumes.TryGetValue(ip, out var v))
            {
                bool busy = c.Slider.IsDragging || _pendingVolumes.ContainsKey(ip) || _volumeSending.Contains(ip);
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

        private void FlushVolumes(bool force)
        {
            foreach (var (ip, volume) in _pendingVolumes.ToList())
            {
                if (!force && _volumeSending.Contains(ip)) continue;
                _pendingVolumes.Remove(ip);
                _ = SendVolumeAsync(ip, volume);
            }
            if (_pendingVolumes.Count == 0) _volumeSendTimer.Stop();
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
                    if (_volumeTouched.TryGetValue(ip, out var touched) && touched > started.AddSeconds(-1)) continue;
                    if (status != null) _volumes[ip] = status.Volume;
                    else _volumes.Remove(ip);
                    ApplyVolume(ip);
                }
            }
            finally
            {
                _volumePolling = false;
            }
        }

        private Grid BuildNowPlayingRow(string ip)
        {
            var art = new ArtworkView { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var detail = new TextBlock { Classes = { "dim", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, artist, detail } };

            var (previous, _) = IconButton(Icons.Previous);
            var (playPause, playPauseIcon) = IconButton(Icons.Play);
            var (next, _) = IconButton(Icons.Next);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { previous, playPause, next }
            };
            _nowPlayingControls[ip] = new NowPlayingControls(art, title, artist, detail, playPause, playPauseIcon, previous, next);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(art);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            return row;
        }

        private void ApplyNowPlaying(string ip)
        {
            if (_inputLabels.TryGetValue(ip, out var inputLabel))
                inputLabel.Text = !_statuses.TryGetValue(ip, out var known) ? "Input"
                    : InputOfMode(known.Mode) is { } input ? InputLabel(input)
                    : SourceName(known.Mode) is { Length: > 0 } name ? name
                    : "Input";
            if (!_nowPlayingControls.TryGetValue(ip, out var c)) return;
            if (!_statuses.TryGetValue(ip, out var status))
            {
                c.Title.Text = _volumes.Count == 0 && _statuses.Count == 0 ? "Loading…" : "Not reachable";
                c.Artist.Text = c.Detail.Text = string.Empty;
                c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = false;
                c.Art.ImageUrl = string.Empty;
                c.Art.Bitmap = null;
                return;
            }

            string source = SourceName(status.Mode);
            c.Title.Text = status.Title.Length > 0 ? status.Title
                : status.IsPlaying ? (source.Length > 0 ? source : "Playing")
                : "Not playing";
            c.Artist.Text = status.Artist.Length > 0 && status.Album.Length > 0 ? $"{status.Artist}  ·  {status.Album}" : status.Artist;
            c.Detail.Text = string.Join("  ·  ", new[] { source, Quality(status) }.Where(t => t.Length > 0));
            c.PlayPauseIcon.Data = status.IsPlaying ? Icons.Pause : Icons.Play;
            c.PlayPause.IsEnabled = c.Previous.IsEnabled = c.Next.IsEnabled = true;
            if (status.ArtUrl != c.Art.ImageUrl)
            {
                c.Art.ImageUrl = status.ArtUrl;
                c.Art.Bitmap = null;
                if (status.ArtUrl.Length > 0) _ = LoadArtworkAsync(c.Art, status.ArtUrl);
            }
        }

        private static string Quality(DeviceStatus status)
        {
            if (status.SampleRate <= 0) return string.Empty;
            string rate = (status.SampleRate / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz";
            return status.BitDepth > 0 ? $"{rate}  ·  {status.BitDepth}-bit" : rate;
        }

        private async Task LoadArtworkAsync(ArtworkView view, string url)
        {
            var image = await _owner.GetArtworkAsync(url);
            if (view.ImageUrl == url) view.Bitmap = image;
        }

        private async Task TransportAsync(string ip, string action)
        {
            string target = ip;
            if (_multiroom != null && _multiroom.LeaderOf.TryGetValue(ip, out var leader)) target = leader;
            await _owner.SendTransportAsync(target, action);
            await Task.Delay(600);
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }

        private string? SelectedAmpIp => (_cboAmp.SelectedItem as ComboBoxItem)?.Tag as string;

        private Control BuildAmpPage()
        {
            _cboAmp.SelectionChanged += async (_, _) => { if (!_loading) await RefreshAmpAsync(); };
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshAmpAsync();
            return Page("Amp settings", _lblAmpStatus, [_cboAmp, btnRefresh], _ampList);
        }

        private async Task RefreshAmpAsync()
        {
            string? ip = SelectedAmpIp;
            if (ip == null)
            {
                _lblAmpStatus.Text = string.Empty;
                SetRows(_ampList, [Muted("Add a Wiim amp on the Devices page first.")]);
                return;
            }
            _lblAmpStatus.Text = "Loading…";
            var settings = await _owner.GetAmpSettingsAsync(ip);
            if (ip != SelectedAmpIp) return;
            _lblAmpStatus.Text = settings == null ? "Not reachable" : string.Empty;
            SetRows(_ampList, settings == null ? [Muted("Couldn't reach this amp.")] : BuildAmpCards(ip, settings));
        }

        private async Task ChangeAmpAsync(Func<Task<bool>> change)
        {
            if (_ampBusy) return;
            _ampBusy = true;
            _ampList.IsEnabled = false;
            _lblAmpStatus.Text = "Saving…";
            bool ok = await change();
            _ampBusy = false;
            _ampList.IsEnabled = true;
            if (!ok) await Dialogs.MessageAsync(this, "The amp didn't accept the change.", _scale);
            await RefreshAmpAsync();
        }

        private Grid AmpToggle(string text, bool value, Func<bool, Task<bool>> apply)
        {
            var toggle = new ToggleSwitch { IsChecked = value };
            toggle.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(() => apply(toggle.IsChecked == true));
            return ToggleRow(text, toggle);
        }

        private static LevelSlider AmpSlider(int min, int max, int value, Func<int, string> format, bool fromCenter, out Control row)
        {
            var slider = new LevelSlider { Minimum = min, Maximum = max, FillFromCenter = fromCenter, Width = 240, VerticalAlignment = VerticalAlignment.Center };
            slider.Value = value;
            var label = new TextBlock
            {
                Text = format(slider.Value), MinWidth = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) => label.Text = format(slider.Value);
            row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, label } };
            return slider;
        }

        private static string BalanceText(int value) =>
            value == 0 ? "Center" : value < 0 ? $"L {-value}" : $"R {value}";

        private List<Control> BuildAmpCards(string ip, AmpSettings s)
        {
            var cards = new List<Control>();

            var volumeRows = new List<Control>();
            if (s.MaxVolume is int maxVolume)
            {
                var slider = AmpSlider(1, 100, maxVolume, v => v.ToString(), false, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => _owner.SetMaxVolumeAsync(ip, slider.Value));
                volumeRows.Add(LabeledRow("Maximum volume", row));
            }
            if (s.Balance is double balance)
            {
                var slider = AmpSlider(-100, 100, (int)Math.Round(balance * 100), BalanceText, true, out var row);
                slider.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => _owner.SetBalanceAsync(ip, slider.Value / 100.0));
                volumeRows.Add(LabeledRow("Balance", row));
            }
            if (s.Fade is bool fade)
                volumeRows.Add(AmpToggle("Fade in and out when playback starts or stops", fade, on => _owner.SetFadeAsync(ip, on)));
            if (volumeRows.Count > 0) cards.Add(Card("Volume", volumeRows.ToArray()));

            var soundRows = new List<Control> { LabeledRow("Output", Info(OutputName(s.OutputHardware))) };
            if (s.FilterMode is string mode && s.Filters.Count > 0)
            {
                var combo = new ComboBox { Width = 360 };
                foreach (var f in s.Filters) combo.Items.Add(ComboItem(f.Index, f.Name));
                combo.SelectedIndex = s.Filters.FindIndex(f => f.Index == s.Filter);
                combo.SelectionChanged += async (_, _) =>
                {
                    if ((combo.SelectedItem as ComboBoxItem)?.Tag is int index)
                        await ChangeAmpAsync(() => _owner.SetDigitalFilterAsync(ip, mode, index));
                };
                soundRows.Add(LabeledRow("Digital filter", combo));
            }
            cards.Add(Card("Sound", soundRows.ToArray()));

            if (s.Subwoofer is { } sub)
                cards.Add(Card("Subwoofer",
                    LabeledRow("Subwoofer output", Info(sub.Enabled ? "On" : "Off")),
                    LabeledRow("Subwoofer connected", Info(sub.Plugged ? "Yes" : "No")),
                    LabeledRow("Crossover", Info($"{sub.Crossover:0} Hz")),
                    LabeledRow("Level", Info($"{sub.Level:+0.#;-0.#;0} dB")),
                    LabeledRow("Phase", Info($"{sub.Phase:0}°")),
                    LabeledRow("Main speakers high-pass", Info(sub.MainHighPass ? "On" : "Off")),
                    Muted("These can only be changed in the WiiM Home app.")));

            var lightRows = new List<Control>();
            if (s.StatusLight is bool statusLight)
                lightRows.Add(AmpToggle("Status light", statusLight, on => _owner.SetStatusLightAsync(ip, on)));
            if (s.ButtonsLocked is bool locked)
                lightRows.Add(AmpToggle("Lock the touch buttons", locked, on => _owner.SetButtonsLockedAsync(ip, on)));
            if (s.Screen is { } screen)
            {
                var screenOn = new ToggleSwitch { IsChecked = screen.On };
                var autoBrightness = new ToggleSwitch { IsChecked = screen.AutoBrightness };
                var brightness = new Stepper { Minimum = 1, Maximum = Math.Max(10, screen.Brightness), HorizontalAlignment = HorizontalAlignment.Right };
                brightness.Value = Math.Max(1, screen.Brightness);
                Task<bool> ApplyScreen() =>
                    _owner.SetScreenAsync(ip, screen, screenOn.IsChecked == true, autoBrightness.IsChecked == true, brightness.Value);
                screenOn.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
                autoBrightness.IsCheckedChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
                brightness.ValueChanged += async (_, _) => await ChangeAmpAsync(ApplyScreen);
                lightRows.Add(ToggleRow("Screen on", screenOn));
                lightRows.Add(ToggleRow("Automatic brightness", autoBrightness));
                lightRows.Add(LabeledRow("Screen brightness", brightness));
            }
            if (lightRows.Count > 0) cards.Add(Card("Lights and buttons", lightRows.ToArray()));

            if (s.Inputs is { Count: > 0 } inputs)
            {
                var inputRows = new List<Control>
                {
                    Muted("Choose which inputs are shown when you pick an input or an EQ input. This is saved on the amp, " +
                          "so the WiiM Home app shows the same inputs.")
                };
                foreach (var input in inputs)
                {
                    string inputMode = input.Mode;
                    if (inputMode == "wifi")
                    {
                        inputRows.Add(LabeledRow(InputLabel(inputMode), Info("Always shown")));
                        continue;
                    }
                    var row = AmpToggle(InputLabel(inputMode), input.Shown, async shown =>
                    {
                        bool ok = await _owner.SetInputShownAsync(ip, inputMode, shown);
                        if (ok && ip == _owner._deviceIp) _ = RefreshEqAsync();
                        return ok;
                    });
                    inputRows.Add(row);
                }
                cards.Add(Card("Inputs", inputRows.ToArray()));
            }

            return cards;
        }

        private void ReloadSoon(bool refreshEq) => Dispatcher.UIThread.Post(() =>
        {
            LoadFromOwner();
            if (refreshEq) _ = RefreshEqAsync();
            _ = RefreshMultiroomAsync();
            _ = RefreshAmpAsync();
        });

        private async Task ShowConnectionTestAsync()
        {
            try
            {
                var status = await _owner.TestConnectionAsync();
                await Dialogs.MessageAsync(this, $"Connected — HTTP {status}", _scale);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, $"Failed: {ex.Message}", _scale);
            }
        }

        private Control BuildMultiroomPage()
        {
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshMultiroomAsync();
            return Page("Multiroom", _lblGroupStatus, [btnRefresh], _groupList);
        }

        private async Task RefreshMultiroomAsync()
        {
            _lblGroupStatus.Text = "Checking…";
            _multiroom = await _owner.GetMultiroomSnapshotAsync();
            RebuildMultiroom();
            RebuildDeviceCards();
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
                cards.Add(Muted("Checking which amps are linked…"));
            }
            else if (devices.Count < 2)
            {
                _lblGroupStatus.Text = "Not grouped";
                cards.Add(Card("Only one amp", Muted("Add another Wiim amp on the Devices page to play music in sync.")));
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
            SetRows(_groupList, cards);
        }

        private Border BuildLeaderChoiceCard(List<KnownDevice> devices)
        {
            var combo = new ComboBox { Width = 320 };
            combo.Items.Add(ComboItem(string.Empty, $"Controlled amp ({DeviceName(_owner._deviceIp)})"));
            foreach (var d in devices) combo.Items.Add(ComboItem(d.Ip, d.Name));
            int index = devices.FindIndex(d => d.Ip == _owner._groupLeaderIp);
            combo.SelectedIndex = index >= 0 ? index + 1 : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is not string ip) return;
                _owner.SetGroupLeader(ip);
                Dispatcher.UIThread.Post(RebuildMultiroom);
            };
            return Card("Group leader", LabeledRow("Leader", combo), Muted("Linked amps play whatever the leader plays."));
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
                    rows.Add(new TextBlock
                    {
                        Text = linkedHere ? "Linked" : LinkStatus(snapshot, o.Ip), Classes = { "muted" }, Margin = new Thickness(0, 0, 0, 6)
                    });
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
                await Dialogs.MessageAsync(this, "The amp didn't accept the request. Check that the amps are switched on and reachable.", _scale);
            await RefreshMultiroomAsync();
        }

        private Control BuildEqPage()
        {
            var btnRefresh = Pill("Refresh");
            btnRefresh.Click += async (_, _) => await RefreshEqAsync();

            _btnEqSave.Click += async (_, _) => await SaveEqPresetAsync();
            _btnEqRename.Click += async (_, _) => await RenameEqPresetAsync();
            _btnEqDelete.Click += async (_, _) => await DeleteEqPresetAsync();
            var presetRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { _btnEqSave, _btnEqRename, _btnEqDelete, _cboEqPreset }
            };
            _cboEqType.Items.Add(ComboItem(DefaultEqPlugin, "Graphic EQ (10 bands)"));
            _cboEqType.Items.Add(ComboItem(PeqPlugin, "Parametric EQ"));
            _cboEqType.SelectedIndex = 0;
            _eqTypeRow = LabeledRow("Type", _cboEqType);
            _eqTypeRow.IsVisible = false;
            _eqInputRow = LabeledRow("Input", _cboEqInput);
            _eqInputRow.IsVisible = false;
            var topCard = Card(null, ToggleRow("Enable EQ", _chkEqEnabled), _eqInputRow, _eqTypeRow, LabeledRow("Preset", presetRow));

            var table = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", EqBandCount))),
                RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                MinHeight = 300
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

            var btnFlat = Pill("Flat");
            var hint = new TextBlock
            {
                Text = "Applies to the selected input. Double-click a slider to reset it.", Classes = { "muted" },
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
            bottom.Children.Add(btnFlat);
            Grid.SetColumn(hint, 1);
            bottom.Children.Add(hint);

            var eqGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            eqGrid.Children.Add(table);
            Grid.SetRow(bottom, 1);
            eqGrid.Children.Add(bottom);
            var eqCard = new Border { Child = eqGrid, Margin = new Thickness(0) };
            eqCard.Classes.Add("card");
            _geqEditor = eqCard;
            _peqEditor = BuildPeqEditor();

            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            body.Children.Add(topCard);
            var editors = new Panel { Children = { eqCard, _peqEditor } };
            Grid.SetRow(editors, 1);
            body.Children.Add(editors);

            _eqEditControls.Add(_cboEqPreset);
            _eqEditControls.AddRange(_eqSliders);
            _eqEditControls.Add(btnFlat);

            _chkEqEnabled.IsCheckedChanged += async (_, _) =>
            {
                if (_eqLoading) return;
                UpdateEqEnabledState();
                bool ok = _chkEqEnabled.IsChecked == true
                    ? await _owner.SetEqTypeAsync(EqTarget, _eqPluginUri)
                    : await _owner.TurnEqOffAsync(EqTarget, _eqPluginUri);
                if (!ok) await RefreshEqAsync();
            };
            _cboEqInput.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboEqInput.SelectedItem as ComboBoxItem)?.Tag is not string input || input == _eqSource) return;
                _eqInputChoice = input;
                await RefreshEqAsync();
            };
            _cboEqType.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboEqType.SelectedItem as ComboBoxItem)?.Tag is not string plugin || plugin == _eqPluginUri) return;
                await SendPendingEqBandsAsync();
                _eqPluginUri = plugin;
                ShowEqEditor();
                if (_chkEqEnabled.IsChecked == true && !await _owner.SetEqTypeAsync(EqTarget, plugin))
                    await Dialogs.MessageAsync(this, "The amp didn't switch the EQ type.", _scale);
                await RefreshEqAsync();
            };
            _cboEqPreset.SelectionChanged += async (_, _) =>
            {
                if (_eqLoading || (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag is not string name) return;
                _eqSendTimer.Stop();
                _eqDirtyBands.Clear();
                _peqSendTimer.Stop();
                _peqDirty.Clear();
                if (_eqLegacy) await _owner.LoadEqPresetAsync(name);
                else await _owner.LoadSourcePresetAsync(_eqSource, _eqPluginUri, name);
                await RefreshEqAsync();
            };
            btnFlat.Click += (_, _) => { foreach (var s in _eqSliders) s.Value = 50; };

            _chkEqEnabled.IsEnabled = false;
            UpdateEqEnabledState();
            return Page("Equalizer", _lblEqSource, [btnRefresh], body, scroll: false);
        }

        private static string FormatEqDb(int value) =>
            ((value - 50) / 4.0).ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.InvariantCulture);

        private string? SelectedEqPreset => (_cboEqPreset.SelectedItem as ComboBoxItem)?.Tag as string;

        private string EqTarget => _eqLegacy ? string.Empty : _eqSource;

        private void UpdateEqEnabledState()
        {
            bool editable = _eqAvailable && _chkEqEnabled.IsChecked == true;
            foreach (var c in _eqEditControls) c.IsEnabled = editable;
            _cboEqType.IsEnabled = _cboEqInput.IsEnabled = _eqAvailable;
            UpdatePeqRowStates();
            bool customSelected = SelectedEqPreset is { } name && _eqCustomPresets.Contains(name);
            _btnEqSave.IsEnabled = editable;
            _btnEqRename.IsEnabled = _btnEqDelete.IsEnabled = editable && customSelected;
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

        private async Task SendPendingEqBandsAsync()
        {
            while (_eqSending || _eqDirtyBands.Count > 0 || _peqSending || _peqDirty.Count > 0)
            {
                await FlushEqBandsAsync();
                await FlushPeqAsync();
                if (_eqSending || _peqSending) await Task.Delay(50);
            }
        }

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

        private async Task SaveEqPresetAsync()
        {
            string suggestion = SelectedEqPreset is { } current && _eqCustomPresets.Contains(current) ? current : string.Empty;
            string? name = await Dialogs.PromptAsync(this, "Save the current EQ on the amp as a preset named:", suggestion, _scale);
            if (string.IsNullOrEmpty(name) || !await IsValidPresetNameAsync(name)) return;
            bool replacing = _eqCustomPresets.Contains(name);
            if (replacing && !await Dialogs.ConfirmAsync(this, $"Replace the preset \"{name}\" with the current EQ?", _scale)) return;

            await SendPendingEqBandsAsync();
            if (!await _owner.SaveEqPresetAsync(name, EqTarget, _eqPluginUri, replacing))
                await Dialogs.MessageAsync(this, "The amp didn't save the preset.", _scale);
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
            if (!await _owner.RenameEqPresetAsync(oldName, name, _eqPluginUri))
                await Dialogs.MessageAsync(this, "The amp didn't rename the preset.", _scale);
            await RefreshEqAsync();
        }

        private async Task DeleteEqPresetAsync()
        {
            if (SelectedEqPreset is not { } name || !_eqCustomPresets.Contains(name)) return;
            if (!await Dialogs.ConfirmAsync(this, $"Delete the preset \"{name}\" from the amp?", _scale)) return;
            if (!await _owner.DeleteEqPresetAsync(name, _eqPluginUri))
                await Dialogs.MessageAsync(this, "The amp didn't delete the preset.", _scale);
            await RefreshEqAsync();
        }

        private async Task FlushEqBandsAsync()
        {
            if (_eqSending) return;
            _eqSendTimer.Stop();
            if (_eqDirtyBands.Count == 0) return;

            var bands = _eqDirtyBands.Order()
                .Select(i => new EqBand(i, EqBandParam(i), _eqSliders[i].Value))
                .ToList();
            _eqDirtyBands.Clear();

            _eqSending = true;
            try
            {
                if (_eqLegacy) await _owner.SetEqBandsAsync(bands);
                else await _owner.SetGeqBandsAsync(_eqSource, bands);
            }
            finally { _eqSending = false; }
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
            _lblEqSource.Text = "Loading…";
            if (_eqDeviceIp != _owner._deviceIp)
            {
                _eqDeviceIp = _owner._deviceIp;
                _eqInputChoice = null;
            }
            var current = await _owner.GetEqStateAsync();
            var modes = current is { Source.Length: > 0 } ? await _owner.GetEqSourceModesAsync() : null;
            bool legacy = modes == null;
            string currentInput = current?.Source ?? string.Empty;
            var shownInputs = !legacy ? await _owner.GetInputsAsync(_owner._deviceIp) : null;
            var inputs = modes?.Select(m => m.Source)
                .Where(i => shownInputs == null || i == currentInput || shownInputs.Contains(i, StringComparer.OrdinalIgnoreCase))
                .ToList() ?? [];
            if (currentInput.Length > 0 && !inputs.Contains(currentInput)) inputs.Insert(0, currentInput);
            string source = !legacy && _eqInputChoice is { } choice && inputs.Contains(choice) ? choice : currentInput;
            var mode = modes?.FirstOrDefault(m => m.Source == source);

            var state = legacy ? current : await _owner.GetGeqStateAsync(source);
            var peq = !legacy ? await _owner.GetPeqStateAsync(source) : null;
            bool enabled = mode?.Enabled ?? (state?.Enabled == true || peq?.Enabled == true);
            string plugin =
                peq == null ? DefaultEqPlugin
                : mode != null && (mode.PluginUri == PeqPlugin || mode.PluginUri == DefaultEqPlugin) && (enabled || source != _eqSource) ? mode.PluginUri
                : peq.Enabled ? PeqPlugin
                : state?.Enabled == true ? DefaultEqPlugin
                : _eqPluginUri;
            var presets = await _owner.GetEqPresetListAsync(plugin);
            string presetName = plugin == PeqPlugin ? peq?.Name ?? string.Empty : state?.Name ?? string.Empty;

            _eqLoading = true;
            _eqLegacy = legacy;
            _eqAvailable = state != null;
            _eqSource = source;
            _eqPluginUri = plugin;
            _cboEqInput.Items.Clear();
            foreach (var input in inputs)
                _cboEqInput.Items.Add(ComboItem(input, input == currentInput ? $"{InputLabel(input)}  (current)" : InputLabel(input)));
            _cboEqInput.SelectedIndex = inputs.IndexOf(source);
            if (_eqInputRow != null) _eqInputRow.IsVisible = !legacy && inputs.Count > 0;
            _cboEqType.SelectedIndex = plugin == PeqPlugin ? 1 : 0;
            if (_eqTypeRow != null) _eqTypeRow.IsVisible = peq != null;
            _eqCustomPresets.Clear();
            _eqCustomPresets.UnionWith(presets.Custom);
            _eqBuiltInPresets.Clear();
            _eqBuiltInPresets.UnionWith(presets.BuiltIn);
            _cboEqPreset.Items.Clear();
            foreach (var p in presets.Custom) _cboEqPreset.Items.Add(ComboItem(p, $"{p}  (custom)"));
            foreach (var p in presets.BuiltIn) _cboEqPreset.Items.Add(ComboItem(p, p));
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
                _lblEqSource.Text = legacy && state.Source.Length > 0 ? $"Input: {InputLabel(state.Source)}" : string.Empty;
            }
            else
            {
                _lblEqSource.Text = "EQ unavailable";
            }
            ApplyPeqState(peq);
            ShowEqEditor();
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
                ToggleRow("Volume keys also change linked amps", _chkGroupVolume));

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
            var overlayCard = Card("Volume overlay", LabeledRow("Position", _cboCorner), LabeledRow("Duration", _cboDuration));

            return Page("Volume", null, [], new StackPanel { Children = { keysCard, overlayCard } });
        }

        private Control BuildGeneralPage()
        {
            _chkSuppress.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetSuppress(_chkSuppress.IsChecked == true); };
            _chkAutoStart.IsCheckedChanged += (_, _) => { if (_loading) return; _owner.SetAutoStartSetting(_chkAutoStart.IsChecked == true); };
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
                    ToggleRow("Suppress Windows volume OSD", _chkSuppress),
                    ToggleRow(AutoStart.Label, _chkAutoStart),
                    ToggleRow("Forward media keys (play/pause/next/prev) to Wiim", _chkForwardMedia)));

                var btnClear = Pill("Allow all outputs");
                btnClear.Margin = new Thickness(0, 8, 0, 0);
                btnClear.Click += (_, _) => { _owner.ClearOutputDevices(); LoadFromOwner(); };
                cards.Children.Add(Card("Output devices",
                    Muted("Take over the volume keys only for these outputs (none selected = all)."), _outputList, btnClear));
            }
            else if (OperatingSystem.IsLinux())
            {
                cards.Children.Add(Card("Behavior", ToggleRow(AutoStart.Label, _chkAutoStart)));
                cards.Children.Add(BuildLinuxShortcutsCard());
            }
            else
            {
                cards.Children.Add(Card("Behavior", ToggleRow(AutoStart.Label, _chkAutoStart)));
                cards.Children.Add(Card("Volume and media keys",
                    Muted("macOS doesn't let apps take over the volume keys. Set your own shortcuts on the Hotkeys page, " +
                          "or run these commands from the Shortcuts app or Automator:"),
                    CommandList()));
            }

            var scale = new ComboBox { Width = 140 };
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
            cards.Children.Add(Card("Appearance", LabeledRow("Scale", scale), Muted("Makes this window bigger or smaller.")));

            return Page("General", null, [], cards);
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

        private void LoadFromOwner()
        {
            _loading = true;

            var current = _owner._knownDevices.FirstOrDefault(d => d.Ip == _owner._deviceIp);
            _lblDeviceName.Text = current.Name ?? "No device";
            _lblDeviceIp.Text = _owner._deviceIp +
                (_owner._outputMonitor is { IsEnabledOutputActive: false } ? "  ·  inactive output" : "");
            RebuildDeviceCards();

            _chkSuppress.IsChecked = _owner._suppress;
            _chkAutoStart.IsChecked = IsAutoStartEnabled();
            _chkForwardMedia.IsChecked = _owner._forwardMediaKeys;
            _chkPortal.IsChecked = _owner._linuxPortalShortcuts;
            _numStep.Value = Math.Clamp(_owner._volumeStep, 1, 50);
            _chkLogStep.IsChecked = _owner._logStep;
            _chkGroupVolume.IsChecked = _owner._groupVolumeKeys;
            UpdatePortalStatus();

            string selectedAmp = SelectedAmpIp ?? _owner._deviceIp;
            _cboAmp.Items.Clear();
            foreach (var d in _owner._knownDevices) _cboAmp.Items.Add(ComboItem(d.Ip, d.Name));
            int ampIndex = _owner._knownDevices.FindIndex(d => d.Ip == selectedAmp);
            _cboAmp.SelectedIndex = ampIndex >= 0 ? ampIndex : _cboAmp.Items.Count > 0 ? 0 : -1;
            _cboCorner.SelectedItem = _cboCorner.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is OsdCorner c && c == _owner._osdCorner);
            _cboDuration.SelectedItem = _cboDuration.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int ms && ms == _owner._osdDurationMs);
            if (_cboDuration.SelectedIndex < 0 && _cboDuration.Items.Count > 0) _cboDuration.SelectedIndex = 2;

            var toggles = new List<Control>();
            foreach (var (id, name) in ActiveOutputDevices())
            {
                var toggle = new ToggleSwitch { IsChecked = _owner._enabledOutputIds.Contains(id) };
                string deviceId = id;
                toggle.IsCheckedChanged += (_, _) => { if (!_loading) _owner.SetOutputDeviceEnabled(deviceId, toggle.IsChecked == true); };
                toggles.Add(ToggleRow(name, toggle));
            }
            if (toggles.Count == 0) toggles.Add(Muted("No playback devices found"));
            SetRows(_outputList, toggles);
            RebuildHotkeys();

            _loading = false;
        }
    }
}
