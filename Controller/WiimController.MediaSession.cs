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
        return new WindowsMediaSession(OnMediaSessionCommand, OnMediaSessionSeek, LoadMediaArtworkAsync);
#else
        if (OperatingSystem.IsLinux())
            return LinuxMediaSession.Create(OnMediaSessionCommand, OnMediaSessionSeek, () => Dispatcher.UIThread.Post(ShowSettingsWindow),
                OnMediaSessionVolume);
        if (OperatingSystem.IsMacOS()) return MacMediaSession.Create(OnMediaSessionCommand, OnMediaSessionSeek, LoadMediaArtworkAsync);
        return null;
#endif
    }

    private DeviceStatus? _sessionStatus;
    private readonly Dictionary<string, string> _sessionArtFiles = new();

    private void ShowInMediaSession(DeviceStatus? status)
    {
        _sessionStatus = status;
        if (_mediaSession == null) return;
        if (OperatingSystem.IsLinux() && status is { ArtUrl.Length: > 0 } && !IsWebAddress(status.ArtUrl))
        {
            if (_sessionArtFiles.TryGetValue(status.ArtUrl, out var file)) status = status with { ArtUrl = new Uri(file).AbsoluteUri };
            else
            {
                _ = SaveSessionArtAsync(status.ArtUrl);
                status = status with { ArtUrl = string.Empty };
            }
        }
        _mediaSession.Update(status);
    }

    private static bool IsWebAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file";

    private async Task SaveSessionArtAsync(string url)
    {
        if (await LoadMediaArtworkAsync(url) is not { Length: > 0 } bytes) return;
        string folder = Path.Combine(Path.GetTempPath(), "WiimControl-art");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in _sessionArtFiles.Values) LocalTempFile.Delete(old);
            _sessionArtFiles.Clear();
            string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".img");
            await File.WriteAllBytesAsync(file, bytes);
            Dispatcher.UIThread.Post(() =>
            {
                _sessionArtFiles[url] = file;
                if (_sessionStatus?.ArtUrl == url) ShowInMediaSession(_sessionStatus);
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<byte[]?> LoadMediaArtworkAsync(string url)
    {
        try { return await ArtworkBytesAsync(url).ConfigureAwait(false); }
        catch (Exception) { return null; }
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
        if (OperatingSystem.IsWindows() && !_forwardMediaKeys && IsMediaKeyEcho()) return;
        if (LocalPlayerActive && LocalTransport(action)) return;
        await SendMediaCommandAsync(action);
        await Task.Delay(400);
        await RefreshNowPlayingAsync();
    });

    private void OnMediaSessionVolume(int volume) => Dispatcher.UIThread.Post(async () =>
    {
        if (LocalPlayerActive)
        {
            SetLocalVolume(volume);
            return;
        }
        if (_deviceIp.Length == 0 || BlockedByLock(_deviceIp)) return;
        await SetVolumeAsync(_deviceIp, volume);
        await RefreshNowPlayingAsync();
    });

    private void OnMediaSessionSeek(int seconds) => Dispatcher.UIThread.Post(async () =>
    {
        if (LocalPlayerActive)
        {
            LocalSeek(seconds);
            return;
        }
        if (BlockedByLock(_deviceIp, PlaybackTarget)) return;
        await SeekAsync(PlaybackTarget, seconds);
        await RefreshNowPlayingAsync();
    });
}
