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
        private sealed class QueuePage(SettingsWindow window)
        {
            private readonly ComboBox _amp = new() { Width = 200 };
            private readonly TextBlock _status = Subtitle();
            private readonly StackPanel _rows = new();
            private readonly Button _clear = Pill("Clear up next");
            private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
            private readonly List<Bitmap> _bitmaps = [];
            private readonly Dictionary<string, Task<Bitmap?>> _art = new();
            private QueueView _view = QueueView.Empty;
            private string _ip = string.Empty;
            private int _version;
            private bool _busy;

            public void FillDevices() => window.FillAmpCombo(_amp);

            public Control Build()
            {
                ToolTip.SetTip(_amp, "The device whose queue is shown");
                ToolTip.SetTip(_clear, "Remove all tracks after the one that's playing");
                _clear.IsEnabled = false;
                _clear.Click += async (_, _) => await ClearUpNextAsync();
                _amp.SelectionChanged += async (_, _) => await ReloadAsync(force: true);
                _timer.Tick += async (_, _) => await ReloadAsync(force: false);
                var page = window.Page("Queue", _status, [_amp, _clear], _rows);
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property != IsVisibleProperty) return;
                    if (page.IsVisible)
                    {
                        _timer.Start();
                        await ReloadAsync(force: true);
                    }
                    else _timer.Stop();
                };
                window.Closed += (_, _) =>
                {
                    _timer.Stop();
                    ClearRows();
                };
                return page;
            }

            private void ClearRows()
            {
                _version++;
                _rows.Children.Clear();
                foreach (var bitmap in _bitmaps) bitmap.Dispose();
                _bitmaps.Clear();
                _art.Clear();
            }

            private async Task ReloadAsync(bool force)
            {
                if (_busy && !force) return;
                if (AmpOf(_amp) is not { } amp)
                {
                    ClearRows();
                    _rows.Children.Add(Muted("Add a WiiM device on the Devices page first."));
                    return;
                }
                string ip = window.TargetOf(amp);
                _busy = true;
                try
                {
                    if (!force && ip == _ip)
                    {
                        var state = await window._owner.GetQueueStateAsync(ip);
                        if (state.Signature == _view.Signature && state.Kind == _view.Kind)
                        {
                            if (state.Current != _view.Current)
                            {
                                _view = _view with { Current = state.Current };
                                Render();
                            }
                            return;
                        }
                    }
                    if (force) _status.Text = "Loading…";
                    var view = await window._owner.GetQueueAsync(ip);
                    if (AmpOf(_amp) is not { } now || window.TargetOf(now) != ip) return;
                    bool changed = ip != _ip || view.Entries.Count != _view.Entries.Count || view.Kind != _view.Kind;
                    _ip = ip;
                    _view = view;
                    Render(changed);
                }
                finally
                {
                    _busy = false;
                }
            }

            private void Render(bool resetScroll = false)
            {
                ClearRows();
                var view = _view;
                int count = view.Entries.Count;
                _clear.IsEnabled = view.Kind is not (QueueKind.None or QueueKind.Roon) && view.Current >= 0 && view.Current < count - 1;
                if (view.Kind == QueueKind.None || count == 0)
                {
                    _status.Text = string.Empty;
                    _rows.Children.Add(Muted("What the device plays now has no queue. Play an album or folder from the Library page, a Roon album, or a " +
                                                  "Lyrion playlist, to see and change what plays next. Spotify, AirPlay and the inputs have no queue here."));
                    return;
                }
                string source = view.Kind is QueueKind.Lyrion or QueueKind.Roon ? $" · {view.Name}" : string.Empty;
                _status.Text = (count == 1 ? "1 track" : $"{count} tracks") + source;

                if (view.Current > 0)
                {
                    _rows.Children.Add(Section("Played"));
                    for (int i = 0; i < view.Current && i < count; i++) _rows.Children.Add(Row(view, i));
                }
                if (view.Current >= 0 && view.Current < count)
                {
                    _rows.Children.Add(Section("Now playing"));
                    _rows.Children.Add(Row(view, view.Current));
                }
                if (view.Current < count - 1)
                {
                    _rows.Children.Add(Section("Up next"));
                    for (int i = Math.Max(0, view.Current + 1); i < count; i++) _rows.Children.Add(Row(view, i));
                }
                _rows.Children.Add(Muted(view.Kind == QueueKind.Roon
                    ? "Click a track to play it. Roon doesn't let other apps reorder or remove tracks, so change the queue in Roon."
                    : "Click a track to play it. Use the arrows to change the order, or ✕ to remove a track."));
                if (resetScroll && _rows.FindAncestorOfType<ScrollViewer>() is { } scroll) scroll.Offset = default;
            }

            private static TextBlock Section(string text) => new()
            {
                Text = text.ToUpperInvariant(), Classes = { "small", "muted" }, Margin = new Thickness(8, 14, 0, 4)
            };

            private Control Row(QueueView view, int index)
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
                    end.Children.Add(ActionButton(Icons.ArrowUp, "Move up", index > view.Current + 1, () => window._owner.QueueMoveAsync(_ip, view, index, index - 1)));
                    end.Children.Add(ActionButton(Icons.ArrowDown, "Move down", index < view.Entries.Count - 1, () => window._owner.QueueMoveAsync(_ip, view, index, index + 1)));
                    end.Children.Add(ActionButton(Icons.Close, "Remove from the queue", true, () => window._owner.QueueRemoveAsync(_ip, view, index, index)));
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
                        await RunAsync(() => window._owner.QueueJumpAsync(_ip, view, index));
                    };
                }
                if (entry.ArtUrl.Length > 0) _ = LoadArtAsync(entry.ArtUrl, art, icon, _version);
                return row;
            }

            private Button ActionButton(StreamGeometry geometry, string tip, bool enabled, Func<Task<bool>> action)
            {
                var (button, _) = IconButton(geometry);
                button.IsEnabled = enabled;
                button.Opacity = enabled ? 1 : 0.25;
                ToolTip.SetTip(button, tip);
                button.Click += async (_, _) => await RunAsync(action);
                return button;
            }

            private async Task RunAsync(Func<Task<bool>> action)
            {
                if (_busy) return;
                _busy = true;
                bool ok;
                try { ok = await action(); }
                finally { _busy = false; }
                if (!ok) await Dialogs.MessageAsync(window, "The device didn't accept the change to the queue.", window._scale);
                await Task.Delay(400);
                await ReloadAsync(force: true);
            }

            private async Task ClearUpNextAsync()
            {
                var view = _view;
                if (view.Kind == QueueKind.None || view.Current < 0 || view.Current >= view.Entries.Count - 1) return;
                int upcoming = view.Entries.Count - view.Current - 1;
                if (!await Dialogs.ConfirmAsync(window, upcoming == 1 ? "Remove the next track from the queue?" : $"Remove the {upcoming} tracks after the one that's playing?", window._scale))
                    return;
                await RunAsync(() => window._owner.QueueRemoveAsync(_ip, view, view.Current + 1, view.Entries.Count - 1));
            }

            private async Task LoadArtAsync(string url, Image art, PathIcon icon, int version)
            {
                if (!_art.TryGetValue(url, out var download))
                {
                    download = window._owner.LoadThumbnailAsync(url, 112, () => version == _version);
                    _art[url] = download;
                    _ = KeepArtAsync(download, version);
                }
                var bitmap = await download;
                if (bitmap == null || version != _version) return;
                art.Source = bitmap;
                art.IsVisible = true;
                icon.IsVisible = false;
            }

            private async Task KeepArtAsync(Task<Bitmap?> download, int version)
            {
                var bitmap = await download;
                if (bitmap == null) return;
                if (version == _version) _bitmaps.Add(bitmap);
                else bitmap.Dispose();
            }
        }
    }
}
