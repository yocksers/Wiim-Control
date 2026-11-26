using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const string ComputerArtPrefix = "computer-art:";
    private readonly DispatcherTimer _computerTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private IComputerPlayback? _computerPlayback;
    private ComputerTrack? _computerTrack;
    private string _computerTrackArtUrl = string.Empty;
    private string _computerArtUrl = string.Empty;
    private byte[]? _computerArt;
    private int _computerArtNumber;
    private bool _computerReading;
    private const int MinComputerArtPixels = 300;
    private const int GoodComputerArtPixels = 640;
    private byte[]? _checkedArt;
    private int _checkedArtPixels;
    private string? _computerDefaultOutput;
    private string? _computerProblem;

    internal string? ComputerPlaybackProblem => _computerProblem;

    private void StartComputerPlayback()
    {
        _computerTimer.Tick += async (_, _) => await ReadComputerPlaybackAsync();
        _computerTimer.Start();
    }

    private bool ComputerSoundGoesToWiim => _outputMonitor?.IsEnabledOutputActive ?? true;

    private static bool IsCableInput(string? input) => input is not null and not "wifi" and not "udisk";

    private void OnComputerOutputChanged()
    {
        _ = ReadComputerPlaybackAsync();
        _settingsWindow?.OnComputerPlaybackChanged();
    }

    private void StopComputerPlayback()
    {
        _computerTimer.Stop();
        _computerPlayback?.Dispose();
        _computerPlayback = null;
    }

    private static IComputerPlayback? CreateComputerPlayback()
    {
#if WINDOWS
        return new WindowsComputerPlayback();
#else
        if (OperatingSystem.IsLinux()) return new LinuxComputerPlayback();
        if (OperatingSystem.IsMacOS()) return new MacComputerPlayback();
        return null;
#endif
    }

    private async Task ReadComputerPlaybackAsync()
    {
        if (_computerReading) return;
        if (_settingsWindow == null && _miniPlayers.Count == 0)
        {
            SetComputerTrack(null);
            return;
        }
        if (LocalPlaying)
        {
            SetComputerTrack(LocalComputerTrack());
            return;
        }
        if ((_computerPlayback ??= CreateComputerPlayback()) is not { } reader) return;
        _computerReading = true;
        ComputerTrack? track;
        string? defaultOutput = _computerDefaultOutput;
        try
        {
            if (OperatingSystem.IsWindows()) defaultOutput = await Task.Run(ComputerOutputs.DefaultId);
            track = await Task.Run(reader.ReadAsync);
        }
        catch (Exception) { track = null; }
        finally { _computerReading = false; }
        if (reader.Problem != _computerProblem)
        {
            _computerProblem = reader.Problem;
            _settingsWindow?.OnComputerPlaybackProblem();
        }
        bool outputChanged = !string.Equals(defaultOutput, _computerDefaultOutput, StringComparison.OrdinalIgnoreCase);
        _computerDefaultOutput = defaultOutput;
        if (LocalPlaying || LocalPlayerActive && (track == null || !track.Playing && ShowsLocalTrack)) track = LocalComputerTrack();
        SetComputerTrack(track);
        if (outputChanged) _settingsWindow?.OnComputerPlaybackChanged();
    }

    private void SetComputerTrack(ComputerTrack? track)
    {
        var old = _computerTrack;
        string artUrl = track == null ? string.Empty : ComputerArtUrlFor(track);
        bool changed = (old == null) != (track == null) || old != null && track != null &&
            (old.Key != track.Key || old.Playing != track.Playing || old.DurationMs != track.DurationMs || artUrl != _computerTrackArtUrl ||
             Math.Abs(track.PositionMs - ExpectedPosition(old, track.PositionAt)) > 2000);
        _computerTrack = track;
        _computerTrackArtUrl = artUrl;
        if (changed) _settingsWindow?.OnComputerPlaybackChanged();
    }

    private int ArtPixels(byte[] art)
    {
        if (ReferenceEquals(art, _checkedArt)) return _checkedArtPixels;
        int pixels;
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(art));
            pixels = codec == null ? 0 : Math.Min(codec.Info.Width, codec.Info.Height);
        }
        catch (Exception) { pixels = 0; }
        _checkedArt = art;
        _checkedArtPixels = pixels;
        return pixels;
    }

    private string ComputerArtUrlFor(ComputerTrack track)
    {
        if (track.ArtUrl.Length > 0) return track.ArtUrl;
        int pixels = track.Art is { } own ? ArtPixels(own) : 0;
        if (pixels < GoodComputerArtPixels && OnlineArtworkFor(track) is { } online) return online;
        if (track.Art is not { } art || pixels < MinComputerArtPixels) return string.Empty;
        if (!ReferenceEquals(art, _computerArt))
        {
            _computerArt = art;
            _computerArtUrl = $"{ComputerArtPrefix}{++_computerArtNumber}";
        }
        return _computerArtUrl;
    }

    private static int ExpectedPosition(ComputerTrack track, DateTime at) =>
        track.PositionMs + (track.Playing ? (int)Math.Max(0, (at - track.PositionAt).TotalMilliseconds) : 0);

    private bool ShowsComputerPlayback(int mode) => _computerTrack != null && InputFromComputer(mode);

    private bool ControlsComputerPlayback(string ip) =>
        _computerTrack != null && (_pcAudioIp == ip ||
            _lastInputs.TryGetValue(ip, out var input) && IsCableInput(input) && ComputerSoundGoesToWiim && (input != "bluetooth" || IsBluetoothFromThisComputer(ip)));

    internal bool PlaysFromComputer(int mode) => ShowsComputerPlayback(mode);

    internal bool InputFromComputer(int mode) => IsCableInput(InputOfMode(mode)) && ComputerSoundGoesToWiim;

    internal string? ComputerSoundSource => _computerDefaultOutput;

    private bool ShowsLocalTrack => LocalPlayerActive && _computerTrack?.Key.StartsWith(LocalIp + '\n', StringComparison.Ordinal) == true;

    private async Task<bool> SeekComputerPlaybackAsync(int seconds)
    {
        if (ShowsLocalTrack)
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LocalSeek(Math.Max(0, seconds));
                _ = ReadComputerPlaybackAsync();
                return true;
            });
        if (_computerPlayback is not { } reader) return false;
        bool ok = await Task.Run(() => reader.SeekAsync(Math.Max(0, seconds) * 1000)).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(ReadComputerPlaybackAsync);
        return ok;
    }

    private async Task<bool> ControlComputerPlaybackAsync(string action)
    {
        if (ShowsLocalTrack)
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                bool done = LocalTransport(action);
                _ = ReadComputerPlaybackAsync();
                return done;
            });
        if (_computerPlayback is not { } reader) return false;
        bool ok = await Task.Run(() => reader.ControlAsync(action)).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(ReadComputerPlaybackAsync);
        return ok;
    }

    internal DeviceStatus WithComputerPlayback(DeviceStatus status)
    {
        if (!(status.StreamFromComputer || ShowsComputerPlayback(status.Mode)) || _computerTrack is not { } track) return status;
        if (InputOfMode(status.Mode) == "bluetooth" && !ThisComputer.Names.Contains(status.BluetoothName, StringComparer.OrdinalIgnoreCase)) return status;
        return status with
        {
            PlayState = track.Playing ? "play" : "pause", Title = track.Title, Artist = track.Artist, Album = track.Album,
            ArtUrl = _computerTrackArtUrl, PositionMs = track.PositionMs, DurationMs = track.DurationMs, PositionAt = track.PositionAt
        };
    }

    private async Task<byte[]> ArtworkBytesAsync(string url)
    {
        if (url.StartsWith(ComputerArtPrefix, StringComparison.Ordinal))
            return url == _computerArtUrl && _computerArt is { } art ? art : throw new InvalidOperationException("The computer's artwork has changed.");
        if (url.StartsWith(LocalArtPrefix, StringComparison.Ordinal))
            return LocalArtBytes(url) ?? throw new InvalidOperationException("The track's artwork has changed.");
        if (IsShareUrl(url))
            return await ReadShareFileAsync(url).ConfigureAwait(false) ?? throw new IOException("The shared folder didn't send the cover.");
        var bytes = await HttpFor(url).GetByteArrayAsync(url).ConfigureAwait(false);
        return LooksLikeJson(bytes) ? await StationArtworkBytesAsync(url, bytes).ConfigureAwait(false) : bytes;
    }
}
