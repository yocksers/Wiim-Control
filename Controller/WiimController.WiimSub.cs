using System.Text.Json.Nodes;

namespace WiimControl;

sealed record PairedSub(string Uuid, string Name, bool Wired, bool Online);

sealed record SubCandidate(string Ip, string Uuid, string Name, string PairedTo);

sealed record DeviceIdentity(string Ip, string Uuid, string Name, string Project);

sealed partial class WiimController
{
    private const string WiredSubType = "local";
    private const string WirelessSubType = "wireless";
    private static readonly TimeSpan SubPairTimeout = TimeSpan.FromSeconds(40);

    internal static bool IsWiimSub(string? project) =>
        project is { Length: > 0 } && (project.Contains("WiiM_SUB", StringComparison.OrdinalIgnoreCase) ||
                                       project.Contains("WiiM_Sub_Pro", StringComparison.OrdinalIgnoreCase));

    internal static List<PairedSub>? ParsePairedSubs(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["subwoofer"] is not JsonArray list) return null;
            return list.OfType<JsonObject>()
                .Select(s => new PairedSub(Text(s["uuid"]), Text(s["name"]), Text(s["type"]) == WiredSubType, Text(s["online"]) == "1"))
                .Where(s => s.Uuid.Length > 0)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }

        static string Text(JsonNode? node) => node?.ToString() ?? string.Empty;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _subwooferSupport = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _highPassSupport = new();

    internal bool? SubwooferSupport(string ip) => _subwooferSupport.TryGetValue(ip, out bool supported) ? supported : null;

    internal bool? HighPassSupport(string ip) => _highPassSupport.TryGetValue(ip, out bool supported) ? supported : null;

    internal static bool HasHighPassCapability(string? acousticCapability)
    {
        try { return JsonNode.Parse(acousticCapability ?? string.Empty) is JsonObject root && root["MainProtect"] is JsonObject; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    internal static bool HasSubwooferCapability(string? acousticCapability)
    {
        try { return JsonNode.Parse(acousticCapability ?? string.Empty) is JsonObject root && root["SubLPF"] is JsonObject; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    internal async Task<bool?> CheckSubwooferSupportAsync(string ip)
    {
        if (_subwooferSupport.TryGetValue(ip, out bool known)) return known;
        var status = SendToDeviceAsync(ip, "getStatusEx");
        var capability = SendToDeviceAsync(ip, "GetAcousticCapability");
        var pairing = SendToDeviceAsync(ip, "multiroom:subwooferGetPairInfo");
        var highPass = SendToDeviceAsync(ip, "getMainProtect");
        await Task.WhenAll(status, capability, pairing, highPass).ConfigureAwait(false);
        if (status.Result == null) return null;
        string? project = JsonField(status.Result, "project");
        _highPassSupport[ip] = !IsWiimSub(project) && (ParseHighPassFilter(highPass.Result) != null || HasHighPassCapability(capability.Result));
        if (IsWiimSub(project)) return _subwooferSupport[ip] = false;
        bool supported = HasSubwooferOutput(project) || HasSubwooferCapability(capability.Result) || ParsePairedSubs(pairing.Result) != null;
        return _subwooferSupport[ip] = supported;
    }

    internal async Task<List<PairedSub>?> GetPairedSubsAsync(string ip) =>
        ParsePairedSubs(await SendToDeviceAsync(ip, "multiroom:subwooferGetPairInfo").ConfigureAwait(false));

    internal async Task<(List<SubCandidate> Subs, List<DeviceIdentity> Devices)> FindWiimSubsAsync()
    {
        var found = await TryDiscoverDevicesAsync().ConfigureAwait(false);
        var ips = found.Select(d => d.Ip).Concat(_knownDevices.Select(d => d.Ip)).Distinct().ToList();
        var identities = await Task.WhenAll(ips.Select(async ip =>
        {
            string? status = await SendToDeviceAsync(ip, "getStatusEx").ConfigureAwait(false);
            if (status == null) return null;
            return new
            {
                Device = new DeviceIdentity(ip, JsonField(status, "uuid") ?? string.Empty, JsonField(status, "DeviceName") ?? ip,
                    JsonField(status, "project") ?? string.Empty),
                PairedTo = JsonField(status, "sub_master_uuid") ?? string.Empty
            };
        })).ConfigureAwait(false);
        var all = identities.Where(i => i != null).Select(i => i!).ToList();
        var subs = all.Where(i => IsWiimSub(i.Device.Project) && i.Device.Uuid.Length > 0)
            .Select(i => new SubCandidate(i.Device.Ip, i.Device.Uuid, i.Device.Name, i.PairedTo))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (subs, all.Select(i => i.Device).ToList());
    }

    internal async Task<bool> PairSubAsync(string ip, SubCandidate sub, bool wired)
    {
        var body = new JsonObject
        {
            ["ip"] = sub.Ip, ["name"] = sub.Name, ["type"] = wired ? WiredSubType : WirelessSubType, ["uuid"] = sub.Uuid
        };
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "multiroom:subwooferPair:" + body.ToJsonString()).ConfigureAwait(false))) return false;
        var deadline = DateTime.UtcNow + SubPairTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000).ConfigureAwait(false);
            var paired = await GetPairedSubsAsync(ip).ConfigureAwait(false);
            if (paired?.Any(s => s.Uuid.Equals(sub.Uuid, StringComparison.OrdinalIgnoreCase)) == true) return true;
        }
        return false;
    }

    internal async Task<bool> ForgetSubAsync(string ip, string subUuid)
    {
        var body = new JsonObject { ["uuid"] = subUuid };
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "multiroom:subwooferForget:" + body.ToJsonString()).ConfigureAwait(false))) return false;
        var paired = await GetPairedSubsAsync(ip).ConfigureAwait(false);
        return paired?.Any(s => s.Uuid.Equals(subUuid, StringComparison.OrdinalIgnoreCase)) != true;
    }
}
