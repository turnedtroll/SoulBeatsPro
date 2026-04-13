import sys
import time
import json
import os
import platform
import ctypes
import ctypes.wintypes
import mss
import numpy as np
import random
import threading

# ─── JUDGMENT VARIATION ──────────────────────────────────────────────────────
# 30% of taps get a Good delay via background thread.
# The state machine is untouched — thread fires independently.
# To avoid burst misses: if the same lane was pressed very recently
# (jack pattern), skip the delay and hit instantly.
GOOD_CHANCE      = 0.30
GOOD_DELAY_MIN   = 0.052
GOOD_DELAY_MAX   = 0.062
# If the lane was last pressed less than this ago, skip Good (it's a jack)
JACK_THRESHOLD   = 0.150  # 150ms — notes closer than this are jacks

# ─── DPI AWARENESS (must run before any screen coords or capture) ─────────────
# Without this, on PCs with 125%/150% scaling the macro samples wrong pixels:
# calibration and mss would use different coordinate spaces, so hold/tap detection fails.
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(1)  # PROCESS_SYSTEM_DPI_AWARE
except Exception:
    pass

# ─── PIXEL COORDINATES ────────────────────────────────────────────────────────
SAMPLE_HALF = 3

DEFAULT_TAP_PIXELS  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]
DEFAULT_HOLD_PIXELS = [(720, 824), (878, 824), (1040, 824), (1197, 824)]

KEYS = ['z', 'x', ',', '.']

# ─── COLOR THRESHOLDS ─────────────────────────────────────────────────────────
WHITE_MIN  = 240
GRAY_MIN   = 130
GRAY_MAX   = 170
MIN_PIXELS = 3

TAP_KEY_DURATION      = 0.045
HOLD_RELEASE_COOLDOWN = 0.06
TOGGLE_DELAY          = 0.3
MAX_HOLD_DURATION     = 5.0
# On slower PCs we may skip the exact frame where only hold-gray is visible; keep
# "saw hold gray" for this long so we still treat the note as a hold when white appears.
HOLD_GRACE_SEC        = 0.1

# ─── SENDINPUT KEY SIMULATION (hardware scan codes) ─────────────────────────
# Uses SendInput with KEYEVENTF_SCANCODE so the OS treats them like real
# keyboard presses — far more reliable with games than keyboard library.
KEYEVENTF_SCANCODE  = 0x0008
KEYEVENTF_KEYUP     = 0x0002

# Map key chars to hardware scan codes
_SCAN_CODES = {
    'z': 0x2C,
    'x': 0x2D,
    ',': 0x33,
    '.': 0x34,
}

# Correct INPUT struct for 64-bit Windows — union must include MOUSEINPUT
# (the largest member). SendInput requires the *documented* cbSize (28 or 40),
# not ctypes.sizeof(INPUT), because structure padding can differ across
# Python/ctypes builds and cause keys to be ignored on other PCs.
ULONG_PTR = ctypes.c_uint64
# Windows API: 32-bit INPUT = 28 bytes, 64-bit = 40 bytes (used as SendInput cbSize)
_INPUT_CB_SIZE = 28 if ctypes.sizeof(ctypes.c_void_p) == 4 else 40

class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx",          ctypes.c_long),
        ("dy",          ctypes.c_long),
        ("mouseData",   ctypes.c_ulong),
        ("dwFlags",     ctypes.c_ulong),
        ("time",        ctypes.c_ulong),
        ("dwExtraInfo", ULONG_PTR),
    ]

class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk",         ctypes.c_ushort),
        ("wScan",       ctypes.c_ushort),
        ("dwFlags",     ctypes.c_ulong),
        ("time",        ctypes.c_ulong),
        ("dwExtraInfo", ULONG_PTR),
    ]

class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [
        ("uMsg",    ctypes.c_ulong),
        ("wParamL", ctypes.c_short),
        ("wParamH", ctypes.c_ushort),
    ]

class _INPUT_UNION(ctypes.Union):
    _fields_ = [
        ("mi", MOUSEINPUT),
        ("ki", KEYBDINPUT),
        ("hi", HARDWAREINPUT),
    ]

# On 64-bit Windows the union must start at offset 8 (4-byte padding after type).
# Without this, SendInput can misread the struct on some PCs and key events are ignored.
if ctypes.sizeof(ctypes.c_void_p) == 8:
    class INPUT(ctypes.Structure):
        _fields_ = [
            ("type",   ctypes.c_ulong),
            ("_pad",   ctypes.c_ulong),
            ("union",  _INPUT_UNION),
        ]
else:
    class INPUT(ctypes.Structure):
        _fields_ = [
            ("type",  ctypes.c_ulong),
            ("union", _INPUT_UNION),
        ]

_SendInput = ctypes.windll.user32.SendInput
_SendInput.argtypes = [ctypes.c_uint, ctypes.POINTER(INPUT), ctypes.c_int]
_SendInput.restype = ctypes.c_uint

def _send_key(scan_code, up=False):
    flags = KEYEVENTF_SCANCODE | (KEYEVENTF_KEYUP if up else 0)
    inp = INPUT()
    inp.type = 1  # INPUT_KEYBOARD
    inp.union.ki.wVk = 0
    inp.union.ki.wScan = scan_code
    inp.union.ki.dwFlags = flags
    inp.union.ki.time = 0
    inp.union.ki.dwExtraInfo = 0
    _SendInput(1, ctypes.byref(inp), _INPUT_CB_SIZE)

