using System.Text.Json;

namespace WiimControl;

sealed record OutputChoice(int Code, string Mode, string Name);

sealed record BluetoothSpeaker(string Name, string Address, bool Connected);

sealed record CastProfile(string Key, string Name, string Format, int BitRate, int SampleFormat);

sealed record CastReceiver(string Id, string Name, bool Selected);

sealed record OutputState(List<OutputChoice> Choices, string ActiveMode, bool BluetoothOut, bool CastOut, string BluetoothName,
    List<CastReceiver>? CastReceivers = null, string? CastProfileKey = null, int? CastLatency = null, UsbResolution? Usb = null,
    SimultaneousLineOut? LineOut = null, int? SwitchDelay = null, bool? FixedVolume = null, List<OutputLevel>? Levels = null);

sealed record UsbResolution(List<int> Rates, List<int> Bits, int Rate, int BitDepth);

sealed partial class WiimController
{
    private static readonly Dictionary<string, (int Code, string Name)> OutputModes = new()
    {
        ["AUDIO_OUTPUT_SPEAKER_MODE"] = (7, "Speakers"),
        ["AUDIO_OUTPUT_UAC_CARD_MODE"] = (8, "USB DAC"),
        ["AUDIO_OUTPUT_AUX_MODE"] = (2, "Line out"),
        ["AUDIO_OUTPUT_SPDIF_MODE"] = (1, "Optical out"),
        ["AUDIO_OUTPUT_COAX_MODE"] = (3, "Coaxial out")
    };

    internal static readonly CastProfile[] CastProfiles =
    [
        new("mp3-128", "MP3, 128 kbps", "mp3", 128000, 0),
        new("mp3-320", "MP3, 320 kbps", "mp3", 320000, 0),
        new("flac", "FLAC, 24-bit / 48 kHz", "flac", 700000, 1)
    ];

    internal static readonly (int Milliseconds, string Name)[] CastLatencies = [(0, "Low (0 ms)"), (1000, "Medium (1000 ms)"), (3000, "High (3000 ms)")];

    private static string? CastProfileKeyOf(string? json)
    {
        string format = JsonField(json, "transcode_profile") ?? string.Empty;
        int.TryParse(JsonField(json, "bit_rate"), out int bitRate);
        return CastProfiles.FirstOrDefault(p => p.Format == format && (p.Format == "flac" || p.BitRate == bitRate))?.Key;
    }

    private static List<CastReceiver>? ParseCastReceivers(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            if (Prop(doc.RootElement, "outputs") is not { ValueKind: JsonValueKind.Array } outputs) return null;
            return outputs.EnumerateArray()
                .Select(o => new CastReceiver(Prop(o, "id").ToString(), Prop(o, "name").ToString(), Prop(o, "selected").ValueKind == JsonValueKind.True))
                .Where(r => r.Id.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal async Task<List<CastReceiver>?> GetCastReceiversAsync(string ip) =>
        ParseCastReceivers(await SendToDeviceAsync(ip, "audio_cast:get_speaker_list").ConfigureAwait(false));

    internal async Task<List<CastReceiver>> FindCastReceiversAsync(string ip, Action<List<CastReceiver>> found, CancellationToken cancel)
    {
        await SendToDeviceAsync(ip, "audio_cast:scan_speaker").ConfigureAwait(false);
        var latest = new List<CastReceiver>();
        for (int i = 0; i < 15 && !cancel.IsCancellationRequested; i++)
        {
            if (await GetCastReceiversAsync(ip).ConfigureAwait(false) is { } receivers && receivers.Count >= latest.Count)
            {
                latest = receivers;
                found(receivers);
            }
            await Task.Delay(1000, cancel).ConfigureAwait(false);
        }
        return latest;
    }

    internal async Task<bool> SetCastReceiverAsync(string ip, string id, bool enabled) =>
        CastReplyOk(await SendToDeviceAsync(ip, $"audio_cast:speaker_{(enabled ? "enable" : "disable")}:{id}").ConfigureAwait(false));

    internal async Task<bool> SetCastProfileAsync(string ip, CastProfile profile) =>
        CastReplyOk(await SendToDeviceAsync(ip, "audio_cast:speaker_set_transcode_profile:" + JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sample_rate"] = 48000, ["sample_format"] = profile.SampleFormat, ["bit_rate"] = profile.BitRate, ["transcode_profile"] = profile.Format
        })).ConfigureAwait(false));

    internal async Task<bool> SetCastLatencyAsync(string ip, int milliseconds) =>
        CastReplyOk(await SendToDeviceAsync(ip, "audio_cast:speaker_set_transcode_buffer_time:" +
            JsonSerializer.Serialize(new Dictionary<string, int> { ["cache_max_time"] = milliseconds })).ConfigureAwait(false));

    private static bool CastReplyOk(string? reply) => JsonField(reply, "code") == "0";

    private static string ShortDeviceName(string name) =>
        name.IndexOf(" at ", StringComparison.Ordinal) is int at and > 0 ? name[..at].Trim() : name.Trim();

