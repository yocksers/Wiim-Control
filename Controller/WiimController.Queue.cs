using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace WiimControl;

enum QueueKind { None, Amp, Lyrion, Roon }

sealed record QueueEntry(string Title, string Artist, string Album, string ArtUrl, TimeSpan Duration);

sealed record QueueView(QueueKind Kind, string Name, List<QueueEntry> Entries, int Current, string Signature, List<string>? ItemIds = null)
{
    public static readonly QueueView Empty = new(QueueKind.None, string.Empty, [], -1, string.Empty);
}

sealed record QueueState(QueueKind Kind, int Current, string Signature);

sealed partial class WiimController
{
    private const string CurrentQueue = "CurrentQueue";
    private static readonly int[] QueuelessModes = [1, 2, 3, 31, 32, 36, 99];

    private static bool HasQueue(int mode) => !QueuelessModes.Contains(mode) && mode is < 40 or > 60;

    private async Task<int> ModeOfAsync(string ip) =>
        int.TryParse(JsonField(await SendToDeviceAsync(ip, "getPlayerStatus").ConfigureAwait(false), "mode"), out int mode) ? mode : -1;

    private async Task<XElement?> QueueCallAsync(string ip, string action, params (string, string)[] args) =>
        await Upnp.CallAsync(await GetPlayQueueUrlAsync(ip).ConfigureAwait(false), PlayQueueService, action, args).ConfigureAwait(false);

    private static string Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;

    internal async Task<QueueState> GetQueueStateAsync(string ip)
    {
        var state = await ReadQueueStateAsync(ip).ConfigureAwait(false);
        if (state.Kind != QueueKind.None) return state;
        await Task.Delay(400).ConfigureAwait(false);
        return await ReadQueueStateAsync(ip).ConfigureAwait(false);
    }

    private async Task<QueueState> ReadQueueStateAsync(string ip)
    {
        int mode = await ModeOfAsync(ip).ConfigureAwait(false);
        if (mode == RoonMode && await RoonQueueStateAsync(ip).ConfigureAwait(false) is { } roonState) return roonState;
        if (mode == Lms.SourceMode && await GetLyrionLinkAsync(ip).ConfigureAwait(false) is { } link)
        {
            var status = await Lms.RequestAsync(link.Server, link.Player, "status", "-", "0").ConfigureAwait(false);
            if (status is not { } s) return new QueueState(QueueKind.None, -1, string.Empty);
            int current = s.TryGetProperty("playlist_cur_index", out var c) && int.TryParse(c.ToString(), out int i) ? i : -1;
            string stamp = s.TryGetProperty("playlist_timestamp", out var t) ? t.ToString() : string.Empty;
            string count = s.TryGetProperty("playlist_tracks", out var n) ? n.ToString() : "0";
            return new QueueState(QueueKind.Lyrion, current, $"lyrion|{stamp}|{count}");
        }
        if (mode < 0 || !HasQueue(mode)) return new QueueState(QueueKind.None, -1, $"none|{mode}");
        var index = await QueueCallAsync(ip, "GetQueueIndex", ("QueueName", CurrentQueue)).ConfigureAwait(false);
        if (index == null || !int.TryParse(Child(index, "TrackNums"), out int tracks) || tracks == 0)
            return new QueueState(QueueKind.None, -1, $"none|{mode}");
        int.TryParse(Child(index, "CurrentIndex"), out int position);
        return new QueueState(QueueKind.Amp, position - 1, $"amp|{tracks}");
    }

    internal async Task<QueueView> GetQueueAsync(string ip)
    {
        var state = await GetQueueStateAsync(ip).ConfigureAwait(false);
        return state.Kind switch
        {
            QueueKind.Lyrion => await GetLyrionQueueAsync(ip, state).ConfigureAwait(false),
            QueueKind.Roon => await GetRoonQueueAsync(ip).ConfigureAwait(false),
            QueueKind.Amp => await GetAmpQueueAsync(ip, state).ConfigureAwait(false),
            _ => QueueView.Empty with { Signature = state.Signature }
        };
    }

    private async Task<QueueView> GetAmpQueueAsync(string ip, QueueState state)
    {
        var response = await QueueCallAsync(ip, "BrowseQueue", ("QueueName", CurrentQueue)).ConfigureAwait(false);
        string context = Child(response, "QueueContext");
        try
        {
            var list = XDocument.Parse(context).Root;
            if (list == null) return QueueView.Empty;
            string name = list.Element("ListName")?.Value ?? CurrentQueue;
            var entries = new List<QueueEntry>();
            foreach (var track in list.Element("Tracks")?.Elements() ?? [])
            {
                var item = Didl.Parse(track.Element("Metadata")?.Value ?? string.Empty)?.FirstOrDefault();
                string url = track.Element("URL")?.Value ?? string.Empty;
                entries.Add(item != null
                    ? new QueueEntry(item.Title.Length > 0 ? item.Title : url, item.Artist, item.Album, item.ArtUrl, item.Duration)
                    : new QueueEntry(url, string.Empty, string.Empty, string.Empty, TimeSpan.Zero));
            }
            return new QueueView(QueueKind.Amp, name, entries, state.Current, state.Signature);
        }
        catch (Exception)
        {
            return QueueView.Empty;
        }
    }

