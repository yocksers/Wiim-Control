using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace WiimControl;

static class Lms
{
    public const int SourceMode = 34;
    private const int DiscoveryPort = 3483;
    private const string TrackClass = "object.item.audioItem.musicTrack";
    private const string AlbumClass = "object.container.album.musicAlbum";
    private const string FolderClass = "object.container";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly byte[] Probe = Encoding.ASCII.GetBytes("eIPAD\0NAME\0JSON\0UUID\0");

    private static readonly (string Id, string Title)[] RootFolders =
    [
        ("artists", "Artists"), ("albums", "Albums"), ("genres", "Genres"), ("years", "Years"),
        ("new", "New music"), ("folders", "Music folders"), ("playlists", "Playlists")
    ];

    public static async Task<List<MediaServer>> DiscoverAsync(TimeSpan timeout)
    {
        var found = new ConcurrentDictionary<string, MediaServer>();
        var (lan, other) = Upnp.LocalAddresses();
        var ignore = lan.Count > 0 ? other : [];
        await Task.WhenAll(lan.Concat(other).Select(a => ProbeAsync(a, [IPAddress.Broadcast, a], found, lan, ignore, timeout))).ConfigureAwait(false);
        return found.Values.ToList();
    }

    public static async Task<MediaServer?> ServerAtAsync(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return null;
        var found = new ConcurrentDictionary<string, MediaServer>();
        await ProbeAsync(IPAddress.Any, [address], found, Upnp.LocalAddresses().Lan, [], TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
        if (found.Values.FirstOrDefault() is { } server) return server;
        var guess = new MediaServer(ip, "lms:" + ip, $"http://{ip}:9000", string.Empty, IsLyrion: true);
        return await RequestAsync(guess, "", "serverstatus", "0", "0").ConfigureAwait(false) != null ? guess : null;
    }

    private static async Task ProbeAsync(IPAddress local, IPAddress[] targets, ConcurrentDictionary<string, MediaServer> found,
        List<IPAddress> lan, List<IPAddress> ignore, TimeSpan timeout)
    {
        UdpClient client;
        try
        {
            client = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
        }
        catch { return; }

        using (client)
        using (var cts = new CancellationTokenSource(timeout))
        {
            try
            {
                foreach (var target in targets)
                    await client.SendAsync(Probe, Probe.Length, new IPEndPoint(target, DiscoveryPort)).ConfigureAwait(false);
                while (true)
                {
                    var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (ignore.Contains(result.RemoteEndPoint.Address)) continue;
                    if (ParseReply(result.Buffer, result.RemoteEndPoint.Address, lan) is { } server) found[server.Udn] = server;
                }
            }
            catch { }
        }
    }

    private static MediaServer? ParseReply(byte[] data, IPAddress from, List<IPAddress> lan)
    {
        if (data.Length < 1 || data[0] != (byte)'E') return null;
        var fields = new Dictionary<string, string>();
        for (int i = 1; i + 5 <= data.Length;)
        {
            string tag = Encoding.ASCII.GetString(data, i, 4);
            int length = data[i + 4];
            if (i + 5 + length > data.Length) break;
            fields[tag] = Encoding.UTF8.GetString(data, i + 5, length);
            i += 5 + length;
        }
        string ip = from.ToString();
        string port = fields.TryGetValue("JSON", out var p) && int.TryParse(p, out _) ? p : "9000";
        string name = fields.TryGetValue("NAME", out var n) && n.Length > 0 ? n : ip;
        string id = fields.TryGetValue("UUID", out var u) && u.Length > 0 ? u : ip;
        return new MediaServer($"{name} (Lyrion)", "lms:" + id, $"http://{ip}:{port}", string.Empty, lan.Contains(from), IsLyrion: true);
    }

    public static async Task<JsonElement?> RequestAsync(MediaServer server, string player, params string[] command)
    {
        var body = JsonSerializer.Serialize(new { id = 1, method = "slim.request", @params = new object[] { player, command } });
        try
        {
            using var response = await Http.PostAsync(server.ContentDirectoryUrl + "/jsonrpc.js",
                new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<string?> FindPlayerAsync(MediaServer server, string ampIp, string mac)
    {
        var result = await RequestAsync(server, "", "players", "0", "99").ConfigureAwait(false);
        if (result is not { } r || !r.TryGetProperty("players_loop", out var players)) return null;
        foreach (var p in players.EnumerateArray())
        {
            string id = Str(p, "playerid"), ip = Str(p, "ip");
            if ((mac.Length > 0 && id.Equals(mac, StringComparison.OrdinalIgnoreCase)) || ip.Split(':')[0] == ampIp) return id;
        }
        return null;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString() : "";

    private static string Cover(MediaServer server, string id) => id.Length > 0 ? $"{server.ContentDirectoryUrl}/music/{id}/cover_100x100_o" : "";

    public static async Task<BrowsePage?> BrowseAsync(MediaServer server, string id, int start, int count)
    {
        if (id == "0")
            return new BrowsePage(RootFolders.Skip(start).Take(count)
                .Select(f => Container(f.Id, f.Title)).ToList(), RootFolders.Length);

        string s = start.ToString(CultureInfo.InvariantCulture), c = count.ToString(CultureInfo.InvariantCulture);
        string[] AlbumQuery(params string[] filter) => ["albums", s, c, "tags:laj", "sort:album", .. filter];
        string[] TrackQuery(params string[] filter) => ["titles", s, c, "tags:adlJ", "sort:tracknum", .. filter];
        var (kind, value) = id.IndexOf(':') is int i and > 0 ? (id[..i], id[(i + 1)..]) : (id, "");
        string[]? command = kind switch
        {
            "artists" => ["artists", s, c],
            "albums" => AlbumQuery(),
            "new" => ["albums", s, c, "tags:laj", "sort:new"],
            "genres" => ["genres", s, c],
            "years" => ["years", s, c],
            "folders" => ["musicfolder", s, c],
            "playlists" => ["playlists", s, c],
            "artist" => AlbumQuery("artist_id:" + value),
            "genre" => AlbumQuery("genre_id:" + value),
            "year" => AlbumQuery("year:" + value),
            "album" => TrackQuery("album_id:" + value),
            "folder" => ["musicfolder", s, c, "folder_id:" + value],
            "playlist" => ["playlists", "tracks", s, c, "tags:adlJ", "playlist_id:" + value],
            _ => null
        };
        if (command == null || await RequestAsync(server, "", command).ConfigureAwait(false) is not { } result) return null;

        int total = result.TryGetProperty("count", out var t) && t.TryGetInt32(out int n) ? n : 0;
        var entries = new List<MediaEntry>();
        IEnumerable<JsonElement> Loop(string name) =>
            result.TryGetProperty(name, out var loop) && loop.ValueKind == JsonValueKind.Array ? loop.EnumerateArray() : [];

        switch (kind)
        {
            case "artists":
                foreach (var a in Loop("artists_loop")) entries.Add(Container("artist:" + Str(a, "id"), Str(a, "artist")));
                break;
            case "albums" or "new" or "artist" or "genre" or "year":
                foreach (var a in Loop("albums_loop"))
                    entries.Add(Container("album:" + Str(a, "id"), Str(a, "album"), Str(a, "artist"), Cover(server, Str(a, "artwork_track_id")), AlbumClass));
                break;
            case "genres":
                foreach (var g in Loop("genres_loop")) entries.Add(Container("genre:" + Str(g, "id"), Str(g, "genre")));
                break;
            case "years":
                foreach (var y in Loop("years_loop"))
                    if (Str(y, "year") is { Length: > 0 } year && year != "0") entries.Add(Container("year:" + year, year));
                break;
            case "playlists":
                foreach (var p in Loop("playlists_loop")) entries.Add(Container("playlist:" + Str(p, "id"), Str(p, "playlist")));
                break;
            case "folders" or "folder":
                foreach (var f in Loop("folder_loop"))
                {
                    string type = Str(f, "type"), title = Str(f, "filename");
                    if (type == "folder") entries.Add(Container("folder:" + Str(f, "id"), title));
                    else if (type == "track") entries.Add(Track(server, Str(f, "id"), title, "", "", Str(f, "coverid"), 0));
                }
                break;
            case "album" or "playlist":
                foreach (var tr in Loop(kind == "album" ? "titles_loop" : "playlisttracks_loop"))
                    entries.Add(Track(server, Str(tr, "id"), Str(tr, "title"), Str(tr, "artist"), Str(tr, "album"), Str(tr, "artwork_track_id"),
                        double.TryParse(Str(tr, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0));
                break;
        }
        return new BrowsePage(entries, Math.Max(total, start + entries.Count));
    }

    private static MediaEntry Container(string id, string title, string artist = "", string art = "", string cls = FolderClass) =>
        new(id, title, true, null, cls, artist, "", art, "", "", TimeSpan.Zero);

    private static MediaEntry Track(MediaServer server, string id, string title, string artist, string album, string artId, double seconds) =>
        new("track:" + id, title, false, null, TrackClass, artist, album, Cover(server, artId),
            $"{server.ContentDirectoryUrl}/music/{id}/download", "", TimeSpan.FromSeconds(seconds));

    public static string? TrackId(MediaEntry entry) => entry.Id.StartsWith("track:", StringComparison.Ordinal) ? entry.Id[6..] : null;
}
