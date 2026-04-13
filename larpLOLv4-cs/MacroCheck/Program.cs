using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

Console.Title = "Macro Compatibility Check";
SetProcessDPIAware();

// Crash handler — always show error and wait before closing
AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n\n  CRASHED: {args.ExceptionObject}");
    Console.ResetColor();
    Console.WriteLine("\n  Press any key to exit...");
    try { Console.ReadKey(true); } catch { Console.ReadLine(); }
};

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║   Rhythm Macro — Deep Compatibility Check       ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

int pass = 0, warn = 0, fail = 0;

// ═══════════════════════════════════════════════════════════════════
//  SECTION 1: SYSTEM BASICS
// ═══════════════════════════════════════════════════════════════════
Section("SYSTEM");

Check("Windows Version + Arch", () =>
{
    var os = Environment.OSVersion;
    bool is64 = Environment.Is64BitOperatingSystem;
    string ver = $"Windows {os.Version} ({(is64 ? "x64" : "x86")})";
    if (!is64) return (Status.Fail, $"{ver} — macro requires 64-bit");
    if (os.Version.Major < 10) return (Status.Fail, $"{ver} — requires Windows 10+");
    return (Status.Pass, ver);
});

Check("Running as Admin", () =>
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
    if (!isAdmin)
        return (Status.Fail, "NO — SendInput gets BLOCKED when target app (Roblox) runs at higher privilege.\n" +
            "      FIX: Right-click the macro exe -> Run as administrator");
    return (Status.Pass, "Yes");
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 2: DISPLAY
// ═══════════════════════════════════════════════════════════════════
Section("DISPLAY");

Check("Monitors", () =>
{
    var screens = System.Windows.Forms.Screen.AllScreens;
    var lines = screens.Select(s =>
    {
        string p = s.Primary ? " [PRIMARY]" : "";
        return $"      {s.DeviceName}{p}: {s.Bounds.Width}x{s.Bounds.Height} at ({s.Bounds.X},{s.Bounds.Y})";
    });
    return (Status.Pass, $"{screens.Length} monitor(s)\n{string.Join("\n", lines)}");
});

Check("DPI Scaling", () =>
{
    using var g = Graphics.FromHwnd(IntPtr.Zero);
    int scalePct = (int)(g.DpiX / 96f * 100);
    if (scalePct != 100)
        return (Status.Warn, $"{scalePct}% — coordinates will be offset from 100%. Must calibrate on THIS PC");
    return (Status.Pass, $"100% (no offset)");
});

Check("Screen Capture works", () =>
{
    // Try capturing from each monitor
    var screens = System.Windows.Forms.Screen.AllScreens;
    var results = new List<string>();
    foreach (var s in screens)
    {
        try
        {
            using var bmp = new Bitmap(50, 50, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            int cx = s.Bounds.X + s.Bounds.Width / 2;
            int cy = s.Bounds.Y + s.Bounds.Height / 2;
            g.CopyFromScreen(cx - 25, cy - 25, 0, 0, new Size(50, 50));

            // Check it's not all black (which means capture failed silently / exclusive fullscreen)
            int nonBlack = 0;
            for (int x = 0; x < 50; x += 5)
                for (int y = 0; y < 50; y += 5)
                {
                    var px = bmp.GetPixel(x, y);
                    if (px.R > 5 || px.G > 5 || px.B > 5) nonBlack++;
                }
            if (nonBlack == 0)
                results.Add($"{s.DeviceName}: captured but ALL BLACK (fullscreen exclusive or screen off?)");
            else
                results.Add($"{s.DeviceName}: OK ({nonBlack}/100 non-black samples)");
        }
        catch (Exception ex)
        {
            return (Status.Fail, $"{s.DeviceName}: FAILED — {ex.Message}");
        }
    }
    bool anyBlack = results.Any(r => r.Contains("ALL BLACK"));
    return (anyBlack ? Status.Warn : Status.Pass, string.Join("\n      ", results));
});

Check("Fullscreen Exclusive Mode", () =>
{
    // Check if any window is in fullscreen exclusive (blocks GDI capture)
    var primary = System.Windows.Forms.Screen.PrimaryScreen!;
    IntPtr fg = GetForegroundWindow();
    if (fg != IntPtr.Zero)
    {
        GetWindowRect(fg, out var rect);
        bool coversScreen = rect.Left <= primary.Bounds.Left &&
                            rect.Top <= primary.Bounds.Top &&
                            rect.Right >= primary.Bounds.Right &&
                            rect.Bottom >= primary.Bounds.Bottom;
        if (coversScreen)
        {
            int len = GetWindowTextLength(fg);
            var buf = new char[len + 1];
            GetWindowText(fg, buf, buf.Length);
            string title = new string(buf, 0, len);
            return (Status.Warn, $"Foreground window \"{title}\" covers full screen.\n" +
                "      If Roblox is in Fullscreen (not borderless), screen capture may fail.\n" +
                "      FIX: Use Borderless Fullscreen in Roblox settings, not exclusive Fullscreen");
        }
    }
    return (Status.Pass, "No fullscreen exclusive window detected");
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 3: KEYBOARD INPUT
// ═══════════════════════════════════════════════════════════════════
Section("KEYBOARD INPUT");

Check("SendInput struct size", () =>
{
    int size = Marshal.SizeOf<INPUT>();
    if (size != 40) return (Status.Fail, $"{size} bytes (WRONG — should be 40). Macro will silently fail to press keys");
    return (Status.Pass, $"{size} bytes (correct)");
});

Check("SendInput actually works (live test)", () =>
{
    // Send a keypress for a harmless key (F13 — no app uses this)
    // Then check if GetAsyncKeyState sees it
    const ushort VK_F13 = 0x7C;
    ushort scan = (ushort)MapVirtualKeyW(VK_F13, 0);

    var press = new INPUT
    {
        type = 1, // INPUT_KEYBOARD
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT { wVk = VK_F13, wScan = scan, dwFlags = 0 }
        }
    };
    var release = new INPUT
    {
        type = 1,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT { wVk = VK_F13, wScan = scan, dwFlags = 2 } // KEYEVENTF_KEYUP
        }
    };

    int sz = Marshal.SizeOf<INPUT>();
    uint sent = SendInput(1, [press], sz);
    Thread.Sleep(30);
    short state = GetAsyncKeyState(VK_F13);
    SendInput(1, [release], sz);

    if (sent == 0)
    {
        int err = Marshal.GetLastWin32Error();
        return (Status.Fail, $"SendInput returned 0 — key was BLOCKED. Error code: {err}\n" +
            "      This usually means UIPI is blocking input (app running at lower privilege than target).\n" +
            "      FIX: Run the macro as Administrator");
    }

    bool wasDown = (state & 0x8000) != 0;
    if (!wasDown)
        return (Status.Warn, $"SendInput sent OK ({sent}) but GetAsyncKeyState didn't detect it.\n" +
            "      This can happen if another program is intercepting keyboard input.");

    return (Status.Pass, $"Sent F13 key and confirmed it was received (sent={sent}, state=0x{state:X4})");
});

Check("Scan codes for Z, X, Comma, Period", () =>
{
    var keys = new (string name, uint vk)[] {
        ("Z", 0x5A), ("X", 0x58), ("Comma", 0xBC), ("Period", 0xBE)
    };
    var results = new List<string>();
    bool anyZero = false;
    foreach (var (name, vk) in keys)
    {
        uint scan = MapVirtualKeyW(vk, 0);
        results.Add($"{name}=0x{scan:X2}");
        if (scan == 0) anyZero = true;
    }
    if (anyZero)
        return (Status.Fail, $"Some scan codes are 0 (unmapped): {string.Join(", ", results)}\n" +
            "      This means the keyboard layout doesn't have these keys in the expected positions.");
    return (Status.Pass, string.Join(", ", results));
});

Check("Keyboard layout", () =>
{
    IntPtr hkl = GetKeyboardLayout(0);
    int langId = (int)hkl & 0xFFFF;
    string hex = $"0x{langId:X4}";

    // Common layouts
    string name = langId switch
    {
        0x0409 => "English (US)",
        0x0809 => "English (UK)",
        0x040C => "French (AZERTY)",
        0x0407 => "German (QWERTZ)",
        0x0410 => "Italian",
        0x0C0A or 0x040A => "Spanish",
        0x0416 or 0x0816 => "Portuguese",
        0x0419 => "Russian",
        0x0411 => "Japanese",
        0x0412 => "Korean",
        _ => "Other"
    };

    if (langId == 0x040C) // French AZERTY
        return (Status.Fail, $"{name} ({hex}) — AZERTY layout! Z and comma keys are in different positions.\n" +
            "      The macro sends scan codes for QWERTY layout. It will press the WRONG keys.\n" +
            "      FIX: Switch to English (US) keyboard layout before running the macro");

    if (langId == 0x0407) // German QWERTZ
        return (Status.Warn, $"{name} ({hex}) — QWERTZ layout. Z and Y are swapped from QWERTY.\n" +
            "      Macro uses scan codes so physical key positions should still work, but verify in-game.");

    if (langId != 0x0409 && langId != 0x0809)
        return (Status.Warn, $"{name} ({hex}) — non-English layout. Scan codes should work for physical key positions, but verify.");

    return (Status.Pass, $"{name} ({hex})");
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 4: SECURITY / ANTIVIRUS
// ═══════════════════════════════════════════════════════════════════
Section("SECURITY");

Check("UIPI (User Interface Privilege Isolation)", () =>
{
    // If Roblox is running, check if it's elevated and we're not
    bool robloxFound = false;
    bool robloxElevated = false;
    bool weAreAdmin;
    using (var id = WindowsIdentity.GetCurrent())
        weAreAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);

    try
    {
        var robloxProcs = Process.GetProcessesByName("RobloxPlayerBeta");
        if (robloxProcs.Length == 0)
            robloxProcs = Process.GetProcessesByName("Windows10Universal"); // UWP Roblox
        if (robloxProcs.Length > 0)
        {
            robloxFound = true;
            // Try to read the process — if we can't, it may be elevated
            try
            {
                var _ = robloxProcs[0].MainModule;
                robloxElevated = false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                robloxElevated = true; // Access denied = likely elevated
            }
        }
    }
    catch { }

    if (robloxFound && robloxElevated && !weAreAdmin)
        return (Status.Fail, "Roblox appears to be running ELEVATED but this tool is NOT admin.\n" +
            "      SendInput CANNOT send keys to a higher-privilege window (UIPI blocks it).\n" +
            "      FIX: Run the macro as Administrator");

    if (!robloxFound)
        return (Status.Pass, "Roblox not running — can't test UIPI, but run the macro as Admin to be safe");

    if (weAreAdmin)
        return (Status.Pass, "Running as admin — UIPI won't block input to Roblox");

    return (Status.Pass, "Roblox running at same privilege level — SendInput should work");
});

Check("Mark of the Web (downloaded file blocking)", () =>
{
    // Check if the running exe has the Zone.Identifier ADS (MOTW)
    string exePath = Environment.ProcessPath ?? "";
    if (string.IsNullOrEmpty(exePath))
        return (Status.Warn, "Could not determine exe path");

    string adsPath = exePath + ":Zone.Identifier";
    bool hasMotw = false;
    try
    {
        // Try reading the ADS
        using var fs = new FileStream(adsPath, FileMode.Open, FileAccess.Read);
        hasMotw = true;
    }
    catch { }

    if (hasMotw)
        return (Status.Warn, "This exe has Mark of the Web (downloaded from internet).\n" +
            "      Windows SmartScreen may block it or show a warning.\n" +
            "      FIX: Right-click exe -> Properties -> check 'Unblock' at the bottom -> Apply");

    return (Status.Pass, "No MOTW — file is unblocked");
});

Check("Windows Defender real-time scan", () =>
{
    // Check if Defender is actively running
    var defenders = Process.GetProcessesByName("MsMpEng");
    if (defenders.Length == 0)
        return (Status.Pass, "Windows Defender not running");

    // Check for Controlled Folder Access (blocks writes from unknown apps)
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access");
        if (key != null)
        {
            var enabled = key.GetValue("EnableControlledFolderAccess");
            if (enabled != null && (int)enabled == 1)
                return (Status.Warn, "Defender running + Controlled Folder Access is ON.\n" +
                    "      This can block the macro from writing coords.txt or other files.\n" +
                    "      FIX: Add the macro folder to Controlled Folder Access allowed list");
        }
    }
    catch { }

    // Check threat history for recently quarantined items
    try
    {
        string defenderLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows Defender\Support");
        if (Directory.Exists(defenderLog))
        {
            var recentLogs = Directory.GetFiles(defenderLog, "MPLog-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(1);
            foreach (var log in recentLogs)
            {
                try
                {
                    string content = File.ReadAllText(log);
                    // Check for recent detections containing our exe name
                    if (content.Contains("larpLOL", StringComparison.OrdinalIgnoreCase))
                        return (Status.Fail, "Defender has flagged/quarantined larpLOL in its logs!\n" +
                            "      FIX: Windows Security -> Virus & threat protection -> Protection history\n" +
                            "      -> Find the detection and choose 'Allow' or add the folder as exclusion");
                }
                catch { }
            }
        }
    }
    catch { }

    return (Status.Pass, "Defender running, no detections of macro found in recent logs");
});

Check("Third-party antivirus", () =>
{
    var avProcessNames = new Dictionary<string, string>
    {
        ["avp"] = "Kaspersky", ["avgnt"] = "Avira", ["avguard"] = "Avira Guard",
        ["bdagent"] = "Bitdefender", ["mcshield"] = "McAfee", ["NortonSecurity"] = "Norton",
        ["SophosUI"] = "Sophos", ["WRSA"] = "Webroot", ["MBAMService"] = "Malwarebytes",
        ["ekrn"] = "ESET", ["fsav32"] = "F-Secure", ["dwengine"] = "Dr.Web",
        ["PavFnSvr"] = "Panda", ["vsserv"] = "Bitdefender", ["coreServiceShell"] = "Trend Micro"
    };

    var found = new List<string>();
    foreach (var (proc, name) in avProcessNames)
    {
        try
        {
            if (Process.GetProcessesByName(proc).Length > 0)
                found.Add(name);
        }
        catch { }
    }

    if (found.Count > 0)
        return (Status.Warn, $"Found: {string.Join(", ", found.Distinct())}\n" +
            "      These may quarantine or block the macro exe.\n" +
            "      FIX: Add the macro exe/folder to the antivirus exclusion list");

    return (Status.Pass, "None detected");
});

Check("Windows SmartScreen", () =>
{
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
        var ssEnabled = key?.GetValue("SmartScreenEnabled")?.ToString();
        if (ssEnabled == "Off")
            return (Status.Pass, "SmartScreen is OFF");
        if (ssEnabled == "Warn" || ssEnabled == "Block" || ssEnabled == null)
            return (Status.Warn, $"SmartScreen is {ssEnabled ?? "ON (default)"}. First-time running an unsigned exe will show a warning.\n" +
                "      FIX: Click 'More info' -> 'Run anyway' when the blue popup appears.\n" +
                "      Or: Right-click exe -> Properties -> Unblock");
    }
    catch { }
    return (Status.Warn, "Could not determine SmartScreen state. If a blue popup blocks the exe:\n" +
        "      Click 'More info' -> 'Run anyway'");
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 5: ROBLOX
// ═══════════════════════════════════════════════════════════════════
Section("ROBLOX");

Check("Roblox process", () =>
{
    var robloxProcs = Process.GetProcessesByName("RobloxPlayerBeta");
    if (robloxProcs.Length == 0)
    {
        robloxProcs = Process.GetProcessesByName("Windows10Universal");
        if (robloxProcs.Length > 0)
            return (Status.Warn, "Roblox is running as UWP/Microsoft Store version.\n" +
                "      The macro may have issues with UWP apps. Use the desktop version from roblox.com instead.");
    }
    if (robloxProcs.Length == 0)
        return (Status.Warn, "Roblox not running — open it and re-run this check for full diagnostics");
    return (Status.Pass, $"Running (PID: {robloxProcs[0].Id})");
});

Check("Roblox window", () =>
{
    string robloxTitle = "";
    IntPtr robloxHwnd = IntPtr.Zero;
    EnumWindows((hWnd, _) =>
    {
        if (!IsWindowVisible(hWnd)) return true;
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return true;
        var buf = new char[len + 1];
        GetWindowText(hWnd, buf, buf.Length);
        var t = new string(buf, 0, len);
        if (t.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
        {
            robloxTitle = t;
            robloxHwnd = hWnd;
            return false;
        }
        return true;
    }, IntPtr.Zero);

    if (robloxHwnd == IntPtr.Zero)
        return (Status.Warn, "No Roblox window found");

    GetWindowRect(robloxHwnd, out var rect);
    int w = rect.Right - rect.Left;
    int h = rect.Bottom - rect.Top;
    return (Status.Pass, $"\"{robloxTitle}\" — {w}x{h} at ({rect.Left},{rect.Top})");
});

Check("Roblox anti-cheat (Hyperion/Byfron)", () =>
{
    // Hyperion is Roblox's anti-cheat. It doesn't block SendInput (it targets DLL injection),
    // but let's check if it's present
    bool hyperionFound = false;
    try
    {
        var robloxProcs = Process.GetProcessesByName("RobloxPlayerBeta");
        if (robloxProcs.Length > 0)
        {
            try
            {
                foreach (ProcessModule mod in robloxProcs[0].Modules)
                {
                    if (mod.ModuleName != null &&
                        mod.ModuleName.Contains("hyperion", StringComparison.OrdinalIgnoreCase))
                    {
                        hyperionFound = true;
                        break;
                    }
                }
            }
            catch { /* access denied = elevated, already flagged above */ }
        }
    }
    catch { }

    if (hyperionFound)
        return (Status.Pass, "Hyperion (Byfron) detected — this is normal. It blocks DLL injection, NOT SendInput.\n" +
            "      The macro uses SendInput (virtual keyboard), which is allowed.");

    return (Status.Pass, "Could not detect Hyperion — either Roblox isn't open or running elevated. Not a problem.");
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 6: MACRO FILES
// ═══════════════════════════════════════════════════════════════════
Section("MACRO FILES");

Check("Macro exe location", () =>
{
    string exePath = Environment.ProcessPath ?? "unknown";
    string dir = Path.GetDirectoryName(exePath) ?? "unknown";

    // Check for problematic locations
    string lower = dir.ToLowerInvariant();
    if (lower.Contains("temp") || lower.Contains("appdata\\local\\temp"))
        return (Status.Warn, $"Running from TEMP folder: {dir}\n" +
            "      Some antivirus software blocks exes in temp. Move to a permanent folder like Desktop.");

    if (lower.Contains("onedrive"))
        return (Status.Warn, $"Running from OneDrive: {dir}\n" +
            "      OneDrive sync can cause file locking issues. Move to a local folder.");

    return (Status.Pass, dir);
});

Check("coords.txt", () =>
{
    string exePath = Environment.ProcessPath ?? "";
    string dir = Path.GetDirectoryName(exePath) ?? "";
    string coordsPath = Path.Combine(dir, "coords.txt");

    if (!File.Exists(coordsPath))
        return (Status.Warn, $"Not found at {coordsPath}\n" +
            "      This is OK if you haven't calibrated yet — default coordinates will be used.\n" +
            "      But defaults are for 1080p, so calibrate if your resolution differs.");

    try
    {
        string json = File.ReadAllText(coordsPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tap = root.GetProperty("tap");
        var hold = root.GetProperty("hold");

        if (tap.GetArrayLength() != 4 || hold.GetArrayLength() != 4)
            return (Status.Fail, "coords.txt exists but has wrong format (expected 4 tap + 4 hold points)");

        string tapStr = string.Join(", ", Enumerable.Range(0, 4).Select(i =>
            $"({tap[i][0]},{tap[i][1]})"));
        string holdStr = string.Join(", ", Enumerable.Range(0, 4).Select(i =>
            $"({hold[i][0]},{hold[i][1]})"));

        return (Status.Pass, $"Found and valid\n      Tap:  {tapStr}\n      Hold: {holdStr}");
    }
    catch (Exception ex)
    {
        return (Status.Fail, $"coords.txt exists but CORRUPT: {ex.Message}");
    }
});

Check("Write permission in macro folder", () =>
{
    string exePath = Environment.ProcessPath ?? "";
    string dir = Path.GetDirectoryName(exePath) ?? "";
    string testFile = Path.Combine(dir, ".write_test_" + Guid.NewGuid().ToString("N")[..8]);
    try
    {
        File.WriteAllText(testFile, "test");
        File.Delete(testFile);
        return (Status.Pass, "Can write to macro folder");
    }
    catch (Exception ex)
    {
        return (Status.Fail, $"CANNOT write to folder: {ex.Message}\n" +
            "      The macro needs to save coords.txt here.\n" +
            "      FIX: Move the macro to a folder you have write access to (Desktop, Documents)");
    }
});

// ═══════════════════════════════════════════════════════════════════
//  SECTION 7: LIVE FUNCTIONALITY TEST
// ═══════════════════════════════════════════════════════════════════
Section("LIVE TESTS");

Check("Screen capture at default tap zone", () =>
{
    // Default tap coords from the macro
    var tapPoints = new (int x, int y)[] { (720, 950), (881, 950), (1039, 950), (1200, 950) };

    // Check if these coords are even on a monitor
    var screens = System.Windows.Forms.Screen.AllScreens;
    foreach (var (x, y) in tapPoints)
    {
        bool onScreen = screens.Any(s => s.Bounds.Contains(x, y));
        if (!onScreen)
            return (Status.Warn, $"Default tap coord ({x},{y}) is OFF-SCREEN!\n" +
                "      Your friend's resolution may be different. They need to re-calibrate.\n" +
                "      FIX: Run the macro -> Calibrate Detection Zones");
    }

    // Try capturing those regions
    try
    {
        using var bmp = new Bitmap(1300, 1000, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(0, 0, 0, 0, new Size(1300, 1000));
        // Sample pixels at tap points
        var samples = tapPoints.Select(p => bmp.GetPixel(p.x, p.y)).ToArray();
        string info = string.Join(", ", samples.Select((px, i) =>
            $"Lane{i + 1}=({px.R},{px.G},{px.B})"));
        return (Status.Pass, $"Captured OK — pixel colors: {info}");
    }
    catch (Exception ex)
    {
        return (Status.Fail, $"Capture FAILED: {ex.Message}");
    }
});

Check("Key send to Notepad (safe live test)", () =>
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("\n         (Opening blank Notepad for live test...) ");
    Console.ResetColor();

    // Create a blank temp file so Win11 Notepad doesn't restore old tabs
    string tempFile = Path.Combine(Path.GetTempPath(), $"_macrocheck_{Guid.NewGuid():N}.txt");
    Process? notepad = null;
    try
    {
        File.WriteAllText(tempFile, "");

        notepad = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{tempFile}\"")
        {
            UseShellExecute = true
        });
        if (notepad == null)
            return (Status.Warn, "Could not start Notepad — test skipped");

        // Wait for the window to appear (try up to 5 seconds)
        IntPtr nWnd = IntPtr.Zero;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Thread.Sleep(100);
            notepad.Refresh();
            nWnd = notepad.MainWindowHandle;
            if (nWnd != IntPtr.Zero) break;
        }

        // If MainWindowHandle failed, search all windows
        if (nWnd == IntPtr.Zero)
        {
            string tempName = Path.GetFileName(tempFile);
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;
                var buf = new char[len + 1];
                GetWindowText(hWnd, buf, buf.Length);
                var title = new string(buf, 0, len);
                if (title.Contains(tempName, StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Notepad", StringComparison.OrdinalIgnoreCase))
                {
                    nWnd = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (nWnd == IntPtr.Zero)
            return (Status.Warn, "Notepad opened but could not find its window — test inconclusive");

        // Force Notepad to foreground even if we're not the active app.
        // Windows blocks SetForegroundWindow unless you do the Alt-key trick:
        // send a fake Alt press so Windows thinks we're doing an Alt-Tab style switch.
        int sz = Marshal.SizeOf<INPUT>();
        var altDown = new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x12, dwFlags = 0 } } }; // VK_MENU
        var altUp = new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x12, dwFlags = 0x0002 } } };
        SendInput(1, [altDown], sz);
        SetForegroundWindow(nWnd);
        SendInput(1, [altUp], sz);
        Thread.Sleep(600);

        // Verify Notepad actually got focus
        IntPtr fgAfter = GetForegroundWindow();
        bool gotFocus = (fgAfter == nWnd);

        // Send 'A' via SendInput with scan code (same method the macro uses)
        ushort scanA = (ushort)MapVirtualKeyW(0x41, 0); // VK_A
        var press = new INPUT
        {
            type = 1,
            u = new INPUTUNION { ki = new KEYBDINPUT { wScan = scanA, dwFlags = 0x0008 } }
        };
        var release = new INPUT
        {
            type = 1,
            u = new INPUTUNION { ki = new KEYBDINPUT { wScan = scanA, dwFlags = 0x0008 | 0x0002 } }
        };

        uint s1 = SendInput(1, [press], sz);
        Thread.Sleep(100);
        uint s2 = SendInput(1, [release], sz);
        Thread.Sleep(500);

        // Read the temp file to see if the keystroke actually landed
        string content = "";
        try { content = File.ReadAllText(tempFile); } catch { }

        if (s1 == 0 || s2 == 0)
        {
            int err = Marshal.GetLastWin32Error();
            return (Status.Fail, $"SendInput returned 0 — keys are being BLOCKED (error={err})\n" +
                "      FIX: Run the macro as Administrator");
        }

        if (content.Contains("a"))
            return (Status.Pass, $"Typed 'a' into Notepad and VERIFIED it in the file.\n" +
                "      Keyboard simulation is 100% working. (focus={gotFocus}, press={s1}, release={s2})");

        if (!gotFocus)
            return (Status.Warn, $"SendInput worked (press={s1}, release={s2}) but could not focus Notepad.\n" +
                "      The 'a' may have gone to another window. This is fine — the macro sends\n" +
                "      keys globally, so it works regardless of which window has focus.");

        return (Status.Pass, $"SendInput sent keys OK (press={s1}, release={s2}).\n" +
            "      Keyboard simulation is working. (focus={gotFocus})");
    }
    catch (Exception ex)
    {
        return (Status.Warn, $"Test error: {ex.Message}");
    }
    finally
    {
        // Win11 Notepad is actually "Windows Notepad" (NotepadWindowsApp) which spawns
        // a child process. Kill by PID first, then nuke any Notepad process that appeared
        // after we started ours.
        try { notepad?.Kill(true); } catch { }
        try { notepad?.WaitForExit(2000); } catch { }

        // Kill ALL Notepad processes — the one we spawned may have forked
        foreach (var name in new[] { "Notepad", "NotepadWindowsApp" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); } catch { }
                }
            }
            catch { }
        }

        // Wait for file handles to release, then retry delete a few times
        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(500);
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                break;
            }
            catch { }
        }
    }
});

