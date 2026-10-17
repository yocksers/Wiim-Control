using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WiimControl;

sealed record RoonCoreInfo(string Name, string UniqueId, string Host, int Port);

sealed record RoonMessage(string Verb, string Name, JsonElement Body, byte[] Data, string ContentType)
{
    public bool IsSuccess => Name == "Success";
}

sealed class RoonConnection : IDisposable
{
    public const string ServiceId = "00720724-5143-4a9b-abac-0e50cba674bb";
    private const string MulticastAddress = "239.255.90.90";
    private const int SoodPort = 9003;

    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, Action<RoonMessage?>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _nextId;

    public string Host { get; }
    public int Port { get; }
    public string CoreId { get; private set; } = string.Empty;
    public string CoreName { get; private set; } = string.Empty;
    public bool IsOpen => _socket.State == WebSocketState.Open;
    public bool IsRegistered => _registered.Task.IsCompletedSuccessfully && _registered.Task.Result;
    public Task<bool> Registered => _registered.Task;
    public event Action? Closed;

    private RoonConnection(string host, int port)
    {
        Host = host;
        Port = port;
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    }

    public static async Task<List<RoonCoreInfo>> DiscoverAsync(TimeSpan timeout)
    {
        var cores = new ConcurrentDictionary<string, RoonCoreInfo>();
        var local = LocalAddresses();
        byte[] query = Query(new Dictionary<string, string>
        {
            ["query_service_id"] = ServiceId,
            ["_tid"] = Guid.NewGuid().ToString()
        });
        var tasks = local.Select(address => SearchAsync(address, query, cores, local, timeout)).ToList();
        tasks.Add(SearchAsync(IPAddress.Any, query, cores, local, timeout));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return cores.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static HashSet<IPAddress> LocalAddresses()
    {
        var result = new HashSet<IPAddress>();
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork) result.Add(a.Address);
            }
        }
        catch (Exception) { }
        return result;
    }

    private static async Task SearchAsync(IPAddress local, byte[] query, ConcurrentDictionary<string, RoonCoreInfo> cores,
        HashSet<IPAddress> localAddresses, TimeSpan timeout)
    {
        UdpClient client;
        try
        {
            client = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
            if (!local.Equals(IPAddress.Any))
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
        }
        catch (Exception) { return; }

        using (client)
        using (var cts = new CancellationTokenSource(timeout))
        {
            try
            {
                await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), SoodPort)).ConfigureAwait(false);
                await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Broadcast, SoodPort)).ConfigureAwait(false);
                while (true)
                {
                    var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (ParseSood(result.Buffer) is not { } props) continue;
                    if (props.GetValueOrDefault("service_id") != ServiceId) continue;
                    if (props.GetValueOrDefault("unique_id") is not { Length: > 0 } id) continue;
                    if (!int.TryParse(props.GetValueOrDefault("http_port"), out int port)) continue;
                    var from = props.GetValueOrDefault("_replyaddr") is { } reply && IPAddress.TryParse(reply, out var r) ? r : result.RemoteEndPoint.Address;
                    string host = localAddresses.Contains(from) || IPAddress.IsLoopback(from) ? "127.0.0.1" : from.ToString();
                    cores.TryAdd(id, new RoonCoreInfo(props.GetValueOrDefault("name") ?? "Roon Core", id, host, port));
                }
            }
            catch (Exception) { }
        }
    }

    private static byte[] Query(Dictionary<string, string> props)
    {
        var bytes = new List<byte>();
        bytes.AddRange("SOOD"u8.ToArray());
        bytes.Add(2);
        bytes.Add((byte)'Q');
        foreach (var (name, value) in props)
        {
            byte[] n = Encoding.UTF8.GetBytes(name), v = Encoding.UTF8.GetBytes(value);
            bytes.Add((byte)n.Length);
            bytes.AddRange(n);
            bytes.Add((byte)(v.Length >> 8));
            bytes.Add((byte)(v.Length & 0xff));
            bytes.AddRange(v);
        }
        return bytes.ToArray();
    }

    private static Dictionary<string, string?>? ParseSood(byte[] buffer)
    {
        if (buffer.Length < 6 || Encoding.ASCII.GetString(buffer, 0, 4) != "SOOD" || buffer[4] != 2) return null;
        var props = new Dictionary<string, string?>();
        int pos = 6;
        while (pos < buffer.Length)
        {
            int nameLength = buffer[pos++];
            if (nameLength == 0 || pos + nameLength + 2 > buffer.Length) return null;
            string name = Encoding.UTF8.GetString(buffer, pos, nameLength);
            pos += nameLength;
            int valueLength = (buffer[pos] << 8) | buffer[pos + 1];
            pos += 2;
            if (valueLength == 0xffff) props[name] = null;
            else
            {
                if (pos + valueLength > buffer.Length) return null;
                props[name] = Encoding.UTF8.GetString(buffer, pos, valueLength);
                pos += valueLength;
            }
        }
        return props;
    }

    public static async Task<RoonConnection?> ConnectAsync(string host, int port, Dictionary<string, object?> registration,
        Func<string, string?> tokenFor, Action<string, string> saveToken)
    {
        var connection = new RoonConnection(host, port);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection._socket.ConnectAsync(new Uri($"ws://{host}:{port}/api"), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            connection.Dispose();
            return null;
        }
        _ = connection.ReceiveLoopAsync();

        var info = await connection.RequestAsync("com.roonlabs.registry:1/info").ConfigureAwait(false);
        if (info == null)
        {
            connection.Dispose();
            return null;
        }
        connection.CoreId = Text(info.Body, "core_id");
        connection.CoreName = Text(info.Body, "display_name");
        var request = new Dictionary<string, object?>(registration);
        if (tokenFor(connection.CoreId) is { Length: > 0 } token) request["token"] = token;
        connection.Send("com.roonlabs.registry:1/register", request, message =>
        {
            if (message == null)
            {
                connection._registered.TrySetResult(false);
                return;
            }
            if (message.Name != "Registered") return;
            if (Text(message.Body, "token") is { Length: > 0 } newToken) saveToken(connection.CoreId, newToken);
            connection._registered.TrySetResult(true);
        });
        return connection;
    }

    internal static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : string.Empty;

    public Task<RoonMessage?> RequestAsync(string name, object? body = null, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<RoonMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id = Send(name, body, message => tcs.TrySetResult(message), once: true);
        if (id < 0) return Task.FromResult<RoonMessage?>(null);
        _ = Task.Delay(timeout ?? TimeSpan.FromSeconds(20)).ContinueWith(task =>
        {
            if (_pending.TryRemove(id, out _)) tcs.TrySetResult(null);
        }, TaskScheduler.Default);
        return tcs.Task;
    }

    private int Send(string name, object? body, Action<RoonMessage?> handler, bool once = false)
    {
        if (!IsOpen) return -1;
        int id = Interlocked.Increment(ref _nextId);
        _pending[id] = once
            ? message =>
            {
                _pending.TryRemove(id, out _);
                handler(message);
            }
            : handler;
        byte[]? payload = body == null ? null : JsonSerializer.SerializeToUtf8Bytes(body);
        var header = new StringBuilder($"MOO/1 REQUEST {name}\nRequest-Id: {id}\n");
        if (payload != null) header.Append($"Content-Length: {payload.Length}\nContent-Type: application/json\n");
        header.Append('\n');
        _ = WriteAsync(Encoding.UTF8.GetBytes(header.ToString()), payload);
        return id;
    }

    private async Task WriteAsync(byte[] header, byte[]? payload)
    {
        byte[] message = payload == null ? header : [.. header, .. payload];
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsOpen) await _socket.SendAsync(message, WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception) { }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!_cts.IsCancellationRequested && IsOpen)
            {
                var result = await _socket.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var data = message.ToArray();
                message.SetLength(0);
                Handle(data);
            }
        }
        catch (Exception) { }
        foreach (var id in _pending.Keys.ToList())
            if (_pending.TryRemove(id, out var handler)) handler(null);
        _registered.TrySetResult(false);
        Closed?.Invoke();
    }

    private void Handle(byte[] data)
    {
        int end = IndexOf(data, "\n\n"u8);
        if (end < 0) return;
        var lines = Encoding.UTF8.GetString(data, 0, end).Split('\n');
        var first = lines[0].Split(' ', 3);
        if (first.Length < 3 || !first[0].StartsWith("MOO/", StringComparison.Ordinal)) return;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        int start = end + 2;
        int length = int.TryParse(headers.GetValueOrDefault("Content-Length"), out int l) ? Math.Min(l, data.Length - start) : 0;
        string contentType = headers.GetValueOrDefault("Content-Type") ?? string.Empty;
        byte[] payload = length > 0 ? data[start..(start + length)] : [];
        JsonElement body = default;
        if (length > 0 && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try { body = JsonDocument.Parse(payload).RootElement.Clone(); }
            catch (JsonException) { }
        }
        string verb = first[1];
        int.TryParse(headers.GetValueOrDefault("Request-Id"), out int requestId);

        if (verb == "REQUEST")
        {
            string service = first[2];
            string reply = service.StartsWith("com.roonlabs.ping:", StringComparison.Ordinal) ? "Success" : "InvalidRequest";
            _ = WriteAsync(Encoding.UTF8.GetBytes($"MOO/1 COMPLETE {reply}\nRequest-Id: {requestId}\n\n"), null);
            return;
        }
        if (_pending.TryGetValue(requestId, out var handler)) handler(new RoonMessage(verb, first[2], body, payload, contentType));
        if (verb == "COMPLETE") _pending.TryRemove(requestId, out _);
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern) => data.AsSpan().IndexOf(pattern);

    public void Dispose()
    {
        _cts.Cancel();
        try { _socket.Abort(); }
        catch (Exception) { }
        _socket.Dispose();
    }
}
