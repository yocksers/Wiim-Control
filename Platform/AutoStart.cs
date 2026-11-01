using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WiimControl;

static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WiimControl";
    private const string LegacyRunValueName = "WiimVolumeControl";
    public const string MinimizedArgument = "--minimized";

    public static string Label =>
        OperatingSystem.IsWindows() ? "Start with Windows" : OperatingSystem.IsMacOS() ? "Start at login" : "Start when I log in";

    private static string ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : Environment.ProcessPath ?? string.Empty;

    private static string LinuxDesktopFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "wiim-control.desktop");

    private static string MacLaunchAgent =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.wiimcontrol.app.plist");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return IsEnabledWindows();
            if (OperatingSystem.IsMacOS()) return File.Exists(MacLaunchAgent);
            return File.Exists(LinuxDesktopFile);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) SetEnabledWindows(enabled);
            else if (OperatingSystem.IsMacOS())
            {
                if (enabled) WriteMacLaunchAgent();
                else if (File.Exists(MacLaunchAgent)) File.Delete(MacLaunchAgent);
            }
            else if (enabled) WriteLinuxDesktopFile();
            else if (File.Exists(LinuxDesktopFile)) File.Delete(LinuxDesktopFile);
        }
        catch (Exception) { }
    }

    public static void MigrateLegacy()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key?.GetValue(LegacyRunValueName) != null)
                {
                    key.DeleteValue(LegacyRunValueName, false);
                    key.SetValue(RunValueName, WindowsCommand);
                }
            }
            if (OperatingSystem.IsWindows()) RepairWindows();
            else if (IsEnabled()) SetEnabled(true);
        }
        catch (Exception) { }
    }

    private static string WindowsCommand => $"\"{ExecutablePath}\" {MinimizedArgument}";

    [SupportedOSPlatform("windows")]
    private static string? WindowsEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(RunValueName) as string;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows() => WindowsEntry() is { Length: > 0 };

    internal static string ProgramOf(string command)
    {
        string text = command.Trim();
        if (text.StartsWith('"') && text.IndexOf('"', 1) is int end and > 0) return text[1..end];
        int space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    [SupportedOSPlatform("windows")]
    private static void RepairWindows()
    {
        if (WindowsEntry() is not { Length: > 0 } command || command == WindowsCommand) return;
        if (!File.Exists(ProgramOf(command))) SetEnabledWindows(true);
    }

    [SupportedOSPlatform("windows")]
    private static void SetEnabledWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled) key.SetValue(RunValueName, WindowsCommand);
        else         key.DeleteValue(RunValueName, false);
    }

    private static void WriteMacLaunchAgent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MacLaunchAgent)!);
        string path = System.Security.SecurityElement.Escape(ExecutablePath) ?? string.Empty;
        File.WriteAllText(MacLaunchAgent,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            "  <key>Label</key>\n  <string>com.wiimcontrol.app</string>\n" +
            $"  <key>ProgramArguments</key>\n  <array>\n    <string>{path}</string>\n    <string>{MinimizedArgument}</string>\n  </array>\n" +
            "  <key>RunAtLoad</key>\n  <true/>\n" +
            "</dict>\n</plist>\n");
    }

    private static void WriteLinuxDesktopFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxDesktopFile)!);
        File.WriteAllText(LinuxDesktopFile,
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Wiim Control\n" +
            $"Exec=\"{ExecutablePath}\" {MinimizedArgument}\n" +
            "Icon=wiim-control\n" +
            "X-GNOME-Autostart-enabled=true\n" +
            "Terminal=false\n");
    }
}