def key_press(key):
    _send_key(_SCAN_CODES[key], up=False)

def key_release(key):
    _send_key(_SCAN_CODES[key], up=True)

# ─── KEY STATE POLLING (replaces keyboard library) ──────────────────────────
# GetAsyncKeyState is a simple poll — no global hook, no event interception,
# no admin rights needed. The keyboard library's hook was eating SendInput
# events on slower PCs.
_GetAsyncKeyState = ctypes.windll.user32.GetAsyncKeyState
_GetAsyncKeyState.argtypes = [ctypes.c_int]
_GetAsyncKeyState.restype = ctypes.c_short

VK_L = 0x4C  # virtual key code for 'L'

def is_key_down(vk):
    return bool(_GetAsyncKeyState(vk) & 0x8000)

# Base directory: next to the .exe when frozen, next to the .py when running as script
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(sys.executable)
else:
    _BASE_DIR = os.path.dirname(os.path.abspath(__file__))

COORDS_FILE = os.path.join(_BASE_DIR, 'coords.txt')
LOG_FILE    = os.path.join(_BASE_DIR, 'miss_log.txt')

# ─── ROBLOX FOCUS CHECK (CACHED) ─────────────────────────────────────────────

_user32 = ctypes.windll.user32
_kernel32 = ctypes.windll.kernel32

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

_focus_cache = [False, 0.0]
FOCUS_CHECK_INTERVAL = 0.2

def is_roblox_focused():
    now = time.time()
    if now - _focus_cache[1] < FOCUS_CHECK_INTERVAL:
        return _focus_cache[0]
    _focus_cache[1] = now
    try:
        hwnd = _user32.GetForegroundWindow()
        if not hwnd:
            _focus_cache[0] = False
            return False
        pid = ctypes.wintypes.DWORD()
        _user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        handle = _kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid.value)
        if not handle:
            _focus_cache[0] = False
            return False
        try:
            buf = ctypes.create_unicode_buffer(260)
            size = ctypes.wintypes.DWORD(260)
            _kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size))
            exe = buf.value.lower()
            _focus_cache[0] = 'roblox' in exe
        finally:
            _kernel32.CloseHandle(handle)
    except Exception:
        _focus_cache[0] = False
    return _focus_cache[0]

# ─── COORD SAVE / LOAD ───────────────────────────────────────────────────────

def save_coords(tap_pixels, hold_pixels, timing_offset=0):
    data = {
        "tap":  [list(p) for p in tap_pixels],
        "hold": [list(p) for p in hold_pixels],
        "timing_offset": timing_offset,
    }
    with open(COORDS_FILE, 'w') as f:
        json.dump(data, f, indent=2)

def load_coords():
    if os.path.exists(COORDS_FILE):
        try:
            with open(COORDS_FILE, 'r') as f:
                data = json.load(f)
            tap  = [tuple(p) for p in data["tap"]]
            hold = [tuple(p) for p in data["hold"]]
            offset = data.get("timing_offset", 0)
            if len(tap) == 4 and len(hold) == 4:
                return tap, hold, int(offset)
        except Exception:
            pass
    return list(DEFAULT_TAP_PIXELS), list(DEFAULT_HOLD_PIXELS), 0

# ─── SYSTEM DIAGNOSTIC ──────────────────────────────────────────────────────
# Writes a one-time system info block to miss_log.txt every time the macro starts.
# Helps diagnose why the macro works on one PC but not another.

