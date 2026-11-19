using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace WiimControl;

sealed partial class WiimController
{
    private const int BackdropSize = 128;
    private const float BackdropBlur = 9;
    private const int BackdropCacheSize = 8;
    private const int NowPlayingBackdropSize = 256;
    private const float NowPlayingBackdropBlur = 3;

    private bool _artworkBackground = true;
    private string _activeArtUrl = string.Empty;
    private readonly List<(string Url, Bitmap Bitmap)> _backdrops = [];

    internal bool ArtworkBackground
    {
        get => _artworkBackground;
        set
        {
            _artworkBackground = value;
            UpdateNowPlayingInterval();
            SaveConfig();
            _settingsWindow?.ApplyArtworkBackground();
        }
    }

    internal string BackgroundArtUrl => _artworkBackground ? _activeArtUrl : string.Empty;

    private void NoteActiveArtwork(string ip, DeviceStatus? status)
    {
        if (ip != PlaybackTarget) return;
        string url = status?.ArtUrl ?? string.Empty;
        if (url == _activeArtUrl) return;
        _activeArtUrl = url;
        _settingsWindow?.ApplyArtworkBackground();
    }

    internal async Task<Bitmap?> GetBackdropAsync(string url)
    {
        lock (_backdrops)
        {
            int hit = _backdrops.FindIndex(b => b.Url == url);
            if (hit >= 0) return _backdrops[hit].Bitmap;
        }
        var thumbnail = await GetArtworkAsync(url).ConfigureAwait(false);
        if (thumbnail == null) return null;
        var backdrop = await Task.Run(() => BlurForBackdrop(thumbnail)).ConfigureAwait(false);
        if (backdrop == null) return null;
        var evicted = new List<Bitmap>();
        lock (_backdrops)
        {
            int existing = _backdrops.FindIndex(b => b.Url == url);
            if (existing >= 0)
            {
                evicted.Add(backdrop);
                backdrop = _backdrops[existing].Bitmap;
            }
            else _backdrops.Add((url, backdrop));
            while (_backdrops.Count > BackdropCacheSize)
            {
                evicted.Add(_backdrops[0].Bitmap);
                _backdrops.RemoveAt(0);
            }
        }
        if (evicted.Count > 0) DisposeWhenUnused(evicted);
        return backdrop;
    }

    internal static Bitmap? BlurForNowPlaying(Bitmap source, float blur = NowPlayingBackdropBlur)
    {
        try
        {
            using var scaled = source.CreateScaledBitmap(new PixelSize(NowPlayingBackdropSize, NowPlayingBackdropSize), BitmapInterpolationMode.MediumQuality);
            return BlurForBackdrop(scaled, NowPlayingBackdropSize, blur);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap? BlurForBackdrop(Bitmap source, int size = BackdropSize, float blur = BackdropBlur)
    {
        try
        {
            using var pixels = ReadPixels(source);
            if (pixels == null) return null;
            var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null) return null;
            using var filter = SKImageFilter.CreateBlur(blur, blur, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { ImageFilter = filter, FilterQuality = SKFilterQuality.High, IsAntialias = true };
            surface.Canvas.DrawBitmap(pixels, new SKRect(0, 0, size, size), paint);
            var result = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = result.Lock())
            {
                if (!surface.ReadPixels(info, buffer.Address, buffer.RowBytes, 0, 0))
                {
                    result.Dispose();
                    return null;
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }

    private static SKBitmap? ReadPixels(Bitmap source)
    {
        var size = source.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return null;
        SKColorType? colorType = source.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888
            : source.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888
            : null;
        if (colorType is not { } type) return DecodePixels(source);
        var alpha = source.AlphaFormat switch
        {
            AlphaFormat.Unpremul => SKAlphaType.Unpremul,
            AlphaFormat.Opaque => SKAlphaType.Opaque,
            _ => SKAlphaType.Premul
        };
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, type, alpha));
        try
        {
            source.CopyPixels(new PixelRect(size), bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static SKBitmap? DecodePixels(Bitmap source)
    {
        using var encoded = new MemoryStream();
        source.Save(encoded);
        encoded.Position = 0;
        return SKBitmap.Decode(encoded);
    }
}
