using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly TimeSpan ToastArtworkWait = TimeSpan.FromSeconds(2);

    private bool _trackToasts;
    private TrackToastWindow? _trackToast;
    private readonly Dictionary<string, string> _lastTracks = new();

    internal bool TrackToasts
    {
        get => _trackToasts;
        set
        {
            _trackToasts = value;
            UpdateNowPlayingInterval();
            SaveConfig();
            if (value) _ = PreviewTrackToastAsync();
        }
    }

    private void UpdateNowPlayingInterval() =>
        _nowPlayingTimer.Interval = _miniPlayerAuto || _trackToasts || (_artworkBackground && _settingsWindow != null)
            ? FastNowPlayingInterval
            : NowPlayingInterval;

    internal void NoteDeviceStatus(string ip, DeviceStatus? status)
    {
        NoteDeviceInput(ip, status);
        NoteTrack(ip, status);
        NoteActiveArtwork(ip, status);
    }

    private static string TrackKey(DeviceStatus status) => $"{status.Title.Trim()}\n{status.Artist.Trim()}\n{status.Album.Trim()}";

    private void NoteTrack(string ip, DeviceStatus? status)
    {
        if (status == null) return;
        string key = TrackKey(status);
        bool known = _lastTracks.TryGetValue(ip, out var previous);
        _lastTracks[ip] = key;
        if (!known || previous == key || !_trackToasts || ip != PlaybackTarget || !status.IsPlaying || status.Title.Trim().Length == 0) return;
        _ = ShowTrackToastAsync(ip, status);
    }

    private async Task PreviewTrackToastAsync()
    {
        string target = PlaybackTarget;
        var status = await GetDeviceStatusAsync(target);
        if (status is { IsPlaying: true } && status.Title.Trim().Length > 0 && _trackToasts) await ShowTrackToastAsync(target, status);
    }

    private async Task ShowTrackToastAsync(string target, DeviceStatus status)
    {
        string key = TrackKey(status);
        string deviceIp = _deviceIp;
        Bitmap? art = null;
        if (status.ArtUrl.Length > 0)
        {
            var load = GetArtworkAsync(status.ArtUrl);
            if (await Task.WhenAny(load, Task.Delay(ToastArtworkWait)) == load) art = await load;
        }
        if (!_trackToasts || deviceIp != _deviceIp || _lastTracks.GetValueOrDefault(target) != key) return;

        string source = SourceText(status, target, false);
        string title = TitleText(status, source);
        string device = DeviceNameOf(deviceIp);
        string footer = source.Length > 0 ? $"{device}  ·  {source}" : device;
        if (_trackToast == null)
        {
            _trackToast = new TrackToastWindow();
            _trackToast.VisibilityChanged += MakeRoomForTrackToast;
        }
        _trackToast.ShowTrack(art, title, ArtistText(status, title), footer, _osdCorner, _osdScalePercent / 100.0, () => ShowMiniPlayer(deviceIp));
    }
}
