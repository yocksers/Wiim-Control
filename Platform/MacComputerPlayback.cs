using System.Diagnostics;
using System.Globalization;

namespace WiimControl;

sealed class MacComputerPlayback : IComputerPlayback
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(4);

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

    private string? _shown;

    public async Task<ComputerTrack?> ReadAsync()
    {
        ComputerTrack? paused = null;
        foreach (var (process, script) in Players)
        {
            if (!IsRunning(process)) continue;
            string[] lines = (await RunScriptAsync(script) ?? string.Empty).Split('\n');
            if (lines.Length < 7 || lines[1].Trim().Length == 0) continue;
            bool playing = lines[0].Trim() == "playing";
            if (!playing && (process != _shown || lines[0].Trim() != "paused")) continue;
            string title = lines[1].Trim(), artist = lines[2].Trim(), album = lines[3].Trim(), art = lines[6].Trim();
            int duration = (int)Math.Max(0, Number(lines[4]));
            int position = duration > 0 ? (int)Math.Clamp(Number(lines[5]) * 1000, 0, duration) : 0;
            bool web = art.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || art.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            var track = new ComputerTrack($"{process}\n{title}\n{artist}\n{album}", title, artist, album, playing, position, duration,
                DateTime.UtcNow, web ? art : string.Empty, null);
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

    public async Task<bool> ControlAsync(string action)
    {
        if (_shown is not { } process || !IsRunning(process)) return false;
        string command = action switch { "next" => "next track", "prev" => "previous track", _ => "playpause" };
        return await RunScriptAsync($"tell application \"{process}\" to {command}") != null;
    }

    public async Task<bool> SeekAsync(int positionMs)
    {
        if (_shown is not { } process || !IsRunning(process)) return false;
        string seconds = (Math.Max(0, positionMs) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        return await RunScriptAsync($"tell application \"{process}\" to set player position to {seconds}") != null;
    }

    private static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static double Number(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    private static async Task<string?> RunScriptAsync(string script)
    {
        var start = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        try
        {
            using var process = Process.Start(start);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(ScriptTimeout);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(); }
                catch (Exception) { }
                return null;
            }
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception) { return null; }
    }

    public void Dispose() => _shown = null;
}
