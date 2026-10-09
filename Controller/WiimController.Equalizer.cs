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
        if (pluginUri != DefaultEqPlugin) return false;
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

    internal const string PeqPlugin = "http://moddevices.com/plugins/caps/EqNp";
    internal const int PeqBandCount = 10;

    internal static readonly PeqBand[] PeqDefaults =
        [.. new[] { 31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }.Select(f => new PeqBand(PeqFilter.Peak, f, 0.25, 0))];

    private Task<string?> SendEqJsonAsync(string command, object payload) =>
        SendEqCommandAsync($"{command}:{JsonSerializer.Serialize(payload, EqJson)}");

    internal async Task<bool> SetEqTypeAsync(string source, string pluginUri)
    {
        if (source.Length == 0) return pluginUri == DefaultEqPlugin && await SetEqEnabledAsync(true).ConfigureAwait(false);
        return CommandSucceeded(await SendEqJsonAsync("EQChangeSourceFX", new { source_name = source, pluginURI = pluginUri }).ConfigureAwait(false));
    }

    internal async Task<bool> TurnEqOffAsync(string source, string pluginUri)
    {
        if (source.Length == 0) return await SetEqEnabledAsync(false).ConfigureAwait(false);
        return CommandSucceeded(await SendEqJsonAsync("EQSourceOff", new { source_name = source, pluginURI = pluginUri }).ConfigureAwait(false));
    }

    internal async Task<PeqState?> GetPeqStateAsync(string source)
    {
        if (source.Length == 0) return null;
        var json = await SendEqJsonAsync("EQGetLV2SourceBandEx", new { source_name = source, pluginURI = PeqPlugin }).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !(root.TryGetProperty("pluginURI", out var plugin) && plugin.GetString() == PeqPlugin)) return null;

            static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
            PeqBand[]? Bands(string name) =>
                root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? ParsePeqBands(array) : null;

            bool split = Str(root, "channelMode") == "L/R";
            var stereo = Bands("EQBand");
            var left = Bands("EQBandL") ?? stereo;
            var right = Bands("EQBandR") ?? stereo;
            stereo ??= left;
            if (stereo == null || left == null || right == null) return null;
            return new PeqState(Str(root, "EQStat") == "On", Str(root, "Name"), split, stereo, left, right);
        }
        catch (Exception) { return null; }
    }

    private static PeqBand[] ParsePeqBands(JsonElement array)
    {
        var values = new Dictionary<string, double>();
        foreach (var b in array.EnumerateArray())
        {
            if (!b.TryGetProperty("param_name", out var name) || !b.TryGetProperty("value", out var v)) continue;
            values[name.GetString() ?? string.Empty] = ReadNumber(v);
        }

        var bands = new PeqBand[PeqBandCount];
        for (int i = 0; i < PeqBandCount; i++)
        {
            char letter = (char)('a' + i);
            var d = PeqDefaults[i];
            double Value(string param, double fallback) => values.TryGetValue($"{letter}_{param}", out var v) ? v : fallback;
            int mode = (int)Math.Round(Value("mode", (int)d.Filter));
            bands[i] = new PeqBand(Enum.IsDefined((PeqFilter)mode) ? (PeqFilter)mode : PeqFilter.Peak,
                Value("freq", d.Frequency), Value("q", d.Q), Value("gain", d.Gain));
        }
        return bands;
    }

    internal static string PeqChannelKey(int channel) => channel switch { 1 => "EQBandL", 2 => "EQBandR", _ => "EQBand" };

    internal async Task<bool> SetPeqBandsAsync(string source, bool split, int channel, IEnumerable<(int Index, PeqBand Band)> bands)
    {
        var parameters = new List<object>();
        foreach (var (index, band) in bands)
        {
            char letter = (char)('a' + index);
            parameters.Add(new { param_name = $"{letter}_mode", value = (double)(int)band.Filter });
            parameters.Add(new { param_name = $"{letter}_freq", value = band.Frequency });
            parameters.Add(new { param_name = $"{letter}_q", value = band.Q });
            parameters.Add(new { param_name = $"{letter}_gain", value = band.Gain });
        }
        var payload = new Dictionary<string, object>
        {
            ["source_name"] = source,
            ["pluginURI"] = PeqPlugin,
            ["channelMode"] = split ? "L/R" : "Stereo",
            [PeqChannelKey(channel)] = parameters
        };
        return CommandSucceeded(await SendEqJsonAsync("EQSetLV2SourceBand", payload).ConfigureAwait(false));
    }

    internal async Task<bool> SetPeqSplitAsync(string source, bool split) =>
        CommandSucceeded(await SendEqJsonAsync("EQSetChannelMode",
            new { source_name = source, pluginURI = PeqPlugin, channelMode = split ? "L/R" : "Stereo" }).ConfigureAwait(false));

    internal async Task<bool> LoadSourcePresetAsync(string source, string pluginUri, string name) =>
        CommandSucceeded(await SendEqJsonAsync("EQv2SourceLoad", new { source_name = source, pluginURI = pluginUri, Name = name }).ConfigureAwait(false));

    internal async Task<List<EqSourceMode>?> GetEqSourceModesAsync()
    {
        var json = await SendEqCommandAsync("EQGetSourceModes").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var modes = new List<EqSourceMode>();
            foreach (var m in doc.RootElement.EnumerateArray())
            {
                string source = m.TryGetProperty("source_name", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                if (source.Length == 0 || modes.Any(x => x.Source == source)) continue;
                string plugin = m.TryGetProperty("pluginURI", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                modes.Add(new EqSourceMode(source, plugin, m.TryGetProperty("EQStat", out var stat) && stat.GetString() == "On"));
            }
            return modes.Count > 0 ? modes : null;
        }
        catch (Exception) { return null; }
    }

    internal async Task<EqState?> GetGeqStateAsync(string source)
    {
        var json = await SendEqJsonAsync("EQGetLV2SourceBandEx", new { source_name = source, pluginURI = DefaultEqPlugin }).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !(root.TryGetProperty("pluginURI", out var plugin) && plugin.GetString() == DefaultEqPlugin)) return null;
            JsonElement array = default;
            if (!new[] { "EQBand", "EQBandL" }.Any(name => root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array))
                return null;

            var bands = new List<EqBand>();
            foreach (var b in array.EnumerateArray())
            {
                if (!b.TryGetProperty("param_name", out var name) || !b.TryGetProperty("value", out var v)) continue;
                int index = b.TryGetProperty("index", out var i) && i.TryGetInt32(out int n) ? n : bands.Count;
                bands.Add(new EqBand(index, name.GetString() ?? string.Empty, (int)Math.Round(50 + ReadNumber(v) * 4)));
            }
            static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
            return new EqState(Str(root, "EQStat") == "On", Str(root, "Name"), source, DefaultEqPlugin, bands);
        }
        catch (Exception) { return null; }
    }

    internal async Task<bool> SetGeqBandsAsync(string source, IEnumerable<EqBand> bands)
    {
        var payload = new
        {
            source_name = source,
            pluginURI = DefaultEqPlugin,
            channelMode = "Stereo",
            EQBand = bands.Select(b => new { param_name = b.ParamName, value = (b.Value - 50) / 4.0 })
        };
        return CommandSucceeded(await SendEqJsonAsync("EQSetLV2SourceBand", payload).ConfigureAwait(false));
    }

    private static double ReadNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? double.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : value.GetDouble();
}
