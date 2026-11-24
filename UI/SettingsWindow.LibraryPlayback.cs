using System.Globalization;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage
        {
            private static readonly TimeSpan ServiceSettleTime = TimeSpan.FromSeconds(2);

            private async Task AddLibraryToQueueAsync(MediaEntry entry, int index, bool next)
            {
                if (SelectedLibraryServer is not { } server) return;
                bool local = OnComputer;
                if (local && !await ComputerCanPlayAsync(server)) return;
                string? ip = AmpOf(_cboLibraryAmp);
                if (!local && ip == null)
                {
                    await Dialogs.MessageAsync(window, "Add a WiiM device on the Devices page first.", window._scale);
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
                        await Dialogs.MessageAsync(window, $"{entry.Title} has no tracks of its own. Open it to pick an album or a track.", window._scale);
                        return;
                    }
                }
                if (local)
                {
                    bool starts = !window._owner.LocalPlayerActive;
                    if (!window._owner.AddToLocalQueue(tracks.Take(MaxQueueTracks).Select(ToLocalTrack).ToList(), next))
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window, "Windows couldn't start the player on this computer.", window._scale);
                        return;
                    }
                    string queued = tracks.Count == 1 ? tracks[0].Title : $"{entry.Title} ({tracks.Count} tracks)";
                    _lblLibraryStatus.Text = starts ? $"Playing {queued} on this computer" : next ? $"{queued} plays next" : $"Added {queued} to the queue";
                    return;
                }
                string target = window.TargetOf(ip!);
                var added = await window._owner.AddToQueueAsync(target, server, tracks, next);
                if (added == null)
                {
                    await PlayLibraryTracksAsync(ip, new PlaySource(server, entry.IsContainer ? entry.Id : _libraryPath[^1].Id,
                        entry.IsContainer ? entry.Title : _libraryPath[^1].Title, entry.IsContainer ? 0 : index, entry.ArtUrl), tracks, 0, _lblLibraryStatus);
                    return;
                }
                if (added == false)
                {
                    UpdateLibraryStatus();
                    await Dialogs.MessageAsync(window, "The device didn't accept the tracks.", window._scale);
                    return;
                }
                string what = tracks.Count == 1 ? tracks[0].Title : $"{entry.Title} ({tracks.Count} tracks)";
                _lblLibraryStatus.Text = next ? $"{what} plays next" : $"Added {what} to the queue";
            }

            public sealed record PlaySource(MediaServer Server, string FolderId, string FolderTitle, int FolderIndex, string FolderArt);

            private void ResetStatus(TextBlock status)
            {
                if (ReferenceEquals(status, _lblLibraryStatus)) UpdateLibraryStatus();
                else status.Text = string.Empty;
            }

            private async Task<bool> PlayOnLyrionAsync(string ip, MediaServer server, List<string> trackIds, int start, TextBlock status)
            {
                var result = await window._owner.PlayOnLyrionAsync(ip, server, trackIds, start);
                string amp = window._owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
                if (result == LyrionPlayResult.NotConnected)
                {
                    string host = new Uri(server.ContentDirectoryUrl).Host;
                    bool switchedOff = await window._owner.GetStreamServiceActiveAsync(ip, SqueezeliteService) == false;
                    if (!await Dialogs.ConfirmAsync(window, switchedOff
                            ? $"Squeezelite is switched off on {amp}, so it can't play from {server.Name}. Switch it on and connect now?"
                            : $"{amp} isn't connected to {server.Name}. Connect it now?", window._scale)) return false;
                    status.Text = $"Connecting {amp} to {server.Name}…";
                    if (switchedOff)
                    {
                        if (!await window._owner.SetStreamServiceActiveAsync(ip, SqueezeliteService, true))
                        {
                            ResetStatus(status);
                            await Dialogs.MessageAsync(window, $"{amp} didn't switch Squeezelite on.", window._scale);
                            return false;
                        }
                        await Task.Delay(ServiceSettleTime);
                    }
                    bool connected = await window._owner.ConnectSqueezeliteAsync(ip, host) && await window._owner.WaitForLyrionPlayerAsync(ip, server);
                    if (!connected)
                    {
                        ResetStatus(status);
                        await Dialogs.MessageAsync(window, $"{amp} didn't connect to {server.Name}.", window._scale);
                        return false;
                    }
                    result = await window._owner.PlayOnLyrionAsync(ip, server, trackIds, start);
                }
                if (result == LyrionPlayResult.Played) return true;
                ResetStatus(status);
                await Dialogs.MessageAsync(window, $"{server.Name} didn't start playing on {amp}.", window._scale);
                return false;
            }

            private static QueueTrack ToQueueTrack(MediaEntry e) => QueueTrackOf(e);

            private static QueueTrack ToLocalTrack(MediaEntry e) => QueueTrackOf(e) with { ArtUrl = e.ArtUrl };

            private void ShowLocalProblem(string message) => _lblLibraryStatus.Text = message;

            private async Task<bool> ComputerCanPlayAsync(MediaServer server)
            {
                if (!server.IsLyrion && !server.IsRoon) return true;
                await Dialogs.MessageAsync(window, $"Music from {server.Name} plays through {(server.IsRoon ? "Roon" : "Lyrion")}, so it can't play on this " +
                                                   "computer from here. Pick a WiiM device to play it.", window._scale);
                return false;
            }

            private Task<bool> PlayHereAsync(PlaySource source, List<MediaEntry> tracks, int start) =>
                OnComputer ? PlayLocallyAsync(source, tracks, start) : PlayLibraryTracksAsync(AmpOf(_cboLibraryAmp), source, tracks, start, _lblLibraryStatus);

            private async Task<bool> PlayLocallyAsync(PlaySource source, List<MediaEntry> tracks, int start)
            {
                if (tracks.Count == 0 || !await ComputerCanPlayAsync(source.Server)) return false;
                var queue = tracks.Take(MaxQueueTracks).Select(ToLocalTrack).ToList();
                start = Math.Clamp(start, 0, queue.Count - 1);
                if (!window._owner.PlayLocally(queue, start))
                {
                    UpdateLibraryStatus();
                    await Dialogs.MessageAsync(window, "Windows couldn't start the player on this computer.", window._scale);
                    return false;
                }
                AddLibraryRecent(source, queue, start);
                _lblLibraryStatus.Text = $"Playing {queue[start].Title} on this computer";
                return true;
            }

            private void AddLibraryRecent(PlaySource source, List<QueueTrack> queue, int start)
            {
                var server = source.Server;
                var first = queue[start];
                var detail = new List<string>();
                string artist = queue.Select(t => t.Artist).FirstOrDefault(a => a.Length > 0) ?? string.Empty;
                if (artist.Length > 0) detail.Add(artist);
                if (start > 0) detail.Add($"from {first.Title}");
                detail.Add(server.Name);
                window._owner.AddRecent(new RecentItem(source.FolderTitle, string.Join(" · ", detail),
                    first.ArtUrl.Length > 0 ? first.ArtUrl : source.FolderArt, DateTime.Now, null,
                    new RecentLibrary(server.Name, server.Udn, server.ContentDirectoryUrl, server.IsLyrion, source.FolderId, source.FolderIndex,
                        IsShare: server.IsShare, IsEmbyJellyfin: server.IsEmbyJellyfin)));
            }

            public async Task<(List<MediaEntry> Tracks, int Start)?> CollectFolderTracksAsync(MediaServer server, string folderId, int index)
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
                await PlayHereAsync(new PlaySource(server, folder.Id, folder.Title, index, string.Empty), tracks, start);
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
                    await Dialogs.MessageAsync(window, page == null
                        ? "The music server didn't answer."
                        : $"{folder.Title} has no tracks of its own. Open it to pick an album or a track.", window._scale);
                    return;
                }
                int first = page.Entries.FindIndex(e => e.IsAudio);
                await PlayHereAsync(new PlaySource(server, folder.Id, folder.Title, Math.Max(0, first), folder.ArtUrl), tracks, 0);
            }

            public async Task<bool> PlayLibraryTracksAsync(string? ip, PlaySource source, List<MediaEntry> tracks, int start, TextBlock status)
            {
                if (ip == null)
                {
                    await Dialogs.MessageAsync(window, "Add a WiiM device on the Devices page first.", window._scale);
                    return false;
                }
                if (tracks.Count == 0 || _libraryPlaying) return false;
                var server = source.Server;
                string target = window.TargetOf(ip);
                var queue = tracks.Take(MaxQueueTracks).Select(ToQueueTrack).ToList();
                start = Math.Clamp(start, 0, queue.Count - 1);
                _libraryPlaying = true;
                bool played;
                try
                {
                    played = server.IsLyrion
                        ? await PlayOnLyrionAsync(target, server, tracks.Take(MaxQueueTracks).Select(Lms.TrackId).OfType<string>().ToList(), start, status)
                        : await window._owner.PlayTracksAsync(target, queue, start, server);
                }
                finally { _libraryPlaying = false; }
                if (!played)
                {
                    if (server.IsLyrion) return false;
                    ResetStatus(status);
                    await Dialogs.MessageAsync(window, "The device didn't accept the tracks. Check that it's switched on and reachable.", window._scale);
                    return false;
                }

                var first = queue[start];
                AddLibraryRecent(source, queue, start);

                string name = window._owner._knownDevices.FirstOrDefault(d => d.Ip == target).Name ?? target;
                status.Text = string.Create(CultureInfo.InvariantCulture, $"Playing {first.Title} on {name}");
                await Task.Delay(1500);
                _ = window._owner.RefreshNowPlayingAsync();
                if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
                return true;
            }
        }
    }
}
