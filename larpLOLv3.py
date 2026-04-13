import sys
import os
import json
import time
import ctypes
import tkinter as tk
import mss
import numpy as np
import keyboard
import cv2

# ─── paths ────────────────────────────────────────────────────────────────────
if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))

COORDS_FILE = os.path.join(BASE_DIR, 'coords.txt')

# ─── defaults ─────────────────────────────────────────────────────────────────
_DEFAULT_TAP  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]
_DEFAULT_HOLD = [(720, 824), (878, 824), (1040, 824), (1197, 824)]
SAMPLE_HALF   = 3

WHITE_MIN  = 240
GRAY_MIN   = 130
GRAY_MAX   = 170
MIN_PIXELS = 3

TAP_KEY_DURATION      = 0.03
HOLD_RELEASE_COOLDOWN = 0.06
TOGGLE_DELAY          = 0.3

KEYS        = ['z', 'x', ',', '.']
LANE_NAMES  = ['Z', 'X', ',', '.']
LANE_COLORS = [(80, 80, 255), (80, 255, 80), (0, 180, 255), (200, 80, 255)]

DBG_WIN  = "Rhythm Macro — Debug"
DBG_PANW = 235
DBG_BOX  = 18

CAL_WIN   = "Rhythm Macro — Calibration"
CAL_SCALE = 0.5
CAL_GRAB  = 18
CAL_HAND  = 13


# ─── coord helpers ────────────────────────────────────────────────────────────

def load_coords():
    if os.path.exists(COORDS_FILE):
        try:
            with open(COORDS_FILE) as f:
                d = json.load(f)
            print(f"Loaded coords from {COORDS_FILE}")
            return [tuple(p) for p in d['tap']], [tuple(p) for p in d['hold']]
        except Exception as e:
            print(f"Could not read {COORDS_FILE}: {e}")
    return list(_DEFAULT_TAP), list(_DEFAULT_HOLD)


def save_coords(tap, hold):
    with open(COORDS_FILE, 'w') as f:
        json.dump({'tap':  [list(p) for p in tap],
                   'hold': [list(p) for p in hold]}, f, indent=2)
    print(f"Saved to {COORDS_FILE}")


# ─── monitor detection helpers ────────────────────────────────────────────────

def _get_cursor_pos():
    class _POINT(ctypes.Structure):
        _fields_ = [('x', ctypes.c_long), ('y', ctypes.c_long)]
    pt = _POINT()
    ctypes.windll.user32.GetCursorPos(ctypes.byref(pt))
    return pt.x, pt.y


def _monitor_for_point(sct, x, y):
    for mon in sct.monitors[1:]:
        if (mon['left'] <= x < mon['left'] + mon['width'] and
                mon['top'] <= y < mon['top'] + mon['height']):
            return mon
    return sct.monitors[1]


def _find_roblox_monitor(sct):
    try:
        import win32gui
        hwnds = []
        def _cb(hwnd, lst):
            if win32gui.IsWindowVisible(hwnd) and 'Roblox' in win32gui.GetWindowText(hwnd):
                lst.append(hwnd)
        win32gui.EnumWindows(_cb, hwnds)
        if hwnds:
            rect = win32gui.GetWindowRect(hwnds[0])
            cx = (rect[0] + rect[2]) // 2
            cy = (rect[1] + rect[3]) // 2
            return _monitor_for_point(sct, cx, cy)
    except ImportError:
        pass
    return None


# ─── CALIBRATION ──────────────────────────────────────────────────────────────

