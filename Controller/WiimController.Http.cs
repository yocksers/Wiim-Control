using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WiimControl;

sealed partial class WiimController
{
    private readonly HttpClient _webHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private HttpClient HttpFor(string url) => IsLocalAddress(url) ? _http : _webHttp;

    internal static bool IsLocalAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;
        string host = uri.IdnHost;
        if (uri.IsLoopback || !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127;
    }

    private static readonly TimeSpan RequestErrorRepeat = TimeSpan.FromSeconds(30);
    private string _lastRequestError = string.Empty;
    private DateTime _lastRequestErrorTime = DateTime.MinValue;

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(4),
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string DeviceText(byte[] bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private async Task<string> GetDeviceTextAsync(string url) => DeviceText(await _http.GetByteArrayAsync(url).ConfigureAwait(false));

    private async Task<string> GetAmpStringAsync(string url)
    {
        try
        {
            return await GetDeviceTextAsync(url).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or IOException)
        {
            await Task.Delay(150).ConfigureAwait(false);
            return await GetDeviceTextAsync(url).ConfigureAwait(false);
        }
    }

    private string AmpName(string ip) =>
        _knownDevices.FirstOrDefault(d => d.Ip == ip).Name is { Length: > 0 } name ? name : $"The device at {ip}";

    private void NotifyRequestFailed(string ip, Exception ex)
    {
        string name = AmpName(ip);
        string message = ex switch
        {
            TaskCanceledException => $"{name} didn't answer in time.",
            HttpRequestException { StatusCode: { } code } => $"{name} answered with an error (HTTP {(int)code}).",
            HttpRequestException or IOException => $"Couldn't reach {name}. Check that it's switched on and connected.",
            _ => $"{name}: {ex.Message}"
        };
        if (message == _lastRequestError && DateTime.UtcNow - _lastRequestErrorTime < RequestErrorRepeat) return;
        _lastRequestError = message;
        _lastRequestErrorTime = DateTime.UtcNow;
        Notify(message);
    }
}
