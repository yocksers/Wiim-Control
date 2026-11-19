using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double StageCover = 600;
        private const double DiscSize = 560;
        private const double DiscLeft = 380;
        private const double LabelSize = 196;
        private const double DegreesPerSecond = 200;
        private const double ArtFadeSeconds = 0.5;
        private const double ArtFlipSeconds = 0.8;
        private const double ArtSwapSeconds = 2.2;
        private const double SwapInEnd = 0.3;
        private const double SwapFlipEnd = 0.65;
        private static readonly TimeSpan LargeArtworkWait = TimeSpan.FromSeconds(1.5);
        private static readonly Thickness ShadowRoom = new(50, 50, 50, 80);

        private readonly Grid _npOverlay = new() { Background = new SolidColorBrush(Color.Parse("#0E0E0E")), IsVisible = false, ClipToBounds = true };
        private readonly Panel _npBackdropLayer = new();
        private readonly Viewbox _npStageBox = new() { Stretch = Stretch.Uniform, Margin = new Thickness(0, 30, 0, 0) };
        private readonly Grid _npTopBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), Transitions = FadeTransitions()
        };
        private readonly DispatcherTimer _npIdleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly Image _npBackdrop = new() { Stretch = Stretch.UniformToFill };
        private readonly Image _npBackdropOld = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
        private readonly Image _npCover = new() { Stretch = Stretch.UniformToFill };
        private readonly Image _npCoverOld = new() { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly FrameTimer _npFadeTimer = new();
        private DateTime _npFadeStart;
        private bool _npFadeFromEmpty;
        private bool _npFlip;
        private int _npArtHold;
        private static readonly TimeSpan ArtLookupPoll = TimeSpan.FromMilliseconds(200);
        private bool _npSwap;
        private Bitmap? _npPendingArt;
        private bool _npHasPendingArt;
        private readonly Rotate3DTransform _npCoverFlip = new() { Depth = 1500 };
        private readonly TranslateTransform _npDiscSlide = new();
        private readonly TranslateTransform _npSheenSlide = new();
        private readonly TranslateTransform _npCassetteSlide = new();
        private readonly PathIcon _npCoverPlaceholder = new()
        {
            Data = Icons.Music, Width = 120, Height = 120, Foreground = Palette.TextDim,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Ellipse _npLabel = new() { Width = LabelSize, Height = LabelSize, Fill = Palette.Input };
        private readonly Ellipse _npLabelOld = new() { Width = LabelSize, Height = LabelSize, Opacity = 0, IsHitTestVisible = false };
        private readonly Spinner _npLabelSpin = new(LabelSize, DegreesPerSecond);
        private readonly Panel _npDisc = new() { Width = DiscSize, Height = DiscSize };
        private readonly Ellipse _npSheen = new() { Width = DiscSize, Height = DiscSize, IsHitTestVisible = false };
        private readonly Canvas _npStage = new() { Height = StageCover, Margin = ShadowRoom };
        private readonly Border _npCoverCard = new()
        {
            Width = StageCover, Height = StageCover, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Card,
            BoxShadow = BoxShadows.Parse("0 12 48 0 #E0000000")
        };
        private readonly Grid _npCoverContent = new();
        private readonly TextBlock _npDeviceMenuLabel = new() { Text = "Device", MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Button _npStyleButton = new();
        private readonly Border _npArtworkNotice = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20),
            Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            IsVisible = false, IsHitTestVisible = false,
            Child = new TextBlock { Text = ArtworkQuotaNotice, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) }
        };
        private string? _npIp;
        private NowPlayingControls? _npControls;
        private Border? _npControlCard;
        private Control? _npControlPanel;
        private readonly List<(Shape Art, Shape Old)> _npArtShapes = [];
        private readonly List<TextBlock> _npTitleCopies = [];
        private readonly List<TextBlock> _npArtistCopies = [];
        private readonly List<TextBlock> _npElapsedCopies = [];
        private VolumeControls? _npVolume;
        private (Button Button, PathIcon Icon) _npFullScreen;
        private readonly Slider _npBrightnessSlider = new()
        {
            Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Height = 160, TickFrequency = 1, IsSnapToTickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        private readonly TextBlock _npBrightnessValue = new() { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 40, TextAlignment = TextAlignment.Center };
        private readonly Flyout _npBrightnessFlyout = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
        private WindowState? _npRestoreState;
        private double _npBackdropLevel = 1;

        private Grid BuildNowPlayingOverlay()
        {
            BuildDisc();
            BuildBigVinyl();
            BuildCassette();
            BuildCd();
            BuildBigReel();
            BuildVuMeters();
            BuildAnalyzer();
            BuildAmbient();
            BuildGatefold();
            BuildEasterEggs();
            BuildMascot();
            _mascot.IsVisible = false;
            _npCoverContent.Children.Add(_npCoverPlaceholder);
            _npCoverContent.Children.Add(_npCover);
            _npCoverContent.Children.Add(_npCoverOld);
            _npCoverCard.Child = _npCoverContent;
            _npCoverCard.RenderTransform = new TransformGroup { Children = { _npCoverFlip, _mascotCoverScale, _mascotCoverShift } };
            _npDisc.RenderTransform = _npDiscSlide;
            _npSheen.RenderTransform = _npSheenSlide;
            _npCassette.RenderTransform = _npCassetteSlide;
            _npStage.Children.Add(_npDisc);
            _npStage.Children.Add(_npSheen);
            _npStage.Children.Add(Place(_npCassette, CassetteLeft, (StageCover - CassetteHeight) / 2));
            _npStage.Children.Add(Place(_npCd, CdLeft, (StageCover - CdSize) / 2));
            _npStage.Children.Add(_npBigReel);
            _npStage.Children.Add(_npVuPanel);
            _npStage.Children.Add(_npAnalyzerBox);
            _npStage.Children.Add(_npStereoPanel);
            _npStage.Children.Add(_npVuSingleBox);
            _npStage.Children.Add(_npBigDisc);
            _npStage.Children.Add(_npBigControls);
            _npStage.Children.Add(Place(_npGatefold, StageCover, 0));
            _npStage.Children.Add(_zipLayer);
            _npStage.Children.Add(_npCoverCard);
            _npStage.Children.Add(_npCase);
            _npStage.Children.Add(_mascot);
            foreach (var layer in new Control[] { _npDisc, _npSheen })
            {
                Canvas.SetLeft(layer, DiscLeft);
                Canvas.SetTop(layer, (StageCover - DiscSize) / 2);
            }

            var close = Pill("✕  Close");
            _npCloseButton = close;
            ToolTip.SetTip(close, "Back to Wiim Control (Esc)");
            close.Click += (_, _) => HideNowPlayingView();
            _npStyleButton.Classes.Add("pill");
            ToolTip.SetTip(_npStyleButton, "Choose how the artwork is shown");
            _npStyleButton.Click += (_, _) =>
            {
                var choices = Enum.GetValues<NowPlayingStyle>()
                    .Where(style => !UsesComputerSound(style) || VuMetersAvailable).Select(style => DropdownAction(StyleName(style), () =>
                    {
                        _owner.NowPlayingStyle = style;
                        ApplyNowPlayingStyle();
                        return Task.CompletedTask;
                    }, style == NpStyle)).ToList();
                if (ParecMissing) choices.Add(new DropdownChoice("More styles need parec (install pulseaudio-utils)", false, false, () => Task.CompletedTask));
                ShowDropdown(_npStyleButton, choices);
            };
            _npFullScreen = IconPill(Icons.FullScreen, null);
            _npFullScreen.Button.Click += (_, _) => SetNowPlayingFullScreen(_npRestoreState == null);
            ApplyFullScreenButton();
            PropertyChanged += (_, e) =>
            {
                if (e.Property != WindowStateProperty || _npWindow != null || WindowState == WindowState.FullScreen || _npRestoreState == null) return;
                _npRestoreState = null;
                ApplyFullScreenButton();
            };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 3), Children = { close } };
            _npTopBar.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 3), Children = { BuildNowPlayingDeviceButton(), _npLyricsDelayButton, _npLyricsButton, BuildControlsButton(), _npStyleButton, BuildBrightnessButton(), BuildBorderlessButton(), BuildPopOutButton(), _npFullScreen.Button } };
            Grid.SetColumn(right, 2);
            _npTopBar.Children.Add(right);
            foreach (var button in left.Children.Concat(right.Children).OfType<Button>())
            {
                button.Classes.Add("barText");
                button.ClearValue(PaddingProperty);
            }

            _npUnder.Child = _npUnderPanel;
            _npStageBox.Child = new StackPanel { Children = { _npStage, _npUnder } };
            _npBackdropLayer.Children.Add(_npBackdrop);
            _npBackdropLayer.Children.Add(_npBackdropOld);
            _npOverlay.Children.Add(_npBackdropLayer);
            _npOverlay.Children.Add(_npStageBox);
            _npOverlay.Children.Add(_npAmbient);
            _npOverlay.Children.Add(BuildLyricsPanel());
            _npOverlay.Children.Add(_npTopBar);
            _npOverlay.Children.Add(_npArtworkNotice);

            _npFadeTimer.Host = _npOverlay;
            _npFadeTimer.Tick += (_, _) => StepArtFade();
            _npIdleTimer.Tick += (_, _) => HideNowPlayingControls();
            _npOverlay.PointerMoved += (_, _) => WakeNowPlaying();
            _npOverlay.AddHandler(PointerPressedEvent, (_, _) => WakeNowPlaying(), RoutingStrategies.Tunnel, handledEventsToo: true);
            return _npOverlay;
        }

        private void BuildDisc()
        {
            var vinyl = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#2A2A2A"), 0),
                    new GradientStop(Color.Parse("#141414"), 0.45),
                    new GradientStop(Color.Parse("#0A0A0A"), 0.9),
                    new GradientStop(Color.Parse("#1C1C1C"), 1)
                }
            };
            _npDisc.Children.Add(new Ellipse { Width = DiscSize, Height = DiscSize, Fill = vinyl });
            for (double d = DiscSize - 18; d > LabelSize + 30; d -= 9)
                _npDisc.Children.Add(new Ellipse
                {
                    Width = d, Height = d, Stroke = new SolidColorBrush(Color.FromArgb((byte)(d % 27 < 9 ? 34 : 18), 255, 255, 255)),
                    StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            _npLabelSpin.Target.HorizontalAlignment = HorizontalAlignment.Center;
            _npLabelSpin.Target.VerticalAlignment = VerticalAlignment.Center;
            _npLabelSpin.Target.Children.Add(_npLabel);
            _npLabelSpin.Target.Children.Add(_npLabelOld);
            _npArtShapes.Add((_npLabel, _npLabelOld));
            _npDisc.Children.Add(_npLabelSpin.Target);
            _npDisc.Children.Add(new Ellipse
            {
                Width = 14, Height = 14, Fill = new SolidColorBrush(Color.Parse("#0E0E0E")),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            _npSheen.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25),
                    new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.45),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55),
                    new GradientStop(Color.FromArgb(28, 255, 255, 255), 0.75),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.85)
                }
            };
        }

        private Button BuildNowPlayingDeviceButton()
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { _npDeviceMenuLabel, new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12 } }
                }
            };
            button.Classes.Add("pill");
            ToolTip.SetTip(button, "Show another device in the big player, or switch a group on or off");
            button.Click += async (_, _) =>
            {
                var groups = await LoadGroupChoicesAsync(button, _npDeviceMenuLabel, NowPlayingMenuText);
                var choices = new List<DropdownChoice> { DropdownHeading("Devices") };
                choices.AddRange(_owner._knownDevices.Select(device => new DropdownChoice(device.Name, device.Ip == _npIp, true, () =>
                    {
                        if (device.Ip != _npIp) ShowNowPlayingView(device.Ip);
                        return Task.CompletedTask;
                    })));
                choices.Add(DropdownHeading("Groups (switch on/off)"));
                if (groups.Count == 0) choices.Add(NoSavedGroupsChoice());
                else choices.AddRange(groups);
                ShowDropdown(button, choices);
            };
            return button;
        }

        private string NowPlayingMenuText()
        {
            if (string.IsNullOrEmpty(_npIp)) return "Device";
            string name = DeviceName(_npIp);
            return GroupNameOf(_npIp) is { } group ? $"{name}  ·  {group}" : name;
        }

        private void UpdateNowPlayingMenuLabels()
        {
            if (string.IsNullOrEmpty(_npIp) || _npDeviceMenuLabel.Text == "Switching…") return;
            _npDeviceMenuLabel.Text = NowPlayingMenuText();
        }

        private void ShowNowPlayingView(string ip)
        {
            _npIp = ip;
            UpdateNowPlayingMenuLabels();
            var art = new ArtworkView { IsVisible = false };
            string? waiting = null;
            art.BitmapChanged += () =>
            {
                _npArtHold++;
                if (art.Bitmap == null && art.ImageUrl.Length > 0 && art.ImageUrl != waiting)
                {
                    waiting = art.ImageUrl;
                    return;
                }
                waiting = null;
                if (art.Bitmap != null && art.ImageUrl.Length > 0) _ = ShowSharpArtworkAsync(art, art.ImageUrl, art.Bitmap, fade: true);
                else if (art.Bitmap == null && _owner.OnlineArtworkSearching) _ = HoldArtDuringLookupAsync(art);
                else ShowNowPlayingArt(art.Bitmap, flip: true);
            };
            if (_nowPlayingControls.TryGetValue(ip, out var card))
            {
                art.ImageUrl = card.Art.ImageUrl;
                art.Bitmap = card.Art.Bitmap;
            }
            waiting = null;
            _npArtHold++;
            if (art.Bitmap == null) ShowNowPlayingArt(null, flip: false);

            var title = new TextBlock { FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock
            {
                FontSize = 18, Foreground = new SolidColorBrush(Color.Parse("#E0FFFFFF")), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
            if (card != null) (title.Text, artist.Text) = (card.Title.Text, card.Artist.Text);
            var detail = new TextBlock
            {
                FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#A0FFFFFF")), TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var playing = new PlayingIndicator { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var detailRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Children = { playing, detail } };
            DockPanel.SetDock(playing, Dock.Left);
            playing.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) UpdateStageMotion();
            };
            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            elapsed.Foreground = total.Foreground = Brushes.White;
            positionRow.Margin = new Thickness(0, 14, 0, 0);
            position.ValueChanged += (_, _) => UpdateTapePacks();
            positionRow.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) UpdateTapePacks();
            };
            foreach (var (source, targets) in new[] { (title, _npTitleCopies), (artist, _npArtistCopies), (elapsed, _npElapsedCopies) })
            {
                foreach (var target in targets) target.Text = source.Text;
                source.PropertyChanged += (_, e) =>
                {
                    if (e.Property != TextBlock.TextProperty) return;
                    foreach (var target in targets) target.Text = source.Text;
                };
            }

            (Button Button, PathIcon Icon) Big(StreamGeometry icon, double size)
            {
                var (button, path) = IconButton(icon);
                path.Width = path.Height = size;
                path.Foreground = Brushes.White;
                return (button, path);
            }
            var (shuffle, shuffleIcon) = Big(Icons.Shuffle, 26);
            var (repeat, repeatIcon) = Big(Icons.Repeat, 26);
            var (previous, _) = Big(Icons.Previous, 34);
            var (next, _) = Big(Icons.Next, 34);
            var (playPause, playPauseIcon) = Big(Icons.Play, 34);
            Icons.SetPlayPause(playPauseIcon, false);
            shuffle.IsVisible = repeat.IsVisible = false;
            shuffle.Click += async (_, _) => await ToggleShuffleAsync(ip);
            repeat.Click += async (_, _) => await CycleRepeatAsync(ip);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto,*") };
            foreach (var (control, column) in new (Control, int)[] { (shuffle, 0), (previous, 2), (playPause, 3), (next, 4), (repeat, 6) })
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, column);
                buttons.Children.Add(control);
            }

            var volume = CreateVolumeControls(ip);
            volume.MuteIcon.Foreground = Brushes.White;
            volume.Value.Foreground = Brushes.White;
            volume.Value.Classes.Remove("muted");
            var volumeRow = VolumeRow(volume);
            volumeRow.Margin = new Thickness(0, 14, 0, 0);

            var controls = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(30, 110, 30, 22),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(170, 0, 0, 0), 0.45),
                        new GradientStop(Color.FromArgb(230, 0, 0, 0), 1)
                    }
                },
                Child = new StackPanel()
            };
            buttons.IsEnabled = positionRow.IsEnabled = volumeRow.IsEnabled = !_owner.IsDeviceLocked(ip);
            ClearNowPlayingControls();
            _npCoverContent.Children.Add(controls);
            _npControlCard = controls;
            _npControlPanel = controls.Child;
            _npParts = new NowPlayingParts((StackPanel)controls.Child, title, artist, detailRow, positionRow, buttons,
                new Border { Child = buttons, Margin = new Thickness(0, 10, 0, 0) }, shuffle, previous, playPause, next, repeat, volumeRow);
            _npCoverContent.Children.Add(art);
            _npControls = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);
            _npVolume = volume;

            if (art.Bitmap != null && art.ImageUrl.Length > 0) _ = ShowSharpArtworkAsync(art, art.ImageUrl, art.Bitmap, fade: false);
            else ShowNowPlayingArt(art.Bitmap, fade: false);
            ApplyNowPlayingStyle();
            _npOverlay.IsVisible = true;
            ApplyNowPlaying(ip);
            ApplyVolume(ip);
            UpdateTapePacks();
            UpdateStageMotion();
            ApplyLyricsVisibility();
            WakeNowPlaying();
            _npWindow?.Activate();
        }

        private void SetNowPlayingFullScreen(bool on)
        {
            var host = NowPlayingHost;
            if (on && _npRestoreState == null)
            {
                _npRestoreState = host.WindowState;
                host.WindowState = WindowState.FullScreen;
            }
            else if (!on && _npRestoreState is { } restore)
            {
                _npRestoreState = null;
                host.WindowState = restore;
            }
            ApplyFullScreenButton();
        }

        private Button BuildBrightnessButton()
        {
            var (button, _) = IconPill(Icons.Brightness, "Background brightness for this style");
            var reset = new Button
            {
                Content = "Reset", FontSize = 11, Padding = new Thickness(8, 2), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(reset, "Back to the default brightness for this style");
            reset.Click += (_, _) => _npBrightnessSlider.Value = DefaultNowPlayingBrightness(NpStyle);
            _npBrightnessFlyout.Content = ScaledPopup(new StackPanel
            {
                Spacing = 8, Margin = new Thickness(4, 6), Children = { _npBrightnessSlider, _npBrightnessValue, reset }
            });
            _npBrightnessSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                int brightness = (int)Math.Round(_npBrightnessSlider.Value);
                _npBrightnessValue.Text = $"{brightness}%";
                _owner.SetNowPlayingBrightness(NpStyle, brightness, save: false);
                _npBackdropLevel = brightness / 100.0;
                BlendBackdrops(0);
            };
            _npBrightnessFlyout.Opening += (_, _) => _npBrightnessSlider.Value = _owner.NowPlayingBrightness(NpStyle);
            _npBrightnessFlyout.Closed += (_, _) =>
            {
                _owner.SetNowPlayingBrightness(NpStyle, (int)Math.Round(_npBrightnessSlider.Value), save: true);
                WakeNowPlaying();
            };
            button.Flyout = _npBrightnessFlyout;
            return button;
        }

        private void ApplyFullScreenButton()
        {
            bool on = _npRestoreState != null;
            _npFullScreen.Icon.Data = on ? Icons.FullScreenExit : Icons.FullScreen;
            ToolTip.SetTip(_npFullScreen.Button, on ? "Leave full screen (Esc)" : "Full screen (F11)");
        }

        private void HideNowPlayingView()
        {
            SetNowPlayingFullScreen(false);
            _npOverlay.IsVisible = false;
            _npIp = null;
            CloseNowPlayingWindow();
            _npControls = null;
            _npVolume = null;
            UpdateStageMotion();
            ClearNowPlayingControls();
            ClearLyrics();
            _npLyricsStatus = null;
            _npIdleTimer.Stop();
            _npTopBar.Opacity = 1;
            _npOverlay.Cursor = null;
            ShowNowPlayingArt(null, fade: false);
        }

        private void ClearNowPlayingControls()
        {
            while (_npCoverContent.Children.Count > 3) _npCoverContent.Children.RemoveAt(3);
            _npAmbientControls.Child = null;
            _npUnder.IsVisible = false;
            ClearBigVinylControls();
            _npControlCard = null;
            _npControlPanel = null;
        }
    }
}
