using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private MultiroomSnapshot? _multiroomCache;
    private DateTime _multiroomCacheTime = DateTime.MinValue;
    private HashSet<string> _groupedIps = [];
    private static readonly TimeSpan MultiroomCacheLifetime = TimeSpan.FromSeconds(30);

    internal string EffectiveGroupLeader =>
        _groupLeaderIp.Length > 0 && _knownDevices.Any(d => d.Ip == _groupLeaderIp) ? _groupLeaderIp : _deviceIp;

    private string PlaybackTarget => PlaybackTargetOf(_deviceIp);

    internal bool IsGrouped(string ip) => _groupedIps.Contains(ip);

    internal string PlaybackTargetOf(string ip) =>
        _multiroomCache != null && _multiroomCache.LeaderOf.TryGetValue(ip, out var leader) ? leader : ip;

    private async Task<string?> SendToDeviceAsync(string ip, string command)
    {
        try
        {
            return await GetDeviceTextAsync($"https://{ip}/httpapi.asp?command={Uri.EscapeDataString(command)}").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _deviceModels = new();

    internal string? ModelOf(string ip) => _deviceModels.GetValueOrDefault(ip);

    private static readonly string[] ScreenProjects = ["WiiM_Ultra", "WiiM_Amp_Ultra", "WiiM_Sound", "WiiM_Bar"];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _deviceProjects = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _screensSeen = new();

    internal static bool IsScreenProject(string? project) =>
        project is { Length: > 0 } && ScreenProjects.Any(p => project.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    internal bool HasScreen(string ip) => _screensSeen.ContainsKey(ip) || IsScreenProject(_deviceProjects.GetValueOrDefault(ip));

    internal void NoteScreen(string ip) => _screensSeen[ip] = true;

    internal bool IsRoundScreen(string ip, ScreenUi ui) =>
        ScreenPreviews.IsRoundProject(_deviceProjects.GetValueOrDefault(ip)) ||
        (ui.Views.TryGetValue("main_music_view", out var main) && main.Choices.Any(c => c >= 7)) ||
        (ui.Views.TryGetValue("sub_music_view", out var second) && second.Choices.Any(c => c is >= 4 and <= 6));

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
        var fixedVolume = new HashSet<string>();
        var uuids = new Dictionary<string, string>();
        foreach (var r in replies)
        {
            if (r.Status == null && r.Slaves == null) continue;
            online.Add(r.Device.Ip);
            if (JsonField(r.Status, "project") is { Length: > 0 } project)
            {
                _deviceModels[r.Device.Ip] = ModelName(project);
                _deviceProjects[r.Device.Ip] = project;
            }
            if (FixedVolumeOf(r.Status) == true) fixedVolume.Add(r.Device.Ip);
            var uuid = NormalizeUuid(JsonField(r.Status, "uuid") ?? r.Device.Uuid);
            if (uuid.Length > 0) uuids[r.Device.Ip] = uuid;
        }

        var leaderOf = new Dictionary<string, string>();
        var memberAddress = new Dictionary<string, string>();
        var channels = new Dictionary<string, int>();
        var grouped = new HashSet<string>();
        var others = new Dictionary<string, List<OtherFollower>>();
        foreach (var r in replies)
        {
            var names = ParseFollowerStates(r.Slaves).ToDictionary(f => f.Address, f => f.Name);
            foreach (var (slaveIp, slaveUuid, channel) in ParseSlaveList(r.Slaves))
            {
                grouped.Add(r.Device.Ip);
                string? known = devices.FirstOrDefault(d => d.Ip == slaveIp).Ip;
                if (known == null && slaveUuid.Length > 0)
                    known = uuids.FirstOrDefault(u => SameUuid(u.Value, slaveUuid)).Key;
                if (known == null && slaveIp.Length > 0)
                {
                    if (!others.TryGetValue(r.Device.Ip, out var list)) others[r.Device.Ip] = list = [];
                    list.Add(new OtherFollower(slaveIp, names.GetValueOrDefault(slaveIp) is { Length: > 0 } name ? name : slaveIp));
                    continue;
                }
                if (known == null || known == r.Device.Ip) continue;
                leaderOf[known] = r.Device.Ip;
                memberAddress[known] = slaveIp.Length > 0 ? slaveIp : known;
                if (channel is int c) channels[known] = c;
            }
        }
        grouped.UnionWith(leaderOf.Keys);
        _groupedIps = grouped;
        var snapshot = new MultiroomSnapshot(online, leaderOf, memberAddress, channels, fixedVolume, others);
        _multiroomCache = snapshot;
        _multiroomCacheTime = DateTime.UtcNow;
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

    internal static List<(string Ip, string Uuid, int? Channel)> ParseSlaveList(string? json)
    {
        var result = new List<(string, string, int?)>();
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
                int? channel = s.TryGetProperty("channel", out var channelProp) && int.TryParse(channelProp.ToString(), out int c) ? c : null;
                result.Add((ip, uuid, channel));
            }
        }
        catch (JsonException) { }
        return result;
    }

    internal static List<(string Address, string Name, int? Volume, bool Muted)> ParseFollowerStates(string? json)
    {
        var result = new List<(string, string, int?, bool)>();
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
                if (ip.Length == 0) continue;
                string name = s.TryGetProperty("name", out var nameProp) ? nameProp.ToString() : string.Empty;
                int? volume = s.TryGetProperty("volume", out var volumeProp) && int.TryParse(volumeProp.ToString(), out int v) ? v : null;
                bool muted = s.TryGetProperty("mute", out var muteProp) && muteProp.ToString() == "1";
                result.Add((ip, name, volume, muted));
            }
        }
        catch (JsonException) { }
        return result;
    }

    internal async Task<List<(string Address, string Name, int? Volume, bool Muted)>> GetFollowerStatesAsync(string leaderIp) =>
        ParseFollowerStates(await SendToDeviceAsync(leaderIp, "multiroom:getSlaveList").ConfigureAwait(false));

    internal async Task<int?> GetFollowerChannelAsync(string leaderIp, string address) =>
        ParseSlaveList(await SendToDeviceAsync(leaderIp, "multiroom:getSlaveList").ConfigureAwait(false))
            .FirstOrDefault(s => s.Ip == address).Channel;

    internal async Task<bool> SetFollowerVolumeAsync(string leaderIp, string address, int volume) =>
        CommandSucceeded(await SendToDeviceAsync(leaderIp, $"multiroom:SlaveVolume:{address}:{Math.Clamp(volume, 0, 100)}").ConfigureAwait(false));

    internal async Task<bool> SetAllFollowersMutedAsync(string leaderIp, bool muted) =>
        CommandSucceeded(await SendToDeviceAsync(leaderIp, $"setPlayerCmd:slave_mute:{(muted ? "mute" : "unmute")}").ConfigureAwait(false));

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
