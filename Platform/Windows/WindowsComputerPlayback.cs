#if WINDOWS
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WiimControl;

sealed class WindowsComputerPlayback : IComputerPlayback
{
    private const ulong MaxArtBytes = 8 * 1024 * 1024;
    private const int ArtTries = 8;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _shown;
    private string _artKey = string.Empty;
    private byte[]? _art;
    private int _artTries;

    internal static bool IsOwnSession(string? appId) =>
        appId?.Contains("WiimControl", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<ComputerTrack?> ReadAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var all = _manager.GetSessions();
        var session = new[] { _manager.GetCurrentSession() }.Concat(all).FirstOrDefault(s => s != null && !IsOwnSession(s.SourceAppUserModelId) &&
            s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        bool playing = session != null;
        if (session == null && _shown != null)
        {
            string shownId = _shown.SourceAppUserModelId;
            session = all.FirstOrDefault(s => s.SourceAppUserModelId == shownId &&
                s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
        }
        _shown = session;
        if (session == null) return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        if (properties == null || string.IsNullOrWhiteSpace(properties.Title)) return null;
        string artist = properties.Artist is { Length: > 0 } a ? a : properties.AlbumArtist ?? string.Empty;
        string album = properties.AlbumTitle ?? string.Empty;
        string key = $"{session.SourceAppUserModelId}\n{properties.Title}\n{artist}\n{album}";
        if (key != _artKey)
        {
            _artKey = key;
            _art = null;
            _artTries = 0;
        }
        if (_artTries < ArtTries && properties.Thumbnail != null)
        {
            _artTries++;
            if (await ReadThumbnailAsync(properties.Thumbnail) is { } fresh && (_art == null || !fresh.AsSpan().SequenceEqual(_art))) _art = fresh;
        }

        var timeline = session.GetTimelineProperties();
        var now = DateTime.UtcNow;
        var updated = timeline.LastUpdatedTime.UtcDateTime;
        bool updatedKnown = updated.Year >= 2000 && updated <= now;
        int duration = (int)Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalMilliseconds);
        int position = duration > 0 ? (int)Math.Clamp((timeline.Position - timeline.StartTime).TotalMilliseconds, 0, duration) : 0;
        return new ComputerTrack(key, properties.Title, artist, album, playing, position, duration, updatedKnown ? updated : now, string.Empty, _art);
    }

    public async Task<bool> ControlAsync(string action)
    {
        if (_shown is not { } session) return false;
        try
        {
            return action switch
            {
                "next" => await session.TrySkipNextAsync(),
                "prev" => await session.TrySkipPreviousAsync(),
                _ => await session.TryTogglePlayPauseAsync()
            };
        }
        catch (Exception) { return false; }
    }

    public async Task<bool> SeekAsync(int positionMs)
    {
        if (_shown is not { } session) return false;
        try
        {
            var start = session.GetTimelineProperties().StartTime;
            return await session.TryChangePlaybackPositionAsync((start + TimeSpan.FromMilliseconds(Math.Max(0, positionMs))).Ticks);
        }
        catch (Exception) { return false; }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxArtBytes) return null;
            uint size = (uint)stream.Size;
            using var reader = new DataReader(stream);
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception) { return null; }
    }

    public void Dispose()
    {
        _manager = null;
        _shown = null;
    }
}
#endif
