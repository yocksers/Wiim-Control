using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace WiimControl;

sealed partial class WiimController
{
    private const string AutomaticAddress = "0.0.0.0";
    private const int DefaultPrefix = 24;
    private static readonly TimeSpan IpChangeTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan IpChangeSettle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IpChangePoll = TimeSpan.FromSeconds(3);

    internal static IpConfig? ParseIpConfig(string? json, bool wired, string currentIp)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{')) return null;
        string prefix = wired ? "eth" : "wlan";
        if (JsonField(json, prefix + "StaticIpEnable") is not { } enabled) return null;
        string address = JsonField(json, prefix + "StaticIp") ?? string.Empty;
        int prefixLength = DefaultPrefix;
        int slash = address.IndexOf('/');
        if (slash > 0)
        {
            if (int.TryParse(address[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int length) && length is > 0 and <= 32)
                prefixLength = length;
            address = address[..slash];
        }
        bool manual = enabled.Trim() == "1";
        static string Clean(string? value) => value is { Length: > 0 } v && v != AutomaticAddress ? v : string.Empty;
        return new IpConfig(manual, Clean(address) is { Length: > 0 } a ? a : currentIp, prefixLength,
            Clean(JsonField(json, prefix + "GateWay")), Clean(JsonField(json, prefix + "DnsServer")));
    }

    internal static string StaticIpCommand(bool wired, string address, int prefix, string gateway, string dns) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(wired ? "setEthStaticIp" : "setWlanStaticIp")}:ipAddr={address}/{prefix}:gateWay={gateway}:dnsServer={dns}");

    internal static string AutomaticIpCommand(bool wired) =>
        StaticIpCommand(wired, AutomaticAddress, DefaultPrefix, AutomaticAddress, AutomaticAddress);

    private static uint? AddressBits(string text) =>
        IPAddress.TryParse(text.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetwork && text.Trim().Count(c => c == '.') == 3
            ? (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(address.GetAddressBytes(), 0))
            : null;

    internal static int? PrefixOfMask(string mask)
    {
        if (AddressBits(mask) is not uint bits) return null;
        int prefix = System.Numerics.BitOperations.PopCount(bits);
        uint expected = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        return bits == expected && prefix is >= 8 and <= 30 ? prefix : null;
    }

    internal static string MaskOfPrefix(int prefix)
    {
        uint bits = prefix <= 0 ? 0 : uint.MaxValue << (32 - Math.Min(prefix, 32));
        return string.Join('.', new[] { bits >> 24, (bits >> 16) & 255, (bits >> 8) & 255, bits & 255 });
    }

    internal static string? ManualIpProblem(string address, string mask, string gateway, string dns)
    {
        if (AddressBits(address) is not uint ip) return "Enter a valid IP address, for example 10.0.0.53.";
        if (PrefixOfMask(mask) is not int prefix) return "Enter a valid subnet mask, for example 255.255.255.0.";
        if (AddressBits(gateway) is not uint router) return "Enter the router's address, for example 10.0.0.1.";
        if (AddressBits(dns) is null) return "Enter a valid DNS server, for example 8.8.8.8 or the router's address.";
        uint netmask = uint.MaxValue << (32 - prefix);
        if ((ip & netmask) != (router & netmask)) return "The IP address and the router have to be in the same network.";
        uint host = ip & ~netmask;
        if (host == 0 || host == ~netmask) return "That address can't be used for a device in this network.";
        if (ip == router) return "The IP address can't be the same as the router's.";
        return null;
    }

    private void RelocateDevice(string oldIp, string newIp)
    {
        if (oldIp == newIp) return;
        int index = _knownDevices.FindIndex(k => k.Ip == oldIp);
        if (index < 0) return;
        MoveMiniPlayerPlacement(oldIp, newIp);
        _knownDevices[index] = _knownDevices[index] with { Ip = newIp };
        if (_deviceIp == oldIp) _deviceIp = newIp;
        if (_groupLeaderIp == oldIp) _groupLeaderIp = newIp;
        SaveConfig();
        RefreshDeviceStatusUi();
    }

    private async Task<bool> AnswersAtAsync(string ip) =>
        JsonField(await SendToDeviceAsync(ip, "getStatusEx").ConfigureAwait(false), "uuid") != null;

    internal async Task<bool> SetManualIpAsync(string ip, bool wired, string address, int prefix, string gateway, string dns)
    {
        string? reply = await SendToDeviceAsync(ip, StaticIpCommand(wired, address, prefix, gateway, dns)).ConfigureAwait(false);
        if (reply != null && !CommandSucceeded(reply)) return false;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => RelocateDevice(ip, address));
        var deadline = DateTime.UtcNow + IpChangeTimeout;
        await Task.Delay(IpChangeSettle).ConfigureAwait(false);
        while (DateTime.UtcNow < deadline)
        {
            if (await AnswersAtAsync(address).ConfigureAwait(false)) return true;
            await Task.Delay(IpChangePoll).ConfigureAwait(false);
        }
        return false;
    }

    internal async Task<string?> SetAutomaticIpAsync(string ip, bool wired)
    {
        string uuid = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _knownDevices.FirstOrDefault(k => k.Ip == ip).Uuid ?? string.Empty);
        string? reply = await SendToDeviceAsync(ip, AutomaticIpCommand(wired)).ConfigureAwait(false);
        if (reply != null && !CommandSucceeded(reply)) return null;
        var deadline = DateTime.UtcNow + IpChangeTimeout;
        await Task.Delay(IpChangeSettle).ConfigureAwait(false);
        while (DateTime.UtcNow < deadline)
        {
            string current = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (uuid.Length == 0) return ip;
                await DiscoverAndMergeDevicesAsync();
                return _knownDevices.FirstOrDefault(k => SameDeviceId(k.Uuid, uuid)).Ip ?? ip;
            });
            if (await AnswersAtAsync(current).ConfigureAwait(false) &&
                ParseIpConfig(await SendToDeviceAsync(current, "getStaticIpInfo").ConfigureAwait(false), wired, current) is { Manual: false })
                return current;
            await Task.Delay(IpChangePoll).ConfigureAwait(false);
        }
        return null;
    }
}
