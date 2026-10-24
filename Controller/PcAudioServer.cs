using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace WiimControl;

sealed class PcAudioServer : IDisposable
{
    public const string StreamPath = "/pc.wav";
    private const int Channels = 2;
    public const int StreamBits = 24;
    private const int BitsPerSample = StreamBits;
    private static readonly TimeSpan CatchUpAfter = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CatchUp = TimeSpan.FromMilliseconds(1200);
    private const int QueuedChunks = 300;
    private const int MaxRequestBytes = 8192;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FillInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SilenceAfter = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxSilence = TimeSpan.FromSeconds(1);
    private const int FrameBytes = Channels * BitsPerSample / 8;

    private readonly TcpListener _listener = new(IPAddress.Any, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<byte[]>> _clients = [];
    private readonly int _sampleRate;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Timer _filler;
    private TimeSpan _lastAudio;
    private TimeSpan _silenceUntil;

    public int SampleRate => _sampleRate;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int ClientCount { get { lock (_clients) return _clients.Count; } }
    public DateTime LastClientSeen { get; private set; } = DateTime.UtcNow;
    public IPAddress? AllowedClient { get; set; }

    public PcAudioServer(int sampleRate)
    {
        _sampleRate = sampleRate;
        _filler = new Timer(_ => FillSilence(), null, FillInterval, FillInterval);
        _listener.Start();
        _ = AcceptAsync();
    }

    public void Write(byte[] pcm)
    {
        lock (_clients)
        {
            _lastAudio = _silenceUntil = _clock.Elapsed;
            if (_clients.Count > 0) LastClientSeen = DateTime.UtcNow;
            foreach (var client in _clients) client.Writer.TryWrite(pcm);
        }
    }

    private void FillSilence()
    {
        lock (_clients)
        {
            var now = _clock.Elapsed;
            if (now - _lastAudio < SilenceAfter) return;
            var gap = now - _silenceUntil;
            if (gap > MaxSilence) gap = MaxSilence;
            _silenceUntil = now;
            long frames = (long)(gap.TotalSeconds * _sampleRate);
            if (frames <= 0 || _clients.Count == 0) return;
            var silence = new byte[frames * FrameBytes];
            foreach (var client in _clients) client.Writer.TryWrite(silence);
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            if (!IsAllowed(tcp))
            {
                tcp.Dispose();
                continue;
            }
            _ = ServeAsync(tcp);
        }
    }

    private bool IsAllowed(TcpClient tcp) =>
        AllowedClient is not { } allowed ||
        (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().Equals(allowed.MapToIPv4()) == true;

    private async Task ServeAsync(TcpClient tcp)
    {
        using var _ = tcp;
        var stream = tcp.GetStream();
        Channel<byte[]>? queue = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(RequestTimeout);
            string? request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
            var parts = request?.Split(' ');
            if (parts is not [var method, var path, ..] || !path.StartsWith(StreamPath, StringComparison.Ordinal))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token)
                    .ConfigureAwait(false);
                return;
            }
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"), _stop.Token).ConfigureAwait(false);
            if (method == "HEAD") return;
            await stream.WriteAsync(WavHeader(), _stop.Token).ConfigureAwait(false);

            queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueuedChunks)
            {
                FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
            });
            lock (_clients) _clients.Add(queue);
            var streaming = System.Diagnostics.Stopwatch.StartNew();
            long catchUpBytes = (long)(CatchUp.TotalSeconds * _sampleRate) * Channels * BitsPerSample / 8, skipped = 0;
            await foreach (var chunk in queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (skipped < catchUpBytes && streaming.Elapsed > CatchUpAfter)
                {
                    skipped += chunk.Length;
                    continue;
                }
                await stream.WriteAsync(chunk, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            if (queue != null)
                lock (_clients) _clients.Remove(queue);
        }
    }

    private static async Task<string?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[MaxRequestBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) return null;
            length += read;
            string text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text[..text.IndexOf('\r')];
        }
        return null;
    }

    private byte[] WavHeader()
    {
        int blockAlign = Channels * BitsPerSample / 8;
        using var header = new MemoryStream();
        using var writer = new BinaryWriter(header);
        writer.Write("RIFF"u8);
        writer.Write(uint.MaxValue);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(_sampleRate);
        writer.Write(_sampleRate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)BitsPerSample);
        writer.Write("data"u8);
        writer.Write(uint.MaxValue);
        writer.Flush();
        return header.ToArray();
    }

    public void Dispose()
    {
        _filler.Dispose();
        _stop.Cancel();
        _listener.Stop();
        lock (_clients)
        {
            foreach (var client in _clients) client.Writer.TryComplete();
            _clients.Clear();
        }
    }
}
