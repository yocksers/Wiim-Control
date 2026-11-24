namespace WiimControl;

interface ILocalPlayer : IDisposable
{
    event Action? Ended;
    event Action<string>? Failed;
    event Action? StateChanged;

    bool Playing { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    bool Muted { get; set; }

    void Open(Uri url);
    void Open(Stream stream, string contentType);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
}
