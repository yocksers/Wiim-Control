using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double GatefoldOpenSeconds = 0.9;
        private static readonly TimeSpan TrackChangeSettle = TimeSpan.FromMilliseconds(400);

        private readonly Border _npGatefold = new()
        {
            Width = StageCover, Height = StageCover, CornerRadius = new CornerRadius(0, 6, 6, 0), ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 12 48 0 #E0000000"), RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative)
        };
        private readonly Rotate3DTransform _npGatefoldOpen = new() { Depth = 1800 };
        private readonly FrameTimer _npGatefoldTimer = new();
        private readonly StackPanel _npTrackRows = new();
        private readonly ScrollViewer _npTrackScroll = new();
        private readonly TextBlock _npTrackCount = new() { FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#8CFFFFFF")), VerticalAlignment = VerticalAlignment.Bottom };
        private DateTime _npGatefoldStart;
        private QueueView _npTracks = QueueView.Empty;
        private string? _npTracksIp;
        private int _npTracksVersion;
        private string _npTracksKey = string.Empty;

        private void BuildGatefold()
        {
            _npGatefold.RenderTransform = _npGatefoldOpen;
            _npGatefold.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0B0B0C"), 0), new GradientStop(Color.Parse("#1A1B1E"), 0.05),
                    new GradientStop(Color.Parse("#202125"), 0.5), new GradientStop(Color.Parse("#1B1C1F"), 1)
                }
            };
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(_npTrackCount, Dock.Right);
            header.Children.Add(_npTrackCount);
            header.Children.Add(new TextBlock
            {
                Text = "TRACK LIST", FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = 3, Foreground = Brushes.White
            });
            _npTrackScroll.Content = _npTrackRows;
            var body = new DockPanel { Margin = new Thickness(40, 34, 30, 30) };
            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);
            body.Children.Add(_npTrackScroll);
            _npGatefold.Child = body;
            _npGatefoldTimer.Host = this;
            _npGatefoldTimer.Tick += (_, _) => StepGatefold();
        }

        private void OpenGatefold()
        {
            _npGatefoldStart = DateTime.UtcNow;
            _npGatefoldOpen.AngleY = 90;
            _npGatefold.Opacity = 0;
            _npGatefoldTimer.Start();
            _npTracksKey = string.Empty;
            _ = LoadTracklistAsync();
        }

        private void StepGatefold()
        {
            double progress = (DateTime.UtcNow - _npGatefoldStart).TotalSeconds / GatefoldOpenSeconds;
            if (progress >= 1 || !_npOverlay.IsVisible)
            {
                _npGatefoldTimer.Stop();
                _npGatefoldOpen.AngleY = 0;
                _npGatefold.Opacity = 1;
                return;
            }
            double eased = Ease(progress);
            _npGatefoldOpen.AngleY = 90 * (1 - eased);
            _npGatefold.Opacity = Math.Min(1, eased * 2);
        }

        private void UpdateGatefoldTrack(DeviceStatus status)
        {
            if (_owner.NowPlayingStyle != NowPlayingStyle.Gatefold || !_npOverlay.IsVisible) return;
            string key = $"{status.Title}\n{status.Artist}\n{status.Album}";
            if (key == _npTracksKey) return;
            bool first = _npTracksKey.Length == 0;
            _npTracksKey = key;
            if (!first) _ = LoadTracklistAsync(TrackChangeSettle);
        }

        private async Task LoadTracklistAsync(TimeSpan? delay = null)
        {
            if (_npIp is not { } shown) return;
            int version = ++_npTracksVersion;
            if (delay is { } wait) await Task.Delay(wait);
            string ip = TargetOf(shown);
            if (version != _npTracksVersion) return;
            if (ip == _npTracksIp && _npTracks.Kind != QueueKind.None)
            {
                var state = await _owner.GetQueueStateAsync(ip);
                if (version != _npTracksVersion) return;
                if (state.Kind == _npTracks.Kind && state.Signature == _npTracks.Signature)
                {
                    if (state.Current != _npTracks.Current)
                    {
                        _npTracks = _npTracks with { Current = state.Current };
                        RenderTracklist();
                    }
                    return;
                }
            }
            else if (ip != _npTracksIp)
            {
                _npTrackRows.Children.Clear();
                _npTrackCount.Text = string.Empty;
                _npTrackRows.Children.Add(TracklistMessage("Loading…"));
            }
            var view = await _owner.GetQueueAsync(ip);
            if (version != _npTracksVersion || _npIp != shown) return;
            _npTracksIp = ip;
            _npTracks = view;
            RenderTracklist();
        }

        private static TextBlock TracklistMessage(string text) => new()
        {
            Text = text, FontSize = 16, Foreground = new SolidColorBrush(Color.Parse("#A0FFFFFF")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        };

        private void RenderTracklist()
        {
            _npTrackRows.Children.Clear();
            var view = _npTracks;
            if (view.Entries.Count == 0)
            {
                _npTrackCount.Text = string.Empty;
                _npTrackRows.Children.Add(TracklistMessage("There's no track list for what's playing right now."));
                return;
            }
            _npTrackCount.Text = view.Entries.Count == 1 ? "1 track" : $"{view.Entries.Count} tracks";
            bool locked = _npIp != null && _owner.IsDeviceLocked(_npIp);
            Control? current = null;
            for (int i = 0; i < view.Entries.Count; i++)
            {
                var row = TrackRow(view, i, locked);
                if (i == view.Current) current = row;
                _npTrackRows.Children.Add(row);
            }
            if (current != null) Dispatcher.UIThread.Post(() => current.BringIntoView(), DispatcherPriority.Background);
        }

        private Control TrackRow(QueueView view, int index, bool locked)
        {
            var entry = view.Entries[index];
            bool playing = index == view.Current;
            var white = new SolidColorBrush(Color.Parse(playing ? "#FFFFFFFF" : "#E6FFFFFF"));
            var dim = new SolidColorBrush(Color.Parse("#8CFFFFFF"));
            Control number = playing
                ? new PathIcon { Data = Icons.Play, Width = 14, Height = 14, Foreground = Palette.AccentText, HorizontalAlignment = HorizontalAlignment.Left }
                : new TextBlock { Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 15, Foreground = dim };
            number.VerticalAlignment = VerticalAlignment.Center;
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = entry.Title, FontSize = 18, FontWeight = playing ? FontWeight.Bold : FontWeight.Normal, Foreground = white,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            if (entry.Artist.Length > 0)
                text.Children.Add(new TextBlock { Text = entry.Artist, FontSize = 14, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis });
            var length = new TextBlock
            {
                Text = entry.Duration > TimeSpan.Zero ? FormatTime((int)entry.Duration.TotalSeconds) : string.Empty, FontSize = 14, Foreground = dim,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto") };
            grid.Children.Add(number);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(length, 2);
            grid.Children.Add(length);
            IBrush idle = playing ? new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)) : Brushes.Transparent;
            var row = new Border
            {
                Child = grid, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(6), Background = idle, Margin = new Thickness(0, 0, 10, 2)
            };
            if (locked || playing) return row;
            row.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(row, "Play this track");
            row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            row.PointerExited += (_, _) => row.Background = idle;
            row.Tapped += async (_, _) =>
            {
                if (_npIp is not { } ip) return;
                if (!await _owner.QueueJumpAsync(TargetOf(ip), view, index)) return;
                await Task.Delay(600);
                await RefreshDeviceStatusesAsync();
                _ = LoadTracklistAsync();
            };
            return row;
        }
    }
}
