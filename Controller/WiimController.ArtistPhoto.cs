using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private const int ArtistPhotoSize = 2000;
    private const int ArtistPhotoCacheSize = 3;
    private const int ArtistLinkCacheSize = 200;
    private const int ArtistPageLimit = 256 * 1024;
    private static readonly Regex OpenGraphImage = new(@"<meta[^>]+property=""og:image""[^>]+content=""(?<url>https://[^""]+)""", RegexOptions.IgnoreCase);
    private readonly Dictionary<string, string> _artistPhotoLinks = new();
    private readonly List<(string Key, Bitmap Photo)> _artistPhotos = [];
    private readonly Dictionary<string, Task<Bitmap?>> _artistPhotoLoads = new();

    internal static string ArtistPhotoName(string artistLine)
    {
        string first = artistLine.Split("  ·  ", 2)[0].Trim();
        return FirstArtist(first);
    }

    internal Task<Bitmap?> GetArtistPhotoAsync(string artist)
    {
        string name = artist.Trim();
        string key = Comparable(name);
        if (!_onlineArtwork || key.Length == 0) return Task.FromResult<Bitmap?>(null);
        lock (_artistPhotos)
        {
            int hit = _artistPhotos.FindIndex(p => p.Key == key);
            if (hit >= 0) return Task.FromResult<Bitmap?>(_artistPhotos[hit].Photo);
            if (_artistPhotoLoads.TryGetValue(key, out var running)) return running;
            var load = LoadArtistPhotoAsync(name, key);
            _artistPhotoLoads[key] = load;
            return load;
        }
    }

    private async Task<Bitmap?> LoadArtistPhotoAsync(string name, string key)
    {
        try
        {
            string? link;
            lock (_artistPhotos) _artistPhotoLinks.TryGetValue(key, out link);
            if (link == null)
            {
                link = await FindAppleArtistPhotoAsync(name, key).ConfigureAwait(false) ?? await FindDeezerArtistPhotoAsync(name, key).ConfigureAwait(false);
                if (link == null) return null;
                lock (_artistPhotos)
                {
                    if (_artistPhotoLinks.Count >= ArtistLinkCacheSize) _artistPhotoLinks.Clear();
                    _artistPhotoLinks[key] = link;
                }
            }
            if (link.Length == 0 || !_onlineArtwork) return null;
            var photo = await DownloadArtistPhotoAsync(link).ConfigureAwait(false);
            if (photo == null) return null;
            var evicted = new List<Bitmap>();
            lock (_artistPhotos)
            {
                _artistPhotos.Add((key, photo));
                while (_artistPhotos.Count > ArtistPhotoCacheSize)
                {
                    evicted.Add(_artistPhotos[0].Photo);
                    _artistPhotos.RemoveAt(0);
                }
            }
            if (evicted.Count > 0) DisposeWhenUnused(evicted);
            return photo;
        }
        finally
        {
            lock (_artistPhotos) _artistPhotoLoads.Remove(key);
        }
    }

    private async Task<string?> FindAppleArtistPhotoAsync(string name, string key)
    {
        string term = Uri.EscapeDataString(name);
        var (doc, _) = await GetOnlineJsonAsync($"https://itunes.apple.com/search?term={term}&media=music&entity=musicArtist&limit=10&country={ArtworkCountry.Value}")
            .ConfigureAwait(false);
        using var owned = doc;
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return string.Empty;
        string page = results.EnumerateArray()
            .Where(r => SameText(Comparable(JsonText(r, "artistName")), key))
            .Select(r => JsonText(r, "artistLinkUrl"))
            .FirstOrDefault(u => u.StartsWith("https://music.apple.com/", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (page.Length == 0) return string.Empty;
        string? image = await ReadOpenGraphImageAsync(page.Split('?')[0]).ConfigureAwait(false);
        if (image == null) return null;
        int sizeStart = image.LastIndexOf('/');
        if (!image.Contains(".mzstatic.com/image/thumb/", StringComparison.OrdinalIgnoreCase) || sizeStart < 0) return string.Empty;
        return $"{image[..sizeStart]}/{ArtistPhotoSize}x{ArtistPhotoSize}bb.jpg";
    }

    private async Task<string?> ReadOpenGraphImageAsync(string page)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.UserAgent.ParseAdd($"WiimControl/{AppVersion}");
            using var timeout = new CancellationTokenSource(OnlineArtworkTimeout);
            using var response = await _webHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = new StringBuilder();
            var buffer = new char[16 * 1024];
            while (text.Length < ArtistPageLimit)
            {
                int read = await reader.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                text.Append(buffer, 0, read);
                if (OpenGraphImage.Match(text.ToString()) is { Success: true } found) return found.Groups["url"].Value;
            }
            return string.Empty;
        }
        catch (Exception) { return null; }
    }

    private async Task<string?> FindDeezerArtistPhotoAsync(string name, string key)
    {
        var (doc, _) = await GetOnlineJsonAsync($"https://api.deezer.com/search/artist?limit=10&q={Uri.EscapeDataString(name)}").ConfigureAwait(false);
        using var owned = doc;
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.TryGetProperty("error", out _)) return null;
        if (!doc.RootElement.TryGetProperty("data", out var results) || results.ValueKind != JsonValueKind.Array) return string.Empty;
        return results.EnumerateArray()
            .Where(r => SameText(Comparable(JsonText(r, "name")), key))
            .Select(r => JsonText(r, "picture_xl"))
            .FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !u.Contains("/artist//", StringComparison.Ordinal))
            ?? string.Empty;
    }

    private async Task<Bitmap?> DownloadArtistPhotoAsync(string url)
    {
        try
        {
            using var timeout = new CancellationTokenSource(OnlineArtworkTimeout);
            var bytes = await _webHttp.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }).ConfigureAwait(false);
        }
        catch (Exception) { return null; }
    }
}
