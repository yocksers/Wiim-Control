using System.Globalization;
using System.Text.Json;

namespace WiimControl;

sealed record OutputLevelChoice(int Index, int Millivolts);

sealed record OutputLevel(string Mode, bool NewCommands, List<OutputLevelChoice> Choices, int? Current);

sealed partial class WiimController
{
    private const string LineOutMode = "AUDIO_OUTPUT_AUX_MODE";

    internal static string OutputLevelName(string mode) => mode == HeadphoneOutputMode ? "Headphone level" : "Line out level";

    internal static string MillivoltsText(int millivolts) =>
        millivolts >= 1000 ? (millivolts / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " Vrms" : $"{millivolts} mVrms";

    internal static int? ParseMillivolts(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        bool millivolts = text.EndsWith("mV", StringComparison.OrdinalIgnoreCase);
        string number = millivolts ? text[..^2] : text.EndsWith('V') || text.EndsWith('v') ? text[..^1] : text;
        if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || amount <= 0) return null;
        return (int)Math.Round(millivolts ? amount : amount * 1000);
    }

    internal static List<OutputLevelChoice> ParseLevelChoices(JsonElement settings)
    {
        var choices = new List<OutputLevelChoice>();
        if (settings.ValueKind != JsonValueKind.Array) return choices;
        foreach (var setting in settings.EnumerateArray())
            if (int.TryParse(Prop(setting, "index").ToString(), out int index) && ParseMillivolts(Prop(setting, "value").ToString()) is int mv &&
                choices.All(c => c.Index != index))
                choices.Add(new OutputLevelChoice(index, mv));
        return choices.OrderByDescending(c => c.Millivolts).ToList();
    }

    internal static List<(string Mode, List<OutputLevelChoice> Choices)> ParseNewLevelLists(string? reply)
    {
        var lists = new List<(string, List<OutputLevelChoice>)>();
        try
        {
            using var doc = JsonDocument.Parse(reply ?? string.Empty);
            if (Prop(doc.RootElement, "voltage") is not { ValueKind: JsonValueKind.Array } cards) return lists;
            foreach (var card in cards.EnumerateArray())
                if (Prop(card, "mode").ToString() is { Length: > 0 } mode && ParseLevelChoices(Prop(card, "setting")) is { Count: > 1 } choices)
                    lists.Add((mode, choices));
        }
        catch (JsonException) { }
        return lists;
    }

    internal static List<OutputLevelChoice> ParseOldLevelList(string? reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply ?? string.Empty);
            return ParseLevelChoices(doc.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<List<OutputLevel>> GetOutputLevelsAsync(string ip)
    {
        var lists = ParseNewLevelLists(await SendToDeviceAsync(ip, "getAudioOutputVrmsSupportList:{\"ver\":\"1.1\"}").ConfigureAwait(false));
        if (lists.Count > 0)
        {
            var currents = await Task.WhenAll(lists.Select(l => SendToDeviceAsync(ip, $"getAudioOutputVrms:{l.Mode}"))).ConfigureAwait(false);
            return lists.Select((l, i) => new OutputLevel(l.Mode, true, l.Choices, int.TryParse(currents[i]?.Trim(), out int c) ? c : null)).ToList();
        }
        var old = ParseOldLevelList(await SendToDeviceAsync(ip, "getAuxVoltageSupportList").ConfigureAwait(false));
        if (old.Count < 2) return [];
        string? current = await SendToDeviceAsync(ip, "getOutputVoltage").ConfigureAwait(false);
        return [new OutputLevel(LineOutMode, false, old, int.TryParse(current?.Trim(), out int index) ? index : null)];
    }

    internal async Task<bool> SetOutputLevelAsync(string ip, OutputLevel level, int index) =>
        CommandSucceeded(await SendToDeviceAsync(ip, level.NewCommands
            ? $"setAudioOutputVrms:{{\"mode\": \"{level.Mode}\",\"type\": \"{index}\"}}"
            : $"setOutputVoltage:{index}").ConfigureAwait(false));
}
