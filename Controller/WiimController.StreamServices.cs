using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed partial class WiimController
{
    internal const string SqueezeliteService = "Squeezelite";
    internal const string RoonReadyService = "Roon";

    internal static bool? ParseStreamServiceActive(string? json, string service)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["streamServices"] is not JsonArray list) return null;
            var entry = list.OfType<JsonObject>().FirstOrDefault(s => s["id"] is JsonValue id && id.TryGetValue(out string? name) && name == service);
            if (entry?["configurable"] is not JsonValue c || !c.TryGetValue(out bool configurable) || !configurable) return null;
            return entry["active"] is JsonValue a && a.TryGetValue(out bool active) && active;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    internal async Task<(bool? Squeezelite, bool? RoonReady, bool Answered)> GetStreamServiceStatesAsync(string ip)
    {
        var json = await SendToDeviceAsync(ip, "streamServicesCapability").ConfigureAwait(false);
        return (ParseStreamServiceActive(json, SqueezeliteService), ParseStreamServiceActive(json, RoonReadyService), json != null);
    }

    internal async Task<bool?> GetStreamServiceActiveAsync(string ip, string service) =>
        ParseStreamServiceActive(await SendToDeviceAsync(ip, "streamServicesCapability").ConfigureAwait(false), service);

    internal async Task<bool> SetStreamServiceActiveAsync(string ip, string service, bool active) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"SetStreamServicesCapability:{(active ? "Active" : "Deactive")}:{service}").ConfigureAwait(false));
}
