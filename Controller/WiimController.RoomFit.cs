using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private const int RoomFitLevel = 2;

    internal async Task<RoomFitState?> GetRoomFitStateAsync(string ip)
    {
        var listTask = SendEqJsonAsync(ip, "EQv2GetNewList", new { pluginURI = PeqPlugin, EQLevel = RoomFitLevel });
        var stateTask = SendEqJsonAsync(ip, "EQGetLV2SourceBandEx", new { EQLevel = RoomFitLevel, source_name = "", pluginURI = PeqPlugin });
        await Task.WhenAll(listTask, stateTask).ConfigureAwait(false);
        try
        {
            using var list = JsonDocument.Parse(listTask.Result ?? string.Empty);
            if (list.RootElement.ValueKind != JsonValueKind.Object ||
                !list.RootElement.TryGetProperty("custom", out var custom) || custom.ValueKind != JsonValueKind.Array) return null;

            var profiles = custom.EnumerateArray()
                .Select(p => p.ValueKind == JsonValueKind.Object
                    ? p.TryGetProperty("Name", out var name) ? name.GetString() : null
                    : p.ValueKind == JsonValueKind.String ? p.GetString() : null)
                .OfType<string>().Where(n => n.Length > 0).Distinct().ToList();
            if (profiles.Count == 0) return null;

            using var state = JsonDocument.Parse(stateTask.Result ?? string.Empty);
            var root = state.RootElement;
            bool enabled = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("EQStat", out var stat) && stat.GetString() == "On";
            string active = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            PeqBand[]? Bands(string key) =>
                root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array
                    ? ParsePeqBands(array, bandCount: null) : null;
            var left = Bands("EQBandL");
            var right = Bands("EQBandR");
            bool split = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("channelMode", out var mode) && mode.GetString() == "L/R";
            return new RoomFitState(enabled, active, profiles, Bands("EQBand") ?? left ?? [],
                split ? left : null, split ? right : null);
        }
        catch (JsonException) { return null; }
    }

    internal async Task<bool> SetRoomFitEnabledAsync(string ip, bool enabled) =>
        CommandSucceeded(await SendEqJsonAsync(ip, enabled ? "EQChangeSourceFX" : "EQSourceOff",
            new { EQLevel = RoomFitLevel, source_name = "", pluginURI = PeqPlugin }).ConfigureAwait(false));

    internal async Task<bool> LoadRoomFitProfileAsync(string ip, string name, bool enabled)
    {
        if (!CommandSucceeded(await SendEqJsonAsync(ip, "EQv2SourceLoad",
                new { EQLevel = RoomFitLevel, pluginURI = PeqPlugin, Name = name }).ConfigureAwait(false))) return false;
        return enabled || await SetRoomFitEnabledAsync(ip, false).ConfigureAwait(false);
    }
}
