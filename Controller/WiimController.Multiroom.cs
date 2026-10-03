using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private MultiroomSnapshot? _multiroomCache;

    internal string EffectiveGroupLeader =>
        _groupLeaderIp.Length > 0 && _knownDevices.Any(d => d.Ip == _groupLeaderIp) ? _groupLeaderIp : _deviceIp;

    private string PlaybackTarget =>
        _multiroomCache != null && _multiroomCache.LeaderOf.TryGetValue(_deviceIp, out var leader) ? leader : _deviceIp;

    private async Task<string?> SendToDeviceAsync(string ip, string command)
    {
        try
        {
            return await _http.GetStringAsync(
                $"https://{ip}/httpapi.asp?command={Uri.EscapeDataString(command)}").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal async Task<MultiroomSnapshot> GetMultiroomSnapshotAsync()
    {
        var devices = _knownDevices.ToList();
        var replies = await Task.WhenAll(devices.Select(async d =>
        {
            var status = SendToDeviceAsync(d.Ip, "getStatusEx");
            var slaves = SendToDeviceAsync(d.Ip, "multiroom:getSlaveList");
            await Task.WhenAll(status, slaves).ConfigureAwait(false);
            return (Device: d, Status: status.Result, Slaves: slaves.Result);
        })).ConfigureAwait(false);

        var online = new HashSet<string>();
        var uuids = new Dictionary<string, string>();
        foreach (var r in replies)
        {
            if (r.Status == null && r.Slaves == null) continue;
            online.Add(r.Device.Ip);
            var uuid = NormalizeUuid(JsonField(r.Status, "uuid") ?? r.Device.Uuid);
            if (uuid.Length > 0) uuids[r.Device.Ip] = uuid;
        }

        var leaderOf = new Dictionary<string, string>();
        var memberAddress = new Dictionary<string, string>();
        foreach (var r in replies)
        {
            foreach (var (slaveIp, slaveUuid) in ParseSlaveList(r.Slaves))
            {
                string? known = devices.FirstOrDefault(d => d.Ip == slaveIp).Ip;
                if (known == null && slaveUuid.Length > 0)
                    known = uuids.FirstOrDefault(u => SameUuid(u.Value, slaveUuid)).Key;
                if (known == null || known == r.Device.Ip) continue;
                leaderOf[known] = r.Device.Ip;
                memberAddress[known] = slaveIp.Length > 0 ? slaveIp : known;
            }
        }
        var snapshot = new MultiroomSnapshot(online, leaderOf, memberAddress);
        _multiroomCache = snapshot;
        return snapshot;
    }

    internal async Task<bool> JoinGroupAsync(string memberIp, string leaderIp) =>
        CommandSucceeded(await SendToDeviceAsync(memberIp, $"ConnectMasterAp:JoinGroupMaster:eth{leaderIp}:wifi0.0.0.0").ConfigureAwait(false));

    internal async Task<bool> RemoveFromGroupAsync(string leaderIp, string memberAddress) =>
        CommandSucceeded(await SendToDeviceAsync(leaderIp, $"multiroom:SlaveKickout:{memberAddress}").ConfigureAwait(false));

    internal async Task<bool> UngroupAsync(string leaderIp) =>
        CommandSucceeded(await SendToDeviceAsync(leaderIp, "multiroom:Ungroup").ConfigureAwait(false));

    private static string? JsonField(string? json, string name)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var p)
                ? p.ToString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<(string Ip, string Uuid)> ParseSlaveList(string? json)
    {
        var result = new List<(string, string)>();
        if (json == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("slave_list", out var list) ||
                list.ValueKind != JsonValueKind.Array) return result;
            foreach (var s in list.EnumerateArray())
            {
                string ip = s.TryGetProperty("ip", out var ipProp) ? ipProp.ToString() : string.Empty;
                string uuid = s.TryGetProperty("uuid", out var uuidProp) ? NormalizeUuid(uuidProp.ToString()) : string.Empty;
                result.Add((ip, uuid));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static List<(string Ip, int Volume)> ParseSlaveVolumes(string? json)
    {
        var result = new List<(string, int)>();
        if (json == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("slave_list", out var list) ||
                list.ValueKind != JsonValueKind.Array) return result;
            foreach (var s in list.EnumerateArray())
                if (s.TryGetProperty("ip", out var ip) && s.TryGetProperty("volume", out var volume) &&
                    int.TryParse(volume.ToString(), out int v))
                    result.Add((ip.ToString(), v));
        }
        catch (JsonException) { }
        return result;
    }

    private static string NormalizeUuid(string? uuid) =>
        new string((uuid ?? string.Empty).Replace("uuid:", "", StringComparison.OrdinalIgnoreCase)
            .Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private static bool SameUuid(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal));
}
