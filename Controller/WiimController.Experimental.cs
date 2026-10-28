using System.Globalization;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record ExperimentalSettings(
    bool? TriggerOn, bool? TriggerOnReboot,
    string? SoundMode, List<string> SoundModes,
    string? DolbyEffect, List<string> DolbyEffects,
    string? VoiceLevel, List<string> VoiceLevels,
    bool? AmazonHd, bool? AlexaMusicHd, bool? TidalMqa,
    bool? HideSsid, bool? SmartDns, bool? DnsSpeedCheck,
    string? Language, List<(string Code, string Name)> Languages, HighPassFilter? HiddenHighPass = null)
{
    public bool IsEmpty => TriggerOn == null && TriggerOnReboot == null && SoundModes.Count == 0 && DolbyEffects.Count == 0 &&
                           VoiceLevels.Count == 0 && AmazonHd == null && AlexaMusicHd == null && TidalMqa == null && HideSsid == null &&
                           SmartDns == null && DnsSpeedCheck == null && Languages.Count < 2 && HiddenHighPass == null;
}

sealed record SyncAudio(double? ExtraDelayMs, bool MicroDelay, int? LegacyGroupDelay);

sealed partial class WiimController
{
    internal static readonly (int Bit, string Code, string Name)[] PromptLanguages =
    [
        (1, "zh_cn", "Chinese"), (2, "en_us", "English"), (3, "Ger_de", "German"), (5, "spanish", "Spanish"),
        (6, "french", "French"), (7, "portuguese", "Portuguese"), (8, "italian", "Italian"), (12, "ja_jp", "Japanese")
    ];

    internal static readonly int[] LegacyGroupDelays = [800, 400, 200, 80];
    private const string ConnectionManagerService = "urn:schemas-upnp-org:service:ConnectionManager:1";

    private static readonly (string Mime, string Name)[] FormatNames =
    [
        ("audio/flac", "FLAC"), ("audio/x-flac", "FLAC"), ("audio/wav", "WAV"), ("audio/x-wav", "WAV"), ("audio/l16", "PCM"),
        ("audio/x-dsd", "DSD"), ("audio/dsd", "DSD"), ("audio/ape", "APE (Monkey's Audio)"), ("audio/x-ape", "APE (Monkey's Audio)"),
        ("audio/mpeg", "MP3"), ("audio/mp3", "MP3"), ("audio/aac", "AAC"), ("audio/vnd.dlna.adts", "AAC"), ("audio/m4a", "M4A (AAC/ALAC)"),
        ("audio/mp4", "M4A (AAC/ALAC)"), ("audio/ogg", "Ogg Vorbis"), ("audio/opus", "Opus"), ("audio/wma", "WMA"), ("audio/x-ms-wma", "WMA"),
        ("audio/ac3", "Dolby Digital (AC-3)"), ("audio/eac3", "Dolby Digital Plus")
    ];

