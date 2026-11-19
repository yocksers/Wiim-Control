namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage
        {
            private async Task ShowLibrarySearchAsync(MediaServer server)
            {
                int check = ++_librarySearchCheck;
                if (server.IsShare)
                {
                    _librarySearchRow.IsVisible = false;
                    return;
                }
                bool can = await window._owner.CanSearchLibraryAsync(server);
                if (check == _librarySearchCheck) _librarySearchRow.IsVisible = can;
            }

            private async Task<BrowsePage?> SearchLibraryPageAsync(MediaServer server, string query, int start, int count, bool fresh)
            {
                string key = server.Udn + "\n" + query;
                if (fresh || _librarySearch is not { } last || last.Key != key) _librarySearch = (key, window._owner.SearchLibraryAsync(server, query));
                var search = _librarySearch.Value.Search;
                var found = await search;
                if (found == null)
                {
                    if (_librarySearch?.Search == search) _librarySearch = null;
                    return null;
                }
                return new BrowsePage(found.Skip(start).Take(count).ToList(), found.Count);
            }

            private async Task SearchLibraryAsync()
            {
                if (SelectedLibraryServer is not { } server || _libraryPath.Count == 0) return;
                string query = _txtLibrarySearch.Text?.Trim() ?? string.Empty;
                if (query.Length == 0) return;
                var steps = new List<(string Id, string Title)> { (LibrarySearchPrefix + query, $"Search: {query}") };
                if (server.IsRoon)
                {
                    _lblLibraryStatus.Text = "Searching…";
                    var found = await FindRoonSearchAsync();
                    if (found == null)
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window, "Roon didn't offer a search.", window._scale);
                        return;
                    }
                    var (parents, input) = found.Value;
                    steps = [.. parents, ($"{input.Id}{RoonInputSeparator}{query}", $"{input.Title}: {query}")];
                }
                if (_libraryPath.Count == 1) SaveLibraryPlace(1, null);
                _libraryPath.RemoveRange(1, _libraryPath.Count - 1);
                _libraryPath.AddRange(steps);
                await LoadLibraryFolderAsync(fresh: true);
            }

            private async Task<(List<(string Id, string Title)> Parents, MediaEntry Input)?> FindRoonSearchAsync()
            {
                var parents = new List<(string Id, string Title)>();
                for (int level = 0; level < 2; level++)
                {
                    var page = await window._owner.RoonBrowseAsync(parents, 0, LibraryPageSize);
                    if (page == null) return null;
                    if (page.Entries.Where(IsRoonInput).OrderBy(e => e.Title == "Search" ? 0 : 1).FirstOrDefault() is { } input) return (parents, input);
                    if (page.Entries.FirstOrDefault(e => e.Title == "Library") is not { } library) return null;
                    parents.Add((library.Id, library.Title));
                }
                return null;
            }
        }
    }
}
