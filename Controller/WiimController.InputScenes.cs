using System.Globalization;
using System.Text.Json.Nodes;

namespace WiimControl;

sealed record RoutineContext(Version Version, List<JsonObject> Routines, string DeviceUuid, string DeviceName, bool FixedVolume, int MaxVolume,
    int CurrentVolume, bool? SubwooferOn, List<SavedGroup> Groups, string Project)
{
    public bool HasEnabledSwitch => Version >= WiimController.EnabledSceneVersion;
    public bool PicksSavedGroup => Version >= WiimController.EnabledSceneVersion;
}

sealed partial class WiimController
{
    internal static readonly Version InputSceneVersion = new(1, 2);
    internal static readonly Version EnabledSceneVersion = new(1, 3);
    private const int SceneGroupNameBytes = 31;

    internal static int? NodeInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out int number)) return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    internal static bool HasSceneSteps(JsonObject routine) =>
        routine["steps"] is not JsonArray { Count: 1 } steps || steps[0]?["type"]?.ToString() != "EQ";

    internal static JsonObject? SceneStep(JsonObject? routine, string type)
    {
        var step = (routine?["steps"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(s => s["type"]?.ToString() == type);
        return step == null ? null : step["payload"] as JsonObject ?? new JsonObject();
    }

    internal static bool SceneUuidsMatch(string? a, string? b) =>
        NormalizeUuid(a) is { Length: > 0 } plain && plain == NormalizeUuid(b);

    private static string PlainUuid(string uuid) => uuid.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase) ? uuid[5..] : uuid;

    private async Task<List<JsonObject>?> GetInputRoutinesAsync(string ip)
    {
        try
        {
            return (JsonNode.Parse(await SendToDeviceAsync(ip, "getAllRoutines:input").ConfigureAwait(false) ?? string.Empty)?["routines"] as JsonArray)
                ?.OfType<JsonObject>().Where(r => r["type"]?.ToString() is null or "input").ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    internal async Task<Version?> GetRoutineVersionAsync(string ip) =>
        Version.TryParse(JsonField(await SendToDeviceAsync(ip, "getRoutineCapability").ConfigureAwait(false), "version"), out var version) ? version : null;

    internal async Task<RoutineContext?> GetRoutineContextAsync(string ip, Version version)
    {
        var statusEx = SendToDeviceAsync(ip, "getStatusEx");
        var player = SendToDeviceAsync(ip, "getPlayerStatus");
        var sub = SendToDeviceAsync(ip, "getSubLPF");
        var subSupport = CheckSubwooferSupportAsync(ip);
        var groups = GetSavedGroupsAsync();
        await Task.WhenAll(statusEx, player, sub, subSupport, groups).ConfigureAwait(false);
        if (statusEx.Result == null) return null;

        string uuid = JsonField(statusEx.Result, "uuid") ?? string.Empty;
        string name = JsonField(statusEx.Result, "DeviceName") is { Length: > 0 } n ? n : _knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
        int maxVolume = int.TryParse(JsonField(statusEx.Result, "max_volume"), out int max) ? max : 100;
        int volume = int.TryParse(JsonField(player.Result, "vol"), out int vol) ? vol : 0;
        bool? subOn = subSupport.Result == true ? JsonField(sub.Result, "status") == "1" : null;
        var mine = groups.Result
            .Where(g => g.LeaderIp == ip || SceneUuidsMatch(_knownDevices.FirstOrDefault(d => d.Ip == g.LeaderIp).Uuid, uuid) || g.Members.Any(m => SceneUuidsMatch(m.Uuid, uuid)))
            .GroupBy(g => g.Name, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new RoutineContext(version, [], uuid, name, FixedVolumeOf(statusEx.Result) == true, maxVolume, volume, subOn, mine,
            JsonField(statusEx.Result, "project") ?? string.Empty);
    }

    internal async Task<RoutineContext?> GetInputScenesAsync(string ip)
    {
        if (await GetRoutineVersionAsync(ip).ConfigureAwait(false) is not { } version || version < InputSceneVersion) return null;
        var routines = GetInputRoutinesAsync(ip);
        var context = GetRoutineContextAsync(ip, version);
        await Task.WhenAll(routines, context).ConfigureAwait(false);
        return routines.Result == null || context.Result == null ? null : context.Result with { Routines = routines.Result };
    }

    private static JsonObject SceneSlave(string name, string uuid, string? ip)
    {
        var slave = new JsonObject { ["name"] = name, ["uuid"] = uuid };
        if (ip is { Length: > 0 }) slave["ipv4"] = ip;
        return slave;
    }

    internal JsonObject SceneGroupPayload(RoutineContext scenes, string ip, SavedGroup group)
    {
        var leader = _knownDevices.FirstOrDefault(d => d.Ip == group.LeaderIp);
        bool ownGroup = group.LeaderIp == ip || SceneUuidsMatch(leader.Uuid, scenes.DeviceUuid);
        var slaves = new JsonArray();
        if (!ownGroup && leader.Uuid is { Length: > 0 }) slaves.Add(SceneSlave(leader.Name, PlainUuid(leader.Uuid), leader.Ip));
        foreach (var member in group.Members.Where(m => !SceneUuidsMatch(m.Uuid, scenes.DeviceUuid)))
            slaves.Add(SceneSlave(member.Name, PlainUuid(member.Uuid), member.Ip));
        var payload = new JsonObject
        {
            ["group_name"] = group.Name,
            ["group_id"] = ownGroup ? group.Id : Guid.NewGuid().ToString().ToUpperInvariant(),
            ["group_type"] = ownGroup ? 0 : OwnGroupType
        };
        if (slaves.Count > 0) payload["slavelist"] = slaves;
        return payload;
    }

    internal static JsonObject SceneDevicesPayload(RoutineContext scenes, JsonObject? existing, IReadOnlyList<KnownDevice> devices)
    {
        bool keep = existing?["group_id"]?.ToString() is { Length: > 0 };
        return new JsonObject
        {
            ["group_name"] = keep ? existing!["group_name"]?.ToString() ?? string.Empty : SceneGroupName(scenes.DeviceName, devices.Count + 1),
            ["group_id"] = keep ? existing!["group_id"]!.ToString() : Guid.NewGuid().ToString().ToUpperInvariant(),
            ["group_type"] = OwnGroupType,
            ["slavelist"] = new JsonArray(devices.Select(d => (JsonNode)SceneSlave(d.Name, PlainUuid(d.Uuid), d.Ip)).ToArray())
        };
    }

    internal static string SceneGroupName(string name, int count)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Test Name";
        string suffix = count > 1 ? $" + {count - 1}" : string.Empty;
        var utf8 = System.Text.Encoding.UTF8;
        if (utf8.GetByteCount(name) + utf8.GetByteCount(suffix) <= SceneGroupNameBytes) return name + suffix;
        string tail = "..." + suffix;
        int room = SceneGroupNameBytes - utf8.GetByteCount(tail);
        string cut = name;
        while (cut.Length > 0 && utf8.GetByteCount(cut) > room) cut = cut[..^1];
        return cut.Length > 0 ? cut + tail : name;
    }

    internal async Task<bool> SaveInputSceneAsync(string ip, RoutineContext scenes, string input, string name, bool enabled,
        JsonObject? group, JsonObject? volume, int? subOutput)
    {
        var routines = await GetInputRoutinesAsync(ip).ConfigureAwait(false);
        if (routines == null) return false;
        var existing = routines.FirstOrDefault(r => r["bindInput"]?.ToString() == input);
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var routine = new JsonObject
        {
            ["id"] = existing?["id"]?.ToString() is { Length: > 0 } id ? id : Random.Shared.Next(int.MaxValue).ToString("x", CultureInfo.InvariantCulture).PadLeft(16, '0'),
            ["name"] = name
        };
        if ((existing == null ? 0 : NodeInt(existing["index"])) is int index) routine["index"] = index;
        if ((existing == null ? now : existing["createDate"]?.ToString()) is { } created) routine["createDate"] = created;
        routine["updateDate"] = now;
        routine["type"] = "input";
        routine["bindInput"] = input;
        if (scenes.HasEnabledSwitch) routine["enabled"] = enabled ? 1 : 0;

        var steps = new JsonArray();
        if (group != null) steps.Add(new JsonObject { ["type"] = "nameGroup", ["payload"] = group.DeepClone() });
        if (volume != null) steps.Add(new JsonObject { ["type"] = "volume", ["payload"] = volume.DeepClone() });
        if (subOutput is int sub) steps.Add(new JsonObject { ["type"] = "subwoofer", ["payload"] = new JsonObject { ["subOutput"] = sub } });
        if (steps.Count > 0) routine["steps"] = steps;

        string command = (existing == null ? "createRoutine:" : "updateRoutine:") + routine.ToJsonString(EqJson);
        if (!CommandSucceeded(await PostCommandAsync(ip, command).ConfigureAwait(false))) return false;
        var check = await GetInputRoutinesAsync(ip).ConfigureAwait(false);
        return check?.Any(r => r["bindInput"]?.ToString() == input && r["name"]?.ToString() == name) == true;
    }
}
