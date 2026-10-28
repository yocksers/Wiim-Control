using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed record LyricLine(int TimeMs, string Text);

sealed record Lyrics(IReadOnlyList<LyricLine> Synced, string Plain, bool Instrumental);

sealed class LyricsServiceException(bool busy) : Exception
{
    public bool Busy { get; } = busy;
}

sealed partial class WiimController
{
    private const string LyricsApi = "https://lrclib.net/api/";
    private const int LyricsDurationTolerance = 8;
    private const int LyricsRequestRetries = 2;
    private static readonly TimeSpan LyricsLongestRetryWait = TimeSpan.FromSeconds(5);
    private bool _nowPlayingLyrics;

    internal bool NowPlayingLyrics
    {
        get => _nowPlayingLyrics;
        set { _nowPlayingLyrics = value; SaveConfig(); }
    }

    internal const int LyricsDelayLimitMs = 1000;
    private int _lyricsDelayMs;

    internal int LyricsDelayMs => _lyricsDelayMs;

    internal void SetLyricsDelay(int delayMs, bool save)
    {
        _lyricsDelayMs = Math.Clamp(delayMs, -LyricsDelayLimitMs, LyricsDelayLimitMs);
        if (save) SaveConfig();
    }

    [GeneratedRegex(@"^\s*((?:\[\d+:\d+(?:[.:]\d+)?\]\s*)+)(.*)$")]
    private static partial Regex LyricsLine();

    [GeneratedRegex(@"\[(\d+):(\d+)(?:[.:](\d+))?\]")]
    private static partial Regex LyricsTime();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex TitleExtras();

    internal async Task<Lyrics?> GetLyricsAsync(string title, string artist, string album, int durationSeconds, CancellationToken cancel)
    {
        bool failed = false, busy = false;
        async Task<string?> AskAsync(string query)
        {
            try
            {
                return await QueryLyricsAsync(query, cancel).ConfigureAwait(false);
            }
            catch (LyricsServiceException e)
            {
                failed = true;
                busy |= e.Busy;
                return null;
            }
        }

        string artistQuery = Uri.EscapeDataString(artist);
        if (durationSeconds > 0 && album.Length > 0 && artist.Length > 0)
        {
            string? exact = await AskAsync(
                $"get?track_name={Uri.EscapeDataString(title)}&artist_name={artistQuery}&album_name={Uri.EscapeDataString(album)}&duration={durationSeconds}")
                .ConfigureAwait(false);
            if (exact != null)
            {
                using var doc = JsonDocument.Parse(exact);
                if (ToLyrics(doc.RootElement) is { } found) return found;
            }
        }
        string simple = TitleExtras().Replace(title, string.Empty).Split(" - ")[0].Trim();
        foreach (var name in new[] { title, simple }.Where(n => n.Length > 0).Distinct())
        {
            string query = artist.Length > 0 ? $"search?track_name={Uri.EscapeDataString(name)}&artist_name={artistQuery}" : $"search?q={Uri.EscapeDataString(name)}";
            if (await AskAsync(query).ConfigureAwait(false) is not { } results) continue;
            using var doc = JsonDocument.Parse(results);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && BestLyrics(doc.RootElement, durationSeconds) is { } best) return best;
        }
        if (failed) throw new LyricsServiceException(busy);
        return null;
    }

    private async Task<string?> QueryLyricsAsync(string query, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LyricsApi + query);
            request.Headers.UserAgent.ParseAdd($"WiimControl/{AppVersion}");
            using var response = await _webHttp.SendAsync(request, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            bool busy = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;
            if (!busy || attempt >= LyricsRequestRetries) throw new LyricsServiceException(busy);
            var wait = response.Headers.RetryAfter?.Delta is { } hint && hint > TimeSpan.Zero
                ? (hint < LyricsLongestRetryWait ? hint : LyricsLongestRetryWait)
                : TimeSpan.FromSeconds(1.5 * (attempt + 1));
            await Task.Delay(wait, cancel).ConfigureAwait(false);
        }
    }

    private static Lyrics? BestLyrics(JsonElement results, int durationSeconds)
    {
        var candidates = results.EnumerateArray()
            .Select(r => (Lyrics: ToLyrics(r), Off: r.TryGetProperty("duration", out var d) && d.TryGetDouble(out double seconds) && durationSeconds > 0
                ? Math.Abs(seconds - durationSeconds) : 0))
            .Where(c => c.Lyrics != null && c.Off <= LyricsDurationTolerance)
            .ToList();
        return candidates.OrderBy(c => c.Lyrics!.Synced.Count > 0 ? 0 : 1).ThenBy(c => c.Off).Select(c => c.Lyrics).FirstOrDefault();
    }

    private static Lyrics? ToLyrics(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        bool instrumental = item.TryGetProperty("instrumental", out var flag) && flag.ValueKind == JsonValueKind.True;
        string synced = item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        string plain = item.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        var lines = ParseSyncedLyrics(synced);
        if (lines.Count == 0 && plain.Trim().Length == 0 && !instrumental) return null;
        return new Lyrics(lines, plain.Trim(), instrumental);
    }

    internal static List<LyricLine> ParseSyncedLyrics(string text)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in text.Split('\n'))
        {
            var match = LyricsLine().Match(raw.TrimEnd('\r'));
            if (!match.Success) continue;
            string words = match.Groups[2].Value.Trim();
            foreach (Match time in LyricsTime().Matches(match.Groups[1].Value))
            {
                int minutes = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture);
                string fraction = time.Groups[3].Value;
                int ms = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
                lines.Add(new LyricLine((minutes * 60 + seconds) * 1000 + ms, words));
            }
        }
        return lines.OrderBy(l => l.TimeMs).ToList();
    }
}
