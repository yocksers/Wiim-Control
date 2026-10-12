using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly Dictionary<int, string> SourceNames = new()
    {
        [1] = "AirPlay", [2] = "DLNA", [3] = "QPlay", [31] = "Spotify", [32] = "TIDAL Connect", [36] = "Qobuz Connect",
        [40] = "Line-in", [41] = "Bluetooth", [43] = "Optical", [45] = "Coaxial", [47] = "Line-in 2", [49] = "HDMI",
        [51] = "USB", [54] = "Phono", [58] = "HDMI ARC", [99] = "Multiroom"
    };

    private const int ArtworkCacheSize = 40;
    private readonly Dictionary<string, Bitmap> _artworkCache = new();
    private readonly List<string> _artworkOrder = [];
    private readonly DispatcherTimer _nowPlayingTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private string _nowPlayingText = string.Empty;
    private bool _nowPlayingBusy;

    internal static string SourceName(int mode) => SourceNames.TryGetValue(mode, out var name) ? name : string.Empty;

    internal async Task<DeviceStatus?> GetDeviceStatusAsync(string ip)
    {
        var statusTask = SendToDeviceAsync(ip, "getPlayerStatus");
        var metaTask = SendToDeviceAsync(ip, "getMetaInfo");
        var sleepTask = GetSleepSecondsAsync(ip);
        var renamesTask = EnsureInputRenamesAsync(ip);
        await Task.WhenAll(statusTask, metaTask, sleepTask, renamesTask).ConfigureAwait(false);
        var status = statusTask.Result;
        if (!int.TryParse(JsonField(status, "vol"), out int volume)) return null;

        var meta = MetaFields(metaTask.Result);
        int.TryParse(JsonField(status, "mode"), out int mode);
        int.TryParse(meta.GetValueOrDefault("sampleRate"), out int sampleRate);
        int.TryParse(meta.GetValueOrDefault("bitDepth"), out int bitDepth);
        int.TryParse(meta.GetValueOrDefault("bitRate"), out int bitRate);
        if (bitRate > 50000) bitRate /= 1000;
        int loop = int.TryParse(JsonField(status, "loop"), out int l) ? l : 4;
        int.TryParse(JsonField(status, "curpos"), out int position);
        int.TryParse(JsonField(status, "totlen"), out int duration);
        string bluetooth = mode == 41 ? await GetBluetoothSourceAsync(ip).ConfigureAwait(false) : string.Empty;
        return new DeviceStatus(
            new DeviceVolume(Math.Clamp(volume, 0, 100), JsonField(status, "mute") == "1"),
            JsonField(status, "status") ?? string.Empty,
            mode,
            Clean(meta.GetValueOrDefault("title")) ?? Clean(HexText(JsonField(status, "Title"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("artist")) ?? Clean(HexText(JsonField(status, "Artist"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("album")) ?? Clean(HexText(JsonField(status, "Album"))) ?? string.Empty,
            Clean(meta.GetValueOrDefault("albumArtURI")) ?? string.Empty,
            sampleRate,
            bitDepth,
            loop,
            Math.Max(0, position),
            Math.Max(0, duration),
            Math.Max(0, bitRate),
            bluetooth,
            sleepTask.Result);
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
            if (_artworkCache.TryGetValue(url, out var cached))
            {
                _artworkOrder.Remove(url);
                _artworkOrder.Add(url);
                return cached;
            }
        try
        {
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var thumbnail = Bitmap.DecodeToWidth(stream, 160, BitmapInterpolationMode.HighQuality);
            var evicted = new List<Bitmap>();
            lock (_artworkCache)
            {
                if (_artworkCache.TryGetValue(url, out var existing))
                {
                    evicted.Add(thumbnail);
                    thumbnail = existing;
                }
                else
                {
                    _artworkCache[url] = thumbnail;
                    _artworkOrder.Add(url);
                }
                while (_artworkOrder.Count > ArtworkCacheSize)
                {
                    string oldest = _artworkOrder[0];
                    _artworkOrder.RemoveAt(0);
                    if (_artworkCache.Remove(oldest, out var old)) evicted.Add(old);
                }
            }
            if (evicted.Count > 0)
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var bitmap in evicted)
                        if (_settingsWindow?.IsShowingArtwork(bitmap) != true) bitmap.Dispose();
                });
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
            string ip = _deviceIp;
            if (_knownDevices.Count > 1) await GetMultiroomSnapshotAsync();
            string target = PlaybackTarget;
            var status = await GetDeviceStatusAsync(target);
            _nowPlayingText = status == null ? string.Empty : NowPlayingLine(status);
            RefreshDeviceStatusUi();
            bool reachable = target == ip ? status != null : await GetDeviceVolumeAsync(ip) != null;
            if (ip != _deviceIp) return;
            if (reachable) OnWiimRequestSucceeded();
            else await OnWiimRequestFailedAsync();
        }
        finally
        {
            _nowPlayingBusy = false;
        }
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
