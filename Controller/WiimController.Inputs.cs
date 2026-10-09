using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed partial class WiimController
{
    internal static string InputLabel(string input) => input.ToLowerInvariant() switch
    {
        "wifi" => "Wi-Fi",
        "line-in" => "Line in",
        "line-in2" => "Line in 2",
        "optical" => "Optical",
        "co-axial" or "coaxial" => "Coaxial",
        "bluetooth" => "Bluetooth",
        "hdmi" => "HDMI",
        "phono" => "Phono",
        "udisk" => "USB drive",
        "pcusb" or "usb" => "USB",
        _ => input
    };

    internal static string? InputOfMode(int mode) => mode switch
    {
        4 or 40 or 60 => "line-in",
        47 => "line-in2",
        5 or 41 => "bluetooth",
        6 or 43 => "optical",
        45 => "co-axial",
        49 => "HDMI",
        51 => "PCUSB",
        11 => "udisk",
        99 => null,
        _ => "wifi"
    };

    private static List<InputVisibility>? ParseInputs(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (!doc.RootElement.TryGetProperty("audioInput", out var inputs) || inputs.ValueKind != JsonValueKind.Array) return null;
            var list = new List<InputVisibility>();
            foreach (var input in inputs.EnumerateArray())
            {
                string mode = input.TryGetProperty("mode", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                bool shown = !input.TryGetProperty("enable", out var e) || e.ToString() != "0";
                if (mode.Length > 0 && mode != "udisk" && !list.Any(i => i.Mode == mode)) list.Add(new InputVisibility(mode, shown));
            }
            return list.Count > 0 ? list : null;
        }
        catch (JsonException) { return null; }
    }

    internal async Task<List<InputVisibility>?> GetInputVisibilityAsync(string ip) =>
        ParseInputs(await SendToDeviceAsync(ip, "getAudioInputEnable").ConfigureAwait(false));

    internal async Task<List<string>?> GetInputsAsync(string ip)
    {
        var inputs = await GetInputVisibilityAsync(ip).ConfigureAwait(false)
            ?? ParseInputs(await SendToDeviceAsync(ip, "getAudioInputCapbility").ConfigureAwait(false));
        return inputs?.Where(i => i.Shown).Select(i => i.Mode).ToList();
    }

    internal async Task<bool> SetInputShownAsync(string ip, string mode, bool shown)
    {
        var json = await SendToDeviceAsync(ip, "getAudioInputEnable").ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["audioInput"] is not JsonArray inputs) return false;
            if (inputs.OfType<JsonObject>().FirstOrDefault(i => i["mode"]?.GetValue<string>() == mode) is not { } input) return false;
            input["enable"] = shown ? 1 : 0;
            if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setAudioInputEnable:{root.ToJsonString()}").ConfigureAwait(false))) return false;
        }
        catch (Exception) { return false; }
        var check = await GetInputVisibilityAsync(ip).ConfigureAwait(false);
        return check?.Any(i => i.Mode == mode && i.Shown == shown) == true;
    }

    internal async Task<bool> SwitchInputAsync(string ip, string input) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setPlayerCmd:switchmode:{input}").ConfigureAwait(false));
}
