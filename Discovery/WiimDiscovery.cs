using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace WiimControl;

static class WiimDiscovery
{
    private static readonly string[] SearchTargets =
        ["ssdp:all", "urn:schemas-upnp-org:device:MediaRenderer:1"];

    private static readonly HttpClient ProbeHttp = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    { Timeout = TimeSpan.FromMilliseconds(700) };

    public static async Task<List<KnownDevice>> DiscoverAsync(TimeSpan timeout)
    {
        var ssdpTask = DiscoverViaSsdpAsync(timeout);
        var scanTask = ScanSubnetAsync(timeout);
        await Task.WhenAll(ssdpTask, scanTask);

        var merged = new Dictionary<string, KnownDevice>();
        foreach (var d in ssdpTask.Result) merged[d.Ip] = d;
        foreach (var d in scanTask.Result) merged.TryAdd(d.Ip, d);
        return merged.Values.ToList();
    }

    private static async Task<List<KnownDevice>> DiscoverViaSsdpAsync(TimeSpan timeout)
    {
        var confirmed = new ConcurrentDictionary<string, string?>();
        var candidateLocations = new ConcurrentDictionary<string, byte>();
        using var cts = new CancellationTokenSource(timeout);

        var localAddresses = GetCandidateLocalAddresses();
        if (localAddresses.Count == 0) localAddresses.Add(IPAddress.Any);

        var clients = new List<UdpClient>();
        var listenTasks = new List<Task>();
        var mcastEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

        foreach (var addr in localAddresses)
        {
            UdpClient client;
            try { client = new UdpClient(new IPEndPoint(addr, 0)); }
            catch { continue; }

            clients.Add(client);
            listenTasks.Add(ListenAsync(client, confirmed, candidateLocations, cts.Token));

            foreach (var st in SearchTargets)
            {
                var msg = BuildSearchMessage(st);
                try { await client.SendAsync(msg, msg.Length, mcastEndpoint); } catch { }
            }
        }

        if (clients.Count == 0) return [];

        try { await Task.WhenAll(listenTasks); } catch { }
        foreach (var c in clients) c.Dispose();

        var results = new ConcurrentDictionary<string, KnownDevice>();
        foreach (var (ip, location) in confirmed)
            results[ip] = new KnownDevice(await TryGetFriendlyNameAsync(location) ?? "WiiM Amp", ip, string.Empty);

        await ResolveUnconfirmedAsync(candidateLocations.Keys, results);
        return results.Values.ToList();
    }

    private static byte[] BuildSearchMessage(string searchTarget) => Encoding.ASCII.GetBytes(
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 3\r\n" +
        $"ST: {searchTarget}\r\n\r\n");

