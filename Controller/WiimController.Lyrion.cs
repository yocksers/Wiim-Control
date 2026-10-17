using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace WiimControl;

enum LyrionPlayResult { Played, NotConnected, Failed }

sealed partial class WiimController
{
    private readonly ConcurrentDictionary<string, (MediaServer Server, string Player)> _lyrionLinks = new();

    internal async Task<SqueezeliteState?> GetSqueezeliteStateAsync(string ip)
    {
        var json = await SendToDeviceAsync(ip, "Squeezelite:getState").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string Str(string name) => root.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
            var found = root.TryGetProperty("discover_list", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(e => e.ToString()).Where(s => s.Length > 0).Distinct().ToList()
                : [];
            return new SqueezeliteState(Str("state"), Str("connected_server"), Str("default_server"), found, Str("auto_connect") == "1");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal async Task<bool> SetSqueezeliteAutoConnectAsync(string ip, bool enabled) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"Squeezelite:autoConnectEnable:{(enabled ? 1 : 0)}").ConfigureAwait(false));

    internal async Task<bool> ConnectSqueezeliteAsync(string ip, string server)
    {
        _lyrionLinks.TryRemove(ip, out _);
        return CommandSucceeded(await SendToDeviceAsync(ip, $"Squeezelite:connectServer:{server}").ConfigureAwait(false));
    }

    internal async Task<bool> DiscoverSqueezeliteAsync(string ip) =>
        CommandSucceeded(await SendToDeviceAsync(ip, "Squeezelite:discover").ConfigureAwait(false));

    private async Task<(MediaServer Server, string Player)?> GetLyrionLinkAsync(string ip, MediaServer? server = null)
    {
        if (_lyrionLinks.TryGetValue(ip, out var cached) && (server == null || cached.Server.Udn == server.Udn)) return cached;
        if (server == null)
        {
            var state = await GetSqueezeliteStateAsync(ip).ConfigureAwait(false);
            if (state?.ConnectedServer is not { Length: > 0 } connected) return null;
            server = await Lms.ServerAtAsync(connected.Split(':')[0]).ConfigureAwait(false);
            if (server == null) return null;
        }
        string mac = JsonField(await SendToDeviceAsync(ip, "getStatusEx").ConfigureAwait(false), "MAC") ?? string.Empty;
        if (await Lms.FindPlayerAsync(server, ip, mac).ConfigureAwait(false) is not { } player) return null;
        _lyrionLinks[ip] = (server, player);
        return (server, player);
    }

    private async Task<bool> TryLyrionCommandAsync(string ip, string action, int seconds = 0)
    {
        int source = _lastModes.TryGetValue(ip, out var known) && DateTime.UtcNow - known.Fetched < TimeSpan.FromSeconds(5)
            ? known.Mode
            : int.TryParse(JsonField(await SendToDeviceAsync(ip, "getPlayerStatus").ConfigureAwait(false), "mode"), out int m) ? m : -1;
        if (source != Lms.SourceMode) return false;
        if (await GetLyrionLinkAsync(ip).ConfigureAwait(false) is not { } link) return false;

        string[]? command = action switch
        {
            "onepause" or "playpause" => ["pause"],
            "resume" or "play" => ["pause", "0"],
            "pause" => ["pause", "1"],
            "stop" => ["stop"],
            "next" => ["playlist", "index", "+1"],
            "prev" => ["playlist", "index", "-1"],
            "seek" => ["time", Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture)],
            _ => null
        };
        if (command == null) return false;
        if (command[0] == "pause" && command.Length < 2 || command is ["pause", "0"])
        {
            var status = await Lms.RequestAsync(link.Server, link.Player, "mode", "?").ConfigureAwait(false);
            if (status is { } s && s.TryGetProperty("_mode", out var mode) && mode.GetString() == "stop") command = ["play"];
        }
        if (await Lms.RequestAsync(link.Server, link.Player, command).ConfigureAwait(false) != null) return true;
        _lyrionLinks.TryRemove(ip, out _);
        return false;
    }

    internal async Task<LyrionPlayResult> PlayOnLyrionAsync(string ip, MediaServer server, IReadOnlyList<string> trackIds, int start)
    {
        if (trackIds.Count == 0) return LyrionPlayResult.Failed;
        if (await GetLyrionLinkAsync(ip, server).ConfigureAwait(false) is not { } link) return LyrionPlayResult.NotConnected;
        start = Math.Clamp(start, 0, trackIds.Count - 1);
        string index = start.ToString(CultureInfo.InvariantCulture);
        if (await Lms.RequestAsync(server, link.Player, "playlistcontrol", "cmd:load", "track_id:" + string.Join(',', trackIds),
                "play_index:" + index).ConfigureAwait(false) == null)
        {
            _lyrionLinks.TryRemove(ip, out _);
            return LyrionPlayResult.Failed;
        }
        if (start > 0)
        {
            await Task.Delay(800).ConfigureAwait(false);
            var status = await Lms.RequestAsync(server, link.Player, "status", "-", "1").ConfigureAwait(false);
            if (status is { } s && s.TryGetProperty("playlist_cur_index", out var current) && current.ToString() != index)
                await Lms.RequestAsync(server, link.Player, "playlist", "index", index).ConfigureAwait(false);
        }
        return LyrionPlayResult.Played;
    }

    internal async Task<bool> WaitForLyrionPlayerAsync(string ip, MediaServer server)
    {
        for (int i = 0; i < 10; i++)
        {
            if (await GetLyrionLinkAsync(ip, server).ConfigureAwait(false) != null) return true;
            await Task.Delay(1000).ConfigureAwait(false);
        }
        return false;
    }
}
