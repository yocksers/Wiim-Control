using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WiimControl;

sealed record NetworkSettings(string? Band, string? Roam, string? Ipv6, string? Dns, string? PowerMode, string? StaticIp = null);

sealed partial class WiimController
{
    private async Task<NetworkSettings> GetNetworkSettingsAsync(string ip)
    {
        var band = SendToDeviceAsync(ip, "getWlanBandConfig");
        var roam = SendToDeviceAsync(ip, "getWlanRoamConfig");
        var ipv6 = SendToDeviceAsync(ip, "getIPV6Enable");
        var dns = SendToDeviceAsync(ip, "getNetworkPreferDNS");
        var power = SendToDeviceAsync(ip, "getPowerModeTime");
        var staticIp = SendToDeviceAsync(ip, "getStaticIpInfo");
        await Task.WhenAll(band, roam, ipv6, dns, power, staticIp).ConfigureAwait(false);
        return new NetworkSettings(band.Result, roam.Result, ipv6.Result, dns.Result, power.Result, staticIp.Result);
    }

    internal static NetworkInfo? ParseNetworkInfo(string? statusEx, NetworkSettings settings)
    {
        if (statusEx == null) return null;
        string Field(string name) => JsonField(statusEx, name) ?? string.Empty;
        int? Number(string name) => int.TryParse(Field(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
        static bool HasAddress(string ip) => ip.Length > 0 && ip != "0.0.0.0";

        string wired = new[] { Field("eth0"), Field("eth2") }.FirstOrDefault(HasAddress) ?? string.Empty;
        string wireless = Field("apcli0");
        bool isWired = wired.Length > 0 && !HasAddress(wireless);

        string dns = settings.Dns?.Trim() ?? string.Empty;
        bool dnsKnown = IPAddress.TryParse(dns, out _);
        int? standby = null;
        try
        {
            using var doc = JsonDocument.Parse(settings.PowerMode ?? string.Empty);
            if (doc.RootElement.TryGetProperty("idleInterval", out var idle) && int.TryParse(idle.ToString(), out int seconds)) standby = seconds;
        }
        catch (JsonException) { }

        return new NetworkInfo(isWired, HexText(Field("essid")) ?? Field("essid"), isWired ? wired : wireless,
            Number("RSSI"), Field("BSSID"), Number("wlanDataRate"), Number("wlanSnr"), Number("wlanFreq"), Number("WifiChannel"),
            ParseFlag(settings.Band), ParseFlag(settings.Roam), ParseFlag(settings.Ipv6),
            dnsKnown ? (dns == "0.0.0.0" ? string.Empty : dns) : null, standby,
            ParseIpConfig(settings.StaticIp, isWired, isWired ? wired : wireless),
            ParseFlag(Field("internet")), Number("wlanNoise") is int noise and < 0 ? noise : null, CleanMac(Field("MAC")));
    }

    internal static string SignalName(int rssi) => rssi switch
    {
        >= -55 => "Excellent",
        >= -67 => "Very good",
        >= -75 => "Good",
        >= -85 => "Weak",
        _ => "Poor"
    };

    private async Task<bool> SetAndConfirmAsync(string ip, string set, string get, string expected)
    {
        await SendToDeviceAsync(ip, set).ConfigureAwait(false);
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(i == 0 ? 500 : 1500).ConfigureAwait(false);
            if ((await SendToDeviceAsync(ip, get).ConfigureAwait(false))?.Trim() != expected) continue;
            await Task.Delay(2000).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    internal Task<bool> SetFiveGhzOnlyAsync(string ip, bool on) =>
        SetAndConfirmAsync(ip, $"setWlanBandConfig:{(on ? 1 : 0)}", "getWlanBandConfig", on ? "1" : "0");

    internal Task<bool> SetWifiRoamingAsync(string ip, bool on) =>
        SetAndConfirmAsync(ip, $"setWlanRoamConfig:{(on ? 1 : 0)}", "getWlanRoamConfig", on ? "1" : "0");

    internal Task<bool> SetIpv6Async(string ip, bool on) =>
        SetAndConfirmAsync(ip, $"setIPV6Enable:{(on ? 1 : 0)}", "getIPV6Enable", on ? "1" : "0");

    internal Task<bool> SetPreferredDnsAsync(string ip, string dns)
    {
        string value = dns.Length > 0 ? dns : "0.0.0.0";
        return SetAndConfirmAsync(ip, $"setNetworkPreferDNS:{value}", "getNetworkPreferDNS", value);
    }

    internal async Task<bool> SetStandbyAsync(string ip, int seconds) =>
        CommandSucceeded(await SendToDeviceAsync(ip,
            $"setPowerModeTime:{{\"idleInterval\":\"{seconds.ToString(CultureInfo.InvariantCulture)}\"}}").ConfigureAwait(false));

    internal async Task<bool> RestoreFactorySettingsAsync(string ip) =>
        CommandSucceeded(await SendToDeviceAsync(ip, "restoreToDefault").ConfigureAwait(false));
}
