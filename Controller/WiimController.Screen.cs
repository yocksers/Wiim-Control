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
        "auxin" => "auxIn",
        _ => null
    };

    internal static string InputOfUiKey(string key) => key switch
    {
        "lineIn" => "line-in",
        "hdmi" => "HDMI",
        "coaxial" => "co-axial",
        "auxIn" => "AUX in",
        _ => key
    };

    private static int? UiInt(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out int i) ? i : null;

    internal async Task<ScreenUi?> GetScreenUiAsync(string ip, string? chosenInputKey = null)
    {
        var configTask = SendToDeviceAsync(ip, "get_ui_config");
        var statusTask = SendToDeviceAsync(ip, "getPlayerStatus");
        var inputsTask = GetInputVisibilityAsync(ip);
        await Task.WhenAll(configTask, statusTask, inputsTask).ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(configTask.Result ?? string.Empty) is not JsonObject root || root["view_type"] is not JsonObject viewType) return null;
            int.TryParse(JsonField(statusTask.Result, "mode"), out int mode);
            var inputConfigs = root["input_config"] as JsonObject;
            string? playingKey = UiInputKey(InputOfMode(mode));
            if (playingKey == "lineIn" && inputConfigs?["lineIn"] is not JsonObject && inputConfigs?["auxIn"] is JsonObject) playingKey = "auxIn";
            if (playingKey != null && inputConfigs?[playingKey] is not JsonObject) playingKey = null;
            var realInputs = inputsTask.Result?.Select(i => UiInputKey(i.Mode)).OfType<string>().ToHashSet();
            var inputKeys = inputConfigs?.Select(p => p.Key).Where(k => realInputs == null || realInputs.Contains(k) || k == playingKey).ToList() ?? [];
            string? inputKey = chosenInputKey != null && inputKeys.Contains(chosenInputKey) ? chosenInputKey : playingKey;
            var inputConfig = inputKey != null ? inputConfigs?[inputKey] as JsonObject : null;
            bool playing = inputKey == playingKey;
            var features = root["view_features"] as JsonObject;
            var supported = inputConfig?["supported_views"] as JsonObject;
            var prefer = inputConfig?["prefer_config"] as JsonObject;

            var views = new Dictionary<string, ScreenView>();
            foreach (var (key, node) in viewType)
            {
                bool global = GlobalScreenViews.Contains(key);
                if ((global || playing ? UiInt(node) : UiInt(prefer?[key]) ?? UiInt(node)) is not int value) continue;
                int? mask = (global ? null : UiInt(supported?[key])) ?? UiInt(features?[key]);
                var choices = mask is int m ? Enumerable.Range(0, 31).Where(b => ((m >> b) & 1) == 1).ToList() : [];
                if (!choices.Contains(value)) choices.Add(value);
                choices.Sort();
                views[key] = new ScreenView(key, value, choices);
            }

            var behavior = root["behavior"] as JsonObject;
            bool? Flag(string name) => UiInt(behavior?[name]) is int v ? v != 0 : null;
            int? startPage = playing ? UiInt(behavior?["playing_default_page"]) : UiInt(prefer?["playing_default_page"]) ?? UiInt(behavior?["playing_default_page"]);
            var clockColors = new Dictionary<int, string>();
            if (behavior?["clock_font"] is JsonArray fonts)
                foreach (var font in fonts.OfType<JsonObject>())
                    if (UiInt(font["clock_view"]) is int clockView && font["clock_color"]?.ToString() is { Length: 6 } color)
                        clockColors[clockView] = color;

            var extra = viewType["extra_info"] as JsonObject;
            return new ScreenUi(inputKey, inputKey != null ? InputOfUiKey(inputKey) : null, Flag("clock_24_hours"), Flag("standby_turn_off_screen"),
                startPage, views, inputKeys, playingKey,
                UiInt(prefer?["screen_on"]) is int screenOn ? screenOn != 0 : null, Flag("hidden_sub_cover_view_icon"), clockColors,
                UiInt(extra?["clock_view_bg"]), UiInt(extra?["clock_view_color"]));
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

    internal Task<bool> SetScreenViewAsync(string ip, ScreenUi ui, params (string Key, int Value)[] values)
    {
        bool playing = ui.InputKey == ui.PlayingInputKey;
        var viewType = new JsonObject();
        foreach (var (key, value) in values)
            if (playing || GlobalScreenViews.Contains(key)) viewType[key] = value;
        var payload = new JsonObject();
        if (viewType.Count > 0) payload["view_type"] = viewType;
        var perInput = values.Where(v => !GlobalScreenViews.Contains(v.Key)).ToList();
        if (ui.InputKey != null && perInput.Count > 0) payload["input_config"] = PreferConfig(ui.InputKey, perInput);
        return SetUiConfigAsync(ip, payload);
    }

    internal Task<bool> SetPlaybackScreenAsync(string ip, ScreenUi ui, int startPage, params (string Key, int Value)[] values)
    {
        bool playing = ui.InputKey == ui.PlayingInputKey;
        var payload = new JsonObject();
        if (playing)
        {
            var viewType = new JsonObject();
            foreach (var (key, value) in values) viewType[key] = value;
            payload["view_type"] = viewType;
            payload["behavior"] = new JsonObject { ["playing_default_page"] = startPage };
        }
        if (ui.InputKey != null) payload["input_config"] = PreferConfig(ui.InputKey, [.. values, ("playing_default_page", startPage)]);
        return payload.Count == 0 ? Task.FromResult(false) : SetUiConfigAsync(ip, payload);
    }

    internal Task<bool> SetSoundClockAsync(string ip, ScreenPreviews.SoundClock clock) => SetUiConfigAsync(ip, new JsonObject
    {
        ["view_type"] = new JsonObject
        {
            ["clock_view"] = clock.ClockView,
            ["extra_info"] = new JsonObject { ["clock_view_bg"] = clock.Background, ["clock_view_color"] = clock.Color }
        }
    });

    internal Task<bool> SetScreenBehaviorAsync(string ip, ScreenUi ui, string key, int value, bool perInput)
    {
        var payload = new JsonObject();
        if (!perInput || ui.InputKey == ui.PlayingInputKey) payload["behavior"] = new JsonObject { [key] = value };
        if (perInput && ui.InputKey != null) payload["input_config"] = PreferConfig(ui.InputKey, [(key, value)]);
        return payload.Count == 0 ? Task.FromResult(false) : SetUiConfigAsync(ip, payload);
    }

    internal Task<bool> SetClockColorAsync(string ip, ScreenUi ui, int clockView, string color)
    {
        var colors = new Dictionary<int, string>(ui.ClockColors ?? []) { [clockView] = color.ToLowerInvariant() };
        var fonts = new JsonArray(colors.OrderBy(c => c.Key)
            .Select(c => (JsonNode)new JsonObject { ["clock_view"] = c.Key, ["clock_color"] = c.Value }).ToArray());
        return SetUiConfigAsync(ip, new JsonObject { ["behavior"] = new JsonObject { ["clock_font"] = fonts } });
    }
}
