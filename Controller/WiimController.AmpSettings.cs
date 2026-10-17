using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed partial class WiimController
{
    internal async Task<AmpSettings?> GetAmpSettingsAsync(string ip, string? screenInput = null)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var led = SendToDeviceAsync(ip, "LED_SWITCH_GET");
        var volumeLight = SendToDeviceAsync(ip, "VOL_LED_SWITCH_GET");
        var buttons = SendToDeviceAsync(ip, "Button_Enable_GET");
        var screen = SendToDeviceAsync(ip, "getLightOperationBrightConfig");
        var screenUi = GetScreenUiAsync(ip, screenInput);
        var wallpapers = GetWallpapersAsync(ip);
        var alarms = GetAlarmsAsync(ip);
        var network = GetNetworkSettingsAsync(ip);
        var prompts = SendToDeviceAsync(ip, "getLowPriorityPromptDisable");
        var autoUpdate = SendToDeviceAsync(ip, "getMVRemoteAutoOTAEnable");
        await Task.WhenAll(statusEx, led, volumeLight, buttons, screen, screenUi, wallpapers, alarms,
            network, prompts, autoUpdate).ConfigureAwait(false);
        if (statusEx.Result == null) return null;

        return new AmpSettings(
            int.TryParse(JsonField(statusEx.Result, "max_volume"), out int max) ? max : null,
            null,
            null,
            ParseInt(led.Result) is { } l ? l == 1 : null,
            ParseInt(buttons.Result) is { } b ? b == 0 : null,
            null,
            null,
            [],
            null,
            null,
            ParseScreen(screen.Result),
            null,
            ParseScreen(screen.Result) == null ? ParseInt(volumeLight.Result) : null,
            screenUi.Result,
            wallpapers.Result,
            ParseDeviceInfo(statusEx.Result),
            alarms.Result,
            null,
            ParseNetworkInfo(statusEx.Result, network.Result),
            null,
            null,
            null,
            null,
            false,
            false,
            null,
            null,
            PromptSounds: ParseInt(prompts.Result) is int promptsOff and (0 or 1) ? promptsOff == 0 : null,
            AutoUpdate: ParseAutoUpdate(autoUpdate.Result));
    }

    internal async Task<InputsPage> GetInputsPageAsync(string ip)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var inputs = GetInputVisibilityAsync(ip);
        await Task.WhenAll(statusEx, inputs).ConfigureAwait(false);
        if (statusEx.Result == null) return new InputsPage(false, null, null);
        return new InputsPage(true, inputs.Result, await GetInputSettingsAsync(ip, statusEx.Result).ConfigureAwait(false));
    }

    internal async Task<OutputPage> GetOutputPageAsync(string ip)
    {
        var state = GetOutputStateAsync(ip);
        var filter = GetDigitalFilterAsync(ip);
        var hardware = SendToDeviceAsync(ip, "getNewAudioOutputHardwareMode");
        var soundCards = SendToDeviceAsync(ip, "getSoundCardModeSupportList");
        await Task.WhenAll(state, filter, hardware, soundCards).ConfigureAwait(false);
        var (mode, filters, value) = filter.Result;
        if (state.Result == null && hardware.Result == null && mode == null) return new OutputPage(false, null, null, null, [], null);
        string? name = null;
        if (state.Result == null && hardware.Result != null)
        {
            var (modes, usbDac) = ParseOutputModes(soundCards.Result);
            name = JsonField(hardware.Result, "audiocast") == "1" ? "DLNA out"
                : JsonField(hardware.Result, "source") == "1" ? "Bluetooth out"
                : OutputName(int.TryParse(JsonField(hardware.Result, "hardware"), out int hw) ? hw : null, modes, usbDac);
        }
        return new OutputPage(true, state.Result, name, mode, filters, value);
    }

    private async Task<(string? Mode, List<DigitalFilter> Filters, int? Value)> GetDigitalFilterAsync(string ip)
    {
        var (mode, filters) = ParseFilterList(await SendToDeviceAsync(ip, "getOutputDigitalFilterTypeSupportList").ConfigureAwait(false));
        int? value = mode != null ? ParseInt(await SendToDeviceAsync(ip, $"getOutputDigitalFilterType:{mode}").ConfigureAwait(false)) : null;
        return (mode, filters, value);
    }

    internal async Task<AmpSettings?> GetVolumeSettingsAsync(string ip)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var balance = SendToDeviceAsync(ip, "getChannelBalance");
        var fade = SendToDeviceAsync(ip, "GetFadeFeature");
        var buttonStep = SendToDeviceAsync(ip, "get_button_volume_step");
        var remoteStep = SendToDeviceAsync(ip, "get_remote_volume_step");
        var headroom = SendToDeviceAsync(ip, "EQGetHeadroom");
        var channel = GetChannelAsync(ip);
        await Task.WhenAll(statusEx, balance, fade, buttonStep, remoteStep, headroom, channel).ConfigureAwait(false);
        if (statusEx.Result == null) return null;

        return new AmpSettings(
            int.TryParse(JsonField(statusEx.Result, "max_volume"), out int max) ? max : null,
            ParseDouble(balance.Result),
            JsonField(fade.Result, "FadeFeature") is { } f ? f == "1" : null,
            null, null, null, null, [], null, null, null, null, null, null, null, null, null,
            ButtonVolumeStep: int.TryParse(JsonField(buttonStep.Result, "button_volume_step"), out int bs) ? bs : null,
            RemoteVolumeStep: int.TryParse(JsonField(remoteStep.Result, "remote_volume_step"), out int rs) ? rs : null,
            AutoHeadroom: JsonField(headroom.Result, "Mode") is { } headroomMode ? headroomMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) : null,
            HeadroomGain: double.TryParse(JsonField(headroom.Result, "Gain"), NumberStyles.Float, CultureInfo.InvariantCulture, out double headroomGain)
                ? headroomGain : null,
            Channel: channel.Result);
    }

    private const string UsbDacMode = "AUDIO_OUTPUT_UAC_CARD_MODE";

    private static (List<string>? Modes, string? UsbName) ParseOutputModes(string? json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return (null, null);
            var cards = doc.RootElement.EnumerateArray()
                .Select(m => (Mode: m.TryGetProperty("mode", out var mode) ? mode.GetString() ?? string.Empty : string.Empty,
                    Name: m.TryGetProperty("soundCard", out var card) && card.TryGetProperty("devName", out var name) ? name.GetString() ?? string.Empty : string.Empty))
                .Where(c => c.Mode.Length > 0)
                .ToList();
            var usbNames = cards.Where(c => c.Mode == UsbDacMode)
                .Select(c => c.Name.IndexOf(" at ", StringComparison.Ordinal) is int at and > 0 ? c.Name[..at].Trim() : c.Name.Trim())
                .Distinct()
                .ToList();
            return (cards.Select(c => c.Mode).Distinct().ToList(), usbNames is [{ Length: > 0 } single] ? single : null);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null);
        }
    }

    private const string SpeakerMode = "AUDIO_OUTPUT_SPEAKER_MODE";

    private static string? OutputModeOf(int? hardware) => hardware switch
    {
        1 => "AUDIO_OUTPUT_SPDIF_MODE",
        2 => "AUDIO_OUTPUT_AUX_MODE",
        3 => "AUDIO_OUTPUT_COAX_MODE",
        7 => SpeakerMode,
        8 => UsbDacMode,
        _ => null
    };

    internal static string OutputName(int? hardware, List<string>? supported = null, string? usbDac = null)
    {
        if (hardware == null) return "Unknown";
        string? mode = OutputModeOf(hardware);
        if (supported is { Count: > 0 } && supported.Contains(SpeakerMode) && mode != null && !supported.Contains(mode)) return "Speakers";
        return hardware switch
        {
            1 => "Optical out",
            2 => "Line out",
            3 => "Coaxial out",
            7 => "Speakers",
            8 => usbDac is { Length: > 0 } ? $"USB DAC ({usbDac})" : "USB DAC",
            _ => $"Mode {hardware}"
        };
    }

    internal async Task<(bool Reachable, SubwooferInfo? Info, HighPassFilter? HighPass, List<PairedSub>? WiimSubs)> GetSubwooferAsync(string ip)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var sub = SendToDeviceAsync(ip, "getSubLPF");
        var highPass = SendToDeviceAsync(ip, "getMainProtect");
        var wiimSubs = GetPairedSubsAsync(ip);
        await Task.WhenAll(statusEx, sub, highPass, wiimSubs).ConfigureAwait(false);
        if (statusEx.Result == null) return (false, null, null, null);
        string? project = JsonField(statusEx.Result, "project");
        var info = ParseSubwoofer(sub.Result, HasSubwooferOutput(project));
        return (true, info, info != null ? ParseHighPassFilter(highPass.Result) : null, IsWiimSub(project) ? null : wiimSubs.Result);
    }

    internal async Task<bool> SetMaxVolumeAsync(string ip, int maxVolume) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setMaxVolume:{Math.Clamp(maxVolume, 1, 100)}").ConfigureAwait(false));

    internal async Task<bool> SetBalanceAsync(string ip, double balance) =>
        CommandSucceeded(await SendToDeviceAsync(ip,
            $"setChannelBalance:{Math.Clamp(balance, -1, 1).ToString("0.00", CultureInfo.InvariantCulture)}").ConfigureAwait(false));

    internal async Task<bool> SetButtonVolumeStepAsync(string ip, int step) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"set_button_volume_step:{Math.Clamp(step, 1, 10)}").ConfigureAwait(false));

    internal async Task<bool> SetRemoteVolumeStepAsync(string ip, int step) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"set_remote_volume_step:{Math.Clamp(step, 1, 10)}").ConfigureAwait(false));

    internal async Task<bool> SetAutoHeadroomAsync(string ip, bool auto)
    {
        string mode = auto ? "Auto" : "Off";
        string? reply = await SendToDeviceAsync(ip, "EQSetHeadroom:" + System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["Mode"] = mode }))
            .ConfigureAwait(false);
        return string.Equals(JsonField(reply, "Mode"), mode, StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<bool> SetFadeAsync(string ip, bool enabled) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"SetFadeFeature:{(enabled ? 1 : 0)}").ConfigureAwait(false));

    internal async Task<bool> SetStatusLightAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"LED_SWITCH_SET:{(on ? 1 : 0)}").ConfigureAwait(false));

    internal static readonly (int Mode, string Label)[] VolumeLightModes =
        [(0, "Completely off"), (1, "On when adjusting the volume"), (2, "Always on")];

    internal async Task<bool> SetVolumeLightModeAsync(string ip, int mode) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"VOL_LED_SWITCH_SET:{mode}").ConfigureAwait(false));

    internal async Task<bool> SetButtonsLockedAsync(string ip, bool locked) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"Button_Enable_SET:{(locked ? 0 : 1)}").ConfigureAwait(false));

    internal async Task<bool> SetDigitalFilterAsync(string ip, string mode, int index)
    {
        var before = await GetDeviceVolumeAsync(ip).ConfigureAwait(false);
        bool muteAround = before is { Muted: false } && await SetDeviceMuteAsync(ip, true).ConfigureAwait(false);
        try
        {
            if (muteAround) await Task.Delay(400).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(new { mode, type = index });
            if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setOutputDigitalFilterType:{payload}").ConfigureAwait(false))) return false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                if (ParseInt(await SendToDeviceAsync(ip, $"getOutputDigitalFilterType:{mode}").ConfigureAwait(false)) == index) return true;
            }
            return false;
        }
        finally
        {
            if (muteAround)
            {
                await Task.Delay(500).ConfigureAwait(false);
                await SetDeviceMuteAsync(ip, false).ConfigureAwait(false);
            }
        }
    }

    internal async Task<bool> SetScreenAsync(string ip, ScreenConfig current, bool on, bool autoBrightness, int brightness, bool? wakeByTouch = null)
    {
        if (JsonNode.Parse(current.RawJson) is not JsonObject config) return false;
        config["disable"] = on ? 0 : 1;
        config["auto_sense_enable"] = autoBrightness ? 1 : 0;
        config["default_bright"] = brightness;
        if (wakeByTouch is bool wake) config["wakeup_by_touch"] = wake ? 1 : 0;
        return CommandSucceeded(await SendToDeviceAsync(ip, $"setLightOperationBrightConfig:{config.ToJsonString()}").ConfigureAwait(false));
    }

    private static int? ParseInt(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static double? ParseDouble(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    private static (string? Mode, List<DigitalFilter> Filters) ParseFilterList(string? json)
    {
        var filters = new List<DigitalFilter>();
        if (json == null) return (null, filters);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("filterType", out var types) ||
                types.ValueKind != JsonValueKind.Array) return (null, filters);
            foreach (var type in types.EnumerateArray())
            {
                if (!type.TryGetProperty("mode", out var mode) || !type.TryGetProperty("setting", out var settings) ||
                    settings.ValueKind != JsonValueKind.Array) continue;
                foreach (var s in settings.EnumerateArray())
                    if (s.TryGetProperty("index", out var index) && index.TryGetInt32(out int i) &&
                        s.TryGetProperty("value", out var name))
                        filters.Add(new DigitalFilter(i, name.ToString()));
                return (mode.ToString(), filters);
            }
        }
        catch (JsonException) { }
        return (null, filters);
    }

    private static readonly string[] SubwooferHardwareFields = ["plugged", "delay_main_sub", "linein_delay"];

    internal static SubwooferInfo? ParseSubwoofer(string? json, bool knownModel)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cross", out _)) return null;
            if (!knownModel && !SubwooferHardwareFields.Any(f => root.TryGetProperty(f, out _))) return null;
            double Number(string name) =>
                root.TryGetProperty(name, out var p) && double.TryParse(p.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
            return new SubwooferInfo(Number("status") == 1, Number("plugged") == 1, Number("cross"),
                Number("phase"), Number("level"), Number("main_filter") == 1,
                root.TryGetProperty("sub_delay", out _) ? Number("sub_delay") : null,
                root.TryGetProperty("sub_filter", out _) && Number("sub_filter") == 0);
        }
        catch (JsonException) { return null; }
    }

    private static ScreenConfig? ParseScreen(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("default_bright", out var bright)) return null;
            int Value(string name) => root.TryGetProperty(name, out var p) && p.TryGetInt32(out int v) ? v : 0;
            return new ScreenConfig(json, Value("disable") == 0, Value("auto_sense_enable") == 1, bright.TryGetInt32(out int b) ? b : 0,
                root.TryGetProperty("wakeup_by_touch", out _) ? Value("wakeup_by_touch") == 1 : null);
        }
        catch (JsonException) { return null; }
    }
}
