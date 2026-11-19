using System.Collections.Concurrent;
using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    internal const int RoonMode = 5;
    private static readonly TimeSpan RoonZoneLifetime = TimeSpan.FromSeconds(1.5);
    private readonly ConcurrentDictionary<string, (DateTime Fetched, JsonElement Zone)> _roonZones = new();
    private DateTime _lastRoonConnect = DateTime.MinValue;

    private RoonConnection? ActiveRoon()
    {
        if (_roon is { IsRegistered: true } roon) return roon;
        if (_roonTokens.Count > 0 && _roonConnecting == null && DateTime.UtcNow - _lastRoonConnect > TimeSpan.FromSeconds(30))
        {
            _lastRoonConnect = DateTime.UtcNow;
            _ = GetRoonAsync();
        }
        return null;
    }

    private async Task<JsonElement?> RoonZoneOfAsync(string ip)
    {
        if (ActiveRoon() is not { } roon) return null;
        if (_roonZones.TryGetValue(ip, out var cached) && DateTime.UtcNow - cached.Fetched < RoonZoneLifetime) return cached.Zone;
        string name = _knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? string.Empty;
        if (name.Length == 0) return null;
        var reply = await roon.RequestAsync($"{RoonTransportService}/get_zones", timeout: TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (reply is not { IsSuccess: true } || Prop(reply.Body, "zones") is not { ValueKind: JsonValueKind.Array } zones) return null;
        JsonElement? match = null;
        foreach (var zone in zones.EnumerateArray())
        {
            bool named = string.Equals(Prop(zone, "display_name").ToString(), name, StringComparison.OrdinalIgnoreCase);
            bool output = Prop(zone, "outputs") is { ValueKind: JsonValueKind.Array } outputs &&
                          outputs.EnumerateArray().Any(o => string.Equals(Prop(o, "display_name").ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (named) { match = zone; break; }
            if (output) match ??= zone;
        }
        if (match is { } found) _roonZones[ip] = (DateTime.UtcNow, found);
        else _roonZones.TryRemove(ip, out _);
        return match;
    }

    private async Task<int> CurrentModeAsync(string ip) =>
        _lastModes.TryGetValue(ip, out var known) && DateTime.UtcNow - known.Fetched < TimeSpan.FromSeconds(5)
            ? known.Mode
            : int.TryParse(JsonField(await SendToDeviceAsync(ip, "getPlayerStatus").ConfigureAwait(false), "mode"), out int mode) ? mode : -1;

    private async Task<string?> RoonZoneIdForCommandAsync(string ip)
    {
        if (ActiveRoon() == null) return null;
        if (await CurrentModeAsync(ip).ConfigureAwait(false) != RoonMode) return null;
        return await RoonZoneOfAsync(ip).ConfigureAwait(false) is { } zone ? Prop(zone, "zone_id").ToString() : null;
    }

    private async Task<bool> TryRoonCommandAsync(string ip, string action, int seconds = 0)
    {
        if (await RoonZoneIdForCommandAsync(ip).ConfigureAwait(false) is not { } zoneId || _roon is not { } roon) return false;
        _roonZones.TryRemove(ip, out _);
        if (action == "seek")
            return (await roon.RequestAsync($"{RoonTransportService}/seek", new Dictionary<string, object?>
            {
                ["zone_or_output_id"] = zoneId, ["how"] = "absolute", ["seconds"] = Math.Max(0, seconds)
            }).ConfigureAwait(false))?.IsSuccess == true;
        string? control = action switch
        {
            "onepause" or "playpause" => "playpause",
            "resume" or "play" => "play",
            "pause" => "pause",
            "next" => "next",
            "prev" => "previous",
            "stop" => "stop",
            _ => null
        };
        if (control == null) return false;
        return (await roon.RequestAsync($"{RoonTransportService}/control", new Dictionary<string, object?>
        {
            ["zone_or_output_id"] = zoneId, ["control"] = control
        }).ConfigureAwait(false))?.IsSuccess == true;
    }

    private async Task<bool> TryRoonLoopAsync(string ip, int mode)
    {
        if (await RoonZoneIdForCommandAsync(ip).ConfigureAwait(false) is not { } zoneId || _roon is not { } roon) return false;
        _roonZones.TryRemove(ip, out _);
        var (shuffle, repeat) = ParseLoop(mode);
        return (await roon.RequestAsync($"{RoonTransportService}/change_settings", new Dictionary<string, object?>
        {
            ["zone_or_output_id"] = zoneId,
            ["shuffle"] = shuffle,
            ["loop"] = repeat switch { 2 => "loop", 1 => "loop_one", _ => "disabled" }
        }).ConfigureAwait(false))?.IsSuccess == true;
    }

    private async Task<bool> TryExternalCommandAsync(string ip, string action, int seconds = 0) =>
        await TryRoonCommandAsync(ip, action, seconds).ConfigureAwait(false) ||
        await TryLyrionCommandAsync(ip, action, seconds).ConfigureAwait(false);

    private async Task<DeviceStatus> ApplyRoonAsync(string ip, DeviceStatus status)
    {
        if (status.Mode != RoonMode || ActiveRoon() is not { } roon) return status;
        if (await RoonZoneOfAsync(ip).ConfigureAwait(false) is not { } zone) return status;
        string state = Prop(zone, "state").ToString();
        if (state is not ("playing" or "paused" or "loading")) return status;
        var playing = Prop(zone, "now_playing");
        var lines = Prop(playing, "three_line");
        string image = Prop(playing, "image_key").ToString();
        var settings = Prop(zone, "settings");
        bool shuffle = Prop(settings, "shuffle").ValueKind == JsonValueKind.True;
        int repeat = Prop(settings, "loop").ToString() switch { "loop" => 2, "loop_one" => 1, _ => 0 };
        double.TryParse(Prop(playing, "seek_position").ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seek);
        double.TryParse(Prop(playing, "length").ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double length);
        return status with
        {
            PlayState = state switch { "playing" => "play", "paused" => "pause", _ => "load" },
            Title = Prop(lines, "line1").ToString(),
            Artist = Prop(lines, "line2").ToString(),
            Album = Prop(lines, "line3").ToString(),
            ArtUrl = image.Length > 0 ? $"http://{roon.Host}:{roon.Port}/api/image/{image}?scale=fit&width=600&height=600&format=image/jpeg" : string.Empty,
            PositionMs = (int)(seek * 1000),
            PositionAt = DateTime.UtcNow,
            DurationMs = (int)(length * 1000),
            Loop = LoopMode(shuffle, repeat),
            SourceLabel = "Roon"
        };
    }
}
