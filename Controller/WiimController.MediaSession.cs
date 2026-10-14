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
#if WINDOWS
            try { _mediaSession = new WindowsMediaSession(OnMediaSessionCommand, OnMediaSessionSeek); }
            catch (Exception ex) { Notify($"Couldn't show the amp in the Windows media controls: {ex.Message}"); }
#endif
            if (_mediaSession != null) _ = RefreshNowPlayingAsync();
        }
        else if (!_mediaControls && _mediaSession != null)
        {
            _mediaSession.Dispose();
            _mediaSession = null;
        }
        _nowPlayingTimer.Interval = TimeSpan.FromSeconds(_mediaSession != null ? 4 : 10);
    }

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

    private void OnMediaSessionSeek(int seconds) => Dispatcher.UIThread.Post(async () =>
    {
        await SeekAsync(PlaybackTarget, seconds);
        await RefreshNowPlayingAsync();
    });
}
