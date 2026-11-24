using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage(SettingsWindow window)
        {
            private const int LibraryPageSize = 200;

            private sealed record LibraryPlace(string FolderId, int Loaded, double Offset, string? OpenedId);

            private readonly Dictionary<int, LibraryPlace> _libraryPlaces = new();
            private readonly Dictionary<string, Border> _libraryRowsById = new();

            private readonly ComboBox _cboLibraryServer = new() { Width = 220, PlaceholderText = "No music server" };
            private readonly ComboBox _cboLibraryAmp = new() { Width = 200 };
            private readonly TextBlock _lblLibraryStatus = Subtitle();
            private readonly WrapPanel _libraryCrumbs = new() { VerticalAlignment = VerticalAlignment.Center };
            private readonly StackPanel _libraryRows = new();
            private readonly Button _btnLibraryPlayAll = Pill("Play all", accent: true);
            private readonly Button _btnLibraryMore = Pill("Load more");
            private readonly ScrollViewer _libraryScroll = new() { Padding = new Thickness(0, 0, 12, 0) };
            private readonly TextBox _txtLibrarySearch = new() { Watermark = "Search for an artist, album or track", MinWidth = 340 };
            private readonly StackPanel _librarySearchRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12), IsVisible = false };
            private (string Key, Task<List<MediaEntry>?> Search)? _librarySearch;
            private int _librarySearchCheck;
            private readonly List<(string Id, string Title)> _libraryPath = [];
            private readonly List<Bitmap> _libraryBitmaps = [];
            private readonly Dictionary<string, Task<Bitmap?>> _libraryArt = new();
            private List<MediaEntry> _libraryEntries = [];
            private int _libraryTotal;
            private int _libraryFolderVersion;
            private bool _librarySearched;
            private bool _libraryLoadFailed;
            private bool _libraryFilling;
            private bool _libraryPlaying;
            private int? _libraryMoreVersion;
            private bool _libraryAutoMoreFailed;

            private MediaServer? SelectedLibraryServer => (_cboLibraryServer.SelectedItem as ComboBoxItem)?.Tag as MediaServer;

            private static readonly object ThisComputer = new();
            private bool _fillingDevices;

            private bool OnComputer => (_cboLibraryAmp.SelectedItem as ComboBoxItem)?.Tag == ThisComputer;

            public void FillDevices()
            {
                bool local = window._owner.LibraryOnComputer;
                _fillingDevices = true;
                try
                {
                    window.FillAmpCombo(_cboLibraryAmp);
                    if (LocalPlayerSupported)
                    {
                        var item = ComboItem(ThisComputer, "This computer");
                        _cboLibraryAmp.Items.Add(item);
                        if (local || _cboLibraryAmp.SelectedIndex < 0) _cboLibraryAmp.SelectedItem = item;
                    }
                }
                finally { _fillingDevices = false; }
                window.ShowLibraryPlayer(AmpOf(_cboLibraryAmp), OnComputer);
            }

            public Control Build()
            {
                ToolTip.SetTip(_cboLibraryAmp, "The device to play on");
                _cboLibraryAmp.SelectionChanged += (_, _) =>
                {
                    if (_fillingDevices) return;
                    window._owner.LibraryOnComputer = OnComputer;
                    window.ShowLibraryPlayer(AmpOf(_cboLibraryAmp), OnComputer);
                };
                window._owner.LocalPlayerProblem += ShowLocalProblem;
                window.Closed += (_, _) => window._owner.LocalPlayerProblem -= ShowLocalProblem;
                ToolTip.SetTip(_cboLibraryServer, "The music server to browse");
                var btnFind = Pill("Find servers");
                btnFind.Click += async (_, _) => await FindLibraryServersAsync(force: true);
                var btnAddShare = Pill("Add shared folder");
                ToolTip.SetTip(btnAddShare, "Play music from a shared network folder (SMB), for example on a NAS or another computer");
                btnAddShare.Click += async (_, _) => await AddSharedFolderAsync();
                var btnAddEmby = Pill("Add Emby/Jellyfin");
                ToolTip.SetTip(btnAddEmby, "Play music from an Emby or Jellyfin server, at home or on the internet");
                btnAddEmby.Click += async (_, _) => await AddEmbyServerAsync();
                ToolTip.SetTip(_btnRemoveShare, "Remove this server or shared folder from the list");
                _btnRemoveShare.Click += async (_, _) => await RemoveLibraryServerAsync();
                _btnRemoveShare.IsVisible = false;
                _cboLibraryServer.SelectionChanged += async (_, _) =>
                {
                    _btnRemoveShare.IsVisible = SelectedLibraryServer is { IsShare: true } or { IsEmbyJellyfin: true };
                    if (_libraryFilling || SelectedLibraryServer is not { } server) return;
                    window._owner.SetLibraryServer(server.Udn);
                    await OpenLibraryRootAsync(server);
                };
                _btnLibraryPlayAll.Click += async (_, _) => await PlayLibraryFolderAsync();
                _btnLibraryMore.Click += async (_, _) => await LoadMoreLibraryAsync(auto: false);
                _libraryScroll.ScrollChanged += (_, _) => MaybeLoadMoreLibrary();
                _btnLibraryPlayAll.IsVisible = _btnLibraryMore.IsVisible = false;
                _btnLibraryMore.Margin = new Thickness(0, 10, 0, 0);
                _btnLibraryMore.HorizontalAlignment = HorizontalAlignment.Center;

                var (refresh, _) = IconButton(Icons.Refresh);
                refresh.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(refresh, "Read this folder again from the server, for example after adding music");
                refresh.Click += async (_, _) =>
                {
                    refresh.IsEnabled = false;
                    try { await RefreshLibraryFolderAsync(); }
                    finally { refresh.IsEnabled = true; }
                };
                var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
                top.Children.Add(_libraryCrumbs);
                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
                    Children = { _btnLibraryPlayAll, refresh }
                };
                Grid.SetColumn(actions, 1);
                top.Children.Add(actions);
                _libraryScroll.Content = new StackPanel { Children = { _libraryRows, _btnLibraryMore } };
                var btnSearch = Pill("Search", accent: true);
                btnSearch.Click += async (_, _) => await SearchLibraryAsync();
                _txtLibrarySearch.KeyDown += async (_, e) =>
                {
                    if (e.Key != Key.Enter) return;
                    e.Handled = true;
                    await SearchLibraryAsync();
                };
                _librarySearchRow.Children.Add(_txtLibrarySearch);
                _librarySearchRow.Children.Add(btnSearch);
                var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
                body.Children.Add(_librarySearchRow);
                Grid.SetRow(top, 1);
                body.Children.Add(top);
                Grid.SetRow(_libraryScroll, 2);
                body.Children.Add(_libraryScroll);
                Grid.SetRow(window._libraryPlayer, 3);
                body.Children.Add(window._libraryPlayer);

                HookRoonStatus();
                var page = window.Page("Library", _lblLibraryStatus, [btnFind, btnAddShare, btnAddEmby, _btnRemoveShare], body,
                    scroll: false, subtitleBelow: true, secondRow: [_cboLibraryAmp, _cboLibraryServer]);
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property != IsVisibleProperty || !page.IsVisible) return;
                    window.ShowLibraryPlayer(AmpOf(_cboLibraryAmp), OnComputer);
                    _ = window.RefreshDeviceStatusesAsync();
                    if (!_librarySearched) await FindLibraryServersAsync(force: false);
                    else if (SelectedLibraryServer is { IsEmbyJellyfin: true } server && (_libraryLoadFailed || !await window._owner.EmbyServerAnswersAsync(server)))
                        await LoadLibraryFolderAsync();
                };
                window.Closed += (_, _) => ClearLibraryRows();
                return page;
            }

            private static int LibraryPageSizeOf(MediaServer server) => server.IsShare ? int.MaxValue : LibraryPageSize;

            private Task<BrowsePage?> BrowseLibraryAsync(MediaServer server, string id, int start, int count, bool fresh = false) =>
                !server.IsRoon && IsLibrarySearch(id) ? SearchLibraryPageAsync(server, id[LibrarySearchPrefix.Length..], start, count, fresh)
                : server.IsRoon ? window._owner.RoonBrowseAsync(RoonPath, start, count)
                : server.IsShare ? window._owner.BrowseShareAsync(server, id, start, count, fresh)
                : server.IsEmbyJellyfin ? window._owner.BrowseEmbyAsync(server, id, start, count)
                : server.IsLyrion ? Lms.BrowseAsync(server, id, start, count)
                : Upnp.BrowseAsync(server, id, start, count);

            private async Task FindLibraryServersAsync(bool force)
            {
                _librarySearched = true;
                var servers = window._owner._mediaServers;
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
                    window._owner._mediaServers = servers;
                }
                servers = window._owner.ShareServers.Concat(window._owner.EmbyServers).Concat(servers).ToList();
                string selected = SelectedLibraryServer?.Udn ?? window._owner.LibraryServer;
                _libraryFilling = true;
                _cboLibraryServer.Items.Clear();
                foreach (var s in servers) _cboLibraryServer.Items.Add(ComboItem(s, s.Name));
                int index = servers.FindIndex(s => s.Udn == selected);
                _cboLibraryServer.SelectedIndex = index >= 0 ? index : servers.Count > 0 ? 0 : -1;
                _libraryFilling = false;
                _btnRemoveShare.IsVisible = SelectedLibraryServer is { IsShare: true } or { IsEmbyJellyfin: true };

                if (SelectedLibraryServer is not { } server)
                {
                    _librarySearchCheck++;
                    _librarySearchRow.IsVisible = false;
                    ClearLibraryRows();
                    _libraryPath.Clear();
                    UpdateLibraryCrumbs();
                    _lblLibraryStatus.Text = "No music server found";
                    _libraryRows.Children.Add(Muted(
                        "No music servers were found on your network. Start a DLNA/UPnP media server, for example Windows Media Player " +
                        "media streaming, MinimServer, Plex, Jellyfin or a NAS media server, a Lyrion Music Server or Roon, and click Find servers. " +
                        "You can also click Add shared folder to play music from a shared network folder, or Add Emby/Jellyfin to sign in to an Emby or Jellyfin server."));
                    return;
                }
                window._owner.SetLibraryServer(server.Udn);
                await OpenLibraryRootAsync(server);
            }

            private async Task OpenLibraryRootAsync(MediaServer server)
            {
                if (server.IsRoon) window._owner.UseRoonCore(server.ContentDirectoryUrl);
                _ = ShowLibrarySearchAsync(server);
                _libraryPath.Clear();
                _libraryPlaces.Clear();
                _libraryPath.Add(("0", server.Name));
                await LoadLibraryFolderAsync();
            }

            private void SaveLibraryPlace(int depth, string? openedId)
            {
                if (depth < 1 || depth != _libraryPath.Count) return;
                int level = depth - 1;
                foreach (int deeper in _libraryPlaces.Keys.Where(k => k >= level).ToList()) _libraryPlaces.Remove(deeper);
                _libraryPlaces[level] = new LibraryPlace(_libraryPath[level].Id, _libraryEntries.Count, _libraryScroll.Offset.Y, openedId);
            }

            private Task RefreshLibraryFolderAsync()
            {
                SaveLibraryPlace(_libraryPath.Count, null);
                return LoadLibraryFolderAsync(fresh: true);
            }

            private async Task OpenLibraryFolderAsync(MediaEntry folder, int depth)
            {
                SaveLibraryPlace(depth, folder.Id);
                if (IsRoonInput(folder) && await OpenRoonInputAsync(folder, depth)) return;
                if (_libraryPath.Count > depth) _libraryPath.RemoveRange(depth, _libraryPath.Count - depth);
                _libraryPath.Add((folder.Id, folder.Title));
                await LoadLibraryFolderAsync();
            }

            private async Task LoadLibraryFolderAsync(bool fresh = false)
            {
                if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0) return;
                int version = ++_libraryFolderVersion;
                int level = _libraryPath.Count - 1;
                LibraryPlace? place = _libraryPlaces.Remove(level, out var saved) && saved.FolderId == _libraryPath[level].Id ? saved : null;
                foreach (int deeper in _libraryPlaces.Keys.Where(k => k > level).ToList()) _libraryPlaces.Remove(deeper);
                UpdateLibraryCrumbs();
                _lblLibraryStatus.Text = "Loading…";
                int count = Math.Max(LibraryPageSizeOf(server), place?.Loaded ?? 0);
                var page = await BrowseLibraryAsync(server, _libraryPath[^1].Id, 0, count, fresh);
                if (version != _libraryFolderVersion) return;

                _libraryLoadFailed = page == null;
                ClearLibraryRows();
                _libraryAutoMoreFailed = false;
                _btnLibraryMore.Content = "Load more";
                _btnLibraryMore.IsEnabled = true;
                _libraryScroll.Offset = default;
                if (page == null && server.IsRoon && window._owner.RoonStatus == RoonState.WaitingForApproval)
                {
                    ShowRoonApprovalHint();
                    return;
                }
                if (page == null && server.IsShare && await AskSharedFolderLoginAsync(server))
                {
                    await LoadLibraryFolderAsync();
                    return;
                }
                if (page == null && server.IsEmbyJellyfin && await AskEmbySignInAsync(server))
                {
                    await LoadLibraryFolderAsync();
                    return;
                }
                bool search = !server.IsRoon && IsLibrarySearch(_libraryPath[^1].Id);
                if (page == null && search)
                {
                    _libraryEntries = [];
                    _libraryTotal = 0;
                    _lblLibraryStatus.Text = "Couldn't search";
                    _libraryRows.Children.Add(Muted("The music server didn't answer the search. Check that it's running and try again."));
                    UpdateLibraryButtons();
                    return;
                }
                if (page == null)
                {
                    _libraryEntries = [];
                    _libraryTotal = 0;
                    _lblLibraryStatus.Text = "Couldn't read this folder";
                    _libraryRows.Children.Add(Muted(server.IsShare
                        ? "The shared folder couldn't be opened. Check that the computer or NAS is switched on and that the user name and password still work."
                        : server.IsEmbyJellyfin
                            ? "The server didn't answer. Check that it's running and that this computer can reach it."
                            : "The music server didn't answer. Check that it's running, then click Find servers."));
                    UpdateLibraryButtons();
                    return;
                }
                _libraryEntries = [];
                _libraryTotal = page.Total;
                AddLibraryEntries(page.Entries);
                if (_libraryRows.Children.Count == 0)
                    _libraryRows.Children.Add(Muted(search ? $"Nothing was found for “{_libraryPath[^1].Id[LibrarySearchPrefix.Length..]}”." : "This folder is empty."));
                if (place != null) await RestoreLibraryPlaceAsync(server, place);
            }

            private bool LibraryLoadingMore => _libraryMoreVersion == _libraryFolderVersion;

            private void MaybeLoadMoreLibrary()
            {
                if (LibraryLoadingMore || _libraryAutoMoreFailed || !_btnLibraryMore.IsVisible) return;
                if (_libraryScroll.Offset.Y + _libraryScroll.Viewport.Height < _libraryScroll.Extent.Height - 800) return;
                _ = LoadMoreLibraryAsync(auto: true);
            }

            private async Task RestoreLibraryPlaceAsync(MediaServer server, LibraryPlace place)
            {
                int version = _libraryFolderVersion;
                _libraryMoreVersion = version;
                try
                {
                    while (_libraryEntries.Count < place.Loaded && _libraryEntries.Count < _libraryTotal)
                    {
                        var more = await BrowseLibraryAsync(server, _libraryPath[^1].Id, _libraryEntries.Count, LibraryPageSizeOf(server));
                        if (version != _libraryFolderVersion || more == null) return;
                        _libraryTotal = more.Entries.Count == 0 ? _libraryEntries.Count : more.Total;
                        AddLibraryEntries(more.Entries);
                    }
                }
                finally
                {
                    if (_libraryMoreVersion == version) _libraryMoreVersion = null;
                }
                if (version != _libraryFolderVersion) return;
                _libraryScroll.UpdateLayout();
                _libraryScroll.Offset = new Vector(0, place.Offset);
                _libraryScroll.UpdateLayout();
                if (place.OpenedId == null || !_libraryRowsById.TryGetValue(place.OpenedId, out var row)) return;
                if (row.TranslatePoint(default, _libraryRows) is { } top)
                {
                    double viewport = _libraryScroll.Viewport.Height, offset = _libraryScroll.Offset.Y;
                    if (top.Y < offset || top.Y + row.Bounds.Height > offset + viewport)
                        _libraryScroll.Offset = new Vector(0, Math.Max(0, top.Y - (viewport - row.Bounds.Height) / 2));
                }
                row.Background = Palette.CardActive;
                DispatcherTimer.RunOnce(() =>
                {
                    if (version == _libraryFolderVersion) row.Background = row.IsPointerOver ? Palette.Card : Brushes.Transparent;
                }, TimeSpan.FromSeconds(1.5));
            }

            private async Task LoadMoreLibraryAsync(bool auto)
            {
                if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0 || LibraryLoadingMore) return;
                if (!auto) _libraryAutoMoreFailed = false;
                int version = _libraryFolderVersion;
                _libraryMoreVersion = version;
                _btnLibraryMore.IsEnabled = false;
                _btnLibraryMore.Content = "Loading more…";
                try
                {
                    var page = await BrowseLibraryAsync(server, _libraryPath[^1].Id, _libraryEntries.Count, LibraryPageSizeOf(server));
                    if (version != _libraryFolderVersion) return;
                    if (page == null)
                    {
                        _libraryAutoMoreFailed = true;
                        _btnLibraryMore.Content = "Couldn't load more. Try again";
                        return;
                    }
                    _btnLibraryMore.Content = "Load more";
                    _libraryTotal = page.Total;
                    if (page.Entries.Count == 0) _libraryTotal = _libraryEntries.Count;
                    AddLibraryEntries(page.Entries);
                }
                finally
                {
                    if (_libraryMoreVersion == version)
                    {
                        _libraryMoreVersion = null;
                        _btnLibraryMore.IsEnabled = true;
                    }
                }
            }
        }
    }
}
