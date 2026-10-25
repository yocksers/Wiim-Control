using System.Globalization;
using System.Text.Json;

namespace WiimControl;

sealed record PreGain(int Mode, string Name, double Gain);

sealed record InputSettings(bool? PreGainEnabled, List<PreGain> PreGains, double MinGain, double MaxGain,
    bool? OpticalAutoSense, bool? HdmiAutoSense, bool? LineInAutoSense, int? TvRemoteStep, bool? HdmiAudioOnly, bool? TvSpeakers,
    bool? StandbySourceEnabled, string? StandbySource, int? LineInRate = null, int? LineInBits = null,
    bool? ReduceNoise = null);

sealed partial class WiimController
{
    internal async Task<InputSettings> GetInputSettingsAsync(string ip, string? statusEx)
    {
        var gain = SendToDeviceAsync(ip, "getPlayModeGainConfig");
        var optical = SendToDeviceAsync(ip, "getSpdifAutoSenseEnable");
        var hdmi = SendToDeviceAsync(ip, "getHDMIAutoSenseEnable");
        var lineIn = SendToDeviceAsync(ip, "getAuxAutoSenseEnable");
        var tvStep = SendToDeviceAsync(ip, "get_cec_volume_step");
        var tvPower = SendToDeviceAsync(ip, "getCecPowerCtrl");
        var tvSpeakers = SendToDeviceAsync(ip, "getNonHDMIModeSwitchSpkCtrl");
        var standbyOn = SendToDeviceAsync(ip, "getAudioInputPassThroughEnable");
        var standbySource = SendToDeviceAsync(ip, "getAudioInputPassThroughSrc");
        var lineInCap = SendToDeviceAsync(ip, "getAuxInMaxCap");
        var reduceNoise = SendToDeviceAsync(ip, "getSpdifInNoiseRemove");
        var inputCards = SendToDeviceAsync(ip, "getInputModeSupportList");
        await Task.WhenAll(gain, optical, hdmi, lineIn, tvStep, tvPower, tvSpeakers, standbyOn, standbySource, lineInCap, reduceNoise, inputCards)
            .ConfigureAwait(false);

        bool? gainEnabled = null;
        var gains = new List<PreGain>();
        double min = -10, max = 10;
        try
        {
            using var doc = JsonDocument.Parse(gain.Result ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && Prop(root, "config") is { ValueKind: JsonValueKind.Array } config)
            {
                gainEnabled = Prop(root, "enable").ToString() == "1";
                double.TryParse(Prop(root, "min_gain").ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out min);
                if (!double.TryParse(Prop(root, "max_gain").ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out max)) max = 10;
                if (min >= max) (min, max) = (-10, 10);
                foreach (var item in config.EnumerateArray())
                    if (int.TryParse(Prop(item, "mode").ToString(), out int mode) &&
                        double.TryParse(Prop(item, "gain").ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        gains.Add(new PreGain(mode, Prop(item, "name").ToString(), value));
            }
        }
        catch (JsonException) { }

        string? source = standbySource.Result?.Trim();
        if (source is null || source.Length == 0 || source.Contains("unknown", StringComparison.OrdinalIgnoreCase) || source.Contains('{')) source = null;
        return new InputSettings(gainEnabled, gains, min, max,
            ParseFlag(optical.Result), ParseFlag(hdmi.Result), ParseFlag(lineIn.Result),
            int.TryParse(JsonField(tvStep.Result, "cec_volume_step"), out int step) ? step : null,
            ParseFlag(tvPower.Result) is { } power ? !power : null,
            ParseFlag(tvSpeakers.Result),
            ParseFlag(standbyOn.Result), source,
            lineInCap.Result?.Trim().Split(':') is [var rate, ..] && int.TryParse(rate, out int r) ? r : null,
            lineInCap.Result?.Trim().Split(':') is [_, var depth, ..] && int.TryParse(depth, out int d) ? d : null,
            HasReduceNoise(inputCards.Result, JsonField(statusEx, "project")) ? ParseFlag(reduceNoise.Result) : null);
    }

    internal static bool HasReduceNoise(string? inputModeList, string? project)
    {
        if (project?.Contains("WiiM_Ultra", StringComparison.OrdinalIgnoreCase) == true) return true;
        try
        {
            using var doc = JsonDocument.Parse(inputModeList ?? string.Empty);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.EnumerateArray().Any(card =>
                Prop(card, "mode").ToString() == "HDMI" && Prop(Prop(card, "soundCard"), "supportDolbyAudio").ToString().Length > 0);
        }
        catch (JsonException) { return false; }
    }

    internal async Task<bool> SetReduceNoiseAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setSpdifInNoiseRemove:{(on ? 1 : 0)}").ConfigureAwait(false));

    internal static string? ReduceNoiseInputs(IEnumerable<string>? inputs)
    {
        if (inputs == null) return "optical or HDMI ARC";
        var modes = inputs.ToList();
        bool optical = modes.Contains("optical"), hdmi = modes.Contains("HDMI");
        return optical && hdmi ? "optical or HDMI ARC" : optical ? "optical" : hdmi ? "HDMI ARC" : null;
    }

    internal async Task<bool> SetPreGainAsync(string ip, bool enabled, IEnumerable<PreGain> gains)
    {
        var body = new Dictionary<string, object>
        {
            ["enable"] = enabled ? 1 : 0,
            ["config"] = gains.Select(g => new Dictionary<string, object>
            {
                ["mode"] = g.Mode, ["name"] = g.Name, ["gain"] = g.Gain.ToString("0.0", CultureInfo.InvariantCulture)
            }).ToList()
        };
        return CommandSucceeded(await SendToDeviceAsync(ip, "setPlayModeGainConfig:" + JsonSerializer.Serialize(body)).ConfigureAwait(false));
    }

    internal async Task<bool> SetAutoSenseAsync(string ip, string input, bool on)
    {
        string command = input switch { "optical" => "setSpdifAutoSenseEnable", "HDMI" => "setHDMIAutoSenseEnable", _ => "setAuxAutoSenseEnable" };
        return CommandSucceeded(await SendToDeviceAsync(ip, $"{command}:{(on ? 1 : 0)}").ConfigureAwait(false));
    }

    internal async Task<bool> SetTvRemoteStepAsync(string ip, int step) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"set_cec_volume_step:{Math.Clamp(step, 1, 10)}").ConfigureAwait(false));

    internal async Task<bool> SetHdmiAudioOnlyAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setCecPowerCtrl:{(on ? 0 : 1)}").ConfigureAwait(false));

    internal async Task<bool> SetTvSpeakersAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setNonHDMIModeSwitchSpkCtrl:{(on ? 1 : 0)}").ConfigureAwait(false));

    internal async Task<bool> SetStandbySourceAsync(string ip, string? source)
    {
        if (source == null) return CommandSucceeded(await SendToDeviceAsync(ip, "setAudioInputPassThroughEnable:0").ConfigureAwait(false));
        return CommandSucceeded(await SendToDeviceAsync(ip, $"setAudioInputPassThroughSrc:{source}").ConfigureAwait(false)) &&
               CommandSucceeded(await SendToDeviceAsync(ip, "setAudioInputPassThroughEnable:1").ConfigureAwait(false));
    }

    internal async Task<bool> SetLineInResolutionAsync(string ip, int sampleRate, int bits) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"setAuxInMaxCap:{sampleRate}:{bits}:0").ConfigureAwait(false));
}
