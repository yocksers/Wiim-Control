using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WiimControl;

sealed partial class WiimController
{
    private const int WallpaperThumbWidth = 160;
    private const int MaxBuiltInWallpapers = 30;
    private readonly Dictionary<string, Bitmap> _wallpaperCache = new();
    private readonly SemaphoreSlim _wallpaperDownloads = new(4);
    private readonly Dictionary<string, int> _builtInWallpaperCount = new();

    internal async Task<WallpaperInfo?> GetWallpapersAsync(string ip)
    {
        var json = await SendToDeviceAsync(ip, "get_ui_wallpaper_list").ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["wallpaper_list"]?["current"] is not JsonArray current) return null;
            var names = current.Select(n => n?.GetValue<string>()).OfType<string>().ToList();
            int max = root["max_number"] is JsonValue m && m.TryGetValue<int>(out int n) ? n : 1;
            return new WallpaperInfo(root.ToJsonString(), names, Math.Max(1, max));
        }
        catch (Exception) { return null; }
    }

    internal async Task<bool> SetWallpapersAsync(string ip, WallpaperInfo info, IReadOnlyList<string> current)
    {
        try
        {
            if (JsonNode.Parse(info.RawJson) is not JsonObject root || root["wallpaper_list"] is not JsonObject list) return false;
            list["current"] = new JsonArray(current.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
            if (!CommandSucceeded(await SendToDeviceAsync(ip, $"set_ui_wallpaper_list:{root.ToJsonString()}").ConfigureAwait(false))) return false;
        }
        catch (Exception) { return false; }
        var check = await GetWallpapersAsync(ip).ConfigureAwait(false);
        return check != null && check.Current.SequenceEqual(current);
    }

    internal async Task<List<(string Name, Bitmap? Thumbnail)>> GetWallpaperThumbnailsAsync(string ip, WallpaperInfo info)
    {
        int known;
        lock (_builtInWallpaperCount) known = _builtInWallpaperCount.GetValueOrDefault(ip, MaxBuiltInWallpapers);
        var builtIn = Enumerable.Range(1, known).Select(i => $"home_bg_{i}.bmp").ToList();
        var names = builtIn.Concat(info.Current.Where(c => !builtIn.Contains(c))).ToList();
        var thumbs = await Task.WhenAll(names.Select(name => GetWallpaperThumbnailAsync(ip, name))).ConfigureAwait(false);
        var found = names.Zip(thumbs).ToList();
        int count = found.Take(builtIn.Count).TakeWhile(p => p.Second != null).Count();
        bool endFound = count == builtIn.Count || found.Skip(count).Take(Math.Min(2, builtIn.Count - count)).All(p => p.Second == null);
        if (count > 0 && endFound)
            lock (_builtInWallpaperCount) _builtInWallpaperCount[ip] = count;
        return found.Where(p => p.Second != null || info.Current.Contains(p.First)).Select(p => (p.First, p.Second)).ToList();
    }

    private async Task<Bitmap?> GetWallpaperThumbnailAsync(string ip, string name)
    {
        string key = $"{ip}|{name}";
        lock (_wallpaperCache)
            if (_wallpaperCache.TryGetValue(key, out var cached)) return cached;
        await _wallpaperDownloads.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await _http.GetByteArrayAsync($"https://{ip}/wallpaper/{Uri.EscapeDataString(name)}").ConfigureAwait(false);
            var thumb = DecodeWallpaper(bytes);
            if (thumb != null)
                lock (_wallpaperCache) _wallpaperCache[key] = thumb;
            return thumb;
        }
        catch (Exception) { return null; }
        finally { _wallpaperDownloads.Release(); }
    }

    private static Bitmap? DecodeWallpaper(byte[] data)
    {
        if (data.Length > 54 && data[0] == 'B' && data[1] == 'M' && BitConverter.ToUInt16(data, 28) == 16)
            return DecodeRgb565Bmp(data);
        using var stream = new MemoryStream(data);
        return Bitmap.DecodeToWidth(stream, WallpaperThumbWidth, BitmapInterpolationMode.HighQuality);
    }

    private static Bitmap? DecodeRgb565Bmp(byte[] data)
    {
        int offset = BitConverter.ToInt32(data, 10);
        int width = BitConverter.ToInt32(data, 18), height = BitConverter.ToInt32(data, 22);
        int rows = Math.Abs(height), stride = (width * 2 + 3) & ~3;
        if (width <= 0 || rows <= 0 || offset + stride * rows > data.Length) return null;

        int thumbWidth = Math.Min(WallpaperThumbWidth, width);
        int thumbHeight = Math.Max(1, rows * thumbWidth / width);
        var pixels = new byte[thumbWidth * thumbHeight * 4];
        for (int y = 0; y < thumbHeight; y++)
        {
            int sourceY = y * rows / thumbHeight;
            int row = height > 0 ? rows - 1 - sourceY : sourceY;
            for (int x = 0; x < thumbWidth; x++)
            {
                int value = BitConverter.ToUInt16(data, offset + row * stride + x * width / thumbWidth * 2);
                int i = (y * thumbWidth + x) * 4;
                pixels[i] = (byte)((value & 0x1F) * 255 / 31);
                pixels[i + 1] = (byte)(((value >> 5) & 0x3F) * 255 / 63);
                pixels[i + 2] = (byte)(((value >> 11) & 0x1F) * 255 / 31);
                pixels[i + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(new PixelSize(thumbWidth, thumbHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        for (int y = 0; y < thumbHeight; y++)
            Marshal.Copy(pixels, y * thumbWidth * 4, frame.Address + y * frame.RowBytes, thumbWidth * 4);
        return bitmap;
    }
}
