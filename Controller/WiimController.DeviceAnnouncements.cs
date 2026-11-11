using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly TimeSpan OwnChangeQuiet = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<string, DateTime> _ownEqChanges = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _inputSignals = new();

    private void NoteOwnEqChange(string ip, string command)
    {
        if (command.StartsWith("EQGet", StringComparison.Ordinal) || command.StartsWith("EQv2Get", StringComparison.Ordinal)) return;
        _ownEqChanges[ip] = DateTime.UtcNow;
    }

    private bool ChangedEqJustNow(string ip) => _ownEqChanges.TryGetValue(ip, out var when) && DateTime.UtcNow - when < OwnChangeQuiet;

    internal string? InputSignal(string ip, string input) =>
        _inputSignals.TryGetValue(ip, out var signals) ? signals.FirstOrDefault(s => IsSameInput(s.Key, input)).Value : null;

    internal async Task ReadInputSignalsAsync(string ip)
    {
        if (ParseInputSignals(await SendToDeviceAsync(ip, "getAudioInputStreamState").ConfigureAwait(false)) is { } signals)
            _inputSignals[ip] = signals;
    }

    internal static Dictionary<string, string>? ParseInputSignals(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : Prop(doc.RootElement, "total");
            if (list.ValueKind != JsonValueKind.Array) return null;
            var signals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in list.EnumerateArray())
                if (Prop(input, "modeName").ToString() is { Length: > 0 } mode && Prop(input, "contentEnable").ToString() == "1")
                    signals[mode] = Prop(input, "contentStatus").ToString();
            return signals;
        }
        catch (JsonException) { return null; }
    }

    private void OnCommonEvent(string ip, string json)
    {
        string category;
        JsonElement body;
        try
        {
            using var doc = JsonDocument.Parse(json);
            category = Prop(doc.RootElement, "category").ToString();
            body = Prop(doc.RootElement, "body").Clone();
        }
        catch (JsonException) { return; }
        switch (category)
        {
            case "equaliser":
                if (!ChangedEqJustNow(ip)) Settle($"{ip}|eq", () => _settingsWindow?.OnEqualizerChanged(ip));
                break;
            case "hardware" when Prop(body, "output_mode").ValueKind == JsonValueKind.String:
                Settle($"{ip}|output", () => _settingsWindow?.OnOutputChanged(ip));
                break;
            case "lpmrm":
                Settle("groups", () => _settingsWindow?.OnGroupsChanged());
                break;
            case "screen_config":
                Settle($"{ip}|screen", () => _settingsWindow?.OnScreenChanged(ip));
                break;
            case "audio_input_signal_changed":
                if (ParseInputSignals(body.GetRawText()) is { } signals)
                {
                    _inputSignals[ip] = signals;
                    _settingsWindow?.OnInputSignalChanged(ip);
                }
                break;
        }
    }

    private void Settle(string key, Action action)
    {
        if (!_eventRefreshPending.Add(key)) return;
        DispatcherTimer.RunOnce(() =>
        {
            _eventRefreshPending.Remove(key);
            action();
        }, EventRefreshDelay);
    }
}
