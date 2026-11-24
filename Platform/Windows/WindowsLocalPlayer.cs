#if WINDOWS
using Windows.Media.Core;
using Windows.Media.Playback;

namespace WiimControl;

sealed class WindowsLocalPlayer : ILocalPlayer
{
    private readonly MediaPlayer _player = new() { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Media };
    private MediaSource? _source;

    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action? StateChanged;

    public WindowsLocalPlayer()
    {
        _player.CommandManager.IsEnabled = false;
        _player.MediaEnded += (_, _) => Ended?.Invoke();
        _player.MediaFailed += (_, e) => Failed?.Invoke(Describe(e));
        _player.MediaOpened += (_, _) => StateChanged?.Invoke();
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => StateChanged?.Invoke();
    }

    private static string Describe(MediaPlayerFailedEventArgs e) => e.Error switch
    {
        MediaPlayerError.NetworkError => "the music server didn't send the file",
        MediaPlayerError.DecodingError => "Windows can't decode this file",
        MediaPlayerError.SourceNotSupported => "Windows can't open this file, or doesn't support its format",
        MediaPlayerError.Aborted => "playback was stopped",
        _ => e.ErrorMessage is { Length: > 0 } message ? message : "unknown error"
    };

    public bool Playing => _source != null && _player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening
        or MediaPlaybackState.Buffering;

    public TimeSpan Position => _source == null ? TimeSpan.Zero : _player.PlaybackSession.Position;

    public TimeSpan Duration => _source == null ? TimeSpan.Zero : _player.PlaybackSession.NaturalDuration;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public bool Muted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    public void Open(Uri url) => SetSource(MediaSource.CreateFromUri(url));

    public void Open(Stream stream, string contentType) => SetSource(MediaSource.CreateFromStream(stream.AsRandomAccessStream(), contentType));

    private void SetSource(MediaSource? source)
    {
        var old = _source;
        _source = source;
        _player.Source = source;
        old?.Dispose();
    }

    public void Play() => _player.Play();

    public void Pause() => _player.Pause();

    public void Stop()
    {
        _player.Pause();
        SetSource(null);
        StateChanged?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_source != null && _player.PlaybackSession.CanSeek) _player.PlaybackSession.Position = position;
    }

    public void Dispose()
    {
        SetSource(null);
        _player.Dispose();
    }
}
#endif
