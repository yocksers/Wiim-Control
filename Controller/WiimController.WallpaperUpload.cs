using System.Net.Http.Headers;
using System.Security.Cryptography;
using SkiaSharp;

namespace WiimControl;

sealed partial class WiimController
{
    private const int WallpaperJpegQuality = 90;
    private static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    internal static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    internal static byte[]? PrepareWallpaper(byte[] data, int width, int height)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec == null) return null;
        using var decoded = SKBitmap.Decode(codec);
        if (decoded == null) return null;
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var source = oriented ?? decoded;

        float scale = Math.Max((float)width / source.Width, (float)height / source.Height);
        float cropWidth = width / scale, cropHeight = height / scale;
        var crop = SKRect.Create((source.Width - cropWidth) / 2, (source.Height - cropHeight) / 2, cropWidth, cropHeight);
        using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, crop, SKRect.Create(width, height), paint);
        }
        using var image = SKImage.FromBitmap(target);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, WallpaperJpegQuality);
        return encoded?.ToArray();
    }

    private static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var (degrees, mirror) = origin switch
        {
            SKEncodedOrigin.TopRight => (0f, true),
            SKEncodedOrigin.BottomRight => (180f, false),
            SKEncodedOrigin.BottomLeft => (180f, true),
            SKEncodedOrigin.LeftTop => (90f, true),
            SKEncodedOrigin.RightTop => (90f, false),
            SKEncodedOrigin.RightBottom => (270f, true),
            SKEncodedOrigin.LeftBottom => (270f, false),
            _ => (0f, false)
        };
        if (degrees == 0 && !mirror) return null;
        bool swap = degrees is 90 or 270;
        var oriented = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(oriented);
        if (mirror)
        {
            canvas.Translate(oriented.Width, 0);
            canvas.Scale(-1, 1);
        }
        canvas.Translate(oriented.Width / 2f, oriented.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return oriented;
    }

    internal async Task<string?> UploadWallpaperAsync(string ip, byte[] jpeg)
    {
        string name = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + ".jpg";
        using var content = new ByteArrayContent(jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{ip}/upload.asp?filename={name}&crc32={Crc32(jpeg)}") { Content = content };
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await _http.SendAsync(request, cancel.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancel.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode && CommandSucceeded(body) && !body.Contains("error", StringComparison.OrdinalIgnoreCase) ? name : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException) { return null; }
    }
}