    private async Task<QueueView> GetLyrionQueueAsync(string ip, QueueState state)
    {
        if (await GetLyrionLinkAsync(ip).ConfigureAwait(false) is not { } link) return QueueView.Empty;
        var status = await Lms.RequestAsync(link.Server, link.Player, "status", "0", MaxQueueTracks.ToString(CultureInfo.InvariantCulture), "tags:adlJc")
            .ConfigureAwait(false);
        if (status is not { } s) return QueueView.Empty;
        var entries = new List<QueueEntry>();
        if (s.TryGetProperty("playlist_loop", out var loop) && loop.ValueKind == JsonValueKind.Array)
            foreach (var t in loop.EnumerateArray())
            {
                string Str(string name) => t.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
                string art = Str("artwork_track_id") is { Length: > 0 } a ? a : Str("coverid");
                double seconds = double.TryParse(Str("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
                entries.Add(new QueueEntry(Str("title"), Str("artist"), Str("album"),
                    art.Length > 0 ? $"{link.Server.ContentDirectoryUrl}/music/{art}/cover_100x100_o" : string.Empty, TimeSpan.FromSeconds(seconds)));
            }
        return new QueueView(QueueKind.Lyrion, link.Server.Name, entries, state.Current, state.Signature);
    }

    private async Task<bool> LyrionAsync(string ip, params string[] command) =>
        await GetLyrionLinkAsync(ip).ConfigureAwait(false) is { } link &&
        await Lms.RequestAsync(link.Server, link.Player, command).ConfigureAwait(false) != null;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal async Task<bool> QueueJumpAsync(string ip, QueueView view, int index) => view.Kind switch
    {
        QueueKind.Lyrion => await LyrionAsync(ip, "playlist", "index", Number(index)).ConfigureAwait(false),
        QueueKind.Roon => await RoonPlayFromHereAsync(ip, view, index).ConfigureAwait(false),
        QueueKind.Amp => await QueueCallAsync(ip, "PlayQueueWithIndex", ("QueueName", view.Name), ("Index", Number(index + 1))).ConfigureAwait(false) != null,
        _ => false
    };

    internal async Task<bool> QueueRemoveAsync(string ip, QueueView view, int from, int to) => view.Kind switch
    {
        QueueKind.Lyrion => await RemoveLyrionRangeAsync(ip, from, to).ConfigureAwait(false),
        QueueKind.Amp => await RemoveAmpRangeAsync(ip, view.Name, from, to).ConfigureAwait(false),
        _ => false
    };

    private async Task<bool> RemoveAmpRangeAsync(string ip, string name, int from, int to)
    {
        for (int i = to; i >= from; i--)
            if (await QueueCallAsync(ip, "RemoveTracksInQueue", ("QueueName", name), ("RangStart", Number(i + 1)),
                    ("RangEnd", Number(i + 1)), ("Action", "")).ConfigureAwait(false) == null) return false;
        return true;
    }

    private async Task<bool> RemoveLyrionRangeAsync(string ip, int from, int to)
    {
        for (int i = to; i >= from; i--)
            if (!await LyrionAsync(ip, "playlist", "delete", Number(i)).ConfigureAwait(false)) return false;
        return true;
    }

    internal async Task<bool> QueueMoveAsync(string ip, QueueView view, int from, int to) => view.Kind switch
    {
        QueueKind.Lyrion => await LyrionAsync(ip, "playlist", "move", Number(from), Number(to)).ConfigureAwait(false),
        QueueKind.Amp => await QueueCallAsync(ip, "MoveTracksInQueue", ("QueueName", view.Name), ("IndexList", Number(from + 1)),
            ("ToIndex", Number(to + 1))).ConfigureAwait(false) != null,
        _ => false
    };

    internal async Task<bool?> AddToQueueAsync(string ip, MediaServer server, IReadOnlyList<MediaEntry> tracks, bool next)
    {
        if (tracks.Count == 0) return false;
        var state = await GetQueueStateAsync(ip).ConfigureAwait(false);
        if (server.IsLyrion)
        {
            if (state.Kind != QueueKind.Lyrion || await GetLyrionLinkAsync(ip, server).ConfigureAwait(false) is not { } link) return null;
            string ids = string.Join(',', tracks.Select(Lms.TrackId).OfType<string>());
            return await Lms.RequestAsync(server, link.Player, "playlistcontrol", next ? "cmd:insert" : "cmd:add", "track_id:" + ids).ConfigureAwait(false) != null;
        }
        if (state.Kind != QueueKind.Amp) return null;
        var view = await GetAmpQueueAsync(ip, state).ConfigureAwait(false);
        var queue = tracks.Take(MaxQueueTracks).Select(QueueTrackOf).ToList();
        string context = Didl.QueueContext(view.Name.Length > 0 ? view.Name : "WiimControl", queue, 0, radio: false,
            server.IsShare ? ShareSourceName : "UPnPServer");
        return await QueueCallAsync(ip, "AppendTracksInQueueEx", ("QueueContext", context), ("Direction", "1"),
            ("StartIndex", next ? Number(Math.Max(1, state.Current + 1)) : "0"), ("Play", "0"), ("Action", "")).ConfigureAwait(false) != null;
    }
}
