using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record DynamicBassInfo(int Level, int Min, int Max, int Step);

sealed partial class WiimController
{
    private const int DefaultDynamicBassMax = 10;

    internal static DynamicBassInfo? ParseDynamicBass(string? capability, string? levelReply)
    {
        try
        {
            if (JsonNode.Parse(levelReply ?? string.Empty) is not JsonObject levelRoot ||
                levelRoot["level"] is not JsonValue levelValue || !TryInt(levelValue, out int level)) return null;
            int min = 0, max = DefaultDynamicBassMax, step = 1;
            if (JsonNode.Parse(capability ?? "{}") is JsonObject root && root["DynamicBass"] is JsonObject range)
            {
                if (range["Min"] is JsonValue a && TryInt(a, out int parsedMin)) min = parsedMin;
                if (range["Max"] is JsonValue b && TryInt(b, out int parsedMax)) max = parsedMax;
                if (range["Step"] is JsonValue c && TryInt(c, out int parsedStep) && parsedStep > 0) step = parsedStep;
            }
            if (max <= min) return null;
            return new DynamicBassInfo(Math.Clamp(level, min, max), min, max, step);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }

        static bool TryInt(JsonValue value, out int result)
        {
            if (value.TryGetValue(out result)) return true;
            if (value.TryGetValue(out double number))
            {
                result = (int)Math.Round(number);
                return true;
            }
            return value.TryGetValue(out string? text) && int.TryParse(text, out result);
        }
    }

    internal async Task<DynamicBassInfo?> GetDynamicBassAsync(string ip)
    {
        var capability = SendToDeviceAsync(ip, "GetAcousticCapability");
        var level = SendToDeviceAsync(ip, "EQGetDynamicBass");
        await Task.WhenAll(capability, level).ConfigureAwait(false);
        return ParseDynamicBass(capability.Result, level.Result);
    }

    internal async Task<bool> SetDynamicBassAsync(string ip, int level) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"EQSetDynamicBass:{{\"on\":1, \"level\":{level}}}").ConfigureAwait(false));
}
