namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage
        {
            private bool _roonHooked;

            private void HookRoonStatus()
            {
                if (_roonHooked) return;
                _roonHooked = true;
                window._owner.RoonStatusChanged += OnRoonStatusChanged;
                window.Closed += (_, _) => window._owner.RoonStatusChanged -= OnRoonStatusChanged;
            }

            private async void OnRoonStatusChanged()
            {
                if (SelectedLibraryServer is not { IsRoon: true } server || !_libraryPath.Any()) return;
                if (window._owner.RoonStatus == RoonState.Connected) await OpenLibraryRootAsync(server);
                else if (window._owner.RoonStatus == RoonState.WaitingForApproval) ShowRoonApprovalHint();
            }

            private void ShowRoonApprovalHint()
            {
                ClearLibraryRows();
                _libraryEntries = [];
                _libraryTotal = 0;
                UpdateLibraryButtons();
                _lblLibraryStatus.Text = "Waiting for approval in Roon";
                _libraryRows.Children.Add(Muted(
                    "Allow Wiim Control in Roon: open Roon, go to Settings → Extensions and click Enable next to Wiim Control. " +
                    "The library appears here as soon as it's enabled."));
            }

            private List<(string Id, string Title)> RoonPath => _libraryPath.Skip(1).ToList();

            private static bool HasRoonPlay(MediaEntry entry) => entry.Artist.Length > 0 || entry.ArtUrl.Length > 0;

            private async Task<bool> OpenRoonInputAsync(MediaEntry folder, int depth)
            {
                string? text = await Dialogs.PromptAsync(window, $"{folder.Title} in Roon:", string.Empty, window._scale);
                if (string.IsNullOrWhiteSpace(text)) return true;
                text = text.Trim();
                if (_libraryPath.Count > depth) _libraryPath.RemoveRange(depth, _libraryPath.Count - depth);
                _libraryPath.Add(($"{folder.Id}{RoonInputSeparator}{text}", $"{folder.Title}: {text}"));
                await LoadLibraryFolderAsync();
                return true;
            }

            private async Task RoonPlayAsync(MediaEntry entry, string action)
            {
                if (AmpOf(_cboLibraryAmp) is not { } ip)
                {
                    await Dialogs.MessageAsync(window, "Add a WiiM device on the Devices page first.", window._scale);
                    return;
                }
                if (_libraryPlaying) return;
                string target = window.TargetOf(ip);
                string name = window.DeviceName(target);
                _libraryPlaying = true;
                string? error;
                try
                {
                    _lblLibraryStatus.Text = "Asking Roon…";
                    string? zone = await window._owner.RoonZoneForAsync(name);
                    if (zone == null)
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window,
                            $"{name} isn't a zone in Roon. In Roon, open Settings → Audio and enable {name}, and give it the same name as in Wiim Control.", window._scale);
                        return;
                    }
                    error = await window._owner.RoonActionAsync(RoonPath, entry, zone, action);
                }
                finally
                {
                    _libraryPlaying = false;
                }
                if (error != null)
                {
                    UpdateLibraryStatus();
                    await Dialogs.MessageAsync(window, error, window._scale);
                    return;
                }
                if (action is RoonPlayNow or RoonStartRadio) AddRoonRecent(entry, action);
                _lblLibraryStatus.Text = action switch
                {
                    RoonAddNext => $"{entry.Title} plays next on {name}",
                    RoonQueue => $"Added {entry.Title} to the queue on {name}",
                    RoonStartRadio => $"Started Roon Radio from {entry.Title} on {name}",
                    _ => $"Playing {entry.Title} on {name}"
                };
                await Task.Delay(1500);
                _ = window._owner.RefreshNowPlayingAsync();
                if (window.IsVisible) await window.RefreshDeviceStatusesAsync();
            }

            private void AddRoonRecent(MediaEntry entry, string action)
            {
                if (SelectedLibraryServer is not { } server) return;
                var path = RoonPath.Select(p =>
                {
                    string input = p.Id.IndexOf(RoonInputSeparator) is int i and >= 0 ? p.Id[(i + 1)..] : string.Empty;
                    string title = input.Length > 0 && p.Title.EndsWith($": {input}", StringComparison.Ordinal) ? p.Title[..^(input.Length + 2)] : p.Title;
                    return new RoonStep(title, input);
                }).ToList();
                bool playItem = !entry.IsContainer && entry.Title.StartsWith("Play ", StringComparison.OrdinalIgnoreCase) && _libraryPath.Count > 1;
                string title = playItem ? _libraryPath[^1].Title : entry.Title;
                var detail = new List<string>();
                if (entry.Artist.Length > 0) detail.Add(entry.Artist);
                if (action == RoonStartRadio) detail.Add("Roon Radio");
                detail.Add(server.Name);
                string folderId = string.Join('/', path.Select(p => p.Title).Append(entry.Title)) + (action == RoonStartRadio ? "#radio" : string.Empty);
                window._owner.AddRecent(new RecentItem(title, string.Join(" · ", detail), entry.ArtUrl, DateTime.Now, null,
                    new RecentLibrary(server.Name, server.Udn, server.ContentDirectoryUrl, false, folderId, 0,
                        IsRoon: true, RoonPath: path, RoonEntry: entry.Title, RoonContainer: entry.IsContainer, RoonAction: action)));
            }

            private async Task RoonPlayFolderAsync()
            {
                var entry = _libraryEntries.FirstOrDefault(e => !e.IsContainer && e.Title.StartsWith("Play ", StringComparison.OrdinalIgnoreCase))
                            ?? _libraryEntries.FirstOrDefault(e => e.IsAudio);
                if (entry != null) await RoonPlayAsync(entry, RoonPlayNow);
            }
        }
    }
}
