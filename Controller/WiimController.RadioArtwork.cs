using System.Collections.Concurrent;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const string InternetRadioAlbum = "Internet radio";
    private const int RadioArtLinkCacheSize = 200;
    private readonly ConcurrentDictionary<string, bool> _stationGivesArtwork = new();
    private readonly ConcurrentDictionary<string, byte> _radioArtProbing = new();
    private readonly HashSet<string> _radioLookups = [];

    internal static bool IsInternetRadio(DeviceStatus status) =>
        status.Album.Trim().Equals(InternetRadioAlbum, StringComparison.OrdinalIgnoreCase) && status.DurationMs <= 0;

    internal DeviceStatus WithOnlineArtwork(DeviceStatus status)
    {
        if (!_onlineArtwork) return status;
        if (IsInternetRadio(status)) return WithRadioArtwork(status);
        if (status.ArtUrl.Trim().Length > 0 || status.Title.Trim().Length == 0 || status.Artist.Trim().Length == 0) return status;
        var track = new ComputerTrack(string.Empty, status.Title.Trim(), status.Artist.Trim(), status.Album.Trim(), status.IsPlaying, 0, 0,
            DateTime.UtcNow, string.Empty, null);
        _radioLookups.Add(OnlineArtworkKey(track));
        return OnlineArtworkFor(track) is { Length: > 0 } cover ? status with { ArtUrl = cover } : status;
    }

    private DeviceStatus WithRadioArtwork(DeviceStatus status)
    {
        if (!_onlineArtwork || !IsInternetRadio(status) || status.Title.Trim().Length == 0 || status.Artist.Trim().Length == 0) return status;
        string link = status.ArtUrl.Trim();
        if (link.Length > 0 && !link.StartsWith(ComputerArtPrefix, StringComparison.Ordinal))
        {
            if (!_stationGivesArtwork.TryGetValue(link, out bool stationArt))
            {
                if (_radioArtProbing.TryAdd(link, 0)) _ = ProbeRadioArtLinkAsync(link);
                return status;
            }
            if (stationArt) return status;
        }
        var track = new ComputerTrack(string.Empty, status.Title.Trim(), status.Artist.Trim(), string.Empty, status.IsPlaying, 0, 0, DateTime.UtcNow,
            string.Empty, null);
        _radioLookups.Add(OnlineArtworkKey(track));
        return OnlineArtworkFor(track, bothOrders: true) is { Length: > 0 } cover ? status with { ArtUrl = cover } : status;
    }

    private async Task ProbeRadioArtLinkAsync(string link)
    {
        bool stationArt = false;
        try
        {
            var bytes = await HttpFor(link).GetByteArrayAsync(link).ConfigureAwait(false);
            if (LooksLikeJson(bytes))
            {
                var (apple, picture) = ParseStationArtwork(bytes);
                stationArt = apple != null || picture != null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException) { }
        if (_stationGivesArtwork.Count >= RadioArtLinkCacheSize) _stationGivesArtwork.Clear();
        _stationGivesArtwork[link] = stationArt;
        _radioArtProbing.TryRemove(link, out _);
        RefreshRadioArtwork();
    }

    private void RefreshRadioArtwork() => Dispatcher.UIThread.Post(() =>
    {
        _settingsWindow?.OnComputerPlaybackChanged();
        foreach (var window in _miniPlayers.Values.ToList()) window.RefreshNow();
        _ = RefreshNowPlayingAsync();
    });
}
