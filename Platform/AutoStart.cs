using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WiimControl;

static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WiimControl";
    private const string LegacyRunValueName = "WiimVolumeControl";

    public static string Label => OperatingSystem.IsWindows() ? "Start with Windows" : "Start when I log in";

    private static string ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : Environment.ProcessPath ?? string.Empty;

    private static string LinuxDesktopFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "wiim-control.desktop");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return IsEnabledWindows();
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
