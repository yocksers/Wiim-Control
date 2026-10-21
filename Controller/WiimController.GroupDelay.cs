using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record GroupDelay(string Input, int Milliseconds);

sealed record HighPassFilter(bool Enabled, int Frequency);

sealed partial class WiimController
{
    internal static readonly (int Milliseconds, string Name)[] GroupDelayLevels = [(800, "High"), (150, "Medium"), (70, "Low")];
    internal static readonly int[] HighPassFrequencies = [30, 35, 40];
    internal const int HighPassMin = 20;
    internal const int HighPassMax = 120;

    internal static string GroupDelayName(int milliseconds) =>
        GroupDelayLevels.FirstOrDefault(l => l.Milliseconds == milliseconds).Name is { } name ? $"{name} ({milliseconds} ms)" : $"{milliseconds} ms";

    internal static List<GroupDelay>? ParseGroupDelays(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["cache_time"] is not JsonArray list) return null;
            var delays = new List<GroupDelay>();
            foreach (var item in list.OfType<JsonObject>())
                foreach (var (input, value) in item)
                    if (value is JsonValue v && v.TryGetValue(out int ms)) delays.Add(new GroupDelay(input, ms));
            return delays.Count > 0 ? delays : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    internal async Task<(List<GroupDelay>? Delays, List<InputVisibility>? Inputs)> GetGroupDelaysAsync(string ip)
    {
        var delays = SendToDeviceAsync(ip, "multiroom:get_source_cache_time");
        var inputs = GetInputVisibilityAsync(ip);
        await Task.WhenAll(delays, inputs).ConfigureAwait(false);
        return (ParseGroupDelays(delays.Result), inputs.Result);
    }

    internal async Task<bool> SetGroupDelayAsync(string ip, string input, int milliseconds)
    {
        var body = new JsonObject { ["cache_time"] = new JsonArray(new JsonObject { [input] = milliseconds }) };
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "multiroom:set_source_cache_time:" + body.ToJsonString()).ConfigureAwait(false))) return false;
        var check = ParseGroupDelays(await SendToDeviceAsync(ip, "multiroom:get_source_cache_time").ConfigureAwait(false));
        return check?.Any(d => d.Input == input && d.Milliseconds == milliseconds) == true;
    }

    internal static HighPassFilter? ParseHighPassFilter(string? json)
    {
        if (JsonField(json, "status") is not { } status ||
            !double.TryParse(JsonField(json, "cross"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cross)) return null;
        return new HighPassFilter(status == "1", (int)Math.Round(cross));
    }

    internal async Task<bool> SetHighPassEnabledAsync(string ip, bool enabled) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setMainProtect:status:{(enabled ? 1 : 0)}").ConfigureAwait(false));

    internal async Task<bool> SetHighPassFrequencyAsync(string ip, int frequency) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setMainProtect:cross:{frequency}").ConfigureAwait(false));
}
