namespace WiimControl;

interface IPcAudioCapture : IDisposable
{
    int SampleRate { get; }
    event Action<byte[]>? Pcm;
    event Action? Stopped;
}