def run_calibration():
    tap_pts, hold_pts = load_coords()
    cv2.namedWindow(CAL_WIN, cv2.WINDOW_AUTOSIZE)

    dragging = None
    hover    = None

    def mouse_cb(event, mx, my, flags, _):
        nonlocal dragging, hover

        def nearest():
            best_d, best = CAL_GRAB ** 2, None
            for i in range(4):
                dx, dy = _s2d(*tap_pts[i])
                d = (mx - dx) ** 2 + (my - dy) ** 2
                if d <= best_d: best_d, best = d, ('tap', i)
            for i in range(4):
                dx, dy = _s2d(*hold_pts[i])
                d = (mx - dx) ** 2 + (my - dy) ** 2
                if d <= best_d: best_d, best = d, ('hold', i)
            return best

        if   event == cv2.EVENT_LBUTTONDOWN: dragging = nearest()
        elif event == cv2.EVENT_LBUTTONUP:   dragging = None
        elif event == cv2.EVENT_MOUSEMOVE:
            hover = nearest()
            if dragging:
                sx, sy = _d2s(mx, my)
                t, i   = dragging
                if t == 'tap':  tap_pts[i]  = (max(0, sx), max(0, sy))
                else:           hold_pts[i] = (max(0, sx), max(0, sy))

    cv2.setMouseCallback(CAL_WIN, mouse_cb)
    print("\nCalibration open — drag T/H boxes to align. S=save  ESC=cancel\n")

    with mss.mss() as sct:
        mon = _find_roblox_monitor(sct)
        if mon is not None:
            print(f"Roblox detected — calibrating on monitor at "
                  f"left={mon['left']}, top={mon['top']} "
                  f"({mon['width']}x{mon['height']})")
        else:
            mx, my = _get_cursor_pos()
            mon = _monitor_for_point(sct, mx, my)
            print(f"Roblox not found — using monitor the mouse is on: "
                  f"left={mon['left']}, top={mon['top']} "
                  f"({mon['width']}x{mon['height']})")

        _s2d = lambda sx, sy: (int(round((sx - mon['left']) * CAL_SCALE)),
                                int(round((sy - mon['top'])  * CAL_SCALE)))
        _d2s = lambda dx, dy: (int(round(dx / CAL_SCALE)) + mon['left'],
                                int(round(dy / CAL_SCALE)) + mon['top'])

        while True:
            img    = sct.grab(mon)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            frame  = img_np[:, :, :3].copy()
            dw     = int(frame.shape[1] * CAL_SCALE)
            dh     = int(frame.shape[0] * CAL_SCALE)
            disp   = cv2.resize(frame, (dw, dh), interpolation=cv2.INTER_LINEAR)
            disp   = (disp.astype(np.float32) * 0.65).astype(np.uint8)

            for i in range(4):
                col  = LANE_COLORS[i]
                name = LANE_NAMES[i]

                for kind in ('tap', 'hold'):
                    pts        = tap_pts if kind == 'tap' else hold_pts
                    cx_s, cy_s = pts[i]
                    dx_, dy_   = _s2d(cx_s, cy_s)
                    dx_ = max(CAL_HAND, min(dw - CAL_HAND - 1, dx_))
                    dy_ = max(CAL_HAND, min(dh - CAL_HAND - 1, dy_))
                    hot = (dragging == (kind, i) or
                           (dragging is None and hover == (kind, i)))

                    if hot:
                        y1 = max(0, dy_ - CAL_HAND); y2 = min(dh, dy_ + CAL_HAND + 1)
                        x1 = max(0, dx_ - CAL_HAND); x2 = min(dw, dx_ + CAL_HAND + 1)
                        roi  = disp[y1:y2, x1:x2].astype(np.float32)
                        fill = np.full_like(roi, col, dtype=np.float32)
                        disp[y1:y2, x1:x2] = (roi * 0.55 + fill * 0.45).astype(np.uint8)

                    thick = (3 if hot else 2) if kind == 'tap' else (2 if hot else 1)
                    cv2.rectangle(disp,
                                  (dx_ - CAL_HAND, dy_ - CAL_HAND),
                                  (dx_ + CAL_HAND, dy_ + CAL_HAND), col, thick)
                    cv2.drawMarker(disp, (dx_, dy_), col,
                                   cv2.MARKER_CROSS, 10, 1, cv2.LINE_AA)
                    sh = max(1, int(SAMPLE_HALF * CAL_SCALE))
                    inner_col = (255, 255, 255) if kind == 'tap' else (200, 200, 200)
                    cv2.rectangle(disp, (dx_-sh, dy_-sh), (dx_+sh, dy_+sh), inner_col, 1)
                    lbl = f"T{name}" if kind == 'tap' else f"H{name}"
                    cv2.putText(disp, lbl, (dx_ - CAL_HAND, dy_ - CAL_HAND - 4),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.4, col, 1, cv2.LINE_AA)
                    if hot:
                        cv2.putText(disp, f'({cx_s}, {cy_s})',
                                    (dx_ + CAL_HAND + 3, dy_ + 5),
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.35, col, 1, cv2.LINE_AA)

            disp[:32] = (disp[:32].astype(np.float32) * 0.25).astype(np.uint8)
            if dragging:
                t, i     = dragging
                sx_, sy_ = (tap_pts if t == 'tap' else hold_pts)[i]
                msg      = (f"Dragging {t.upper()} {LANE_NAMES[i]} -> "
                            f"({sx_}, {sy_})   |   S = Save & Quit   ESC = Cancel")
                mc = LANE_COLORS[i]
            else:
                msg = ("Drag T / H boxes to align  |  T=thick=tap  "
                       "H=thin=hold  |  S = Save & Quit   ESC = Cancel")
                mc  = (0, 220, 255)
            cv2.putText(disp, msg, (8, 22),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, mc, 1, cv2.LINE_AA)

            cv2.imshow(CAL_WIN, disp)
            key = cv2.waitKey(16) & 0xFF
            if key in (ord('s'), ord('S')):
                save_coords(tap_pts, hold_pts)
                print("Saved! Returning to menu.")
                break
            elif key == 27:
                print("Cancelled — no changes saved.")
                break

    cv2.destroyWindow(CAL_WIN)


