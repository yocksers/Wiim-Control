using System.Globalization;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record AutoUpdate(int Months, DateTime? PausedUntil);

sealed record SimultaneousLineOut(string Mode, bool Enabled);

sealed partial class WiimController
{
    internal static readonly int[] AutoUpdatePauseMonths = [3, 6, 12];
    private static readonly string[] LineOutModes = ["AUDIO_OUTPUT_AUX_MODE", "AUDIO_OUTPUT_LINEOUT_MODE"];
    private const string MaxCapQuery = "getAuxOutMaxCap:{\"ver\":\"1.3\"}";

    internal static AutoUpdate? ParseAutoUpdate(string? json)
    {
        if (!int.TryParse(JsonField(json, "auto_ota_flag_value"), out int value) || value < -1) return null;
        DateTime? until = long.TryParse(JsonField(json, "auto_ota_flag_expire_ts"), out long ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
            : null;
        return new AutoUpdate(value, until);
    }

    internal async Task<bool> SetAutoUpdateAsync(string ip, int months) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setMVRemoteAutoOTAEnable:{months}").ConfigureAwait(false));

    internal async Task<bool> SetPromptSoundsAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"disableLowPriorityPrompt:{(on ? 0 : 1)}").ConfigureAwait(false));

    internal static readonly (int Bit, string Code, string Name)[] PromptLanguages =
    [
        (1, "zh_cn", "Chinese"), (2, "en_us", "English"), (3, "Ger_de", "German"), (5, "spanish", "Spanish"),
        (6, "french", "French"), (7, "portuguese", "Portuguese"), (8, "italian", "Italian"), (12, "ja_jp", "Japanese")
    ];

    internal static List<(string Code, string Name)> ParsePromptLanguages(string? mask)
    {
        string text = mask?.Trim() ?? string.Empty;
        bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bits)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bits);
        if (!parsed) return [];
        return PromptLanguages.Where(l => (bits & (1 << l.Bit)) != 0).Select(l => (l.Code, l.Name)).ToList();
    }

    internal static (string? Current, List<(string Code, string Name)> Languages) ParsePromptLanguageChoice(string? statusEx)
    {
        var languages = ParsePromptLanguages(JsonField(statusEx, "languages"));
        string? language = JsonField(statusEx, "language");
        if (language is { Length: > 0 } && languages.Count > 1 && !languages.Any(l => l.Code.Equals(language, StringComparison.OrdinalIgnoreCase)))
            languages.Add((language, language));
        return (language, languages);
    }

    internal async Task<bool> SetPromptLanguageAsync(string ip, string code) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setLanguage:{code}").ConfigureAwait(false));

    internal async Task<bool?> RestartDeviceAsync(string ip)
    {
        if (await SendToDeviceAsync(ip, "getStatusEx").ConfigureAwait(false) == null) return null;
        string? reply = await SendToDeviceAsync(ip, "reboot").ConfigureAwait(false);
        return reply == null || CommandSucceeded(reply);
    }

    internal static string? LineOutModeOf(IEnumerable<string> coexist) => LineOutModes.FirstOrDefault(coexist.Contains);

    internal static bool? ParseMultiList(string? json, string mode)
    {
        try
        {
            return JsonNode.Parse(json ?? string.Empty) is JsonObject { } root && root["multiList"] is JsonArray list
                ? list.Any(item => item?.ToString() == mode)
                : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private async Task<SimultaneousLineOut?> GetSimultaneousLineOutAsync(string ip, string mode) =>
        ParseMultiList(await SendToDeviceAsync(ip, MaxCapQuery).ConfigureAwait(false), mode) is bool on ? new SimultaneousLineOut(mode, on) : null;

    internal async Task<bool> SetSimultaneousLineOutAsync(string ip, string mode, bool on)
    {
        string? current = await SendToDeviceAsync(ip, MaxCapQuery).ConfigureAwait(false);
        JsonObject? root;
        try { root = JsonNode.Parse(current ?? string.Empty) as JsonObject; }
        catch (System.Text.Json.JsonException) { return false; }
        if (root == null) return false;
        root["multiList"] = on ? new JsonArray(mode) : new JsonArray();
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "setAuxOutMaxCap:" + root.ToJsonString()).ConfigureAwait(false))) return false;
        return ParseMultiList(await SendToDeviceAsync(ip, MaxCapQuery).ConfigureAwait(false), mode) == on;
    }

    internal async Task<bool> SetSwitchDelayAsync(string ip, int milliseconds) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setSpdifOutSwitchDelayMs:{milliseconds}").ConfigureAwait(false));
}
