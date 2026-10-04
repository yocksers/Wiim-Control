using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WiimControl;

static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WiimControl";
    private const string LegacyRunValueName = "WiimVolumeControl";

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
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key?.GetValue(LegacyRunValueName) == null) return;
            key.DeleteValue(LegacyRunValueName, false);
            key.SetValue(RunValueName, $"\"{ExecutablePath}\"");
        }
        catch (Exception) { }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(RunValueName) as string == $"\"{ExecutablePath}\"";
    }

    [SupportedOSPlatform("windows")]
    private static void SetEnabledWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled) key.SetValue(RunValueName, $"\"{ExecutablePath}\"");
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
            $"  <key>ProgramArguments</key>\n  <array>\n    <string>{path}</string>\n  </array>\n" +
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
            $"Exec=\"{ExecutablePath}\"\n" +
            "Icon=wiim-control\n" +
            "X-GNOME-Autostart-enabled=true\n" +
            "Terminal=false\n");
    }
}
