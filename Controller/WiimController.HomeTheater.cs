using System.Text;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record HomeTheaterMember(string Uuid, string Name, int Channel);

sealed record HomeTheaterDevice(string Ip, string Uuid, string Name, string Project, bool CanBeFront);

sealed record HomeTheaterState(bool Supported, List<HomeTheaterMember> Members, bool Active, List<HomeTheaterDevice> Devices);

sealed partial class WiimController
{
    internal const int CenterChannel = 3;
    internal const int SurroundLeftChannel = 6;
    internal const int SurroundRightChannel = 7;
    internal const int SurroundBothChannel = 10;
    private const int HomeTheaterGroupType = 3;
    private const string HomeTheaterGroupName = "Home Theater";
    private static readonly string[] HomeTheaterFronts = ["WiiM_Ultra", "WiiM_Amp_Ultra", "WiiM_Amp_Pro"];

    internal static bool CanBeHomeTheaterFront(string? project) =>
        project is { Length: > 0 } && HomeTheaterFronts.Any(p => project.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private async Task<string?> PostCommandAsync(string ip, string command)
    {
        try
        {
            using var content = new StringContent("command=" + command, Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await _http.PostAsync($"https://{ip}/httpapi.asp", content).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private async Task<JsonObject?> GetNameGroupsAsync(string ip)
    {
        try { return JsonNode.Parse(await SendToDeviceAsync(ip, "multiroom:getnamegrouplist").ConfigureAwait(false) ?? string.Empty) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static JsonObject? HomeTheaterGroup(JsonObject groups) =>
        (groups["grouplist"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g => UiInt(g["group_type"]) == HomeTheaterGroupType);

    internal async Task<List<HomeTheaterDevice>> GetHomeTheaterDevicesAsync()
    {
        var devices = await Task.WhenAll(_knownDevices.Select(async d =>
        {
            string? status = await SendToDeviceAsync(d.Ip, "getStatusEx").ConfigureAwait(false);
            if (status == null) return null;
            string project = JsonField(status, "project") ?? string.Empty;
            return new HomeTheaterDevice(d.Ip, JsonField(status, "uuid") ?? d.Uuid, d.Name, project, CanBeHomeTheaterFront(project));
        })).ConfigureAwait(false);
        return devices.OfType<HomeTheaterDevice>().Where(d => d.Uuid.Length > 0).ToList();
    }

    internal async Task<HomeTheaterState> GetHomeTheaterAsync(string frontIp, List<HomeTheaterDevice> devices)
    {
        var groups = await GetNameGroupsAsync(frontIp).ConfigureAwait(false);
        if (groups?["grouplist"] is not JsonArray) return new HomeTheaterState(false, [], false, devices);
        var group = HomeTheaterGroup(groups);
        var members = (group?["slavelist"] as JsonArray)?.OfType<JsonObject>()
            .Select(s => new HomeTheaterMember(s["uuid"]?.ToString() ?? string.Empty, s["name"]?.ToString() ?? string.Empty, UiInt(s["channel"]) ?? -1))
            .Where(m => m.Uuid.Length > 0)
            .ToList() ?? [];
        bool active = group != null && groups["current_group_id"]?.ToString() == group["group_id"]?.ToString();
        return new HomeTheaterState(true, members, active, devices);
    }

    internal async Task<bool> SetHomeTheaterAsync(string frontIp, IReadOnlyList<(HomeTheaterDevice Device, int Channel)> members)
    {
        var groups = await GetNameGroupsAsync(frontIp).ConfigureAwait(false);
        if (groups?["grouplist"] is not JsonArray list) return false;
        var group = HomeTheaterGroup(groups);
        bool wasActive = group != null && groups["current_group_id"]?.ToString() == group["group_id"]?.ToString();
        var previous = (group?["slavelist"] as JsonArray)?.OfType<JsonObject>().Select(s => s["uuid"]?.ToString()).OfType<string>().ToList() ?? [];

        if (members.Count == 0)
        {
            if (group != null) list.Remove(group);
            groups["updater"] = "WiimControl";
            if (!CommandSucceeded(await PostCommandAsync(frontIp, "multiroom:savenamegrouplist:" + groups.ToJsonString()).ConfigureAwait(false))) return false;
            if (wasActive) await SendToDeviceAsync(frontIp, "multiroom:setcurrentnamegroup:" + new JsonObject { ["group_id"] = "-1" }.ToJsonString()).ConfigureAwait(false);
            await RestoreOwnGroupsAsync(previous).ConfigureAwait(false);
            return true;
        }

        if (group == null)
        {
            group = new JsonObject { ["group_id"] = Guid.NewGuid().ToString().ToUpperInvariant() };
            list.Add(group);
        }
        group["group_name"] = HomeTheaterGroupName;
        group["group_type"] = HomeTheaterGroupType;
        group["channel"] = 0;
        group["update_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        group["slavelist"] = new JsonArray(members.Select(m => (JsonNode)new JsonObject
        {
            ["name"] = m.Device.Name, ["uuid"] = m.Device.Uuid, ["ipv4"] = m.Device.Ip, ["channel"] = m.Channel
        }).ToArray());
        groups["updater"] = "WiimControl";
        if (!CommandSucceeded(await PostCommandAsync(frontIp, "multiroom:savenamegrouplist:" + groups.ToJsonString()).ConfigureAwait(false))) return false;

        var activate = new JsonObject
        {
            ["group_id"] = group["group_id"]?.ToString(),
            ["slavelist"] = new JsonArray(members.Select(m => (JsonNode)new JsonObject { ["ipv4"] = m.Device.Ip, ["uuid"] = m.Device.Uuid }).ToArray())
        };
        if (!CommandSucceeded(await SendToDeviceAsync(frontIp, "multiroom:setcurrentnamegroup:" + activate.ToJsonString()).ConfigureAwait(false))) return false;
        await RestoreOwnGroupsAsync(previous.Except(members.Select(m => m.Device.Uuid), StringComparer.OrdinalIgnoreCase).ToList()).ConfigureAwait(false);

        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(1500).ConfigureAwait(false);
            var slaves = await GetSlaveUuidsAsync(frontIp).ConfigureAwait(false);
            if (members.All(m => slaves.Contains(m.Device.Uuid, StringComparer.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private async Task<List<string>> GetSlaveUuidsAsync(string ip)
    {
        try
        {
            return JsonNode.Parse(await SendToDeviceAsync(ip, "multiroom:getSlaveList").ConfigureAwait(false) ?? string.Empty)?["slave_list"] is JsonArray list
                ? list.OfType<JsonObject>().Select(s => s["uuid"]?.ToString()).OfType<string>().ToList()
                : [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private async Task RestoreOwnGroupsAsync(IReadOnlyCollection<string> uuids)
    {
        if (uuids.Count == 0) return;
        await Task.Delay(1500).ConfigureAwait(false);
        foreach (var device in await GetHomeTheaterDevicesAsync().ConfigureAwait(false))
        {
            if (!uuids.Contains(device.Uuid, StringComparer.OrdinalIgnoreCase)) continue;
            var groups = await GetNameGroupsAsync(device.Ip).ConfigureAwait(false);
            if (groups == null || groups["current_group_id"]?.ToString() is { Length: > 0 }) continue;
            var own = (groups["grouplist"] as JsonArray)?.OfType<JsonObject>()
                .Where(g => UiInt(g["group_type"]) == 1 && (g["slavelist"] as JsonArray)?.Count is null or 0).ToList();
            if (own is not [var single]) continue;
            await SendToDeviceAsync(device.Ip, "multiroom:setcurrentnamegroup:" +
                new JsonObject { ["group_id"] = single["group_id"]?.ToString(), ["slavelist"] = new JsonArray() }.ToJsonString()).ConfigureAwait(false);
        }
    }
}