    internal static bool IsSpeakerOutput(string devName) => devName.Contains("Speaker", StringComparison.OrdinalIgnoreCase);

    internal static string OutputChoiceName(string mode, string knownName, string devName)
    {
        if (IsSpeakerOutput(devName)) return "Speakers";
        string device = ShortDeviceName(devName);
        return mode == UsbDacOutputMode && device.Length > 0 ? $"{knownName} ({device})" : knownName;
    }

    internal async Task<OutputState?> GetOutputStateAsync(string ip)
    {
        var list = SendToDeviceAsync(ip, "getSoundCardModeSupportList");
        var active = SendToDeviceAsync(ip, "getActiveSoundCardOutputMode");
        var bluetooth = SendToDeviceAsync(ip, "getbtstatus");
        var usbCap = SendToDeviceAsync(ip, "getUacOutMaxCap");
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var levels = GetOutputLevelsAsync(ip);
        await Task.WhenAll(list, active, bluetooth, usbCap, statusEx, levels).ConfigureAwait(false);
        if (list.Result == null && active.Result == null) return null;

        var choices = new List<OutputChoice>();
        string activeIndex = JsonField(active.Result, "index") ?? string.Empty;
        (List<int> Rates, List<int> Bits)? usbCaps = null;
        var coexist = new List<string>();
        var speakerModes = new HashSet<string>();
        bool switchDelaySupported = false;
        static List<int> Numbers(JsonElement element) => element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => int.TryParse(e.ToString(), out int n) ? n : 0).Where(n => n > 0).Distinct().Order().ToList()
            : [];
        try
        {
            using var doc = JsonDocument.Parse(list.Result ?? "[]");
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var card in doc.RootElement.EnumerateArray())
                {
                    string mode = Prop(card, "mode").ToString();
                    if (activeIndex.Length > 0 && Prop(card, "index").ToString() == activeIndex)
                    {
                        var soundCard = Prop(card, "soundCard");
                        if (Prop(soundCard, "coexistMode") is { ValueKind: JsonValueKind.Array } modes)
                            coexist.AddRange(modes.EnumerateArray().Select(m => m.ToString()));
                        switchDelaySupported = Prop(soundCard, "supportSampleRateSwitchLatency").ToString() == "1";
                    }
                    if (mode == UsbDacOutputMode && (usbCaps == null || Prop(card, "index").ToString() == activeIndex))
                        usbCaps = (Numbers(Prop(Prop(card, "soundCard"), "sampleRateSupportList")), Numbers(Prop(Prop(card, "soundCard"), "bitDepthSupportList")));
                    string devName = Prop(Prop(card, "soundCard"), "devName").ToString();
                    if (IsSpeakerOutput(devName)) speakerModes.Add(mode);
                    if (!OutputModes.TryGetValue(mode, out var known) || choices.Any(c => c.Mode == mode)) continue;
                    choices.Add(new OutputChoice(known.Code, mode, OutputChoiceName(mode, known.Name, devName)));
                }
        }
        catch (JsonException) { }

        string activeMode = JsonField(active.Result, "mode") ?? string.Empty;
        bool btOut = JsonField(active.Result, "btSource") == "1";
        bool castOut = JsonField(active.Result, "audioCast") == "1";
        string btName = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(bluetooth.Result ?? "{}");
            var sink = Prop(doc.RootElement, "a2dp_sink");
            if (Prop(sink, "link_state").ToString() == "connected") btName = Prop(sink, "name").ToString();
        }
        catch (JsonException) { }
        UsbResolution? usb = null;
        if (usbCaps is { Rates.Count: > 0, Bits.Count: > 0 } caps && usbCap.Result?.Trim().Split(':') is [var r, var b, ..] &&
            int.TryParse(r, out int usbRate) && int.TryParse(b, out int usbBits))
            usb = new UsbResolution(caps.Rates, caps.Bits, usbRate, usbBits);
        var lineOutTask = LineOutModeOf(coexist.Where(m => !speakerModes.Contains(m))) is { } lineOutMode ? GetSimultaneousLineOutAsync(ip, lineOutMode) : Task.FromResult<SimultaneousLineOut?>(null);
        var delayTask = switchDelaySupported ? SendToDeviceAsync(ip, "getSpdifOutSwitchDelayMs") : Task.FromResult<string?>(null);
        await Task.WhenAll(lineOutTask, delayTask).ConfigureAwait(false);
        var lineOut = lineOutTask.Result;
        int? switchDelay = int.TryParse(delayTask.Result?.Trim(), out int delayMs) ? delayMs : null;
        bool? fixedVolume = !IsAmpProject(JsonField(statusEx.Result, "project")) && choices.Any(c => !speakerModes.Contains(c.Mode))
            ? FixedVolumeOf(statusEx.Result)
            : null;
        if (!castOut)
            return new OutputState(choices, activeMode, btOut, castOut, btName, Usb: usb, LineOut: lineOut, SwitchDelay: switchDelay,
                FixedVolume: fixedVolume, Levels: levels.Result);
        var receivers = SendToDeviceAsync(ip, "audio_cast:get_speaker_list");
        var profile = SendToDeviceAsync(ip, "audio_cast:speaker_get_transcode_profile");
        var latency = SendToDeviceAsync(ip, "audio_cast:speaker_get_transcode_buffer_time");
        await Task.WhenAll(receivers, profile, latency).ConfigureAwait(false);
        return new OutputState(choices, activeMode, btOut, castOut, btName, ParseCastReceivers(receivers.Result), CastProfileKeyOf(profile.Result),
            int.TryParse(JsonField(latency.Result, "cache_max_time"), out int ms) ? ms : null, usb, lineOut, switchDelay, fixedVolume, levels.Result);
    }

    private const string UsbDacOutputMode = "AUDIO_OUTPUT_UAC_CARD_MODE";
    private const int OutputSwitchChecks = 6;
    private static readonly TimeSpan OutputSwitchCheckPause = TimeSpan.FromMilliseconds(500);
    internal const string HeadphoneOutputMode = "AUDIO_OUTPUT_PHONE_JACK_MODE";

    internal static string UnlistedOutputName(string activeMode) =>
        activeMode == HeadphoneOutputMode ? "Headphones" : activeMode.Length > 0 ? "Other output" : "Unknown";

    internal async Task<bool> SetUsbResolutionAsync(string ip, int sampleRate, int bits) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setUacOutMaxCap:{sampleRate}:{bits}:0").ConfigureAwait(false));

    internal async Task<bool> SetOutputAsync(string ip, OutputChoice choice)
    {
        if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setAudioOutputHardwareMode:{choice.Code}").ConfigureAwait(false))) return false;
        for (int attempt = 0; attempt < OutputSwitchChecks; attempt++)
        {
            if (JsonField(await SendToDeviceAsync(ip, "getActiveSoundCardOutputMode").ConfigureAwait(false), "mode") == choice.Mode) break;
            await Task.Delay(OutputSwitchCheckPause).ConfigureAwait(false);
        }
        return true;
    }

    private static readonly string[] AmpProjects = ["WiiM_AMP", "WiiM_Amp_4layer", "WiiM_Amp_Pro", "WiiM_Amp_Ultra", "WiiM_CI_MOD_A80", "WiiM_CI_AMP"];

    internal static bool IsAmpProject(string? project) =>
        project is { Length: > 0 } && AmpProjects.Any(a => project.Contains(a, StringComparison.Ordinal));

    internal static bool? FixedVolumeOf(string? statusEx) => JsonField(statusEx, "volume_control") switch
    {
        "1" => true,
        "0" => false,
        _ => null
    };

    internal async Task<bool> SetFixedVolumeAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setVolumeControl:{(on ? 1 : 0)}").ConfigureAwait(false));


    private static List<BluetoothSpeaker> ParseBluetoothList(string? json)
    {
        var speakers = new List<BluetoothSpeaker>();
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            if (Prop(doc.RootElement, "list") is not { ValueKind: JsonValueKind.Array } list) return speakers;
            foreach (var item in list.EnumerateArray())
            {
                string address = Prop(item, "ad").ToString();
                if (address.Length == 0) continue;
                string role = Prop(item, "role").ToString();
                if (role.Length > 0 && role.Contains("Source", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Prop(item, "name").ToString();
                speakers.Add(new BluetoothSpeaker(name.Length > 0 ? name : address, address, Prop(item, "ct").ToString() == "1"));
            }
        }
        catch (JsonException) { }
        return speakers;
    }

    internal async Task<List<BluetoothSpeaker>> GetPairedSpeakersAsync(string ip) =>
        ParseBluetoothList(await SendToDeviceAsync(ip, "getbthistory").ConfigureAwait(false));

    internal async Task<List<BluetoothSpeaker>> ScanBluetoothSpeakersAsync(string ip, CancellationToken cancel)
    {
        await SendToDeviceAsync(ip, "clearbtdiscoveryresult").ConfigureAwait(false);
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "startbtdiscovery:3").ConfigureAwait(false))) return [];
        string? result = null;
        for (int i = 0; i < 20 && !cancel.IsCancellationRequested; i++)
        {
            await Task.Delay(1000, cancel).ConfigureAwait(false);
            result = await SendToDeviceAsync(ip, "getbtdiscoveryresult").ConfigureAwait(false);
            if (JsonField(result, "scan_status") == "4") break;
        }
        return ParseBluetoothList(result);
    }

    internal async Task<bool> ConnectBluetoothSpeakerAsync(string ip, string address) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"connectbta2dpsynk:{address}").ConfigureAwait(false));

    internal async Task<bool> DisconnectBluetoothSpeakerAsync(string ip)
    {
        var connected = (await GetPairedSpeakersAsync(ip).ConfigureAwait(false)).FirstOrDefault(s => s.Connected);
        return connected != null && CommandSucceeded(await SendToDeviceAsync(ip, $"disconnectbta2dpsynk:{connected.Address}").ConfigureAwait(false));
    }
}
