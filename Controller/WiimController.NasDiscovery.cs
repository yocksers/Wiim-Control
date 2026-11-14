using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SMBLibrary;
using SMBLibrary.Client;

namespace WiimControl;

sealed record NasHost(string Name, string Address);

sealed partial class WiimController
{
    private static readonly IPEndPoint MdnsGroup = new(IPAddress.Parse("224.0.0.251"), 5353);
    private static readonly IPEndPoint WsdGroup = new(IPAddress.Parse("239.255.255.250"), 3702);
    private static readonly TimeSpan SmbPortTimeout = TimeSpan.FromMilliseconds(800);
    private const string SmbService = "_smb._tcp.local";

    internal static async Task<List<NasHost>> DiscoverNasAsync(TimeSpan listen)
    {
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        await Task.WhenAll(ListenMdnsAsync(found, listen), ListenWsdAsync(found, listen)).ConfigureAwait(false);
        var checks = found.Select(async pair => await AcceptsSmbAsync(pair.Key).ConfigureAwait(false) ? new NasHost(pair.Value, pair.Key) : null);
        var hosts = (await Task.WhenAll(checks).ConfigureAwait(false)).OfType<NasHost>().ToList();
        var named = await Task.WhenAll(hosts.Select(async h => h.Name.Length > 0 ? h : h with { Name = await HostNameAsync(h.Address).ConfigureAwait(false) }))
            .ConfigureAwait(false);
        return named.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task ListenMdnsAsync(System.Collections.Concurrent.ConcurrentDictionary<string, string> found, TimeSpan listen)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            byte[] query = MdnsQuery(SmbService);
            await udp.SendAsync(query, query.Length, MdnsGroup).ConfigureAwait(false);
            using var stop = new CancellationTokenSource(listen);
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult reply;
                try { reply = await udp.ReceiveAsync(stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                if (reply.RemoteEndPoint.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string? name = MdnsInstanceName(reply.Buffer, SmbService);
                if (name == null) continue;
                found.AddOrUpdate(reply.RemoteEndPoint.Address.ToString(), name, (_, old) => old.Length > 0 ? old : name);
            }
        }
        catch (Exception) { }
    }

    internal static byte[] MdnsQuery(string service)
    {
        var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (string label in service.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange(new byte[] { 0, 0, 12, 0x80, 1 });
        return [.. bytes];
    }

    internal static string? MdnsInstanceName(byte[] packet, string service)
    {
        try
        {
            if (packet.Length < 12 || (packet[2] & 0x80) == 0) return null;
            int questions = packet[4] << 8 | packet[5];
            int records = (packet[6] << 8 | packet[7]) + (packet[8] << 8 | packet[9]) + (packet[10] << 8 | packet[11]);
            int position = 12;
            for (int i = 0; i < questions; i++)
            {
                ReadDnsName(packet, ref position);
                position += 4;
            }
            for (int i = 0; i < records; i++)
            {
                string owner = ReadDnsName(packet, ref position);
                int type = packet[position] << 8 | packet[position + 1];
                int length = packet[position + 8] << 8 | packet[position + 9];
                position += 10;
                if (type == 12 && owner.Equals(service, StringComparison.OrdinalIgnoreCase))
                {
                    int target = position;
                    string instance = ReadDnsName(packet, ref target);
                    string suffix = "." + service;
                    return instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? instance[..^suffix.Length] : instance;
                }
                position += length;
            }
        }
        catch (IndexOutOfRangeException) { }
        return null;
    }

    private static string ReadDnsName(byte[] packet, ref int position)
    {
        var labels = new List<string>();
        int at = position;
        bool jumped = false;
        for (int guard = 0; guard < 64; guard++)
        {
            int length = packet[at];
            if (length == 0)
            {
                at++;
                break;
            }
            if ((length & 0xC0) == 0xC0)
            {
                int pointer = (length & 0x3F) << 8 | packet[at + 1];
                if (!jumped) position = at + 2;
                jumped = true;
                at = pointer;
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(packet, at + 1, length));
            at += length + 1;
        }
        if (!jumped) position = at;
        return string.Join('.', labels);
    }

    private static async Task ListenWsdAsync(System.Collections.Concurrent.ConcurrentDictionary<string, string> found, TimeSpan listen)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            byte[] probe = Encoding.UTF8.GetBytes(WsdProbe(Guid.NewGuid()));
            await udp.SendAsync(probe, probe.Length, WsdGroup).ConfigureAwait(false);
            using var stop = new CancellationTokenSource(listen);
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult reply;
                try { reply = await udp.ReceiveAsync(stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                if (reply.RemoteEndPoint.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (Encoding.UTF8.GetString(reply.Buffer).Contains("ProbeMatches", StringComparison.Ordinal))
                    found.TryAdd(reply.RemoteEndPoint.Address.ToString(), string.Empty);
            }
        }
        catch (Exception) { }
    }

    private static string WsdProbe(Guid id) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" " +
        "xmlns:wsd=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:wsdp=\"http://schemas.xmlsoap.org/ws/2006/02/devprof\">" +
        "<soap:Header><wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>" +
        "<wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>" +
        $"<wsa:MessageID>urn:uuid:{id}</wsa:MessageID></soap:Header>" +
        "<soap:Body><wsd:Probe><wsd:Types>wsdp:Device</wsd:Types></wsd:Probe></soap:Body></soap:Envelope>";

    private static async Task<bool> AcceptsSmbAsync(string address)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(SmbPortTimeout);
            await tcp.ConnectAsync(IPAddress.Parse(address), 445, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static async Task<string> HostNameAsync(string address)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            string name = entry.HostName.Split('.')[0];
            return name.Length > 0 && name != address ? name : address;
        }
        catch (Exception) { return address; }
    }

    internal static async Task<(List<string>? Shares, ShareProblem? Error)> ListSharesAsync(string host, string user, string password)
    {
        try
        {
            var attempt = await Task.Run(() => ListShares(host, user, password)).WaitAsync(NasShareListTimeout).ConfigureAwait(false);
            if (attempt.Error is { LoginRefused: true } && user.Length == 0)
            {
                var guest = await Task.Run(() => ListShares(host, GuestUser, string.Empty)).WaitAsync(NasShareListTimeout).ConfigureAwait(false);
                if (guest.Error == null) return guest;
            }
            return attempt;
        }
        catch (TimeoutException)
        {
            return (null, new ShareProblem($"{host} didn't answer in time.", false));
        }
    }

    private static readonly TimeSpan NasShareListTimeout = TimeSpan.FromSeconds(12);

    private static (List<string>? Shares, ShareProblem? Error) ListShares(string host, string user, string password)
    {
        var client = new SMB2Client();
        bool loggedIn = false;
        try
        {
            var address = IPAddress.TryParse(host, out var ip) ? ip
                : Dns.GetHostAddresses(host).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).FirstOrDefault();
            if (address == null || !client.Connect(address, SMBTransportType.DirectTCPTransport))
                return (null, new ShareProblem($"Couldn't reach {host}.", false));
            var (domain, name) = SplitShareUser(user);
            var status = client.Login(domain, name, password);
            if (status != NTStatus.STATUS_SUCCESS)
                return (null, new ShareProblem(user.Length == 0 ? $"{host} needs a user name and password to show its shared folders."
                    : $"{host} didn't accept the user name or password.", status is NTStatus.STATUS_LOGON_FAILURE or NTStatus.STATUS_ACCESS_DENIED
                    or NTStatus.STATUS_ACCOUNT_RESTRICTION));
            loggedIn = true;
            var shares = client.ListShares(out status);
            if (shares == null || status != NTStatus.STATUS_SUCCESS)
                return (null, new ShareProblem($"{host} didn't list its shared folders ({status}). Type the folder address instead.", false));
            return (shares.Where(s => !s.EndsWith('$')).Order(StringComparer.OrdinalIgnoreCase).ToList(), null);
        }
        catch (Exception ex)
        {
            return (null, new ShareProblem($"Couldn't reach {host}: {ex.Message}", false));
        }
        finally
        {
            try { if (loggedIn) client.Logoff(); }
            catch (Exception) { }
            try { client.Disconnect(); }
            catch (Exception) { }
        }
    }
}
