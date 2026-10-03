using System.Text.Encodings.Web;
using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private async Task<string?> SendEqCommandAsync(string command)
    {
        try
        {
            var body = await _http.GetStringAsync(
                $"https://{_deviceIp}/httpapi.asp?command={Uri.EscapeDataString(command)}").ConfigureAwait(false);
            OnWiimRequestSucceeded();
            return body;
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
            return null;
        }
    }

    private static bool CommandSucceeded(string? body) =>
        body != null &&
        !body.Contains("Failed", StringComparison.OrdinalIgnoreCase) &&
        !body.Contains("unknown command", StringComparison.OrdinalIgnoreCase);

    internal const string DefaultEqPlugin = "http://moddevices.com/plugins/caps/Eq10HP";

    private static readonly JsonSerializerOptions EqJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static List<string> StringArray(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
            : [];

    internal async Task<EqPresetList> GetEqPresetListAsync(string pluginUri)
    {
        var json = await SendEqCommandAsync($"EQv2GetList:{pluginUri}").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("preset", out var preset))
                return new EqPresetList(
                    root.TryGetProperty("custom", out var custom) ? StringArray(custom) : [],
                    StringArray(preset));
        }
        catch (JsonException) { }

        json = await SendEqCommandAsync("EQGetList").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            return new EqPresetList([], StringArray(doc.RootElement));
        }
        catch (JsonException) { return new EqPresetList([], []); }
    }

    private async Task<bool> HasCustomEqPresetAsync(string name, string pluginUri) =>
        (await GetEqPresetListAsync(pluginUri).ConfigureAwait(false)).Custom.Contains(name);

    internal async Task<bool> SaveEqPresetAsync(string name, string source, string pluginUri, bool replacing)
    {
        if (source.Length > 0)
        {
            var payload = JsonSerializer.Serialize(new { source_name = source, pluginURI = pluginUri, Name = name }, EqJson);
            bool saved = CommandSucceeded(await SendEqCommandAsync($"EQSourceSave:{payload}").ConfigureAwait(false));
            if (saved && (replacing || await HasCustomEqPresetAsync(name, pluginUri).ConfigureAwait(false))) return true;
        }
        bool legacySaved = CommandSucceeded(await SendEqCommandAsync($"EQSave:{name}").ConfigureAwait(false));
        return legacySaved && (replacing || await HasCustomEqPresetAsync(name, pluginUri).ConfigureAwait(false));
    }

    internal async Task<bool> RenameEqPresetAsync(string name, string newName, string pluginUri)
    {
        var payload = JsonSerializer.Serialize(new { pluginURI = pluginUri, Name = name, newName }, EqJson);
        if (!CommandSucceeded(await SendEqCommandAsync($"EQv2Rename:{payload}").ConfigureAwait(false))) return false;
        var list = await GetEqPresetListAsync(pluginUri).ConfigureAwait(false);
        return list.Custom.Contains(newName) && !list.Custom.Contains(name);
    }

    internal async Task<bool> DeleteEqPresetAsync(string name, string pluginUri)
    {
        var payload = JsonSerializer.Serialize(new { pluginURI = pluginUri, Name = name }, EqJson);
        if (!CommandSucceeded(await SendEqCommandAsync($"EQv2Delete:{payload}").ConfigureAwait(false))) return false;
        return !await HasCustomEqPresetAsync(name, pluginUri).ConfigureAwait(false);
    }

    internal async Task<EqState?> GetEqStateAsync()
    {
        var json = await SendEqCommandAsync("EQGetBand").ConfigureAwait(false);
        if (json == null) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("EQBand", out var bandsProp) ||
                bandsProp.ValueKind != System.Text.Json.JsonValueKind.Array) return null;

            static string Str(System.Text.Json.JsonElement e, string name) =>
                e.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;

            var bands = new List<EqBand>();
            foreach (var b in bandsProp.EnumerateArray())
            {
                var v = b.GetProperty("value");
                double value = v.ValueKind == System.Text.Json.JsonValueKind.String
                    ? double.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : v.GetDouble();
                bands.Add(new EqBand(b.GetProperty("index").GetInt32(), Str(b, "param_name"), (int)Math.Round(value)));
            }
            string plugin = Str(root, "pluginURI");
            return new EqState(Str(root, "EQStat") == "On", Str(root, "Name"), Str(root, "source_name"),
                plugin.Length > 0 ? plugin : DefaultEqPlugin, bands);
        }
        catch (Exception) { return null; }
    }

    internal async Task<bool> SetEqEnabledAsync(bool enabled) =>
        CommandSucceeded(await SendEqCommandAsync(enabled ? "EQOn" : "EQOff").ConfigureAwait(false));

    internal async Task<bool> LoadEqPresetAsync(string name) =>
        CommandSucceeded(await SendEqCommandAsync($"EQLoad:{name}").ConfigureAwait(false));

    internal async Task<bool> SetEqBandsAsync(IEnumerable<EqBand> bands)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            EQBand = bands.Select(b => new { index = b.Index, param_name = b.ParamName, value = b.Value })
        });
        return CommandSucceeded(await SendEqCommandAsync($"EQSetBand:{payload}").ConfigureAwait(false));
    }
}
