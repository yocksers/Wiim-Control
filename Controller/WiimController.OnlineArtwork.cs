using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const int OnlineArtworkCacheSize = 100;
    private static readonly TimeSpan OnlineArtworkWait = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan OnlineArtworkTimeout = TimeSpan.FromSeconds(6);
    private readonly Dictionary<string, string> _onlineArtworkFound = new();
    private readonly List<string> _onlineArtworkOrder = [];
    private readonly Dictionary<string, DateTime> _onlineArtworkRunning = new();
    private readonly Dictionary<string, (DateTime At, bool Limited)> _onlineArtworkRetryAt = new();
    internal const string ArtworkQuotaNotice = "Artwork lookup is busy or over its limit – trying again in a minute";
    private static readonly TimeSpan OnlineArtworkRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly Lazy<string> ArtworkCountry = new(() => RadioDirectory.HomeCountry().ToLowerInvariant());

    private static readonly Regex VersionTag = new(
        @"\s*[\(\[][^\)\]]*\b(version|edit|edition|mix|remix|live|remaster|remastered|deluxe|instrumental|acoustic|radio|extended|single|mono|stereo|demo|bonus|explicit|clean|feat\.?|ft\.?|featuring|with)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase);
    private static readonly Regex DashTag = new(
        @"\s+-\s+.*\b(version|edit|mix|remix|live|remaster|remastered|deluxe|instrumental|acoustic|radio|mono|stereo|demo|bonus)\b.*$", RegexOptions.IgnoreCase);
    private static readonly Regex FeatTail = new(@"\s+(feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.IgnoreCase);
    private static readonly Regex UnwantedVersion = new(@"\b(live|karaoke|instrumental|acoustic|unplugged)\b", RegexOptions.IgnoreCase);
    private static readonly Regex Brackets = new(@"\s*[\(\[\{][^\)\]\}]*[\)\]\}]");
    private static readonly Regex DashEnding = new(@"\s+-\s+.*$");
    private static readonly Regex LeadingArticle = new(@"^\s*(the|a|an)\s+", RegexOptions.IgnoreCase);
    private static readonly Regex ArtistSeparators = new(@"\s*(?:,|;|&|/|\bfeat\.?|\bft\.?|\bfeaturing\b|\bx\b)\s*", RegexOptions.IgnoreCase);
    private static readonly Regex SoundtrackAlbum = new(
        @"\b(soundtrack|o\.?s\.?t|original (motion picture|cast|score|london|broadway|series)|cast recording|motion picture|music from|score|musical)\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex OriginalRecording = new(@"\boriginal (motion picture|soundtrack|cast)\b", RegexOptions.IgnoreCase);
    private static readonly Regex GenericSoundtrackTitle = new(
        @"^\s*(the\s+)?(main|end|opening|closing|love)?\s*(title|titles|credits|theme|suite|prologue|epilogue|finale|overture|intro|introduction|outro|reprise|end)\b",
        RegexOptions.IgnoreCase);
    private const int MinSoundtrackTitleLetters = 10;
    private const int MaxLookupTries = 6;
    private const int MaxAppleSearches = 2;
    private static readonly Regex TrailingArticle = new(
        @"([^,;&/]+?),\s*(The|A|An|Los|Las|La|Le|Les|Die|Der|Das|El|Il)\s*(?=$|[;&/,])", RegexOptions.IgnoreCase);
    private const int MinArtistPrefix = 6;

    private sealed record LookupTry(string QueryArtist, string QueryTitle, string Artist, string Title, string Album, bool Soundtrack);

    private static readonly Regex Compilation = new(
        @"\b(greatest hits|best of|the very best|hits of|now that's what|collection|anthology|essential|gold|hit parade|original album classics|itunes originals|karaoke)\b",
        RegexOptions.IgnoreCase);

    private bool _onlineArtwork;

    internal bool OnlineArtwork
    {
        get => _onlineArtwork;
        set
        {
            if (_onlineArtwork == value) return;
            _onlineArtwork = value;
            SaveConfig();
            SetComputerTrack(_computerTrack);
            RefreshRadioArtwork();
            if (LocalNowPlaying != null) NotifyLocalPlayer();
        }
    }

    private static string OnlineArtworkKey(ComputerTrack track) => $"{track.Title}\n{track.Artist}\n{track.Album}";

    internal bool OnlineArtworkSearching => _onlineArtwork && _onlineArtworkRunning.Values.Any(started => DateTime.UtcNow - started < OnlineArtworkTimeout);

    private static readonly Regex VideoTag = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|music\s+video|mv|m/v|hd|hq|4k|8k|1080p|720p|clip|videoclip|full\s+song)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase);
    private static readonly Regex VideoTail = new(@"\s*[|｜].*$");
    private static readonly Regex FanCover = new(@"[\(\[][^\)\]]*\bcover\b[^\)\]]*[\)\]]|\bcover\s+by\b", RegexOptions.IgnoreCase);
    private static readonly Regex ChannelSuffix = new(@"(\s*-\s*topic|\s*vevo|\s+official(\s+channel)?)\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex TitleDash = new(@"\s+[-–—]\s+");

    private static List<(string Artist, string Title)> VideoCandidates(string artist, string title)
    {
        var candidates = new List<(string Artist, string Title)>();
        if (FanCover.IsMatch(title)) return candidates;
        string channel = ChannelSuffix.Replace(artist, string.Empty).Trim();
        string cleaned = VideoTail.Replace(VideoTag.Replace(title, string.Empty), string.Empty).Trim();
        var parts = TitleDash.Split(cleaned, 2);
        if (parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
        {
            candidates.Add((parts[0].Trim(), parts[1].Trim()));
            if (channel.Length > 0) candidates.Add((channel, parts[1].Trim()));
        }
        if (channel.Length > 0 && cleaned.Length > 0) candidates.Add((channel, cleaned));
        string original = Comparable(FirstArtist(artist)) + "\n" + Comparable(title);
        return candidates
            .Where(c => Comparable(FirstArtist(c.Artist)).Length > 0 && Comparable(c.Title).Length > 0)
            .DistinctBy(c => Comparable(FirstArtist(c.Artist)) + "\n" + Comparable(c.Title))
            .Where(c => Comparable(FirstArtist(c.Artist)) + "\n" + Comparable(c.Title) != original)
            .ToList();
    }

    private string? OnlineArtworkFor(ComputerTrack track, bool bothOrders = false)
    {
        if (!_onlineArtwork || track.Title.Length == 0 || track.Artist.Length == 0) return null;
        string key = OnlineArtworkKey(track);
        if (_onlineArtworkFound.TryGetValue(key, out var found)) return found.Length > 0 ? found : null;
        if (_onlineArtworkRetryAt.TryGetValue(key, out var retryAt))
        {
            if (DateTime.UtcNow < retryAt.At) return null;
            _onlineArtworkRetryAt.Remove(key);
        }
        if (!_onlineArtworkRunning.TryGetValue(key, out var started))
        {
            _onlineArtworkRunning[key] = DateTime.UtcNow;
            _ = LookUpOnlineArtworkAsync(key, track.Title, track.Artist, track.Album, bothOrders);
            return string.Empty;
        }
        return DateTime.UtcNow - started < OnlineArtworkWait ? string.Empty : null;
    }

    private static List<LookupTry> LookupTries(string title, string artist, string album, bool bothOrders)
    {
        var tries = new List<LookupTry>();
        void Add(string tryArtist, string tryTitle, string tryAlbum)
        {
            var names = ArtistNames(tryArtist);
            string first = names.Count > 0 ? names[0] : tryArtist.Trim();
            string cleaned = SearchTitle(tryTitle), plain = PlainTitle(tryTitle);
            tries.Add(new LookupTry(first, cleaned, tryArtist, tryTitle, tryAlbum, false));
            if (plain.Length > 0 && plain != cleaned) tries.Add(new LookupTry(first, plain, tryArtist, tryTitle, tryAlbum, false));
            if (names.Count > 1) tries.Add(new LookupTry(names[1], plain.Length > 0 ? plain : cleaned, tryArtist, tryTitle, tryAlbum, false));
        }
        Add(artist, title, album);
        if (bothOrders) Add(title, artist, album);
        tries.AddRange(VideoCandidates(artist, title).Select(c => new LookupTry(FirstArtist(c.Artist), SearchTitle(c.Title), c.Artist, c.Title, string.Empty, false)));
        var unique = tries.DistinctBy(t => (Comparable(t.QueryArtist), t.QueryTitle.ToLowerInvariant())).Take(MaxLookupTries - 1).ToList();
        if (SoundtrackCandidate(title))
            unique.Add(new LookupTry(string.Empty, PlainTitle(title) is { Length: > 0 } plainTitle ? plainTitle : SearchTitle(title), artist, title, album, true));
        return unique;
    }

    private async Task LookUpOnlineArtworkAsync(string key, string title, string artist, string album, bool bothOrders = false)
    {
        string url = string.Empty;
        bool unanswered = false, limited = false;
        int appleSearches = 0;
        foreach (var attempt in LookupTries(title, artist, album, bothOrders))
        {
            if (!_onlineArtwork) break;
            var deezer = await FindDeezerArtworkAsync(attempt).ConfigureAwait(false);
            unanswered |= deezer.Url == null;
            limited |= deezer.Limited;
            url = deezer.Url ?? string.Empty;
            if (url.Length == 0 && _onlineArtwork && appleSearches++ < MaxAppleSearches)
            {
                var apple = await FindAppleArtworkAsync(attempt).ConfigureAwait(false);
                unanswered |= apple.Url == null;
                limited |= apple.Limited;
                url = apple.Url ?? string.Empty;
            }
            if (url.Length > 0) break;
        }
        if (url.Length == 0 && _onlineArtwork)
        {
            var musicBrainz = await FindMusicBrainzArtworkAsync(title, artist, bothOrders).ConfigureAwait(false);
            unanswered |= musicBrainz.Url == null;
            limited |= musicBrainz.Busy;
            url = musicBrainz.Url ?? string.Empty;
        }
        Dispatcher.UIThread.Post(() =>
        {
            _onlineArtworkRunning.Remove(key);
            if (!_onlineArtwork) return;
            if (url.Length == 0 && unanswered)
            {
                if (_onlineArtworkRetryAt.Count >= OnlineArtworkCacheSize) _onlineArtworkRetryAt.Clear();
                _onlineArtworkRetryAt[key] = (DateTime.UtcNow + OnlineArtworkRetryDelay, limited);
                if (limited)
                {
                    if (_computerTrack is { } waiting && OnlineArtworkKey(waiting) == key) _settingsWindow?.OnComputerPlaybackChanged();
                    RefreshRadioArtwork();
                }
                OnLocalLookupDone(key);
                return;
            }
            _onlineArtworkFound[key] = url;
            _onlineArtworkOrder.Remove(key);
            _onlineArtworkOrder.Add(key);
            while (_onlineArtworkOrder.Count > OnlineArtworkCacheSize)
            {
                _onlineArtworkFound.Remove(_onlineArtworkOrder[0]);
                _onlineArtworkOrder.RemoveAt(0);
            }
            if (_computerTrack is { } current && OnlineArtworkKey(current) == key) SetComputerTrack(current);
            if (_radioLookups.Remove(key)) RefreshRadioArtwork();
            OnLocalLookupDone(key);
        });
    }

    internal bool ArtworkLookupLimited(DeviceStatus status)
    {
        string title = status.Title.Trim(), artist = status.Artist.Trim();
        if (!_onlineArtwork || title.Length == 0 || artist.Length == 0) return false;
        foreach (string key in new[] { $"{status.Title}\n{status.Artist}\n{status.Album}", $"{title}\n{artist}\n" })
            if (_onlineArtworkRetryAt.TryGetValue(key, out var retry) && retry.Limited && DateTime.UtcNow < retry.At) return true;
        return false;
    }

    internal static bool IsQuotaRefusal(System.Net.HttpStatusCode status, string url) =>
        status == System.Net.HttpStatusCode.TooManyRequests ||
        status == System.Net.HttpStatusCode.Forbidden && url.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDeezerQuotaError(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
        (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() == 4 ||
         JsonText(error, "message").Contains("quota", StringComparison.OrdinalIgnoreCase));

    private async Task<(JsonDocument? Doc, bool Limited)> GetOnlineJsonAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd($"WiimControl/{AppVersion}");
            using var timeout = new CancellationTokenSource(OnlineArtworkTimeout);
            using var response = await _webHttp.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, IsQuotaRefusal(response.StatusCode, url));
            return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)), false);
        }
        catch (Exception) { return (null, false); }
    }

    private async Task<(string? Url, bool Limited)> FindDeezerArtworkAsync(LookupTry attempt)
    {
        string query = $"{attempt.QueryArtist} {attempt.QueryTitle}".Trim();
        var (doc, limited) = await GetOnlineJsonAsync($"https://api.deezer.com/search?limit=25&q={Uri.EscapeDataString(query)}").ConfigureAwait(false);
        using var owned = doc;
        if (doc == null) return (null, limited);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.TryGetProperty("error", out _))
            return (null, IsDeezerQuotaError(doc.RootElement));
        if (!doc.RootElement.TryGetProperty("data", out var results) || results.ValueKind != JsonValueKind.Array) return (string.Empty, false);
        return (BestArtwork(attempt, results.EnumerateArray().Select(r => (
            JsonText(r, "title"),
            r.TryGetProperty("artist", out var a) ? JsonText(a, "name") : string.Empty,
            r.TryGetProperty("album", out var al) ? JsonText(al, "title") : string.Empty,
            r.TryGetProperty("album", out var cover) ? JsonText(cover, "cover_xl") : string.Empty))), false);
    }

    private async Task<(string? Url, bool Limited)> FindAppleArtworkAsync(LookupTry attempt)
    {
        string country = ArtworkCountry.Value;
        string term = Uri.EscapeDataString($"{attempt.QueryArtist} {attempt.QueryTitle}".Trim());
        var (doc, limited) = await GetOnlineJsonAsync($"https://itunes.apple.com/search?term={term}&media=music&entity=song&limit=25&country={country}")
            .ConfigureAwait(false);
        using var owned = doc;
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return (null, limited);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return (string.Empty, false);
        return (BestArtwork(attempt, results.EnumerateArray().Select(r => (
            JsonText(r, "trackName"), JsonText(r, "artistName"), JsonText(r, "collectionName"),
            Regex.Replace(JsonText(r, "artworkUrl100"), @"\d+x\d+bb\.", "600x600bb.")))), false);
    }

    private static string JsonText(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string BestArtwork(LookupTry attempt, IEnumerable<(string Title, string Artist, string Album, string Art)> results)
    {
        var wantedTitles = TitleForms(attempt.Title);
        var wantedArtists = ArtistForms(attempt.Artist);
        string wantedAlbum = Comparable(attempt.Album);
        bool wantsUnusual = UnwantedVersion.IsMatch(attempt.Title) || UnwantedVersion.IsMatch(attempt.Album);
        string fallback = string.Empty;
        foreach (var result in results)
        {
            if (result.Art.Length == 0) continue;
            var titles = TitleForms(result.Title);
            if (attempt.Soundtrack)
            {
                if (!titles.Overlaps(wantedTitles) || !SoundtrackAlbum.IsMatch(result.Album)) continue;
            }
            else if (!titles.Any(t => wantedTitles.Any(w => SameText(t, w))) || !ArtistForms(result.Artist).Any(a => wantedArtists.Any(w => SameArtist(a, w))))
                continue;
            if (!wantsUnusual && (UnwantedVersion.IsMatch(result.Title) || UnwantedVersion.IsMatch(result.Album))) continue;
            if (attempt.Soundtrack && OriginalRecording.IsMatch(result.Album)) return result.Art;
            if (!attempt.Soundtrack && wantedAlbum.Length > 0 && SameText(Comparable(result.Album), wantedAlbum)) return result.Art;
            if (fallback.Length == 0 && !Compilation.IsMatch(result.Album)) fallback = result.Art;
        }
        return fallback;
    }

    private static string ArtistText(string artist) => TrailingArticle.Replace(NormalizePunctuation(artist), "$2 $1").Trim();

    private static List<string> ArtistNames(string artist) =>
        ArtistSeparators.Split(ArtistText(artist)).Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

    private static string FirstArtist(string artist) => ArtistNames(artist) is { Count: > 0 } names ? names[0] : artist.Trim();

    private static HashSet<string> ArtistForms(string artist)
    {
        var forms = ArtistNames(artist).Select(Comparable).ToHashSet();
        forms.Add(Comparable(ArtistText(artist)));
        forms.Remove(string.Empty);
        return forms;
    }

    private static bool SameArtist(string found, string wanted) =>
        found == wanted || Math.Min(found.Length, wanted.Length) >= MinArtistPrefix &&
        (found.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(found, StringComparison.Ordinal));

    private static HashSet<string> TitleForms(string title)
    {
        string text = SearchTitle(title);
        var forms = new HashSet<string> { Comparable(text), Comparable(LeadingArticle.Replace(text, string.Empty)) };
        int colon = text.LastIndexOf(':');
        if (colon > 0 && colon < text.Length - 1)
        {
            string tail = text[(colon + 1)..];
            forms.Add(Comparable(tail));
            forms.Add(Comparable(LeadingArticle.Replace(tail.Trim(), string.Empty)));
        }
        forms.Remove(string.Empty);
        return forms;
    }

    private static bool SoundtrackCandidate(string title)
    {
        string plain = PlainTitle(title);
        return Comparable(plain).Length >= MinSoundtrackTitleLetters && !GenericSoundtrackTitle.IsMatch(plain);
    }

    private static string NormalizePunctuation(string text) => text
        .Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u201A', '\'').Replace('\u2032', '\'')
        .Replace('\u201C', '"').Replace('\u201D', '"').Replace('\u201E', '"')
        .Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2012', '-').Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2015', '-')
        .Replace('\u00A0', ' ').Replace("\u2026", "...");

    private static string SearchTitle(string title) =>
        FeatTail.Replace(DashTag.Replace(VersionTag.Replace(NormalizePunctuation(title), string.Empty), string.Empty), string.Empty).Trim();

    private static string PlainTitle(string title) =>
        DashEnding.Replace(Brackets.Replace(SearchTitle(title), string.Empty), string.Empty).Trim();

    private static string Comparable(string text)
    {
        var plain = new StringBuilder();
        foreach (char c in SearchTitle(text).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) plain.Append(char.ToLowerInvariant(c));
        }
        return plain.ToString();
    }

    private static bool SameText(string found, string wanted) =>
        found.Length > 0 && wanted.Length > 0 && (found == wanted || Math.Min(found.Length, wanted.Length) >= 4 &&
            (found.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(found, StringComparison.Ordinal)));
}
