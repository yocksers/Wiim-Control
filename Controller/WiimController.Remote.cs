using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiimControl;

enum RemotePairResult { Paired, NotFound, Failed }

sealed partial class WiimController
{
    private const int RemoteScanSeconds = 8;
    private const int RemotePairSeconds = 30;
    private static readonly TimeSpan RemotePoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RemoteScanWait = TimeSpan.FromSeconds(RemoteScanSeconds + 4);
    private static readonly TimeSpan RemotePairWait = TimeSpan.FromSeconds(RemotePairSeconds + 5);

    internal static RemoteInfo? ParseRemoteStatus(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root || root["status"] is not JsonValue statusValue ||
                !statusValue.TryGetValue(out string? status)) return null;
            int? Number(string name) =>
                root[name] is JsonValue v && (v.TryGetValue(out int n) || (v.TryGetValue(out string? text) && int.TryParse(text, out n))) ? n : null;
            string Text(string name) => root[name] is JsonValue v && v.TryGetValue(out string? text) ? text : string.Empty;
            bool connected = status.Equals("connected", StringComparison.OrdinalIgnoreCase);
            return new RemoteInfo(connected, connected ? Text("name") : string.Empty, connected && Number("battery") is > 0 and var battery ? battery : null,
                connected ? Text("ver") : string.Empty, connected ? Number("rssi") : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    internal static (string Name, string Address)? ParseFoundRemote(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? string.Empty) is not JsonObject root) return null;
            foreach (var (_, value) in root)
                if (value is JsonArray list)
                    foreach (var item in list.OfType<JsonObject>())
                    {
                        string Text(params string[] names) => names.Select(n => item[n] is JsonValue v && v.TryGetValue(out string? t) ? t : null)
                            .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? string.Empty;
                        string address = Text("ad", "addr", "mac", "address");
                        if (address.Length > 0) return (Text("name"), address);
                    }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }

    internal async Task<RemoteInfo?> GetRemoteAsync(string ip) => ParseRemoteStatus(await SendToDeviceAsync(ip, "getblehidstatus").ConfigureAwait(false));

    internal async Task<RemotePairResult> PairRemoteAsync(string ip)
    {
        if (!CommandSucceeded(await SendToDeviceAsync(ip, string.Create(CultureInfo.InvariantCulture, $"startblescan:{RemoteScanSeconds}")).ConfigureAwait(false)))
            return RemotePairResult.Failed;
        (string Name, string Address)? found = null;
        var until = DateTime.UtcNow + RemoteScanWait;
        while (found == null && DateTime.UtcNow < until)
        {
            await Task.Delay(RemotePoll).ConfigureAwait(false);
            found = ParseFoundRemote(await SendToDeviceAsync(ip, "getblediscoveryresult").ConfigureAwait(false));
        }
        if (found is not { } remote) return RemotePairResult.NotFound;
        var request = new JsonObject { ["timeout"] = RemotePairSeconds };
        if (remote.Name.Length > 0) request["name"] = remote.Name;
        request["addr"] = remote.Address;
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "blehidpair:" + request.ToJsonString()).ConfigureAwait(false))) return RemotePairResult.Failed;
        until = DateTime.UtcNow + RemotePairWait;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(RemotePoll).ConfigureAwait(false);
            if (await GetRemoteAsync(ip).ConfigureAwait(false) is { Connected: true }) return RemotePairResult.Paired;
        }
        return RemotePairResult.Failed;
    }

    internal async Task<bool> ForgetRemoteAsync(string ip) =>
        CommandSucceeded(await SendToDeviceAsync(ip, "blehidremoveall").ConfigureAwait(false));

    internal async Task<bool> SetTvRemoteVolumeStepAsync(string ip, int step) =>
        CommandSucceeded(await SendToDeviceAsync(ip, $"set_cec_volume_step:{Math.Clamp(step, 1, 10)}").ConfigureAwait(false));
}
