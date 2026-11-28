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
        private sealed partial class SpotifyPage
        {
            private static readonly TimeSpan PlayerPoll = TimeSpan.FromSeconds(3);
            private static readonly TimeSpan AfterCommand = TimeSpan.FromMilliseconds(700);
            private const double BarArt = 48;

            private readonly DispatcherTimer _playerTimer = new() { Interval = PlayerPoll };
            private SpotifyPlayerState? _player;
            private bool? _pendingShuffle;
            private bool _pageShown;
            private int _playerVersion;
            private readonly List<(string Uri, TextBlock Title)> _rowTitles = [];
            private (SpotifyPlayback Context, PathIcon Play, PathIcon Shuffle)? _detailActions;

            private readonly Border _playerBar = new()
            {
                Background = Palette.Card, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Margin = new Thickness(0, 12, 12, 0), IsVisible = false
            };
            private readonly Image _barArt = new() { Stretch = Stretch.UniformToFill };
            private string _barArtUrl = string.Empty;
            private Bitmap? _barBitmap;
            private readonly TextBlock _barTitle = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly TextBlock _barArtist = new() { Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
            private PathIcon _barPlay = new();
            private PathIcon _barShuffle = new();
            private PathIcon _barRepeat = new();

            private Control BuildPlayerBar()
            {
                _playerTimer.Tick += async (_, _) => await RefreshPlayerAsync();
                var art = new Border
                {
                    Width = BarArt, Height = BarArt, CornerRadius = new CornerRadius(4), ClipToBounds = true, Background = Palette.Input, Child = _barArt
                };
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), Children = { _barTitle, _barArtist } };
                var (shuffle, shuffleIcon) = BarButton(Icons.Shuffle, "Shuffle", ToggleShuffleAsync);
                var (previous, _) = BarButton(Icons.Previous, "Previous", () => CommandAsync(window._owner.SpotifyPreviousAsync));
                var play = PlayCircle(38, TogglePlayingAsync, light: true);
                var (next, _) = BarButton(Icons.Next, "Next", () => CommandAsync(window._owner.SpotifyNextAsync));
                var (repeat, repeatIcon) = BarButton(Icons.Repeat, "Repeat", CycleRepeatAsync);
                _barShuffle = shuffleIcon;
                _barRepeat = repeatIcon;
                _barPlay = (PathIcon)play.Child!;
                var controls = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { shuffle, previous, play, next, repeat }
                };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { art, words, controls } };
                Grid.SetColumn(words, 1);
                Grid.SetColumn(controls, 2);
                _playerBar.Child = grid;
                return _playerBar;
            }

            private static (Button Button, PathIcon Icon) BarButton(StreamGeometry icon, string tip, Func<Task> click)
            {
                var (button, path) = IconButton(icon);
                ToolTip.SetTip(button, tip);
                button.Click += async (_, _) => await click();
                return (button, path);
            }

            private void UpdatePolling(bool shown)
            {
                _pageShown = shown;
                bool run = shown && window._owner.SpotifyEnabled && window._owner.SpotifyLinked;
                if (run && !_playerTimer.IsEnabled)
                {
                    _playerTimer.Start();
                    _ = RefreshPlayerAsync();
                }
                else if (!run)
                {
                    _playerTimer.Stop();
                    _playerBar.IsVisible = false;
                }
            }

            private async Task RefreshPlayerAsync()
            {
                int version = ++_playerVersion;
                var player = await window._owner.SpotifyPlayerAsync();
                if (version != _playerVersion || player == null) return;
                _player = player;
                ApplyPlayer();
            }

            private void RefreshPlayerSoon() => DispatcherTimer.RunOnce(() => _ = RefreshPlayerAsync(), AfterCommand);

            private bool IsCurrent(SpotifyPlayback context) =>
                _player?.Item is { } item && (context.ContextUri != null ? _player.ContextUri == context.ContextUri : context.Uris?.Contains(item.Uri) == true);

            private bool ShuffleOn => _pendingShuffle ?? _player?.Shuffle ?? false;

            private void ApplyPlayer()
            {
                var player = _player;
                var item = player?.Item;
                _playerBar.IsVisible = _pageShown && item != null;
                if (item != null && player != null)
                {
                    _barTitle.Text = item.Name;
                    _barArtist.Text = string.Join("  ·  ", new[] { item.Subtitle, player.DeviceName.Length > 0 ? $"on {player.DeviceName}" : string.Empty }.Where(s => s.Length > 0));
                    _barPlay.Data = player.Playing ? Icons.Pause : Icons.Play;
                    _barRepeat.Data = player.Repeat == "track" ? Icons.RepeatOnce : Icons.Repeat;
                    _barRepeat.Foreground = player.Repeat == "off" ? Palette.Text : Green;
                    if (item.ImageUrl != _barArtUrl) _ = LoadBarArtAsync(item.ImageUrl);
                }
                _barShuffle.Foreground = ShuffleOn ? Green : Palette.Text;
                foreach (var (uri, title) in _rowTitles) title.Foreground = item?.Uri == uri ? Green : Palette.Text;
                if (_detailActions is var (context, play, shuffle))
                {
                    play.Data = IsCurrent(context) && player?.Playing == true ? Icons.Pause : Icons.Play;
                    shuffle.Foreground = ShuffleOn ? Green : Palette.TextMuted;
                }
            }

            private async Task LoadBarArtAsync(string url)
            {
                _barArtUrl = url;
                var bitmap = url.Length > 0 ? await window._owner.LoadThumbnailAsync(url, (int)(BarArt * 2), () => _barArtUrl == url) : null;
                if (_barArtUrl != url)
                {
                    bitmap?.Dispose();
                    return;
                }
                var old = _barBitmap;
                _barBitmap = bitmap;
                _barArt.Source = bitmap;
                old?.Dispose();
            }

            private void ClearPlayer()
            {
                _playerTimer.Stop();
                _barArt.Source = null;
                _barBitmap?.Dispose();
                _barBitmap = null;
                _barArtUrl = string.Empty;
            }

            private async Task CommandAsync(Func<Task<string?>> command, string? done = null)
            {
                string? problem = await command();
                if (problem != null)
                {
                    await Dialogs.MessageAsync(window, problem, window._scale);
                    return;
                }
                if (done != null) _status.Text = done;
                RefreshPlayerSoon();
            }

            private async Task TogglePlayingAsync()
            {
                if (_player?.Item == null) return;
                await CommandAsync(_player.Playing ? window._owner.SpotifyPauseAsync : window._owner.SpotifyResumeAsync);
            }

            private async Task ToggleShuffleAsync()
            {
                bool wanted = !ShuffleOn;
                if (_player?.Item == null)
                {
                    _pendingShuffle = wanted;
                    ApplyPlayer();
                    return;
                }
                _pendingShuffle = null;
                await CommandAsync(() => window._owner.SpotifyShuffleAsync(wanted));
            }

            private async Task CycleRepeatAsync()
            {
                string next = (_player?.Repeat ?? "off") switch { "off" => "context", "context" => "track", _ => "off" };
                await CommandAsync(() => window._owner.SpotifyRepeatAsync(next));
            }

            private async Task AfterPlayStartedAsync()
            {
                if (_pendingShuffle is { } shuffle)
                {
                    _pendingShuffle = null;
                    await window._owner.SpotifyShuffleAsync(shuffle);
                }
                RefreshPlayerSoon();
            }

            private async Task PlayOrToggleAsync(SpotifyPlayback context, string what)
            {
                if (IsCurrent(context) && _player != null)
                {
                    await CommandAsync(_player.Playing ? window._owner.SpotifyPauseAsync : window._owner.SpotifyResumeAsync);
                    return;
                }
                await PlayAsync(context, what);
            }

            private async Task PlayTrackOrToggleAsync(SpotifyItem track, SpotifyPlayback playback)
            {
                if (_player?.Item?.Uri == track.Uri)
                {
                    await CommandAsync(_player.Playing ? window._owner.SpotifyPauseAsync : window._owner.SpotifyResumeAsync);
                    return;
                }
                await PlayAsync(playback, track.Name);
            }

            private Control ActionRow(SpotifyItem shown, SpotifyPlayback context, SpotifyDetail detail)
            {
                var play = PlayCircle(56, () => PlayOrToggleAsync(context, shown.Name));
                var (shuffle, shuffleIcon) = IconButton(Icons.Shuffle);
                shuffleIcon.Width = shuffleIcon.Height = 30;
                ToolTip.SetTip(shuffle, "Shuffle");
                shuffle.Click += async (_, _) => await ToggleShuffleAsync();
                var (more, moreIcon) = IconButton(Icons.More);
                moreIcon.Width = moreIcon.Height = 28;
                ToolTip.SetTip(more, $"More options for {shown.Name}");
                more.Click += async (_, _) => await ShowContextMenuAsync(more, shown, detail);
                _detailActions = (context, (PathIcon)play.Child!, shuffleIcon);
                ApplyPlayer();
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 0, 0, 20), Children = { play, shuffle, more }
                };
            }

            private Button MoreButton(SpotifyItem track, bool inAlbum)
            {
                var (more, icon) = IconButton(Icons.More);
                icon.Width = icon.Height = 18;
                icon.Foreground = Palette.TextMuted;
                more.VerticalAlignment = VerticalAlignment.Center;
                more.HorizontalAlignment = HorizontalAlignment.Right;
                ToolTip.SetTip(more, $"More options for {track.Name}");
                more.Tapped += (_, e) => e.Handled = true;
                more.Click += async (_, _) => await ShowTrackMenuAsync(more, track, inAlbum);
                return more;
            }

            private async Task ShowTrackMenuAsync(Control anchor, SpotifyItem track, bool inAlbum)
            {
                bool episode = track.Kind == "episode";
                bool saved = await window._owner.SpotifySavedAsync(track.Uri) == true;
                var choices = new List<DropdownChoice>();
                if (!episode) choices.Add(DropdownAction("Add to playlist…", () => ShowPlaylistPickerAsync(anchor, [track.Uri], track.Name)));
                choices.Add(DropdownAction(episode ? saved ? "Remove from Your Episodes" : "Save to Your Episodes" : saved ? "Remove from Liked Songs" : "Add to Liked Songs",
                    () => CommandAsync(() => window._owner.SpotifySaveAsync(track.Uri, !saved),
                        saved ? $"Removed {track.Name} from {(episode ? "Your Episodes" : "Liked Songs")}" : $"Added {track.Name} to {(episode ? "Your Episodes" : "Liked Songs")}")));
                choices.Add(DropdownAction("Add to queue", () => CommandAsync(() => window._owner.SpotifyQueueAsync([track.Uri]), $"Added {track.Name} to the queue")));
                if (track.ArtistId.Length > 0)
                    choices.Add(DropdownAction("Go to artist", () => OpenAsync(new SpotifyItem("artist", track.ArtistId, $"spotify:artist:{track.ArtistId}", track.ArtistName, "Artist", string.Empty))));
                if (!inAlbum && track.AlbumId.Length > 0)
                    choices.Add(DropdownAction("Go to album", () => OpenAsync(new SpotifyItem("album", track.AlbumId, $"spotify:album:{track.AlbumId}", track.Album, string.Empty, string.Empty))));
                choices.Add(DropdownAction(episode ? "Copy episode link" : "Copy song link", () => CopyLinkAsync(track)));
                window.ShowDropdown(anchor, choices);
            }

            private async Task ShowContextMenuAsync(Control anchor, SpotifyItem shown, SpotifyDetail detail)
            {
                var choices = new List<DropdownChoice>();
                var uris = detail.Tracks.Select(t => t.Uri).ToList();
                if (uris.Count > 0)
                    choices.Add(DropdownAction("Add to queue", () => CommandAsync(() => window._owner.SpotifyQueueAsync(uris), $"Added {shown.Name} to the queue")));
                if (shown.Kind is "album" && uris.Count > 0)
                    choices.Add(DropdownAction("Add to playlist…", () => ShowPlaylistPickerAsync(anchor, uris, shown.Name)));
                if (shown.Kind is "album" or "playlist" or "show")
                {
                    bool saved = await window._owner.SpotifySavedAsync(shown.Uri) == true;
                    choices.Add(DropdownAction(saved ? "Remove from Your Library" : "Save to Your Library",
                        () => CommandAsync(() => window._owner.SpotifySaveAsync(shown.Uri, !saved),
                            saved ? $"Removed {shown.Name} from Your Library" : $"Saved {shown.Name} to Your Library")));
                }
                if (shown.Kind == "album" && shown.ArtistId.Length > 0)
                    choices.Add(DropdownAction("Go to artist", () => OpenAsync(new SpotifyItem("artist", shown.ArtistId, $"spotify:artist:{shown.ArtistId}", shown.ArtistName, "Artist", string.Empty))));
                choices.Add(DropdownAction("Copy link", () => CopyLinkAsync(shown)));
                window.ShowDropdown(anchor, choices);
            }

            private async Task ShowPlaylistPickerAsync(Control anchor, IReadOnlyList<string> uris, string what)
            {
                _status.Text = "Loading your playlists…";
                var playlists = await window._owner.SpotifyEditablePlaylistsAsync();
                _status.Text = string.Empty;
                if (playlists == null)
                {
                    await Dialogs.MessageAsync(window, "Spotify didn't answer. Check your internet connection and try again.", window._scale);
                    return;
                }
                if (playlists.Count == 0)
                {
                    await Dialogs.MessageAsync(window, "You don't have any playlists of your own to add to yet. Make one in the Spotify app first.", window._scale);
                    return;
                }
                var choices = new List<DropdownChoice> { DropdownHeading("Add to playlist") };
                foreach (var playlist in playlists)
                    choices.Add(DropdownAction(playlist.Name, () => CommandAsync(() => window._owner.SpotifyAddToPlaylistAsync(playlist.Id, uris), $"Added {what} to {playlist.Name}")));
                if (anchor.GetVisualRoot() != null) window.ShowDropdown(anchor, choices);
            }

            private async Task CopyLinkAsync(SpotifyItem item)
            {
                if (TopLevel.GetTopLevel(window)?.Clipboard is not { } clipboard) return;
                await clipboard.SetTextAsync(SpotifyLink(item));
                _status.Text = "Link copied";
            }
        }
    }
}
