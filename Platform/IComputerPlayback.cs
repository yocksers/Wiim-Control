namespace WiimControl;

sealed record ComputerTrack(string Key, string Title, string Artist, string Album, bool Playing, int PositionMs, int DurationMs,
    DateTime PositionAt, string ArtUrl, byte[]? Art);

interface IComputerPlayback : IDisposable
{
    Task<ComputerTrack?> ReadAsync();

    Task<bool> ControlAsync(string action);

    Task<bool> SeekAsync(int positionMs);

    string? Problem => null;
}
