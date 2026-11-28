using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WiimControl;

sealed record SpotifyItem(string Kind, string Id, string Uri, string Name, string Subtitle, string ImageUrl, int DurationMs = 0, bool Explicit = false,
    string Album = "", string ArtistId = "", string ArtistName = "", string AlbumId = "", string OwnerId = "", bool Collaborative = false);

sealed record SpotifyPlayerState(bool Playing, bool Shuffle, string Repeat, string ContextUri, SpotifyItem? Item, string DeviceName);

sealed record SpotifyResults(List<SpotifyItem> Items, int Total);

sealed record SpotifyDetail(SpotifyItem Item, List<SpotifyItem> Tracks, List<SpotifyItem> Albums, bool TracksHidden, string Info);

sealed record SpotifyPlayback(string? ContextUri, IReadOnlyList<string>? Uris, string? StartUri);

sealed partial class WiimController
{
    internal const string SpotifyRedirectUri = "http://127.0.0.1:43821/callback";
    private const int SpotifyRedirectPort = 43821;
    internal const string SpotifyDashboardUrl = "https://developer.spotify.com/dashboard";
    private const string SpotifyApi = "https://api.spotify.com/v1";
    private const string SpotifyAccounts = "https://accounts.spotify.com";
    private const string SpotifyScopes = "user-read-private user-read-playback-state user-modify-playback-state user-read-currently-playing " +
                                         "playlist-read-private playlist-read-collaborative user-library-read user-library-modify " +
                                         "playlist-modify-private playlist-modify-public";
    private const string SpotifyNeededScope = "user-library-modify";
    private const string SpotifySecretId = "spotify";
    internal const int SpotifyPageSize = 10;
    private static readonly TimeSpan SpotifyLinkTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SpotifyAppStartWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SpotifyAppPoll = TimeSpan.FromSeconds(1.5);
    private static readonly HttpClient SpotifyHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private bool _spotifyEnabled;
    private string _spotifyClientId = string.Empty;
    private string _spotifyRefreshToken = string.Empty;
    private string _spotifyUser = string.Empty;
    private string _spotifyProduct = string.Empty;
    private string _spotifyUserId = string.Empty;
    private string _spotifyScopes = string.Empty;
    private string _spotifyAccessToken = string.Empty;
    private DateTime _spotifyTokenExpires;
    private readonly SemaphoreSlim _spotifyTokenGate = new(1, 1);

    internal bool SpotifyEnabled
    {
        get => _spotifyEnabled;
        set
        {
            _spotifyEnabled = value;
            SaveConfig();
            _settingsWindow?.OnSpotifyEnabledChanged();
        }
    }

    internal string SpotifyClientId
    {
        get => _spotifyClientId;
        set
        {
            if (value.Trim() == _spotifyClientId) return;
            _spotifyClientId = value.Trim();
            SaveConfig();
        }
    }

    internal bool SpotifyLinked => _spotifyRefreshToken.Length > 0;
    internal string SpotifyUser => _spotifyUser;
    internal bool SpotifyPremium => _spotifyProduct.Length == 0 || _spotifyProduct == "premium";
    internal bool SpotifyNeedsRelink => SpotifyLinked && !_spotifyScopes.Split(' ').Contains(SpotifyNeededScope);

    private string SaveSpotifyToken() => SecretStore.Protect(_spotifyRefreshToken, SpotifySecretId, "Wiim Control Spotify access");

    private void LoadSpotifyToken(string value) => _spotifyRefreshToken = SecretStore.IsSealed(value) ? SecretStore.Unprotect(value) : value;

