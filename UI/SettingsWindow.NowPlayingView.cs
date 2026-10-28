using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 16), VerticalAlignment = VerticalAlignment.Top,
            Transitions = FadeTransitions()
        };
        private readonly DispatcherTimer _npIdleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly Image _npBackdrop = new() { Stretch = Stretch.UniformToFill };
        private readonly Image _npBackdropOld = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
        private readonly Image _npCover = new() { Stretch = Stretch.UniformToFill };
        private readonly Image _npCoverOld = new() { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly DispatcherTimer _npFadeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private DateTime _npFadeStart;
        private bool _npFadeFromEmpty;
        private bool _npFlip;
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
        private readonly TextBlock _npDevice = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _npStyleButton = new();
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

        private Grid BuildNowPlayingOverlay()
        {
            BuildDisc();
            BuildBigVinyl();
            BuildCassette();
            BuildCd();
            BuildBigReel();
            BuildAmbient();
            _npCoverContent.Children.Add(_npCoverPlaceholder);
            _npCoverContent.Children.Add(_npCover);
            _npCoverContent.Children.Add(_npCoverOld);
            _npCoverCard.Child = _npCoverContent;
            _npCoverCard.RenderTransform = _npCoverFlip;
            _npDisc.RenderTransform = _npDiscSlide;
            _npSheen.RenderTransform = _npSheenSlide;
            _npCassette.RenderTransform = _npCassetteSlide;
            _npStage.Children.Add(_npDisc);
            _npStage.Children.Add(_npSheen);
            _npStage.Children.Add(Place(_npCassette, CassetteLeft, (StageCover - CassetteHeight) / 2));
            _npStage.Children.Add(Place(_npCd, CdLeft, (StageCover - CdSize) / 2));
            _npStage.Children.Add(_npBigReel);
            _npStage.Children.Add(_npBigDisc);
            _npStage.Children.Add(_npBigControls);
            _npStage.Children.Add(_npCoverCard);
            _npStage.Children.Add(_npCase);
            foreach (var layer in new Control[] { _npDisc, _npSheen })
            {
                Canvas.SetLeft(layer, DiscLeft);
                Canvas.SetTop(layer, (StageCover - DiscSize) / 2);
            }

            var close = Pill("✕  Close");
            ToolTip.SetTip(close, "Back to Wiim Control (Esc)");
            close.Click += (_, _) => HideNowPlayingView();
            _npStyleButton.Classes.Add("pill");
            ToolTip.SetTip(_npStyleButton, "Choose how the artwork is shown");
            _npStyleButton.Click += (_, _) =>
            {
                var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
                foreach (var style in Enum.GetValues<NowPlayingStyle>())
                {
                    var item = new MenuItem { Header = StyleName(style), ToggleType = MenuItemToggleType.Radio, IsChecked = style == _owner.NowPlayingStyle };
                    item.Click += (_, _) =>
                    {
                        _owner.NowPlayingStyle = style;
                        ApplyNowPlayingStyle();
                    };
                    menu.Items.Add(item);
                }
                menu.ShowAt(_npStyleButton);
            };
            _npFullScreen = IconPill(Icons.FullScreen, null);
            _npFullScreen.Button.Click += (_, _) => SetNowPlayingFullScreen(_npRestoreState == null);
            ApplyFullScreenButton();
            PropertyChanged += (_, e) =>
            {
                if (e.Property != WindowStateProperty || WindowState == WindowState.FullScreen || _npRestoreState == null) return;
                _npRestoreState = null;
                ApplyFullScreenButton();
            };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { close, _npDevice } };
            _npTopBar.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _npLyricsDelayButton, _npLyricsButton, BuildBrightnessButton(), _npFullScreen.Button, _npStyleButton } };
            Grid.SetColumn(right, 2);
            _npTopBar.Children.Add(right);

            _npStageBox.Child = _npStage;
            _npBackdropLayer.Children.Add(_npBackdrop);
            _npBackdropLayer.Children.Add(_npBackdropOld);
            _npOverlay.Children.Add(_npBackdropLayer);
            _npOverlay.Children.Add(_npStageBox);
            _npOverlay.Children.Add(_npAmbient);
            _npOverlay.Children.Add(BuildLyricsPanel());
            _npOverlay.Children.Add(_npTopBar);

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

        private void ShowNowPlayingView(string ip)
        {
            _npIp = ip;
            _npDevice.Text = DeviceName(ip);
            var art = new ArtworkView { IsVisible = false };
            bool loading = false;
            art.BitmapChanged += () =>
            {
                if (art.Bitmap == null && art.ImageUrl.Length > 0 && !loading)
                {
                    loading = true;
                    return;
                }
                loading = false;
                if (art.Bitmap != null && art.ImageUrl.Length > 0) _ = ShowSharpArtworkAsync(art, art.ImageUrl, art.Bitmap, fade: true);
                else ShowNowPlayingArt(art.Bitmap, flip: true);
            };
            if (_nowPlayingControls.TryGetValue(ip, out var card))
            {
                art.ImageUrl = card.Art.ImageUrl;
                art.Bitmap = card.Art.Bitmap;
            }
            loading = false;

            var title = new TextBlock { FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock
            {
                FontSize = 18, Foreground = new SolidColorBrush(Color.Parse("#E0FFFFFF")), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
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
            var playPauseIcon = new PathIcon { Data = Icons.Play, Width = 30, Height = 30, Foreground = Brushes.Black };
            var playPause = new Button
            {
                Content = playPauseIcon, Width = 68, Height = 68, CornerRadius = new CornerRadius(34), Background = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0)
            };
            shuffle.IsVisible = repeat.IsVisible = false;
            shuffle.Click += async (_, _) => await ToggleShuffleAsync(ip);
            repeat.Click += async (_, _) => await CycleRepeatAsync(ip);
            previous.Click += async (_, _) => await TransportAsync(ip, "prev");
            playPause.Click += async (_, _) => await TransportAsync(ip, "onepause");
            next.Click += async (_, _) => await TransportAsync(ip, "next");
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,*"), Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (control, column, align) in new (Control, int, HorizontalAlignment)[]
                     {
                         (shuffle, 0, HorizontalAlignment.Left), (previous, 1, HorizontalAlignment.Center), (playPause, 2, HorizontalAlignment.Center),
                         (next, 3, HorizontalAlignment.Center), (repeat, 4, HorizontalAlignment.Right)
                     })
            {
                control.HorizontalAlignment = align;
                control.VerticalAlignment = VerticalAlignment.Center;
                control.Margin = new Thickness(column is 1 or 3 ? 18 : 0, 0, column is 1 or 3 ? 18 : 0, 0);
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
                Child = new StackPanel { Children = { title, artist, detailRow, positionRow, buttons, volumeRow } }
            };
            buttons.IsEnabled = positionRow.IsEnabled = volumeRow.IsEnabled = !_owner.IsDeviceLocked(ip);
            ClearNowPlayingControls();
            _npCoverContent.Children.Add(controls);
            _npControlCard = controls;
            _npControlPanel = controls.Child;
            _npParts = new NowPlayingParts((StackPanel)controls.Child, title, artist, detailRow, positionRow, buttons,
                shuffle, previous, playPause, next, repeat, volumeRow);
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
        }

        private async Task ShowSharpArtworkAsync(ArtworkView art, string url, Bitmap thumbnail, bool fade)
        {
            var largeTask = _owner.GetLargeArtworkAsync(url);
            if (largeTask.IsCompletedSuccessfully && largeTask.Result is { } cached)
            {
                ShowNowPlayingArt(cached, fade, flip: true);
                return;
            }
            if (!fade) ShowNowPlayingArt(null, fade: false);
            bool arrived = await Task.WhenAny(largeTask, Task.Delay(LargeArtworkWait)) == largeTask;
            bool Current() => _npControls?.Art == art && art.ImageUrl == url;
            if (!Current()) return;
            if (!arrived) ShowNowPlayingArt(thumbnail, flip: true);
            var large = await largeTask;
            if (!Current()) return;
            if (large != null) ShowNowPlayingArt(large, flip: arrived);
            else if (arrived) ShowNowPlayingArt(thumbnail, flip: true);
        }

        private void SetNowPlayingFullScreen(bool on)
        {
            if (on && _npRestoreState == null)
            {
                _npRestoreState = WindowState;
                WindowState = WindowState.FullScreen;
            }
            else if (!on && _npRestoreState is { } restore)
            {
                _npRestoreState = null;
                WindowState = restore;
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
            reset.Click += (_, _) => _npBrightnessSlider.Value = DefaultNowPlayingBrightness(_owner.NowPlayingStyle);
            _npBrightnessFlyout.Content = new StackPanel
            {
                Spacing = 8, Margin = new Thickness(4, 6), Children = { _npBrightnessSlider, _npBrightnessValue, reset }
            };
            _npBrightnessSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                int brightness = (int)Math.Round(_npBrightnessSlider.Value);
                _npBrightnessValue.Text = $"{brightness}%";
                _owner.SetNowPlayingBrightness(_owner.NowPlayingStyle, brightness, save: false);
                _npBackdrop.Opacity = brightness / 100.0;
            };
            _npBrightnessFlyout.Opening += (_, _) => _npBrightnessSlider.Value = _owner.NowPlayingBrightness(_owner.NowPlayingStyle);
            _npBrightnessFlyout.Closed += (_, _) =>
            {
                _owner.SetNowPlayingBrightness(_owner.NowPlayingStyle, (int)Math.Round(_npBrightnessSlider.Value), save: true);
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
            ClearBigVinylControls();
            _npControlCard = null;
            _npControlPanel = null;
        }

        private void ShowNowPlayingArt(Bitmap? bitmap, bool fade = true, bool flip = false)
        {
            if (!fade)
            {
                _npPendingArt = null;
                _npHasPendingArt = false;
            }
            var previous = _npCover.IsVisible ? _npCover.Source : null;
            if (ReferenceEquals(previous, bitmap) && bitmap != null) return;
            if (fade && (_npFlip || _npSwap) && _npFadeTimer.IsEnabled)
            {
                if (_npHasPendingArt) _npPendingArt = bitmap;
                else if (!flip || !PastArtSwitch()) ReplaceIncomingArt(bitmap);
                else
                {
                    _npPendingArt = bitmap;
                    _npHasPendingArt = true;
                }
                return;
            }
            _npCoverOld.Source = previous;
            ReleaseNowPlayingBackdrop();
            _npBackdropOld.Source = _npBackdrop.Source;
            foreach (var (art, old) in _npArtShapes) old.Fill = art.Fill;
            _npCover.Source = bitmap;
            _npBackdrop.Source = bitmap != null ? WiimController.BlurForNowPlaying(bitmap) : null;
            _npCover.IsVisible = bitmap != null;
            _npCoverPlaceholder.IsVisible = bitmap == null;
            IBrush artFill = bitmap != null ? new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill } : Palette.Input;
            foreach (var (art, _) in _npArtShapes) art.Fill = artFill;
            _npFadeFromEmpty = previous == null;
            var style = _owner.NowPlayingStyle;
            _npFlip = flip && previous != null && (style is NowPlayingStyle.Cover or NowPlayingStyle.BigVinyl || IsAmbient(style));
            _npSwap = flip && previous != null && style is NowPlayingStyle.Vinyl or NowPlayingStyle.Cassette or NowPlayingStyle.Cd;
            _npFadeStart = DateTime.UtcNow;
            if (fade && _npOverlay.IsVisible && (previous != null || bitmap != null))
            {
                ApplyArtFade(0);
                _npFadeTimer.Start();
                if (_npSwap || _npFlip) UpdateStageMotion();
            }
            else EndArtFade();
        }

        private bool PastArtSwitch()
        {
            double seconds = _npSwap ? ArtSwapSeconds : ArtFlipSeconds;
            double progress = (DateTime.UtcNow - _npFadeStart).TotalSeconds / seconds;
            return progress >= (_npSwap ? SwapInEnd + (SwapFlipEnd - SwapInEnd) / 2 : 0.5);
        }

        private void ReplaceIncomingArt(Bitmap? bitmap)
        {
            if (_npBackdrop.Source is Bitmap blurred) _owner.DisposeWhenUnused([blurred]);
            _npCover.Source = bitmap;
            _npBackdrop.Source = bitmap != null ? WiimController.BlurForNowPlaying(bitmap) : null;
            _npCover.IsVisible = bitmap != null;
            _npCoverPlaceholder.IsVisible = bitmap == null;
            IBrush artFill = bitmap != null ? new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill } : Palette.Input;
            foreach (var (art, _) in _npArtShapes) art.Fill = artFill;
        }

        private void StepArtFade()
        {
            double seconds = _npSwap ? ArtSwapSeconds : _npFlip ? ArtFlipSeconds : ArtFadeSeconds;
            double progress = (DateTime.UtcNow - _npFadeStart).TotalSeconds / seconds;
            if (progress >= 1) EndArtFade();
            else ApplyArtFade(progress);
        }

        private static double Ease(double progress) => progress * progress * (3 - 2 * progress);

        private static double Stage(double progress, double start, double end) => Math.Clamp((progress - start) / (end - start), 0, 1);

        private void ApplySwap(double progress)
        {
            double slide = progress < SwapInEnd ? Ease(Stage(progress, 0, SwapInEnd)) : 1 - Ease(Stage(progress, SwapFlipEnd, 1));
            double inside = progress < SwapInEnd ? Stage(progress, 0, SwapInEnd) : 1 - Stage(progress, SwapFlipEnd, 1);
            double hidden = 1 - Stage(inside, 0.8, 1);
            _npDiscSlide.X = _npSheenSlide.X = slide * ((StageCover - DiscSize) / 2 - DiscLeft);
            _npCassetteSlide.X = slide * ((StageCover - CassetteWidth) / 2 - CassetteLeft);
            _npCdSlide.X = slide * ((StageCover - CdSize) / 2 - CdLeft);
            _npDisc.Opacity = _npSheen.Opacity = _npCassette.Opacity = _npCd.Opacity = hidden;
            double turn = Ease(Stage(progress, SwapInEnd, SwapFlipEnd));
            bool front = turn < 0.5;
            _npCoverFlip.AngleY = _npCaseFlip.AngleY = front ? turn * 180 : turn * 180 - 180;
            _npCoverOld.Opacity = front ? 1 : 0;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = front ? 1 : 0;
            _npBackdropOld.Opacity = (1 - turn) * _npBackdrop.Opacity;
            _npCover.Opacity = 1;
        }

        private void ApplyArtFade(double progress)
        {
            double eased = Ease(progress);
            double old = _npFadeFromEmpty ? 0 : 1 - eased;
            if (_npSwap)
            {
                ApplySwap(progress);
                return;
            }
            if (_npFlip)
            {
                double angle = eased * 180;
                bool front = angle < 90;
                _npCoverFlip.AngleY = _npAmbientFlip.AngleY = _npBigDiscFlip.AngleY = front ? angle : angle - 180;
                _npCoverOld.Opacity = front ? 1 : 0;
                foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
                _npBackdropOld.Opacity = old * _npBackdrop.Opacity;
                _npCover.Opacity = 1;
                return;
            }
            _npCoverOld.Opacity = old;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = old;
            _npBackdropOld.Opacity = old * _npBackdrop.Opacity;
            _npCover.Opacity = _npFadeFromEmpty ? eased : 1;
        }

        private void EndArtFade()
        {
            _npFadeTimer.Stop();
            bool swapped = _npSwap || _npFlip;
            _npFlip = _npSwap = false;
            _npCoverFlip.AngleY = _npAmbientFlip.AngleY = _npBigDiscFlip.AngleY = _npCaseFlip.AngleY = 0;
            _npDiscSlide.X = _npSheenSlide.X = _npCassetteSlide.X = _npCdSlide.X = 0;
            _npDisc.Opacity = _npSheen.Opacity = _npCassette.Opacity = _npCd.Opacity = 1;
            if (swapped) UpdateStageMotion();
            _npCoverOld.Opacity = _npBackdropOld.Opacity = 0;
            _npCover.Opacity = 1;
            _npCoverOld.Source = null;
            ReleaseNowPlayingBackdrop();
            foreach (var (_, faded) in _npArtShapes)
            {
                faded.Opacity = 0;
                faded.Fill = null;
            }
            if (!_npHasPendingArt) return;
            var next = _npPendingArt;
            _npPendingArt = null;
            _npHasPendingArt = false;
            if (_npOverlay.IsVisible) ShowNowPlayingArt(next, flip: true);
        }

        private void ReleaseNowPlayingBackdrop()
        {
            if (_npBackdropOld.Source is not Bitmap old) return;
            _npBackdropOld.Source = null;
            _owner.DisposeWhenUnused([old]);
        }

        private static string StyleName(NowPlayingStyle style) => style switch
        {
            NowPlayingStyle.Vinyl => "Vinyl record",
            NowPlayingStyle.BigVinyl => "Big vinyl",
            NowPlayingStyle.Cassette => "Cassette tape",
            NowPlayingStyle.Cd => "CD",
            NowPlayingStyle.Reel => "Reel-to-reel",
            NowPlayingStyle.Ambient => "Ambient",
            NowPlayingStyle.AmbientControls => "Ambient with controls",
            NowPlayingStyle.Lyrics => "Lyrics only",
            _ => "Cover only"
        };

        private static bool IsAmbient(NowPlayingStyle style) => style is NowPlayingStyle.Ambient or NowPlayingStyle.AmbientControls;

        private void ApplyNowPlayingStyle()
        {
            var style = _owner.NowPlayingStyle;
            bool ambient = IsAmbient(style);
            _npDisc.IsVisible = _npSheen.IsVisible = style == NowPlayingStyle.Vinyl;
            _npBigDisc.IsVisible = _npBigControls.IsVisible = style == NowPlayingStyle.BigVinyl;
            _npCassette.IsVisible = style == NowPlayingStyle.Cassette;
            _npCd.IsVisible = _npCase.IsVisible = style == NowPlayingStyle.Cd;
            _npBigReel.IsVisible = style == NowPlayingStyle.Reel;
            bool lyricsOnly = style == NowPlayingStyle.Lyrics;
            _npAmbient.IsVisible = ambient;
            _npStageBox.IsVisible = !ambient && !lyricsOnly;
            _npCoverCard.IsVisible = !ambient && style is not (NowPlayingStyle.BigVinyl or NowPlayingStyle.Reel);
            (_npStage.Width, _npStage.Height) = style switch
            {
                NowPlayingStyle.Vinyl => (DiscLeft + DiscSize, StageCover),
                NowPlayingStyle.BigVinyl => (BigDiscSize, BigDiscSize),
                NowPlayingStyle.Cassette => (CassetteLeft + CassetteWidth, StageCover),
                NowPlayingStyle.Cd => (CdLeft + CdSize, StageCover),
                NowPlayingStyle.Reel => (BigReelWidth, BigReelHeight),
                _ => (StageCover, StageCover)
            };
            _npStyleButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock { Text = StyleName(style), VerticalAlignment = VerticalAlignment.Center },
                    new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            ArrangeNowPlayingParts();
            _npBackdrop.Opacity = _owner.NowPlayingBrightness(style) / 100.0;
            PlaceNowPlayingControls();
            ApplyLyricsVisibility();
            UpdateStageMotion();
            if (_npOverlay.IsVisible) WakeNowPlaying();
        }

        private static Avalonia.Animation.Transitions FadeTransitions() =>
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }
        ];

        private void WakeNowPlaying()
        {
            _npTopBar.Opacity = _npAmbientControls.Opacity = 1;
            _npOverlay.Cursor = null;
            _npIdleTimer.Stop();
            _npIdleTimer.Start();
        }

        private void HideNowPlayingControls()
        {
            bool ambient = _owner.NowPlayingStyle == NowPlayingStyle.Ambient;
            if (_npTopBar.IsPointerOver || _npBrightnessFlyout.IsOpen || _npLyricsDelayFlyout.IsOpen || ambient && _npAmbientControls.IsPointerOver) return;
            _npIdleTimer.Stop();
            _npTopBar.Opacity = 0;
            if (ambient) _npAmbientControls.Opacity = 0;
            if (ambient || _owner.NowPlayingStyle == NowPlayingStyle.Lyrics) _npOverlay.Cursor = new Cursor(StandardCursorType.None);
        }

        private void PlaceNowPlayingControls()
        {
            if (_npControlPanel is not { } panel || _npControlCard is not { } card) return;
            var host = _owner.NowPlayingStyle switch
            {
                NowPlayingStyle.Ambient or NowPlayingStyle.AmbientControls => _npAmbientControls,
                _ => card
            };
            if (panel.Parent == host) return;
            if (panel.Parent is Decorator previous) previous.Child = null;
            host.Child = panel;
        }

        private void UpdateStageMotion()
        {
            bool playing = _npOverlay.IsVisible && _npControls?.Playing.IsVisible == true;
            var style = _owner.NowPlayingStyle;
            _npLabelSpin.Run(playing && style == NowPlayingStyle.Vinyl && !_npSwap);
            _npBigLabelSpin.Run(playing && style == NowPlayingStyle.BigVinyl && !_npFlip);
            _npCassetteReels?.Run(playing && style == NowPlayingStyle.Cassette && !_npSwap);
            _npCdSpin.Run(playing && style == NowPlayingStyle.Cd && !_npSwap);
            UpdateAmbientDrift(_npOverlay.IsVisible && IsAmbient(style));
            _npBigReelReels?.Run(playing && style == NowPlayingStyle.Reel);
        }

        private void UpdateTapePacks()
        {
            double progress = _npControls is { PositionRow.IsVisible: true } c ? (double)c.Position.Value / Math.Max(1, c.Position.Maximum) : 0.5;
            _npCassetteReels?.Wind(progress);
            _npBigReelReels?.Wind(progress);
        }
    }
}
