using System.Runtime.InteropServices;

namespace SoulBeatsPro;

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
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk, wScan;
    public uint dwFlags, time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL, wParamH;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

internal static class NativeApi
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

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

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    // ── User idle time ──────────────────────────────────────────
    // Used by SystemProbe.GetIdleSeconds() to detect AFK users.

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // ── Well-known VK codes ─────────────────────────────────────

    public const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_RETURN = 0x0D;
    public const int VK_L = 0x4C, VK_P = 0x50, VK_S = 0x53;
    public const int VK_Z = 0x5A, VK_X = 0x58;
    public const int VK_OEM_COMMA = 0xBC, VK_OEM_PERIOD = 0xBE;
    public const int VK_F2 = 0x71;

    // ── Configurable lane scan codes ────────────────────────────

    public static ushort[] LaneScans { get; private set; } =
    [
        (ushort)MapVirtualKey((uint)VK_Z, 0),
        (ushort)MapVirtualKey((uint)VK_X, 0),
        (ushort)MapVirtualKey((uint)VK_OEM_COMMA, 0),
        (ushort)MapVirtualKey((uint)VK_OEM_PERIOD, 0)
    ];

    /// Update lane scan codes from config key names.
    public static void UpdateLaneScans(string[] keyNames)
    {
        for (int i = 0; i < 4 && i < keyNames.Length; i++)
        {
            int vk = VkFromName(keyNames[i]);
            if (vk != 0)
                LaneScans[i] = (ushort)MapVirtualKey((uint)vk, 0);
        }
    }

    /// Build scan codes for N keys (osu!mania variable key count).
    public static ushort[] BuildScanCodes(string[] keyNames, int count)
    {
        int n = Math.Min(count, keyNames.Length);
        var scans = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            int vk = VkFromName(keyNames[i]);
            scans[i] = vk != 0 ? (ushort)MapVirtualKey((uint)vk, 0) : (ushort)0;
        }
        return scans;
    }

    // ── Press / Release ─────────────────────────────────────────

    public static void PressScan(ushort scan)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION { ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE } }
        };
        SendInput(1, [inp], Marshal.SizeOf<INPUT>());
    }

    public static void ReleaseScan(ushort scan)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION { ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } }
        };
        SendInput(1, [inp], Marshal.SizeOf<INPUT>());
    }

    public static void PressKey(int lane) => PressScan(LaneScans[lane]);
    public static void ReleaseKey(int lane) => ReleaseScan(LaneScans[lane]);

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// Check if a named key is currently down.
    public static bool IsKeyDown(string keyName) => IsKeyDown(VkFromName(keyName));

    // ── Key name ↔ VK code mapping ──────────────────────────────

    public static int VkFromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        var n = name.ToUpperInvariant().Trim();

        // Single letter A-Z
        if (n.Length == 1 && n[0] >= 'A' && n[0] <= 'Z') return n[0];
        // Single digit 0-9
        if (n.Length == 1 && n[0] >= '0' && n[0] <= '9') return n[0];

        return n switch
        {
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PRIOR" => 0x21,
            "PAGEDOWN" or "NEXT" => 0x22,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "OEM_COMMA" or "COMMA" or "," => 0xBC,
            "OEM_PERIOD" or "PERIOD" or "." => 0xBE,
            "OEM_1" or "SEMICOLON" or ";" => 0xBA,
            "OEM_2" or "SLASH" or "/" => 0xBF,
            "OEM_3" or "TILDE" or "`" => 0xC0,
            "OEM_4" or "LBRACKET" or "[" => 0xDB,
            "OEM_5" or "BACKSLASH" or "\\" => 0xDC,
            "OEM_6" or "RBRACKET" or "]" => 0xDD,
            "OEM_7" or "QUOTE" or "'" => 0xDE,
            "OEM_MINUS" or "MINUS" or "-" => 0xBD,
            "OEM_PLUS" or "EQUALS" or "PLUS" or "=" => 0xBB,
            "LSHIFT" => 0xA0, "RSHIFT" => 0xA1, "SHIFT" => 0x10,
            "LCONTROL" or "LCTRL" => 0xA2, "RCONTROL" or "RCTRL" => 0xA3, "CONTROL" or "CTRL" => 0x11,
            "LMENU" or "LALT" => 0xA4, "RMENU" or "RALT" => 0xA5, "ALT" or "MENU" => 0x12,
            "NUMPAD0" => 0x60, "NUMPAD1" => 0x61, "NUMPAD2" => 0x62, "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64, "NUMPAD5" => 0x65, "NUMPAD6" => 0x66, "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68, "NUMPAD9" => 0x69,
            "MULTIPLY" => 0x6A, "ADD" => 0x6B, "SUBTRACT" => 0x6D,
            "DECIMAL" => 0x6E, "DIVIDE" => 0x6F,
            "CAPSLOCK" or "CAPS" => 0x14,
            "NUMLOCK" => 0x90, "SCROLLLOCK" or "SCROLL" => 0x91,
            _ => 0
        };
    }

    public static string NameFromVk(int vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();

        return vk switch
        {
            0x1B => "ESCAPE", 0x20 => "SPACE", 0x0D => "RETURN", 0x09 => "TAB",
            0x08 => "BACKSPACE", 0x2E => "DELETE", 0x2D => "INSERT",
            0x24 => "HOME", 0x23 => "END", 0x21 => "PAGEUP", 0x22 => "PAGEDOWN",
            0x25 => "LEFT", 0x26 => "UP", 0x27 => "RIGHT", 0x28 => "DOWN",
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            0xBC => "OEM_COMMA", 0xBE => "OEM_PERIOD",
            0xBA => "OEM_1", 0xBF => "OEM_2", 0xC0 => "OEM_3",
            0xDB => "OEM_4", 0xDC => "OEM_5", 0xDD => "OEM_6", 0xDE => "OEM_7",
            0xBD => "OEM_MINUS", 0xBB => "OEM_PLUS",
            0xA0 => "LSHIFT", 0xA1 => "RSHIFT", 0x10 => "SHIFT",
            0xA2 => "LCONTROL", 0xA3 => "RCONTROL", 0x11 => "CONTROL",
            0xA4 => "LMENU", 0xA5 => "RMENU", 0x12 => "ALT",
            0x60 => "NUMPAD0", 0x61 => "NUMPAD1", 0x62 => "NUMPAD2", 0x63 => "NUMPAD3",
            0x64 => "NUMPAD4", 0x65 => "NUMPAD5", 0x66 => "NUMPAD6", 0x67 => "NUMPAD7",
            0x68 => "NUMPAD8", 0x69 => "NUMPAD9",
            0x14 => "CAPSLOCK", 0x90 => "NUMLOCK", 0x91 => "SCROLLLOCK",
            _ => $"0x{vk:X2}"
        };
    }

    /// Friendly display name for a key (e.g. "OEM_COMMA" → ",").
    public static string DisplayName(string keyName)
    {
        return keyName.ToUpperInvariant() switch
        {
            "OEM_COMMA" => ",",
            "OEM_PERIOD" => ".",
            "OEM_1" => ";",
            "OEM_2" => "/",
            "OEM_3" => "`",
            "OEM_4" => "[",
            "OEM_5" => "\\",
            "OEM_6" => "]",
            "OEM_7" => "'",
            "OEM_MINUS" => "-",
            "OEM_PLUS" => "=",
            "ESCAPE" => "ESC",
            "RETURN" => "Enter",
            "BACKSPACE" => "BkSp",
            "DELETE" => "Del",
            "INSERT" => "Ins",
            "SPACE" => "Space",
            "LSHIFT" => "LShift",
            "RSHIFT" => "RShift",
            "LCONTROL" => "LCtrl",
            "RCONTROL" => "RCtrl",
            "LMENU" => "LAlt",
            "RMENU" => "RAlt",
            "CONTROL" => "Ctrl",
            "SHIFT" => "Shift",
            "CAPSLOCK" => "CapsLk",
            _ => keyName
        };
    }

    // ── Roblox window ───────────────────────────────────────────

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
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
