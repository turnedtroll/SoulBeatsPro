using System.Runtime.InteropServices;

namespace larpLOLv4;

// ── Win32 P/Invoke for keyboard simulation, hooks, and window detection ──

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public INPUTUNION u;
}

[StructLayout(LayoutKind.Explicit)]
internal struct INPUTUNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

internal static class NativeApi
{
    // ── Keyboard ──
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ── Cursor ──
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    // ── Window enumeration ──
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    // ── Low-level keyboard hook ──
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── DPI awareness ──
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    // ── Monitor info ──
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // ── Virtual key codes ──
    public const int VK_Z = 0x5A;
    public const int VK_X = 0x58;
    public const int VK_OEM_COMMA = 0xBC;
    public const int VK_OEM_PERIOD = 0xBE;
    public const int VK_L = 0x4C;
    public const int VK_P = 0x50;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_S = 0x53;
    public const int VK_SPACE = 0x20;

    // scan codes for the 4 lane keys
    public static readonly ushort SCAN_Z = (ushort)MapVirtualKey((uint)VK_Z, 0);
    public static readonly ushort SCAN_X = (ushort)MapVirtualKey((uint)VK_X, 0);
    public static readonly ushort SCAN_COMMA = (ushort)MapVirtualKey((uint)VK_OEM_COMMA, 0);
    public static readonly ushort SCAN_PERIOD = (ushort)MapVirtualKey((uint)VK_OEM_PERIOD, 0);

    public static readonly ushort[] LANE_SCANS = [SCAN_Z, SCAN_X, SCAN_COMMA, SCAN_PERIOD];

    public static void PressKey(int lane)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wScan = LANE_SCANS[lane],
                    dwFlags = KEYEVENTF_SCANCODE
                }
            }
        };
        SendInput(1, [inp], Marshal.SizeOf<INPUT>());
    }

    public static void ReleaseKey(int lane)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wScan = LANE_SCANS[lane],
                    dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP
                }
            }
        };
        SendInput(1, [inp], Marshal.SizeOf<INPUT>());
    }

    public static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// Find the Roblox window and return its center point, or null.
    public static (int cx, int cy)? FindRobloxCenter()
    {
        (int cx, int cy)? result = null;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var buf = new char[len + 1];
            GetWindowText(hWnd, buf, buf.Length);
            var title = new string(buf, 0, len);
            if (title.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
            {
                GetWindowRect(hWnd, out var rect);
                result = ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                return false; // stop
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
