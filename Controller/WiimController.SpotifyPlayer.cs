using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private const int SpotifyQueueMost = 100;

    internal static string SpotifyLink(SpotifyItem item) => $"https://open.spotify.com/{item.Kind}/{item.Id}";

    internal async Task<SpotifyPlayerState?> SpotifyPlayerAsync()
    {
        var (status, json) = await SpotifyCallAsync(HttpMethod.Get, "/me/player?additional_types=track,episode").ConfigureAwait(false);
        if (status is < 200 or >= 300) return null;
        if (status == 204 || json is not { ValueKind: JsonValueKind.Object } player) return new SpotifyPlayerState(false, false, "off", string.Empty, null, string.Empty);
        return new SpotifyPlayerState(
            SpotifyObject(player, "is_playing").ValueKind == JsonValueKind.True,
            SpotifyObject(player, "shuffle_state").ValueKind == JsonValueKind.True,
            SpotifyText(player, "repeat_state") is { Length: > 0 } repeat ? repeat : "off",
            SpotifyText(SpotifyObject(player, "context"), "uri"),
            ParseSpotifyItem(SpotifyObject(player, "item")),
            SpotifyText(SpotifyObject(player, "device"), "name"));
    }

    private async Task<string?> SpotifyCommandAsync(HttpMethod method, string path, object? body = null)
    {
        var (status, reply) = await SpotifyCallAsync(method, path, body).ConfigureAwait(false);
        return status is >= 200 and < 300 ? null : SpotifyProblem(status, reply);
    }

    private static string SpotifyProblem(int status, JsonElement? reply)
    {
        var error = reply is { } r ? SpotifyObject(r, "error") : default;
        string reason = SpotifyText(error, "reason"), message = SpotifyText(error, "message");
        if (status == 0) return "Spotify couldn't be reached.";
        if (status == 401) return "Spotify isn't linked any more. Link your account again on the Spotify page.";
        if (reason == "PREMIUM_REQUIRED") return "Spotify only lets Premium accounts control playback from other programs.";
        if (reason == "NO_ACTIVE_DEVICE" || (status == 404 && message.Contains("device", StringComparison.OrdinalIgnoreCase)))
            return "Nothing is playing on Spotify right now. Start something first.";
        if (status == 403 && message.Contains("scope", StringComparison.OrdinalIgnoreCase))
            return "Wiim Control needs more permissions from Spotify for this. Unlink Spotify on the Services page, then link it again on the Spotify page.";
        return $"Spotify didn't do it{(message.Length > 0 ? ": " + message : ".")}";
    }

    internal Task<string?> SpotifyPauseAsync() => SpotifyCommandAsync(HttpMethod.Put, "/me/player/pause");

    internal Task<string?> SpotifyResumeAsync() => SpotifyCommandAsync(HttpMethod.Put, "/me/player/play");

    internal Task<string?> SpotifyNextAsync() => SpotifyCommandAsync(HttpMethod.Post, "/me/player/next");

    internal Task<string?> SpotifyPreviousAsync() => SpotifyCommandAsync(HttpMethod.Post, "/me/player/previous");

    internal Task<string?> SpotifyShuffleAsync(bool on) => SpotifyCommandAsync(HttpMethod.Put, $"/me/player/shuffle?state={(on ? "true" : "false")}");

    internal Task<string?> SpotifyRepeatAsync(string mode) => SpotifyCommandAsync(HttpMethod.Put, $"/me/player/repeat?state={Uri.EscapeDataString(mode)}");

    internal async Task<string?> SpotifyQueueAsync(IEnumerable<string> uris)
    {
        foreach (string uri in uris.Take(SpotifyQueueMost))
            if (await SpotifyCommandAsync(HttpMethod.Post, "/me/player/queue?uri=" + Uri.EscapeDataString(uri)).ConfigureAwait(false) is { } problem)
                return problem;
        return null;
    }

    internal async Task<bool?> SpotifySavedAsync(string uri)
    {
        var (status, json) = await SpotifyCallAsync(HttpMethod.Get, "/me/library/contains?uris=" + Uri.EscapeDataString(uri)).ConfigureAwait(false);
        if (status is < 200 or >= 300 || json is not { ValueKind: JsonValueKind.Array } answer || answer.GetArrayLength() == 0) return null;
        return answer[0].ValueKind == JsonValueKind.True;
    }

    internal Task<string?> SpotifySaveAsync(string uri, bool save) =>
        SpotifyCommandAsync(save ? HttpMethod.Put : HttpMethod.Delete, "/me/library?uris=" + Uri.EscapeDataString(uri));

    internal Task<string?> SpotifyAddToPlaylistAsync(string playlistId, IReadOnlyList<string> uris) =>
        SpotifyCommandAsync(HttpMethod.Post, $"/playlists/{Uri.EscapeDataString(playlistId)}/items",
            new Dictionary<string, object> { ["uris"] = uris.Take(SpotifyQueueMost).ToList() });

    internal async Task<List<SpotifyItem>?> SpotifyEditablePlaylistsAsync()
    {
        if (_spotifyUserId.Length == 0 && await SpotifyGetAsync("/me").ConfigureAwait(false) is { } me)
        {
            _spotifyUserId = SpotifyText(me, "id");
            if (_spotifyUserId.Length > 0) SaveConfig();
        }
        var playlists = await SpotifyMyPlaylistsAsync().ConfigureAwait(false);
        return playlists?.Where(p => p.Collaborative || (p.OwnerId.Length > 0 && p.OwnerId == _spotifyUserId)).ToList();
    }
}