    internal void UnlinkSpotify()
    {
        _spotifyRefreshToken = _spotifyAccessToken = _spotifyUser = _spotifyProduct = _spotifyUserId = _spotifyScopes = string.Empty;
        _spotifyTokenExpires = default;
        SecretStore.Forget(SpotifySecretId);
        SaveConfig();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string SpotifyQuery(params (string Name, string Value)[] fields) =>
        string.Join('&', fields.Select(f => $"{f.Name}={Uri.EscapeDataString(f.Value)}"));

    internal async Task<string?> LinkSpotifyAsync(CancellationToken cancel)
    {
        string clientId = _spotifyClientId;
        if (clientId.Length == 0) return "Enter your Client ID first.";
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var listener = new TcpListener(IPAddress.Loopback, SpotifyRedirectPort);
        try { listener.Start(); }
        catch (SocketException)
        {
            return $"Another program on this computer is using port {SpotifyRedirectPort}, which Spotify sends its answer to. Close it and try again.";
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(SpotifyLinkTimeout);
        try
        {
            OpenUrl($"{SpotifyAccounts}/authorize?" + SpotifyQuery(("client_id", clientId), ("response_type", "code"), ("redirect_uri", SpotifyRedirectUri),
                ("code_challenge_method", "S256"), ("code_challenge", challenge), ("scope", SpotifyScopes), ("state", state)));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                string? line;
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                    line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                string target = line?.Split(' ') is [_, var t, ..] ? t : string.Empty;
                if (!target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await AnswerBrowserAsync(stream, 404, string.Empty).ConfigureAwait(false);
                    continue;
                }
                var fields = ParseQueryText(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : string.Empty);
                if (fields.GetValueOrDefault("state") != state)
                {
                    await AnswerBrowserAsync(stream, 400, "This answer doesn't belong to the link request from Wiim Control. Start linking again.").ConfigureAwait(false);
                    continue;
                }
                string? problem = fields.TryGetValue("error", out var error)
                    ? error == "access_denied" ? "Linking was cancelled in Spotify." : $"Spotify answered: {error}"
                    : await RedeemSpotifyCodeAsync(clientId, fields.GetValueOrDefault("code") ?? string.Empty, verifier).ConfigureAwait(false);
                await AnswerBrowserAsync(stream, 200, problem == null
                    ? "Spotify is now linked to Wiim Control. You can close this tab."
                    : "Spotify wasn't linked: " + problem).ConfigureAwait(false);
                return problem;
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return "Spotify didn't answer in time. Try linking again.";
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Dictionary<string, string> ParseQueryText(string query)
    {
        var fields = new Dictionary<string, string>();
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = pair.IndexOf('=');
            string name = Uri.UnescapeDataString((at < 0 ? pair : pair[..at]).Replace('+', ' '));
            fields[name] = at < 0 ? string.Empty : Uri.UnescapeDataString(pair[(at + 1)..].Replace('+', ' '));
        }
        return fields;
    }

    private static async Task AnswerBrowserAsync(Stream stream, int status, string message)
    {
        string body = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Wiim Control</title></head>" +
                      "<body style=\"font-family:sans-serif;background:#181818;color:#fff;display:flex;align-items:center;justify-content:center;height:90vh\">" +
                      $"<p style=\"font-size:20px\">{WebUtility.HtmlEncode(message)}</p></body></html>";
        byte[] content = Encoding.UTF8.GetBytes(body);
        string reason = status switch { 200 => "OK", 404 => "Not Found", _ => "Bad Request" };
        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head).ConfigureAwait(false);
            await stream.WriteAsync(content).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException) { }
    }

    private async Task<string?> RedeemSpotifyCodeAsync(string clientId, string code, string verifier)
    {
        var (json, error, _) = await SpotifyTokenRequestAsync(("grant_type", "authorization_code"), ("code", code), ("redirect_uri", SpotifyRedirectUri),
            ("client_id", clientId), ("code_verifier", verifier)).ConfigureAwait(false);
        if (json is not { } token) return error;
        ApplySpotifyToken(token);
        var (_, me) = await SpotifyCallAsync(HttpMethod.Get, "/me").ConfigureAwait(false);
        if (me is { } profile)
        {
            _spotifyUser = SpotifyText(profile, "display_name") is { Length: > 0 } name ? name : SpotifyText(profile, "id");
            _spotifyProduct = SpotifyText(profile, "product");
            _spotifyUserId = SpotifyText(profile, "id");
        }
        SaveConfig();
        return null;
    }

    private static async Task<(JsonElement? Json, string Error, string Code)> SpotifyTokenRequestAsync(params (string Name, string Value)[] fields)
    {
        try
        {
            using var content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
            using var response = await SpotifyHttp.PostAsync($"{SpotifyAccounts}/api/token", content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (response.IsSuccessStatusCode) return (doc.RootElement.Clone(), string.Empty, string.Empty);
            string code = SpotifyText(doc.RootElement, "error");
            string description = SpotifyText(doc.RootElement, "error_description");
            return (null, description.Length > 0 ? description : code.Length > 0 ? code : $"error {(int)response.StatusCode}", code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, "Spotify couldn't be reached.", string.Empty);
        }
    }

    private void ApplySpotifyToken(JsonElement token)
    {
        _spotifyAccessToken = SpotifyText(token, "access_token");
        int seconds = token.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out int s) ? s : 3600;
        _spotifyTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
        if (SpotifyText(token, "scope") is { Length: > 0 } scopes && scopes != _spotifyScopes)
        {
            _spotifyScopes = scopes;
            SaveConfig();
        }
        string refresh = SpotifyText(token, "refresh_token");
        if (refresh.Length > 0 && refresh != _spotifyRefreshToken)
        {
            _spotifyRefreshToken = refresh;
            SaveConfig();
        }
    }

    private async Task<string?> SpotifyAccessTokenAsync(bool renew)
    {
        await _spotifyTokenGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!renew && _spotifyAccessToken.Length > 0 && DateTime.UtcNow < _spotifyTokenExpires) return _spotifyAccessToken;
            if (_spotifyRefreshToken.Length == 0 || _spotifyClientId.Length == 0) return null;
            var (json, _, code) = await SpotifyTokenRequestAsync(("grant_type", "refresh_token"), ("refresh_token", _spotifyRefreshToken),
                ("client_id", _spotifyClientId)).ConfigureAwait(false);
            if (json is { } token)
            {
                ApplySpotifyToken(token);
                return _spotifyAccessToken;
            }
            if (code is "invalid_grant" or "invalid_client") Avalonia.Threading.Dispatcher.UIThread.Post(UnlinkSpotify);
            return null;
        }
        finally
        {
            _spotifyTokenGate.Release();
        }
    }

    private async Task<(int Status, JsonElement? Json)> SpotifyCallAsync(HttpMethod method, string path, object? body = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (await SpotifyAccessTokenAsync(renew: attempt > 0).ConfigureAwait(false) is not { } token) return (401, null);
            using var request = new HttpRequestMessage(method, SpotifyApi + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            try
            {
                using var response = await SpotifyHttp.SendAsync(request).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status == 401 && attempt == 0) continue;
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (text.Length == 0) return (status, null);
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    return (status, doc.RootElement.Clone());
                }
                catch (JsonException) { return (status, null); }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (0, null);
            }
        }
        return (401, null);
    }

    private async Task<JsonElement?> SpotifyGetAsync(string path)
    {
        var (status, json) = await SpotifyCallAsync(HttpMethod.Get, path).ConfigureAwait(false);
        return status is >= 200 and < 300 ? json : null;
    }

    private static string SpotifyText(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static JsonElement SpotifyObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static IEnumerable<JsonElement> SpotifyArray(JsonElement element, string name) =>
        SpotifyObject(element, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static string SpotifyImage(JsonElement element)
    {
        var images = SpotifyArray(element, "images").ToList();
        if (images.Count == 0) return string.Empty;
        var fitting = images.Where(i => i.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.GetInt32() >= 200)
            .OrderBy(i => i.GetProperty("width").GetInt32()).FirstOrDefault();
        return SpotifyText(fitting.ValueKind == JsonValueKind.Object ? fitting : images[0], "url");
    }

    private static string SpotifyArtists(JsonElement element) =>
        string.Join(", ", SpotifyArray(element, "artists").Select(a => SpotifyText(a, "name")).Where(n => n.Length > 0));

    private static int SpotifyNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out int number) ? number : 0;

    internal static SpotifyItem? ParseSpotifyItem(JsonElement e, string? albumImage = null, string? albumName = null)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string kind = SpotifyText(e, "type"), id = SpotifyText(e, "id"), uri = SpotifyText(e, "uri"), name = SpotifyText(e, "name");
        if (id.Length == 0 || uri.Length == 0) return null;
        string year = SpotifyText(e, "release_date") is { Length: >= 4 } date ? date[..4] : string.Empty;
        return kind switch
        {
            "track" => new SpotifyItem(kind, id, uri, name, SpotifyArtists(e),
                albumImage ?? SpotifyImage(SpotifyObject(e, "album")), SpotifyNumber(e, "duration_ms"),
                SpotifyObject(e, "explicit").ValueKind == JsonValueKind.True, albumName ?? SpotifyText(SpotifyObject(e, "album"), "name"),
                SpotifyArray(e, "artists").Select(a => SpotifyText(a, "id")).FirstOrDefault() ?? string.Empty,
                SpotifyArray(e, "artists").Select(a => SpotifyText(a, "name")).FirstOrDefault() ?? string.Empty,
                SpotifyText(SpotifyObject(e, "album"), "id")),
            "artist" => new SpotifyItem(kind, id, uri, name, "Artist", SpotifyImage(e)),
            "album" => new SpotifyItem(kind, id, uri, name, string.Join(" • ", new[] { year, SpotifyArtists(e) }.Where(s => s.Length > 0)), SpotifyImage(e),
                ArtistId: SpotifyArray(e, "artists").Select(a => SpotifyText(a, "id")).FirstOrDefault() ?? string.Empty,
                ArtistName: SpotifyArray(e, "artists").Select(a => SpotifyText(a, "name")).FirstOrDefault() ?? string.Empty),
            "playlist" => new SpotifyItem(kind, id, uri, name, SpotifyText(SpotifyObject(e, "owner"), "display_name") is { Length: > 0 } owner ? $"By {owner}" : "Playlist",
                SpotifyImage(e), OwnerId: SpotifyText(SpotifyObject(e, "owner"), "id"),
                Collaborative: SpotifyObject(e, "collaborative").ValueKind == JsonValueKind.True),
            "show" => new SpotifyItem(kind, id, uri, name, SpotifyText(e, "publisher"), SpotifyImage(e)),
            "episode" => new SpotifyItem(kind, id, uri, name, SpotifyText(e, "release_date"), albumImage ?? SpotifyImage(e), SpotifyNumber(e, "duration_ms"),
                SpotifyObject(e, "explicit").ValueKind == JsonValueKind.True),
            _ => null
        };
    }

    internal async Task<Dictionary<string, SpotifyResults>?> SpotifySearchAsync(string query, IReadOnlyList<string> types, int offset, int limit)
    {
        string path = "/search?" + SpotifyQuery(("q", query), ("type", string.Join(',', types)),
            ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("offset", offset.ToString(CultureInfo.InvariantCulture)), ("market", "from_token"));
        if (await SpotifyGetAsync(path).ConfigureAwait(false) is not { } json) return null;
        var results = new Dictionary<string, SpotifyResults>();
        foreach (string type in types)
        {
            var group = SpotifyObject(json, type + "s");
            var items = SpotifyArray(group, "items").Select(i => ParseSpotifyItem(i)).OfType<SpotifyItem>().ToList();
            results[type] = new SpotifyResults(items, Math.Max(SpotifyNumber(group, "total"), offset + items.Count));
        }
        return results;
    }

    internal async Task<List<SpotifyItem>?> SpotifyMyPlaylistsAsync()
    {
        if (await SpotifyGetAsync("/me/playlists?limit=50").ConfigureAwait(false) is not { } json) return null;
        return SpotifyArray(json, "items").Select(i => ParseSpotifyItem(i)).OfType<SpotifyItem>().ToList();
    }

    internal async Task<SpotifyDetail?> SpotifyDetailAsync(SpotifyItem item) => item.Kind switch
    {
        "album" => await SpotifyAlbumAsync(item.Id).ConfigureAwait(false),
        "artist" => await SpotifyArtistAsync(item).ConfigureAwait(false),
        "playlist" => await SpotifyPlaylistAsync(item.Id).ConfigureAwait(false),
        "show" => await SpotifyShowAsync(item.Id).ConfigureAwait(false),
        _ => null
    };

    private async Task<SpotifyDetail?> SpotifyAlbumAsync(string id)
    {
        if (await SpotifyGetAsync($"/albums/{Uri.EscapeDataString(id)}?market=from_token").ConfigureAwait(false) is not { } json) return null;
        if (ParseSpotifyItem(json) is not { } album) return null;
        var tracks = SpotifyArray(SpotifyObject(json, "tracks"), "items").Select(t => ParseSpotifyItem(t, album.ImageUrl, album.Name)).OfType<SpotifyItem>().ToList();
        string year = SpotifyText(json, "release_date") is { Length: >= 4 } date ? date[..4] : string.Empty;
        string info = string.Join(" • ", new[] { SpotifyArtists(json), year, tracks.Count == 1 ? "1 song" : $"{tracks.Count} songs" }.Where(s => s.Length > 0));
        return new SpotifyDetail(album, tracks, [], false, info);
    }

    private async Task<SpotifyDetail?> SpotifyArtistAsync(SpotifyItem artist)
    {
        var info = SpotifyGetAsync($"/artists/{Uri.EscapeDataString(artist.Id)}");
        var popular = SpotifySearchAsync($"artist:\"{artist.Name}\"", ["track"], 0, SpotifyPageSize);
        var albums = SpotifyGetAsync($"/artists/{Uri.EscapeDataString(artist.Id)}/albums?include_groups=album,single&market=from_token&limit={SpotifyPageSize}");
        await Task.WhenAll(info, popular, albums).ConfigureAwait(false);
        var item = info.Result is { } json ? ParseSpotifyItem(json) ?? artist : artist;
        var tracks = popular.Result?.GetValueOrDefault("track")?.Items.Where(t => t.ArtistId == artist.Id).ToList() ?? [];
        var albumList = albums.Result is { } a ? SpotifyArray(a, "items").Select(i => ParseSpotifyItem(i)).OfType<SpotifyItem>().ToList() : [];
        if (info.Result == null && popular.Result == null && albums.Result == null) return null;
        return new SpotifyDetail(item, tracks, albumList, false, "Artist");
    }

    private async Task<SpotifyDetail?> SpotifyPlaylistAsync(string id)
    {
        if (await SpotifyGetAsync($"/playlists/{Uri.EscapeDataString(id)}?market=from_token").ConfigureAwait(false) is not { } json) return null;
        if (ParseSpotifyItem(json) is not { } playlist) return null;
        var holder = SpotifyObject(json, "items") is { ValueKind: JsonValueKind.Object } items ? items : SpotifyObject(json, "tracks");
        bool listed = SpotifyObject(holder, "items").ValueKind == JsonValueKind.Array;
        var tracks = SpotifyArray(holder, "items")
            .Select(entry => SpotifyObject(entry, "item") is { ValueKind: JsonValueKind.Object } inner ? inner : SpotifyObject(entry, "track"))
            .Select(t => ParseSpotifyItem(t)).OfType<SpotifyItem>().ToList();
        int total = SpotifyNumber(holder, "total");
        string info = string.Join(" • ", new[] { playlist.Subtitle, total > 0 ? (total == 1 ? "1 song" : $"{total} songs") : string.Empty }.Where(s => s.Length > 0));
        return new SpotifyDetail(playlist, tracks, [], !listed, info);
    }

    private async Task<SpotifyDetail?> SpotifyShowAsync(string id)
    {
        if (await SpotifyGetAsync($"/shows/{Uri.EscapeDataString(id)}?market=from_token").ConfigureAwait(false) is not { } json) return null;
        if (ParseSpotifyItem(json) is not { } show) return null;
        var episodes = SpotifyArray(SpotifyObject(json, "episodes"), "items").Select(e => ParseSpotifyItem(e, show.ImageUrl)).OfType<SpotifyItem>().ToList();
        return new SpotifyDetail(show, episodes, [], false, show.Subtitle);
    }

    internal static string? MatchSpotifyDevice(IEnumerable<(string Id, string Name)> devices, string name)
    {
        var list = devices.ToList();
        string Clean(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return list.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Id
               ?? list.FirstOrDefault(d => Clean(d.Name) == Clean(name)).Id;
    }

    internal static string? ThisComputerDevice(IEnumerable<(string Id, string Name, string Type)> devices, string machine)
    {
        var computers = devices.Where(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase)).ToList();
        return MatchSpotifyDevice(computers.Select(d => (d.Id, d.Name)), machine) ?? (computers.Count == 1 ? computers[0].Id : null);
    }

    private async Task<(List<(string Id, string Name, string Type)>? Devices, string Problem)> SpotifyDevicesAsync()
    {
        var (status, json) = await SpotifyCallAsync(HttpMethod.Get, "/me/player/devices").ConfigureAwait(false);
        if (status == 401) return (null, "Spotify isn't linked any more. Link your account again on this page.");
        if (json is not { } list || status is < 200 or >= 300) return (null, "Spotify couldn't be reached.");
        return (SpotifyArray(list, "devices").Select(d => (Id: SpotifyText(d, "id"), Name: SpotifyText(d, "name"), Type: SpotifyText(d, "type")))
            .Where(d => d.Id.Length > 0).ToList(), string.Empty);
    }

    internal async Task<string?> SpotifyPlayAsync(string deviceName, SpotifyPlayback playback)
    {
        var (devices, problem) = await SpotifyDevicesAsync().ConfigureAwait(false);
        if (devices == null) return problem;
        if (MatchSpotifyDevice(devices.Select(d => (d.Id, d.Name)), deviceName) is not { } deviceId)
            return $"{deviceName} isn't in your Spotify devices right now. Open Spotify on your phone or computer, play something on {deviceName} " +
                   "with Spotify Connect once, then try again.";
        return await SpotifyStartAsync(deviceId, deviceName, playback).ConfigureAwait(false);
    }

    internal async Task<string?> SpotifyPlayHereAsync(SpotifyPlayback playback)
    {
        var (devices, problem) = await SpotifyDevicesAsync().ConfigureAwait(false);
        if (devices == null) return problem;
        string? deviceId = ThisComputerDevice(devices, Environment.MachineName);
        if (deviceId == null)
        {
            OpenUrl("spotify:");
            var until = DateTime.UtcNow + SpotifyAppStartWait;
            while (deviceId == null && DateTime.UtcNow < until)
            {
                await Task.Delay(SpotifyAppPoll).ConfigureAwait(false);
                if ((await SpotifyDevicesAsync().ConfigureAwait(false)).Devices is { } now) deviceId = ThisComputerDevice(now, Environment.MachineName);
            }
        }
        if (deviceId == null)
            return "The Spotify app isn't running on this computer. Start it, sign in with the same Spotify account, then try again.";
        string? started = await SpotifyStartAsync(deviceId, "this computer", playback).ConfigureAwait(false);
        if (started == null) return null;
        await Task.Delay(SpotifyAppPoll).ConfigureAwait(false);
        return await SpotifyStartAsync(deviceId, "this computer", playback).ConfigureAwait(false);
    }

    private async Task<string?> SpotifyStartAsync(string deviceId, string deviceName, SpotifyPlayback playback)
    {
        var body = new Dictionary<string, object>();
        if (playback.ContextUri != null) body["context_uri"] = playback.ContextUri;
        if (playback.Uris != null) body["uris"] = playback.Uris;
        if (playback.StartUri != null) body["offset"] = new Dictionary<string, string> { ["uri"] = playback.StartUri };
        var (played, reply) = await SpotifyCallAsync(HttpMethod.Put, $"/me/player/play?device_id={Uri.EscapeDataString(deviceId)}", body).ConfigureAwait(false);
        if (played is >= 200 and < 300) return null;
        var error = reply is { } r ? SpotifyObject(r, "error") : default;
        string reason = SpotifyText(error, "reason"), message = SpotifyText(error, "message");
        if (reason == "PREMIUM_REQUIRED" || (played == 403 && !SpotifyPremium))
            return "Spotify only lets Premium accounts start playback from other programs.";
        if (played == 404) return $"Spotify couldn't reach {deviceName}. Play something on it from the Spotify app once, then try again.";
        return played == 0 ? "Spotify couldn't be reached." : $"Spotify didn't start playing{(message.Length > 0 ? ": " + message : ".")}";
    }
}
