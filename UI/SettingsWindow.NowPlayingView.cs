using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

        private readonly Grid _npOverlay = new() { Background = new SolidColorBrush(Color.Parse("#0E0E0E")), IsVisible = false };
        private readonly Image _npBackdrop = new() { Stretch = Stretch.UniformToFill, Opacity = 0.45, Effect = new BlurEffect { Radius = 60 } };
        private readonly Image _npCover = new() { Stretch = Stretch.UniformToFill };
        private readonly PathIcon _npCoverPlaceholder = new()
        {
            Data = Icons.Music, Width = 120, Height = 120, Foreground = Palette.TextDim,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Ellipse _npLabel = new() { Width = LabelSize, Height = LabelSize, Fill = Palette.Input };
        private readonly RotateTransform _npSpin = new();
        private readonly Panel _npDisc = new() { Width = DiscSize, Height = DiscSize };
        private readonly Ellipse _npSheen = new() { Width = DiscSize, Height = DiscSize, IsHitTestVisible = false };
        private readonly Canvas _npStage = new() { Height = StageCover };
        private readonly Border _npCoverCard = new()
        {
            Width = StageCover, Height = StageCover, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Card,
            BoxShadow = BoxShadows.Parse("0 12 48 0 #E0000000")
        };
        private readonly Grid _npCoverContent = new();
        private readonly TextBlock _npDevice = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _npStyleButton = new();
        private readonly DispatcherTimer _npSpinTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private DateTime _npLastTick;
        private string? _npIp;
        private NowPlayingControls? _npControls;

        private Grid BuildNowPlayingOverlay()
        {
            BuildDisc();
            _npCoverContent.Children.Add(_npCoverPlaceholder);
            _npCoverContent.Children.Add(_npCover);
            _npCoverCard.Child = _npCoverContent;
            _npStage.Children.Add(_npDisc);
            _npStage.Children.Add(_npSheen);
            _npStage.Children.Add(_npCoverCard);
            foreach (var layer in new Control[] { _npDisc, _npSheen })
            {
                Canvas.SetLeft(layer, DiscLeft);
                Canvas.SetTop(layer, (StageCover - DiscSize) / 2);
            }

            var close = Pill("✕  Close");
            ToolTip.SetTip(close, "Back to Wiim Control (Esc)");
            close.Click += (_, _) => HideNowPlayingView();
            _npStyleButton.Classes.Add("pill");
            _npStyleButton.Click += (_, _) =>
            {
                _owner.NowPlayingVinyl = !_owner.NowPlayingVinyl;
                ApplyNowPlayingStyle();
            };
            var topBar = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 16), VerticalAlignment = VerticalAlignment.Top
            };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { close, _npDevice } };
            topBar.Children.Add(left);
            Grid.SetColumn(_npStyleButton, 2);
            topBar.Children.Add(_npStyleButton);

            var stage = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(40, 70, 40, 40), Child = _npStage };
            _npOverlay.Children.Add(_npBackdrop);
            _npOverlay.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#99000000")) });
            _npOverlay.Children.Add(stage);
            _npOverlay.Children.Add(topBar);

            _npSpinTimer.Tick += (_, _) => SpinDisc();
            return _npOverlay;
        }

        private void BuildDisc()
        {
            _npDisc.RenderTransform = _npSpin;
            _npDisc.RenderTransformOrigin = RelativePoint.Center;
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
            _npLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _npLabel.VerticalAlignment = VerticalAlignment.Center;
            _npDisc.Children.Add(_npLabel);
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
            art.BitmapChanged += () =>
            {
                ShowNowPlayingArt(art.Bitmap);
                if (art.Bitmap != null && art.ImageUrl.Length > 0) _ = LoadLargeArtworkAsync(art, art.ImageUrl);
            };
            if (_nowPlayingControls.TryGetValue(ip, out var card))
            {
                art.ImageUrl = card.Art.ImageUrl;
                art.Bitmap = card.Art.Bitmap;
            }

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
            var positionRow = BuildPositionRow(ip, out var position, out var elapsed, out var total);
            elapsed.Foreground = total.Foreground = Brushes.White;
            positionRow.Margin = new Thickness(0, 14, 0, 0);

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
                Child = new StackPanel { Children = { title, artist, detailRow, positionRow, buttons } }
            };
            ClearNowPlayingControls();
            _npCoverContent.Children.Add(controls);
            _npCoverContent.Children.Add(art);
            _npControls = new NowPlayingControls(art, title, artist, detail, playing, playPause, playPauseIcon, previous, next,
                shuffle, shuffleIcon, repeat, repeatIcon, positionRow, position, elapsed, total);

            ShowNowPlayingArt(art.Bitmap);
            if (art.Bitmap != null && art.ImageUrl.Length > 0) _ = LoadLargeArtworkAsync(art, art.ImageUrl);
            ApplyNowPlayingStyle();
            _npOverlay.IsVisible = true;
            ApplyNowPlaying(ip);
        }

        private async Task LoadLargeArtworkAsync(ArtworkView art, string url)
        {
            var large = await _owner.GetLargeArtworkAsync(url);
            if (large != null && _npControls?.Art == art && art.ImageUrl == url) ShowNowPlayingArt(large);
        }

        private void HideNowPlayingView()
        {
            _npOverlay.IsVisible = false;
            _npSpinTimer.Stop();
            _npIp = null;
            _npControls = null;
            ClearNowPlayingControls();
            ShowNowPlayingArt(null);
        }

        private void ClearNowPlayingControls()
        {
            while (_npCoverContent.Children.Count > 2) _npCoverContent.Children.RemoveAt(2);
        }

        private void ShowNowPlayingArt(Bitmap? bitmap)
        {
            _npCover.Source = bitmap;
            _npBackdrop.Source = bitmap;
            _npCover.IsVisible = bitmap != null;
            _npCoverPlaceholder.IsVisible = bitmap == null;
            _npLabel.Fill = bitmap != null ? new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill } : Palette.Input;
        }

        private void ApplyNowPlayingStyle()
        {
            bool vinyl = _owner.NowPlayingVinyl;
            _npDisc.IsVisible = _npSheen.IsVisible = vinyl;
            _npStage.Width = vinyl ? DiscLeft + DiscSize : StageCover;
            _npStyleButton.Content = vinyl ? "Cover only" : "Vinyl";
            ToolTip.SetTip(_npStyleButton, vinyl ? "Show only the cover" : "Show the cover with a spinning record");
            _npLastTick = DateTime.UtcNow;
            if (vinyl) _npSpinTimer.Start();
            else _npSpinTimer.Stop();
        }

        private void SpinDisc()
        {
            var now = DateTime.UtcNow;
            double seconds = (now - _npLastTick).TotalSeconds;
            _npLastTick = now;
            if (_npIp == null || !_npOverlay.IsVisible)
            {
                _npSpinTimer.Stop();
                return;
            }
            if (_npControls?.Playing.IsVisible != true) return;
            _npSpin.Angle = (_npSpin.Angle + DegreesPerSecond * Math.Min(seconds, 0.2)) % 360;
        }
    }
}
