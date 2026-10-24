using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const int UpnpPort = 49152;
    private const string TransportEventService = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string VolumeEventService = "urn:schemas-upnp-org:service:RenderingControl:1";
    private static readonly (string Service, string Key, string Fallback)[] EventServices =
    [
        (TransportEventService, "av", "/upnp/event/rendertransport1"),
        (VolumeEventService, "rc", "/upnp/event/rendercontrol1")
    ];
    private static readonly TimeSpan SubscriptionLength = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan EventRefreshDelay = TimeSpan.FromMilliseconds(300);
    private const int MaxEventBytes = 256 * 1024;

    private sealed class EventSubscription
    {
        public required string Ip { get; init; }
        public required string Key { get; init; }
        public required string EventUrl { get; init; }
        public string Sid { get; set; } = string.Empty;
        public string Callback { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
    }

    private bool _instantUpdates = true;
    private TcpListener? _eventListener;
    private CancellationTokenSource? _eventStop;
    private readonly ConcurrentDictionary<string, EventSubscription> _subscriptions = new();
    private readonly DispatcherTimer _eventTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly HashSet<string> _eventRefreshPending = [];
    private bool _eventTimerHooked;

    internal bool InstantUpdates
    {
        get => _instantUpdates;
        set
        {
            if (_instantUpdates == value) return;
            _instantUpdates = value;
            SaveConfig();
            if (value) StartInstantUpdates();
            else _ = StopInstantUpdatesAsync();
        }
    }

    internal bool HasInstantUpdates(string ip) =>
        _subscriptions.Values.Count(s => s.Ip == ip && s.Sid.Length > 0 && s.Expires > DateTime.UtcNow) == EventServices.Length;

    private void StartInstantUpdates()
    {
        if (!_instantUpdates || _eventListener != null) return;
        try
        {
            _eventStop = new CancellationTokenSource();
            _eventListener = new TcpListener(IPAddress.Any, 0);
            _eventListener.Start();
            _ = AcceptEventsAsync(_eventListener, _eventStop.Token);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Instant updates", ex);
            _eventListener = null;
            return;
        }
        if (!_eventTimerHooked)
        {
            _eventTimer.Tick += async (_, _) => await ReconcileSubscriptionsAsync();
            _eventTimerHooked = true;
        }
        _eventTimer.Start();
        _ = ReconcileSubscriptionsAsync();
    }

    private async Task StopInstantUpdatesAsync()
    {
        _eventTimer.Stop();
        var subscriptions = _subscriptions.Values.ToList();
        _subscriptions.Clear();
        await Task.WhenAll(subscriptions.Select(UnsubscribeAsync)).ConfigureAwait(false);
        _eventStop?.Cancel();
        try { _eventListener?.Stop(); } catch (SocketException) { }
        _eventListener = null;
    }

    private async Task ReconcileSubscriptionsAsync()
    {
        if (_eventListener == null) return;
        var ips = _knownDevices.Select(d => d.Ip).Where(ip => ip.Length > 0).Distinct().ToHashSet();
        foreach (var (path, stale) in _subscriptions.Where(s => !ips.Contains(s.Value.Ip)).ToList())
            if (_subscriptions.TryRemove(path, out _)) _ = UnsubscribeAsync(stale);
        var work = new List<Task>();
        foreach (string ip in ips)
            foreach (var (service, key, fallback) in EventServices)
            {
                string path = CallbackPath(ip, key);
                string? callback = CallbackFor(ip, path);
                if (_subscriptions.TryGetValue(path, out var existing) && existing.Sid.Length > 0 && existing.Callback != callback)
                {
                    _ = UnsubscribeAsync(existing);
                    existing.Sid = string.Empty;
                }
                if (existing is { Sid.Length: > 0 } && existing.Expires - DateTime.UtcNow > RenewBefore)
                    continue;
                work.Add(SubscribeAsync(ip, key, path, existing ?? new EventSubscription
                {
                    Ip = ip, Key = key, EventUrl = await GetEventUrlAsync(ip, service, fallback)
                }));
            }
        await Task.WhenAll(work);
    }

    private static string CallbackPath(string ip, string key) => $"/wiimcontrol/{ip}/{key}";

    private string? CallbackFor(string ip, string path) =>
        _eventListener?.LocalEndpoint is IPEndPoint endpoint && LocalAddressFor(ip) is { } local
            ? $"<http://{local}:{endpoint.Port}{path}>"
            : null;

    private async Task<string> GetEventUrlAsync(string ip, string serviceType, string fallbackPath)
    {
        string key = $"{ip}|{serviceType}|event";
        if (_serviceUrls.TryGetValue(key, out var cached)) return cached;
        string url = $"http://{ip}:{UpnpPort}{fallbackPath}";
        try
        {
            string location = $"http://{ip}:{UpnpPort}/description.xml";
            var doc = XDocument.Parse(await _http.GetStringAsync(location).ConfigureAwait(false));
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            if (doc.Descendants(ns + "service").FirstOrDefault(s => s.Element(ns + "serviceType")?.Value == serviceType)
                    ?.Element(ns + "eventSubURL")?.Value is { Length: > 0 } events)
                url = new Uri(new Uri(location), events).ToString();
        }
        catch (Exception) { }
        _serviceUrls[key] = url;
        return url;
    }

    private async Task SubscribeAsync(string ip, string key, string path, EventSubscription subscription)
    {
        if (CallbackFor(ip, path) is not { } callback) return;
        bool renew = subscription.Sid.Length > 0;
        if (renew && await SendSubscribeAsync(subscription.EventUrl, subscription, ("SID", subscription.Sid)).ConfigureAwait(false)) return;
        subscription.Sid = string.Empty;
        if (await SendSubscribeAsync(subscription.EventUrl, subscription, ("CALLBACK", callback), ("NT", "upnp:event")).ConfigureAwait(false))
        {
            subscription.Callback = callback;
            _subscriptions[path] = subscription;
        }
        else _subscriptions.TryRemove(path, out _);
    }

    private async Task<bool> SendSubscribeAsync(string url, EventSubscription subscription, params (string Name, string Value)[] headers)
    {
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod("SUBSCRIBE"), url);
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
            request.Headers.TryAddWithoutValidation("TIMEOUT", $"Second-{(int)SubscriptionLength.TotalSeconds}");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, cancel.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            if (response.Headers.TryGetValues("SID", out var sids) && sids.FirstOrDefault() is { Length: > 0 } sid) subscription.Sid = sid;
            int seconds = response.Headers.TryGetValues("TIMEOUT", out var timeouts) && timeouts.FirstOrDefault() is { } timeout &&
                          int.TryParse(timeout.Replace("Second-", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int s) && s > 0 ? s : (int)SubscriptionLength.TotalSeconds;
            subscription.Expires = DateTime.UtcNow.AddSeconds(seconds);
            return subscription.Sid.Length > 0;
        }
        catch (Exception) { return false; }
    }

    private async Task UnsubscribeAsync(EventSubscription subscription)
    {
        if (subscription.Sid.Length == 0) return;
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod("UNSUBSCRIBE"), subscription.EventUrl);
            request.Headers.TryAddWithoutValidation("SID", subscription.Sid);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var _ = await _http.SendAsync(request, cancel.Token).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private async Task AcceptEventsAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (Exception) { return; }
            _ = HandleEventClientAsync(client, token);
        }
    }

    private async Task HandleEventClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var stream = client.GetStream();
                var (head, body) = await ReadHttpRequestAsync(stream, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
                string remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? string.Empty;
                string[] request = head.Split("\r\n")[0].Split(' ');
                if (request.Length < 2 || !request[0].Equals("NOTIFY", StringComparison.OrdinalIgnoreCase)) return;
                if (!_subscriptions.TryGetValue(request[1], out var subscription) || subscription.Ip != remote) return;
                var change = ParseLastChange(body);
                if (change.Count > 0) Dispatcher.UIThread.Post(() => OnDeviceEvent(subscription.Ip, subscription.Key, change));
            }
            catch (Exception) { }
        }
    }

    private static async Task<(string Head, string Body)> ReadHttpRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int headerEnd = -1, length = -1;
        while (buffer.Length < MaxEventBytes)
        {
            int read = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            string text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (headerEnd < 0 && (headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0)
            {
                var lengthLine = text[..headerEnd].Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                length = lengthLine != null && int.TryParse(lengthLine[(lengthLine.IndexOf(':') + 1)..].Trim(), out int n) ? n : -1;
            }
            if (headerEnd >= 0 && (length < 0 ? text.Contains("</e:propertyset>", StringComparison.Ordinal) : buffer.Length - headerEnd - 4 >= length)) break;
        }
        string all = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        int split = all.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return split < 0 ? (all, string.Empty) : (all[..split], all[(split + 4)..]);
    }

    internal static Dictionary<string, string> ParseLastChange(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var lastChange = XDocument.Parse(body).Descendants().FirstOrDefault(e => e.Name.LocalName == "LastChange")?.Value;
            if (string.IsNullOrWhiteSpace(lastChange)) return values;
            foreach (var element in XDocument.Parse(lastChange).Descendants().Where(e => e.Name.LocalName != "InstanceID" && e.Name.LocalName != "Event"))
            {
                string? channel = element.Attribute("channel")?.Value;
                if (channel != null && !channel.Equals("Master", StringComparison.OrdinalIgnoreCase)) continue;
                if (element.Attribute("val")?.Value is { } value) values[element.Name.LocalName] = value;
            }
        }
        catch (Exception) { }
        return values;
    }

    private void OnDeviceEvent(string ip, string key, Dictionary<string, string> change)
    {
        bool volumeEvent = key == "rc" && !IsBluetoothFromThisComputer(ip);
        if (volumeEvent)
        {
            int? volume = int.TryParse(change.GetValueOrDefault("Volume"), out int v) ? Math.Clamp(v, 0, 100) : null;
            bool? muted = change.GetValueOrDefault("Mute") is { } mute ? mute == "1" : null;
            if (volume == null && muted == null) return;
            if (ip == _deviceIp && (volume ?? (_cachedIp == ip ? _cachedVolume : null)) is int known &&
                (muted ?? (_cachedIp == ip ? _cachedMuted : null)) is bool knownMuted)
                StoreVolumeState(ip, known, knownMuted);
            _settingsWindow?.OnVolumeEvent(ip, volume, muted);
        }
        string pendingKey = $"{ip}|{key}";
        if (!_eventRefreshPending.Add(pendingKey)) return;
        DispatcherTimer.RunOnce(async () =>
        {
            _eventRefreshPending.Remove(pendingKey);
            foreach (var window in _miniPlayers.Values.Where(w => w.Ip == ip || PlaybackTargetOf(w.Ip) == ip).ToList()) window.RefreshNow();
            if (volumeEvent) return;
            _settingsWindow?.OnDeviceEvent(ip);
            if (ip == _deviceIp || ip == PlaybackTarget) await RefreshNowPlayingAsync();
        }, EventRefreshDelay);
    }
}
