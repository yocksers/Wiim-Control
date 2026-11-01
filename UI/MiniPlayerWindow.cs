using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed class MiniPlayerWindow : Window
    {
        private const double StageSize = 360;
        private const double DefaultSize = 340;
        private const double ScreenMargin = 24;
        private const double SlotGap = 12;
        private const double GripSize = 5;
        private const double CornerGripSize = 12;
        private const double SquareMinSize = 200;
        private const double CompactWidth = 520;
        private const double CompactHeight = 100;
        private const double CompactDefaultWidth = 480;
        private const double CompactMinWidth = 300;

        private readonly WiimController _owner;
        private readonly Image _cover = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
        private readonly PathIcon _placeholder = new()
        {
            Data = Icons.Music, Width = 96, Height = 96, Foreground = Palette.TextDim,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 60, 0, 0)
        };
        private readonly Image _compactCover = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
        private readonly PathIcon _compactPlaceholder = new()
        {
            Data = Icons.Music, Width = 40, Height = 40, Foreground = Palette.TextDim,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly TextBlock _deviceName = new()
        {
            FontSize = 12, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Grid _topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10), Opacity = 0,
            Transitions = Fade()
        };
        private readonly TextBlock _title = new()
        {
            FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis
        };
        private readonly TextBlock _artist = new()
        {
            FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#E0FFFFFF")), TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        private readonly TextBlock _detail = new()
        {
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#A0FFFFFF")), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly PlayingIndicator _playing = new()
        {
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), IsVisible = false
        };
        private readonly LevelSlider _position = new() { Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center, WheelChangesValue = false };
        private readonly TextBlock _elapsed = TimeText(TextAlignment.Left);
        private readonly TextBlock _total = TimeText(TextAlignment.Right);
        private readonly Grid _positionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 8, 0, 0), IsVisible = false
        };
        private readonly PathIcon _playPauseIcon = new() { Data = Icons.Play, Width = 22, Height = 22, Foreground = Brushes.Black };
        private readonly Button _playPause;
        private readonly Button _shuffle, _repeat, _previous, _next;
        private readonly PathIcon _shuffleIcon, _repeatIcon;
        private readonly LevelSlider _volume = new()
        {
            Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false, WheelChangesValue = false
        };
        private readonly TextBlock _volumeValue = new()
        {
            Text = "–", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#C0FFFFFF")), MinWidth = 24, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Button _mute;
        private readonly PathIcon _muteIcon;
        private readonly Grid _volumeRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 0)
        };
        private readonly StackPanel _controlsStack = new();
        private readonly Grid _buttons = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,*"), Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _close = SmallButton(Icons.Close, 12);
        private readonly Button _modeButton = SmallButton(Icons.FullScreenExit, 13);
        private readonly Viewbox _squareView = new() { Stretch = Stretch.Uniform };
        private readonly Viewbox _compactView = new() { Stretch = Stretch.Uniform };
        private readonly Panel _compactText = new() { Margin = new Thickness(14, 0, 6, 0) };
        private readonly StackPanel _compactInfo = new() { VerticalAlignment = VerticalAlignment.Center, Transitions = Fade() };
        private readonly StackPanel _compactControls = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Transitions = Fade() };
        private readonly StackPanel _compactButtons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _compactSide = new()
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0), Opacity = 0, Transitions = Fade()
        };
        private readonly ColumnDefinition _progressDone = new(0, GridUnitType.Star);
        private readonly ColumnDefinition _progressLeft = new(1, GridUnitType.Star);
        private readonly Grid _compactProgress = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false };
        private bool _compact;
        private bool _hovering;
        private readonly DispatcherTimer _volumeSendTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private int? _pendingVolume;
        private bool _volumeSending;
        private bool _volumeUpdating;
        private bool? _muted;
        private DateTime _volumeTouched = DateTime.MinValue;
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private const int InstantPollTicks = 5;
        private int _quietTicks;
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DeviceStatus? _status;
        private string _target;
        private DateTime _fetched;
        private string _artUrl = string.Empty;
        private bool _refreshing;
        private bool _pinned;
        private readonly Button _pin = SmallButton(Icons.PinOutline, 13);
        private readonly List<Border> _grips = [];
        private WindowEdge? _resizeEdge;
        private PixelPoint _resizeStartPointer;
        private PixelPoint _resizeStartPosition;
        private Size _resizeStartSize;

        internal string Ip { get; }

        public MiniPlayerWindow(WiimController owner, string ip, int slot)
        {
            _owner = owner;
            Ip = ip;
            _target = ip;
            SetDeviceName(owner.DeviceNameOf(ip));
            Icon = new WindowIcon(AssetLoader.Open(new Uri(
                $"avares://WiimControl/Assets/{(OperatingSystem.IsWindows() ? "logo.ico" : "tray.png")}")));
            SystemDecorations = SystemDecorations.None;
            Background = new SolidColorBrush(Color.Parse("#0E0E0E"));
            WindowStartupLocation = WindowStartupLocation.Manual;
            _compact = owner._miniPlayerCompactIps.Contains(ip);
            ApplyTopmost();
            ApplyMinimumSize();
            bool remembered = Placements.TryGetValue(ip, out var placement);
            SetSize(remembered ? FitAspect(placement.Size) : DefaultWidth);
            Position = remembered && IsOnScreen(placement.Position) ? placement.Position : DefaultPosition(slot);
            _pinned = remembered && placement.Pinned;

            (_shuffle, _shuffleIcon) = ControlButton(Icons.Shuffle, 18);
            (_repeat, _repeatIcon) = ControlButton(Icons.Repeat, 18);
            (_previous, _) = ControlButton(Icons.Previous, 26);
            (_next, _) = ControlButton(Icons.Next, 26);
            (_mute, _muteIcon) = ControlButton(Icons.Volume, 16);
            _mute.IsEnabled = false;
            ToolTip.SetTip(_mute, "Mute");
            _playPause = new Button
            {
                Content = _playPauseIcon, Width = 50, Height = 50, CornerRadius = new CornerRadius(25), Background = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            _shuffle.IsVisible = _repeat.IsVisible = false;
            ToolTip.SetTip(_previous, "Previous");
            ToolTip.SetTip(_next, "Next");
            Content = BuildLayout();

            _position.ValueChanged += (_, _) => { if (_position.IsDragging) _elapsed.Text = FormatTime(_position.Value); };
            _position.ValueCommitted += async (_, _) => await SeekAsync(_position.Value);
            _shuffle.Click += async (_, _) => await ToggleShuffleAsync();
            _repeat.Click += async (_, _) => await CycleRepeatAsync();
            _previous.Click += async (_, _) => await TransportAsync("prev");
            _playPause.Click += async (_, _) => await TransportAsync("onepause");
            _next.Click += async (_, _) => await TransportAsync("next");
            _volume.ValueChanged += (_, _) =>
            {
                _volumeValue.Text = _volume.Value.ToString();
                if (_volumeUpdating) return;
                _volumeTouched = DateTime.UtcNow;
                _pendingVolume = _volume.Value;
                if (!_volumeSendTimer.IsEnabled) _volumeSendTimer.Start();
            };
            _volumeSendTimer.Tick += async (_, _) => await FlushVolumeAsync();
            _mute.Click += async (_, _) => await ToggleMuteAsync();
            _pollTimer.Tick += async (_, _) =>
            {
                if (_owner.HasInstantUpdates(Ip) && _owner.HasInstantUpdates(_owner.PlaybackTargetOf(Ip)) && ++_quietTicks % InstantPollTicks != 0) return;
                await RefreshAsync();
            };
            _clockTimer.Tick += (_, _) => AdvancePosition();
            KeyDown += async (_, e) =>
            {
                if (e.Handled) return;
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.Space && _status != null && !_owner.IsDeviceLocked(Ip))
                {
                    e.Handled = true;
                    await TransportAsync("onepause");
                }
            };
            PositionChanged += (_, _) => RememberPlacement();
            SizeChanged += (_, _) => RememberPlacement();
            Opened += async (_, _) =>
            {
                _pollTimer.Start();
                _clockTimer.Start();
                await RefreshAsync();
            };
            Closing += (_, _) =>
            {
                if (_pendingVolume is { } volume) _ = _owner.SetVolumeAsync(Ip, volume);
                _pendingVolume = null;
                _owner.SaveConfig();
            };
            Closed += (_, _) =>
            {
                _pollTimer.Stop();
                _volumeSendTimer.Stop();
                _clockTimer.Stop();
                ShowArtwork(null);
            };
        }

        private static TextBlock TimeText(TextAlignment alignment) => new()
        {
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#C0FFFFFF")), MinWidth = 30, TextAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static Avalonia.Animation.Transitions Fade() =>
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }
        ];

        private static Button SmallButton(StreamGeometry geometry, double size)
        {
            var button = new Button
            {
                Content = new PathIcon { Data = geometry, Width = size, Height = size }, Width = 26, Height = 26, MinHeight = 0,
                Padding = new Thickness(0), Background = new SolidColorBrush(Color.Parse("#B0000000")),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("icon");
            return button;
        }

        private double Aspect => _compact ? CompactWidth / CompactHeight : 1;

        private double DefaultWidth => (_compact ? CompactDefaultWidth : DefaultSize) * _owner.UiScale;

        private Dictionary<string, MiniPlayerPlacement> Placements => _compact ? _owner._miniPlayerCompactPlacements : _owner._miniPlayerPlacements;

        private double FitAspect(Size size) => Math.Min(size.Width, size.Height * Aspect);

        private void SetSize(double width)
        {
            width = Math.Max(width, MinWidth);
            Width = width;
            Height = width / Aspect;
        }

        private void ApplyMinimumSize()
        {
            MinWidth = MinHeight = 0;
            MinWidth = _compact ? CompactMinWidth : SquareMinSize;
            MinHeight = MinWidth / Aspect;
        }

        internal void ApplyTopmost() => Topmost = _compact ? _owner.MiniPlayerCompactOnTop : _owner.MiniPlayerOnTop;

        private static void Detach(Control control)
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
        }

        private static (Button Button, PathIcon Icon) ControlButton(StreamGeometry geometry, double size)
        {
            var (button, icon) = SettingsWindow.IconButton(geometry);
            icon.Width = icon.Height = size;
            icon.Foreground = Brushes.White;
            return (button, icon);
        }

        private Control BuildLayout()
        {
            _positionRow.Children.Add(_elapsed);
            Grid.SetColumn(_position, 1);
            _positionRow.Children.Add(_position);
            Grid.SetColumn(_total, 2);
            _positionRow.Children.Add(_total);
            _positionRow.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) _compactProgress.IsVisible = _positionRow.IsVisible;
            };

            var detailRow = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Children = { _playing, _detail } };
            DockPanel.SetDock(_playing, Dock.Left);

            foreach (var (control, column, align) in new (Control, int, HorizontalAlignment)[]
                     {
                         (_shuffle, 0, HorizontalAlignment.Left), (_previous, 1, HorizontalAlignment.Center),
                         (_playPause, 2, HorizontalAlignment.Center), (_next, 3, HorizontalAlignment.Center),
                         (_repeat, 4, HorizontalAlignment.Right)
                     })
            {
                control.HorizontalAlignment = align;
                control.VerticalAlignment = VerticalAlignment.Center;
                control.Margin = new Thickness(column is 1 or 3 ? 12 : 0, 0, column is 1 or 3 ? 12 : 0, 0);
                Grid.SetColumn(control, column);
                _buttons.Children.Add(control);
            }

            _volumeRow.Children.Add(_mute);
            Grid.SetColumn(_volume, 1);
            _volumeRow.Children.Add(_volume);
            Grid.SetColumn(_volumeValue, 2);
            _volumeRow.Children.Add(_volumeValue);

            _controlsStack.Children.AddRange([_title, _artist, detailRow, _positionRow, _buttons, _volumeRow]);
            var controls = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(18, 70, 18, 12),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(170, 0, 0, 0), 0.4),
                        new GradientStop(Color.FromArgb(235, 0, 0, 0), 1)
                    }
                },
                Child = _controlsStack
            };
            _squareView.Child = new Border
            {
                Width = StageSize, Height = StageSize, ClipToBounds = true, Background = Palette.Card,
                Child = new Grid { Children = { _placeholder, _cover, controls } }
            };

            _compactText.Children.AddRange([_compactInfo, _compactControls]);
            _compactProgress.ColumnDefinitions.AddRange([_progressDone, _progressLeft]);
            _compactProgress.Children.Add(new Border { Background = Brushes.White });
            var compactGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
            compactGrid.Children.Add(new Panel
            {
                Width = CompactHeight, Height = CompactHeight, Background = Palette.Card, Children = { _compactPlaceholder, _compactCover }
            });
            foreach (var (control, column) in new (Control, int)[] { (_compactText, 1), (_compactButtons, 2), (_compactSide, 3) })
            {
                Grid.SetColumn(control, column);
                compactGrid.Children.Add(control);
            }
            Grid.SetColumnSpan(_compactProgress, 4);
            compactGrid.Children.Add(_compactProgress);
            _compactView.Child = new Border
            {
                Width = CompactWidth, Height = CompactHeight, ClipToBounds = true, Background = new SolidColorBrush(Color.Parse("#161616")),
                Child = compactGrid
            };

            ToolTip.SetTip(_close, "Close (Esc)");
            _close.Click += (_, _) => Close();
            _modeButton.Click += (_, _) => SetCompact(!_compact);
            var name = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#B0000000")), CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 4),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                Child = _deviceName
            };
            _pin.Click += (_, _) => SetPinned(!_pinned, save: true);
            _topBar.Children.Add(name);
            foreach (var (control, column) in new (Control, int)[] { (_modeButton, 1), (_pin, 2), (_close, 3) }) Grid.SetColumn(control, column);

            var root = new Grid { Background = Brushes.Transparent, Children = { _squareView, _compactView, _topBar } };
            root.PropertyChanged += (_, e) =>
            {
                if (e.Property != IsPointerOverProperty) return;
                _hovering = root.IsPointerOver;
                ApplyHover();
            };
            root.PointerPressed += (_, e) =>
            {
                if (e.Handled || _pinned || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                BeginMoveDrag(e);
            };
            AddResizeGrips(root);
            ArrangeLayout();
            SetPinned(_pinned, save: false);
            return root;
        }

        private void ArrangeLayout()
        {
            foreach (var part in new Control[] { _title, _artist, _positionRow, _volumeRow, _previous, _playPause, _next, _modeButton, _pin, _close })
                Detach(part);
            if (_compact)
            {
                _compactInfo.Children.AddRange([_title, _artist]);
                _compactControls.Children.AddRange([_positionRow, _volumeRow]);
                _compactButtons.Children.AddRange([_previous, _playPause, _next]);
                _compactSide.Children.AddRange([_modeButton, _pin, _close]);
            }
            else
            {
                _controlsStack.Children.Insert(0, _title);
                _controlsStack.Children.Insert(1, _artist);
                _controlsStack.Children.Insert(3, _positionRow);
                _controlsStack.Children.Add(_volumeRow);
                _buttons.Children.AddRange([_previous, _playPause, _next]);
                _topBar.Children.AddRange([_modeButton, _pin, _close]);
            }
            _modeButton.Margin = _pin.Margin = new Thickness(0, 0, _compact ? 0 : 6, 0);
            _position.MinWidth = _volume.MinWidth = _compact ? 60 : 160;
            _squareView.IsVisible = _topBar.IsVisible = !_compact;
            _compactView.IsVisible = _compact;
            ((PathIcon)_modeButton.Content!).Data = _compact ? Icons.FullScreen : Icons.FullScreenExit;
            ToolTip.SetTip(_modeButton, _compact ? "Switch to the full mini player" : "Switch to the compact mini player");
            ApplyHover();
        }

        private void ApplyHover()
        {
            _topBar.Opacity = _compactSide.Opacity = _compactControls.Opacity = _hovering ? 1 : 0;
            _compactControls.IsHitTestVisible = _hovering;
            _compactInfo.Opacity = _hovering ? 0 : 1;
        }

        private void SetCompact(bool compact)
        {
            if (compact == _compact) return;
            RememberPlacement();
            var placements = compact ? _owner._miniPlayerCompactPlacements : _owner._miniPlayerPlacements;
            bool remembered = placements.TryGetValue(Ip, out var placement);
            _compact = compact;
            if (compact) _owner._miniPlayerCompactIps.Add(Ip);
            else _owner._miniPlayerCompactIps.Remove(Ip);
            ArrangeLayout();
            ApplyMinimumSize();
            ApplyTopmost();
            SetSize(remembered ? FitAspect(placement.Size) : DefaultWidth);
            if (remembered && IsOnScreen(placement.Position)) Position = placement.Position;
            SetPinned(remembered && placement.Pinned, save: false);
            RememberPlacement();
            _owner.SaveConfig();
        }

        private void SetPinned(bool pinned, bool save)
        {
            _pinned = pinned;
            var pinIcon = (PathIcon)_pin.Content!;
            pinIcon.Data = pinned ? Icons.Pin : Icons.PinOutline;
            pinIcon.Foreground = pinned ? Palette.AccentText : Brushes.White;
            ToolTip.SetTip(_pin, pinned ? "Pinned: can't be moved or resized. Click to unpin" : "Pin in place");
            foreach (var grip in _grips) grip.IsHitTestVisible = !pinned;
            if (!save) return;
            Placements[Ip] = new MiniPlayerPlacement(Position, new Size(Width, Height), pinned);
            _owner.SaveConfig();
        }

        private void AddResizeGrips(Grid root)
        {
            var stretch = double.NaN;
            foreach (var (edge, horizontal, vertical, width, height, cursor) in new (WindowEdge, HorizontalAlignment, VerticalAlignment, double, double, StandardCursorType)[]
                     {
                         (WindowEdge.West, HorizontalAlignment.Left, VerticalAlignment.Stretch, GripSize, stretch, StandardCursorType.SizeWestEast),
                         (WindowEdge.East, HorizontalAlignment.Right, VerticalAlignment.Stretch, GripSize, stretch, StandardCursorType.SizeWestEast),
                         (WindowEdge.North, HorizontalAlignment.Stretch, VerticalAlignment.Top, stretch, GripSize, StandardCursorType.SizeNorthSouth),
                         (WindowEdge.South, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, stretch, GripSize, StandardCursorType.SizeNorthSouth),
                         (WindowEdge.NorthWest, HorizontalAlignment.Left, VerticalAlignment.Top, CornerGripSize, CornerGripSize, StandardCursorType.TopLeftCorner),
                         (WindowEdge.NorthEast, HorizontalAlignment.Right, VerticalAlignment.Top, CornerGripSize, CornerGripSize, StandardCursorType.TopRightCorner),
                         (WindowEdge.SouthWest, HorizontalAlignment.Left, VerticalAlignment.Bottom, CornerGripSize, CornerGripSize, StandardCursorType.BottomLeftCorner),
                         (WindowEdge.SouthEast, HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerGripSize, CornerGripSize, StandardCursorType.BottomRightCorner)
                     })
            {
                var grip = new Border
                {
                    Background = Brushes.Transparent, HorizontalAlignment = horizontal, VerticalAlignment = vertical, Width = width, Height = height,
                    Cursor = new Cursor(cursor)
                };
                grip.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    _resizeEdge = edge;
                    _resizeStartPointer = this.PointToScreen(e.GetPosition(this));
                    _resizeStartPosition = Position;
                    _resizeStartSize = new Size(Width, Height);
                    e.Pointer.Capture(grip);
                };
                grip.PointerMoved += (_, e) =>
                {
                    if (_resizeEdge is { } active) ResizeKeepingShape(active, this.PointToScreen(e.GetPosition(this)));
                };
                grip.PointerReleased += (_, _) => _resizeEdge = null;
                grip.PointerCaptureLost += (_, _) => _resizeEdge = null;
                _grips.Add(grip);
                root.Children.Add(grip);
            }
        }

        private void ResizeKeepingShape(WindowEdge edge, PixelPoint pointer)
        {
            double scaling = DesktopScaling, aspect = Aspect;
            double dx = (pointer.X - _resizeStartPointer.X) / scaling, dy = (pointer.Y - _resizeStartPointer.Y) / scaling * aspect;
            double grow = edge switch
            {
                WindowEdge.East => dx,
                WindowEdge.West => -dx,
                WindowEdge.South => dy,
                WindowEdge.North => -dy,
                WindowEdge.SouthEast => (dx + dy) / 2,
                WindowEdge.NorthWest => -(dx + dy) / 2,
                WindowEdge.NorthEast => (dx - dy) / 2,
                _ => (dy - dx) / 2
            };
            double largest = Screens.ScreenFromWindow(this) is { } screen
                ? Math.Min(screen.WorkingArea.Width, screen.WorkingArea.Height * aspect) / scaling
                : double.MaxValue;
            double width = Math.Clamp(Math.Round(_resizeStartSize.Width + grow), MinWidth, Math.Max(MinWidth, largest));
            bool left = edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
            bool top = edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
            SetSize(width);
            if (left || top)
                Position = new PixelPoint(
                    _resizeStartPosition.X + (left ? (int)Math.Round((_resizeStartSize.Width - Width) * scaling) : 0),
                    _resizeStartPosition.Y + (top ? (int)Math.Round((_resizeStartSize.Height - Height) * scaling) : 0));
        }

        private void SetDeviceName(string name)
        {
            Title = name;
            _deviceName.Text = name;
        }

        internal void PlaceAtDefault(int slot)
        {
            SetPinned(false, save: false);
            WindowState = WindowState.Normal;
            SetSize(DefaultWidth);
            Position = DefaultPosition(slot);
        }

        internal bool IsShowingArtwork(Bitmap bitmap) => ReferenceEquals(_cover.Source, bitmap);

        private PixelPoint DefaultPosition(int slot)
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen == null) return new PixelPoint(100 + slot * 30, 100 + slot * 30);
            var area = screen.WorkingArea;
            double scaling = screen.Scaling;
            int perRow = Math.Max(1, (int)((area.Width / scaling - ScreenMargin) / (Width + SlotGap)));
            int perColumn = Math.Max(1, (int)((area.Height / scaling - ScreenMargin) / (Height + SlotGap)));
            int column = slot % perRow, row = slot / perRow % perColumn;
            return new PixelPoint(
                area.Right - (int)((Width + ScreenMargin + column * (Width + SlotGap)) * scaling),
                area.Bottom - (int)((Height + ScreenMargin + row * (Height + SlotGap)) * scaling));
        }

        private bool IsOnScreen(PixelPoint position) =>
            Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(position.X + 60, position.Y + 10)));

        private void RememberPlacement()
        {
            if (WindowState != WindowState.Normal || !IsVisible) return;
            Placements[Ip] = new MiniPlayerPlacement(Position, new Size(Width, Height), _pinned);
        }

        internal void RefreshNow() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                string ip = Ip;
                if (_owner._knownDevices.Count > 1 && DateTime.UtcNow - _owner._multiroomCacheTime > MultiroomCacheLifetime)
                    await _owner.GetMultiroomSnapshotAsync();
                string target = _owner.PlaybackTargetOf(ip);
                var statusTask = _owner.GetDeviceStatusAsync(target);
                var volumeTask = target == ip ? null : _owner.GetVolumeAsync(ip);
                var status = await statusTask;
                var volume = volumeTask != null ? await volumeTask : status?.Volume;
                if (ip != Ip || !IsVisible) return;
                ShowStatus(target, status);
                ShowVolume(volume);
                _owner.NoteDeviceStatus(target, status);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ShowStatus(string target, DeviceStatus? status)
        {
            SetDeviceName(_owner.DeviceNameOf(Ip));
            _target = target;
            _status = status;
            _fetched = status?.PositionAt ?? DateTime.UtcNow;
            ApplyLock();
            if (status == null)
            {
                _title.Text = "Not reachable";
                _artist.Text = _detail.Text = string.Empty;
                _playing.IsVisible = _positionRow.IsVisible = _shuffle.IsVisible = _repeat.IsVisible = false;
                _playPauseIcon.Data = Icons.Play;
                _placeholder.Data = _compactPlaceholder.Data = Icons.Music;
                SetArtwork(string.Empty);
                return;
            }

            (_title.Text, _artist.Text, _detail.Text) = _owner.NowPlayingTexts(status, target, target != Ip);
            _artist.IsVisible = _artist.Text.Length > 0;
            _placeholder.Data = _compactPlaceholder.Data = Icons.ForInput(InputOfMode(status.Mode));
            _playing.IsVisible = status.IsPlaying;
            _playPauseIcon.Data = status.IsPlaying ? Icons.Pause : Icons.Play;
            ToolTip.SetTip(_playPause, status.IsPlaying ? "Pause" : "Play");

            bool network = IsNetworkMode(status.Mode);
            _shuffle.IsVisible = _repeat.IsVisible = network;
            ShowLoop(status.Loop);
            bool seekable = network && status.DurationMs > 0;
            _positionRow.IsVisible = seekable;
            if (seekable)
            {
                _position.Maximum = Math.Max(1, status.DurationMs / 1000);
                _total.Text = FormatTime(status.DurationMs / 1000);
                ShowPosition(status.PositionNow);
            }
            SetArtwork(status.ArtUrl);
        }

        private void ShowVolume(DeviceVolume? volume)
        {
            if (volume is not { } v)
            {
                _muted = null;
                ApplyLock();
                _volumeValue.Text = "–";
                return;
            }
            bool busy = _volume.IsDragging || _pendingVolume != null || _volumeSending ||
                        DateTime.UtcNow - _volumeTouched < TimeSpan.FromSeconds(1.5);
            if (busy && _muted != null) return;
            ShowMuted(v.Muted);
            ApplyLock();
            _volumeUpdating = true;
            _volume.Value = v.Volume;
            _volumeUpdating = false;
            _volumeValue.Text = v.Volume.ToString();
        }

        internal void ApplyLock()
        {
            bool unlocked = !_owner.IsDeviceLocked(Ip);
            _playPause.IsEnabled = _previous.IsEnabled = _next.IsEnabled = _status != null && unlocked;
            _volume.IsEnabled = _mute.IsEnabled = _muted != null && unlocked;
            _position.IsEnabled = _shuffle.IsEnabled = _repeat.IsEnabled = unlocked;
        }

        private void ShowMuted(bool muted)
        {
            _muted = muted;
            _volume.Muted = muted;
            _muteIcon.Data = muted ? Icons.Mute : Icons.Volume;
            ToolTip.SetTip(_mute, muted ? "Unmute" : "Mute");
        }

        private async Task FlushVolumeAsync()
        {
            if (_volumeSending) return;
            if (_pendingVolume is not { } volume)
            {
                _volumeSendTimer.Stop();
                return;
            }
            _pendingVolume = null;
            _volumeSending = true;
            try { await _owner.SetVolumeAsync(Ip, volume); }
            finally
            {
                _volumeSending = false;
                _volumeTouched = DateTime.UtcNow;
            }
            if (_pendingVolume == null) _volumeSendTimer.Stop();
        }

        private async Task ToggleMuteAsync()
        {
            if (_muted is not { } muted) return;
            ShowMuted(!muted);
            _volumeTouched = DateTime.UtcNow;
            await _owner.SetMuteAsync(Ip, !muted);
            _volumeTouched = DateTime.UtcNow;
        }

        private void ShowLoop(int loop)
        {
            var (shuffle, repeat) = ParseLoop(loop);
            _shuffleIcon.Foreground = shuffle ? Palette.AccentText : Brushes.White;
            _repeatIcon.Foreground = repeat > 0 ? Palette.AccentText : Brushes.White;
            _repeatIcon.Data = repeat == 1 ? Icons.RepeatOnce : Icons.Repeat;
            ToolTip.SetTip(_shuffle, shuffle ? "Shuffle is on" : "Shuffle is off");
            ToolTip.SetTip(_repeat, repeat switch { 1 => "Repeat one", 2 => "Repeat all", _ => "Repeat is off" });
        }

        private void AdvancePosition()
        {
            if (_status is not { IsPlaying: true } status || !_positionRow.IsVisible) return;
            ShowPosition(status.PositionMs + (int)(DateTime.UtcNow - _fetched).TotalMilliseconds);
        }

        private void ShowPosition(int positionMs)
        {
            if (_position.IsDragging) return;
            int seconds = Math.Clamp(positionMs / 1000, 0, _position.Maximum);
            _position.Value = seconds;
            _elapsed.Text = FormatTime(seconds);
            _progressDone.Width = new GridLength(seconds, GridUnitType.Star);
            _progressLeft.Width = new GridLength(Math.Max(0, _position.Maximum - seconds), GridUnitType.Star);
        }

        private void SetArtwork(string url)
        {
            if (url == _artUrl) return;
            _artUrl = url;
            ShowArtwork(null);
            if (url.Length > 0) _ = LoadArtworkAsync(url);
        }

        private async Task LoadArtworkAsync(string url)
        {
            var thumbnail = await _owner.GetArtworkAsync(url);
            if (url != _artUrl) return;
            if (thumbnail != null) ShowArtwork(thumbnail);
            var large = await _owner.GetLargeArtworkAsync(url);
            if (url == _artUrl && large != null) ShowArtwork(large);
        }

        private void ShowArtwork(Bitmap? bitmap)
        {
            _cover.Source = _compactCover.Source = bitmap;
            _cover.IsVisible = _compactCover.IsVisible = bitmap != null;
            _placeholder.IsVisible = _compactPlaceholder.IsVisible = bitmap == null;
        }

        private async Task TransportAsync(string action)
        {
            await _owner.SendTransportAsync(_target, action);
            await RefreshSoonAsync(600);
        }

        private async Task SeekAsync(int seconds)
        {
            if (_status != null)
            {
                _status = _status with { PositionMs = seconds * 1000, PositionAt = DateTime.UtcNow };
                _fetched = DateTime.UtcNow;
            }
            await _owner.SeekAsync(_target, seconds);
            await RefreshSoonAsync(800);
        }

        private async Task ToggleShuffleAsync()
        {
            if (_status == null) return;
            var (shuffle, repeat) = ParseLoop(_status.Loop);
            await ChangeLoopAsync(LoopMode(!shuffle, repeat));
        }

        private async Task CycleRepeatAsync()
        {
            if (_status == null) return;
            var (shuffle, repeat) = ParseLoop(_status.Loop);
            await ChangeLoopAsync(LoopMode(shuffle, repeat switch { 0 => 2, 2 => 1, _ => 0 }));
        }

        private async Task ChangeLoopAsync(int mode)
        {
            if (_status != null) _status = _status with { Loop = mode };
            ShowLoop(mode);
            await _owner.SetLoopModeAsync(_target, mode);
            await RefreshSoonAsync(600);
        }

        private async Task RefreshSoonAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            if (IsVisible) await RefreshAsync();
        }
    }
}
