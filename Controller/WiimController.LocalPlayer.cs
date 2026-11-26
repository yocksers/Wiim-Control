using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    internal const string LocalIp = "this-computer";
    internal const string LocalName = "This computer";
    private const int LocalMode = 10;
    private const int LocalRestartMs = 3000;
    private const string LocalArtPrefix = "local-art:";
    private const int LocalArtProbeBytes = 4 * 1024 * 1024;
    private const long MaxLocalTrackBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan LocalShareTimeout = TimeSpan.FromMinutes(3);

    private ILocalPlayer? _localPlayer;
    private bool _localPlayerFailed;
    private readonly List<QueueTrack> _localQueue = [];
    private readonly List<QueueTrack> _localOriginal = [];
    private int _localIndex = -1;
    private bool _localStopped;
    private bool _localShuffle;
    private int _localRepeat;
    private int _localVolume = 50;
    private bool _localMuted;
    private int _localLoad;
    private int _localFailures;

    internal event Action? LocalPlayerChanged;
    internal event Action<string>? LocalPlayerProblem;

    private static readonly Lazy<bool> LocalPlayerAvailable = new(() =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && GStreamerLocalPlayer.Available()) ||
        (OperatingSystem.IsMacOS() && MacLocalPlayer.Available()));

    internal static bool LocalPlayerSupported => LocalPlayerAvailable.Value;

    internal static bool IsLocalIp(string? ip) => ip == LocalIp;

    private bool _localInSession;
    private const int LocalArtKept = 30;
    private readonly List<LocalTrackInfo> _localInfo = [];
    private readonly HashSet<string> _localLookups = [];
    private int _localArtNumber;

    internal bool LibraryOnComputer { get; set; }

    internal QueueTrack? LocalNowPlaying => _localIndex >= 0 && _localIndex < _localQueue.Count ? _localQueue[_localIndex] : null;

    internal bool LocalPlayerActive => LocalNowPlaying != null && !_localStopped;

    internal bool LocalPlaying => LocalPlayerActive && _localPlayer?.Playing == true;

    internal int LocalPositionMs => LocalPlayerActive ? (int)(_localPlayer?.Position.TotalMilliseconds ?? 0) : 0;

    internal int LocalDurationMs =>
        _localPlayer?.Duration is { TotalMilliseconds: > 0 } duration ? (int)duration.TotalMilliseconds : (int)(LocalNowPlaying?.Duration.TotalMilliseconds ?? 0);

    internal bool LocalShuffle => _localShuffle;
    internal int LocalRepeat => _localRepeat;
    internal int LocalVolume => _localVolume;
    internal bool LocalMuted => _localMuted;

    private ILocalPlayer? LocalPlayer
    {
        get
        {
            if (_localPlayer != null || _localPlayerFailed || !LocalPlayerSupported) return _localPlayer;
            _localPlayerFailed = true;
            try
            {
                if (CreateLocalPlayer() is { } player)
                {
                    player.Volume = _localVolume / 100.0;
                    player.Muted = _localMuted;
                    player.Ended += () => Dispatcher.UIThread.Post(LocalTrackEnded);
                    player.Failed += message => Dispatcher.UIThread.Post(() => LocalTrackFailed(message));
                    player.StateChanged += () => Dispatcher.UIThread.Post(NotifyLocalPlayer);
                    _localPlayer = player;
                    _localPlayerFailed = false;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Unexpected("Computer player", ex);
            }
            return _localPlayer;
        }
    }

    private static ILocalPlayer? CreateLocalPlayer()
    {
#if WINDOWS
        return new WindowsLocalPlayer();
#else
        if (OperatingSystem.IsLinux()) return new GStreamerLocalPlayer();
        if (OperatingSystem.IsMacOS()) return new MacLocalPlayer();
        return null;
#endif
    }

    private ComputerTrack LocalComputerTrack()
    {
        var status = LocalStatus();
        return new ComputerTrack(string.Join('\n', LocalIp, status.Title, status.Artist, status.Album), status.Title, status.Artist, status.Album,
            status.IsPlaying, status.PositionMs, status.DurationMs, DateTime.UtcNow, status.ArtUrl, null);
    }

    private void NotifyLocalPlayer()
    {
        LocalPlayerChanged?.Invoke();
        _ = ReadComputerPlaybackAsync();
        if (_miniPlayers.TryGetValue(LocalIp, out var mini)) mini.RefreshNow();
        if (LocalPlayerActive)
        {
            _localInSession = true;
            ShowInMediaSession(LocalStatus());
        }
        else if (_localInSession)
        {
            _localInSession = false;
            ShowInMediaSession(null);
            _ = RefreshNowPlayingAsync();
        }
    }

    private sealed record LocalTrackInfo(QueueTrack Track, string Title, string Artist, string Album, string ArtUrl, byte[]? Image);

    internal DeviceStatus LocalStatus()
    {
        var track = LocalNowPlaying;
        var info = InfoOf(track);
        string state = LocalPlaying ? "play" : LocalPlayerActive ? "pause" : "stop";
        string title = Pick(info?.Title, track?.Title), artist = Pick(info?.Artist, track?.Artist), album = Pick(info?.Album, track?.Album);
        return new DeviceStatus(new DeviceVolume(_localVolume, _localMuted), state, LocalMode, title, artist, album,
            LocalArtUrlOf(track, info, title, artist, album), 0, 0, LoopMode(_localShuffle, _localRepeat),
            LocalPositionMs, LocalPlayerActive ? LocalDurationMs : 0, SourceLabel: LocalName);
    }

    private static string Pick(string? preferred, string? fallback) => preferred is { Length: > 0 } ? preferred : fallback ?? string.Empty;

    private LocalTrackInfo? InfoOf(QueueTrack? track)
    {
        if (track == null) return null;
        lock (_localInfo) return _localInfo.FindLast(i => ReferenceEquals(i.Track, track));
    }

    private string LocalArtUrlOf(QueueTrack? track, LocalTrackInfo? info, string title, string artist, string album)
    {
        if (track == null) return string.Empty;
        if (track.ArtUrl.Length > 0) return track.ArtUrl;
        if (info == null) return string.Empty;
        if (info.ArtUrl.Length > 0) return info.ArtUrl;
        var lookup = new ComputerTrack(string.Empty, title.Trim(), artist.Trim(), album.Trim(), true, 0, 0, DateTime.UtcNow, string.Empty, null);
        if (!_onlineArtwork || lookup.Title.Length == 0 || lookup.Artist.Length == 0) return string.Empty;
        _localLookups.Add(OnlineArtworkKey(lookup));
        return OnlineArtworkFor(lookup) is { Length: > 0 } cover ? cover : string.Empty;
    }

    private void RememberLocalInfo(QueueTrack track, TrackTagInfo tags, bool tagsWin)
    {
        if (InfoOf(track) != null) return;
        string Choose(string tag, string current) => tagsWin ? Pick(tag, current) : Pick(current, tag);
        byte[]? image = track.ArtUrl.Length == 0 && tags.Picture is { Length: > 0 } picture ? picture : null;
        var info = new LocalTrackInfo(track, Choose(tags.Title, track.Title), Choose(tags.Artist, track.Artist), Choose(tags.Album, track.Album),
            image != null ? $"{LocalArtPrefix}{++_localArtNumber}" : string.Empty, image);
        lock (_localInfo)
        {
            _localInfo.Add(info);
            if (_localInfo.Count > LocalArtKept) _localInfo.RemoveAt(0);
        }
        if (ReferenceEquals(track, LocalNowPlaying)) NotifyLocalPlayer();
    }

    private async Task ReadWebTagsAsync(QueueTrack track)
    {
        if (InfoOf(track) != null) return;
        var tags = TrackTagInfo.Empty;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, track.Url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, LocalArtProbeBytes - 1);
            using var response = await HttpFor(track.Url).SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[LocalArtProbeBytes];
                int read = 0, chunk;
                while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read))) > 0) read += chunk;
                tags = await Task.Run(() => TrackTags.Read(buffer.AsSpan(0, read)));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UriFormatException) { }
        RememberLocalInfo(track, tags, tagsWin: false);
    }

    internal byte[]? LocalArtBytes(string url)
    {
        lock (_localInfo) return _localInfo.FindLast(i => i.ArtUrl == url)?.Image;
    }

    private void OnLocalLookupDone(string key)
    {
        if (_localLookups.Remove(key) && LocalNowPlaying != null) NotifyLocalPlayer();
    }

    private bool LocalTransport(string action)
    {
        switch (action)
        {
            case "onepause" or "playpause": LocalPlayPause(); break;
            case "resume" or "play": LocalPlay(); break;
            case "pause": LocalPause(); break;
            case "next": LocalNext(); break;
            case "prev": LocalPrevious(); break;
            case "stop": LocalStop(); break;
            default: return false;
        }
        return true;
    }

    private bool SetLocalLoop(int mode)
    {
        var (shuffle, repeat) = ParseLoop(mode);
        if (shuffle != _localShuffle) ToggleLocalShuffle();
        if (repeat != _localRepeat)
        {
            _localRepeat = repeat;
            SaveConfig();
            NotifyLocalPlayer();
        }
        return true;
    }

    private bool SetLocalVolumeFrom(int volume)
    {
        SetLocalVolume(volume);
        return true;
    }

    private bool SetLocalMutedFrom(bool muted)
    {
        if (muted != _localMuted) ToggleLocalMute();
        return true;
    }

    private bool SeekLocal(int seconds)
    {
        LocalSeek(seconds);
        return true;
    }

    internal bool PlayLocally(IReadOnlyList<QueueTrack> tracks, int start)
    {
        if (LocalPlayer == null || tracks.Count == 0) return false;
        _localOriginal.Clear();
        _localOriginal.AddRange(tracks);
        _localQueue.Clear();
        _localQueue.AddRange(tracks);
        _localIndex = Math.Clamp(start, 0, tracks.Count - 1);
        if (_localShuffle) ShuffleLocalQueue();
        _localFailures = 0;
        _ = LoadLocalTrackAsync();
        return true;
    }

    internal bool AddToLocalQueue(IReadOnlyList<QueueTrack> tracks, bool next)
    {
        if (tracks.Count == 0) return false;
        if (!LocalPlayerActive) return PlayLocally(tracks, 0);
        var current = LocalNowPlaying!;
        _localQueue.InsertRange(next ? _localIndex + 1 : _localQueue.Count, tracks);
        int original = _localOriginal.FindIndex(t => ReferenceEquals(t, current));
        _localOriginal.InsertRange(next && original >= 0 ? original + 1 : _localOriginal.Count, tracks);
        NotifyLocalPlayer();
        return true;
    }

    private void ShuffleLocalQueue()
    {
        if (LocalNowPlaying is not { } current) return;
        var rest = _localQueue.Where((_, i) => i != _localIndex).ToArray();
        Random.Shared.Shuffle(rest);
        _localQueue.Clear();
        _localQueue.Add(current);
        _localQueue.AddRange(rest);
        _localIndex = 0;
    }

    private async Task LoadLocalTrackAsync()
    {
        if (LocalPlayer is not { } player || LocalNowPlaying is not { } track) return;
        int load = ++_localLoad;
        _localStopped = false;
        NotifyLocalPlayer();
        try
        {
            if (IsShareUrl(track.Url))
            {
                var bytes = await ReadShareTrackAsync(track.Url);
                if (load != _localLoad) return;
                if (bytes == null)
                {
                    LocalTrackFailed("The shared folder didn't send the file");
                    return;
                }
                player.Open(new MemoryStream(bytes, writable: false), LocalContentType(track));
                if (InfoOf(track) == null) RememberLocalInfo(track, await Task.Run(() => TrackTags.Read(bytes)), tagsWin: true);
            }
            else
            {
                player.Open(new Uri(track.Url));
                if (track.ArtUrl.Length == 0 || track.Artist.Length == 0)
                {
                    if (track.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _ = ReadWebTagsAsync(track);
                    else RememberLocalInfo(track, TrackTagInfo.Empty, tagsWin: false);
                }
            }
            player.Play();
        }
        catch (Exception ex)
        {
            if (load == _localLoad) LocalTrackFailed(ex.Message);
        }
        NotifyLocalPlayer();
    }

    private static string LocalContentType(QueueTrack track)
    {
        var parts = track.ProtocolInfo.Split(':');
        if (parts.Length > 2 && parts[2].Contains('/')) return parts[2];
        string path = track.Url.Split('?')[0];
        int dot = path.LastIndexOf('.');
        return dot >= 0 && ShareAudioTypes.TryGetValue(path[dot..], out var type) ? type : "audio/mpeg";
    }

    private async Task<byte[]?> ReadShareTrackAsync(string url)
    {
        var raw = url[ShareScheme.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length < 3) return null;
        int at = raw[0].LastIndexOf('@');
        string host = raw[0][(at + 1)..];
        var parts = raw.Skip(1).Select(Uri.UnescapeDataString).ToArray();
        string user = string.Empty, password = string.Empty;
        if (at > 0)
        {
            string auth = raw[0][..at];
            int colon = auth.IndexOf(':');
            string login = colon >= 0 ? auth[..colon] : auth;
            if (colon >= 0) password = Uri.UnescapeDataString(auth[(colon + 1)..]);
            int semicolon = login.IndexOf(';');
            user = semicolon > 0
                ? Uri.UnescapeDataString(login[..semicolon]) + "\\" + Uri.UnescapeDataString(login[(semicolon + 1)..])
                : Uri.UnescapeDataString(login);
        }
        var folder = _sharedFolders.FirstOrDefault(f => f.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
                         f.Share.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                     ?? new SharedFolder(host, host, parts[0], string.Empty, user, password);
        var (bytes, _) = await WithShareAsync(folder, store => ReadFile(store, string.Join('\\', parts[1..]), MaxLocalTrackBytes),
            LocalShareTimeout).ConfigureAwait(false);
        return bytes;
    }

    private void LocalTrackEnded()
    {
        if (!LocalPlayerActive) return;
        _localFailures = 0;
        if (_localRepeat == 1) _ = LoadLocalTrackAsync();
        else LocalNext(byUser: false);
    }

    private void LocalTrackFailed(string message)
    {
        if (LocalNowPlaying is not { } track || _localStopped) return;
        LocalPlayerProblem?.Invoke($"Couldn't play {track.Title} on this computer: {message}");
        if (++_localFailures >= _localQueue.Count)
        {
            LocalStop();
            return;
        }
        LocalNext(byUser: false);
    }

    internal void LocalPlayPause()
    {
        if (LocalPlayer is not { } player || LocalNowPlaying == null) return;
        if (_localStopped)
        {
            _localFailures = 0;
            _ = LoadLocalTrackAsync();
            return;
        }
        if (player.Playing) player.Pause();
        else player.Play();
        NotifyLocalPlayer();
    }

    internal void LocalPlay()
    {
        if (_localStopped || _localPlayer?.Playing != true) LocalPlayPause();
    }

    internal void LocalPause()
    {
        if (LocalPlayerActive && _localPlayer?.Playing == true) LocalPlayPause();
    }

    internal void LocalNext() => LocalNext(byUser: true);

    private void LocalNext(bool byUser)
    {
        if (LocalNowPlaying == null) return;
        if (_localIndex + 1 < _localQueue.Count) _localIndex++;
        else if (_localRepeat == 2) _localIndex = 0;
        else
        {
            if (byUser) return;
            _localIndex = 0;
            LocalStop();
            return;
        }
        _ = LoadLocalTrackAsync();
    }

    internal void LocalPrevious()
    {
        if (LocalPlayer is not { } player || LocalNowPlaying == null) return;
        if (!_localStopped && player.Position.TotalMilliseconds > LocalRestartMs)
        {
            player.Seek(TimeSpan.Zero);
            NotifyLocalPlayer();
            return;
        }
        if (_localIndex > 0) _localIndex--;
        else if (_localRepeat == 2) _localIndex = _localQueue.Count - 1;
        _localFailures = 0;
        _ = LoadLocalTrackAsync();
    }

    internal void LocalStop()
    {
        _localLoad++;
        _localStopped = true;
        _localPlayer?.Stop();
        NotifyLocalPlayer();
    }

    internal void LocalSeek(int seconds)
    {
        if (!LocalPlayerActive) return;
        _localPlayer?.Seek(TimeSpan.FromSeconds(seconds));
        NotifyLocalPlayer();
    }

    internal void ToggleLocalShuffle()
    {
        _localShuffle = !_localShuffle;
        SaveConfig();
        if (LocalNowPlaying is { } current)
        {
            if (_localShuffle) ShuffleLocalQueue();
            else
            {
                _localQueue.Clear();
                _localQueue.AddRange(_localOriginal);
                _localIndex = Math.Max(0, _localQueue.FindIndex(t => ReferenceEquals(t, current)));
            }
        }
        NotifyLocalPlayer();
    }

    internal void CycleLocalRepeat()
    {
        _localRepeat = _localRepeat switch { 0 => 2, 2 => 1, _ => 0 };
        SaveConfig();
        NotifyLocalPlayer();
    }

    internal void SetLocalVolume(int volume)
    {
        _localVolume = Math.Clamp(volume, 0, 100);
        if (_localPlayer != null) _localPlayer.Volume = _localVolume / 100.0;
        SaveConfig();
        if (_miniPlayers.TryGetValue(LocalIp, out var mini)) mini.RefreshNow();
        if (LocalPlayerActive) ShowInMediaSession(LocalStatus());
    }

    internal void ToggleLocalMute()
    {
        _localMuted = !_localMuted;
        if (_localPlayer != null) _localPlayer.Muted = _localMuted;
        SaveConfig();
        NotifyLocalPlayer();
    }

    private void SendKeyMediaCommand(string cmd)
    {
        if (LocalPlayerActive && cmd is "playpause" or "next" or "prev" or "stop")
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (cmd)
                {
                    case "playpause": LocalPlayPause(); break;
                    case "next": LocalNext(); break;
                    case "prev": LocalPrevious(); break;
                    default: LocalStop(); break;
                }
            });
            return;
        }
        _ = SendMediaCommandAsync(cmd);
    }

    private void DisposeLocalPlayer()
    {
        _localPlayer?.Dispose();
        _localPlayer = null;
    }
}
