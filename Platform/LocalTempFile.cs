namespace WiimControl;

static class LocalTempFile
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/flac"] = ".flac", ["audio/x-flac"] = ".flac", ["audio/mpeg"] = ".mp3", ["audio/mp3"] = ".mp3", ["audio/mp4"] = ".m4a",
        ["audio/x-m4a"] = ".m4a", ["audio/aac"] = ".aac", ["audio/wav"] = ".wav", ["audio/x-wav"] = ".wav", ["audio/aiff"] = ".aiff",
        ["audio/x-aiff"] = ".aiff", ["audio/ogg"] = ".ogg", ["audio/opus"] = ".opus", ["audio/x-ms-wma"] = ".wma"
    };

    private static string Folder => Path.Combine(Path.GetTempPath(), "WiimControl-player");

    internal static string Write(Stream stream, string contentType)
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, Guid.NewGuid().ToString("N") + Extensions.GetValueOrDefault(contentType, ".audio"));
        using (var file = File.Create(path)) stream.CopyTo(file);
        return path;
    }

    internal static void Delete(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
