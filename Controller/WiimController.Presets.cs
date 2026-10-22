using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly int[] UpnpPorts = [49152, 59152];
    private static readonly Regex KeyRoutine = new(@"<Key(\d+)>\s*<RoutineId>([^<]+)</RoutineId>", RegexOptions.Compiled);

    internal async Task<PresetList?> GetPresetsAsync(string ip)
    {
        var statusTask = SendToDeviceAsync(ip, "getStatusEx");
        var presetTask = SendToDeviceAsync(ip, "getPresetInfo");
        var keysTask = GetPresetKeyListAsync(ip);
        var routinesTask = GetRoutinesAsync(ip);
        await Task.WhenAll(statusTask, presetTask, keysTask, routinesTask).ConfigureAwait(false);
        if (statusTask.Result == null && presetTask.Result == null) return null;

        int slots = int.TryParse(JsonField(statusTask.Result, "preset_key"), out int key) && key is > 0 and <= 40 ? key : 12;
        var presets = new List<PresetInfo>();
        try
        {
            using var doc = JsonDocument.Parse(presetTask.Result ?? string.Empty);
            if (doc.RootElement.TryGetProperty("preset_list", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var p in list.EnumerateArray())
                {
                    string Field(string name) => p.TryGetProperty(name, out var v) ? v.ToString() : string.Empty;
                    if (!int.TryParse(Field("number"), out int number) || number < 1) continue;
                    presets.Add(new PresetInfo(number, Clean(Field("name")) ?? $"Preset {number}", Clean(Field("source")) ?? string.Empty,
                        Clean(Field("picurl")) ?? string.Empty));
                }
        }
        catch (JsonException) { }

        string keyList = keysTask.Result;
        foreach (Match m in KeyRoutine.Matches(keyList))
        {
            int number = int.Parse(m.Groups[1].Value);
            string routineId = m.Groups[2].Value.Trim();
            if (number < 1 || routineId.Equals("Empty", StringComparison.OrdinalIgnoreCase) || presets.Any(p => p.Number == number)) continue;
            var routine = routinesTask.Result.GetValueOrDefault(routineId);
            presets.Add(new PresetInfo(number, routine.Name ?? $"Routine {number}", "Routine", string.Empty, routine.Summary ?? string.Empty, true));
        }
        foreach (Match m in KeyNumber.Matches(keyList))
        {
            int number = int.Parse(m.Groups[1].Value);
            string? content = KeyContent(keyList, number);
            if (number < 1 || !IsMusicKey(content) || presets.Any(p => p.Number == number)) continue;
            presets.Add(new PresetInfo(number, Clean(KeyValue(content, "Name")) ?? $"Preset {number}", Clean(KeyValue(content, "Source")) ?? string.Empty,
                Clean(KeyValue(content, "PicUrl")) ?? string.Empty));
        }
        presets.Sort((a, b) => a.Number.CompareTo(b.Number));
        return new PresetList(Math.Max(slots, presets.Count == 0 ? 0 : presets.Max(p => p.Number)), presets);
    }

    private static readonly Regex KeyNumber = new(@"<Key(\d+)>", RegexOptions.Compiled);

    private async Task<string> GetPresetKeyListAsync(string ip)
    {
        const string body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:GetKeyMapping " +
            "xmlns:u=\"urn:schemas-wiimu-com:service:PlayQueue:1\"></u:GetKeyMapping></s:Body></s:Envelope>";
        foreach (int port in UpnpPorts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{ip}:{port}/upnp/control/PlayQueue1")
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/xml")
                };
                request.Headers.Add("SOAPACTION", "\"urn:schemas-wiimu-com:service:PlayQueue:1#GetKeyMapping\"");
                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var envelope = System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return envelope.Descendants().FirstOrDefault(e => e.Name.LocalName == "QueueContext")?.Value ?? string.Empty;
            }
            catch (Exception) { }
        }
        return string.Empty;
    }

    private async Task<Dictionary<string, (string? Name, string? Summary)>> GetRoutinesAsync(string ip)
    {
        var result = new Dictionary<string, (string?, string?)>();
        var json = await SendToDeviceAsync(ip, "getAllRoutines").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (!doc.RootElement.TryGetProperty("routines", out var routines) || routines.ValueKind != JsonValueKind.Array) return result;
            foreach (var routine in routines.EnumerateArray())
            {
                if (!routine.TryGetProperty("id", out var id) || id.GetString() is not { } routineId) continue;
                string? name = routine.TryGetProperty("name", out var n) ? Clean(n.GetString()) : null;
                var parts = new List<string>();
                if (routine.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                    foreach (var step in steps.EnumerateArray())
                        if (RoutineStepText(step) is { Length: > 0 } text) parts.Add(text);
                result[routineId] = (name, string.Join(" · ", parts));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static string? RoutineStepText(JsonElement step)
    {
        string type = step.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var payload = step.TryGetProperty("payload", out var p) ? p : default;
        string Field(string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var v) ? v.ToString() : string.Empty;
        return type switch
        {
            "audioOutput" => RoutineOutputName(Field("output"), Field("devHexName")),
            "EQ" => Field("name") is { Length: > 0 } eq ? $"EQ {eq}" : "EQ",
            "subwoofer" => Field("subOutput") == "1" ? "Subwoofer on" : "Subwoofer off",
            "volume" => Field("volume") is { Length: > 0 } volume ? $"Volume {volume}" : "Volume",
            "source" or "input" => Field("source") is { Length: > 0 } source ? InputLabel(source) : null,
            "loopMode" => null,
            _ => null
        };
    }

    internal static string RoutineOutputName(string output, string hexName)
    {
        string device = string.Empty;
        try
        {
            if (hexName.Length > 0 && hexName.Length % 2 == 0)
                device = Encoding.UTF8.GetString(Convert.FromHexString(hexName));
        }
        catch (FormatException) { }
        int at = device.IndexOf(" at ", StringComparison.Ordinal);
        if (at > 0) device = device[..at];
        if (IsSpeakerOutput(device)) return "Speakers";
        return output switch
        {
            "AUDIO_OUTPUT_SPEAKER_MODE" => "Speakers",
            "AUDIO_OUTPUT_UAC_CARD_MODE" => device.Length > 0 ? $"USB DAC ({device})" : "USB DAC",
            "AUDIO_OUTPUT_SPDIF_MODE" => "Optical out",
            "AUDIO_OUTPUT_AUX_MODE" => "Line out",
            "AUDIO_OUTPUT_COAX_MODE" => "Coax out",
            "AUDIO_OUTPUT_HDMI_MODE" => "HDMI out",
            _ => device.Length > 0 ? device : "Output"
        };
    }

    internal async Task<bool> PlayPresetOnAsync(string ip, int number) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"MCUKeyShortClick:{number}").ConfigureAwait(false));
}
