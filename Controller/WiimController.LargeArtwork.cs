using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const int LargeArtworkWidth = 1600;
    private const int LargeArtworkCacheSize = 4;
    private readonly List<(string Url, Bitmap Bitmap)> _largeArtwork = [];

    private static readonly (Regex Pattern, string Replacement)[] ArtworkUpgrades =
    [
        (new Regex(@"^(https?://i\.scdn\.co/image/)ab67616d0000(?:1e02|b273|4851)", RegexOptions.IgnoreCase), "${1}ab67616d000082c1"),
        (new Regex(@"^(https?://resources\.tidal\.com/images/.+/)\d+x\d+(\.jpg)$", RegexOptions.IgnoreCase), "${1}1280x1280$2"),
        (new Regex(@"^(https?://[^/]*dzcdn\.net/images/cover/[0-9a-f]+/)\d+x\d+", RegexOptions.IgnoreCase), "${1}1000x1000"),
        (new Regex(@"^(https?://[^/]*mzstatic\.com/.+/)\d+x\d+(bb\.[a-z]+)$", RegexOptions.IgnoreCase), "${1}1200x1200$2"),
        (new Regex(@"([?&])width=\d+&height=\d+", RegexOptions.IgnoreCase), "${1}width=1200&height=1200")
    ];

    internal static string LargeArtworkUrl(string url)
    {
        foreach (var (pattern, replacement) in ArtworkUpgrades)
            if (pattern.IsMatch(url)) return pattern.Replace(url, replacement, 1);
        return url;
    }

    internal async Task<Bitmap?> GetLargeArtworkAsync(string url)
    {
        lock (_largeArtwork)
            if (_largeArtwork.FirstOrDefault(a => a.Url == url).Bitmap is { } cached) return cached;
        string large = LargeArtworkUrl(url);
        var bitmap = await DownloadLargeArtworkAsync(large).ConfigureAwait(false);
        if (bitmap == null && large != url) bitmap = await DownloadLargeArtworkAsync(url).ConfigureAwait(false);
        if (bitmap == null) return null;
        var evicted = new List<Bitmap>();
        lock (_largeArtwork)
        {
            if (_largeArtwork.FirstOrDefault(a => a.Url == url).Bitmap is { } existing)
            {
                evicted.Add(bitmap);
                bitmap = existing;
            }
            else _largeArtwork.Add((url, bitmap));
            while (_largeArtwork.Count > LargeArtworkCacheSize)
            {
                evicted.Add(_largeArtwork[0].Bitmap);
                _largeArtwork.RemoveAt(0);
            }
        }
        if (evicted.Count > 0) DisposeWhenUnused(evicted);
        return bitmap;
    }

    private async Task<Bitmap?> DownloadLargeArtworkAsync(string url)
    {
        try
        {
            var bytes = await HttpFor(url).GetByteArrayAsync(url).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                int width;
                using (var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(bytes))) width = codec?.Info.Width ?? 0;
                using var stream = new MemoryStream(bytes);
                return width > LargeArtworkWidth
                    ? Bitmap.DecodeToWidth(stream, LargeArtworkWidth, BitmapInterpolationMode.HighQuality)
                    : new Bitmap(stream);
            }).ConfigureAwait(false);
        }
        catch (Exception) { return null; }
    }
}