# ─── MACRO ────────────────────────────────────────────────────────────────────

def run_macro(start_debug=False):
    TAP_PIXELS, HOLD_PIXELS = load_coords()
    print(f"  Tap  zones: {TAP_PIXELS}")
    print(f"  Hold zones: {HOLD_PIXELS}")

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

    def rel(px, py):
        return px - cap_left, py - cap_top

    tap_rel  = [rel(x, y) for x, y in TAP_PIXELS]
    hold_rel = [rel(x, y) for x, y in HOLD_PIXELS]
    sh = SAMPLE_HALF

    # Per-lane state
    states           = ['idle'] * 4
    hold_incoming    = [False]  * 4
    hold_saw_tail    = [False]  * 4
    tap_release_at   = [0.0]    * 4
    hold_released_at = [0.0]    * 4

    active      = True
    last_toggle = 0.0
    debug_on    = start_debug
    debug_alive = False
    dbg_mon     = None

    frame_count = 0
    fps         = 0
    fps_timer   = time.time()

    def put(panel, text, row, color=(220, 220, 220)):
        cv2.putText(panel, text, (6, row),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.38, color, 1, cv2.LINE_AA)

    print("L = pause/resume   P = toggle debug   Ctrl+C = quit")

    with mss.mss() as sct:
        while True:
            now = time.time()

            frame_count += 1
            if now - fps_timer >= 1.0:
                fps         = frame_count
                frame_count = 0
                fps_timer   = now

            # ── L: pause / resume ─────────────────────────────────────────────
            if keyboard.is_pressed('l') and now - last_toggle > TOGGLE_DELAY:
                active      = not active
                last_toggle = now
                if not active:
                    for i, s in enumerate(states):
                        if s == 'holding':        keyboard.release(KEYS[i])
                        if tap_release_at[i] > 0: keyboard.release(KEYS[i])
                    states         = ['idle'] * 4
                    hold_incoming  = [False]  * 4
                    hold_saw_tail  = [False]  * 4
                    tap_release_at = [0.0]    * 4
                print("Paused" if not active else "Resumed")

            # ── P: toggle debug window ────────────────────────────────────────
            if keyboard.is_pressed('p') and now - last_toggle > TOGGLE_DELAY:
                debug_on    = not debug_on
                last_toggle = now
                if not debug_on:
                    if debug_alive:
                        try: cv2.destroyWindow(DBG_WIN)
                        except Exception: pass
                        debug_alive = False
                        dbg_mon     = None
                print("Debug ON  (P to hide)" if debug_on else "Debug OFF (P to show)")

            if not active:
                time.sleep(0.01)
                if debug_on: cv2.waitKey(1)
                continue

            # ── non-blocking tap releases ──────────────────────────────────────
            for i in range(4):
                if tap_release_at[i] > 0 and now >= tap_release_at[i]:
                    keyboard.release(KEYS[i])
                    tap_release_at[i] = 0.0

            # ── single capture covering all 8 pixel positions ──────────────────
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            r = img_np[:, :, 2]
            g = img_np[:, :, 1]
            b = img_np[:, :, 0]

            white_counts     = [0]     * 4
            tap_gray_counts  = [0]     * 4
            hold_gray_counts = [0]     * 4
            hold_has_whites  = [False] * 4

            for i in range(4):
                # ── tap zone ──────────────────────────────────────────────────
                tx, ty = tap_rel[i]
                tr = r[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                tg = g[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                tb = b[ty-sh:ty+sh+1, tx-sh:tx+sh+1]

                white_count    = int(np.sum(
                    (tr >= WHITE_MIN) & (tg >= WHITE_MIN) & (tb >= WHITE_MIN)
                ))
                tap_gray_count = int(np.sum(
                    (tr >= GRAY_MIN) & (tr <= GRAY_MAX) &
                    (tg >= GRAY_MIN) & (tg <= GRAY_MAX) &
                    (tb >= GRAY_MIN) & (tb <= GRAY_MAX)
                ))

                # ── hold zone ─────────────────────────────────────────────────
                hx, hy = hold_rel[i]
                hr = r[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                hg = g[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                hb = b[hy-sh:hy+sh+1, hx-sh:hx+sh+1]

                hold_gray_count = int(np.sum(
                    (hr >= GRAY_MIN) & (hr <= GRAY_MAX) &
                    (hg >= GRAY_MIN) & (hg <= GRAY_MAX) &
                    (hb >= GRAY_MIN) & (hb <= GRAY_MAX)
                ))
                hold_has_white = bool(np.any(
                    (hr >= WHITE_MIN) & (hg >= WHITE_MIN) & (hb >= WHITE_MIN)
                ))

                white_counts[i]     = white_count
                tap_gray_counts[i]  = tap_gray_count
                hold_gray_counts[i] = hold_gray_count
                hold_has_whites[i]  = hold_has_white

                key   = KEYS[i]
                state = states[i]

                # ── hold zone: arm flag ───────────────────────────────────────
                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True
                elif state == 'idle' and hold_gray_count < MIN_PIXELS:
                    hold_incoming[i] = False

                # ── HOLDING ───────────────────────────────────────────────────
                if state == 'holding':
                    if tap_gray_count >= MIN_PIXELS:
                        hold_saw_tail[i] = True
                    elif hold_saw_tail[i]:
                        keyboard.release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now
                    elif white_count == 0 and tap_gray_count == 0 and hold_gray_count == 0:
                        keyboard.release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now

                # ── IDLE ──────────────────────────────────────────────────────
                if state == 'idle' and now - hold_released_at[i] >= HOLD_RELEASE_COOLDOWN:
                    if white_count >= MIN_PIXELS:
                        if hold_incoming[i]:
                            keyboard.press(key)
                            states[i]        = 'holding'
                            hold_incoming[i] = False
                        else:
                            keyboard.press(key)
                            tap_release_at[i] = now + TAP_KEY_DURATION
                            states[i]         = 'tapped'

                # ── TAPPED ────────────────────────────────────────────────────
                elif state == 'tapped':
                    if white_count < MIN_PIXELS and tap_release_at[i] == 0.0:
                        states[i]        = 'idle'
                        hold_incoming[i] = False

            # ── debug window (only active when P is pressed) ───────────────────
            if debug_on:
                if not debug_alive:
                    cv2.namedWindow(DBG_WIN, cv2.WINDOW_NORMAL)
                    debug_alive = True
                    cx = (cap_left + cap_right)  // 2
                    cy = (cap_top  + cap_bottom) // 2
                    dbg_mon = _monitor_for_point(sct, cx, cy)

                full_img = sct.grab(dbg_mon)
                full_np  = np.frombuffer(full_img.raw, dtype=np.uint8).reshape(
                               dbg_mon['height'], dbg_mon['width'], 4)
                vis  = full_np[:, :, :3].copy()
                dw_  = int(vis.shape[1] * CAL_SCALE)
                dh_  = int(vis.shape[0] * CAL_SCALE)
                disp = cv2.resize(vis, (dw_, dh_), interpolation=cv2.INTER_LINEAR)
                disp = (disp.astype(np.float32) * 0.7).astype(np.uint8)

                sh_d = max(1, int(SAMPLE_HALF * CAL_SCALE))

                for i in range(4):
                    col = LANE_COLORS[i]
                    s   = states[i]

                    tx_abs, ty_abs = TAP_PIXELS[i]
                    tx_d = int((tx_abs - dbg_mon['left']) * CAL_SCALE)
                    ty_d = int((ty_abs - dbg_mon['top'])  * CAL_SCALE)
                    tx_d = max(DBG_BOX, min(dw_ - DBG_BOX - 1, tx_d))
                    ty_d = max(DBG_BOX, min(dh_ - DBG_BOX - 1, ty_d))

                    if s == 'tapped':
                        tap_col, tap_thick = (0, 255, 255), 3
                    elif s == 'holding':
                        tap_col, tap_thick = (0, 255, 0),   3
                    elif white_counts[i] >= MIN_PIXELS:
                        tap_col, tap_thick = (255, 255, 255), 2
                    else:
                        tap_col, tap_thick = col, 1

                    cv2.rectangle(disp,
                                  (tx_d - DBG_BOX, ty_d - DBG_BOX),
                                  (tx_d + DBG_BOX, ty_d + DBG_BOX), tap_col, tap_thick)
                    cv2.rectangle(disp,
                                  (tx_d - sh_d, ty_d - sh_d),
                                  (tx_d + sh_d, ty_d + sh_d), tap_col, 1)
                    cv2.putText(disp, f'T{LANE_NAMES[i]}',
                                (tx_d - DBG_BOX, ty_d - DBG_BOX - 3),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.35, tap_col, 1, cv2.LINE_AA)
                    if s != 'idle':
                        cv2.putText(disp, s.upper(),
                                    (tx_d - DBG_BOX, ty_d + DBG_BOX + 11),
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.35, tap_col, 1, cv2.LINE_AA)

                    hx_abs, hy_abs = HOLD_PIXELS[i]
                    hx_d = int((hx_abs - dbg_mon['left']) * CAL_SCALE)
                    hy_d = int((hy_abs - dbg_mon['top'])  * CAL_SCALE)
                    hx_d = max(DBG_BOX, min(dw_ - DBG_BOX - 1, hx_d))
                    hy_d = max(DBG_BOX, min(dh_ - DBG_BOX - 1, hy_d))

                    if hold_gray_counts[i] >= MIN_PIXELS:
                        hld_col, hld_thick = (0, 200, 255), 2
                    else:
                        hld_col, hld_thick = col, 1

                    cv2.rectangle(disp,
                                  (hx_d - DBG_BOX, hy_d - DBG_BOX),
                                  (hx_d + DBG_BOX, hy_d + DBG_BOX), hld_col, hld_thick)
                    cv2.rectangle(disp,
                                  (hx_d - sh_d, hy_d - sh_d),
                                  (hx_d + sh_d, hy_d + sh_d), hld_col, 1)
                    cv2.putText(disp, f'H{LANE_NAMES[i]}',
                                (hx_d - DBG_BOX, hy_d - DBG_BOX - 3),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.35, hld_col, 1, cv2.LINE_AA)

                ph    = max(disp.shape[0], 280)
                panel = np.zeros((ph, DBG_PANW, 3), dtype=np.uint8)

                row = 18
                sc  = (0, 220, 0) if active else (80, 80, 220)
                put(panel, f"{'ACTIVE' if active else 'PAUSED'}    FPS: {fps}", row, sc); row += 16
                put(panel, "L=pause  P=debug off", row, (140, 140, 140));                row += 22
                put(panel, " Ln  State   W  TG  HG  Fl", row, (255, 220, 80));           row += 14

                for i in range(4):
                    s    = states[i]
                    sabb = {'idle': 'IDLE', 'tapped': 'TAP ', 'holding': 'HOLD'}[s]
                    sc2  = LANE_COLORS[i] if s != 'idle' else (160, 160, 160)
                    fl   = ('I' if hold_incoming[i] else '') + ('T' if hold_saw_tail[i] else '')
                    put(panel, f"  {LANE_NAMES[i]}   {sabb}  "
                        f"{white_counts[i]:2d}  {tap_gray_counts[i]:2d}  "
                        f"{hold_gray_counts[i]:2d}  {fl}",
                        row, sc2)
                    row += 14

                row += 10
                put(panel, "--- box colors ---", row, (140, 140, 140)); row += 14
                cv2.rectangle(panel, (6, row-10), (16, row+2), (255, 255, 255), -1)
                put(panel, "  note arriving",   row); row += 14
                cv2.rectangle(panel, (6, row-10), (16, row+2), (0, 255, 255), -1)
                put(panel, "  key down (tap)",  row); row += 14
                cv2.rectangle(panel, (6, row-10), (16, row+2), (0, 255, 0), -1)
                put(panel, "  key held (hold)", row); row += 14
                cv2.rectangle(panel, (6, row-10), (16, row+2), (0, 200, 255), -1)
                put(panel, "  hold gray found", row); row += 18
                put(panel, "I=incoming T=tail", row, (120, 120, 120))

                if panel.shape[0] < disp.shape[0]:
                    pad   = np.zeros((disp.shape[0] - panel.shape[0], DBG_PANW, 3), dtype=np.uint8)
                    panel = np.vstack([panel, pad])
                elif disp.shape[0] < panel.shape[0]:
                    pad  = np.zeros((panel.shape[0] - disp.shape[0], disp.shape[1], 3), dtype=np.uint8)
                    disp = np.vstack([disp, pad])

                cv2.imshow(DBG_WIN, np.hstack([panel, disp]))
                cv2.waitKey(1)


# ─── MENU ─────────────────────────────────────────────────────────────────────

def show_menu():
    choice    = [None]
    debug_var = [False]

    root = tk.Tk()
    root.title("Rhythm Macro")
    root.resizable(False, False)
    root.configure(bg='#1a1a1a')

    root.update_idletasks()
    w, h = 300, 220
    x    = (root.winfo_screenwidth()  - w) // 2
    y    = (root.winfo_screenheight() - h) // 2
    root.geometry(f"{w}x{h}+{x}+{y}")

    tk.Label(root, text="Rhythm Macro",
             bg='#1a1a1a', fg='#ffffff',
             font=('Segoe UI', 15, 'bold')).pack(pady=(20, 2))
    tk.Label(root, text="select a mode",
             bg='#1a1a1a', fg='#666666',
             font=('Segoe UI', 9)).pack(pady=(0, 14))

    BTN = dict(bg='#2a2a2a', fg='#eeeeee',
               activebackground='#383838', activeforeground='#ffffff',
               relief='flat', bd=0, font=('Segoe UI', 10),
               cursor='hand2', width=28, height=2)

    def pick(val):
        choice[0]    = val
        debug_var[0] = dbg_check.get()
        root.destroy()

    tk.Button(root, text="▶   Run Macro",
              command=lambda: pick('run'), **BTN).pack(pady=(0, 4))

    dbg_check = tk.BooleanVar(value=False)
    tk.Checkbutton(root, text="open debug window on start",
                   variable=dbg_check,
                   bg='#1a1a1a', fg='#888888',
                   activebackground='#1a1a1a', activeforeground='#aaaaaa',
                   selectcolor='#2a2a2a', relief='flat',
                   font=('Segoe UI', 9)).pack()

    tk.Frame(root, bg='#333333', height=1, width=260).pack(pady=10)

    tk.Button(root, text="⚙   Calibrate Detection Zones",
              command=lambda: pick('calibrate'), **BTN).pack()

    root.mainloop()
    return choice[0], debug_var[0]


# ─── ENTRY POINT ──────────────────────────────────────────────────────────────

if __name__ == '__main__':
    while True:
        mode, start_debug = show_menu()

        if mode is None:
            break

        elif mode == 'calibrate':
            run_calibration()

        elif mode == 'run':
            print(f"\nRunning — L=pause  P=toggle debug  Ctrl+C=quit\n")
            try:
                run_macro(start_debug=start_debug)
            except KeyboardInterrupt:
                for k in KEYS:
                    try: keyboard.release(k)
                    except Exception: pass
                print("Stopped.")
            cv2.destroyAllWindows()
            break
