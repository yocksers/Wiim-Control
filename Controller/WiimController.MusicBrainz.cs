using System.Net;
using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private const string MusicBrainzContact = "https://github.com/yocksers/Wiim-Control";
    private const int MaxMusicBrainzSearches = 3;
    private const int MaxCoverChecks = 3;
    private const int MusicBrainzAttempts = 2;
    private const string SoundtrackType = "Soundtrack";
    private static readonly TimeSpan MusicBrainzSpacing = TimeSpan.FromSeconds(1.1);
    private static readonly SemaphoreSlim MusicBrainzGate = new(1, 1);
    private static DateTime _musicBrainzLast = DateTime.MinValue;

    private static string MusicBrainzAgent => $"WiimControl/{AppVersion} ( {MusicBrainzContact} )";

    private static string LuceneQuoted(string text) =>
        "\"" + SpacedSlashes.Replace(text, " / ").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static readonly System.Text.RegularExpressions.Regex SpacedSlashes = new(@"\s*/\s*");

    private static List<(string Title, string Artist)> MusicBrainzQueries(string title, string artist, bool bothOrders)
    {
        var queries = new List<(string Title, string Artist)>();
        void Add(string tryTitle, string tryArtist)
        {
            string cleaned = SearchTitle(tryTitle), plain = PlainTitle(tryTitle);
            if (cleaned.Length > 0) queries.Add((cleaned, tryArtist));
            if (plain.Length > 0) queries.Add((plain, tryArtist));
            if (plain.Split('/', 2) is [var first, _] && first.Trim().Length > 0) queries.Add((first.Trim(), tryArtist));
        }
        Add(title, artist);
        if (bothOrders) Add(artist, title);
        return queries.Where(q => ArtistNames(q.Artist).Count > 0)
            .DistinctBy(q => (q.Title.ToLowerInvariant(), q.Artist.ToLowerInvariant()))
            .Take(MaxMusicBrainzSearches)
            .ToList();
    }

    private async Task<string?> FindMusicBrainzArtworkAsync(string title, string artist, bool bothOrders)
    {
        bool answered = false;
        foreach (var (queryTitle, queryArtist) in MusicBrainzQueries(title, artist, bothOrders))
        {
            if (!_onlineArtwork) break;
            var names = ArtistNames(queryArtist);
            string artists = string.Join(" OR ", names.Select(n => "artist:" + LuceneQuoted(n)));
            string query = $"recording:{LuceneQuoted(queryTitle)} AND ({artists})";
            var (doc, status) = await GetMusicBrainzJsonAsync(
                $"https://musicbrainz.org/ws/2/recording/?fmt=json&limit=10&query={Uri.EscapeDataString(query)}").ConfigureAwait(false);
            using var owned = doc;
            if (doc == null)
            {
                if (status == HttpStatusCode.ServiceUnavailable) return null;
                continue;
            }
            answered = true;
            var wantedTitles = TitleForms(queryTitle);
            wantedTitles.UnionWith(TitleForms(title));
            var wantedArtists = ArtistForms(queryArtist);
            var groups = new List<string>();
            if (doc.RootElement.TryGetProperty("recordings", out var recordings) && recordings.ValueKind == JsonValueKind.Array)
                foreach (var recording in recordings.EnumerateArray())
                {
                    var titles = TitleForms(JsonText(recording, "title"));
                    if (!titles.Any(t => wantedTitles.Any(w => SameText(t, w)))) continue;
                    if (!CreditNames(recording).Any(n => ArtistForms(n).Any(a => wantedArtists.Any(w => SameArtist(a, w))))) continue;
                    if (!recording.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array) continue;
                    foreach (var release in releases.EnumerateArray())
                        if (release.TryGetProperty("release-group", out var group) && JsonText(group, "id") is { Length: > 0 } id && !groups.Contains(id))
                            groups.Add(id);
                }
            foreach (string group in groups.Take(MaxCoverChecks))
            {
                string cover = $"https://coverartarchive.org/release-group/{group}/front-500";
                if (await CoverExistsAsync(cover).ConfigureAwait(false)) return cover;
            }
        }
        if (_onlineArtwork && SoundtrackCandidate(title))
        {
            string plainTitle = PlainTitle(title) is { Length: > 0 } plain ? plain : SearchTitle(title);
            var (doc, status) = await GetMusicBrainzJsonAsync("https://musicbrainz.org/ws/2/recording/?fmt=json&limit=10&query=" +
                Uri.EscapeDataString($"recording:{LuceneQuoted(plainTitle)}")).ConfigureAwait(false);
            using var owned = doc;
            if (doc == null) return status == HttpStatusCode.ServiceUnavailable || !answered ? null : string.Empty;
            answered = true;
            var wantedTitles = TitleForms(plainTitle);
            var groups = new List<string>();
            if (doc.RootElement.TryGetProperty("recordings", out var recordings) && recordings.ValueKind == JsonValueKind.Array)
                foreach (var recording in recordings.EnumerateArray())
                {
                    var titles = TitleForms(JsonText(recording, "title"));
                    if (!titles.Any(t => wantedTitles.Any(w => SameText(t, w)))) continue;
                    if (!recording.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array) continue;
                    foreach (var release in releases.EnumerateArray())
                        if (release.TryGetProperty("release-group", out var group) && IsSoundtrackGroup(group) &&
                            JsonText(group, "id") is { Length: > 0 } id && !groups.Contains(id))
                            groups.Add(id);
                }
            foreach (string group in groups.Take(MaxCoverChecks))
            {
                string cover = $"https://coverartarchive.org/release-group/{group}/front-500";
                if (await CoverExistsAsync(cover).ConfigureAwait(false)) return cover;
            }
        }
        return answered ? string.Empty : null;
    }

    private static bool IsSoundtrackGroup(JsonElement group) =>
        group.TryGetProperty("secondary-types", out var types) && types.ValueKind == JsonValueKind.Array &&
        types.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == SoundtrackType) ||
        SoundtrackAlbum.IsMatch(JsonText(group, "title"));

    private static IEnumerable<string> CreditNames(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array) yield break;
        foreach (var credit in credits.EnumerateArray())
        {
            if (JsonText(credit, "name") is { Length: > 0 } name) yield return name;
            if (credit.TryGetProperty("artist", out var credited) && JsonText(credited, "name") is { Length: > 0 } artistName) yield return artistName;
        }
    }

    private async Task<(JsonDocument? Doc, HttpStatusCode? Status)> GetMusicBrainzJsonAsync(string url)
    {
        await MusicBrainzGate.WaitAsync().ConfigureAwait(false);
        try
        {
            HttpStatusCode status = HttpStatusCode.ServiceUnavailable;
            for (int attempt = 0; attempt < MusicBrainzAttempts && status == HttpStatusCode.ServiceUnavailable; attempt++)
            {
                var wait = _musicBrainzLast + MusicBrainzSpacing - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(MusicBrainzAgent);
                request.Headers.Accept.ParseAdd("application/json");
                using var timeout = new CancellationTokenSource(OnlineArtworkTimeout);
                using var response = await _webHttp.SendAsync(request, timeout.Token).ConfigureAwait(false);
                _musicBrainzLast = DateTime.UtcNow;
                status = response.StatusCode;
                if (response.IsSuccessStatusCode)
                    return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)), status);
                if (status == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter?.Delta is { } retry && retry > MusicBrainzSpacing)
                    _musicBrainzLast = DateTime.UtcNow + retry - MusicBrainzSpacing;
            }
            return (null, status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (null, null);
        }
        finally
        {
            _musicBrainzLast = DateTime.UtcNow;
            MusicBrainzGate.Release();
        }
    }

    private async Task<bool> CoverExistsAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.UserAgent.ParseAdd(MusicBrainzAgent);
            using var timeout = new CancellationTokenSource(OnlineArtworkTimeout);
            using var response = await _webHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }
    }
}
