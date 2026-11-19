using System.Text.Json.Nodes;

namespace WiimControl;

sealed record SavedGroupMember(string Uuid, string Name, string? Ip);

sealed record SavedGroup(string LeaderIp, string Id, string Name, List<SavedGroupMember> Members, bool Current);

sealed partial class WiimController
{
    private const int OwnGroupType = 1;
    private const int StereoPairGroupType = 2;

    internal static bool IsSavedMultiroomGroup(int? groupType) =>
        groupType is not (OwnGroupType or StereoPairGroupType or HomeTheaterGroupType);

    internal static List<SavedGroup> ParseSavedGroups(string leaderIp, JsonObject? groups, IReadOnlyList<KnownDevice> devices)
    {
        if (groups?["grouplist"] is not JsonArray list) return [];
        string current = groups["current_group_id"]?.ToString() ?? string.Empty;
        return list.OfType<JsonObject>()
            .Where(g => IsSavedMultiroomGroup(UiInt(g["group_type"])) && g["group_id"]?.ToString() is { Length: > 0 })
            .Select(g =>
            {
                string id = g["group_id"]!.ToString();
                var members = (g["slavelist"] as JsonArray)?.OfType<JsonObject>()
                    .Where(s => s["uuid"]?.ToString() is { Length: > 0 })
                    .Select(s =>
                    {
                        string uuid = s["uuid"]!.ToString();
                        var known = devices.FirstOrDefault(d => string.Equals(d.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
                        string name = known.Ip != null ? known.Name : s["name"]?.ToString() is { Length: > 0 } saved ? saved : uuid;
                        return new SavedGroupMember(uuid, name, known.Ip);
                    })
                    .ToList() ?? [];
                string name = g["group_name"]?.ToString() is { Length: > 0 } n ? n : "Unnamed group";
                return new SavedGroup(leaderIp, id, name, members, string.Equals(id, current, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    internal async Task<List<SavedGroup>> GetSavedGroupsAsync()
    {
        var devices = _knownDevices.ToList();
        var lists = await Task.WhenAll(devices.Select(d => d.Ip).Distinct()
            .Select(async ip => ParseSavedGroups(ip, await GetNameGroupsAsync(ip).ConfigureAwait(false), devices))).ConfigureAwait(false);
        return lists.SelectMany(l => l).ToList();
    }

    internal async Task<bool> SaveNewGroupAsync(string leaderIp, string name, IReadOnlyList<KnownDevice> members)
    {
        var groups = await GetNameGroupsAsync(leaderIp).ConfigureAwait(false);
        if (groups?["grouplist"] is not JsonArray list) return false;
        list.Add(new JsonObject
        {
            ["group_name"] = name,
            ["group_id"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["group_type"] = 0,
            ["channel"] = -1,
            ["update_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["slavelist"] = new JsonArray(members.Select(m => (JsonNode)new JsonObject
            {
                ["name"] = m.Name, ["uuid"] = m.Uuid, ["ipv4"] = m.Ip, ["channel"] = -1
            }).ToArray())
        });
        return await SaveGroupListAsync(leaderIp, groups, list).ConfigureAwait(false);
    }

    internal async Task<bool> DeleteSavedGroupAsync(SavedGroup group)
    {
        var groups = await GetNameGroupsAsync(group.LeaderIp).ConfigureAwait(false);
        if (groups?["grouplist"] is not JsonArray list) return false;
        var entry = list.OfType<JsonObject>().FirstOrDefault(g => string.Equals(g["group_id"]?.ToString(), group.Id, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return true;
        bool current = string.Equals(groups["current_group_id"]?.ToString(), group.Id, StringComparison.OrdinalIgnoreCase);
        list.Remove(entry);
        if (!await SaveGroupListAsync(group.LeaderIp, groups, list).ConfigureAwait(false)) return false;
        if (current)
            await SendToDeviceAsync(group.LeaderIp, "multiroom:setcurrentnamegroup:" + new JsonObject { ["group_id"] = "-1" }.ToJsonString()).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> SaveGroupListAsync(string leaderIp, JsonObject groups, JsonArray list)
    {
        groups.Remove("grouplist");
        var body = new JsonObject { ["grouplist"] = list };
        return CommandSucceeded(await PostCommandAsync(leaderIp, "multiroom:savenamegrouplist:" + body.ToJsonString()).ConfigureAwait(false));
    }

    internal async Task<bool> SwitchOnSavedGroupAsync(SavedGroup group)
    {
        var activate = new JsonObject
        {
            ["group_id"] = group.Id,
            ["slavelist"] = new JsonArray(group.Members.Select(m => (JsonNode)new JsonObject { ["ipv4"] = m.Ip ?? string.Empty, ["uuid"] = m.Uuid }).ToArray())
        };
        if (!CommandSucceeded(await SendToDeviceAsync(group.LeaderIp, "multiroom:setcurrentnamegroup:" + activate.ToJsonString()).ConfigureAwait(false)))
            return false;
        var expected = group.Members.Where(m => m.Ip != null).Select(m => m.Uuid).ToList();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(1500).ConfigureAwait(false);
            var joined = await GetSlaveUuidsAsync(group.LeaderIp).ConfigureAwait(false);
            if (expected.All(u => joined.Contains(u, StringComparer.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    internal async Task<bool> SwitchOffSavedGroupAsync(SavedGroup group)
    {
        if (!CommandSucceeded(await SendToDeviceAsync(group.LeaderIp, "multiroom:setcurrentnamegroup:" + new JsonObject { ["group_id"] = "-1" }.ToJsonString())
                .ConfigureAwait(false)))
            return false;
        if ((await GetSlaveUuidsAsync(group.LeaderIp).ConfigureAwait(false)).Count == 0) return true;
        return await UngroupAsync(group.LeaderIp).ConfigureAwait(false);
    }
}
