using System.Diagnostics;

namespace WiimControl;

sealed class LinuxAudioSamples : IAudioSamples
{
    private const int Rate = 44100;
    private const int Channels = 2;
    private const int ChunkFrames = 441;

    private readonly Process _process;
    private readonly SampleRing _ring = new(AudioSamples.RingSize);
    private volatile bool _stopped;

    private LinuxAudioSamples(Process process)
    {
        _process = process;
        new Thread(ReadLoop) { IsBackground = true, Name = "Audio samples" }.Start();
    }

    public int SampleRate => Rate;

    internal static LinuxAudioSamples? Open(string? outputId)
    {
        if (!PulseMonitorCapture.Available) return null;
        var start = new ProcessStartInfo("parec")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("--device=" + (outputId is { Length: > 0 } ? outputId + ".monitor" : "@DEFAULT_MONITOR@"));
        start.ArgumentList.Add("--format=float32le");
        start.ArgumentList.Add($"--rate={Rate}");
        start.ArgumentList.Add($"--channels={Channels}");
        start.ArgumentList.Add("--latency-msec=20");
        start.ArgumentList.Add("--client-name=Wiim Control");
        start.Environment["LC_ALL"] = "C";
        var process = Process.Start(start);
        if (process == null) return null;
        _ = process.StandardError.ReadToEndAsync();
        return new LinuxAudioSamples(process);
    }

    private void ReadLoop()
    {
        var buffer = new byte[ChunkFrames * Channels * sizeof(float)];
        var left = new float[ChunkFrames];
        var right = new float[ChunkFrames];
        var stream = _process.StandardOutput.BaseStream;
        int filled = 0;
        try
        {
            while (!_stopped)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read <= 0) return;
                filled += read;
                int frames = filled / (Channels * sizeof(float));
                if (frames == 0) continue;
                for (int i = 0; i < frames; i++)
                {
                    left[i] = BitConverter.ToSingle(buffer, i * 8);
                    right[i] = BitConverter.ToSingle(buffer, i * 8 + 4);
                }
                _ring.Push(left, right, frames);
                int used = frames * Channels * sizeof(float);
                Array.Copy(buffer, used, buffer, 0, filled - used);
                filled -= used;
            }
        }
        catch (Exception) { }
    }

    public void CopyRecent(float[] destination, AudioChannel channel = AudioChannel.Mix) => _ring.CopyRecent(destination, channel);

    public void Dispose()
    {
        _stopped = true;
        try { if (!_process.HasExited) _process.Kill(); } catch (Exception) { }
        _process.Dispose();
    }
}
