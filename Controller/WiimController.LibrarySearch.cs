using System.Collections.Concurrent;
using System.Globalization;

namespace WiimControl;

sealed partial class WiimController
{
    internal const string LibrarySearchPrefix = "\u0002search:";
    internal const int SearchArtistLimit = 50, SearchAlbumLimit = 100, SearchPlaylistLimit = 50, SearchTrackLimit = 200;

    private readonly ConcurrentDictionary<string, Task<string?>> _upnpSearchCaps = new();

    internal static bool IsLibrarySearch(string id) => id.StartsWith(LibrarySearchPrefix, StringComparison.Ordinal);

    internal static int? SearchYear(string query) =>
        query.Length == 4 && int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1
            ? year
            : null;

    private async Task<string?> UpnpSearchCapsAsync(MediaServer server)
    {
        var task = _upnpSearchCaps.GetOrAdd(server.Udn, _ => Upnp.SearchCapabilitiesAsync(server));
        string? caps = await task.ConfigureAwait(false);
        if (caps == null) _upnpSearchCaps.TryRemove(new KeyValuePair<string, Task<string?>>(server.Udn, task));
        return caps;
    }

    internal async Task<bool> CanSearchLibraryAsync(MediaServer server)
    {
        if (server.IsLyrion || server.IsEmbyJellyfin || server.IsRoon) return true;
        if (server.IsShare) return false;
        return await UpnpSearchCapsAsync(server).ConfigureAwait(false) is { Length: > 0 } caps && Upnp.MusicSearchCriteria(caps, "x", null).Count > 0;
    }

    internal static readonly string[] LibrarySearchGroups = ["Artists", "Albums", "Playlists", "Folders", "Tracks"];

    internal static string LibrarySearchGroup(MediaEntry entry) =>
        entry.IsAudio ? "Tracks"
        : entry.Class.Contains("musicArtist", StringComparison.OrdinalIgnoreCase) || entry.Class.Contains(".person", StringComparison.OrdinalIgnoreCase) ? "Artists"
        : entry.Class.Contains("album", StringComparison.OrdinalIgnoreCase) ? "Albums"
        : entry.Class.Contains("playlist", StringComparison.OrdinalIgnoreCase) ? "Playlists"
        : "Folders";

    internal static List<MediaEntry> GroupSearchResults(IEnumerable<MediaEntry> entries) =>
        entries.OrderBy(e => Array.IndexOf(LibrarySearchGroups, LibrarySearchGroup(e))).ToList();

    internal async Task<List<MediaEntry>?> SearchLibraryAsync(MediaServer server, string query)
    {
        int? year = SearchYear(query);
        var found = server.IsLyrion ? await Lms.SearchAsync(server, query, year).ConfigureAwait(false)
            : server.IsEmbyJellyfin ? await SearchEmbyAsync(server, query, year).ConfigureAwait(false)
            : server.IsShare || server.IsRoon ? null
            : await UpnpSearchCapsAsync(server).ConfigureAwait(false) is { Length: > 0 } caps
                ? await Upnp.SearchMusicAsync(server, caps, query, year).ConfigureAwait(false)
                : null;
        return found == null ? null : GroupSearchResults(found);
    }
}
