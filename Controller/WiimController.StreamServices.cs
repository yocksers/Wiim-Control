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

    internal async Task<bool?> GetStreamServiceActiveAsync(string ip, string service) =>
        ParseStreamServiceActive(await SendToDeviceAsync(ip, "streamServicesCapability").ConfigureAwait(false), service);

    internal async Task<bool> SetStreamServiceActiveAsync(string ip, string service, bool active) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"SetStreamServicesCapability:{(active ? "Active" : "Deactive")}:{service}").ConfigureAwait(false));

    internal static readonly (string Id, string Name)[] KnownStreamServices =
    [
        ("SpotifyConnect", "Spotify Connect"), ("TidalConnect", "TIDAL Connect"), ("QobuzConnect", "Qobuz Connect"),
        (RoonReadyService, "Roon Ready"), (SqueezeliteService, "Squeezelite"), ("Soundtrack", "Soundtrack")
    ];

    private static readonly string[] HiddenStreamServices = ["AudioCast", "AVS_MRM"];

    internal static List<(string Id, string Name, bool Active)>? ParseStreamServices(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["streamServices"] is not JsonArray list) return null;
            var found = new List<(string Id, bool Active)>();
            foreach (var entry in list.OfType<JsonObject>())
            {
                if (entry["id"] is not JsonValue idValue || !idValue.TryGetValue(out string? id) || id is not { Length: > 0 }) continue;
                if (entry["configurable"] is not JsonValue c || !c.TryGetValue(out bool configurable) || !configurable || HiddenStreamServices.Contains(id)) continue;
                found.Add((id, entry["active"] is JsonValue a && a.TryGetValue(out bool active) && active));
            }
            int Rank(string id) => Array.FindIndex(KnownStreamServices, k => k.Id == id) is >= 0 and var i ? i : KnownStreamServices.Length;
            return found.OrderBy(f => Rank(f.Id))
                .Select(f => (f.Id, KnownStreamServices.FirstOrDefault(k => k.Id == f.Id).Name ?? f.Id, f.Active)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    internal async Task<List<(string Id, string Name, bool Active)>?> GetStreamServicesAsync(string ip) =>
        ParseStreamServices(await SendToDeviceAsync(ip, "streamServicesCapability").ConfigureAwait(false));

    internal static bool? ParseGoogleCast(string? json) =>
        JsonField(json, "cast_registed") is { } value ? value == "1" : null;

    internal async Task<bool?> GetGoogleCastAsync(string ip) => ParseGoogleCast(await SendToDeviceAsync(ip, "Cast:GetRegisterStatus").ConfigureAwait(false));

    internal async Task<bool> SetGoogleCastAsync(string ip, bool on) =>
        CommandSucceeded(await SendToDeviceAsync(ip, on ? "Cast:Register" : "Cast:Deregister").ConfigureAwait(false));
}
