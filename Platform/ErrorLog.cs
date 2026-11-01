using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WiimControl;

static class ErrorLog
{
    private const long MaxSize = 256 * 1024;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(10);
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly Regex EntryStart = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[", RegexOptions.Compiled);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (DateTime Written, int Skipped)> Recent = new();

    public static string FilePath => Path.Combine(WiimController.ConfigDirectory, "wiim-errors.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                var now = DateTime.Now;
                string signature = $"{source}|{exception?.GetType().FullName}|{exception?.Message}|{exception?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
                int skipped = 0;
                if (Recent.TryGetValue(signature, out var last))
                {
                    if (now - last.Written < RepeatWindow)
                    {
                        Recent[signature] = last with { Skipped = last.Skipped + 1 };
                        return;
                    }
                    skipped = last.Skipped;
                }
                Recent[signature] = (now, 0);
                foreach (var stale in Recent.Where(r => now - r.Value.Written > RepeatWindow && r.Value.Skipped == 0).Select(r => r.Key).ToList())
                    Recent.Remove(stale);

                Directory.CreateDirectory(WiimController.ConfigDirectory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSize) File.Move(FilePath, FilePath + ".old", overwrite: true);
                var text = new StringBuilder();
                text.Append(now.ToString(TimeFormat, CultureInfo.InvariantCulture))
                    .Append($" [{source}] version {typeof(ErrorLog).Assembly.GetName().Version}").AppendLine();
                if (skipped > 0)
                    text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"(The same error also happened {skipped} more time{(skipped == 1 ? "" : "s")} since {last.Written.ToString(TimeFormat, CultureInfo.InvariantCulture)}.)"));
                text.Append(exception).AppendLine().AppendLine();
                File.AppendAllText(FilePath, text.ToString());
            }
        }
        catch (Exception) { }
    }

    public static void Cleanup()
    {
        try
        {
            lock (Gate)
            {
                var cutoff = DateTime.Now - KeepFor;
                string old = FilePath + ".old";
                if (File.Exists(old) && File.GetLastWriteTime(old) < cutoff) File.Delete(old);
                if (!File.Exists(FilePath)) return;

                var lines = File.ReadAllLines(FilePath);
                int keepFrom = lines.Length;
                bool sawOld = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var match = EntryStart.Match(lines[i]);
                    if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value, TimeFormat, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var when)) continue;
                    if (when >= cutoff)
                    {
                        keepFrom = i;
                        break;
                    }
                    sawOld = true;
                }
                if (keepFrom == 0 || !sawOld) return;
                if (keepFrom >= lines.Length) File.Delete(FilePath);
                else File.WriteAllLines(FilePath, lines[keepFrom..]);
            }
        }
        catch (Exception) { }
    }
}
