using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboRecentAmp = new() { Width = 200 };
        private readonly TextBlock _lblRecentStatus = Subtitle();
        private readonly StackPanel _recentRows = new();
        private readonly List<Bitmap> _recentBitmaps = [];
        private Control? _recentPage;
        private bool _recentPlaying;
        private int _recentVersion;

        private Control BuildRecentPage()
        {
            ToolTip.SetTip(_cboRecentAmp, "The device to play on");
            var clear = Pill("Clear");
            clear.Click += async (_, _) =>
            {
                if (_owner.Recent.Count == 0 || !await Dialogs.ConfirmAsync(this, "Clear the list of recently played music?", _scale)) return;
                _owner.ClearRecent();
            };
            var body = new StackPanel
            {
                Children =
                {
                    _recentRows,
                    Muted("Radio stations and albums or folders you play from the Library and Radio pages are listed here. Click one to play it again.")
                }
            };
            var page = Page("Recent", _lblRecentStatus, [_cboRecentAmp, clear], body);
            _recentPage = page;
            page.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && page.IsVisible) RenderRecent();
            };
            _owner.RecentChanged += OnRecentChanged;
            Closed += (_, _) => _owner.RecentChanged -= OnRecentChanged;
            return page;
        }

        private void OnRecentChanged()
        {
            if (_recentPage?.IsVisible == true) RenderRecent();
        }

        private void ClearRecentRows()
        {
            _recentVersion++;
            _recentRows.Children.Clear();
            foreach (var bitmap in _recentBitmaps) bitmap.Dispose();
            _recentBitmaps.Clear();
        }

        private void RenderRecent()
        {
            ClearRecentRows();
            if (_owner.Recent.Count == 0)
                _recentRows.Children.Add(Muted("Nothing played yet."));
            foreach (var item in _owner.Recent) _recentRows.Children.Add(RecentRow(item));
        }

        private static string Ago(DateTime played)
        {
            var age = DateTime.Now - played;
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalHours < 1) return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} min ago");
            if (played.Date == DateTime.Today) return played.ToString("t", CultureInfo.CurrentCulture);
            if (played.Date == DateTime.Today.AddDays(-1)) return "yesterday";
            return played.ToString("d MMM", CultureInfo.CurrentCulture);
        }

        private Control RecentRow(RecentItem item)
        {
            bool radio = item.Station != null;
            var icon = new PathIcon
            {
                Data = radio ? Icons.Radio : Icons.Album, Width = 20, Height = 20, Foreground = Palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var art = new Image { Stretch = radio ? Stretch.Uniform : Stretch.UniformToFill, IsVisible = false };
            var picture = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Input,
                Child = new Panel { Children = { icon, art } }
            };
            string kind = radio ? "Radio" : item.Library!.IsLyrion ? "Lyrion" : item.Library.IsShare ? "Shared folder" : "Library";
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0),
                Children =
                {
                    new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = string.Join(" · ", new[] { kind, item.Detail }.Where(s => s.Length > 0)),
                        Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            var when = new TextBlock { Text = Ago(item.Played), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(picture);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(when, 2);
            grid.Children.Add(when);
            var row = new Border
            {
                Child = grid, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(row, $"Play {item.Title}");
            row.PointerEntered += (_, _) => row.Background = Palette.Card;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.Tapped += async (_, _) => await PlayRecentAsync(item);
            if (item.ArtUrl.Length > 0) _ = LoadRecentArtAsync(item.ArtUrl, art, icon, _recentVersion);
            return row;
        }

        private async Task LoadRecentArtAsync(string url, Image art, PathIcon icon, int version)
        {
            var bitmap = await _owner.LoadThumbnailAsync(url, 88, () => version == _recentVersion);
            if (bitmap == null) return;
            if (version != _recentVersion)
            {
                bitmap.Dispose();
                return;
            }
            _recentBitmaps.Add(bitmap);
            art.Source = bitmap;
            art.IsVisible = true;
            icon.IsVisible = false;
        }

        private async Task PlayRecentAsync(RecentItem item)
        {
            if (_recentPlaying) return;
            if (AmpOf(_cboRecentAmp) is not { } ip)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return;
            }
            _recentPlaying = true;
            try
            {
                if (item.Station is { } station) await PlayRecentStationAsync(ip, station);
                else if (item.Library is { IsRoon: true } roon) await PlayRecentRoonAsync(ip, item, roon);
                else if (item.Library is { } library) await PlayRecentLibraryAsync(ip, item, library);
            }
            finally
            {
                _recentPlaying = false;
            }
        }

        private async Task PlayRecentStationAsync(string ip, RadioStation station)
        {
            string target = TargetOf(ip);
            _lblRecentStatus.Text = $"Starting {station.Name}…";
            if (!await _owner.PlayStationAsync(target, station))
            {
                _lblRecentStatus.Text = string.Empty;
                await Dialogs.MessageAsync(this, "The device didn't start the station. Check that it's switched on and reachable.", _scale);
                return;
            }
            _owner.AddRecent(new RecentItem(station.Name, station.Detail, station.ArtUrl, DateTime.Now, station, null));
            string name = _owner._knownDevices.FirstOrDefault(d => d.Ip == target).Name ?? target;
            _lblRecentStatus.Text = $"Playing {station.Name} on {name}";
            await Task.Delay(2000);
            _ = _owner.RefreshNowPlayingAsync();
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }

        private async Task PlayRecentLibraryAsync(string ip, RecentItem item, RecentLibrary library)
        {
            _lblRecentStatus.Text = $"Loading {item.Title}…";
            var server = new MediaServer(library.ServerName, library.ServerUdn, library.ServerUrl, string.Empty, IsLyrion: library.IsLyrion,
                IsShare: library.IsShare);
            var collected = await CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
            if (collected == null && library.IsShare && await AskSharedFolderLoginAsync(server))
                collected = await CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
            if (collected == null && !library.IsShare)
            {
                _lblRecentStatus.Text = $"Looking for {library.ServerName}…";
                var upnp = Upnp.DiscoverMediaServersAsync(TimeSpan.FromSeconds(3));
                var lyrion = Lms.DiscoverAsync(TimeSpan.FromSeconds(3));
                await Task.WhenAll(upnp, lyrion);
                _owner._mediaServers = upnp.Result.Concat(lyrion.Result).ToList();
                if (_owner._mediaServers.FirstOrDefault(s => s.Udn == library.ServerUdn) is { } found)
                {
                    server = found;
                    collected = await CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
                }
            }
            if (collected is not { Tracks.Count: > 0 } c)
            {
                _lblRecentStatus.Text = string.Empty;
                await Dialogs.MessageAsync(this, collected == null
                    ? $"Couldn't reach {library.ServerName}. Check that the music server is running."
                    : $"{item.Title} has no tracks any more.", _scale);
                return;
            }
            await PlayLibraryTracksAsync(ip, new PlaySource(server, library.FolderId, item.Title, library.FolderIndex, item.ArtUrl),
                c.Tracks, c.Start, _lblRecentStatus);
        }
    }
}
