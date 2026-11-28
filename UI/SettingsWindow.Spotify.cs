using System.Globalization;
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
        private sealed partial class SpotifyPage(SettingsWindow window)
        {
            private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#1ED760"));
            private static readonly IBrush GreenHover = new SolidColorBrush(Color.Parse("#3BE477"));
            private static readonly IBrush OnGreen = new SolidColorBrush(Color.Parse("#000000"));
            private static readonly IBrush ChipOn = new SolidColorBrush(Color.Parse("#FFFFFF"));
            private static readonly IBrush ChipOnText = new SolidColorBrush(Color.Parse("#000000"));
            private const double CardWidth = 176, CardArt = 152, TopArt = 92, HeaderArt = 192, RowArt = 40;
            private const int AllSongs = 4, AllCards = 6;
            private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(450);

            private static readonly (string Key, string Label, string[] Types)[] Filters =
            [
                ("all", "All", ["track", "artist", "album", "playlist", "show"]), ("track", "Songs", ["track"]), ("artist", "Artists", ["artist"]),
                ("album", "Albums", ["album"]), ("playlist", "Playlists", ["playlist"]), ("show", "Podcasts & Shows", ["show"])
            ];

            private readonly TextBlock _status = Subtitle();
            private readonly ComboBox _cboAmp = new() { Width = 200 };
            private readonly TextBox _clientId = new() { Watermark = "Paste your Client ID here", Width = 340 };
            private readonly Button _link = Pill("Link Spotify account", accent: true);
            private readonly StackPanel _steps = new();
            private readonly TextBlock _relinkNote = Muted("Spotify needs more permissions before you can add songs to Liked Songs and playlists. " +
                                                           "Unlink Spotify on the Services page, then link it again here.");
            private Border? _setup;
            private readonly TextBlock _premiumNote = Muted("This Spotify account doesn't have Premium. You can search, but Spotify only lets Premium accounts play from other programs.");
            private readonly Grid _browser = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), IsVisible = false, Margin = new Thickness(0, 16, 0, 0) };
            private readonly TextBox _search = new()
            {
                Watermark = "What do you want to play?", Height = 48, CornerRadius = new CornerRadius(24), MaxWidth = 480,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 16, 0)
            };
            private readonly Border _backButton = new();
            private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 14, 0, 14) };
            private readonly StackPanel _results = new();
            private readonly ScrollViewer _scroll = new() { Padding = new Thickness(0, 0, 12, 0) };
            private readonly DispatcherTimer _typing = new() { Interval = TypingPause };
            private readonly Stack<SpotifyItem?> _history = new();
            private readonly List<Bitmap> _bitmaps = [];
            private string _filter = "all";
            private string _query = string.Empty;
            private Dictionary<string, SpotifyResults>? _lastResults;
            private SpotifyItem? _shown;
            private int _version;
            private bool _started;
            private CancellationTokenSource? _linking;

            private const string ThisComputer = "computer";

            public void FillDevices()
            {
                bool here = AmpOf(_cboAmp) == ThisComputer;
                window.FillAmpCombo(_cboAmp);
                _cboAmp.Items.Add(ComboItem(ThisComputer, "This computer"));
                if (here || _cboAmp.SelectedIndex < 0) _cboAmp.SelectedIndex = _cboAmp.Items.Count - 1;
            }

            public Control Build()
            {
                ToolTip.SetTip(_cboAmp, "Where to play: a WiiM device, or the Spotify app on this computer");
                _clientId.Text = window._owner.SpotifyClientId;
                _clientId.LostFocus += (_, _) => window._owner.SpotifyClientId = _clientId.Text ?? string.Empty;
                _link.Click += async (_, _) => await LinkAsync();

                var dashboard = Pill("Open the Spotify developer dashboard");
                dashboard.Click += (_, _) => OpenUrl(SpotifyDashboardUrl);
                var redirect = new TextBox { Text = SpotifyRedirectUri, IsReadOnly = true, Width = 340 };
                var copy = Pill("Copy");
                copy.Click += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(window)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(SpotifyRedirectUri);
                    copy.Content = "Copied";
                    DispatcherTimer.RunOnce(() => copy.Content = "Copy", TimeSpan.FromSeconds(2));
                };
                _steps.Children.Add(Muted("Spotify asks every program that plays music to have its own key, called a Client ID. Making one is free and takes about two minutes:"));
                _steps.Children.Add(Step("1.", "Open the Spotify developer dashboard, sign in with your Spotify account and click Create app.", dashboard));
                _steps.Children.Add(Step("2.", "Give the app any name and description, add this Redirect URI, tick Web API and save.",
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { redirect, copy } }));
                _steps.Children.Add(Step("3.", "Open the app's settings, copy the Client ID and paste it here.", _clientId));
                _steps.Children.Add(Step("4.", "Click Link Spotify account and allow access in the browser window that opens.", _link));
                _steps.Children.Add(Muted("Spotify Premium is needed to play. Up to five Spotify accounts can use the same Client ID."));

                var setup = Card(null, _steps, _relinkNote, _premiumNote);
                _setup = setup;

                _search.InnerLeftContent = new PathIcon
                {
                    Data = Icons.Search, Width = 20, Height = 20, Margin = new Thickness(14, 0, 4, 0), Foreground = Palette.TextMuted
                };
                _search.TextChanged += (_, _) =>
                {
                    _typing.Stop();
                    _typing.Start();
                };
                _search.KeyDown += async (_, e) =>
                {
                    if (e.Key != Key.Enter) return;
                    e.Handled = true;
                    _typing.Stop();
                    await SearchAsync();
                };
                _typing.Tick += async (_, _) =>
                {
                    _typing.Stop();
                    if ((_search.Text?.Trim() ?? string.Empty) != _query || _shown != null) await SearchAsync();
                };
                _backButton.Child = new PathIcon { Data = Icons.ChevronLeft, Width = 22, Height = 22 };
                _backButton.Width = _backButton.Height = 40;
                _backButton.CornerRadius = new CornerRadius(20);
                _backButton.Background = Palette.Card;
                _backButton.Cursor = new Cursor(StandardCursorType.Hand);
                _backButton.Margin = new Thickness(0, 0, 12, 0);
                _backButton.VerticalAlignment = VerticalAlignment.Center;
                _backButton.IsVisible = false;
                ToolTip.SetTip(_backButton, "Go back");
                _backButton.PointerEntered += (_, _) => _backButton.Background = Palette.CardActive;
                _backButton.PointerExited += (_, _) => _backButton.Background = Palette.Card;
                _backButton.Tapped += async (_, _) => await GoBackAsync();
                var searchRow = new DockPanel();
                DockPanel.SetDock(_backButton, Dock.Left);
                searchRow.Children.Add(_backButton);
                searchRow.Children.Add(_search);
                foreach (var (key, label, _) in Filters) _chips.Children.Add(Chip(key, label));
                UpdateChips();
                _scroll.Content = _results;
                _browser.Children.Add(searchRow);
                Grid.SetRow(_chips, 1);
                _browser.Children.Add(_chips);
                Grid.SetRow(_scroll, 2);
                _browser.Children.Add(_scroll);
                var bar = BuildPlayerBar();
                Grid.SetRow(bar, 3);
                _browser.Children.Add(bar);

                var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
                body.Children.Add(setup);
                Grid.SetRow(_browser, 1);
                body.Children.Add(_browser);
                ShowState();
                var (bigPlayer, _) = IconPill(Icons.FullScreen, "Open the big player");
                bigPlayer.Click += (_, _) =>
                {
                    if (window.BigPlayerDevice() is { } ip) window.ShowNowPlayingView(ip);
                };
                window._idlePlayerDevice = () => _pageShown && AmpOf(_cboAmp) is { } ip && ip != ThisComputer ? ip : null;
                var page = window.Page("Spotify", _status, [], body, scroll: false, subtitleBelow: true, secondRow: [bigPlayer, window.IdleBigPlayerButton(), _cboAmp]);
                page.PropertyChanged += (_, e) =>
                {
                    if (e.Property != IsVisibleProperty) return;
                    if (page.IsVisible) ShowState();
                    UpdatePolling(page.IsVisible);
                };
                window.Closed += (_, _) =>
                {
                    _linking?.Cancel();
                    ClearResults();
                    ClearPlayer();
                };
                return page;
            }

            private static Grid Step(string number, string text, Control action)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*"), Margin = new Thickness(0, 6) };
                row.Children.Add(new TextBlock { Text = number, Classes = { "muted" } });
                var stack = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } } };
                action.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(action);
                Grid.SetColumn(stack, 1);
                row.Children.Add(stack);
                return row;
            }

            private void ShowState()
            {
                bool on = window._owner.SpotifyEnabled, linked = window._owner.SpotifyLinked;
                _steps.IsVisible = on && !linked;
                _relinkNote.IsVisible = on && window._owner.SpotifyNeedsRelink;
                _premiumNote.IsVisible = on && linked && !window._owner.SpotifyPremium;
                if (_setup != null) _setup.IsVisible = _steps.IsVisible || _relinkNote.IsVisible || _premiumNote.IsVisible;
                _browser.IsVisible = on && linked;
                _status.Text = !on ? "Switched off" : linked ? string.Empty : "Not linked yet";
                if (on && linked && !_started)
                {
                    _started = true;
                    _ = SearchAsync();
                }
                if (!linked) _started = false;
                UpdatePolling(_pageShown);
            }

            private async Task LinkAsync()
            {
                if (_linking != null)
                {
                    _linking.Cancel();
                    return;
                }
                window._owner.SpotifyClientId = _clientId.Text ?? string.Empty;
                if (window._owner.SpotifyClientId.Length == 0)
                {
                    await Dialogs.MessageAsync(window, "Paste the Client ID from your Spotify app first.", window._scale);
                    return;
                }
                _linking = new CancellationTokenSource();
                _link.Content = "Waiting for Spotify… click to cancel";
                string? problem;
                try { problem = await window._owner.LinkSpotifyAsync(_linking.Token); }
                catch (OperationCanceledException) { problem = null; }
                finally
                {
                    _linking.Dispose();
                    _linking = null;
                    _link.Content = "Link Spotify account";
                }
                if (problem != null) await Dialogs.MessageAsync(window, problem, window._scale);
                ShowState();
            }

            private Border Chip(string key, string label)
            {
                var text = new TextBlock { Text = label, FontSize = 13 };
                var chip = new Border
                {
                    Child = text, Tag = key, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6), Margin = new Thickness(0, 0, 8, 8),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                chip.PointerEntered += (_, _) => { if (_filter != key) chip.Background = Palette.InputHover; };
                chip.PointerExited += (_, _) => UpdateChips();
                chip.Tapped += async (_, _) =>
                {
                    if (_filter == key && _shown == null) return;
                    _filter = key;
                    UpdateChips();
                    await SearchAsync();
                };
                return chip;
            }

            private void UpdateChips()
            {
                foreach (var chip in _chips.Children.OfType<Border>())
                {
                    bool on = (string?)chip.Tag == _filter;
                    chip.Background = on ? ChipOn : Palette.Input;
                    if (chip.Child is TextBlock text) text.Foreground = on ? ChipOnText : Palette.Text;
                }
            }

            private void ClearResults()
            {
                _version++;
                _results.Children.Clear();
                foreach (var bitmap in _bitmaps) bitmap.Dispose();
                _bitmaps.Clear();
                _scroll.Offset = default;
                _rowTitles.Clear();
                _detailActions = null;
            }

            private async Task SearchAsync()
            {
                _history.Clear();
                _shown = null;
                _backButton.IsVisible = false;
                _query = _search.Text?.Trim() ?? string.Empty;
                ClearResults();
                int version = _version;
                if (_query.Length == 0)
                {
                    await ShowHomeAsync(version);
                    return;
                }
                _status.Text = "Searching…";
                var types = Filters.First(f => f.Key == _filter).Types;
                var results = await window._owner.SpotifySearchAsync(_query, types, 0, SpotifyPageSize);
                if (version != _version) return;
                _lastResults = results;
                ShowSearch(version);
            }

            private void ShowSearch(int version)
            {
                _status.Text = string.Empty;
                if (_lastResults is not { } results)
                {
                    ShowProblem("Spotify didn't answer the search. Check your internet connection and try again.");
                    return;
                }
                if (results.Values.All(r => r.Items.Count == 0))
                {
                    _results.Children.Add(Heading($"No results found for “{_query}”"));
                    _results.Children.Add(Muted("Please make sure your words are spelled correctly, or use fewer or different keywords."));
                    return;
                }
                if (_filter == "all") ShowAll(results, version);
                else ShowFiltered(results[_filter], version);
            }

            private void ShowProblem(string message)
            {
                _status.Text = string.Empty;
                if (!window._owner.SpotifyLinked)
                {
                    ShowState();
                    return;
                }
                _results.Children.Add(Muted(message));
            }

            private async Task ShowHomeAsync(int version)
            {
                _status.Text = "Loading your playlists…";
                var playlists = await window._owner.SpotifyMyPlaylistsAsync();
                if (version != _version) return;
                _status.Text = string.Empty;
                if (playlists == null)
                {
                    ShowProblem("Spotify didn't answer. Check your internet connection and try again.");
                    return;
                }
                if (playlists.Count == 0)
                {
                    _results.Children.Add(Muted("Search for songs, artists, albums, playlists and podcasts."));
                    return;
                }
                _results.Children.Add(Heading("Your playlists"));
                _results.Children.Add(CardGrid(playlists));
            }

            private static SpotifyItem? TopResult(Dictionary<string, SpotifyResults> results, string query)
            {
                IEnumerable<SpotifyItem> Of(string type) => results.TryGetValue(type, out var r) ? r.Items : [];
                foreach (string type in new[] { "artist", "track", "album", "playlist", "show" })
                    if (Of(type).FirstOrDefault(i => i.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)) is { } exact) return exact;
                return Of("artist").FirstOrDefault(a => a.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
                       ?? Of("track").FirstOrDefault() ?? Of("artist").FirstOrDefault() ?? Of("album").FirstOrDefault()
                       ?? Of("playlist").FirstOrDefault() ?? Of("show").FirstOrDefault();
            }

            private void ShowAll(Dictionary<string, SpotifyResults> results, int version)
            {
                var tracks = results["track"].Items;
                var top = TopResult(results, _query);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,7*"), Margin = new Thickness(0, 0, 0, 28) };
                if (top != null) row.Children.Add(new StackPanel { Children = { Heading("Top result"), TopCard(top) } });
                if (tracks.Count > 0)
                {
                    var songs = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
                    songs.Children.Add(Heading("Songs"));
                    var shown = tracks.Take(AllSongs).ToList();
                    for (int i = 0; i < shown.Count; i++)
                        songs.Children.Add(TrackRow(shown[i], null, showArt: true, showAlbum: false,
                            new SpotifyPlayback(null, [shown[i].Uri], null), version));
                    Grid.SetColumn(songs, 1);
                    row.Children.Add(songs);
                }
                _results.Children.Add(row);
                foreach (var (key, label) in new[] { ("artist", "Artists"), ("album", "Albums"), ("playlist", "Playlists"), ("show", "Podcasts & Shows") })
                {
                    var items = results[key].Items;
                    if (items.Count == 0) continue;
                    _results.Children.Add(Heading(label, () =>
                    {
                        _filter = key;
                        UpdateChips();
                        _ = SearchAsync();
                    }));
                    _results.Children.Add(CardGrid(items.Take(AllCards)));
                }
            }

            private void ShowFiltered(SpotifyResults first, int version)
            {
                var list = new List<SpotifyItem>(first.Items);
                int total = first.Total;
                Panel holder = _filter == "track" ? new StackPanel() : new WrapPanel();
                var more = Pill("Load more");
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.Margin = new Thickness(0, 12, 0, 0);
                void Add(IEnumerable<SpotifyItem> items)
                {
                    foreach (var item in items)
                    {
                        int number = holder.Children.Count + 1;
                        holder.Children.Add(_filter == "track"
                            ? TrackRow(item, number, showArt: true, showAlbum: true, new SpotifyPlayback(null, [item.Uri], null), version)
                            : ItemCard(item, version));
                    }
                    more.IsVisible = list.Count < total;
                }
                more.Click += async (_, _) =>
                {
                    more.IsEnabled = false;
                    more.Content = "Loading more…";
                    var next = await window._owner.SpotifySearchAsync(_query, [_filter], list.Count, SpotifyPageSize);
                    if (version != _version) return;
                    more.IsEnabled = true;
                    more.Content = "Load more";
                    if (next?.GetValueOrDefault(_filter) is not { } page || page.Items.Count == 0)
                    {
                        more.IsVisible = false;
                        return;
                    }
                    total = page.Total;
                    list.AddRange(page.Items);
                    Add(page.Items);
                };
                if (_filter == "track") _results.Children.Add(SongHeader(showArt: true, showAlbum: true));
                _results.Children.Add(holder);
                _results.Children.Add(more);
                Add(list);
            }

            private async Task OpenAsync(SpotifyItem item)
            {
                if (item.Kind is "track" or "episode") return;
                _history.Push(_shown);
                await ShowDetailAsync(item);
            }

            private async Task GoBackAsync()
            {
                if (_history.Count == 0) return;
                var previous = _history.Pop();
                if (previous != null)
                {
                    await ShowDetailAsync(previous);
                    return;
                }
                _shown = null;
                _backButton.IsVisible = false;
                ClearResults();
                if (_query.Length == 0) await ShowHomeAsync(_version);
                else ShowSearch(_version);
            }

            private async Task ShowDetailAsync(SpotifyItem item)
            {
                _shown = item;
                _backButton.IsVisible = true;
                ClearResults();
                int version = _version;
                _status.Text = "Loading…";
                var detail = await window._owner.SpotifyDetailAsync(item);
                if (version != _version) return;
                _status.Text = string.Empty;
                if (detail == null)
                {
                    ShowProblem("Spotify didn't answer. Check your internet connection and try again.");
                    return;
                }
                var shown = detail.Item;
                bool round = shown.Kind == "artist";
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 20) };
                header.Children.Add(Art(shown.ImageUrl, HeaderArt, round, IconFor(shown.Kind), version));
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 0, 0) };
                words.Children.Add(new TextBlock { Text = KindName(shown.Kind), FontSize = 13 });
                words.Children.Add(new TextBlock
                {
                    Text = shown.Name, FontSize = 40, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 8)
                });
                if (detail.Info.Length > 0 && detail.Info != KindName(shown.Kind))
                    words.Children.Add(new TextBlock { Text = detail.Info, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(words, 1);
                header.Children.Add(words);
                _results.Children.Add(header);

                var context = shown.Kind == "artist" && detail.Tracks.Count > 0 && detail.Albums.Count == 0
                    ? new SpotifyPlayback(null, detail.Tracks.Select(t => t.Uri).ToList(), null)
                    : new SpotifyPlayback(shown.Uri, null, null);
                _results.Children.Add(ActionRow(shown, context, detail));

                if (shown.Kind == "artist")
                {
                    if (detail.Tracks.Count > 0)
                    {
                        _results.Children.Add(Heading("Popular"));
                        var uris = detail.Tracks.Select(t => t.Uri).ToList();
                        for (int i = 0; i < detail.Tracks.Count; i++)
                            _results.Children.Add(TrackRow(detail.Tracks[i], i + 1, showArt: true, showAlbum: false,
                                new SpotifyPlayback(null, uris, detail.Tracks[i].Uri), version));
                    }
                    if (detail.Albums.Count > 0)
                    {
                        _results.Children.Add(Heading("Discography"));
                        _results.Children.Add(CardGrid(detail.Albums));
                    }
                    return;
                }
                bool album = shown.Kind == "album", show = shown.Kind == "show";
                if (detail.Tracks.Count > 0 && !show) _results.Children.Add(SongHeader(showArt: !album, showAlbum: !album));
                for (int i = 0; i < detail.Tracks.Count; i++)
                {
                    var track = detail.Tracks[i];
                    var playback = show ? new SpotifyPlayback(null, [track.Uri], null) : new SpotifyPlayback(shown.Uri, null, track.Uri);
                    _results.Children.Add(TrackRow(track, show ? null : i + 1, showArt: !album, showAlbum: !album && !show, playback, version));
                }
                if (detail.TracksHidden)
                    _results.Children.Add(Muted("Spotify only lists the songs of playlists you made or collaborate on. You can still play the whole playlist with the play button."));
            }

            private static string KindName(string kind) => kind switch
            {
                "album" => "Album", "artist" => "Artist", "playlist" => "Playlist", "show" => "Podcast", "episode" => "Episode", _ => "Song"
            };

            private static StreamGeometry IconFor(string kind) => kind switch
            {
                "album" => Icons.Album, "artist" => Icons.Speaker, "playlist" => Icons.Queue, "show" or "episode" => Icons.Radio, _ => Icons.Music
            };

            private static Control Heading(string text, Action? showAll = null)
            {
                var title = new TextBlock { Text = text, FontSize = 22, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Bottom };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 12) };
                row.Children.Add(title);
                if (showAll != null)
                {
                    var link = new TextBlock
                    {
                        Text = "Show all", FontSize = 13, FontWeight = FontWeight.SemiBold, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Bottom,
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };
                    link.PointerEntered += (_, _) => link.TextDecorations = TextDecorations.Underline;
                    link.PointerExited += (_, _) => link.TextDecorations = null;
                    link.Tapped += (_, _) => showAll();
                    Grid.SetColumn(link, 1);
                    row.Children.Add(link);
                }
                return row;
            }

            private Border Art(string url, double size, bool round, StreamGeometry icon, int version)
            {
                var image = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
                var placeholder = new PathIcon
                {
                    Data = icon, Width = size * 0.35, Height = size * 0.35, Foreground = Palette.TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var art = new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Min(8, size / 10)), ClipToBounds = true,
                    Background = Palette.Input, Child = new Panel { Children = { placeholder, image } }
                };
                if (url.Length > 0) _ = LoadArtAsync(url, (int)Math.Ceiling(size * 2), image, placeholder, version);
                return art;
            }

            private async Task LoadArtAsync(string url, int width, Image image, PathIcon placeholder, int version)
            {
                var bitmap = await window._owner.LoadThumbnailAsync(url, width, () => version == _version);
                if (bitmap == null) return;
                if (version != _version)
                {
                    bitmap.Dispose();
                    return;
                }
                _bitmaps.Add(bitmap);
                image.Source = bitmap;
                image.IsVisible = true;
                placeholder.IsVisible = false;
            }

            private Border PlayCircle(double size, Func<Task> play, bool light = false)
            {
                IBrush normal = light ? ChipOn : Green, hover = light ? Palette.TextMuted : GreenHover;
                var circle = new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = normal, Cursor = new Cursor(StandardCursorType.Hand),
                    BoxShadow = light ? default : BoxShadows.Parse("0 8 16 0 #60000000"),
                    Child = new PathIcon
                    {
                        Data = Icons.Play, Width = size * 0.42, Height = size * 0.42, Foreground = OnGreen,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                ToolTip.SetTip(circle, "Play");
                circle.PointerEntered += (_, _) => circle.Background = hover;
                circle.PointerExited += (_, _) => circle.Background = normal;
                circle.Tapped += async (_, e) =>
                {
                    e.Handled = true;
                    await play();
                };
                return circle;
            }

            private static SpotifyPlayback PlaybackFor(SpotifyItem item) =>
                item.Kind is "track" or "episode" ? new SpotifyPlayback(null, [item.Uri], null) : new SpotifyPlayback(item.Uri, null, null);

            private Border TopCard(SpotifyItem item)
            {
                int version = _version;
                var play = PlayCircle(48, () => PlayAsync(PlaybackFor(item), item.Name));
                play.IsVisible = false;
                play.HorizontalAlignment = HorizontalAlignment.Right;
                play.VerticalAlignment = VerticalAlignment.Bottom;
                string label = item.Kind == "track" ? $"Song • {item.Subtitle}" : item.Kind == "album" ? $"Album • {item.Subtitle}" : KindName(item.Kind);
                var words = new StackPanel
                {
                    Children =
                    {
                        LeftAligned(Art(item.ImageUrl, TopArt, item.Kind == "artist", IconFor(item.Kind), version)),
                        new TextBlock
                        {
                            Text = item.Name, FontSize = 28, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 18, 0, 6)
                        },
                        new TextBlock { Text = label, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 60, 0) }
                    }
                };
                var card = new Border
                {
                    Background = Palette.Card, CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Panel { Children = { words, play } }
                };
                card.PointerEntered += (_, _) =>
                {
                    card.Background = Palette.CardActive;
                    play.IsVisible = true;
                };
                card.PointerExited += (_, _) =>
                {
                    card.Background = Palette.Card;
                    play.IsVisible = false;
                };
                card.Tapped += async (_, _) =>
                {
                    if (item.Kind is "track" or "episode") await PlayAsync(PlaybackFor(item), item.Name);
                    else await OpenAsync(item);
                };
                return card;
            }

            private static Control LeftAligned(Control control)
            {
                control.HorizontalAlignment = HorizontalAlignment.Left;
                return control;
            }

            private WrapPanel CardGrid(IEnumerable<SpotifyItem> items)
            {
                var grid = new WrapPanel { Margin = new Thickness(-12, 0, 0, 20) };
                foreach (var item in items) grid.Children.Add(ItemCard(item, _version));
                return grid;
            }

            private Border ItemCard(SpotifyItem item, int version)
            {
                bool round = item.Kind == "artist";
                var play = PlayCircle(44, () => PlayAsync(PlaybackFor(item), item.Name));
                play.IsVisible = false;
                play.HorizontalAlignment = HorizontalAlignment.Right;
                play.VerticalAlignment = VerticalAlignment.Bottom;
                play.Margin = new Thickness(0, 0, 8, 8);
                var art = new Panel { Children = { Art(item.ImageUrl, CardArt, round, IconFor(item.Kind), version), play } };
                var words = new StackPanel
                {
                    Children =
                    {
                        art,
                        new TextBlock
                        {
                            Text = item.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 2)
                        },
                        new TextBlock
                        {
                            Text = item.Subtitle, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                };
                var card = new Border
                {
                    Width = CardWidth, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = words
                };
                card.PointerEntered += (_, _) =>
                {
                    card.Background = Palette.Card;
                    play.IsVisible = true;
                };
                card.PointerExited += (_, _) =>
                {
                    card.Background = Brushes.Transparent;
                    play.IsVisible = false;
                };
                card.Tapped += async (_, _) =>
                {
                    if (item.Kind is "track" or "episode") await PlayAsync(PlaybackFor(item), item.Name);
                    else await OpenAsync(item);
                };
                return card;
            }

            private static Grid SongHeader(bool showArt, bool showAlbum)
            {
                var columns = new List<string> { "36" };
                if (showArt) columns.Add(RowArt.ToString(CultureInfo.InvariantCulture));
                columns.Add("*");
                if (showAlbum) columns.Add("*");
                columns.Add("64");
                columns.Add("44");
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', columns)), Margin = new Thickness(8, 0, 8, 8) };
                void Add(string text, int column, HorizontalAlignment align = HorizontalAlignment.Left, int span = 1)
                {
                    var block = new TextBlock { Text = text, Classes = { "muted", "small" }, HorizontalAlignment = align };
                    Grid.SetColumn(block, column);
                    Grid.SetColumnSpan(block, span);
                    grid.Children.Add(block);
                }
                Add("#", 0, HorizontalAlignment.Center);
                Add("Title", 1, span: showArt ? 2 : 1);
                if (showAlbum) Add("Album", showArt ? 3 : 2);
                Add("Time", columns.Count - 2, HorizontalAlignment.Right);
                return new Grid { Children = { grid, new Border { Height = 1, Background = Palette.Input, VerticalAlignment = VerticalAlignment.Bottom } } };
            }

            private static string Duration(int ms)
            {
                var time = TimeSpan.FromMilliseconds(ms);
                return time.TotalHours >= 1
                    ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                    : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
            }

            private Border TrackRow(SpotifyItem track, int? number, bool showArt, bool showAlbum, SpotifyPlayback playback, int version)
            {
                var columns = new List<string>();
                if (number != null) columns.Add("36");
                if (showArt) columns.Add("Auto");
                columns.Add("*");
                if (showAlbum) columns.Add("*");
                columns.Add("64");
                columns.Add("44");
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', columns)) };
                int column = 0;
                var playIcon = new PathIcon { Data = Icons.Play, Width = 16, Height = 16, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
                TextBlock? numberText = null;
                if (number is int n)
                {
                    numberText = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center };
                    var cell = new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { numberText, playIcon } };
                    grid.Children.Add(cell);
                    column++;
                }
                if (showArt)
                {
                    var art = new Panel { Children = { Art(track.ImageUrl, RowArt, false, IconFor(track.Kind), version) } };
                    if (number == null)
                    {
                        playIcon.VerticalAlignment = VerticalAlignment.Center;
                        art.Children.Add(new Border
                        {
                            Width = RowArt, Height = RowArt, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#80000000")),
                            IsVisible = false, Child = playIcon, Tag = "shade"
                        });
                        playIcon.IsVisible = true;
                    }
                    Grid.SetColumn(art, column++);
                    grid.Children.Add(art);
                }
                var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(showArt ? 12 : 0, 0, 12, 0) };
                var name = new TextBlock { Text = track.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                _rowTitles.Add((track.Uri, name));
                if (_player?.Item?.Uri == track.Uri) name.Foreground = Green;
                title.Children.Add(name);
                var by = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                if (track.Explicit)
                    by.Children.Add(new Border
                    {
                        Background = Palette.TextMuted, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = "E", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Palette.Window }
                    });
                by.Children.Add(new TextBlock { Text = track.Subtitle, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
                title.Children.Add(by);
                Grid.SetColumn(title, column++);
                grid.Children.Add(title);
                if (showAlbum)
                {
                    var album = new TextBlock
                    {
                        Text = track.Album, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    };
                    Grid.SetColumn(album, column++);
                    grid.Children.Add(album);
                }
                var time = new TextBlock
                {
                    Text = track.DurationMs > 0 ? Duration(track.DurationMs) : string.Empty, Classes = { "muted", "small" },
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(time, column);
                grid.Children.Add(time);
                var more = MoreButton(track, inAlbum: !showArt && !showAlbum && track.Kind == "track");
                Grid.SetColumn(more, column + 1);
                grid.Children.Add(more);

                var row = new Border
                {
                    Child = grid, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(row, $"Play {track.Name}");
                void Hover(bool on)
                {
                    row.Background = on ? Palette.Card : Brushes.Transparent;
                    if (numberText != null)
                    {
                        numberText.IsVisible = !on;
                        playIcon.IsVisible = on;
                    }
                    foreach (var shade in grid.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<Border>()).Where(b => (string?)b.Tag == "shade"))
                        shade.IsVisible = on;
                }
                row.PointerEntered += (_, _) => Hover(true);
                row.PointerExited += (_, _) => Hover(false);
                row.Tapped += async (_, _) => await PlayTrackOrToggleAsync(track, playback);
                return row;
            }

            private async Task PlayAsync(SpotifyPlayback playback, string what)
            {
                if (AmpOf(_cboAmp) is not { } ip) return;
                bool here = ip == ThisComputer;
                string name = here ? "this computer" : window.DeviceName(window.TargetOf(ip));
                _status.Text = $"Starting {what} on {name}…";
                string? problem = here ? await window._owner.SpotifyPlayHereAsync(playback) : await window._owner.SpotifyPlayAsync(name, playback);
                if (problem != null)
                {
                    _status.Text = string.Empty;
                    await Dialogs.MessageAsync(window, problem, window._scale);
                    if (!window._owner.SpotifyLinked) ShowState();
                    return;
                }
                _status.Text = $"Playing {what} on {name}";
                await AfterPlayStartedAsync();
                if (here) return;
                await Task.Delay(1500);
                _ = window._owner.RefreshNowPlayingAsync();
                if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
            }
        }
    }
}
