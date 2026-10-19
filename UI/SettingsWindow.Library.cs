using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const int LibraryPageSize = 200;

        private readonly ComboBox _cboLibraryServer = new() { Width = 220, PlaceholderText = "No music server" };
        private readonly ComboBox _cboLibraryAmp = new() { Width = 200 };
        private readonly TextBlock _lblLibraryStatus = Subtitle();
        private readonly WrapPanel _libraryCrumbs = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _libraryRows = new();
        private readonly Button _btnLibraryPlayAll = Pill("Play all", accent: true);
        private readonly Button _btnLibraryMore = Pill("Load more");
        private readonly ScrollViewer _libraryScroll = new() { Padding = new Thickness(0, 0, 12, 0) };
        private readonly List<(string Id, string Title)> _libraryPath = [];
        private readonly List<Bitmap> _libraryBitmaps = [];
        private readonly Dictionary<string, Task<Bitmap?>> _libraryArt = new();
        private List<MediaEntry> _libraryEntries = [];
        private int _libraryTotal;
        private int _libraryFolderVersion;
        private bool _librarySearched;
        private bool _libraryFilling;
        private bool _libraryPlaying;

        private MediaServer? SelectedLibraryServer => (_cboLibraryServer.SelectedItem as ComboBoxItem)?.Tag as MediaServer;

        private Control BuildLibraryPage()
        {
            ToolTip.SetTip(_cboLibraryAmp, "The device to play on");
            ToolTip.SetTip(_cboLibraryServer, "The music server to browse");
            var btnFind = Pill("Find servers");
            btnFind.Click += async (_, _) => await FindLibraryServersAsync(force: true);
            var btnAddShare = Pill("Add shared folder");
            ToolTip.SetTip(btnAddShare, "Play music from a shared network folder (SMB), for example on a NAS or another computer");
            btnAddShare.Click += async (_, _) => await AddSharedFolderAsync();
            ToolTip.SetTip(_btnRemoveShare, "Remove this shared folder from the list");
            _btnRemoveShare.Click += async (_, _) => await RemoveSharedFolderAsync();
            _btnRemoveShare.IsVisible = false;
            _cboLibraryServer.SelectionChanged += async (_, _) =>
            {
                _btnRemoveShare.IsVisible = SelectedLibraryServer?.IsShare == true;
                if (_libraryFilling || SelectedLibraryServer is not { } server) return;
                _owner.SetLibraryServer(server.Udn);
                await OpenLibraryRootAsync(server);
            };
            _btnLibraryPlayAll.Click += async (_, _) => await PlayLibraryFolderAsync();
            _btnLibraryMore.Click += async (_, _) => await LoadMoreLibraryAsync();
            _btnLibraryPlayAll.IsVisible = _btnLibraryMore.IsVisible = false;
            _btnLibraryMore.Margin = new Thickness(0, 10, 0, 0);
            _btnLibraryMore.HorizontalAlignment = HorizontalAlignment.Center;

            _cboLibraryAmp.SelectionChanged += (_, _) => { if (!_loading) _ = RefreshLibraryServiceAsync(); };
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
            top.Children.Add(_libraryCrumbs);
            Grid.SetColumn(_btnLibraryPlayAll, 1);
            top.Children.Add(_btnLibraryPlayAll);
            _libraryScroll.Content = new StackPanel { Children = { _libraryRows, _btnLibraryMore } };
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            body.Children.Add(_libraryServiceHost);
            Grid.SetRow(top, 1);
            body.Children.Add(top);
            Grid.SetRow(_libraryScroll, 2);
            body.Children.Add(_libraryScroll);

            HookRoonStatus();
            var page = Page("Library", _lblLibraryStatus, [_cboLibraryAmp, _cboLibraryServer, btnFind, btnAddShare, _btnRemoveShare], body, scroll: false, subtitleBelow: true);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible && !_librarySearched) await FindLibraryServersAsync(force: false);
            };
            return page;
        }

        private Task<BrowsePage?> BrowseLibraryAsync(MediaServer server, string id, int start, int count) =>
            server.IsRoon ? _owner.RoonBrowseAsync(server, RoonPath, start, count)
            : server.IsShare ? _owner.BrowseShareAsync(server, id, start, count)
            : server.IsLyrion ? Lms.BrowseAsync(server, id, start, count)
            : Upnp.BrowseAsync(server, id, start, count);

        private string? AmpOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private void FillAmpCombo(ComboBox combo)
        {
            string selected = (_followActive ? null : AmpOf(combo)) ?? _owner._deviceIp;
            combo.Items.Clear();
            foreach (var d in _owner._knownDevices) combo.Items.Add(ComboItem(d.Ip, d.Name));
            int index = _owner._knownDevices.FindIndex(d => d.Ip == selected);
            combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
        }

        private async Task FindLibraryServersAsync(bool force)
        {
            _librarySearched = true;
            var servers = _owner._mediaServers;
            if (force || servers is not { Count: > 0 })
            {
                _lblLibraryStatus.Text = "Looking for music servers…";
                var upnp = Upnp.DiscoverMediaServersAsync(TimeSpan.FromSeconds(3));
                var lyrion = Lms.DiscoverAsync(TimeSpan.FromSeconds(3));
                var roon = RoonConnection.DiscoverAsync(TimeSpan.FromSeconds(3));
                await Task.WhenAll(upnp, lyrion, roon);
                servers = upnp.Result.Concat(lyrion.Result)
                    .Concat(roon.Result.Select(c => new MediaServer($"Roon · {c.Name}", $"roon:{c.UniqueId}", $"{c.Host}:{c.Port}", string.Empty,
                        c.Host == "127.0.0.1", IsRoon: true)))
                    .OrderBy(s => s.OnThisComputer ? 0 : 1)
                    .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _owner._mediaServers = servers;
            }
            servers = _owner.ShareServers.Concat(servers).ToList();
            string selected = SelectedLibraryServer?.Udn ?? _owner.LibraryServer;
            _libraryFilling = true;
            _cboLibraryServer.Items.Clear();
            foreach (var s in servers) _cboLibraryServer.Items.Add(ComboItem(s, s.Name));
            int index = servers.FindIndex(s => s.Udn == selected);
            _cboLibraryServer.SelectedIndex = index >= 0 ? index : servers.Count > 0 ? 0 : -1;
            _libraryFilling = false;
            _btnRemoveShare.IsVisible = SelectedLibraryServer?.IsShare == true;

            if (SelectedLibraryServer is not { } server)
            {
                _ = RefreshLibraryServiceAsync();
                ClearLibraryRows();
                _libraryPath.Clear();
                UpdateLibraryCrumbs();
                _lblLibraryStatus.Text = "No music server found";
                _libraryRows.Children.Add(Muted(
                    "No music servers were found on your network. Start a DLNA/UPnP media server, for example Windows Media Player " +
                    "media streaming, MinimServer, Plex, Jellyfin or a NAS media server, a Lyrion Music Server or Roon, and click Find servers. " +
                    "You can also click Add shared folder to play music from a shared network folder."));
                return;
            }
            _owner.SetLibraryServer(server.Udn);
            await OpenLibraryRootAsync(server);
        }

        private async Task OpenLibraryRootAsync(MediaServer server)
        {
            if (server.IsRoon) _owner.UseRoonCore(server.ContentDirectoryUrl);
            _ = RefreshLibraryServiceAsync();
            _libraryPath.Clear();
            _libraryPath.Add(("0", server.Name));
            await LoadLibraryFolderAsync();
        }

        private async Task OpenLibraryFolderAsync(MediaEntry folder, int depth)
        {
            if (IsRoonInput(folder) && await OpenRoonInputAsync(folder, depth)) return;
            if (_libraryPath.Count > depth) _libraryPath.RemoveRange(depth, _libraryPath.Count - depth);
            _libraryPath.Add((folder.Id, folder.Title));
            await LoadLibraryFolderAsync();
        }

        private async Task LoadLibraryFolderAsync()
        {
            if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0) return;
            int version = ++_libraryFolderVersion;
            UpdateLibraryCrumbs();
            _lblLibraryStatus.Text = "Loading…";
            var page = await BrowseLibraryAsync(server, _libraryPath[^1].Id, 0, LibraryPageSize);
            if (version != _libraryFolderVersion) return;

            ClearLibraryRows();
            _libraryScroll.Offset = default;
            if (page == null && server.IsRoon && _owner.RoonStatus == RoonState.WaitingForApproval)
            {
                ShowRoonApprovalHint();
                return;
            }
            if (page == null && server.IsShare && await AskSharedFolderLoginAsync(server))
            {
                await LoadLibraryFolderAsync();
                return;
            }
            if (page == null)
            {
                _libraryEntries = [];
                _libraryTotal = 0;
                _lblLibraryStatus.Text = "Couldn't read this folder";
                _libraryRows.Children.Add(Muted(server.IsShare
                    ? "The shared folder couldn't be opened. Check that the computer or NAS is switched on and that the user name and password still work."
                    : "The music server didn't answer. Check that it's running, then click Find servers."));
                UpdateLibraryButtons();
                return;
            }
            _libraryEntries = [];
            _libraryTotal = page.Total;
            AddLibraryEntries(page.Entries);
            if (_libraryRows.Children.Count == 0) _libraryRows.Children.Add(Muted("This folder is empty."));
        }

        private async Task LoadMoreLibraryAsync()
        {
            if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0) return;
            int version = _libraryFolderVersion;
            _btnLibraryMore.IsEnabled = false;
            var page = await BrowseLibraryAsync(server, _libraryPath[^1].Id, _libraryEntries.Count, LibraryPageSize);
            _btnLibraryMore.IsEnabled = true;
            if (version != _libraryFolderVersion || page == null) return;
            _libraryTotal = page.Total;
            if (page.Entries.Count == 0) _libraryTotal = _libraryEntries.Count;
            AddLibraryEntries(page.Entries);
        }

        private void AddLibraryEntries(List<MediaEntry> entries)
        {
            int first = _libraryEntries.Count;
            _libraryEntries.AddRange(entries);
            for (int i = first; i < _libraryEntries.Count; i++)
            {
                var entry = _libraryEntries[i];
                if (entry.IsContainer || entry.IsAudio) _libraryRows.Children.Add(LibraryRow(entry, i));
            }
            UpdateLibraryButtons();
            UpdateLibraryStatus();
        }

        private void UpdateLibraryStatus()
        {
            int folders = _libraryEntries.Count(e => e.IsContainer), tracks = _libraryEntries.Count(e => e.IsAudio);
            var parts = new List<string>();
            if (folders > 0) parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
            if (tracks > 0) parts.Add(tracks == 1 ? "1 track" : $"{tracks} tracks");
            if (_libraryEntries.Count < _libraryTotal) parts.Add($"{_libraryEntries.Count} of {_libraryTotal} loaded");
            _lblLibraryStatus.Text = string.Join(" · ", parts);
        }

        private void UpdateLibraryButtons()
        {
            _btnLibraryPlayAll.IsVisible = _libraryEntries.Any(e => e.IsAudio);
            _btnLibraryMore.IsVisible = _libraryEntries.Count < _libraryTotal;
        }

        private void UpdateLibraryCrumbs()
        {
            _libraryCrumbs.Children.Clear();
            for (int i = 0; i < _libraryPath.Count; i++)
            {
                int level = i;
                bool last = i == _libraryPath.Count - 1;
                if (i > 0)
                    _libraryCrumbs.Children.Add(new PathIcon
                    {
                        Data = Icons.ChevronRight, Width = 16, Height = 16, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center
                    });
                var crumb = new TextBlock
                {
                    Text = _libraryPath[i].Title, Classes = { last ? "heading" : "muted" }, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 2), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis
                };
                if (!last)
                {
                    crumb.Cursor = new Cursor(StandardCursorType.Hand);
                    crumb.PointerEntered += (_, _) => crumb.Foreground = Palette.Text;
                    crumb.PointerExited += (_, _) => crumb.ClearValue(TextBlock.ForegroundProperty);
                    crumb.Tapped += async (_, _) =>
                    {
                        _libraryPath.RemoveRange(level + 1, _libraryPath.Count - level - 1);
                        await LoadLibraryFolderAsync();
                    };
                }
                _libraryCrumbs.Children.Add(crumb);
            }
        }

        private void ClearLibraryRows()
        {
            _libraryFolderVersion++;
            _libraryRows.Children.Clear();
            foreach (var bitmap in _libraryBitmaps) bitmap.Dispose();
            _libraryBitmaps.Clear();
            _libraryArt.Clear();
        }

        private Control LibraryRow(MediaEntry entry, int index)
        {
            var icon = new PathIcon
            {
                Data = !entry.IsContainer ? Icons.Music : entry.Class.Contains("album", StringComparison.OrdinalIgnoreCase) ? Icons.Album : Icons.Folder,
                Width = 22, Height = 22, Foreground = Palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var art = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
            var picture = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Input,
                Child = new Panel { Children = { icon, art } }
            };
            string detail = entry.IsContainer
                ? entry.ChildCount is { } n ? (n == 1 ? "1 item" : $"{n} items") : string.Empty
                : string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(s => s.Length > 0));
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0),
                Children = { new TextBlock { Text = entry.Title.Length > 0 ? entry.Title : "(untitled)", TextTrimming = TextTrimming.CharacterEllipsis } }
            };
            if (detail.Length > 0)
                text.Children.Add(new TextBlock { Text = detail, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(picture);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Control end;
            bool roon = SelectedLibraryServer?.IsRoon == true;
            if (entry.IsContainer && roon && (IsRoonInput(entry) || !HasRoonPlay(entry)))
            {
                end = new PathIcon { Data = Icons.ChevronRight, Width = 20, Height = 20, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center };
            }
            else if (entry.IsContainer)
            {
                var (play, _) = IconButton(Icons.Play);
                ToolTip.SetTip(play, $"Play {entry.Title}");
                play.Click += async (_, _) => await PlayLibraryContainerAsync(entry);
                end = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        play,
                        LibraryMoreButton(entry, index),
                        new PathIcon { Data = Icons.ChevronRight, Width = 20, Height = 20, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }
            else
            {
                end = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = entry.Duration > TimeSpan.Zero ? FormatTime((int)entry.Duration.TotalSeconds) : string.Empty,
                            Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                        },
                        LibraryMoreButton(entry, index)
                    }
                };
            }
            Grid.SetColumn(end, 2);
            grid.Children.Add(end);

            var row = new Border
            {
                Child = grid, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(row, entry.IsContainer ? $"Open {entry.Title}" : $"Play {entry.Title}");
            int depth = _libraryPath.Count;
            row.PointerEntered += (_, _) => row.Background = Palette.Card;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.Tapped += async (_, e) =>
            {
                if (e.Source is Visual v && (v as Button ?? v.FindAncestorOfType<Button>()) != null) return;
                if (entry.IsContainer) await OpenLibraryFolderAsync(entry, depth);
                else await PlayLibraryFromAsync(index);
            };
            if (entry.ArtUrl.Length > 0) _ = LoadLibraryArtAsync(entry.ArtUrl, art, icon, _libraryFolderVersion);
            return row;
        }

        private async Task LoadLibraryArtAsync(string url, Image art, PathIcon icon, int version)
        {
            if (!_libraryArt.TryGetValue(url, out var download))
            {
                download = _owner.LoadThumbnailAsync(url, 88, () => version == _libraryFolderVersion);
                _libraryArt[url] = download;
                _ = KeepLibraryArtAsync(download, version);
            }
            var bitmap = await download;
            if (bitmap == null || version != _libraryFolderVersion) return;
            art.Source = bitmap;
            art.IsVisible = true;
            icon.IsVisible = false;
        }

        private Button LibraryMoreButton(MediaEntry entry, int index)
        {
            var (more, _) = IconButton(Icons.More);
            ToolTip.SetTip(more, "Play now, play next or add to the queue");
            var menu = new MenuFlyout();
            void Add(string header, Func<Task> action)
            {
                var item = new MenuItem { Header = header };
                item.Click += async (_, _) => await action();
                menu.Items.Add(item);
            }
            if (SelectedLibraryServer?.IsRoon == true)
            {
                Add("Play now", () => RoonPlayAsync(entry, RoonPlayNow));
                Add("Play next", () => RoonPlayAsync(entry, RoonAddNext));
                Add("Add to queue", () => RoonPlayAsync(entry, RoonQueue));
                Add("Start Roon Radio", () => RoonPlayAsync(entry, RoonStartRadio));
                more.Flyout = menu;
                return more;
            }
            Add("Play now", () => entry.IsContainer ? PlayLibraryContainerAsync(entry) : PlayLibraryFromAsync(index));
            Add("Play next", () => AddLibraryToQueueAsync(entry, index, next: true));
            Add("Add to queue", () => AddLibraryToQueueAsync(entry, index, next: false));
            more.Flyout = menu;
            return more;
        }

        private async Task AddLibraryToQueueAsync(MediaEntry entry, int index, bool next)
        {
            if (SelectedLibraryServer is not { } server) return;
            if (AmpOf(_cboLibraryAmp) is not { } ip)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return;
            }
            List<MediaEntry> tracks = [entry];
            if (entry.IsContainer)
            {
                _lblLibraryStatus.Text = $"Loading {entry.Title}…";
                tracks = (await BrowseLibraryAsync(server, entry.Id, 0, MaxQueueTracks))?.Entries.Where(e => e.IsAudio).ToList() ?? [];
                if (tracks.Count == 0)
                {
                    UpdateLibraryStatus();
                    await Dialogs.MessageAsync(this, $"{entry.Title} has no tracks of its own. Open it to pick an album or a track.", _scale);
                    return;
                }
            }
            string target = TargetOf(ip);
            var added = await _owner.AddToQueueAsync(target, server, tracks, next);
            if (added == null)
            {
                await PlayLibraryTracksAsync(ip, new PlaySource(server, entry.IsContainer ? entry.Id : _libraryPath[^1].Id,
                    entry.IsContainer ? entry.Title : _libraryPath[^1].Title, entry.IsContainer ? 0 : index, entry.ArtUrl), tracks, 0, _lblLibraryStatus);
                return;
            }
            if (added == false)
            {
                UpdateLibraryStatus();
                await Dialogs.MessageAsync(this, "The device didn't accept the tracks.", _scale);
                return;
            }
            string what = tracks.Count == 1 ? tracks[0].Title : $"{entry.Title} ({tracks.Count} tracks)";
            _lblLibraryStatus.Text = next ? $"{what} plays next" : $"Added {what} to the queue";
        }

        private async Task KeepLibraryArtAsync(Task<Bitmap?> download, int version)
        {
            var bitmap = await download;
            if (bitmap == null) return;
            if (version == _libraryFolderVersion) _libraryBitmaps.Add(bitmap);
            else bitmap.Dispose();
        }

        private sealed record PlaySource(MediaServer Server, string FolderId, string FolderTitle, int FolderIndex, string FolderArt);

        private void ResetStatus(TextBlock status)
        {
            if (ReferenceEquals(status, _lblLibraryStatus)) UpdateLibraryStatus();
            else status.Text = string.Empty;
        }

        private async Task<bool> PlayOnLyrionAsync(string ip, MediaServer server, List<string> trackIds, int start, TextBlock status)
        {
            var result = await _owner.PlayOnLyrionAsync(ip, server, trackIds, start);
            string amp = _owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
            if (result == LyrionPlayResult.NotConnected)
            {
                string host = new Uri(server.ContentDirectoryUrl).Host;
                bool switchedOff = await _owner.GetStreamServiceActiveAsync(ip, SqueezeliteService) == false;
                if (!await Dialogs.ConfirmAsync(this, switchedOff
                        ? $"Squeezelite is switched off on {amp}, so it can't play from {server.Name}. Switch it on and connect now?"
                        : $"{amp} isn't connected to {server.Name}. Connect it now?", _scale)) return false;
                status.Text = $"Connecting {amp} to {server.Name}…";
                if (switchedOff)
                {
                    if (!await _owner.SetStreamServiceActiveAsync(ip, SqueezeliteService, true))
                    {
                        ResetStatus(status);
                        await Dialogs.MessageAsync(this, $"{amp} didn't switch Squeezelite on.", _scale);
                        return false;
                    }
                    await Task.Delay(ServiceSettleTime);
                }
                bool connected = await _owner.ConnectSqueezeliteAsync(ip, host) && await _owner.WaitForLyrionPlayerAsync(ip, server);
                _ = RefreshLibraryServiceAsync();
                if (!connected)
                {
                    ResetStatus(status);
                    await Dialogs.MessageAsync(this, $"{amp} didn't connect to {server.Name}.", _scale);
                    return false;
                }
                result = await _owner.PlayOnLyrionAsync(ip, server, trackIds, start);
            }
            if (result == LyrionPlayResult.Played) return true;
            ResetStatus(status);
            await Dialogs.MessageAsync(this, $"{server.Name} didn't start playing on {amp}.", _scale);
            return false;
        }

        private static QueueTrack ToQueueTrack(MediaEntry e) => QueueTrackOf(e);

        private async Task<(List<MediaEntry> Tracks, int Start)?> CollectFolderTracksAsync(MediaServer server, string folderId, int index)
        {
            var page = await BrowseLibraryAsync(server, folderId, 0, MaxQueueTracks);
            if (page == null) return null;
            if (page.Total <= MaxQueueTracks)
                return (page.Entries.Where(e => e.IsAudio).ToList(), page.Entries.Take(index).Count(e => e.IsAudio));
            var rest = await BrowseLibraryAsync(server, folderId, index, MaxQueueTracks);
            return rest == null ? null : (rest.Entries.Where(e => e.IsAudio).ToList(), 0);
        }

        private async Task PlayLibraryFolderAsync()
        {
            if (SelectedLibraryServer?.IsRoon == true)
            {
                await RoonPlayFolderAsync();
                return;
            }
            int first = _libraryEntries.FindIndex(e => e.IsAudio);
            if (first >= 0) await PlayLibraryFromAsync(first);
        }

        private async Task PlayLibraryFromAsync(int index)
        {
            if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0) return;
            if (server.IsRoon)
            {
                if (index >= 0 && index < _libraryEntries.Count) await RoonPlayAsync(_libraryEntries[index], RoonPlayNow);
                return;
            }
            List<MediaEntry> tracks;
            int start;
            if (_libraryEntries.Count >= _libraryTotal && _libraryEntries.Count(e => e.IsAudio) <= MaxQueueTracks)
            {
                tracks = _libraryEntries.Where(e => e.IsAudio).ToList();
                start = _libraryEntries.Take(index).Count(e => e.IsAudio);
            }
            else
            {
                _lblLibraryStatus.Text = "Loading tracks…";
                var page = await BrowseLibraryAsync(server, _libraryPath[^1].Id, index, MaxQueueTracks);
                tracks = page?.Entries.Where(e => e.IsAudio).ToList() ?? [];
                start = 0;
            }
            var folder = _libraryPath[^1];
            await PlayLibraryTracksAsync(AmpOf(_cboLibraryAmp), new PlaySource(server, folder.Id, folder.Title, index, string.Empty),
                tracks, start, _lblLibraryStatus);
        }

        private async Task PlayLibraryContainerAsync(MediaEntry folder)
        {
            if (SelectedLibraryServer is not { } server) return;
            if (server.IsRoon)
            {
                await RoonPlayAsync(folder, RoonPlayNow);
                return;
            }
            _lblLibraryStatus.Text = $"Loading {folder.Title}…";
            var page = await BrowseLibraryAsync(server, folder.Id, 0, MaxQueueTracks);
            var tracks = page?.Entries.Where(e => e.IsAudio).ToList() ?? [];
            if (page == null || tracks.Count == 0)
            {
                UpdateLibraryStatus();
                await Dialogs.MessageAsync(this, page == null
                    ? "The music server didn't answer."
                    : $"{folder.Title} has no tracks of its own. Open it to pick an album or a track.", _scale);
                return;
            }
            int first = page.Entries.FindIndex(e => e.IsAudio);
            await PlayLibraryTracksAsync(AmpOf(_cboLibraryAmp), new PlaySource(server, folder.Id, folder.Title, Math.Max(0, first), folder.ArtUrl),
                tracks, 0, _lblLibraryStatus);
        }

        private async Task<bool> PlayLibraryTracksAsync(string? ip, PlaySource source, List<MediaEntry> tracks, int start, TextBlock status)
        {
            if (ip == null)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return false;
            }
            if (tracks.Count == 0 || _libraryPlaying) return false;
            var server = source.Server;
            string target = TargetOf(ip);
            var queue = tracks.Take(MaxQueueTracks).Select(ToQueueTrack).ToList();
            start = Math.Clamp(start, 0, queue.Count - 1);
            _libraryPlaying = true;
            bool played;
            try
            {
                played = server.IsLyrion
                    ? await PlayOnLyrionAsync(target, server, tracks.Take(MaxQueueTracks).Select(Lms.TrackId).OfType<string>().ToList(), start, status)
                    : await _owner.PlayTracksAsync(target, queue, start, server.IsShare);
            }
            finally { _libraryPlaying = false; }
            if (!played)
            {
                if (server.IsLyrion) return false;
                ResetStatus(status);
                await Dialogs.MessageAsync(this, "The device didn't accept the tracks. Check that it's switched on and reachable.", _scale);
                return false;
            }

            var first = queue[start];
            var detail = new List<string>();
            string artist = queue.Select(t => t.Artist).FirstOrDefault(a => a.Length > 0) ?? string.Empty;
            if (artist.Length > 0) detail.Add(artist);
            if (start > 0) detail.Add($"from {first.Title}");
            detail.Add(server.Name);
            _owner.AddRecent(new RecentItem(source.FolderTitle, string.Join(" · ", detail),
                first.ArtUrl.Length > 0 ? first.ArtUrl : source.FolderArt, DateTime.Now, null,
                new RecentLibrary(server.Name, server.Udn, server.ContentDirectoryUrl, server.IsLyrion, source.FolderId, source.FolderIndex,
                    IsShare: server.IsShare)));

            string name = _owner._knownDevices.FirstOrDefault(d => d.Ip == target).Name ?? target;
            status.Text = string.Create(CultureInfo.InvariantCulture, $"Playing {first.Title} on {name}");
            await Task.Delay(1500);
            _ = _owner.RefreshNowPlayingAsync();
            if (IsVisible) await RefreshDeviceStatusesAsync();
            return true;
        }
    }
}