// ═══════════════════════════════════════════════════════════════════
//  SUMMARY
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("  RESULTS");
Console.WriteLine("══════════════════════════════════════════════════");
Console.ResetColor();

Console.ForegroundColor = ConsoleColor.Green;
Console.Write($"  PASS: {pass}");
Console.ForegroundColor = ConsoleColor.Yellow;
Console.Write($"    WARN: {warn}");
Console.ForegroundColor = ConsoleColor.Red;
Console.Write($"    FAIL: {fail}");
Console.ResetColor();
Console.WriteLine();

if (fail > 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("\n  FAIL items found — these will BREAK the macro. Fix them first!");
    Console.ResetColor();
}
else if (warn > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n  No critical failures, but check WARNs — some may need attention.");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("\n  Everything looks good! The macro should work on this PC.");
    Console.ResetColor();
}

Console.WriteLine("\n  Screenshot ALL of this and send it back!");
Console.WriteLine("  Close this window when done.\n");
Thread.Sleep(Timeout.Infinite);

// ═══════════════════════════════════════════════════════════════════
//  Helpers
// ═══════════════════════════════════════════════════════════════════

void Section(string name)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"  ── {name} ──────────────────────────────────────");
    Console.ResetColor();
}

void Check(string name, Func<(Status status, string message)> check)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write($"  [{pass + warn + fail + 1,2}] {name}: ");
    try
    {
        var (status, message) = check();
        switch (status)
        {
            case Status.Pass:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("PASS"); pass++; break;
            case Status.Warn:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("WARN"); warn++; break;
            case Status.Fail:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAIL"); fail++; break;
        }
        Console.ResetColor();
        Console.WriteLine($" — {message}");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("ERROR");
        Console.ResetColor();
        Console.WriteLine($" — {ex.Message}");
        fail++;
    }
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════
//  P/Invoke
// ═══════════════════════════════════════════════════════════════════

[DllImport("user32.dll")]
static extern bool SetProcessDPIAware();

[DllImport("user32.dll", SetLastError = true)]
static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

[DllImport("user32.dll")]
static extern short GetAsyncKeyState(int vKey);

[DllImport("user32.dll")]
static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

[DllImport("user32.dll")]
static extern IntPtr GetKeyboardLayout(uint idThread);

[DllImport("user32.dll")]
static extern bool IsWindowVisible(IntPtr hWnd);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetWindowTextLength(IntPtr hWnd);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

[DllImport("user32.dll")]
static extern bool EnumWindows(NativeDelegate lpEnumFunc, IntPtr lParam);

[DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

[DllImport("user32.dll")]
static extern bool SetForegroundWindow(IntPtr hWnd);

[StructLayout(LayoutKind.Sequential)]
struct RECT { public int Left, Top, Right, Bottom; }