    private static async Task ListenAsync(
        UdpClient client, ConcurrentDictionary<string, string?> confirmed,
        ConcurrentDictionary<string, byte> candidateLocations, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var receiveTask = client.ReceiveAsync();
                var finished = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, token));
                if (finished != receiveTask) return;

                var result = await receiveTask;
                var text = Encoding.ASCII.GetString(result.Buffer);
                var locationLine = text.Split("\r\n")
                    .FirstOrDefault(l => l.StartsWith("location:", StringComparison.OrdinalIgnoreCase));
                var location = locationLine?[(locationLine.IndexOf(':') + 1)..].Trim();

                if (text.Contains("wiim", StringComparison.OrdinalIgnoreCase))
                    confirmed[result.RemoteEndPoint.Address.ToString()] = location;
                else if (location != null)
                    candidateLocations.TryAdd(location, 0);
            }
        }
        catch { }
    }

    private static async Task<string?> TryGetFriendlyNameAsync(string? location)
    {
        if (location is null || !Uri.TryCreate(location, UriKind.Absolute, out var uri)) return null;
        try
        {
            var xml = await ProbeHttp.GetStringAsync(uri);
            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.Get("urn:schemas-upnp-org:device-1-0");
            return doc.Descendants(ns + "device").FirstOrDefault()?.Element(ns + "friendlyName")?.Value;
        }
        catch { return null; }
    }

    private static async Task ResolveUnconfirmedAsync(IEnumerable<string> locations, ConcurrentDictionary<string, KnownDevice> results)
    {
        var tasks = locations.Select(async location =>
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || results.ContainsKey(uri.Host)) return;
            try
            {
                var xml = await ProbeHttp.GetStringAsync(uri);
                var doc = XDocument.Parse(xml);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.Get("urn:schemas-upnp-org:device-1-0");
                var device = doc.Descendants(ns + "device").FirstOrDefault();
                var friendlyName = device?.Element(ns + "friendlyName")?.Value ?? uri.Host;
                var manufacturer = device?.Element(ns + "manufacturer")?.Value ?? string.Empty;

                if (manufacturer.Contains("linkplay", StringComparison.OrdinalIgnoreCase)
                    || friendlyName.Contains("wiim", StringComparison.OrdinalIgnoreCase))
                {
                    results[uri.Host] = new KnownDevice(friendlyName, uri.Host, uri.Host);
                }
            }
            catch { }
        });
        await Task.WhenAll(tasks);
    }

    private static List<IPAddress> GetCandidateLocalAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addrInfo in nic.GetIPProperties().UnicastAddresses)
                    if (addrInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(addrInfo.Address);
            }
        }
        catch { }
        return result;
    }

    private static async Task<List<KnownDevice>> ScanSubnetAsync(TimeSpan timeout)
    {
        var results = new ConcurrentDictionary<string, KnownDevice>();
        var hosts = GetSubnetHostAddresses();
        if (hosts.Count == 0) return [];

        using var cts = new CancellationTokenSource(timeout);
        var options = new ParallelOptions { MaxDegreeOfParallelism = 48, CancellationToken = cts.Token };
        try
        {
            await Parallel.ForEachAsync(hosts, options, async (ip, ct) =>
            {
                var device = await ProbeWiimApiAsync(ip, ct);
                if (device is { } d) results[d.Ip] = d;
            });
        }
        catch (OperationCanceledException) { }
        return results.Values.ToList();
    }

    private static async Task<KnownDevice?> ProbeWiimApiAsync(IPAddress ip, CancellationToken token)
    {
        foreach (var command in new[] { "getStatusEx", "getStatus" })
        {
            try
            {
                var json = await ProbeHttp.GetStringAsync($"https://{ip}/httpapi.asp?command={command}", token);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                string hardware = doc.RootElement.TryGetProperty("hardware", out var hw) ? hw.GetString() ?? "" : "";
                string project  = doc.RootElement.TryGetProperty("project", out var pr) ? pr.GetString() ?? "" : "";
                string deviceName = doc.RootElement.TryGetProperty("DeviceName", out var dn) ? dn.GetString() ?? "" : "";

                if (hardware.Contains("wiim", StringComparison.OrdinalIgnoreCase)
                    || project.Contains("wiim", StringComparison.OrdinalIgnoreCase)
                    || deviceName.Contains("wiim", StringComparison.OrdinalIgnoreCase))
                {
                    var name = !string.IsNullOrWhiteSpace(deviceName) ? deviceName
                        : !string.IsNullOrWhiteSpace(hardware) ? hardware : "WiiM Amp";
                    return new KnownDevice(name, ip.ToString(), string.Empty);
                }
            }
            catch { }
        }
        return null;
    }

    private static List<IPAddress> GetSubnetHostAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addrInfo in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addrInfo.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = addrInfo.IPv4Mask;
                    if (mask is null) continue;

                    uint ip   = BitConverter.ToUInt32([.. addrInfo.Address.GetAddressBytes().Reverse()], 0);
                    uint m    = BitConverter.ToUInt32([.. mask.GetAddressBytes().Reverse()], 0);
                    uint network   = ip & m;
                    uint broadcast = network | ~m;
                    uint hostCount = ~m;
                    if (hostCount == 0 || hostCount > 1024) continue;

                    for (uint h = network + 1; h < broadcast; h++)
                        result.Add(new IPAddress([.. BitConverter.GetBytes(h).Reverse()]));
                }
            }
        }
        catch { }
        return result.Distinct().ToList();
    }
}
