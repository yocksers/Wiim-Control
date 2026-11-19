using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace WiimControl;

static class WindowChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_APPWINDOW = 0x00040000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr Handle(Window window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : IntPtr.Zero;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        window.Opened += (_, _) =>
        {
            var hwnd = Handle(window);
            if (hwnd == IntPtr.Zero) return;
            int on = 1;
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
            catch (Exception) { }
        };
    }

    public static void MakeToolWindow(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = (style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }
}
