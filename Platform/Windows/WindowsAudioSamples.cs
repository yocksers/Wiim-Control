using System.Runtime.Versioning;

namespace WiimControl;

[SupportedOSPlatform("windows")]
sealed class WindowsAudioSamples : IAudioSamples
{
    private readonly LoopbackCapture _capture;
    private readonly SampleRing _ring = new(AudioSamples.RingSize);
    private float[] _left = new float[4096];
    private float[] _right = new float[4096];

    private WindowsAudioSamples(LoopbackCapture capture)
    {
        _capture = capture;
        _capture.Pcm += OnPcm;
    }

    public int SampleRate => _capture.SampleRate;

    internal static WindowsAudioSamples? Open(string? outputId)
    {
        try { return new WindowsAudioSamples(new LoopbackCapture(outputId)); }
        catch (Exception) { return null; }
    }

    private void OnPcm(byte[] pcm)
    {
        const int frameBytes = LoopbackCapture.Channels * LoopbackCapture.BitsPerSample / 8;
        int frames = pcm.Length / frameBytes;
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
        }
        for (int f = 0; f < frames; f++)
        {
            int at = f * frameBytes;
            _left[f] = (pcm[at] | (pcm[at + 1] << 8) | ((sbyte)pcm[at + 2] << 16)) / 8388608f;
            _right[f] = (pcm[at + 3] | (pcm[at + 4] << 8) | ((sbyte)pcm[at + 5] << 16)) / 8388608f;
        }
        _ring.Push(_left, _right, frames);
    }

    public void CopyRecent(float[] destination, AudioChannel channel = AudioChannel.Mix) => _ring.CopyRecent(destination, channel);

    public void Dispose()
    {
        _capture.Pcm -= OnPcm;
        _capture.Dispose();
    }
}
