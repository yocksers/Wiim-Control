using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WiimControl;

static class ThisComputer
{
    private const int ToolTimeoutMs = 2000;
    private static readonly Lazy<string[]> LazyNames = new(FindNames);

    internal static string[] Names => LazyNames.Value;

    internal static Task<DeviceVolume?> GetVolumeAsync() => Task.Run(GetVolume);

    internal static Task<bool> SetVolumeAsync(int volume) => Task.Run(() => SetVolume(Math.Clamp(volume, 0, 100)));

    internal static Task<bool> SetMutedAsync(bool muted) => Task.Run(() => SetMuted(muted));

    private static DeviceVolume? GetVolume()
    {
        if (OperatingSystem.IsWindows()) return EndpointVolume.Get();
        if (OperatingSystem.IsLinux())
        {
            var volume = Regex.Match(Run("pactl", "get-sink-volume", "@DEFAULT_SINK@") ?? string.Empty, @"(\d+)%");
            var mute = Run("pactl", "get-sink-mute", "@DEFAULT_SINK@");
            if (!volume.Success || mute == null) return null;
            return new DeviceVolume(Math.Clamp(int.Parse(volume.Groups[1].Value), 0, 100), mute.Contains("yes", StringComparison.OrdinalIgnoreCase));
        }
        if (OperatingSystem.IsMacOS())
        {
            string settings = Run("osascript", "-e", "get volume settings") ?? string.Empty;
            var volume = Regex.Match(settings, @"output volume:(\d+)");
            var mute = Regex.Match(settings, @"output muted:(true|false)");
            if (!volume.Success || !mute.Success) return null;
            return new DeviceVolume(Math.Clamp(int.Parse(volume.Groups[1].Value), 0, 100), mute.Groups[1].Value == "true");
        }
        return null;
    }

    private static bool SetVolume(int volume)
    {
        if (OperatingSystem.IsWindows()) return EndpointVolume.SetVolume(volume);
        if (OperatingSystem.IsLinux()) return Run("pactl", "set-sink-volume", "@DEFAULT_SINK@", $"{volume}%") != null;
        if (OperatingSystem.IsMacOS()) return Run("osascript", "-e", $"set volume output volume {volume}") != null;
        return false;
    }

    private static bool SetMuted(bool muted)
    {
        if (OperatingSystem.IsWindows()) return EndpointVolume.SetMuted(muted);
        if (OperatingSystem.IsLinux()) return Run("pactl", "set-sink-mute", "@DEFAULT_SINK@", muted ? "1" : "0") != null;
        if (OperatingSystem.IsMacOS()) return Run("osascript", "-e", $"set volume output muted {(muted ? "true" : "false")}") != null;
        return false;
    }

    private static string[] FindNames()
    {
        var names = new List<string> { Environment.MachineName };
        try { names.Add(System.Net.Dns.GetHostName()); }
        catch (System.Net.Sockets.SocketException) { }
        if (OperatingSystem.IsLinux() && Run("bluetoothctl", "show") is { } controller)
            foreach (Match match in Regex.Matches(controller, @"^\s*(?:Name|Alias):\s*(.+?)\s*$", RegexOptions.Multiline))
                names.Add(match.Groups[1].Value);
        if (OperatingSystem.IsMacOS() && Run("scutil", "--get", "ComputerName") is { } macName) names.Add(macName.Trim());
        return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? Run(string tool, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ToolTimeoutMs))
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
                return null;
            }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }
}
