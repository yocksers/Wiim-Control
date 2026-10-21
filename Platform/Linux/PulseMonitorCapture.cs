using System.Diagnostics;

namespace WiimControl;

sealed class PulseMonitorCapture : IPcAudioCapture
{
    private const string Recorder = "parec";
    private const int Rate = 48000;
    private const int FrameBytes = 2 * 3;
    private const int ChunkFrames = Rate / 100;

    private readonly Process _process;
    private readonly Thread _reader;
    private volatile bool _stopping;

    public int SampleRate => Rate;
    public event Action<byte[]>? Pcm;
    public event Action? Stopped;

    public static bool Available => FindOnPath(Recorder) != null;

    public PulseMonitorCapture()
    {
        var info = new ProcessStartInfo(FindOnPath(Recorder) ?? throw new InvalidOperationException("parec wasn't found."))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[] { "--device=@DEFAULT_MONITOR@", "--format=s24le", $"--rate={Rate}", "--channels=2", "--latency-msec=20" })
            info.ArgumentList.Add(argument);
        _process = Process.Start(info) ?? throw new InvalidOperationException("parec didn't start.");
        _ = _process.StandardError.ReadToEndAsync();
        _reader = new Thread(Read) { IsBackground = true, Name = "PC sound capture" };
        _reader.Start();
    }

    private void Read()
    {
        var stream = _process.StandardOutput.BaseStream;
        var chunk = new byte[ChunkFrames * FrameBytes];
        try
        {
            while (!_stopping)
            {
                int filled = 0;
                while (filled < chunk.Length)
                {
                    int read = stream.Read(chunk, filled, chunk.Length - filled);
                    if (read <= 0) return;
                    filled += read;
                }
                Pcm?.Invoke((byte[])chunk.Clone());
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            if (!_stopping) Stopped?.Invoke();
        }
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);

    public void Dispose()
    {
        _stopping = true;
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (InvalidOperationException) { }
        _process.Dispose();
    }
}
