#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace WiimControl;

sealed class WindowsTouchKeyboard : ITouchKeyboard
{
    private static readonly Guid TipInvocationClass = new("4ce576fa-83dc-4f88-951c-9d0782b4e376");
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan TabTipStart = TimeSpan.FromSeconds(1);
    private bool _expectOpen;
    private DateTime _toggledAt = DateTime.MinValue;

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr window);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    public void Show(TopLevel top)
    {
        if (IsOpen(top)) return;
        if (Toggle(open: true)) return;
        StartTabTip();
        DispatcherTimer.RunOnce(() =>
        {
            if (!IsOpen(top)) Toggle(open: true);
        }, TabTipStart);
    }

    public void Hide(TopLevel top)
    {
        if (IsOpen(top)) Toggle(open: false);
    }

    private bool IsOpen(TopLevel top) =>
        DateTime.UtcNow - _toggledAt < Settle || top.InputPane == null ? _expectOpen : top.InputPane.State == InputPaneState.Open;

    private bool Toggle(bool open)
    {
        try
        {
            if (Type.GetTypeFromCLSID(TipInvocationClass) is not { } type || Activator.CreateInstance(type) is not ITipInvocation tip) return false;
            try { tip.Toggle(GetDesktopWindow()); }
            finally { Marshal.ReleaseComObject(tip); }
            _expectOpen = open;
            _toggledAt = DateTime.UtcNow;
            return true;
        }
        catch (COMException) { return false; }
        catch (InvalidCastException) { return false; }
    }

    private static void StartTabTip()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");
        if (!File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { ErrorLog.Unexpected("Touch keyboard", ex); }
    }
}
#endif
