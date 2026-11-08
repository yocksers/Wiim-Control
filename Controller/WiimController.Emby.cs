using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace WiimControl;

sealed record EmbyServer(string Name, string Url, string Product, string ServerId, string UserId, string UserName, string Token, string Password = "");

sealed record EmbyServerInfo(string Url, string Product, string ServerName, string ServerId);

sealed record EmbySignIn(EmbyServer? Server, string? Problem);

sealed partial class WiimController
{
    private const string EmbyRootId = "0";
    private const string EmbyAlbumsId = "albums";
    private const string EmbyArtistsId = "artists";
    private const string EmbyPlaylistsId = "playlists";
    private const string EmbyFavoritesId = "favorites";
    private const string EmbyFoldersId = "folders";
    private const string EmbyAlbumPrefix = "album:";
    private const string EmbyArtistPrefix = "artist:";
    private const string EmbyPlaylistPrefix = "playlist:";
    private const string EmbyFolderPrefix = "folder:";
    private const int EmbyArtSize = 600;
    private const string EmbyFields = "ChildCount,Container,AlbumArtist,Artists,Album,AlbumId,AlbumPrimaryImageTag,ImageTags,RunTimeTicks";

    private static readonly Dictionary<string, string> EmbyDirectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"] = "audio/mpeg", ["flac"] = "audio/flac", ["m4a"] = "audio/mp4", ["mp4"] = "audio/mp4", ["aac"] = "audio/aac",
        ["alac"] = "audio/mp4", ["wav"] = "audio/wav", ["aiff"] = "audio/aiff", ["aif"] = "audio/aiff", ["ape"] = "audio/ape",
        ["wma"] = "audio/x-ms-wma"
    };

    private List<EmbyServer> _embyServers = [];
    private string _embyDeviceId = string.Empty;

    internal IEnumerable<MediaServer> EmbyServers => _embyServers.Select(s => new MediaServer(s.Name, EmbyUdn(s), s.Url, string.Empty, IsEmbyJellyfin: true));

    private static string EmbyUdn(EmbyServer s) => $"emby:{s.ServerId}:{s.UserId}".ToLowerInvariant();

    private EmbyServer? EmbyOf(MediaServer server) => _embyServers.FirstOrDefault(s => EmbyUdn(s) == server.Udn);

    internal string EmbyProductOf(string udn) => _embyServers.FirstOrDefault(s => EmbyUdn(s) == udn)?.Product ?? "Emby or Jellyfin";

    internal string? EmbyUserOf(MediaServer server) => EmbyOf(server)?.UserName;

    private string EmbyDeviceId
    {
        get
        {
            if (_embyDeviceId.Length > 0) return _embyDeviceId;
            _embyDeviceId = Guid.NewGuid().ToString("N");
            SaveConfig();
            return _embyDeviceId;
        }
    }

    private string EmbyAuthorization(string? token)
    {
        static string Clean(string value) => value.Replace("\"", string.Empty).Replace(",", " ");
        string value = $"MediaBrowser Client=\"Wiim Control\", Device=\"{Clean(Environment.MachineName)}\", DeviceId=\"{EmbyDeviceId}\", " +
                       $"Version=\"{AppVersion}\"";
        return token is { Length: > 0 } ? value + $", Token=\"{token}\"" : value;
    }

    private HttpRequestMessage EmbyRequest(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        string authorization = EmbyAuthorization(token);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", authorization);
        if (token is { Length: > 0 }) request.Headers.TryAddWithoutValidation("X-Emby-Token", token);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private async Task<(string? Body, HttpStatusCode? Status)> EmbySendAsync(HttpRequestMessage request)
    {
        try
        {
            using var response = await MusicServerHttpFor(request.RequestUri!.ToString()).SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, response.StatusCode);
            return (await response.Content.ReadAsStringAsync().ConfigureAwait(false), response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { return (null, null); }
        finally { request.Dispose(); }
    }

    internal static IEnumerable<string> EmbyAddressCandidates(string address)
    {
        string text = address.Trim().TrimEnd('/');
        if (text.Length == 0) yield break;
        bool schemeGiven = text.Contains("://", StringComparison.Ordinal);
        if (!schemeGiven) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) yield break;
        string clean = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        yield return clean;
        bool portGiven = uri.Authority.Contains(':') && !uri.Authority.EndsWith(']');
        if (!portGiven && uri.AbsolutePath is "/" or "")
            yield return $"{uri.Scheme}://{uri.Host}:{(uri.Scheme == "https" ? 8920 : 8096)}";
    }

    internal async Task<EmbyServerInfo?> GetEmbyServerInfoAsync(string address)
    {
        foreach (string url in EmbyAddressCandidates(address))
        {
            var (body, _) = await EmbySendAsync(EmbyRequest(HttpMethod.Get, url + "/System/Info/Public", null)).ConfigureAwait(false);
            if (body == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || EmbyText(root, "Id") is not { Length: > 0 } id) continue;
                string product = (EmbyText(root, "ProductName") ?? string.Empty).Contains("Jellyfin", StringComparison.OrdinalIgnoreCase) ? "Jellyfin" : "Emby";
                return new EmbyServerInfo(url, product, EmbyText(root, "ServerName") ?? product, id);
            }
            catch (JsonException) { }
        }
        return null;
    }

    internal async Task<EmbySignIn> SignInEmbyAsync(EmbyServerInfo info, string user, string password, string name)
    {
        var request = EmbyRequest(HttpMethod.Post, info.Url + "/Users/AuthenticateByName", null);
        request.Content = new StringContent(JsonSerializer.Serialize(new { Username = user, Pw = password }), Encoding.UTF8, "application/json");
        var (body, status) = await EmbySendAsync(request).ConfigureAwait(false);
        if (body == null)
            return new EmbySignIn(null, status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "The user name or password is wrong."
                : $"{info.ServerName} didn't answer the sign-in. Check the address and that the server is running.");
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? token = EmbyText(root, "AccessToken");
            string? userId = root.TryGetProperty("User", out var u) && u.ValueKind == JsonValueKind.Object ? EmbyText(u, "Id") : null;
            if (token is not { Length: > 0 } || userId is not { Length: > 0 }) return new EmbySignIn(null, $"{info.ServerName} didn't accept the sign-in.");
            return new EmbySignIn(new EmbyServer(name, info.Url, info.Product, info.ServerId, userId, user, token, password), null);
        }
        catch (JsonException) { return new EmbySignIn(null, $"{info.ServerName} didn't accept the sign-in."); }
    }

    internal MediaServer AddEmbyServer(EmbyServer server)
    {
        _embyServers.RemoveAll(s => EmbyUdn(s) == EmbyUdn(server));
        _embyServers.Add(server);
        SaveConfig();
        return EmbyServers.First(s => s.Udn == EmbyUdn(server));
    }

    private void ReplaceEmbySignIn(MediaServer server, string token, string password)
    {
        int index = _embyServers.FindIndex(s => EmbyUdn(s) == server.Udn);
        if (index < 0) return;
        _embyServers[index] = _embyServers[index] with { Token = token, Password = password };
        SaveConfig();
    }

    internal async Task RemoveEmbyServerAsync(MediaServer server)
    {
        if (EmbyOf(server) is { } s)
            await EmbySendAsync(EmbyRequest(HttpMethod.Post, s.Url + "/Sessions/Logout", s.Token)).ConfigureAwait(false);
        _embyServers.RemoveAll(e => EmbyUdn(e) == server.Udn);
        SecretStore.Forget(server.Udn);
        SecretStore.Forget(EmbyPasswordId(server.Udn));
        SaveConfig();
    }

    internal async Task<bool> EmbySignInExpiredAsync(MediaServer server)
    {
        if (EmbyOf(server) is not { } s) return false;
        var (_, status) = await EmbySendAsync(EmbyRequest(HttpMethod.Get, $"{s.Url}/Users/{s.UserId}", s.Token)).ConfigureAwait(false);
        return status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    internal async Task<bool> EmbyServerAnswersAsync(MediaServer server) =>
        EmbyOf(server) is { } s &&
        (await EmbySendAsync(EmbyRequest(HttpMethod.Get, $"{s.Url}/Users/{Uri.EscapeDataString(s.UserId)}", s.Token)).ConfigureAwait(false)).Body != null;

    internal async Task<string?> RenewEmbySignInAsync(MediaServer server, string password)
    {
        if (EmbyOf(server) is not { } s) return "This server isn't in the list any more.";
        var result = await SignInEmbyAsync(new EmbyServerInfo(s.Url, s.Product, s.Name, s.ServerId), s.UserName, password, s.Name).ConfigureAwait(false);
        if (result.Server is not { } renewed) return result.Problem;
        ReplaceEmbySignIn(server, renewed.Token, password);
        return null;
    }

    internal async Task<bool> RenewSavedEmbySignInAsync(MediaServer server) =>
        EmbyOf(server) is { Password.Length: > 0 } s && await RenewEmbySignInAsync(server, s.Password).ConfigureAwait(false) == null;

    private static string EmbyPasswordId(string udn) => udn + ":password";

    internal async Task<BrowsePage?> BrowseEmbyAsync(MediaServer server, string id, int start, int count)
    {
        if (EmbyOf(server) is not { } s) return null;
        if (id == EmbyRootId)
        {
            if (!await EmbyServerAnswersAsync(server).ConfigureAwait(false)) return null;
            var root = new List<MediaEntry>
            {
                EmbyFolder(EmbyAlbumsId, "Albums"), EmbyFolder(EmbyArtistsId, "Artists"), EmbyFolder(EmbyPlaylistsId, "Playlists"),
                EmbyFolder(EmbyFavoritesId, "Favourite tracks"), EmbyFolder(EmbyFoldersId, "Folders")
            };
            return new BrowsePage(root.Skip(start).Take(count).ToList(), root.Count);
        }
        string user = Uri.EscapeDataString(s.UserId);
        string Tail(string prefix) => Uri.EscapeDataString(id[prefix.Length..]);
        string[] queries = id switch
        {
            EmbyAlbumsId => [$"/Users/{user}/Items?IncludeItemTypes=MusicAlbum&Recursive=true&SortBy=SortName"],
            EmbyArtistsId => [$"/Artists/AlbumArtists?UserId={user}&SortBy=SortName"],
            EmbyPlaylistsId => [$"/Users/{user}/Items?IncludeItemTypes=Playlist&Recursive=true&SortBy=SortName"],
            EmbyFavoritesId => [$"/Users/{user}/Items?IncludeItemTypes=Audio&Recursive=true&Filters=IsFavorite&SortBy=AlbumArtist,Album,ParentIndexNumber,IndexNumber,SortName"],
            EmbyFoldersId => [$"/Users/{user}/Views"],
            _ when id.StartsWith(EmbyArtistPrefix, StringComparison.Ordinal) =>
            [
                $"/Users/{user}/Items?IncludeItemTypes=MusicAlbum&Recursive=true&ArtistIds={Tail(EmbyArtistPrefix)}&SortBy=ProductionYear,SortName",
                $"/Users/{user}/Items?ParentId={Tail(EmbyArtistPrefix)}&SortBy=ProductionYear,SortName",
                $"/Users/{user}/Items?IncludeItemTypes=Audio&Recursive=true&ArtistIds={Tail(EmbyArtistPrefix)}&SortBy=Album,ParentIndexNumber,IndexNumber,SortName"
            ],
            _ when id.StartsWith(EmbyAlbumPrefix, StringComparison.Ordinal) =>
                [$"/Users/{user}/Items?ParentId={Tail(EmbyAlbumPrefix)}&SortBy=ParentIndexNumber,IndexNumber,SortName"],
            _ when id.StartsWith(EmbyPlaylistPrefix, StringComparison.Ordinal) => [$"/Users/{user}/Items?ParentId={Tail(EmbyPlaylistPrefix)}"],
            _ when id.StartsWith(EmbyFolderPrefix, StringComparison.Ordinal) => [$"/Users/{user}/Items?ParentId={Tail(EmbyFolderPrefix)}&SortBy=SortName"],
            _ => []
        };
        BrowsePage? page = null;
        foreach (string query in queries)
        {
            page = await FetchEmbyPageAsync(s, id, query, start, count).ConfigureAwait(false);
            if (page is not { Total: 0 }) return page;
        }
        return page;
    }

    private async Task<BrowsePage?> FetchEmbyPageAsync(EmbyServer s, string id, string query, int start, int count)
    {
        string paging = string.Create(CultureInfo.InvariantCulture, $"StartIndex={start}&Limit={count}&Fields={EmbyFields}&EnableImageTypes=Primary&ImageTypeLimit=1");
        var (body, _) = await EmbySendAsync(EmbyRequest(HttpMethod.Get, s.Url + query + (query.Contains('?') ? "&" : "?") + paging, s.Token)).ConfigureAwait(false);
        if (body == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            var entries = items.EnumerateArray()
                .Where(item => id != EmbyFoldersId || EmbyText(item, "CollectionType") is null or "music" or "playlists" or "folders")
                .Select(item => EmbyEntry(s, item)).OfType<MediaEntry>().ToList();
            if (id.StartsWith(EmbyFolderPrefix, StringComparison.Ordinal)) entries = entries.OrderBy(e => e.IsContainer ? 0 : 1).ToList();
            bool paged = id != EmbyFoldersId;
            int total = root.TryGetProperty("TotalRecordCount", out var t) && t.TryGetInt32(out int n) ? n : entries.Count;
            return paged ? new BrowsePage(entries, Math.Max(total, start + entries.Count)) : new BrowsePage(entries.Skip(start).Take(count).ToList(), entries.Count);
        }
        catch (JsonException) { return null; }
    }

    private static MediaEntry EmbyFolder(string id, string title) =>
        new(id, title, true, null, "object.container", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, TimeSpan.Zero);

    private static string? EmbyText(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private MediaEntry? EmbyEntry(EmbyServer s, JsonElement item)
    {
        if (EmbyText(item, "Id") is not { Length: > 0 } id) return null;
        string type = EmbyText(item, "Type") ?? string.Empty;
        string title = EmbyText(item, "Name") ?? string.Empty;
        string art = EmbyArtUrl(s, item, id);
        if (type == "Audio")
        {
            string artist = EmbyText(item, "AlbumArtist") is { Length: > 0 } albumArtist ? albumArtist
                : item.TryGetProperty("Artists", out var artists) && artists.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", artists.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()))
                    : string.Empty;
            var duration = item.TryGetProperty("RunTimeTicks", out var ticks) && ticks.TryGetInt64(out long value) ? TimeSpan.FromTicks(value) : TimeSpan.Zero;
            var (url, mime) = EmbyStream(s, id, EmbyText(item, "Container"));
            return new MediaEntry(id, title, false, null, "object.item.audioItem.musicTrack", artist, EmbyText(item, "Album") ?? string.Empty, art, url,
                $"http-get:*:{mime}:*", duration);
        }
        bool isFolder = item.TryGetProperty("IsFolder", out var folder) && folder.ValueKind == JsonValueKind.True;
        if (!isFolder && type is not ("MusicAlbum" or "MusicArtist" or "Playlist")) return null;
        var (prefix, cls) = type switch
        {
            "MusicAlbum" => (EmbyAlbumPrefix, "object.container.album.musicAlbum"),
            "MusicArtist" => (EmbyArtistPrefix, "object.container.person.musicArtist"),
            "Playlist" => (EmbyPlaylistPrefix, "object.container.playlistContainer"),
            _ => (EmbyFolderPrefix, "object.container.storageFolder")
        };
        int? children = item.TryGetProperty("ChildCount", out var c) && c.TryGetInt32(out int count) ? count : null;
        return new MediaEntry(prefix + id, title, true, type == "MusicArtist" ? null : children, cls,
            type == "MusicAlbum" ? EmbyText(item, "AlbumArtist") ?? string.Empty : string.Empty, string.Empty, art, string.Empty, string.Empty, TimeSpan.Zero);
    }

    private static string EmbyArtUrl(EmbyServer s, JsonElement item, string id)
    {
        if (item.TryGetProperty("ImageTags", out var tags) && tags.ValueKind == JsonValueKind.Object && EmbyText(tags, "Primary") is { Length: > 0 } tag)
            return string.Create(CultureInfo.InvariantCulture, $"{s.Url}/Items/{id}/Images/Primary?maxHeight={EmbyArtSize}&tag={Uri.EscapeDataString(tag)}");
        if (EmbyText(item, "AlbumId") is { Length: > 0 } album && EmbyText(item, "AlbumPrimaryImageTag") is { Length: > 0 } albumTag)
            return string.Create(CultureInfo.InvariantCulture, $"{s.Url}/Items/{album}/Images/Primary?maxHeight={EmbyArtSize}&tag={Uri.EscapeDataString(albumTag)}");
        return string.Empty;
    }

    internal (string Url, string Mime) EmbyStream(EmbyServer s, string id, string? container)
    {
        string token = Uri.EscapeDataString(s.Token), device = Uri.EscapeDataString(EmbyDeviceId), item = Uri.EscapeDataString(id);
        string? direct = (container ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(EmbyDirectTypes.ContainsKey);
        return direct != null
            ? ($"{s.Url}/Audio/{item}/stream?static=true&DeviceId={device}&api_key={token}", EmbyDirectTypes[direct])
            : ($"{s.Url}/Audio/{item}/stream.flac?AudioCodec=flac&DeviceId={device}&api_key={token}", "audio/flac");
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _embyFormats = new();

    private string EmbyFormatOf(string trackUri)
    {
        foreach (var s in _embyServers.ToList())
        {
            if (EmbyTrackOf(trackUri, s.Url) is not var (id, converted)) continue;
            if (converted) return "FLAC";
            string key = $"{s.ServerId}/{id}";
            if (_embyFormats.TryGetValue(key, out var format)) return format;
            _embyFormats[key] = string.Empty;
            _ = LookUpEmbyFormatAsync(s, id, key);
            return string.Empty;
        }
        return string.Empty;
    }

    internal static (string Id, bool Converted)? EmbyTrackOf(string trackUri, string serverUrl)
    {
        string prefix = serverUrl + "/Audio/";
        if (!trackUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string[] path = trackUri[prefix.Length..].Split('?')[0].Split('/');
        if (path.Length != 2 || path[0].Length == 0 || !path[1].StartsWith("stream", StringComparison.OrdinalIgnoreCase)) return null;
        return (Uri.UnescapeDataString(path[0]), !path[1].Equals("stream", StringComparison.OrdinalIgnoreCase));
    }

    private async Task LookUpEmbyFormatAsync(EmbyServer s, string id, string key)
    {
        string query = $"{s.Url}/Users/{Uri.EscapeDataString(s.UserId)}/Items?Ids={Uri.EscapeDataString(id)}&Fields=MediaStreams,Container";
        var (body, _) = await EmbySendAsync(EmbyRequest(HttpMethod.Get, query, s.Token)).ConfigureAwait(false);
        if (body == null)
        {
            _embyFormats.TryRemove(key, out _);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array) return;
            var item = items.EnumerateArray().FirstOrDefault();
            string? codec = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("MediaStreams", out var streams) && streams.ValueKind == JsonValueKind.Array
                ? streams.EnumerateArray().Where(m => EmbyText(m, "Type") == "Audio").Select(m => EmbyText(m, "Codec")).FirstOrDefault()
                : null;
            _embyFormats[key] = EmbyFormatName(codec, EmbyText(item, "Container"));
        }
        catch (JsonException) { }
    }

    internal static string EmbyFormatName(string? codec, string? container)
    {
        string c = (codec ?? string.Empty).Trim().ToLowerInvariant();
        string box = (container ?? string.Empty).Split(',')[0].Trim().ToLowerInvariant();
        if (c.StartsWith("pcm", StringComparison.Ordinal)) return box switch { "wav" => "WAV", "aiff" or "aif" => "AIFF", _ => "PCM" };
        if (c.StartsWith("dsd", StringComparison.Ordinal)) return "DSD";
        if (c.StartsWith("wma", StringComparison.Ordinal)) return "WMA";
        return c switch
        {
            "flac" => "FLAC", "mp3" => "MP3", "aac" => "AAC", "alac" => "ALAC", "ape" => "APE", "opus" => "Opus", "vorbis" => "Vorbis",
            "wavpack" => "WavPack", "mp2" => "MP2", "ac3" => "AC-3", "eac3" => "E-AC-3", "dts" => "DTS",
            "" => box.ToUpperInvariant(),
            _ => c.ToUpperInvariant()
        };
    }

    private string SerializeEmbyServers() =>
        JsonSerializer.Serialize(_embyServers.Select(s => s with
        {
            Token = SecretStore.Protect(s.Token, EmbyUdn(s), "Wiim Control Emby/Jellyfin access"),
            Password = SecretStore.Protect(s.Password, EmbyPasswordId(EmbyUdn(s)), "Wiim Control Emby/Jellyfin password")
        }).ToList());

    internal static List<EmbyServer> ParseEmbyServers(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EmbyServer?>>(value)?
                .OfType<EmbyServer>()
                .Where(s => s.Url is { Length: > 0 } && s.ServerId is { Length: > 0 } && s.UserId is { Length: > 0 })
                .Select(s => s with
                {
                    Name = s.Name ?? s.Url, Product = s.Product ?? "Emby", UserName = s.UserName ?? string.Empty,
                    Token = SecretStore.IsSealed(s.Token ?? string.Empty) ? SecretStore.Unprotect(s.Token!) : s.Token ?? string.Empty,
                    Password = SecretStore.IsSealed(s.Password ?? string.Empty) ? SecretStore.Unprotect(s.Password!) : string.Empty
                })
                .ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }
}
