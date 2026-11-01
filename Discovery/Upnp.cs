using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace WiimControl;

static class Upnp
{
    private static readonly XNamespace SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace DeviceNs = "urn:schemas-upnp-org:device-1-0";
    private const string ContentDirectoryService = "urn:schemas-upnp-org:service:ContentDirectory:1";
    private const string MediaServerType = "urn:schemas-upnp-org:device:MediaServer:1";

    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<XElement?> CallAsync(string controlUrl, string service, string action, params (string Name, string Value)[] args)
    {
        XNamespace u = service;
        var envelope = new XElement(SoapNs + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", SoapNs),
            new XAttribute(SoapNs + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(SoapNs + "Body",
                new XElement(u + action, new XAttribute(XNamespace.Xmlns + "u", u),
                    args.Select(a => new XElement(a.Name, a.Value)))));
        string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + envelope.ToString(SaveOptions.DisableFormatting);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
            request.Content.Headers.ContentType!.CharSet = "\"utf-8\"";
            request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service}#{action}\"");
            request.Headers.ConnectionClose = true;
            try
            {
                using var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var doc = XDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == action + "Response");
            }
            catch (Exception ex) when (attempt == 0 && ex is HttpRequestException or IOException)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    public static async Task<List<MediaServer>> DiscoverMediaServersAsync(TimeSpan timeout)
    {
        var locations = new ConcurrentDictionary<string, byte>();
        var (lan, other) = LocalAddresses();
        await Task.WhenAll(lan.Concat(other).Select(a => SearchAsync(a, locations, timeout))).ConfigureAwait(false);
        var reachable = locations.Keys.Where(l => !(lan.Count > 0 && other.Contains(ResolveHost(new Uri(l)))));
        var servers = await Task.WhenAll(reachable.Select(DescribeMediaServerAsync)).ConfigureAwait(false);
        return servers.OfType<MediaServer>()
            .Select(s => s with { OnThisComputer = lan.Contains(ResolveHost(new Uri(s.ContentDirectoryUrl))) })
            .GroupBy(s => s.Udn.Length > 0 ? s.Udn : s.ContentDirectoryUrl)
            .Select(g => g.First())
            .OrderBy(s => s.OnThisComputer ? 0 : 1)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static (List<IPAddress> Lan, List<IPAddress> Other) LocalAddresses()
    {
        var lan = new List<IPAddress>();
        var other = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var properties = nic.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                                    !g.Address.Equals(IPAddress.Any));
                (gateway ? lan : other).AddRange(properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address));
            }
        }
        catch { }
        return (lan, other);
    }

    private static async Task SearchAsync(IPAddress local, ConcurrentDictionary<string, byte> locations, TimeSpan timeout)
    {
        byte[] message = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
            $"ST: {MediaServerType}\r\n\r\n");
        UdpClient client;
        try
        {
            client = new UdpClient(new IPEndPoint(local, 0));
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
        }
        catch { return; }

        using (client)
        using (var cts = new CancellationTokenSource(timeout))
        {
            try
            {
                await client.SendAsync(message, message.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900)).ConfigureAwait(false);
                await client.SendAsync(message, message.Length, new IPEndPoint(local, 1900)).ConfigureAwait(false);
                while (true)
                {
                    var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    string? location = text.Split("\r\n")
                        .FirstOrDefault(l => l.StartsWith("location:", StringComparison.OrdinalIgnoreCase))?[9..].Trim();
                    if (location != null && Uri.TryCreate(location, UriKind.Absolute, out var uri) && !IPAddress.IsLoopback(ResolveHost(uri)))
                        locations.TryAdd(location, 0);
                }
            }
            catch { }
        }
    }

    private static IPAddress ResolveHost(Uri uri) => IPAddress.TryParse(uri.Host, out var address) ? address : IPAddress.None;

    private static async Task<MediaServer?> DescribeMediaServerAsync(string location)
    {
        try
        {
            var doc = XDocument.Parse(await Http.GetStringAsync(location).ConfigureAwait(false));
            var ns = doc.Root?.GetDefaultNamespace() ?? DeviceNs;
            string baseUrl = doc.Root?.Element(ns + "URLBase")?.Value is { Length: > 0 } b ? b : location;
            foreach (var device in doc.Descendants(ns + "device"))
            {
                var service = device.Element(ns + "serviceList")?.Elements(ns + "service")
                    .FirstOrDefault(s => s.Element(ns + "serviceType")?.Value.StartsWith(ContentDirectoryService[..^1], StringComparison.Ordinal) == true);
                if (service?.Element(ns + "controlURL")?.Value is not { Length: > 0 } control) continue;
                string icon = device.Element(ns + "iconList")?.Elements(ns + "icon")
                    .OrderBy(i => i.Element(ns + "mimetype")?.Value == "image/png" ? 0 : 1)
                    .ThenByDescending(i => int.TryParse(i.Element(ns + "width")?.Value, out int w) && w <= 128 ? w : 0)
                    .Select(i => i.Element(ns + "url")?.Value).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? string.Empty;
                return new MediaServer(
                    device.Element(ns + "friendlyName")?.Value.Trim().TrimEnd(':').Trim() ?? new Uri(location).Host,
                    device.Element(ns + "UDN")?.Value ?? string.Empty,
                    new Uri(new Uri(baseUrl), control).ToString(),
                    icon.Length > 0 ? new Uri(new Uri(baseUrl), icon).ToString() : string.Empty);
            }
        }
        catch { }
        return null;
    }

    public static async Task<BrowsePage?> BrowseAsync(MediaServer server, string objectId, int start, int count)
    {
        var response = await CallAsync(server.ContentDirectoryUrl, ContentDirectoryService, "Browse",
            ("ObjectID", objectId), ("BrowseFlag", "BrowseDirectChildren"), ("Filter", "*"),
            ("StartingIndex", start.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("RequestedCount", count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("SortCriteria", "")).ConfigureAwait(false);
        if (response?.Elements().FirstOrDefault(e => e.Name.LocalName == "Result")?.Value is not { } didl) return null;
        int total = int.TryParse(response.Elements().FirstOrDefault(e => e.Name.LocalName == "TotalMatches")?.Value, out int t) ? t : -1;
        var entries = Didl.Parse(didl);
        return entries == null ? null : new BrowsePage(entries, total >= 0 ? total : start + entries.Count);
    }
}
