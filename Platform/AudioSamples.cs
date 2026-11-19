namespace WiimControl;

enum AudioChannel { Mix, Left, Right }

interface IAudioSamples : IDisposable
{
    int SampleRate { get; }
    void CopyRecent(float[] destination, AudioChannel channel = AudioChannel.Mix);
}

sealed class SampleRing(int size)
{
    private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(200);
    private readonly float[] _left = new float[size];
    private readonly float[] _right = new float[size];
    private readonly object _gate = new();
    private int _write;
    private DateTime _last = DateTime.MinValue;

    public void Push(float[] left, float[] right, int count)
    {
        lock (_gate)
        {
            for (int i = 0; i < count; i++)
            {
                _left[_write] = left[i];
                _right[_write] = right[i];
                _write = (_write + 1) % _left.Length;
            }
            _last = DateTime.UtcNow;
        }
    }

    public void CopyRecent(float[] destination, AudioChannel channel)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _last > Stale)
            {
                Array.Clear(destination);
                return;
            }
            int size = _left.Length;
            int count = Math.Min(destination.Length, size);
            int start = (_write - count + size) % size;
            int offset = destination.Length - count;
            for (int i = 0; i < count; i++)
            {
                int at = (start + i) % size;
                destination[offset + i] = channel switch
                {
                    AudioChannel.Left => _left[at],
                    AudioChannel.Right => _right[at],
                    _ => (_left[at] + _right[at]) / 2
                };
            }
            if (offset > 0) Array.Clear(destination, 0, offset);
        }
    }
}

static class AudioSamples
{
    internal const int RingSize = 16384;

    internal static bool Supported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() && PulseMonitorCapture.Available || OperatingSystem.IsMacOS() && MacAudioSamples.Available;

    internal static IAudioSamples? Open(string? outputId)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsAudioSamples.Open(outputId);
            if (OperatingSystem.IsLinux()) return LinuxAudioSamples.Open(outputId);
            if (OperatingSystem.IsMacOS()) return MacAudioSamples.Open(outputId);
        }
        catch (Exception ex) { ErrorLog.Unexpected("Sound capture", ex); }
        return null;
    }
}
