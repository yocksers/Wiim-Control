using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int LyricsLeadMs = 200;
        private const int LyricsAutoRetries = 3;
        private static readonly TimeSpan LyricsRetryDelay = TimeSpan.FromSeconds(15);
        private static readonly IBrush LyricDim = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        private static readonly IBrush LyricsPanelBackground = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0));

        private readonly Button _npLyricsButton = new();
        private readonly Button _npLyricsDelayButton = new() { Content = "Delay" };
        private readonly Border _npLyricsPanel = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 76, 32, 40), Padding = new Thickness(28, 20),
            CornerRadius = new CornerRadius(18), Background = LyricsPanelBackground, IsVisible = false
        };
        private readonly Panel _npLyricsSynced = new()
        {
            ClipToBounds = true, IsVisible = false,
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.14),
                    new GradientStop(Colors.Black, 0.86), new GradientStop(Colors.Transparent, 1)
                }
            }
        };
        private readonly StackPanel _npLyricsLines = new()
        {
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform
            {
                Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(450), Easing = new CubicEaseOut() }]
            }
        };
        private readonly ScrollViewer _npLyricsPlain = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, IsVisible = false };
        private readonly TextBlock _npLyricsNote = new()
        {
            FontSize = 14, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
        };
        private readonly TextBlock _npLyricsPlainText = new()
        {
            FontSize = 22, LineHeight = 36, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255))
        };
        private readonly TextBlock _npLyricsMessage = new()
        {
            FontSize = 20, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false
        };
        private readonly Slider _npLyricsDelaySlider = new()
        {
            Orientation = Orientation.Vertical, Minimum = -LyricsDelayLimitMs / 1000.0, Maximum = LyricsDelayLimitMs / 1000.0, Height = 110,
            TickFrequency = 0.1, IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Center
        };
        private readonly TextBlock _npLyricsDelayValue = new()
        {
            FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center
        };
        private readonly Flyout _npLyricsDelayFlyout = new() { Placement = PlacementMode.Bottom };
        private readonly DispatcherTimer _npLyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private IReadOnlyList<LyricLine> _npLyricLines = [];
        private int _npLyricIndex = -1;
        private string _npLyricsKey = string.Empty;
        private DeviceStatus? _npLyricsStatus;
        private CancellationTokenSource? _npLyricsCancel;

        private Control BuildLyricsPanel()
        {
            _npLyricsButton.Classes.Add("pill");
            _npLyricsButton.Click += (_, _) =>
            {
                _owner.NowPlayingLyrics = !_owner.NowPlayingLyrics;
                ApplyLyricsVisibility();
            };
            BuildLyricsDelayFlyout();
            _npLyricsSynced.Children.Add(_npLyricsLines);
            _npLyricsPlain.Content = new StackPanel { Children = { _npLyricsNote, _npLyricsPlainText } };
            _npLyricsPanel.Child = new Panel { Children = { _npLyricsSynced, _npLyricsPlain, _npLyricsMessage } };
            _npLyricsTimer.Tick += (_, _) => StepLyrics();
            _npLyricsSynced.SizeChanged += (_, _) => ScrollLyrics();
            _npOverlay.SizeChanged += (_, _) => LayoutLyrics();
            return _npLyricsPanel;
        }

        private void BuildLyricsDelayFlyout()
        {
            ToolTip.SetTip(_npLyricsDelaySlider, "Up shows the lyrics later, down shows them earlier");
            _npLyricsDelayFlyout.Content = new StackPanel
            {
                Spacing = 2, Margin = new Thickness(0, 2),
                Children =
                {
                    new TextBlock { Text = LyricsDelayText(LyricsDelayLimitMs), Classes = { "dim", "small" }, HorizontalAlignment = HorizontalAlignment.Center },
                    _npLyricsDelaySlider,
                    new TextBlock { Text = LyricsDelayText(-LyricsDelayLimitMs), Classes = { "dim", "small" }, HorizontalAlignment = HorizontalAlignment.Center },
                    _npLyricsDelayValue
                }
            };
            _npLyricsDelaySlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                int delayMs = (int)Math.Round(_npLyricsDelaySlider.Value * 1000);
                _npLyricsDelayValue.Text = LyricsDelayText(delayMs);
                _owner.SetLyricsDelay(delayMs, save: false);
                StepLyrics();
            };
            _npLyricsDelayFlyout.Opening += (_, _) =>
            {
                _npLyricsDelaySlider.Value = _owner.LyricsDelayMs / 1000.0;
                _npLyricsDelayValue.Text = LyricsDelayText(_owner.LyricsDelayMs);
            };
            _npLyricsDelayFlyout.Closed += (_, _) =>
            {
                _owner.SetLyricsDelay(_owner.LyricsDelayMs, save: true);
                WakeNowPlaying();
            };
            _npLyricsDelayButton.Classes.Add("pill");
            _npLyricsDelayButton.Flyout = _npLyricsDelayFlyout;
        }

        private static string LyricsDelayText(int delayMs) =>
            delayMs == 0 ? "0 s" : (delayMs / 1000.0).ToString("+0.0 s;−0.0 s", System.Globalization.CultureInfo.CurrentCulture);

        private bool LyricsOnly => _owner.NowPlayingStyle == NowPlayingStyle.Lyrics;

        private bool LyricsShown => _owner.NowPlayingLyrics || LyricsOnly;

        private void ApplyLyricsVisibility()
        {
            bool only = LyricsOnly, on = LyricsShown;
            _npLyricsButton.IsVisible = !only;
            _npLyricsDelayButton.IsVisible = on;
            if (_npLyricsDelayFlyout.IsOpen && !on) _npLyricsDelayFlyout.Hide();
            _npLyricsButton.Content = _owner.NowPlayingLyrics ? "Hide lyrics" : "Lyrics";
            ToolTip.SetTip(_npLyricsButton, _owner.NowPlayingLyrics
                ? "Hide the lyrics"
                : "Show lyrics that scroll with the song. The title and artist are sent to LRCLIB (lrclib.net) to find them.");
            _npLyricsPanel.IsVisible = on;
            _npLyricsPanel.HorizontalAlignment = only ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            _npLyricsPanel.Background = only ? Brushes.Transparent : LyricsPanelBackground;
            var alignment = only ? TextAlignment.Center : TextAlignment.Left;
            foreach (var line in _npLyricsLines.Children.OfType<TextBlock>()) StyleLyricLine(line);
            _npLyricsPlainText.FontSize = only ? 30 : 22;
            _npLyricsPlainText.LineHeight = only ? 48 : 36;
            _npLyricsMessage.FontSize = only ? 26 : 20;
            _npLyricsPlainText.TextAlignment = _npLyricsNote.TextAlignment = _npLyricsMessage.TextAlignment = alignment;
            LayoutLyrics();
            Dispatcher.UIThread.Post(ScrollLyrics, DispatcherPriority.Background);
            if (!on) ClearLyrics();
            else if (_npLyricsStatus != null) LoadLyrics(_npLyricsStatus);
        }

        private void LayoutLyrics()
        {
            bool only = LyricsOnly, beside = _npLyricsPanel.IsVisible && !only;
            double width = only ? Math.Clamp(_npOverlay.Bounds.Width * 0.7, 360, 1100) : Math.Clamp(_npOverlay.Bounds.Width * 0.36, 300, 640);
            double side = beside ? LyricsGroupSide(width) : 0;
            _npLyricsPanel.Width = width;
            _npLyricsPanel.Margin = only ? new Thickness(0, 76, 0, 40) : new Thickness(0, 76, 32 + side, 40);
            _npStageBox.Margin = new Thickness(side, 30, beside ? width + 32 + side : 0, 0);
            _npAmbient.Margin = new Thickness(side, 0, beside ? width + 40 + side : 0, 0);
        }

        private double LyricsGroupSide(double panelWidth)
        {
            bool ambient = IsAmbient(_owner.NowPlayingStyle);
            double contentWidth = ambient ? 1280 : _npStage.Width + ShadowRoom.Left + ShadowRoom.Right;
            double contentHeight = ambient ? 900 : _npStage.Height + ShadowRoom.Top + ShadowRoom.Bottom;
            double areaWidth = _npOverlay.Bounds.Width - panelWidth - (ambient ? 120 : 32);
            double areaHeight = _npOverlay.Bounds.Height - (ambient ? 88 : 30);
            if (areaWidth <= 0 || areaHeight <= 0 || double.IsNaN(contentWidth) || double.IsNaN(contentHeight)) return 0;
            double scale = Math.Min(areaWidth / contentWidth, areaHeight / contentHeight);
            return Math.Max(0, (areaWidth - contentWidth * scale) / 2);
        }

        private void UpdateLyricsTrack(DeviceStatus status)
        {
            _npLyricsStatus = status;
            if (LyricsShown && _npOverlay.IsVisible) LoadLyrics(status);
        }

        private void LoadLyrics(DeviceStatus status)
        {
            string key = $"{status.Title}\n{status.Artist}";
            if (key == _npLyricsKey) return;
            ClearLyrics();
            _npLyricsKey = key;
            if (status.Title.Trim().Length == 0)
            {
                ShowLyricsMessage("There's no song title to look up lyrics for.");
                return;
            }
            ShowLyricsMessage("Looking for lyrics…");
            var cancel = _npLyricsCancel = new CancellationTokenSource();
            _ = FetchLyricsAsync(status, key, cancel.Token);
        }

        private async Task FetchLyricsAsync(DeviceStatus status, string key, CancellationToken cancel)
        {
            for (int attempt = 0; ; attempt++)
            {
                Lyrics? lyrics;
                try
                {
                    lyrics = await _owner.GetLyricsAsync(status.Title.Trim(), status.Artist.Trim(), status.Album.Trim(), status.DurationMs / 1000, cancel);
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (key != _npLyricsKey) return;
                    string problem = e is LyricsServiceException { Busy: true } ? "The lyrics service is busy" : "Couldn't reach the lyrics service";
                    if (attempt >= LyricsAutoRetries)
                    {
                        ShowLyricsMessage($"{problem}. Hide and show the lyrics to try again.");
                        return;
                    }
                    ShowLyricsMessage($"{problem}, trying again shortly…");
                    try
                    {
                        await Task.Delay(LyricsRetryDelay, cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    ShowLyricsMessage("Looking for lyrics…");
                    continue;
                }
                if (cancel.IsCancellationRequested || key != _npLyricsKey) return;
                ShowLyrics(lyrics);
                return;
            }
        }

        private void ShowLyricsMessage(string message)
        {
            _npLyricsMessage.Text = message;
            _npLyricsMessage.IsVisible = true;
        }

        private void ShowLyrics(Lyrics? lyrics)
        {
            if (lyrics == null)
            {
                ShowLyricsMessage("No lyrics found for this song.");
                return;
            }
            if (lyrics.Synced.Count == 0 && lyrics.Plain.Length == 0)
            {
                ShowLyricsMessage("This song is instrumental.");
                return;
            }
            _npLyricsMessage.IsVisible = false;
            bool timed = _npIp != null && _positions.ContainsKey(_npIp);
            if (lyrics.Synced.Count > 0 && timed)
            {
                _npLyricLines = lyrics.Synced;
                foreach (var line in lyrics.Synced) _npLyricsLines.Children.Add(LyricText(line));
                _npLyricsSynced.IsVisible = true;
                _npLyricIndex = -1;
                _npLyricsTimer.Start();
                StepLyrics();
                Dispatcher.UIThread.Post(ScrollLyrics, DispatcherPriority.Background);
                return;
            }
            _npLyricsNote.Text = lyrics.Synced.Count == 0
                ? "These lyrics have no timing, so they can't scroll along with the song."
                : "This source doesn't report how far into the song it is, so the lyrics can't scroll along with it.";
            _npLyricsPlainText.Text = lyrics.Plain.Length > 0 ? lyrics.Plain : string.Join('\n', lyrics.Synced.Select(l => l.Text));
            _npLyricsPlain.Offset = default;
            _npLyricsPlain.IsVisible = true;
        }

        private TextBlock LyricText(LyricLine line)
        {
            var text = new TextBlock
            {
                Text = line.Text.Length > 0 ? line.Text : "…", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                Foreground = LyricDim, Cursor = new Cursor(StandardCursorType.Hand)
            };
            StyleLyricLine(text);
            ToolTip.SetTip(text, "Play from here");
            text.PointerPressed += async (_, e) =>
            {
                if (_npIp is not { } ip || _npControls?.Previous.IsEnabled != true) return;
                e.Handled = true;
                await SeekAsync(ip, line.TimeMs / 1000);
            };
            return text;
        }

        private void StyleLyricLine(TextBlock line)
        {
            bool only = LyricsOnly;
            line.FontSize = only ? 44 : 28;
            line.Margin = new Thickness(0, 0, 0, only ? 26 : 18);
            line.TextAlignment = only ? TextAlignment.Center : TextAlignment.Left;
        }

        private void StepLyrics()
        {
            if (_npIp is not { } ip || _npLyricLines.Count == 0 || !_positions.TryGetValue(ip, out var p)) return;
            int ms = p.PositionMs + (p.Playing ? (int)(DateTime.UtcNow - p.Fetched).TotalMilliseconds : 0) - _owner.LyricsDelayMs;
            int index = -1;
            for (int i = 0; i < _npLyricLines.Count && _npLyricLines[i].TimeMs <= ms + LyricsLeadMs; i++) index = i;
            if (index == _npLyricIndex) return;
            if (_npLyricIndex >= 0 && _npLyricIndex < _npLyricsLines.Children.Count) ((TextBlock)_npLyricsLines.Children[_npLyricIndex]).Foreground = LyricDim;
            _npLyricIndex = index;
            if (index >= 0) ((TextBlock)_npLyricsLines.Children[index]).Foreground = Brushes.White;
            ScrollLyrics();
        }

        private void ScrollLyrics()
        {
            if (_npLyricsLines.RenderTransform is not TranslateTransform shift) return;
            double anchor = _npLyricsSynced.Bounds.Height * 0.38;
            shift.Y = _npLyricIndex >= 0 && _npLyricIndex < _npLyricsLines.Children.Count
                ? anchor - (_npLyricsLines.Children[_npLyricIndex].Bounds.Y + _npLyricsLines.Children[_npLyricIndex].Bounds.Height / 2)
                : anchor - 20;
        }

        private void ClearLyrics()
        {
            _npLyricsCancel?.Cancel();
            _npLyricsCancel = null;
            _npLyricsTimer.Stop();
            _npLyricsKey = string.Empty;
            _npLyricLines = [];
            _npLyricIndex = -1;
            _npLyricsLines.Children.Clear();
            _npLyricsPlainText.Text = string.Empty;
            _npLyricsSynced.IsVisible = _npLyricsPlain.IsVisible = _npLyricsMessage.IsVisible = false;
        }
    }
}
