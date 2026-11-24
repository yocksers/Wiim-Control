#if WINDOWS
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace WiimControl;

sealed class WindowsMediaSession : IMediaSession
{
    private readonly MediaPlayer _player = new();
    private readonly SystemMediaTransportControls _controls;
    private readonly Action<string> _onCommand;
    private readonly Action<int> _onSeek;
    private readonly Func<string, Task<byte[]?>> _loadArtwork;
    private string _shownTrack = string.Empty;
    private string _shownArt = string.Empty;

    public WindowsMediaSession(Action<string> onCommand, Action<int> onSeek, Func<string, Task<byte[]?>> loadArtwork)
    {
        _onCommand = onCommand;
        _onSeek = onSeek;
        _loadArtwork = loadArtwork;
        _player.CommandManager.IsEnabled = false;
        _controls = _player.SystemMediaTransportControls;
        _controls.IsPlayEnabled = _controls.IsPauseEnabled = _controls.IsNextEnabled = _controls.IsPreviousEnabled = true;
        _controls.IsStopEnabled = true;
        _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        _controls.ButtonPressed += OnButtonPressed;
        _controls.PlaybackPositionChangeRequested += OnPositionChangeRequested;
        _controls.IsEnabled = true;
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        string? command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => "resume",
            SystemMediaTransportControlsButton.Pause => "pause",
            SystemMediaTransportControlsButton.Next => "next",
            SystemMediaTransportControlsButton.Previous => "prev",
            SystemMediaTransportControlsButton.Stop => "stop",
            _ => null
        };
        if (command != null) _onCommand(command);
    }

    private void OnPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args) =>
        _onSeek((int)args.RequestedPlaybackPosition.TotalSeconds);

    public void Update(DeviceStatus? status)
    {
        if (status == null)
        {
            _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            return;
        }

        string track = $"{status.Title}\n{status.Artist}\n{status.Album}";
        if (track != _shownTrack || status.ArtUrl != _shownArt)
        {
            var display = _controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = status.Title.Length > 0 ? status.Title : WiimController.SourceName(status.Mode);
            display.MusicProperties.Artist = status.Artist;
            display.MusicProperties.AlbumTitle = status.Album;
            bool web = Uri.TryCreate(status.ArtUrl, UriKind.Absolute, out var art) && art.Scheme is "http" or "https";
            display.Thumbnail = web ? RandomAccessStreamReference.CreateFromUri(art) : null;
            display.Update();
            _shownTrack = track;
            _shownArt = status.ArtUrl;
            if (!web && status.ArtUrl.Length > 0) _ = ShowLoadedArtworkAsync(status.ArtUrl);
        }

        _controls.PlaybackStatus = status.PlayState.ToLowerInvariant() switch
        {
            "play" => MediaPlaybackStatus.Playing,
            "pause" => MediaPlaybackStatus.Paused,
            "load" => MediaPlaybackStatus.Changing,
            _ => MediaPlaybackStatus.Stopped
        };

        var duration = TimeSpan.FromMilliseconds(status.DurationMs);
        _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = duration,
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = duration,
            Position = TimeSpan.FromMilliseconds(Math.Min(status.PositionMs, status.DurationMs))
        });
    }

    private async Task ShowLoadedArtworkAsync(string url)
    {
        try
        {
            if (await _loadArtwork(url) is not { Length: > 0 } bytes || url != _shownArt) return;
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            if (url != _shownArt) return;
            var display = _controls.DisplayUpdater;
            display.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
            display.Update();
        }
        catch (Exception ex)
        {
            ErrorLog.Unexpected("Media controls", ex);
        }
    }

    public void Dispose()
    {
        _controls.ButtonPressed -= OnButtonPressed;
        _controls.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
        _controls.IsEnabled = false;
        _player.Dispose();
    }
}
#endif
