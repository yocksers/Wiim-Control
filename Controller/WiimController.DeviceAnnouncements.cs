using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly TimeSpan OwnChangeQuiet = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<string, DateTime> _ownEqChanges = new();

    private void NoteOwnEqChange(string ip, string command)
    {
        if (command.StartsWith("EQGet", StringComparison.Ordinal) || command.StartsWith("EQv2Get", StringComparison.Ordinal)) return;
        _ownEqChanges[ip] = DateTime.UtcNow;
    }

    private bool ChangedEqJustNow(string ip) => _ownEqChanges.TryGetValue(ip, out var when) && DateTime.UtcNow - when < OwnChangeQuiet;

    private void OnCommonEvent(string ip, string json)
    {
        string category;
        JsonElement body;
        try
        {
            using var doc = JsonDocument.Parse(json);
            category = Prop(doc.RootElement, "category").ToString();
            var found = Prop(doc.RootElement, "body");
            body = found.ValueKind == JsonValueKind.Undefined ? default : found.Clone();
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