def run_system_check():
    lines = []
    lines.append("=" * 70)
    lines.append(f"MACRO START — {time.strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("=" * 70)

    # OS / platform
    lines.append(f"OS:           {platform.system()} {platform.version()}")
    lines.append(f"Machine:      {platform.machine()}")
    lines.append(f"Python:       {sys.version}")
    lines.append(f"Frozen (exe): {getattr(sys, 'frozen', False)}")
    lines.append(f"Base dir:     {_BASE_DIR}")

    # Screen resolution & DPI scaling
    try:
        user32 = ctypes.windll.user32
        # Try to get real (unscaled) resolution
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            pass
        real_w = user32.GetSystemMetrics(0)
        real_h = user32.GetSystemMetrics(1)
        lines.append(f"Screen:       {real_w}x{real_h}")

        # Check DPI scaling
        try:
            hdc = user32.GetDC(0)
            dpi = ctypes.windll.gdi32.GetDeviceCaps(hdc, 88)  # LOGPIXELSX
            user32.ReleaseDC(0, hdc)
            scale_pct = round(dpi / 96 * 100)
            lines.append(f"DPI:          {dpi} ({scale_pct}% scaling)")
            if scale_pct != 100:
                lines.append("  !! WARNING: DPI scaling is NOT 100% — pixel coords will be WRONG")
                lines.append("  !! Set Windows display scaling to 100% or recalibrate")
        except Exception as e:
            lines.append(f"DPI:          could not read ({e})")
    except Exception as e:
        lines.append(f"Screen:       error ({e})")

    # coords.txt status
    if os.path.exists(COORDS_FILE):
        try:
            with open(COORDS_FILE) as f:
                data = json.load(f)
            tap = data.get('tap', [])
            hold = data.get('hold', [])
            offset = data.get('timing_offset', 0)
            lines.append(f"coords.txt:   LOADED from {COORDS_FILE}")
            lines.append(f"  tap:  {tap}")
            lines.append(f"  hold: {hold}")
            lines.append(f"  timing_offset: {offset}")
        except Exception as e:
            lines.append(f"coords.txt:   EXISTS but FAILED to read: {e}")
    else:
        lines.append(f"coords.txt:   NOT FOUND at {COORDS_FILE}")
        lines.append(f"  using defaults — tap: {[list(p) for p in DEFAULT_TAP_PIXELS]}")
        lines.append(f"  using defaults — hold: {[list(p) for p in DEFAULT_HOLD_PIXELS]}")
        lines.append("  !! WARNING: no calibration file — coords may not match this screen")

    # SendInput test (press and release a harmless key to verify it works)
    try:
        test_scan = 0x2C  # 'z'
        inp = INPUT()
        inp.type = 1
        inp.union.ki.wVk = 0
        inp.union.ki.wScan = test_scan
        inp.union.ki.dwFlags = KEYEVENTF_SCANCODE
        inp.union.ki.time = 0
        inp.union.ki.dwExtraInfo = 0
        r1 = _SendInput(1, ctypes.byref(inp), _INPUT_CB_SIZE)
        # immediately release
        inp.union.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP
        r2 = _SendInput(1, ctypes.byref(inp), _INPUT_CB_SIZE)
        lines.append(f"SendInput:    cbSize={_INPUT_CB_SIZE} (sizeof={ctypes.sizeof(INPUT)}) press={r1} release={r2}")
        if r1 != 1 or r2 != 1:
            lines.append("  !! WARNING: SendInput returned 0 — keys may not register")
            lines.append("  !! Try running the macro as Administrator")
    except Exception as e:
        lines.append(f"SendInput:    FAILED ({e})")

    # mss screen grab test
    try:
        with mss.mss() as sct:
            test_mon = {'left': 0, 'top': 0, 'width': 10, 'height': 10}
            img = sct.grab(test_mon)
            lines.append(f"Screen grab:  OK (mss {mss.__version__ if hasattr(mss, '__version__') else 'unknown'})")
    except Exception as e:
        lines.append(f"Screen grab:  FAILED ({e})")

    # numpy version
    lines.append(f"numpy:        {np.__version__}")

    # Admin check
    try:
        is_admin = bool(ctypes.windll.shell32.IsUserAnAdmin())
        lines.append(f"Admin:        {is_admin}")
        if not is_admin:
            lines.append("  (note: SendInput may fail in some games without admin)")
    except Exception:
        lines.append("Admin:        unknown")

    lines.append("=" * 70)
    lines.append("")

    # Write to log file
    try:
        with open(LOG_FILE, 'a') as f:
            f.write('\n'.join(lines) + '\n')
    except Exception:
        pass

    return lines


# ─── CALIBRATION GUI ─────────────────────────────────────────────────────────

def calibrate():
    import tkinter as tk

    loaded = load_coords()
    tap_pixels  = [list(p) for p in loaded[0]]
    hold_pixels = [list(p) for p in loaded[1]]
    timing_offset = loaded[2]

    selected_lane = 0
    selected_row  = 'both'
    all_lanes     = False
    step_size     = 5

    LANE_LABELS = ['Z', 'X', ',', '.']
    TAP_COLOR       = '#00ff00'
    HOLD_COLOR      = '#ff8800'
    SELECTED_COLOR  = '#ff00ff'
    LINE_COLOR      = '#444444'
    BOX_SIZE        = SAMPLE_HALF * 2 + 1

    root = tk.Tk()
    root.title("Calibration Overlay")
    root.attributes('-fullscreen', True)
    root.attributes('-topmost', True)
    root.configure(bg='gray1')
    root.attributes('-transparentcolor', 'gray1')

    canvas = tk.Canvas(root, highlightthickness=0, bg='gray1')
    canvas.pack(fill='both', expand=True)

    # ── Control panel ────────────────────────────────────────────────────────
    panel = tk.Frame(root, bg='#222222', bd=2, relief='raised')
    panel.place(x=20, y=20)

    tk.Label(panel, text="Manual Calibration", font=('Consolas', 14, 'bold'),
             fg='white', bg='#222222').grid(row=0, column=0, columnspan=4, pady=(8, 4), padx=10)

    info_lbl = tk.Label(panel, text="", font=('Consolas', 10),
                        fg='#aaaaaa', bg='#222222', justify='left')
    info_lbl.grid(row=1, column=0, columnspan=4, pady=(0, 6), padx=10, sticky='w')

    coord_lbl = tk.Label(panel, text="", font=('Consolas', 11),
                         fg='#00ffcc', bg='#222222')
    coord_lbl.grid(row=2, column=0, columnspan=4, pady=(0, 6), padx=10)

    # Lane buttons
    lane_frame = tk.Frame(panel, bg='#222222')
    lane_frame.grid(row=3, column=0, columnspan=4, pady=4, padx=10)
    lane_btns = []

    def select_lane(i):
        nonlocal selected_lane, all_lanes
        selected_lane = i
        all_lanes = False
        refresh()

    for i, label in enumerate(LANE_LABELS):
        btn = tk.Button(lane_frame, text=f" {label} ", font=('Consolas', 11, 'bold'),
                        width=3, command=lambda i=i: select_lane(i))
        btn.pack(side='left', padx=2)
        lane_btns.append(btn)

    def toggle_all_lanes():
        nonlocal all_lanes
        all_lanes = not all_lanes
        refresh()

    all_btn = tk.Button(lane_frame, text="ALL", font=('Consolas', 10, 'bold'),
                        width=4, command=toggle_all_lanes)
    all_btn.pack(side='left', padx=(8, 2))

    # Tap / Hold / Both toggle
    row_frame = tk.Frame(panel, bg='#222222')
    row_frame.grid(row=4, column=0, columnspan=4, pady=4, padx=10)

    def set_row(r):
        nonlocal selected_row
        selected_row = r
        refresh()

    tap_btn = tk.Button(row_frame, text="Tap", font=('Consolas', 11), width=5,
                        command=lambda: set_row('tap'))
    tap_btn.pack(side='left', padx=3)
    hold_btn = tk.Button(row_frame, text="Hold", font=('Consolas', 11), width=5,
                         command=lambda: set_row('hold'))
    hold_btn.pack(side='left', padx=3)
    both_btn = tk.Button(row_frame, text="Both", font=('Consolas', 11), width=5,
                         command=lambda: set_row('both'))
    both_btn.pack(side='left', padx=3)

    # Arrow buttons
    arrow_frame = tk.Frame(panel, bg='#222222')
    arrow_frame.grid(row=5, column=0, columnspan=4, pady=6, padx=10)

    def move(dx, dy):
        if all_lanes:
            lanes = range(4)
        else:
            lanes = [selected_lane]
        move_tap  = selected_row in ('tap', 'both')
        move_hold = selected_row in ('hold', 'both')
        for i in lanes:
            if move_tap:
                tap_pixels[i][0] += dx * step_size
                tap_pixels[i][1] += dy * step_size
            if move_hold:
                hold_pixels[i][0] += dx * step_size
                hold_pixels[i][1] += dy * step_size
        refresh()

    tk.Button(arrow_frame, text="^", font=('Consolas', 12, 'bold'), width=3,
              command=lambda: move(0, -1)).grid(row=0, column=1, padx=2, pady=1)
    tk.Button(arrow_frame, text="<", font=('Consolas', 12, 'bold'), width=3,
              command=lambda: move(-1, 0)).grid(row=1, column=0, padx=2, pady=1)
    tk.Button(arrow_frame, text=">", font=('Consolas', 12, 'bold'), width=3,
              command=lambda: move(1, 0)).grid(row=1, column=2, padx=2, pady=1)
    tk.Button(arrow_frame, text="v", font=('Consolas', 12, 'bold'), width=3,
              command=lambda: move(0, 1)).grid(row=2, column=1, padx=2, pady=1)

    # Distance control
    dist_frame = tk.Frame(panel, bg='#222222')
    dist_frame.grid(row=6, column=0, columnspan=4, pady=4, padx=10)

    tk.Label(dist_frame, text="Distance:", font=('Consolas', 10, 'bold'),
             fg='white', bg='#222222').pack(side='left')
    step_var = tk.StringVar(value=str(step_size))

    def change_step(delta):
        nonlocal step_size
        step_size = max(1, step_size + delta)
        step_var.set(str(step_size))

    tk.Button(dist_frame, text="-", font=('Consolas', 11, 'bold'), width=2,
              bg='#444444', fg='white',
              command=lambda: change_step(-1)).pack(side='left', padx=3)
    tk.Label(dist_frame, textvariable=step_var, font=('Consolas', 12, 'bold'),
             fg='#ffff00', bg='#333333', width=3, relief='sunken').pack(side='left')
    tk.Button(dist_frame, text="+", font=('Consolas', 11, 'bold'), width=2,
              bg='#444444', fg='white',
              command=lambda: change_step(1)).pack(side='left', padx=3)

    tk.Label(panel, text="(pixels per arrow press)", font=('Consolas', 8),
             fg='#888888', bg='#222222').grid(row=7, column=0, columnspan=4, pady=(0, 4))

    # ── Timing offset control ────────────────────────────────────────────────
    offset_frame = tk.Frame(panel, bg='#222222')
    offset_frame.grid(row=8, column=0, columnspan=4, pady=4, padx=10)

    tk.Label(offset_frame, text="Timing:", font=('Consolas', 10, 'bold'),
             fg='white', bg='#222222').pack(side='left')
    offset_var = tk.StringVar(value=str(timing_offset))

    def change_offset(delta):
        nonlocal timing_offset
        timing_offset = max(0, min(30, timing_offset + delta))
        offset_var.set(str(timing_offset))

    tk.Button(offset_frame, text="-", font=('Consolas', 11, 'bold'), width=2,
              bg='#444444', fg='white',
              command=lambda: change_offset(-1)).pack(side='left', padx=3)
    tk.Label(offset_frame, textvariable=offset_var, font=('Consolas', 12, 'bold'),
             fg='#ff88ff', bg='#333333', width=3, relief='sunken').pack(side='left')
    tk.Button(offset_frame, text="+", font=('Consolas', 11, 'bold'), width=2,
              bg='#444444', fg='white',
              command=lambda: change_offset(1)).pack(side='left', padx=3)

    tk.Label(panel, text="(detect earlier, 0=exact)", font=('Consolas', 8),
             fg='#888888', bg='#222222').grid(row=9, column=0, columnspan=4, pady=(0, 4))

    # Save / Close buttons
    btn_frame = tk.Frame(panel, bg='#222222')
    btn_frame.grid(row=10, column=0, columnspan=4, pady=(4, 10), padx=10)

    def do_save():
        save_coords([tuple(p) for p in tap_pixels], [tuple(p) for p in hold_pixels], timing_offset)
        save_lbl.config(text="Saved!", fg='#00ff00')
        root.after(2000, lambda: save_lbl.config(text=""))

    def do_close():
        root.destroy()

    tk.Button(btn_frame, text="Save", font=('Consolas', 11, 'bold'), width=8,
              bg='#006600', fg='white', command=do_save).pack(side='left', padx=4)
    tk.Button(btn_frame, text="Close", font=('Consolas', 11, 'bold'), width=8,
              bg='#660000', fg='white', command=do_close).pack(side='left', padx=4)

    save_lbl = tk.Label(panel, text="", font=('Consolas', 10, 'bold'),
                        fg='#00ff00', bg='#222222')
    save_lbl.grid(row=11, column=0, columnspan=4, pady=(0, 6))

    tk.Label(panel, text="Keys: Arrows=move 1-4=lane\nA=all T/H/B=row S=save Esc=close",
             font=('Consolas', 8), fg='#888888', bg='#222222', justify='left'
             ).grid(row=12, column=0, columnspan=4, pady=(0, 8), padx=10)

    # ── Draw overlay ─────────────────────────────────────────────────────────
    def refresh():
        canvas.delete('all')
        half = BOX_SIZE // 2
        screen_w = root.winfo_screenwidth()

        tap_ys = [p[1] for p in tap_pixels]
        if len(set(tap_ys)) == 1:
            y = tap_ys[0]
            canvas.create_line(0, y, screen_w, y, fill=TAP_COLOR, width=1, dash=(4, 4))

        hold_ys = [p[1] for p in hold_pixels]
        if len(set(hold_ys)) == 1:
            y = hold_ys[0]
            canvas.create_line(0, y, screen_w, y, fill=HOLD_COLOR, width=1, dash=(4, 4))

        for i in range(4):
            tx, ty = tap_pixels[i]
            hx, hy = hold_pixels[i]
            if tx == hx:
                canvas.create_line(tx, hy - half - 6, tx, ty + half + 6,
                                   fill=LINE_COLOR, width=1)
            else:
                canvas.create_line(hx, hy - half - 2, tx, ty + half + 2,
                                   fill='#ff3333', width=1, dash=(3, 3))

        for i in range(4):
            is_sel = all_lanes or i == selected_lane
            is_selected_tap  = is_sel and selected_row in ('tap', 'both')
            is_selected_hold = is_sel and selected_row in ('hold', 'both')

            tx, ty = tap_pixels[i]
            tc = SELECTED_COLOR if is_selected_tap else TAP_COLOR
            canvas.create_rectangle(tx - half, ty - half, tx + half, ty + half,
                                    outline=tc, width=2)
            canvas.create_text(tx, ty - half - 12, text=LANE_LABELS[i],
                               fill=tc, font=('Consolas', 9, 'bold'))
            canvas.create_text(tx, ty + half + 12, text=f"T({tx},{ty})",
                               fill=tc, font=('Consolas', 8))

            hx, hy = hold_pixels[i]
            hc = SELECTED_COLOR if is_selected_hold else HOLD_COLOR
            canvas.create_rectangle(hx - half, hy - half, hx + half, hy + half,
                                    outline=hc, width=2)
            canvas.create_text(hx, hy + half + 12, text=f"H({hx},{hy})",
                               fill=hc, font=('Consolas', 8))

        tap_aligned  = len(set(tap_ys)) == 1
        hold_aligned = len(set(hold_ys)) == 1
        vert_aligned = all(tap_pixels[i][0] == hold_pixels[i][0] for i in range(4))

        align_parts = []
        if tap_aligned:
            align_parts.append("Tap row: aligned")
        else:
            align_parts.append("Tap row: MISALIGNED")
        if hold_aligned:
            align_parts.append("Hold row: aligned")
        else:
            align_parts.append("Hold row: MISALIGNED")
        if vert_aligned:
            align_parts.append("Verticals: aligned")
        else:
            align_parts.append("Verticals: MISALIGNED")

        align_color = '#00ff00' if (tap_aligned and hold_aligned and vert_aligned) else '#ff6666'
        align_text = " | ".join(align_parts)

        screen_h = root.winfo_screenheight()
        canvas.create_text(screen_w // 2, screen_h - 30, text=align_text,
                           fill=align_color, font=('Consolas', 10, 'bold'))

        mode = "ALL LANES" if all_lanes else f"Lane {LANE_LABELS[selected_lane]}"
        info_lbl.config(text=f"Mode: {mode} | Row: {selected_row.upper()}")

        t = tap_pixels[selected_lane]
        h = hold_pixels[selected_lane]
        if selected_row == 'tap':
            coord_lbl.config(text=f"Tap ({t[0]}, {t[1]})")
        elif selected_row == 'hold':
            coord_lbl.config(text=f"Hold ({h[0]}, {h[1]})")
        else:
            coord_lbl.config(text=f"T({t[0]},{t[1]})  H({h[0]},{h[1]})")

        for idx, btn in enumerate(lane_btns):
            if all_lanes:
                btn.config(bg='#555555', fg='white')
            elif idx == selected_lane:
                btn.config(bg='#0066cc', fg='white')
            else:
                btn.config(bg='SystemButtonFace', fg='black')
        all_btn.config(bg='#cc6600' if all_lanes else 'SystemButtonFace',
                       fg='white' if all_lanes else 'black')
        tap_btn.config(bg='#006600' if selected_row in ('tap', 'both') else 'SystemButtonFace',
                       fg='white' if selected_row in ('tap', 'both') else 'black')
        hold_btn.config(bg='#cc6600' if selected_row in ('hold', 'both') else 'SystemButtonFace',
                        fg='white' if selected_row in ('hold', 'both') else 'black')
        both_btn.config(bg='#9900cc' if selected_row == 'both' else 'SystemButtonFace',
                        fg='white' if selected_row == 'both' else 'black')

    def on_key(event):
        nonlocal selected_lane, selected_row, all_lanes
        k = event.keysym
        if k == 'Up':
            move(0, -1)
        elif k == 'Down':
            move(0, 1)
        elif k == 'Left':
            move(-1, 0)
        elif k == 'Right':
            move(1, 0)
        elif k in ('1', '2', '3', '4'):
            selected_lane = int(k) - 1
            all_lanes = False
            refresh()
        elif k == 'a':
            all_lanes = not all_lanes
            refresh()
        elif k == 't':
            selected_row = 'tap'
            refresh()
        elif k == 'h':
            selected_row = 'hold'
            refresh()
        elif k == 'b':
            selected_row = 'both'
            refresh()
        elif k == 's':
            do_save()
        elif k == 'Escape':
            do_close()

    root.bind('<Key>', on_key)
    refresh()
    root.mainloop()


# ─── MACRO RUN (OPTIMIZED) ───────────────────────────────────────────────────

def run(status_var=None, stop_event=None):
    TAP_PIXELS, HOLD_PIXELS, timing_offset = load_coords()

    # Apply timing offset — shift tap detection upward (earlier)
    if timing_offset > 0:
        TAP_PIXELS  = [(x, y - timing_offset) for x, y in TAP_PIXELS]
        HOLD_PIXELS = [(x, y - timing_offset) for x, y in HOLD_PIXELS]

    all_pixels = TAP_PIXELS + HOLD_PIXELS
    xs = [p[0] for p in all_pixels]
    ys = [p[1] for p in all_pixels]

    cap_left   = min(xs) - SAMPLE_HALF - 1
    cap_top    = min(ys) - SAMPLE_HALF - 1
    cap_right  = max(xs) + SAMPLE_HALF + 1
    cap_bottom = max(ys) + SAMPLE_HALF + 1

    monitor = {
        'left':   cap_left,
        'top':    cap_top,
        'width':  cap_right  - cap_left,
        'height': cap_bottom - cap_top,
    }

    # Pre-compute relative coords as numpy-friendly values
    tap_rel  = [(px - cap_left, py - cap_top) for px, py in TAP_PIXELS]
    hold_rel = [(px - cap_left, py - cap_top) for px, py in HOLD_PIXELS]
    sh = SAMPLE_HALF

    # Pre-build slice objects for each zone (avoids recomputing every frame)
    tap_slices = [(slice(ty-sh, ty+sh+1), slice(tx-sh, tx+sh+1)) for tx, ty in tap_rel]
    hold_slices = [(slice(hy-sh, hy+sh+1), slice(hx-sh, hx+sh+1)) for hx, hy in hold_rel]

    states           = ['idle']  * 4
    last_pressed_at  = [0.0]    * 4  # timestamp of last tap press per lane
    hold_incoming    = [False]   * 4
    hold_seen_at     = [0.0]    * 4  # when we last saw hold gray (for grace window on slow PCs)
    hold_saw_tail    = [False]   * 4
    tap_release_at   = [0.0]    * 4
    hold_released_at = [0.0]    * 4
    hold_started_at  = [0.0]    * 4

    # ── Miss logger: tracks notes that pass without a press ───────────
    note_visible     = [False]   * 4   # True while white >= MIN_PIXELS
    note_was_pressed = [False]   * 4   # True if we pressed during this note
    miss_log_lines   = []

    # Run system diagnostic and write to log
    run_system_check()

    active      = True
    last_toggle = 0.0

    frame_count = 0
    fps_timer   = time.time()
    current_fps = 0

    _time = time.time
    _np_sum = np.sum

    def set_status(text):
        if status_var:
            status_var.set(text)

    def flush_miss_log():
        if miss_log_lines:
            try:
                with open(LOG_FILE, 'a') as lf:
                    lf.write('\n'.join(miss_log_lines) + '\n')
                miss_log_lines.clear()
            except Exception:
                pass

    def release_all():
        for i in range(4):
            if states[i] == 'holding':
                key_release(KEYS[i])
            if tap_release_at[i] > 0:
                key_release(KEYS[i])
                tap_release_at[i] = 0.0
            states[i]        = 'idle'
            hold_incoming[i] = False
            hold_seen_at[i]  = 0.0
            hold_saw_tail[i] = False
            hold_started_at[i] = 0.0
        flush_miss_log()

    set_status(f"Running | Timing: {timing_offset}px | FPS: --")

    with mss.mss() as sct:
        while True:
            if stop_event and stop_event.is_set():
                release_all()
                return

            now = _time()

            # ── FPS counter ──────────────────────────────────────────────────
            frame_count += 1
            elapsed = now - fps_timer
            if elapsed >= 1.0:
                current_fps = int(frame_count / elapsed)
                frame_count = 0
                fps_timer = now
                if active:
                    if is_roblox_focused():
                        set_status(f"Running | T:{timing_offset}px | FPS: {current_fps}")
                    else:
                        set_status(f"Waiting for Roblox... | FPS: {current_fps}")

            # ── L: pause / resume ────────────────────────────────────────────
            if is_key_down(VK_L) and now - last_toggle > TOGGLE_DELAY:
                active      = not active
                last_toggle = now
                if not active:
                    release_all()
                set_status(f"{'Paused' if not active else 'Running'} | T:{timing_offset}px | FPS: {current_fps}")

            if not active:
                time.sleep(0.01)
                continue

            # ── Skip if Roblox is not focused ────────────────────────────────
            if not is_roblox_focused():
                release_all()
                time.sleep(0.05)
                continue

            # ── Non-blocking tap releases ────────────────────────────────────
            for i in range(4):
                if tap_release_at[i] > 0 and now >= tap_release_at[i]:
                    key_release(KEYS[i])
                    tap_release_at[i] = 0.0

            # ── Safety: force-release holds that exceeded max duration ────
            for i in range(4):
                if states[i] == 'holding' and hold_started_at[i] > 0:
                    if now - hold_started_at[i] >= MAX_HOLD_DURATION:
                        key_release(KEYS[i])
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_seen_at[i]     = 0.0
                        hold_released_at[i] = now
                        hold_started_at[i]  = 0.0

            # ── Single capture + numpy extraction ────────────────────────────
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            r = img_np[:, :, 2]
            g = img_np[:, :, 1]
            b = img_np[:, :, 0]

            for i in range(4):
                # ── Tap zone (pre-built slices) ──────────────────────────────
                sy, sx = tap_slices[i]
                tr = r[sy, sx]; tg = g[sy, sx]; tb = b[sy, sx]

                white_count = int(_np_sum(
                    (tr >= WHITE_MIN) & (tg >= WHITE_MIN) & (tb >= WHITE_MIN)
                ))
                tap_gray_count = int(_np_sum(
                    (tr >= GRAY_MIN) & (tr <= GRAY_MAX) &
                    (tg >= GRAY_MIN) & (tg <= GRAY_MAX) &
                    (tb >= GRAY_MIN) & (tb <= GRAY_MAX)
                ))

                # ── Hold zone (pre-built slices) ─────────────────────────────
                sy, sx = hold_slices[i]
                hr = r[sy, sx]; hg = g[sy, sx]; hb = b[sy, sx]

                hold_gray_count = int(_np_sum(
                    (hr >= GRAY_MIN) & (hr <= GRAY_MAX) &
                    (hg >= GRAY_MIN) & (hg <= GRAY_MAX) &
                    (hb >= GRAY_MIN) & (hb <= GRAY_MAX)
                ))
                hold_has_white = bool(np.any(
                    (hr >= WHITE_MIN) & (hg >= WHITE_MIN) & (hb >= WHITE_MIN)
                ))

                key   = KEYS[i]
                state = states[i]

                # ── Miss tracking: detect note appearance BEFORE state machine ──
                # Must run before presses so note_was_pressed isn't reset after a press
                if white_count >= MIN_PIXELS:
                    if not note_visible[i]:
                        note_visible[i]     = True
                        note_was_pressed[i] = False

                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True
                    hold_seen_at[i]  = now

                if state == 'holding':
                    if tap_gray_count >= MIN_PIXELS:
                        hold_saw_tail[i] = True
                    elif hold_saw_tail[i]:
                        key_release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_seen_at[i]     = 0.0
                        hold_released_at[i] = now
                        hold_started_at[i]  = 0.0
                    elif white_count == 0 and tap_gray_count == 0 and hold_gray_count == 0:
                        key_release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_seen_at[i]     = 0.0
                        hold_released_at[i] = now
                        hold_started_at[i]  = 0.0

                if state == 'idle' and now - hold_released_at[i] >= HOLD_RELEASE_COOLDOWN:
                    if hold_seen_at[i] > 0 and now - hold_seen_at[i] >= HOLD_GRACE_SEC:
                        hold_seen_at[i] = 0.0
                    if white_count >= MIN_PIXELS:
                        is_hold = hold_incoming[i] or (hold_seen_at[i] > 0 and now - hold_seen_at[i] < HOLD_GRACE_SEC)
                        if is_hold:
                            key_press(key)
                            states[i]       = 'holding'
                            hold_incoming[i] = False
                            hold_seen_at[i]  = 0.0
                            hold_started_at[i] = now
                            note_was_pressed[i] = True
                        else:
                            _since_last = now - last_pressed_at[i]
                            if _since_last > JACK_THRESHOLD and random.random() < GOOD_CHANCE:
                                # Not a jack — safe to delay
                                note_was_pressed[i] = True
                                last_pressed_at[i]  = now
                                _d = random.uniform(GOOD_DELAY_MIN, GOOD_DELAY_MAX)
                                def _good(k=key, d=_d):
                                    time.sleep(d)
                                    key_press(k)
                                    time.sleep(TAP_KEY_DURATION)
                                    key_release(k)
                                threading.Thread(target=_good, daemon=True).start()
                            else:
                                key_press(key)
                                tap_release_at[i]   = now + TAP_KEY_DURATION
                                states[i]           = 'tapped'
                                note_was_pressed[i] = True
                                last_pressed_at[i]  = now

                elif state == 'tapped':
                    if white_count < MIN_PIXELS and tap_release_at[i] == 0.0:
                        states[i]        = 'idle'
                        hold_incoming[i] = False
                        hold_seen_at[i]  = 0.0

                # ── Miss detection: note disappeared — did we press? ──────
                if white_count < MIN_PIXELS:
                    if note_visible[i]:
                        if not note_was_pressed[i]:
                            avg_tr = int(np.mean(tr))
                            avg_tg = int(np.mean(tg))
                            avg_tb = int(np.mean(tb))
                            avg_hr = int(np.mean(hr))
                            avg_hg = int(np.mean(hg))
                            avg_hb = int(np.mean(hb))
                            cooldown_left = max(0, HOLD_RELEASE_COOLDOWN - (now - hold_released_at[i]))
                            line = (
                                f"MISS lane={i}({key}) t={now:.3f} "
                                f"state={state} white={white_count} gray={tap_gray_count} "
                                f"hold_gray={hold_gray_count} hold_white={hold_has_white} "
                                f"hold_inc={hold_incoming[i]} saw_tail={hold_saw_tail[i]} "
                                f"cooldown={cooldown_left:.3f} "
                                f"tap_avg=({avg_tr},{avg_tg},{avg_tb}) "
                                f"hold_avg=({avg_hr},{avg_hg},{avg_hb}) "
                                f"fps={current_fps}"
                            )
                            miss_log_lines.append(line)
                            if len(miss_log_lines) >= 10:
                                flush_miss_log()
                        note_visible[i]     = False
                        note_was_pressed[i] = False


# ─── GUI LAUNCHER ─────────────────────────────────────────────────────────────

def main_gui():
    import tkinter as tk
    import threading

    root = tk.Tk()
    root.title("larpLOL")
    root.configure(bg='#1a1a2e')
    root.resizable(False, False)
    root.geometry('340x360')

    root.update_idletasks()
    sw = root.winfo_screenwidth()
    sh_ = root.winfo_screenheight()
    x = (sw - 340) // 2
    y = (sh_ - 360) // 2
    root.geometry(f'340x360+{x}+{y}')

    tk.Label(root, text="larpLOL", font=('Consolas', 22, 'bold'),
             fg='#e94560', bg='#1a1a2e').pack(pady=(24, 4))
    tk.Label(root, text="Macro", font=('Consolas', 12),
             fg='#aaaaaa', bg='#1a1a2e').pack(pady=(0, 16))

    status_var = tk.StringVar(value="Ready")
    tk.Label(root, textvariable=status_var, font=('Consolas', 10),
             fg='#00ffcc', bg='#1a1a2e').pack(pady=(0, 12))

    if os.path.exists(COORDS_FILE):
        coords_text = "coords.txt loaded"
        coords_color = '#00ff00'
    else:
        coords_text = "using default coords"
        coords_color = '#ffaa00'
    coords_lbl = tk.Label(root, text=coords_text, font=('Consolas', 9),
                          fg=coords_color, bg='#1a1a2e')
    coords_lbl.pack(pady=(0, 16))

    stop_event = threading.Event()
    macro_running = False

    def on_run():
        nonlocal macro_running
        if macro_running:
            return
        macro_running = True
        stop_event.clear()
        run_btn.config(state='disabled', bg='#333333')
        cal_btn.config(state='disabled')
        stop_btn.config(state='normal', bg='#cc0000')

        def target():
            nonlocal macro_running
            try:
                run(status_var=status_var, stop_event=stop_event)
            except Exception as e:
                status_var.set(f"Error: {e}")
            finally:
                macro_running = False
                root.after(0, on_macro_stopped)

        def on_macro_stopped():
            run_btn.config(state='normal', bg='#006600')
            cal_btn.config(state='normal')
            stop_btn.config(state='disabled', bg='#333333')
            if not stop_event.is_set():
                status_var.set("Ready")

        threading.Thread(target=target, daemon=True).start()

    def on_stop():
        nonlocal macro_running
        if not macro_running:
            return
        stop_event.set()
        for k in KEYS:
            try:
                key_release(k)
            except Exception:
                pass
        status_var.set("Stopped")

    def on_calibrate():
        root.withdraw()
        calibrate()
        root.deiconify()
        if os.path.exists(COORDS_FILE):
            coords_lbl.config(text="coords.txt loaded", fg='#00ff00')
            status_var.set("Calibration saved!")
        else:
            status_var.set("Ready")

    def on_quit():
        stop_event.set()
        for k in KEYS:
            try:
                key_release(k)
            except Exception:
                pass
        root.destroy()

    btn_style = {'font': ('Consolas', 13, 'bold'), 'width': 22, 'height': 1,
                 'cursor': 'hand2', 'bd': 0, 'activeforeground': 'white'}

    run_btn = tk.Button(root, text="Run Macro", bg='#006600', fg='white',
                        activebackground='#008800', command=on_run, **btn_style)
    run_btn.pack(pady=4)

    stop_btn = tk.Button(root, text="Stop Macro", bg='#333333', fg='white',
                         activebackground='#ff0000', command=on_stop, state='disabled',
                         **btn_style)
    stop_btn.pack(pady=4)

    cal_btn = tk.Button(root, text="Calibrate", bg='#0066cc', fg='white',
                        activebackground='#0088ff', command=on_calibrate, **btn_style)
    cal_btn.pack(pady=4)

    tk.Button(root, text="Quit", bg='#660000', fg='white',
              activebackground='#880000', command=on_quit, **btn_style).pack(pady=4)

    tk.Label(root, text="L = pause/resume while running", font=('Consolas', 8),
             fg='#555555', bg='#1a1a2e').pack(side='bottom', pady=8)

    root.protocol("WM_DELETE_WINDOW", on_quit)
    root.mainloop()


if __name__ == '__main__':
    main_gui()
