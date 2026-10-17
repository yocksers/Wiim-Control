using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboQueueAmp = new() { Width = 200 };
        private readonly TextBlock _lblQueueStatus = Subtitle();
        private readonly StackPanel _queueRows = new();
        private readonly Button _btnQueueClear = Pill("Clear up next");
        private readonly DispatcherTimer _queueTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly List<Bitmap> _queueBitmaps = [];
        private readonly Dictionary<string, Task<Bitmap?>> _queueArt = new();
        private QueueView _queue = QueueView.Empty;
        private string _queueIp = string.Empty;
        private int _queueVersion;
        private bool _queueBusy;

        private Control BuildQueuePage()
        {
            ToolTip.SetTip(_cboQueueAmp, "The device whose queue is shown");
            ToolTip.SetTip(_btnQueueClear, "Remove all tracks after the one that's playing");
            _btnQueueClear.IsEnabled = false;
            _btnQueueClear.Click += async (_, _) => await ClearUpNextAsync();
            _cboQueueAmp.SelectionChanged += async (_, _) => await ReloadQueueAsync(force: true);
            _queueTimer.Tick += async (_, _) => await ReloadQueueAsync(force: false);
            var page = Page("Queue", _lblQueueStatus, [_cboQueueAmp, _btnQueueClear], _queueRows);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (page.IsVisible)
                {
                    _queueTimer.Start();
                    await ReloadQueueAsync(force: true);
                }
                else _queueTimer.Stop();
            };
            Closed += (_, _) =>
            {
                _queueTimer.Stop();
                ClearQueueRows();
            };
            return page;
        }

        private void ClearQueueRows()
        {
            _queueVersion++;
            _queueRows.Children.Clear();
            foreach (var bitmap in _queueBitmaps) bitmap.Dispose();
            _queueBitmaps.Clear();
            _queueArt.Clear();
        }

        private async Task ReloadQueueAsync(bool force)
        {
            if (_queueBusy && !force) return;
            if (AmpOf(_cboQueueAmp) is not { } amp)
            {
                ClearQueueRows();
                _queueRows.Children.Add(Muted("Add a WiiM device on the Devices page first."));
                return;
            }
            string ip = TargetOf(amp);
            _queueBusy = true;
            try
            {
                if (!force && ip == _queueIp)
                {
                    var state = await _owner.GetQueueStateAsync(ip);
                    if (state.Signature == _queue.Signature && state.Kind == _queue.Kind)
                    {
                        if (state.Current != _queue.Current)
                        {
                            _queue = _queue with { Current = state.Current };
                            RenderQueue();
                        }
                        return;
                    }
                }
                if (force) _lblQueueStatus.Text = "Loading…";
                var view = await _owner.GetQueueAsync(ip);
                if (AmpOf(_cboQueueAmp) is not { } now || TargetOf(now) != ip) return;
                bool changed = ip != _queueIp || view.Entries.Count != _queue.Entries.Count || view.Kind != _queue.Kind;
                _queueIp = ip;
                _queue = view;
                RenderQueue(changed);
            }
            finally
            {
                _queueBusy = false;
            }
        }

        private void RenderQueue(bool resetScroll = false)
        {
            ClearQueueRows();
            var view = _queue;
            int count = view.Entries.Count;
            _btnQueueClear.IsEnabled = view.Kind is not (QueueKind.None or QueueKind.Roon) && view.Current >= 0 && view.Current < count - 1;
            if (view.Kind == QueueKind.None || count == 0)
            {
                _lblQueueStatus.Text = string.Empty;
                _queueRows.Children.Add(Muted("What the device plays now has no queue. Play an album or folder from the Library page, a Roon album, or a " +
                                              "Lyrion playlist, to see and change what plays next. Spotify, AirPlay and the inputs have no queue here."));
                return;
            }
            string source = view.Kind is QueueKind.Lyrion or QueueKind.Roon ? $" · {view.Name}" : string.Empty;
            _lblQueueStatus.Text = (count == 1 ? "1 track" : $"{count} tracks") + source;

            if (view.Current > 0)
            {
                _queueRows.Children.Add(QueueSection("Played"));
                for (int i = 0; i < view.Current && i < count; i++) _queueRows.Children.Add(QueueRow(view, i));
            }
            if (view.Current >= 0 && view.Current < count)
            {
                _queueRows.Children.Add(QueueSection("Now playing"));
                _queueRows.Children.Add(QueueRow(view, view.Current));
            }
            if (view.Current < count - 1)
            {
                _queueRows.Children.Add(QueueSection("Up next"));
                for (int i = Math.Max(0, view.Current + 1); i < count; i++) _queueRows.Children.Add(QueueRow(view, i));
            }
            _queueRows.Children.Add(Muted(view.Kind == QueueKind.Roon
                ? "Click a track to play it. Roon doesn't let other apps reorder or remove tracks, so change the queue in Roon."
                : "Click a track to play it. Use the arrows to change the order, or ✕ to remove a track."));
            if (resetScroll && _queueRows.FindAncestorOfType<ScrollViewer>() is { } scroll) scroll.Offset = default;
        }

        private static TextBlock QueueSection(string text) => new()
        {
            Text = text.ToUpperInvariant(), Classes = { "small", "muted" }, Margin = new Thickness(8, 14, 0, 4)
        };

        private Control QueueRow(QueueView view, int index)
        {
            var entry = view.Entries[index];
            bool current = index == view.Current, played = index < view.Current, upcoming = index > view.Current;
            var icon = new PathIcon
            {
                Data = Icons.Music, Width = 20, Height = 20, Foreground = Palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var art = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
            var picture = new Border
            {
                Width = current ? 56 : 40, Height = current ? 56 : 40, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = Palette.Input, Child = new Panel { Children = { icon, art } }
            };
            var number = new TextBlock
            {
                Text = (index + 1).ToString(CultureInfo.InvariantCulture), Classes = { "muted" }, Width = 34, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
            };
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = entry.Title.Length > 0 ? entry.Title : "(untitled)", TextTrimming = TextTrimming.CharacterEllipsis,
                        FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal
                    }
                }
            };
            string detail = string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(s => s.Length > 0));
            if (detail.Length > 0)
                text.Children.Add(new TextBlock { Text = detail, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });

            var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            if (entry.Duration > TimeSpan.Zero)
                end.Children.Add(new TextBlock
                {
                    Text = FormatTime((int)entry.Duration.TotalSeconds), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });
            if (upcoming && view.Kind != QueueKind.Roon)
            {
                end.Children.Add(QueueButton(Icons.ArrowUp, "Move up", index > view.Current + 1, () => _owner.QueueMoveAsync(_queueIp, view, index, index - 1)));
                end.Children.Add(QueueButton(Icons.ArrowDown, "Move down", index < view.Entries.Count - 1, () => _owner.QueueMoveAsync(_queueIp, view, index, index + 1)));
                end.Children.Add(QueueButton(Icons.Close, "Remove from the queue", true, () => _owner.QueueRemoveAsync(_queueIp, view, index, index)));
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            grid.Children.Add(number);
            Grid.SetColumn(picture, 1);
            grid.Children.Add(picture);
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            Grid.SetColumn(end, 3);
            grid.Children.Add(end);

            var row = new Border
            {
                Child = grid, Padding = new Thickness(6, current ? 8 : 4), CornerRadius = new CornerRadius(8),
                Background = current ? Palette.Card : Brushes.Transparent, Opacity = played ? 0.6 : 1,
                BorderBrush = current ? Palette.Accent : null, BorderThickness = new Thickness(current ? 1.5 : 0)
            };
            if (!current)
            {
                row.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(row, $"Play {entry.Title}");
                row.PointerEntered += (_, _) => row.Background = Palette.Card;
                row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
                row.Tapped += async (_, e) =>
                {
                    if (e.Source is Visual v && (v as Button ?? v.FindAncestorOfType<Button>()) != null) return;
                    await QueueActionAsync(() => _owner.QueueJumpAsync(_queueIp, view, index));
                };
            }
            if (entry.ArtUrl.Length > 0) _ = LoadQueueArtAsync(entry.ArtUrl, art, icon, _queueVersion);
            return row;
        }

        private Button QueueButton(StreamGeometry geometry, string tip, bool enabled, Func<Task<bool>> action)
        {
            var (button, _) = IconButton(geometry);
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1 : 0.25;
            ToolTip.SetTip(button, tip);
            button.Click += async (_, _) => await QueueActionAsync(action);
            return button;
        }

        private async Task QueueActionAsync(Func<Task<bool>> action)
        {
            if (_queueBusy) return;
            _queueBusy = true;
            bool ok;
            try { ok = await action(); }
            finally { _queueBusy = false; }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the change to the queue.", _scale);
            await Task.Delay(400);
            await ReloadQueueAsync(force: true);
        }

        private async Task ClearUpNextAsync()
        {
            var view = _queue;
            if (view.Kind == QueueKind.None || view.Current < 0 || view.Current >= view.Entries.Count - 1) return;
            int upcoming = view.Entries.Count - view.Current - 1;
            if (!await Dialogs.ConfirmAsync(this, upcoming == 1 ? "Remove the next track from the queue?" : $"Remove the {upcoming} tracks after the one that's playing?", _scale))
                return;
            await QueueActionAsync(() => _owner.QueueRemoveAsync(_queueIp, view, view.Current + 1, view.Entries.Count - 1));
        }

        private async Task LoadQueueArtAsync(string url, Image art, PathIcon icon, int version)
        {
            if (!_queueArt.TryGetValue(url, out var download))
            {
                download = _owner.LoadThumbnailAsync(url, 112, () => version == _queueVersion);
                _queueArt[url] = download;
                _ = KeepQueueArtAsync(download, version);
            }
            var bitmap = await download;
            if (bitmap == null || version != _queueVersion) return;
            art.Source = bitmap;
            art.IsVisible = true;
            icon.IsVisible = false;
        }

        private async Task KeepQueueArtAsync(Task<Bitmap?> download, int version)
        {
            var bitmap = await download;
            if (bitmap == null) return;
            if (version == _queueVersion) _queueBitmaps.Add(bitmap);
            else bitmap.Dispose();
        }
    }
}
