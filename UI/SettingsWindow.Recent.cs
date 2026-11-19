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
        private sealed partial class RecentPage(SettingsWindow window)
        {
            private readonly ComboBox _cboRecentAmp = new() { Width = 200 };
            private readonly TextBlock _lblRecentStatus = Subtitle();
            private readonly StackPanel _recentRows = new();
            private readonly List<Bitmap> _recentBitmaps = [];
            private Control? _recentPage;
            private bool _recentPlaying;
            private int _recentVersion;

            public void FillDevices() => window.FillAmpCombo(_cboRecentAmp);

            public Control Build()
            {
                ToolTip.SetTip(_cboRecentAmp, "The device to play on");
                var clear = Pill("Clear");
                clear.Click += async (_, _) =>
                {
                    if (window._owner.Recent.Count == 0 || !await Dialogs.ConfirmAsync(window, "Clear the list of recently played music?", window._scale)) return;
                    window._owner.ClearRecent();
                };
                var body = new StackPanel { Children = { _recentRows } };
                var page = window.Page("Recent", _lblRecentStatus, [_cboRecentAmp, clear], body);
                _recentPage = page;
                page.PropertyChanged += (_, e) =>
                {
                    if (e.Property == IsVisibleProperty && page.IsVisible) RenderRecent();
                };
                window._owner.RecentChanged += OnRecentChanged;
                window.Closed += (_, _) =>
                {
                    window._owner.RecentChanged -= OnRecentChanged;
                    ClearRecentRows();
                };
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
                if (window._owner.Recent.Count == 0)
                    _recentRows.Children.Add(Muted("Nothing played yet."));
                foreach (var item in window._owner.Recent) _recentRows.Children.Add(RecentRow(item));
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
                string kind = radio ? "Radio" : item.Library!.IsLyrion ? "Lyrion" : item.Library.IsShare ? "Shared folder"
                    : item.Library.IsEmbyJellyfin ? window._owner.EmbyProductOf(item.Library.ServerUdn) : "Library";
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
                var bitmap = await window._owner.LoadThumbnailAsync(url, 88, () => version == _recentVersion);
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
                    await Dialogs.MessageAsync(window, "Add a WiiM device on the Devices page first.", window._scale);
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
                string target = window.TargetOf(ip);
                _lblRecentStatus.Text = $"Starting {station.Name}…";
                if (!await window._owner.PlayStationAsync(target, station))
                {
                    _lblRecentStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(window, "The device didn't start the station. Check that it's switched on and reachable.", window._scale);
                    return;
                }
                window._owner.AddRecent(new RecentItem(station.Name, station.Detail, station.ArtUrl, DateTime.Now, station, null));
                string name = window._owner._knownDevices.FirstOrDefault(d => d.Ip == target).Name ?? target;
                _lblRecentStatus.Text = $"Playing {station.Name} on {name}";
                await Task.Delay(2000);
                _ = window._owner.RefreshNowPlayingAsync();
                if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
            }

            private async Task PlayRecentLibraryAsync(string ip, RecentItem item, RecentLibrary library)
            {
                _lblRecentStatus.Text = $"Loading {item.Title}…";
                var server = new MediaServer(library.ServerName, library.ServerUdn, library.ServerUrl, string.Empty, IsLyrion: library.IsLyrion,
                    IsShare: library.IsShare, IsEmbyJellyfin: library.IsEmbyJellyfin);
                var collected = await window._library.CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
                if (collected == null && library.IsShare && await window._library.AskSharedFolderLoginAsync(server))
                    collected = await window._library.CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
                if (collected == null && library.IsEmbyJellyfin && await window._library.AskEmbySignInAsync(server))
                    collected = await window._library.CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
                if (collected == null && !library.IsShare && !library.IsEmbyJellyfin)
                {
                    _lblRecentStatus.Text = $"Looking for {library.ServerName}…";
                    var upnp = Upnp.DiscoverMediaServersAsync(TimeSpan.FromSeconds(3));
                    var lyrion = Lms.DiscoverAsync(TimeSpan.FromSeconds(3));
                    await Task.WhenAll(upnp, lyrion);
                    window._owner._mediaServers = upnp.Result.Concat(lyrion.Result).ToList();
                    if (window._owner._mediaServers.FirstOrDefault(s => s.Udn == library.ServerUdn) is { } found)
                    {
                        server = found;
                        collected = await window._library.CollectFolderTracksAsync(server, library.FolderId, library.FolderIndex);
                    }
                }
                if (collected is not { Tracks.Count: > 0 } c)
                {
                    _lblRecentStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(window, collected == null
                        ? $"Couldn't reach {library.ServerName}. Check that the music server is running."
                        : $"{item.Title} has no tracks any more.", window._scale);
                    return;
                }
                await window._library.PlayLibraryTracksAsync(ip, new LibraryPage.PlaySource(server, library.FolderId, item.Title, library.FolderIndex, item.ArtUrl),
                    c.Tracks, c.Start, _lblRecentStatus);
            }

            private async Task PlayRecentRoonAsync(string ip, RecentItem item, RecentLibrary library)
            {
                string target = window.TargetOf(ip);
                string name = window.DeviceName(target);
                _lblRecentStatus.Text = "Asking Roon…";
                window._owner.UseRoonCore(library.ServerUrl);
                if (await window._owner.GetRegisteredRoonAsync(TimeSpan.FromSeconds(5)) == null)
                {
                    _lblRecentStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(window, window._owner.RoonStatus == RoonState.WaitingForApproval
                        ? "Allow Wiim Control in Roon first: open Roon, go to Settings → Extensions and click Enable next to Wiim Control."
                        : $"Couldn't reach {library.ServerName}. Check that Roon is running.", window._scale);
                    return;
                }
                if (await window._owner.RoonZoneForAsync(name) is not { } zone)
                {
                    _lblRecentStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(window,
                        $"{name} isn't a zone in Roon. In Roon, open Settings → Audio and enable {name}, and give it the same name as in Wiim Control.", window._scale);
                    return;
                }
                var path = (library.RoonPath ?? []).Select(s => (Id: s.Input.Length > 0 ? $"{RoonInputSeparator}{s.Input}" : string.Empty,
                    Title: s.Input.Length > 0 ? $"{s.Title}: {s.Input}" : s.Title)).ToList();
                var entry = new MediaEntry(string.Empty, library.RoonEntry, library.RoonContainer, null,
                    library.RoonContainer ? "object.container.roon" : "object.item.audioItem.roon.action_list", string.Empty, string.Empty, string.Empty,
                    "roon", string.Empty, TimeSpan.Zero);
                string action = library.RoonAction.Length > 0 ? library.RoonAction : RoonPlayNow;
                string? error = await window._owner.RoonActionAsync(path, entry, zone, action);
                if (error != null)
                {
                    _lblRecentStatus.Text = string.Empty;
                    await Dialogs.MessageAsync(window, error, window._scale);
                    return;
                }
                window._owner.AddRecent(item with { Played = DateTime.Now });
                _lblRecentStatus.Text = action == RoonStartRadio ? $"Started Roon Radio from {item.Title} on {name}" : $"Playing {item.Title} on {name}";
                await Task.Delay(1500);
                _ = window._owner.RefreshNowPlayingAsync();
                if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
            }
        }
    }
}
