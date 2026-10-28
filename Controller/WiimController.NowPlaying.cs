using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly Dictionary<int, string> SourceNames = new()
    {
        [1] = "AirPlay", [2] = "DLNA", [3] = "Alexa", [31] = "Spotify", [34] = "Lyrion", [32] = "TIDAL Connect", [36] = "Qobuz Connect",
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

    private const int AirPlayMode = 1;

    internal static string TrackArtUrl(string url, string ip, int mode, string title, string artist, string album)
    {
        if (url.Length == 0) return url;
        bool onDevice = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals(ip, StringComparison.OrdinalIgnoreCase);
        if (mode != AirPlayMode && !onDevice) return url;
        return $"{url}#{Uri.EscapeDataString($"{title}|{artist}|{album}")}";
    }

    internal async Task<DeviceStatus?> GetDeviceStatusAsync(string ip)
    {
        var statusTask = SendToDeviceAsync(ip, "getPlayerStatus");
        var metaTask = SendToDeviceAsync(ip, "getMetaInfo");
        var sleepTask = GetSleepSecondsAsync(ip);
        var renamesTask = EnsureInputRenamesAsync(ip);
        var connectionTask = EnsureConnectionAsync(ip);
        var infoTask = GetTransportInfoAsync(ip);
        var statusAt = AnsweredAt(statusTask);
        var timesAt = AnsweredAt(infoTask);
        await Task.WhenAll(statusTask, metaTask, sleepTask, renamesTask, connectionTask).ConfigureAwait(false);
        var status = statusTask.Result;
        if (!int.TryParse(JsonField(status, "vol"), out int volume)) return null;

        var meta = MetaFields(metaTask.Result);
        int.TryParse(JsonField(status, "mode"), out int mode);
        _lastModes[ip] = (DateTime.UtcNow, mode);
        int.TryParse(meta.GetValueOrDefault("sampleRate"), out int sampleRate);
        int.TryParse(meta.GetValueOrDefault("bitDepth"), out int bitDepth);
        int.TryParse(meta.GetValueOrDefault("bitRate"), out int bitRate);
        if (bitRate > 50000) bitRate /= 1000;
        int loop = int.TryParse(JsonField(status, "loop"), out int l) ? l : 4;
        int.TryParse(JsonField(status, "curpos"), out int position);
        int.TryParse(JsonField(status, "totlen"), out int duration);
        var positionAt = await statusAt.ConfigureAwait(false);
        bool infoArrived = IsNetworkMode(mode) && await Task.WhenAny(infoTask, Task.Delay(TransportTimesTimeout)).ConfigureAwait(false) == infoTask;
        string audioFormat = IsDigitalInputMode(mode) ? CurrentAudioFormat(ip, infoTask) : string.Empty;
        string serviceQuality = infoArrived && IsNetworkMode(mode)
            ? ServiceQualityOf(infoTask.Result, Clean(JsonField(status, "vendor")) ?? string.Empty)
            : string.Empty;
        if (infoArrived && IsNetworkMode(mode) && TransportTimes(infoTask.Result) is { } times)
        {
            duration = times.DurationMs;
            if (times.PositionMs is { } relative && Math.Abs(position - relative) > PositionAgreementMs)
            {
                position = relative;
                positionAt = await timesAt.ConfigureAwait(false);
            }
            position = Math.Min(position, duration);
        }
        string bluetooth = mode == 41 ? await GetBluetoothSourceAsync(ip).ConfigureAwait(false) : string.Empty;
        NoteBluetoothSource(ip, mode, bluetooth);
        var deviceVolume = new DeviceVolume(Math.Clamp(volume, 0, 100), JsonField(status, "mute") == "1");
        if (IsBluetoothFromThisComputer(ip) && await ThisComputer.GetVolumeAsync().ConfigureAwait(false) is { } computerVolume)
            deviceVolume = computerVolume;
        string title = Clean(meta.GetValueOrDefault("title")) ?? Clean(HexText(JsonField(status, "Title"))) ?? string.Empty;
        string artist = Clean(meta.GetValueOrDefault("artist")) ?? Clean(HexText(JsonField(status, "Artist"))) ?? string.Empty;
        string album = Clean(meta.GetValueOrDefault("album")) ?? Clean(HexText(JsonField(status, "Album"))) ?? string.Empty;
        return WithPcAudioTitle(ip, await ApplyRoonAsync(ip, new DeviceStatus(
            deviceVolume,
            JsonField(status, "status") ?? string.Empty,
            mode,
            title,
            artist,
            album,
            TrackArtUrl(Clean(meta.GetValueOrDefault("albumArtURI")) ?? string.Empty, ip, mode, title, artist, album),
            sampleRate,
            TrackBitDepth(bitDepth),
            loop,
            Math.Max(0, position),
            Math.Max(0, duration),
            Math.Max(0, bitRate),
            bluetooth,
            sleepTask.Result,
            Vendor: Clean(JsonField(status, "vendor")) ?? string.Empty) { PositionAt = positionAt, AudioFormat = audioFormat, ServiceQuality = serviceQuality }).ConfigureAwait(false));
    }

    private static readonly TimeSpan TransportTimesTimeout = TimeSpan.FromSeconds(3);
    private const int PositionAgreementMs = 2000;

    private async Task<System.Xml.Linq.XElement?> GetTransportInfoAsync(string ip)
    {
        string url = await GetServiceUrlAsync(ip, TransportEventService, "/upnp/control/rendertransport1").ConfigureAwait(false);
        return await Upnp.CallAsync(url, TransportEventService, "GetInfoEx", ("InstanceID", "0")).ConfigureAwait(false);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _audioFormats = new();

    private string CurrentAudioFormat(string ip, Task<System.Xml.Linq.XElement?> infoTask)
    {
        if (infoTask.IsCompletedSuccessfully) _audioFormats[ip] = AudioFormatOf(infoTask.Result);
        else
            _ = infoTask.ContinueWith(t => { if (t.IsCompletedSuccessfully) _audioFormats[ip] = AudioFormatOf(t.Result); },
                TaskScheduler.Default);
        return _audioFormats.GetValueOrDefault(ip, string.Empty);
    }

    private static bool IsDigitalInputMode(int mode) => InputOfMode(mode) is "optical" or "co-axial" or "HDMI";

    private static string SongField(string metadata, string name) =>
        System.Text.RegularExpressions.Regex.Match(metadata, $@"<song:{name}[^>]*>([^<]*)<") is { Success: true } match
            ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim()
            : string.Empty;

    internal static string AudioFormatOf(System.Xml.Linq.XElement? infoEx)
    {
        string metadata = Child(infoEx, "TrackMetaData");
        return AudioFormatName(SongField(metadata, "coding_f"), SongField(metadata, "dolby_detail_f"), SongField(metadata, "stream_ch"));
    }

    internal static string ServiceQualityOf(System.Xml.Linq.XElement? infoEx, string vendor)
    {
        string metadata = Child(infoEx, "TrackMetaData");
        return ServiceQualityName($"{Child(infoEx, "TrackSource")} {vendor}", SongField(metadata, "actualQuality"), SongField(metadata, "quality"),
            SongField(metadata, "atmos") == "1");
    }

    internal static string ServiceQualityName(string source, string actualQuality, string quality, bool atmos)
    {
        bool From(string name) => source.Contains(name, StringComparison.OrdinalIgnoreCase);
        if (From("qobuz"))
            return (actualQuality.Length > 0 ? actualQuality : quality) switch
            {
                "1" or "5" => "MP3",
                "2" or "6" => "CD",
                "3" or "4" or "7" or "27" => "Hi-Res",
                _ => string.Empty
            };
        if (From("tidal"))
            return atmos ? string.Empty : actualQuality.ToUpperInvariant() switch
            {
                "HI_RES_LOSSLESS" => "FLAC",
                "HI_RES" => "MQA",
                "LOSSLESS" => "HIGH",
                _ => string.Empty
            };
        if (From("amazon") || From("prime"))
            return actualQuality.ToUpperInvariant() switch
            {
                "UHD" => "ULTRA HD",
                "HD" => "HD",
                _ => string.Empty
            };
        return string.Empty;
    }

    internal static string AudioFormatName(string coding, string dolbyDetail, string channels)
    {
        string code = coding.ToUpperInvariant();
        if (code.Length == 0) return string.Empty;
        if (dolbyDetail.Equals("Dolby Atmos", StringComparison.OrdinalIgnoreCase)) return "Dolby Atmos";
        string WithChannels(string name) => channels.Length > 0 ? $"{name} {channels}" : name;
        return code switch
        {
            "LPCM" => channels is "2" or "2.0" ? "Stereo PCM" : channels is "5.1" or "7.1" ? $"Multichannel PCM {channels}" : "LPCM",
            "DTSUHD" => "DTS:X",
            "DTSHD" => "DTS-HD",
            "DTS" or "DTSCD" => "DTS",
            _ => WithChannels(dolbyDetail.Equals("Multichannel PCM", StringComparison.OrdinalIgnoreCase) || dolbyDetail.Equals("PCM", StringComparison.OrdinalIgnoreCase)
                ? "Dolby Multichannel PCM"
                : dolbyDetail.Length > 0 ? dolbyDetail : "Dolby Audio")
        };
    }

    internal static (int? PositionMs, int DurationMs)? TransportTimes(System.Xml.Linq.XElement? infoEx)
    {
        var duration = Didl.ParseDuration(Child(infoEx, "TrackDuration"));
        if (duration <= TimeSpan.Zero) return null;
        bool trustPosition = !Child(infoEx, "TrackSource").Contains("Prime", StringComparison.Ordinal) ||
                             Child(infoEx, "CurrentTransportState") == "PLAYING";
        var position = Didl.ParseDuration(Child(infoEx, "RelTime"));
        return (trustPosition ? (int)Math.Min(position.TotalMilliseconds, duration.TotalMilliseconds) : null, (int)duration.TotalMilliseconds);
    }

    private const int DecoderBitDepth = 32;
    private const int StreamBitDepth = 24;

    internal static int TrackBitDepth(int reported) => reported == DecoderBitDepth ? StreamBitDepth : reported;

    private static async Task<DateTime> AnsweredAt(Task task)
    {
        await Task.WhenAny(task).ConfigureAwait(false);
        return DateTime.UtcNow;
    }

    internal async Task<Dictionary<string, DeviceStatus?>> GetAllDeviceStatusesAsync()
    {
        var devices = _knownDevices.Select(d => d.Ip).Distinct().ToList();
        var statuses = await Task.WhenAll(devices.Select(GetDeviceStatusAsync)).ConfigureAwait(false);
        return devices.Zip(statuses).ToDictionary(p => p.First, p => p.Second);
    }

    internal async Task<bool> SendTransportAsync(string ip, string action) =>
        await TryExternalCommandAsync(ip, action).ConfigureAwait(false) ||
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
            var bytes = await HttpFor(url).GetByteArrayAsync(url).ConfigureAwait(false);
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
            if (evicted.Count > 0) DisposeWhenUnused(evicted);
            return thumbnail;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly List<Bitmap> _artworkToDispose = [];

    private void DisposeWhenUnused(IEnumerable<Bitmap> bitmaps) => Dispatcher.UIThread.Post(() =>
    {
        _artworkToDispose.AddRange(bitmaps);
        for (int i = _artworkToDispose.Count - 1; i >= 0; i--)
        {
            if (_settingsWindow?.IsShowingArtwork(_artworkToDispose[i]) == true || IsMiniPlayerShowingArtwork(_artworkToDispose[i]) ||
                _trackToast?.IsShowingArtwork(_artworkToDispose[i]) == true) continue;
            _artworkToDispose[i].Dispose();
            _artworkToDispose.RemoveAt(i);
        }
    });

    private void DisposeArtworkCaches()
    {
        lock (_artworkCache)
        {
            foreach (var bitmap in _artworkCache.Values) bitmap.Dispose();
            _artworkCache.Clear();
            _artworkOrder.Clear();
        }
        lock (_largeArtwork)
        {
            foreach (var (_, bitmap) in _largeArtwork) bitmap.Dispose();
            _largeArtwork.Clear();
        }
        lock (_backdrops)
        {
            foreach (var (_, bitmap) in _backdrops) bitmap.Dispose();
            _backdrops.Clear();
        }
        lock (_wallpaperCache)
        {
            foreach (var bitmap in _wallpaperCache.Values) bitmap.Dispose();
            _wallpaperCache.Clear();
            _wallpaperOrder.Clear();
        }
        foreach (var bitmap in _artworkToDispose) bitmap.Dispose();
        _artworkToDispose.Clear();
    }

    private const int LossyMaxKbps = 400;

    private const int CdBitDepth = 16;
    private const int CdMaxSampleRate = 48000;

    internal static bool IsLossless(DeviceStatus status) =>
        status.SampleRate >= 44100 && status.BitDepth >= CdBitDepth && status.BitRate > LossyMaxKbps;

    internal static string LosslessTag(DeviceStatus status) =>
        status.Title == PcAudioTitle || !IsLossless(status) ? string.Empty
        : status.BitDepth > CdBitDepth || status.SampleRate > CdMaxSampleRate ? "Hi-Res Lossless"
        : "Lossless";

    internal const string GroupQualityNote = "Group: 48 kHz, 16-bit";

    internal static string Quality(DeviceStatus status, bool grouped = false)
    {
        var parts = new List<string>();
        if (status.AudioFormat.Length > 0) parts.Add(status.AudioFormat);
        if (status.SampleRate > 0) parts.Add((status.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz");
        if (status.SampleRate > 0 && status.BitDepth > 0) parts.Add($"{status.BitDepth}-bit");
        if (status.BitRate > 0) parts.Add($"{status.BitRate} kbps");
        if (status.ServiceQuality.Length > 0) parts.Add(status.ServiceQuality);
        else if (status.AudioFormat.Length == 0 && LosslessTag(status) is { Length: > 0 } tag) parts.Add(tag);
        if (grouped && (status.SampleRate > CdMaxSampleRate || status.BitDepth > CdBitDepth)) parts.Add(GroupQualityNote);
        return string.Join("  ·  ", parts);
    }

    internal static string FormatTime(int seconds) =>
        seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

    internal string DeviceNameOf(string ip) =>
        _knownDevices.FindIndex(d => d.Ip == ip) is var index and >= 0 ? _knownDevices[index].Name : ip;

    internal string SourceText(DeviceStatus status, string leaderIp, bool following)
    {
        string source = status.Mode == 41 && status.BluetoothName.Length > 0
            ? $"Bluetooth  ·  {status.BluetoothName}"
            : ServiceName(status);
        if (source.Length == 0 && InputOfMode(status.Mode) is { } input && input != "wifi") source = InputName(leaderIp, input);
        if (!following) return source;
        string leader = DeviceNameOf(leaderIp);
        return source.Length > 0 ? $"With {leader}  ·  {source}" : $"With {leader}";
    }

    internal (string Title, string Artist, string Detail) NowPlayingTexts(DeviceStatus status, string leaderIp, bool following)
    {
        string source = SourceText(status, leaderIp, following);
        if (!following && status.Title.Length == 0 && InputOfMode(status.Mode) is { } input && input != "wifi")
        {
            string subtitle = input == "bluetooth" && status.BluetoothName.Length > 0 ? status.BluetoothName
                : status.IsPlaying ? "Playing" : "Not playing";
            return (InputName(leaderIp, input), subtitle, Quality(status, following || IsGrouped(leaderIp)));
        }
        string title = TitleText(status, source);
        return (title, ArtistText(status, title), DetailText(status, source, following || IsGrouped(leaderIp)));
    }

    internal static string TitleText(DeviceStatus status, string source) =>
        status.Title.Length > 0 ? status.Title
        : status.IsPlaying ? (source.Length > 0 ? source : "Playing")
        : "Not playing";

    internal static string ArtistText(DeviceStatus status, string? title) =>
        string.Join("  ·  ", new[] { status.Artist.Trim(), status.Album.Trim() }
            .Where(t => t.Length > 0 && !t.Equals(title?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    internal static string DetailText(DeviceStatus status, string source, bool grouped = false) =>
        string.Join("  ·  ", new[] { source, Quality(status, grouped) }.Where(t => t.Length > 0));

    internal static string NowPlayingLine(DeviceStatus status)
    {
        if (!status.IsPlaying || status.Title.Length == 0) return string.Empty;
        string artist = status.Artist.Trim();
        return artist.Length > 0 && !artist.Equals(status.Title.Trim(), StringComparison.OrdinalIgnoreCase)
            ? $"{status.Title} – {artist}"
            : status.Title;
    }

    private void StartNowPlayingUpdates()
    {
        UpdateNowPlayingInterval();
        _nowPlayingTimer.Tick += async (_, _) => await RefreshNowPlayingAsync();
        _nowPlayingTimer.Start();
        _ = RefreshNowPlayingAsync();
        StartSleepWatch();
    }

    private async Task RefreshNowPlayingAsync()
    {
        if (_nowPlayingBusy || string.IsNullOrEmpty(_deviceIp)) return;
        _nowPlayingBusy = true;
        try
        {
            string ip = _deviceIp;
            if (_knownDevices.Count > 1 && DateTime.UtcNow - _multiroomCacheTime > MultiroomCacheLifetime) await GetMultiroomSnapshotAsync();
            string target = PlaybackTarget;
            var status = await GetDeviceStatusAsync(target);
            _nowPlayingText = status == null ? string.Empty : NowPlayingLine(status);
            _mediaSession?.Update(status == null || IsNetworkMode(status.Mode) ? status : status with { PositionMs = 0, DurationMs = 0 });
            RefreshDeviceStatusUi();
            NoteDeviceStatus(target, status);
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
        try { return DeviceText(Convert.FromHexString(hex)); }
        catch (FormatException) { return null; }
    }
}
