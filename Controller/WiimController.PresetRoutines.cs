using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed record RoutineOutput(string Mode, string HexName, string Title);

sealed record RoutineEq(string Name, string PluginUri, string Title);

sealed record PresetRoutineEditor(RoutineContext Context, int Number, JsonObject? Routine, bool MusicKey, string KeyName,
    List<string> Inputs, List<RoutineOutput> Outputs, List<RoutineEq> Eqs, int? ScreenDisable);

sealed partial class WiimController
{
    internal static readonly Version PresetTypeVersion = new(1, 2);
    internal static readonly Version SubwooferStepVersion = new(1, 1);
    private static readonly string[] ScreenRoutineProjects = ["WiiM_Amp_Ultra", "WiiM_Ultra"];
    private static readonly string[] SkippedRoutineOutputs = ["BT", "BLUETOOTH", "DLNA", "AIRPLAY"];

    private async Task<List<JsonObject>?> GetPresetRoutineObjectsAsync(string ip, Version version)
    {
        string command = version >= PresetTypeVersion ? "getAllRoutines:preset" : "getAllRoutines";
        try
        {
            return (JsonNode.Parse(await SendToDeviceAsync(ip, command).ConfigureAwait(false) ?? string.Empty)?["routines"] as JsonArray)
                ?.OfType<JsonObject>().Where(r => r["type"]?.ToString() is null or "preset").ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    private async Task<EqPresetList?> GetEqListV2Async(string ip, string pluginUri)
    {
        try
        {
            using var doc = JsonDocument.Parse(await SendEqCommandAsync(ip, $"EQv2GetList:{pluginUri}").ConfigureAwait(false) ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("preset", out var preset)) return null;
            return new EqPresetList(root.TryGetProperty("custom", out var custom) ? StringArray(custom) : [], StringArray(preset));
        }
        catch (JsonException) { return null; }
    }

    internal static List<RoutineOutput> ParseRoutineOutputs(string? json)
    {
        var outputs = new List<RoutineOutput>();
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonArray cards) return outputs;
            foreach (var card in cards.OfType<JsonObject>())
            {
                string mode = card["mode"]?.ToString() ?? string.Empty;
                if (mode.Length == 0 || SkippedRoutineOutputs.Any(s => mode.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(card["soundCard"]?["devName"]?.ToString() ?? string.Empty));
                if (outputs.Any(o => o.Mode == mode && o.HexName == hex)) continue;
                outputs.Add(new RoutineOutput(mode, hex, RoutineOutputName(mode, hex)));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return outputs;
    }

    internal async Task<PresetRoutineEditor?> GetPresetRoutineEditorAsync(string ip, int number)
    {
        if (await GetRoutineVersionAsync(ip).ConfigureAwait(false) is not { } version) return null;
        var context = GetRoutineContextAsync(ip, version);
        var keys = GetKeyListAsync(ip);
        var routines = GetPresetRoutineObjectsAsync(ip, version);
        var inputs = GetInputsAsync(ip);
        var cards = SendToDeviceAsync(ip, "getSoundCardModeSupportList");
        var graphic = GetEqPresetListAsync(ip, DefaultEqPlugin);
        var parametric = GetEqListV2Async(ip, PeqPlugin);
        var screen = SendToDeviceAsync(ip, "getLightOperationBrightConfig");
        await Task.WhenAll(context, keys, routines, inputs, cards, graphic, parametric, screen).ConfigureAwait(false);
        if (context.Result is not { } ctx || keys.Result is not { } list || KeyContent(list, number) is not { } content || routines.Result == null) return null;

        string? routineId = KeyValue(content, "RoutineId")?.Trim();
        var routine = routineId is { Length: > 0 } && !routineId.Equals("Empty", StringComparison.OrdinalIgnoreCase)
            ? routines.Result.FirstOrDefault(r => r["id"]?.ToString() == routineId)
            : null;
        var eqs = graphic.Result.Custom.Concat(graphic.Result.BuiltIn).Distinct().Select(n => new RoutineEq(n, DefaultEqPlugin, n)).ToList();
        if (parametric.Result is { } peq)
            eqs.AddRange(peq.Custom.Concat(peq.BuiltIn).Distinct().Select(n => new RoutineEq(n, PeqPlugin, $"{n} (PEQ)")));
        bool hasScreen = version >= PresetTypeVersion && ScreenRoutineProjects.Any(p => ctx.Project.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        int? screenDisable = hasScreen && int.TryParse(JsonField(screen.Result, "disable"), out int disable) ? disable : null;
        var inputModes = (inputs.Result ?? []).Where(m => !m.Contains("usb", StringComparison.OrdinalIgnoreCase)).ToList();
        return new PresetRoutineEditor(ctx, number, routine, IsMusicKey(content), Clean(KeyValue(content, "Name")) ?? string.Empty,
            inputModes, ParseRoutineOutputs(cards.Result), eqs, screenDisable);
    }

    internal static int LoopModeOf(bool repeat, bool shuffle) => (shuffle, repeat) switch
    {
        (false, true) => 0,
        (true, false) => 3,
        (true, true) => 2,
        _ => 4
    };

    internal static string WithRoutineId(string content, string routineId)
    {
        var existing = new Regex("<RoutineId>.*?</RoutineId>", RegexOptions.Singleline);
        string tag = $"<RoutineId>{routineId}</RoutineId>";
        string updated = existing.IsMatch(content) ? existing.Replace(content, tag, 1) : tag + content;
        if (!updated.Contains("<Loop>", StringComparison.Ordinal)) updated += "<Loop>0</Loop>";
        if (!updated.Contains("<Shuffle>", StringComparison.Ordinal)) updated += "<Shuffle>0</Shuffle>";
        return updated;
    }

    internal async Task<bool> SavePresetRoutineAsync(string ip, PresetRoutineEditor editor, string name, IReadOnlyList<(string Type, JsonObject Payload)> steps)
    {
        var existing = editor.Routine;
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string id = existing?["id"]?.ToString() is { Length: > 0 } known
            ? known
            : Random.Shared.Next(int.MaxValue).ToString("x", CultureInfo.InvariantCulture).PadLeft(16, '0');

        var routine = new JsonObject { ["id"] = id, ["name"] = name };
        if ((existing == null ? editor.Number - 1 : NodeInt(existing["index"])) is int index) routine["index"] = index;
        if ((existing == null ? now : existing["createDate"]?.ToString()) is { } created) routine["createDate"] = created;
        if (existing != null) routine["updateDate"] = now;
        if (editor.Context.Version >= PresetTypeVersion) routine["type"] = "preset";
        if (editor.Context.Version >= EnabledSceneVersion && NodeInt(existing?["enabled"]) is int enabled) routine["enabled"] = enabled;
        if (steps.Count > 0)
            routine["steps"] = new JsonArray(steps.Select(s => (JsonNode)new JsonObject { ["type"] = s.Type, ["payload"] = s.Payload.DeepClone() }).ToArray());

        string command = (existing == null ? "createRoutine:" : "updateRoutine:") + routine.ToJsonString(EqJson);
        if (!CommandSucceeded(await PostCommandAsync(ip, command).ConfigureAwait(false))) return false;
        var check = await GetPresetRoutineObjectsAsync(ip, editor.Context.Version).ConfigureAwait(false);
        if (check?.Any(r => r["id"]?.ToString() == id && r["name"]?.ToString() == name) != true) return false;

        if (await GetKeyListAsync(ip).ConfigureAwait(false) is { } list && KeyValue(KeyContent(list, editor.Number), "RoutineId")?.Trim() == id) return true;
        return await UpdateKeyAsync(ip, editor.Number, content => WithRoutineId(content, id), saved => KeyValue(saved, "RoutineId")?.Trim() == id)
            .ConfigureAwait(false);
    }
}
