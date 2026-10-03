using System.Runtime.InteropServices;

namespace WiimControl;

sealed partial class WiimController
{
    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_KEYDOWN     = 0x0100;
    private const int  WM_SYSKEYDOWN  = 0x0104;
    private const uint VK_VOLUME_MUTE = 0xAD;
    private const uint VK_VOLUME_DOWN = 0xAE;
    private const uint VK_VOLUME_UP   = 0xAF;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP       = 0xB2;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    private bool Debounce()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCmd).TotalMilliseconds < 80) return false;
        _lastCmd = now;
        return true;
    }

    private IntPtr InstallHook()
    {
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        var mod = proc.MainModule ?? throw new InvalidOperationException("Cannot get main module.");
        return SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var ks = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            string? cmd = ks.vkCode switch
            {
                VK_VOLUME_UP   => "vol++",
                VK_VOLUME_DOWN => "vol--",
                VK_VOLUME_MUTE => "mute",
                VK_MEDIA_PLAY_PAUSE when _forwardMediaKeys => "playpause",
                VK_MEDIA_NEXT_TRACK  when _forwardMediaKeys => "next",
                VK_MEDIA_PREV_TRACK  when _forwardMediaKeys => "prev",
                VK_MEDIA_STOP        when _forwardMediaKeys => "stop",
                _              => null
            };
            if (cmd != null)
            {
                if (_outputMonitor is null || _outputMonitor.IsCurrentDeviceEnabled())
                {
                    bool isVolumeCmd = cmd is "vol++" or "vol--" or "mute";
                    if (Debounce())
                    {
                        if (cmd is "vol++" or "vol--")
                            _ = ChangeVolumeAsync(cmd == "vol++" ? 1 : -1);
                        else if (cmd == "mute")
                            _ = ToggleMuteAsync();
                        else
                            _ = SendMediaCommandAsync(cmd);
                    }
                    if (!isVolumeCmd || _suppress) return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }
}
