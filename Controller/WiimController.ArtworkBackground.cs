using Avalonia.Media.Imaging;
using SkiaSharp;

namespace WiimControl;

sealed partial class WiimController
{
    private const int BackdropSize = 128;
    private const float BackdropBlur = 9;
    private const int BackdropCacheSize = 8;

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

    private static Bitmap? BlurForBackdrop(Bitmap source)
    {
        try
        {
            using var encoded = new MemoryStream();
            source.Save(encoded);
            encoded.Position = 0;
            using var decoded = SKBitmap.Decode(encoded);
            if (decoded == null) return null;
            using var surface = SKSurface.Create(new SKImageInfo(BackdropSize, BackdropSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface == null) return null;
            using var blur = SKImageFilter.CreateBlur(BackdropBlur, BackdropBlur, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { ImageFilter = blur, FilterQuality = SKFilterQuality.High, IsAntialias = true };
            surface.Canvas.DrawBitmap(decoded, new SKRect(0, 0, BackdropSize, BackdropSize), paint);
            using var image = surface.Snapshot();
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = png.AsStream();
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }
}
