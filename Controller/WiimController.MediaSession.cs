using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private bool _mediaControls;
    private IMediaSession? _mediaSession;

    private void UpdateMediaSession()
    {
        if (_mediaControls && _mediaSession == null)
        {
            try { _mediaSession = CreateMediaSession(); }
            catch (Exception ex)
            {
                ErrorLog.Write("Media controls", ex);
                Notify($"Couldn't show the device in the media controls: {ex.Message}");
            }
            if (_mediaSession != null) _ = RefreshNowPlayingAsync();
        }
        else if (!_mediaControls && _mediaSession != null)
        {
            _mediaSession.Dispose();
            _mediaSession = null;
        }
        _nowPlayingTimer.Interval = TimeSpan.FromSeconds(_mediaSession != null ? 4 : 10);
    }

    private IMediaSession? CreateMediaSession()
    {
#if WINDOWS
        return new WindowsMediaSession(OnMediaSessionCommand, OnMediaSessionSeek);
#else
        if (OperatingSystem.IsLinux())
            return LinuxMediaSession.Create(OnMediaSessionCommand, OnMediaSessionSeek, () => Dispatcher.UIThread.Post(ShowSettingsWindow),
                OnMediaSessionVolume);
        if (OperatingSystem.IsMacOS()) return MacMediaSession.Create(OnMediaSessionCommand, OnMediaSessionSeek);
        return null;
#endif
    }

    internal static string MediaControlsName =>
        OperatingSystem.IsWindows() ? "Windows media controls" : OperatingSystem.IsMacOS() ? "macOS Now Playing controls" : "desktop's media controls";

    private static bool IsNetworkMode(int mode) => mode != 99 && InputOfMode(mode) == "wifi";

    private void SetMediaControls(bool value)
    {
        _mediaControls = value;
        SaveConfig();
        UpdateMediaSession();
    }

    private void OnMediaSessionCommand(string action) => Dispatcher.UIThread.Post(async () =>
    {
        await SendMediaCommandAsync(action);
        await Task.Delay(400);
        await RefreshNowPlayingAsync();
    });

    private void OnMediaSessionVolume(int volume) => Dispatcher.UIThread.Post(async () =>
    {
        if (_deviceIp.Length == 0) return;
        await SetDeviceVolumeAsync(_deviceIp, volume);
        await RefreshNowPlayingAsync();
    });

    private void OnMediaSessionSeek(int seconds) => Dispatcher.UIThread.Post(async () =>
    {
        await SeekAsync(PlaybackTarget, seconds);
        await RefreshNowPlayingAsync();
    });
}
