using System.Collections.Concurrent;
using System.Xml.Linq;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private const string PlayQueueService = "urn:schemas-wiimu-com:service:PlayQueue:1";
    internal const int MaxQueueTracks = 500;

    private readonly ConcurrentDictionary<string, string> _playQueueUrls = new();
    private readonly SemaphoreSlim _thumbnailGate = new(6, 6);
    private List<RadioStation> _radioFavorites = [];
    private List<MediaServer>? _mediaServers;
    private string _libraryServer = string.Empty;

    private string? _radioCountry;

    internal IReadOnlyList<RadioStation> RadioFavorites => _radioFavorites;
    internal string LibraryServer => _libraryServer;
    internal string RadioCountryCode => _radioCountry ??= RadioDirectory.HomeCountry();

    private async Task<string> GetPlayQueueUrlAsync(string ip)
    {
        if (_playQueueUrls.TryGetValue(ip, out var cached)) return cached;
        string url = $"http://{ip}:49152/upnp/control/PlayQueue1";
        try
        {
            string location = $"http://{ip}:49152/description.xml";
            var doc = XDocument.Parse(await _http.GetStringAsync(location).ConfigureAwait(false));
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            if (doc.Descendants(ns + "service").FirstOrDefault(s => s.Element(ns + "serviceType")?.Value == PlayQueueService)
                    ?.Element(ns + "controlURL")?.Value is { Length: > 0 } control)
                url = new Uri(new Uri(location), control).ToString();
        }
        catch (Exception) { }
        _playQueueUrls[ip] = url;
        return url;
    }

    internal async Task<bool> PlayTracksAsync(string ip, IReadOnlyList<QueueTrack> tracks, int startIndex)
    {
        if (tracks.Count == 0) return false;
        string context = Didl.QueueContext("WiimControl", tracks, Math.Clamp(startIndex, 0, tracks.Count - 1), radio: false);
        bool ok = await Upnp.CallAsync(await GetPlayQueueUrlAsync(ip).ConfigureAwait(false), PlayQueueService, "CreateQueue",
            ("QueueContext", context)).ConfigureAwait(false) != null;
        if (ok && ip == _deviceIp) OnWiimRequestSucceeded();
        return ok;
    }

    internal async Task<bool> PlayStationAsync(string ip, RadioStation station)
    {
        var track = new QueueTrack(station.Url, station.Name, station.Name, "Internet radio", station.ArtUrl, TimeSpan.Zero, "http-get:*:audio/mpeg:*");
        string context = Didl.QueueContext("WiimControlRadio", [track], 0, radio: true);
        bool ok = await Upnp.CallAsync(await GetPlayQueueUrlAsync(ip).ConfigureAwait(false), PlayQueueService, "CreateQueue",
            ("QueueContext", context)).ConfigureAwait(false) != null;
        if (ok) _ = RadioDirectory.CountClickAsync(station.Uuid);
        return ok;
    }

    internal async Task<Bitmap?> LoadThumbnailAsync(string url, int width, Func<bool>? stillWanted = null)
    {
        await _thumbnailGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stillWanted?.Invoke() == false) return null;
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    internal bool IsRadioFavorite(RadioStation station) => _radioFavorites.Any(f => f.Url == station.Url);

    internal void SetRadioFavorite(RadioStation station, bool favorite)
    {
        _radioFavorites.RemoveAll(f => f.Url == station.Url);
        if (favorite) _radioFavorites.Add(station);
        SaveConfig();
    }

    internal void SetLibraryServer(string udn)
    {
        if (udn == _libraryServer) return;
        _libraryServer = udn;
        SaveConfig();
    }

    private static string CleanField(string value) => value.Replace('\u0001', ' ').Replace('\u0002', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private string SerializeRadioFavorites() =>
        string.Join('\u0002', _radioFavorites.Select(s =>
            string.Join('\u0001', CleanField(s.Name), CleanField(s.Url), CleanField(s.ArtUrl), CleanField(s.Detail), CleanField(s.Uuid))));

    private static List<RadioStation> ParseRadioFavorites(string value) =>
        value.Split('\u0002', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('\u0001'))
            .Where(parts => parts.Length == 5 && parts[1].Length > 0)
            .Select(parts => new RadioStation(parts[0], parts[1], parts[2], parts[3], parts[4]))
            .ToList();
}
