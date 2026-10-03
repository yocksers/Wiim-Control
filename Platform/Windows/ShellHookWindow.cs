using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WiimControl;

[SupportedOSPlatform("windows")]
sealed class ShellHookWindow : IDisposable
{
    private const int HSHELL_APPCOMMAND      = 12;
    private const int APPCOMMAND_VOLUME_MUTE = 8;
    private const int APPCOMMAND_VOLUME_DOWN = 9;
    private const int APPCOMMAND_VOLUME_UP   = 10;
    private const int APPCOMMAND_MEDIA_NEXTTRACK     = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
    private const int APPCOMMAND_MEDIA_STOP          = 13;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE    = 14;

    private const int  WM_HOTKEY          = 0x0312;
    private const int  HOTKEY_VOLUME_UP   = 0xB001;
    private const int  HOTKEY_VOLUME_DOWN = 0xB002;
    private const int  HOTKEY_VOLUME_MUTE = 0xB003;
    private const int  HOTKEY_MEDIA_PLAYPAUSE = 0xB004;
    private const int  HOTKEY_MEDIA_NEXT      = 0xB005;
    private const int  HOTKEY_MEDIA_PREV      = 0xB006;
    private const int  HOTKEY_MEDIA_STOP      = 0xB007;
    private const uint VK_VOLUME_MUTE     = 0xAD;
    private const uint VK_VOLUME_DOWN     = 0xAE;
    private const uint VK_VOLUME_UP       = 0xAF;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP       = 0xB2;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool DeregisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string msg);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly string _className = $"WiimControlShellHook{Environment.ProcessId}";
    private readonly WndProcDelegate _wndProc;
    private readonly Action<string> _onCommand;
    private readonly IntPtr _hInstance;
    private readonly IntPtr _hWnd;
    private readonly uint _shellMsg;
    private bool _mediaKeysEnabled;

    public ShellHookWindow(Action<string> onCommand, bool forwardMediaKeys = false)
    {
        _onCommand = onCommand;
        _mediaKeysEnabled = forwardMediaKeys;
        _wndProc = WndProc;
        _hInstance = GetModuleHandle(null);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _className
        };
        RegisterClassEx(ref wc);
        _hWnd = CreateWindowEx(WS_EX_TOOLWINDOW, _className, string.Empty, WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (_hWnd == IntPtr.Zero) return;

        _shellMsg = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(_hWnd);
        RegisterHotKey(_hWnd, HOTKEY_VOLUME_UP,   0, VK_VOLUME_UP);
        RegisterHotKey(_hWnd, HOTKEY_VOLUME_DOWN, 0, VK_VOLUME_DOWN);
        RegisterHotKey(_hWnd, HOTKEY_VOLUME_MUTE, 0, VK_VOLUME_MUTE);
        if (_mediaKeysEnabled) RegisterMediaHotkeys();
    }

    public void SetMediaKeysEnabled(bool enabled)
    {
        if (enabled == _mediaKeysEnabled) return;
        _mediaKeysEnabled = enabled;
        if (_hWnd == IntPtr.Zero) return;
        if (enabled) RegisterMediaHotkeys();
        else         UnregisterMediaHotkeys();
    }

    private void RegisterMediaHotkeys()
    {
        RegisterHotKey(_hWnd, HOTKEY_MEDIA_PLAYPAUSE, 0, VK_MEDIA_PLAY_PAUSE);
        RegisterHotKey(_hWnd, HOTKEY_MEDIA_NEXT,      0, VK_MEDIA_NEXT_TRACK);
        RegisterHotKey(_hWnd, HOTKEY_MEDIA_PREV,      0, VK_MEDIA_PREV_TRACK);
        RegisterHotKey(_hWnd, HOTKEY_MEDIA_STOP,      0, VK_MEDIA_STOP);
    }

    private void UnregisterMediaHotkeys()
    {
        UnregisterHotKey(_hWnd, HOTKEY_MEDIA_PLAYPAUSE);
        UnregisterHotKey(_hWnd, HOTKEY_MEDIA_NEXT);
        UnregisterHotKey(_hWnd, HOTKEY_MEDIA_PREV);
        UnregisterHotKey(_hWnd, HOTKEY_MEDIA_STOP);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            string? hotkeyCmd = (int)wParam switch
            {
                HOTKEY_VOLUME_UP       => "vol++",
                HOTKEY_VOLUME_DOWN     => "vol--",
                HOTKEY_VOLUME_MUTE     => "mute",
                HOTKEY_MEDIA_PLAYPAUSE => "playpause",
                HOTKEY_MEDIA_NEXT      => "next",
                HOTKEY_MEDIA_PREV      => "prev",
                HOTKEY_MEDIA_STOP      => "stop",
                _                      => null
            };
            if (hotkeyCmd != null)
            {
                _onCommand(hotkeyCmd);
                return IntPtr.Zero;
            }
        }

        if (_shellMsg != 0 && msg == _shellMsg && (int)wParam == HSHELL_APPCOMMAND)
        {
            int appCmd = (int)(((ulong)(long)lParam >> 16) & 0x0FFF);
            string? cmd = appCmd switch
            {
                APPCOMMAND_VOLUME_UP   => "vol++",
                APPCOMMAND_VOLUME_DOWN => "vol--",
                APPCOMMAND_VOLUME_MUTE => "mute",
                APPCOMMAND_MEDIA_PLAY_PAUSE    when _mediaKeysEnabled => "playpause",
                APPCOMMAND_MEDIA_NEXTTRACK     when _mediaKeysEnabled => "next",
                APPCOMMAND_MEDIA_PREVIOUSTRACK when _mediaKeysEnabled => "prev",
                APPCOMMAND_MEDIA_STOP          when _mediaKeysEnabled => "stop",
                _                      => null
            };
            if (cmd != null)
            {
                _onCommand(cmd);
                return (IntPtr)1;
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            DeregisterShellHookWindow(_hWnd);
            UnregisterHotKey(_hWnd, HOTKEY_VOLUME_UP);
            UnregisterHotKey(_hWnd, HOTKEY_VOLUME_DOWN);
            UnregisterHotKey(_hWnd, HOTKEY_VOLUME_MUTE);
            UnregisterMediaHotkeys();
            DestroyWindow(_hWnd);
        }
        UnregisterClass(_className, _hInstance);
    }
}
