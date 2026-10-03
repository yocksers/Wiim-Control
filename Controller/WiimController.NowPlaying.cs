using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly Dictionary<int, string> SourceNames = new()
    {
        [1] = "AirPlay", [2] = "DLNA", [31] = "Spotify", [32] = "TIDAL Connect", [40] = "Line-in",
        [41] = "Bluetooth", [43] = "Optical", [45] = "Coaxial", [47] = "Line-in 2", [49] = "HDMI",
        [51] = "USB", [99] = "Multiroom"
    };

    private readonly Dictionary<string, Bitmap> _artworkCache = new();
    private readonly DispatcherTimer _nowPlayingTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private string _nowPlayingText = string.Empty;
    private bool _nowPlayingBusy;

    internal static string SourceName(int mode) => SourceNames.TryGetValue(mode, out var name) ? name : string.Empty;

    internal async Task<DeviceStatus?> GetDeviceStatusAsync(string ip)
    {
        var statusTask = SendToDeviceAsync(ip, "getPlayerStatus");
        var metaTask = SendToDeviceAsync(ip, "getMetaInfo");
        await Task.WhenAll(statusTask, metaTask).ConfigureAwait(false);
        var status = statusTask.Result;
        if (!int.TryParse(JsonField(status, "vol"), out int volume)) return null;

        var meta = MetaFields(metaTask.Result);
        int.TryParse(JsonField(status, "mode"), out int mode);
        int.TryParse(meta.GetValueOrDefault("sampleRate"), out int sampleRate);
        int.TryParse(meta.GetValueOrDefault("bitDepth"), out int bitDepth);
        return new DeviceStatus(
            new DeviceVolume(Math.Clamp(volume, 0, 100), JsonField(status, "mute") == "1"),
            JsonField(status, "status") ?? string.Empty,
            mode,
            Clean(meta.GetValueOrDefault("title")) ?? Clean(HexText(JsonField(status, "Title"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("artist")) ?? Clean(HexText(JsonField(status, "Artist"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("album")) ?? Clean(HexText(JsonField(status, "Album"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("albumArtURI")) ?? string.Empty,
            sampleRate,
            bitDepth);
    }

    internal async Task<Dictionary<string, DeviceStatus?>> GetAllDeviceStatusesAsync()
    {
        var devices = _knownDevices.Select(d => d.Ip).Distinct().ToList();
        var statuses = await Task.WhenAll(devices.Select(GetDeviceStatusAsync)).ConfigureAwait(false);
        return devices.Zip(statuses).ToDictionary(p => p.First, p => p.Second);
    }

    internal async Task<bool> SendTransportAsync(string ip, string action) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setPlayerCmd:{action}").ConfigureAwait(false));

    internal async Task<Bitmap?> GetArtworkAsync(string url)
    {
        lock (_artworkCache)
            if (_artworkCache.TryGetValue(url, out var cached)) return cached;
        try
        {
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var thumbnail = Bitmap.DecodeToWidth(stream, 160, BitmapInterpolationMode.HighQuality);
            lock (_artworkCache)
            {
                if (_artworkCache.Count > 40) _artworkCache.Clear();
                _artworkCache[url] = thumbnail;
            }
            return thumbnail;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string NowPlayingLine(DeviceStatus status)
    {
        if (!status.IsPlaying || status.Title.Length == 0) return string.Empty;
        return status.Artist.Length > 0 ? $"{status.Title} – {status.Artist}" : status.Title;
    }

    private void StartNowPlayingUpdates()
    {
        _nowPlayingTimer.Tick += async (_, _) => await RefreshNowPlayingAsync();
        _nowPlayingTimer.Start();
        _ = RefreshNowPlayingAsync();
    }

    private async Task RefreshNowPlayingAsync()
    {
        if (_nowPlayingBusy || string.IsNullOrEmpty(_deviceIp)) return;
        _nowPlayingBusy = true;
        try
        {
            if (_knownDevices.Count > 1) await GetMultiroomSnapshotAsync();
            var status = await GetDeviceStatusAsync(PlaybackTarget);
            _nowPlayingText = status == null ? string.Empty : NowPlayingLine(status);
            RefreshDeviceStatusUi();
        }
        finally
        {
            _nowPlayingBusy = false;
        }
    }

    private static Dictionary<int, string> ParsePresetNames(string? json)
    {
        var result = new Dictionary<int, string>();
        if (json == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("preset_list", out var list) ||
                list.ValueKind != JsonValueKind.Array) return result;
            foreach (var p in list.EnumerateArray())
                if (p.TryGetProperty("number", out var number) && int.TryParse(number.ToString(), out int n) &&
                    p.TryGetProperty("name", out var name) && Clean(name.ToString()) is { } text)
                    result[n] = text;
        }
        catch (JsonException) { }
        return result;
    }

    private static Dictionary<string, string> MetaFields(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("metaData", out var meta) &&
                meta.ValueKind == JsonValueKind.Object)
                foreach (var p in meta.EnumerateObject())
                    result[p.Name.Trim()] = p.Value.ToString();
        }
        catch (JsonException) { }
        return result;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Equals("unknow", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string? HexText(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromHexString(hex)); }
        catch (FormatException) { return null; }
    }
}
