using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed partial class WiimController
{
    private const string Removed = "(removed)";
    private const string NoReply = "(no reply)";

    private static readonly string[] ReportCommands =
    [
        "getStatusEx", "getPlayerStatus", "getMetaInfo", "streamServicesCapability", "GetAcousticCapability", "EQGetDynamicBass", "EQGetHeadroom",
        "GetFadeFeature", "LED_SWITCH_GET", "VOL_LED_SWITCH_GET", "Button_Enable_GET", "Squeezelite:getState", "audio_cast:get_speaker_list",
        "audio_cast:speaker_get_transcode_buffer_time", "audio_cast:speaker_get_transcode_profile", "getActiveSoundCardOutputMode",
        "getAllRoutines", "getAudioInputCapbility", "getAudioInputEnable", "getAudioInputPassThroughEnable", "getAudioInputPassThroughSrc",
        "getAuxAutoSenseEnable", "getAuxInMaxCap", MaxCapQuery, "getCecPowerCtrl", "getChannelBalance", "getHDMIAutoSenseEnable", "getIPV6Enable",
        "getLightOperationBrightConfig", "getLowPriorityPromptDisable", "getMVRemoteAutoOTAEnable", "getMainProtect", "getModeRename",
        "getNetworkPreferDNS", "getNewAudioOutputHardwareMode", "getNonHDMIModeSwitchSpkCtrl", "getOutputDigitalFilterTypeSupportList",
        "getPlayModeGainConfig", "getPowerModeTime", "getPresetInfo", "getShutdown", "getSoundCardModeSupportList", "getSpdifAutoSenseEnable",
        "getSpdifInNoiseRemove", "getInputModeSupportList", "getSpdifOutSwitchDelayMs", "getSubLPF", "getUacOutMaxCap", "getWlanBandConfig", "getWlanRoamConfig", "get_button_volume_step",
        "get_cec_volume_step", "get_remote_volume_step", "get_ui_config", "get_ui_wallpaper_list", "getbtstatus", "multiroom:getSlaveList",
        "multiroom:get_source_cache_time", "multiroom:getnamegrouplist", "multiroom:subwooferGetPairInfo",
        "getAudioOutputVrmsSupportList:{\"ver\":\"1.1\"}", "getAudioOutputVrms:AUDIO_OUTPUT_AUX_MODE",
        "getAudioOutputVrms:" + HeadphoneOutputMode, "getAuxVoltageSupportList", "getOutputVoltage"
    ];

    private static readonly HashSet<string> PersonalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "subtitle", "artist", "album", "trackid", "uri", "url", "albumarturi", "picurl", "pic_url", "art", "ip", "ipv4", "ipv6", "eth0", "eth1", "apcli0",
        "wlan0", "gateway", "dns", "server", "host", "hostname", "connected_server", "default_server", "target_server", "discover_list",
        "password", "psk", "pass", "key", "token", "email", "account", "user", "username", "devicename", "groupname", "group_name",
        "friendlyname", "bt_name", "hostname", "language", "date", "time"
    };

    private static readonly string[] PersonalKeyParts = ["mac", "uuid", "ssid", "timezone"];

    private static readonly HashSet<string> CommandsWithPersonalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "getbtstatus", "multiroom:getSlaveList", "multiroom:getnamegrouplist", "multiroom:subwooferGetPairInfo", "audio_cast:get_speaker_list",
        "getPresetInfo", "getAllRoutines", "getModeRename"
    };

    private static readonly HashSet<string> CommandsWithPersonalValues = new(StringComparer.OrdinalIgnoreCase) { "getModeRename" };

    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex Ipv6 = new(
        @"(?<![0-9A-Fa-f:])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?=[0-9A-Fa-f:]*[0-9A-Fa-f])(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?)(?![0-9A-Fa-f:])",
        RegexOptions.Compiled);
    private static readonly Regex MacAddress = new(@"\b[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5}\b", RegexOptions.Compiled);
    private static readonly Regex OwnPicture = new(@"\b[0-9A-Fa-f]{8,}\.(?:jpe?g|png|bmp|gif)\b", RegexOptions.Compiled);
    private static readonly Regex LongId = new(
        @"\b(?:[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}|(?=[0-9A-Fa-f]*[A-Fa-f])(?=[0-9A-Fa-f]*\d)[0-9A-Fa-f]{12,})\b",
        RegexOptions.Compiled);

    internal static bool IsPersonalKey(string key, bool namesArePersonal)
    {
        string lower = key.ToLowerInvariant();
        return PersonalKeys.Contains(lower) || PersonalKeyParts.Any(lower.Contains) ||
               (namesArePersonal && (lower.EndsWith("name", StringComparison.Ordinal) || lower == "id"));
    }

    internal static string ScrubText(string text, IReadOnlyCollection<string> knownNames)
    {
        foreach (var name in knownNames.Where(n => n.Length >= 3))
            text = text.Replace(name, "(device name)", StringComparison.OrdinalIgnoreCase);
        text = OwnPicture.Replace(text, "(own picture)");
        text = Ipv6.Replace(Ipv4.Replace(text, "0.0.0.0"), "::");
        return LongId.Replace(MacAddress.Replace(text, "00:00:00:00:00:00"), "(id)");
    }

    internal static JsonNode? ScrubReply(string? reply, IReadOnlyCollection<string> knownNames, bool namesArePersonal = false)
    {
        if (reply == null) return JsonValue.Create(NoReply);
        try
        {
            if (JsonNode.Parse(reply) is { } node and (JsonObject or JsonArray)) return Scrub(node);
        }
        catch (JsonException) { }
        return JsonValue.Create(ScrubText(reply.Trim(), knownNames));

        JsonNode? Scrub(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var clean = new JsonObject();
                    foreach (var (key, value) in obj)
                    {
                        bool personal = IsPersonalKey(key, namesArePersonal);
                        clean[UniqueKey(clean, ScrubText(key, knownNames))] = personal && value is not (JsonObject or JsonArray) ? JsonValue.Create(Removed)
                            : personal ? Scrub(value) is { } inner ? RemoveLeaves(inner) : null
                            : Scrub(value);
                    }
                    return clean;
                case JsonArray array:
                    return new JsonArray(array.Select(Scrub).ToArray());
                case JsonValue value when value.TryGetValue(out string? text):
                    return JsonValue.Create(ScrubText(text, knownNames));
                default:
                    return node?.DeepClone();
            }
        }
    }

    private static JsonNode RemoveLeaves(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, p.Value == null ? null : RemoveLeaves(p.Value)))),
        JsonArray array => new JsonArray(array.Select(item => item == null ? null : RemoveLeaves(item)).ToArray()),
        _ => JsonValue.Create(Removed)
    };

    private static string UniqueKey(JsonObject obj, string key)
    {
        string unique = key;
        for (int n = 2; obj.ContainsKey(unique); n++) unique = $"{key} ({n})";
        return unique;
    }

    internal static IReadOnlyList<string> ReportCommandNames => ReportCommands;

    internal static JsonNode? ScrubReportReply(string command, string? reply, IReadOnlyCollection<string> knownNames)
    {
        var clean = ScrubReply(reply, knownNames, CommandsWithPersonalNames.Contains(command));
        return CommandsWithPersonalValues.Contains(command) && clean is JsonObject or JsonArray ? RemoveLeaves(clean) : clean;
    }

    internal async Task<JsonObject> BuildDeviceReportAsync(IProgress<string>? progress = null)
    {
        var devices = new JsonArray();
        var known = _knownDevices.ToList();
        var names = known.Select(d => d.Name).ToList();
        var statuses = await Task.WhenAll(known.Select(d => SendToDeviceAsync(d.Ip, "getStatusEx"))).ConfigureAwait(false);
        foreach (var reply in statuses)
            if (JsonField(reply, "DeviceName") is { Length: > 0 } ownName && !names.Contains(ownName)) names.Add(ownName);
        int number = 0;
        foreach (var device in known)
        {
            number++;
            progress?.Report($"Reading device {number} of {known.Count}…");
            var replies = new JsonObject();
            string? status = statuses[number - 1];
            if (status != null)
                foreach (var command in ReportCommands)
                {
                    string? reply = command == "getStatusEx" ? status : await SendToDeviceAsync(device.Ip, command).ConfigureAwait(false);
                    replies[command] = ScrubReportReply(command, reply, names);
                }
            devices.Add(new JsonObject
            {
                ["Device"] = $"Device {number}",
                ["Model"] = status == null ? null : ModelName(JsonField(status, "project") ?? string.Empty),
                ["Project"] = JsonField(status, "project"),
                ["Firmware"] = JsonField(status, "firmware"),
                ["Reachable"] = status != null,
                ["Replies"] = replies
            });
        }
        return new JsonObject
        {
            ["App"] = "Wiim Control",
            ["Version"] = AppVersion,
            ["Created"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["About"] = "How each device answers Wiim Control's read-only commands. Network names, addresses, IDs, device names and what's playing are removed.",
            ["Devices"] = devices
        };
    }
}
