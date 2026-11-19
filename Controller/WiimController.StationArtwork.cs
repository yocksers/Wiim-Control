using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed partial class WiimController
{
    private const int StationArtworkCacheSize = 50;
    private static readonly Regex AppleAlbumLink = new(@"/(?:(?<country>[a-z]{2})/)?album/(?:[^/?]+/)?(?:id)?(?<id>\d+)", RegexOptions.IgnoreCase);
    private readonly ConcurrentDictionary<string, string> _stationArtwork = new();

    private static bool LooksLikeJson(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF) continue;
            return b == (byte)'{';
        }
        return false;
    }

    private async Task<byte[]> StationArtworkBytesAsync(string url, byte[] data)
    {
        if (!_stationArtwork.TryGetValue(url, out var image))
        {
            var (found, final) = await StationArtworkUrlAsync(data).ConfigureAwait(false);
            image = found ?? string.Empty;
            if (final)
            {
                if (_stationArtwork.Count >= StationArtworkCacheSize) _stationArtwork.Clear();
                _stationArtwork[url] = image;
            }
        }
        if (image.Length == 0) throw new InvalidOperationException("The station's now playing data has no artwork.");
        return await _webHttp.GetByteArrayAsync(image).ConfigureAwait(false);
    }

    private async Task<(string? Url, bool Final)> StationArtworkUrlAsync(byte[] data)
    {
        var (apple, picture) = ParseStationArtwork(data);
        if (apple == null) return (picture, true);
        var (cover, answered) = await AppleAlbumArtworkAsync(apple).ConfigureAwait(false);
        return (cover ?? picture, answered);
    }

    internal static (string? AppleLink, string? Picture) ParseStationArtwork(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            string? Link(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                                         value.GetString() is { } text && text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : null;
            return (Link("eventAppleMusicUrl"), Link("eventImageUrl") ?? Link("eventImageUrlSmall"));
        }
        catch (JsonException) { return (null, null); }
    }

    private async Task<(string? Url, bool Answered)> AppleAlbumArtworkAsync(string link)
    {
        if (AppleAlbumLink.Match(link) is not { Success: true } match) return (null, true);
        string country = match.Groups["country"].Success ? $"&country={match.Groups["country"].Value.ToLowerInvariant()}" : string.Empty;
        try
        {
            string json = await _webHttp.GetStringAsync($"https://itunes.apple.com/lookup?id={match.Groups["id"].Value}{country}").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return (null, true);
            foreach (var result in results.EnumerateArray())
                if (result.TryGetProperty("artworkUrl100", out var art) && art.GetString() is { Length: > 0 } small)
                    return (LargeArtworkUrl(small), true);
            return (null, true);
        }
        catch (Exception) { return (null, false); }
    }
}
