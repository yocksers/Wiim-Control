using System.Diagnostics;
using System.Globalization;

namespace WiimControl;

sealed class MacComputerPlayback : IComputerPlayback
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(4);
    private const string NotPermitted = "-1743";
    private const int MaxArtBytes = 8 * 1024 * 1024;
    private const int ArtTries = 5;

    private static readonly (string Process, string Script)[] Players =
    [
        ("Spotify", """
            tell application "Spotify"
              if player state is stopped then return ""
              set t to current track
              return (player state as text) & linefeed & (name of t) & linefeed & (artist of t) & linefeed & (album of t) & linefeed & ((duration of t) as text) & linefeed & ((player position) as text) & linefeed & (artwork url of t)
            end tell
            """),
        ("Music", """
            tell application "Music"
              if player state is stopped then return ""
              set t to current track
              return (player state as text) & linefeed & (name of t) & linefeed & (artist of t) & linefeed & (album of t) & linefeed & (((duration of t) * 1000) as integer as text) & linefeed & ((player position) as text) & linefeed & ""
            end tell
            """)
    ];

    private const string MusicArtScript = """
        tell application "Music"
          if (count of artworks of current track) is 0 then return ""
          return raw data of artwork 1 of current track
        end tell
        """;

    private string? _shown;
    private readonly HashSet<string> _denied = new(StringComparer.Ordinal);
    private string _artKey = string.Empty;
    private byte[]? _art;
    private int _artTries;

    public string? Problem => _denied.Count == 0 ? null
        : $"macOS doesn't allow Wiim Control to read {string.Join(" and ", _denied.Order())}. Allow it under System Settings → Privacy & Security → " +
          "Automation → Wiim Control, then switch the app on there.";

    public async Task<ComputerTrack?> ReadAsync()
    {
        ComputerTrack? paused = null;
        foreach (var (process, script) in Players)
        {
            if (!IsRunning(process)) continue;
            var (output, denied) = await RunScriptAsync(script);
            if (denied) _denied.Add(process);
            else if (output != null) _denied.Remove(process);
            string[] lines = (output ?? string.Empty).Split('\n');
            if (lines.Length < 7 || lines[1].Trim().Length == 0) continue;
            bool playing = lines[0].Trim() == "playing";
            if (!playing && (process != _shown || lines[0].Trim() != "paused")) continue;
            string title = lines[1].Trim(), artist = lines[2].Trim(), album = lines[3].Trim(), art = lines[6].Trim();
            int duration = (int)Math.Max(0, Number(lines[4]));
            int position = duration > 0 ? (int)Math.Clamp(Number(lines[5]) * 1000, 0, duration) : 0;
            bool web = art.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || art.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            string key = $"{process}\n{title}\n{artist}\n{album}";
            byte[]? artBytes = process == "Music" ? await MusicArtAsync(key) : null;
            var track = new ComputerTrack(key, title, artist, album, playing, position, duration, DateTime.UtcNow, web ? art : string.Empty, artBytes);
            if (playing)
            {
                _shown = process;
                return track;
            }
            paused = track;
        }
        if (paused == null) _shown = null;
        return paused;
    }

    private async Task<byte[]?> MusicArtAsync(string key)
    {
        if (key != _artKey)
        {
            _artKey = key;
            _art = null;
            _artTries = 0;
        }
        if (_art != null || _artTries >= ArtTries) return _art;
        _artTries++;
        _art = ParseScriptData((await RunScriptAsync(MusicArtScript)).Output);
        return _art;
    }

    internal static byte[]? ParseScriptData(string? output)
    {
        if (output == null) return null;
        int start = output.IndexOf("«data ", StringComparison.Ordinal);
        int end = start < 0 ? -1 : output.IndexOf('»', start);
        if (start < 0 || end < 0) return null;
        string hex = output[(start + 10)..end].Trim();
        if (hex.Length < 8 || hex.Length % 2 != 0 || hex.Length / 2 > MaxArtBytes) return null;
        try { return Convert.FromHexString(hex); }
        catch (FormatException) { return null; }
    }

    public async Task<bool> ControlAsync(string action)
    {
        if (_shown is not { } process || !IsRunning(process)) return false;
        string command = action switch { "next" => "next track", "prev" => "previous track", _ => "playpause" };
        return (await RunScriptAsync($"tell application \"{process}\" to {command}")).Output != null;
    }

    public async Task<bool> SeekAsync(int positionMs)
    {
        if (_shown is not { } process || !IsRunning(process)) return false;
        string seconds = (Math.Max(0, positionMs) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        return (await RunScriptAsync($"tell application \"{process}\" to set player position to {seconds}")).Output != null;
    }

    private static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static double Number(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    private static async Task<(string? Output, bool Denied)> RunScriptAsync(string script)
    {
        var start = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        try
        {
            using var process = Process.Start(start);
            if (process == null) return (null, false);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(ScriptTimeout);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(); }
                catch (Exception) { }
                return (null, false);
            }
            if (process.ExitCode == 0) return (await output, false);
            return (null, (await error).Contains(NotPermitted, StringComparison.Ordinal));
        }
        catch (Exception) { return (null, false); }
    }

    public void Dispose() => _shown = null;
}
