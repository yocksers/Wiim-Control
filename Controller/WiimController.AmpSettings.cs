using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed partial class WiimController
{
    private int _activeMaxVolume = 100;

    internal async Task<AmpSettings?> GetAmpSettingsAsync(string ip)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var balance = SendToDeviceAsync(ip, "getChannelBalance");
        var fade = SendToDeviceAsync(ip, "GetFadeFeature");
        var led = SendToDeviceAsync(ip, "LED_SWITCH_GET");
        var buttons = SendToDeviceAsync(ip, "Button_Enable_GET");
        var output = SendToDeviceAsync(ip, "getNewAudioOutputHardwareMode");
        var filters = SendToDeviceAsync(ip, "getOutputDigitalFilterTypeSupportList");
        var sub = SendToDeviceAsync(ip, "getSubLPF");
        var screen = SendToDeviceAsync(ip, "getLightOperationBrightConfig");
        await Task.WhenAll(statusEx, balance, fade, led, buttons, output, filters, sub, screen).ConfigureAwait(false);
        if (statusEx.Result == null) return null;

        var (filterMode, filterList) = ParseFilterList(filters.Result);
        int? filter = filterMode != null
            ? ParseInt(await SendToDeviceAsync(ip, $"getOutputDigitalFilterType:{filterMode}").ConfigureAwait(false))
            : null;

        return new AmpSettings(
            int.TryParse(JsonField(statusEx.Result, "max_volume"), out int max) ? max : null,
            ParseDouble(balance.Result),
            JsonField(fade.Result, "FadeFeature") is { } f ? f == "1" : null,
            ParseInt(led.Result) is { } l ? l == 1 : null,
            ParseInt(buttons.Result) is { } b ? b == 0 : null,
            int.TryParse(JsonField(output.Result, "hardware"), out int hw) ? hw : null,
            filterMode,
            filterList,
            filter,
            ParseSubwoofer(sub.Result),
            ParseScreen(screen.Result));
    }

    internal static string OutputName(int? hardware) => hardware switch
    {
        null => "Unknown",
        1 => "Optical out",
        2 => "Line out",
        3 => "Coaxial out",
        7 => "Speakers",
        _ => $"Mode {hardware}"
    };

    internal async Task<bool> SetMaxVolumeAsync(string ip, int maxVolume)
    {
        maxVolume = Math.Clamp(maxVolume, 1, 100);
        bool ok = CommandSucceeded(await SendToDeviceAsync(ip, $"setMaxVolume:{maxVolume}").ConfigureAwait(false));
        if (ok && ip == _deviceIp) _activeMaxVolume = maxVolume;
        return ok;
    }

    internal async Task<bool> SetBalanceAsync(string ip, double balance) =>
        CommandSucceeded(await SendToDeviceAsync(ip,
            $"setChannelBalance:{Math.Clamp(balance, -1, 1).ToString("0.00", CultureInfo.InvariantCulture)}").ConfigureAwait(false));

    internal async Task<bool> SetFadeAsync(string ip, bool enabled) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"SetFadeFeature:{(enabled ? 1 : 0)}").ConfigureAwait(false));

    internal async Task<bool> SetStatusLightAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"LED_SWITCH_SET:{(on ? 1 : 0)}").ConfigureAwait(false));

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

    internal async Task<bool> SetScreenAsync(string ip, ScreenConfig current, bool on, bool autoBrightness, int brightness)
    {
        if (JsonNode.Parse(current.RawJson) is not JsonObject config) return false;
        config["disable"] = on ? 0 : 1;
        config["auto_sense_enable"] = autoBrightness ? 1 : 0;
        config["default_bright"] = brightness;
        return CommandSucceeded(await SendToDeviceAsync(ip, $"setLightOperationBrightConfig:{config.ToJsonString()}").ConfigureAwait(false));
    }

    private async Task RefreshActiveMaxVolumeAsync()
    {
        string ip = _deviceIp;
        if (string.IsNullOrEmpty(ip)) return;
        var statusEx = await SendToDeviceAsync(ip, "getStatusEx").ConfigureAwait(false);
        if (ip == _deviceIp && int.TryParse(JsonField(statusEx, "max_volume"), out int max) && max is >= 1 and <= 100)
            _activeMaxVolume = max;
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

    private static SubwooferInfo? ParseSubwoofer(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cross", out _)) return null;
            double Number(string name) =>
                root.TryGetProperty(name, out var p) && double.TryParse(p.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
            return new SubwooferInfo(Number("status") == 1, Number("plugged") == 1, Number("cross"),
                Number("phase"), Number("level"), Number("main_filter") == 1);
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
            return new ScreenConfig(json, Value("disable") == 0, Value("auto_sense_enable") == 1, bright.TryGetInt32(out int b) ? b : 0);
        }
        catch (JsonException) { return null; }
    }
}
