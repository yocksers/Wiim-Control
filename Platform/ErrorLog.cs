namespace WiimControl;

static class ErrorLog
{
    private const long MaxSize = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(WiimController.ConfigDirectory, "wiim-errors.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(WiimController.ConfigDirectory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSize) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath,
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}") + $" [{source}] version {typeof(ErrorLog).Assembly.GetName().Version}{Environment.NewLine}" +
                    $"{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }
}
