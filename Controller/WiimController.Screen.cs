using System.Text.Json.Nodes;

namespace WiimControl;

sealed partial class WiimController
{
    internal static readonly string[] GlobalScreenViews = ["clock_view", "standby_view"];

    private static string? UiInputKey(string? input) => input?.ToLowerInvariant() switch
    {
        "wifi" => "wifi",
        "bluetooth" => "bluetooth",
        "line-in" => "lineIn",
        "optical" => "optical",
        "hdmi" => "hdmi",
        "phono" => "phono",
        "co-axial" or "coaxial" => "coaxial",
        _ => null
    };

    private static int? UiInt(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out int i) ? i : null;

    internal async Task<ScreenUi?> GetScreenUiAsync(string ip)
    {
        var configTask = SendToDeviceAsync(ip, "get_ui_config");
        var statusTask = SendToDeviceAsync(ip, "getPlayerStatus");
        await Task.WhenAll(configTask, statusTask).ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(configTask.Result ?? string.Empty) is not JsonObject root || root["view_type"] is not JsonObject viewType) return null;
            int.TryParse(JsonField(statusTask.Result, "mode"), out int mode);
            string? input = InputOfMode(mode);
            string? inputKey = UiInputKey(input);
            var inputConfig = inputKey != null ? root["input_config"]?[inputKey] as JsonObject : null;
            if (inputConfig == null) inputKey = input = null;
            var features = root["view_features"] as JsonObject;
            var supported = inputConfig?["supported_views"] as JsonObject;

            var views = new Dictionary<string, ScreenView>();
            foreach (var (key, node) in viewType)
            {
                if (UiInt(node) is not int value) continue;
                int? mask = (GlobalScreenViews.Contains(key) ? null : UiInt(supported?[key])) ?? UiInt(features?[key]);
                var choices = mask is int m ? Enumerable.Range(0, 31).Where(b => ((m >> b) & 1) == 1).ToList() : [];
                if (!choices.Contains(value)) choices.Add(value);
                choices.Sort();
                views[key] = new ScreenView(key, value, choices);
            }

            var behavior = root["behavior"] as JsonObject;
            bool? Flag(string name) => UiInt(behavior?[name]) is int v ? v != 0 : null;
            return new ScreenUi(inputKey, input, Flag("clock_24_hours"), Flag("standby_turn_off_screen"),
                UiInt(behavior?["playing_default_page"]), views);
        }
        catch (Exception) { return null; }
    }

    private async Task<bool> SetUiConfigAsync(string ip, JsonObject payload) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"set_ui_config:{payload.ToJsonString()}").ConfigureAwait(false));

    private static JsonObject PreferConfig(string inputKey, IEnumerable<(string Key, int Value)> values)
    {
        var prefer = new JsonObject();
        foreach (var (key, value) in values) prefer[key] = value;
        return new JsonObject { [inputKey] = new JsonObject { ["prefer_config"] = prefer } };
    }

    internal Task<bool> SetScreenViewAsync(string ip, string? inputKey, params (string Key, int Value)[] values)
    {
        var viewType = new JsonObject();
        foreach (var (key, value) in values) viewType[key] = value;
        var payload = new JsonObject { ["view_type"] = viewType };
        var perInput = values.Where(v => !GlobalScreenViews.Contains(v.Key)).ToList();
        if (inputKey != null && perInput.Count > 0) payload["input_config"] = PreferConfig(inputKey, perInput);
        return SetUiConfigAsync(ip, payload);
    }

    internal Task<bool> SetScreenBehaviorAsync(string ip, string? inputKey, string key, int value, bool perInput)
    {
        var payload = new JsonObject { ["behavior"] = new JsonObject { [key] = value } };
        if (perInput && inputKey != null) payload["input_config"] = PreferConfig(inputKey, [(key, value)]);
        return SetUiConfigAsync(ip, payload);
    }
}
