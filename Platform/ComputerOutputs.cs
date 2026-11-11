using System.Diagnostics;

namespace WiimControl;

static class ComputerOutputs
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    internal static List<(string Id, string Name)> List()
    {
        if (OperatingSystem.IsWindows())
        {
            try { return OutputDeviceMonitor.GetActiveOutputDevices(); }
            catch (Exception) { return []; }
        }
        if (!OperatingSystem.IsLinux() || Pactl("list", "sinks") is not { } text) return [];
        var outputs = new List<(string Id, string Name)>();
        string? name = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Sink #", StringComparison.Ordinal)) name = null;
            else if (line.StartsWith("Name:", StringComparison.Ordinal)) name = line[5..].Trim();
            else if (line.StartsWith("Description:", StringComparison.Ordinal) && name is { Length: > 0 } id)
            {
                string description = line[12..].Trim();
                outputs.Add((id, description.Length > 0 ? description : id));
                name = null;
            }
        }
        return outputs;
    }

    internal static string? DefaultId()
    {
        if (OperatingSystem.IsWindows())
        {
            try { return OutputDeviceMonitor.GetDefaultOutputId(); }
            catch (Exception) { return null; }
        }
        if (!OperatingSystem.IsLinux()) return null;
        if (Pactl("get-default-sink")?.Trim() is { Length: > 0 } sink && !sink.Contains(' ')) return sink;
        foreach (string line in (Pactl("info") ?? string.Empty).Split('\n'))
            if (line.Trim().StartsWith("Default Sink:", StringComparison.Ordinal) && line.Trim()[13..].Trim() is { Length: > 0 } fallback)
                return fallback;
        return null;
    }

    private static string? Pactl(params string[] arguments)
    {
        var start = new ProcessStartInfo("pactl")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["LC_ALL"] = "C";
        try
        {
            using var process = Process.Start(start);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(CommandTimeout))
            {
                try { process.Kill(); } catch (Exception) { }
                return null;
            }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception) { return null; }
    }
}