    internal static List<string> ParseFormats(string? sink)
    {
        var names = new List<string>();
        foreach (string entry in (sink ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split(':');
            if (parts.Length < 3) continue;
            string mime = parts[2].Trim().ToLowerInvariant();
            string name = FormatNames.FirstOrDefault(f => f.Mime == mime).Name ?? mime.Replace("audio/", string.Empty).ToUpperInvariant();
            if (mime.StartsWith("audio/", StringComparison.Ordinal) && !names.Contains(name)) names.Add(name);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static readonly TimeSpan FormatsTimeout = TimeSpan.FromSeconds(2);

    internal async Task<List<string>> GetFormatsAsync(string ip)
    {
        var lookup = ReadFormatsAsync(ip);
        return await Task.WhenAny(lookup, Task.Delay(FormatsTimeout)).ConfigureAwait(false) == lookup ? await lookup.ConfigureAwait(false) : [];
    }

    private async Task<List<string>> ReadFormatsAsync(string ip)
    {
        string url = await GetServiceUrlAsync(ip, ConnectionManagerService, "/upnp/control/renderconnmgr1").ConfigureAwait(false);
        var response = await Upnp.CallAsync(url, ConnectionManagerService, "GetProtocolInfo").ConfigureAwait(false);
        return ParseFormats(response?.Elements().FirstOrDefault(e => e.Name.LocalName == "Sink")?.Value);
    }
    internal const double SyncDelayLimitMs = 1000;

    internal static bool? ParseFlag(string? reply)
    {
        string text = reply?.Trim() ?? string.Empty;
        if (text.StartsWith('{')) text = JsonField(text, "status") ?? string.Empty;
        int colon = text.LastIndexOf(':');
        if (colon >= 0) text = text[(colon + 1)..].Trim();
        return text switch { "1" => true, "0" => false, _ => null };
    }

    private static JsonObject? ParseObject(string? json)
    {
        try { return JsonNode.Parse(json ?? string.Empty) as JsonObject; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private static JsonNode? FieldIgnoringCase(JsonObject? root, string name) =>
        root?.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static List<string> TextList(JsonNode? node, params string[] itemFields)
    {
        var result = new List<string>();
        if (node is not JsonArray array) return result;
        foreach (var item in array)
        {
            string? text = item is JsonObject obj
                ? itemFields.Select(f => FieldIgnoringCase(obj, f)?.ToString()).FirstOrDefault(v => v is { Length: > 0 })
                : item?.ToString();
            if (text is { Length: > 0 } && !result.Contains(text)) result.Add(text);
        }
        return result;
    }

    internal static (string? Current, List<string> Modes) ParseSoundModes(string? json)
    {
        var root = ParseObject(json);
        var modes = TextList(FieldIgnoringCase(root, "SupportModes"), "Mode", "Name");
        string? current = FieldIgnoringCase(root, "UserMode")?.ToString() ?? FieldIgnoringCase(root, "ActiveMode")?.ToString();
        if (current is { Length: > 0 } && modes.Count > 0 && !modes.Contains(current)) modes.Add(current);
        return (modes.Count > 0 ? current : null, modes);
    }

    internal static (string? Current, List<string> Effects) ParseSoundEffects(string? json)
    {
        var root = ParseObject(json);
        var effects = TextList(FieldIgnoringCase(root, "supportList"), "name", "mode", "title");
        string? current = FieldIgnoringCase(root, "current")?.ToString();
        if (current is { Length: > 0 } && effects.Count > 0 && !effects.Contains(current)) effects.Add(current);
        return (effects.Count > 0 ? current : null, effects);
    }

    internal static (string? Current, List<string> Levels) ParseVoiceLevel(string? levelJson, string? acousticCapability)
    {
        var capability = FieldIgnoringCase(ParseObject(acousticCapability), "VoiceLevel") as JsonObject;
        var levels = TextList(FieldIgnoringCase(capability, "Levels"));
        string? current = FieldIgnoringCase(ParseObject(levelJson), "Level")?.ToString();
        if (levels.Count == 0) return (null, levels);
        if (current is { Length: > 0 } && !levels.Contains(current)) levels.Add(current);
        return (current ?? FieldIgnoringCase(capability, "DefaultLevel")?.ToString(), levels);
    }

    internal static List<(string Code, string Name)> ParsePromptLanguages(string? mask)
    {
        string text = mask?.Trim() ?? string.Empty;
        bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bits)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bits);
        if (!parsed) return [];
        return PromptLanguages.Where(l => (bits & (1 << l.Bit)) != 0).Select(l => (l.Code, l.Name)).ToList();
    }

    internal static HighPassFilter? HiddenHighPass(string? acousticCapability, string? mainProtect) =>
        ParseHighPassFilter(mainProtect) is { } filter && !HasHighPass(acousticCapability, mainProtect) ? filter : null;

    internal static double? ParseSyncDelay(string? json) =>
        double.TryParse(JsonField(json, "SyncPlayExtraDelay"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ? ms : null;

    internal static bool HasMicroDelay(string? acousticCapability) =>
        FieldIgnoringCase(FieldIgnoringCase(ParseObject(acousticCapability), "OutputDelay") as JsonObject, "EnableMicroDelay")?.ToString()
            .Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    internal static string SyncDelayText(double ms, bool micro) =>
        micro ? Math.Round(ms, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture) : Math.Round(ms).ToString(CultureInfo.InvariantCulture);

    internal async Task<ExperimentalSettings?> GetExperimentalAsync(string ip)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var capability = SendToDeviceAsync(ip, "GetAcousticCapability");
        var trigger = SendToDeviceAsync(ip, "getTriggeroutStatus");
        var triggerReboot = SendToDeviceAsync(ip, "triggerOnByRebootGet");
        var soundMode = SendToDeviceAsync(ip, "EQGetSoundMode:{}");
        var soundEffect = SendToDeviceAsync(ip, "get_soundeffect:dap");
        var voiceLevel = SendToDeviceAsync(ip, "EQGetVoiceLevel:{}");
        var amazonHd = SendToDeviceAsync(ip, "GetAmazonHD");
        var alexaHd = SendToDeviceAsync(ip, "getAvsMusicHDEnable");
        var tidalMqa = SendToDeviceAsync(ip, "getTidalMQAOnOff");
        var smartDns = SendToDeviceAsync(ip, "getSmartDNSEnable");
        var dnsSpeed = SendToDeviceAsync(ip, "getSmartDNSSpeedCheckEnable");
        var highPass = SendToDeviceAsync(ip, "getMainProtect");
        await Task.WhenAll(statusEx, capability, trigger, triggerReboot, soundMode, soundEffect, voiceLevel, amazonHd, alexaHd, tidalMqa,
            smartDns, dnsSpeed, highPass).ConfigureAwait(false);
        if (statusEx.Result == null) return null;

        var modes = ParseSoundModes(soundMode.Result);
        var effects = ParseSoundEffects(soundEffect.Result);
        var levels = ParseVoiceLevel(voiceLevel.Result, capability.Result);
        var languages = ParsePromptLanguages(JsonField(statusEx.Result, "languages"));
        string? language = JsonField(statusEx.Result, "language");
        if (language is { Length: > 0 } && languages.Count > 1 && !languages.Any(l => l.Code.Equals(language, StringComparison.OrdinalIgnoreCase)))
            languages.Add((language, language));
        return new ExperimentalSettings(
            ParseFlag(trigger.Result), ParseFlag(triggerReboot.Result),
            modes.Current, modes.Modes, effects.Current, effects.Effects, levels.Current, levels.Levels,
            ParseFlag(amazonHd.Result), ParseFlag(alexaHd.Result), ParseFlag(tidalMqa.Result),
            ParseFlag(JsonField(statusEx.Result, "hideSSID")), ParseFlag(smartDns.Result), ParseFlag(dnsSpeed.Result),
            language, languages, HiddenHighPass(capability.Result, highPass.Result));
    }

    private async Task<bool> SendSettingAsync(string ip, string command) =>
        CommandSucceeded(await SendToDeviceAsync(ip, command).ConfigureAwait(false));

    private static int Bit(bool on) => on ? 1 : 0;

    internal Task<bool> TriggerOnceAsync(string ip, bool on) => SendSettingAsync(ip, $"setTriggeroutStatus:{Bit(on)}");
    internal Task<bool> SetTriggerOnRebootAsync(string ip, bool on) => SendSettingAsync(ip, $"triggerOnByRebootSet:{Bit(on)}");
    internal Task<bool> SetSoundModeAsync(string ip, string mode) =>
        SendSettingAsync(ip, "EQSetSoundMode:" + new JsonObject { ["UserMode"] = mode }.ToJsonString());
    internal Task<bool> SetDolbyEffectAsync(string ip, string effect) => SendSettingAsync(ip, $"set_soundeffect:dap:{effect}");
    internal Task<bool> SetVoiceLevelAsync(string ip, string level) =>
        SendSettingAsync(ip, "EQSetVoiceLevel:" + new JsonObject { ["Level"] = level }.ToJsonString());
    internal Task<bool> SetAmazonHdAsync(string ip, bool on) => SendSettingAsync(ip, $"EnableAmazonHD:{Bit(on)}");
    internal Task<bool> SetAlexaMusicHdAsync(string ip, bool on) => SendSettingAsync(ip, $"setAvsMusicHDEnable:{Bit(on)}");
    internal Task<bool> SetTidalMqaAsync(string ip, bool on) => SendSettingAsync(ip, $"setTidalMQAOnOff:{Bit(on)}");
    internal Task<bool> SetHideSsidAsync(string ip, bool on) => SendSettingAsync(ip, $"setHideSSID:{Bit(on)}");
    internal Task<bool> SetSmartDnsAsync(string ip, bool on) => SendSettingAsync(ip, $"setSmartDNSEnable:{Bit(on)}");
    internal Task<bool> SetDnsSpeedCheckAsync(string ip, bool on) => SendSettingAsync(ip, $"setSmartDNSSpeedCheckEnable:{Bit(on)}");
    internal Task<bool> SetPromptLanguageAsync(string ip, string code) => SendSettingAsync(ip, $"setLanguage:{code}");

    internal async Task<bool?> GetEndOfTrackAsync(string ip) => ParseFlag(await SendToDeviceAsync(ip, "getEndTrackEnable").ConfigureAwait(false));
    internal Task<bool> SetEndOfTrackAsync(string ip, bool on) => SendSettingAsync(ip, $"setEndTrackEnable:{Bit(on)}");

    internal async Task<SyncAudio> GetSyncAudioAsync(string ip)
    {
        var delay = SendToDeviceAsync(ip, "GetSyncPlayExtraDelay");
        var capability = SendToDeviceAsync(ip, "GetAcousticCapability");
        var leadTime = SendToDeviceAsync(ip, "multiroom:ConfigGet:leadtime");
        await Task.WhenAll(delay, capability, leadTime).ConfigureAwait(false);
        int? legacy = int.TryParse(JsonField(leadTime.Result, "leadtime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) ? ms : null;
        return new SyncAudio(ParseSyncDelay(delay.Result), HasMicroDelay(capability.Result), legacy);
    }

    internal Task<bool> SetSyncDelayAsync(string ip, double ms, bool micro) =>
        SendSettingAsync(ip, "SetSyncPlayExtraDelay:" + SyncDelayText(Math.Clamp(ms, -SyncDelayLimitMs, SyncDelayLimitMs), micro));

    internal Task<bool> SetLegacyGroupDelayAsync(string ip, int ms) =>
        SendSettingAsync(ip, $"multiroom:ConfigSet:leadtime:{ms.ToString(CultureInfo.InvariantCulture)}");

    internal Task<bool> SetRoomChannelAsync(string leaderIp, string memberAddress, int channel) =>
        SendSettingAsync(leaderIp, $"multiroom:SlaveChannel:{memberAddress}:{channel.ToString(CultureInfo.InvariantCulture)}");

    private Task<bool> RenameGroupMemberAsync(string leaderIp, string memberAddress, string name) =>
        SendSettingAsync(leaderIp, $"multiroom:SlaveDeviceName:{memberAddress}:{Uri.EscapeDataString(name)}");
}
