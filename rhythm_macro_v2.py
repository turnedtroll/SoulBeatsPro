"""
rhythm_macro_v2.py — same logic as v1 + interactive lane calibration.

  python rhythm_macro_v2.py           run macro (saved coords or defaults)
  python rhythm_macro_v2.py --setup   calibrate lanes, then start macro
"""
import sys
import os
import json
import time
import mss
import numpy as np
import keyboard
import cv2

# ─── PIXEL COORDINATES ────────────────────────────────────────────────────────
SAMPLE_HALF = 3   # sampling box half-size: 3 → 7×7 px

_DEFAULT_TAP  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]
_DEFAULT_HOLD = [(720, 824), (878, 824), (1040, 824), (1197, 824)]

LANE_NAMES = ['Lane 1 (Z)', 'Lane 2 (X)', 'Lane 3 (,)', 'Lane 4 (.)']
KEYS = ['z', 'x', ',', '.']

# ─── COLOR THRESHOLDS ─────────────────────────────────────────────────────────
WHITE_MIN  = 240
GRAY_MIN   = 130
GRAY_MAX   = 170
MIN_PIXELS = 3

TAP_KEY_DURATION      = 0.03
HOLD_RELEASE_COOLDOWN = 0.06
TOGGLE_DELAY          = 0.3
# ──────────────────────────────────────────────────────────────────────────────


def _coords_path():
    base = os.path.dirname(sys.executable if getattr(sys, 'frozen', False)
                           else os.path.abspath(__file__))
    return os.path.join(base, 'coords.txt')


def _load_coords():
    path = _coords_path()
    if os.path.exists(path):
        try:
            with open(path) as f:
                d = json.load(f)
            print(f"Loaded coords from {path}")
            return [tuple(p) for p in d['tap']], [tuple(p) for p in d['hold']]
        except Exception as e:
            print(f"Could not read {path}: {e}")
    print("Using default coordinates.")
    return _DEFAULT_TAP, _DEFAULT_HOLD


def _save_coords(tap, hold):
    path = _coords_path()
    with open(path, 'w') as f:
        json.dump({'tap': [list(p) for p in tap],
                   'hold': [list(p) for p in hold]}, f, indent=2)
    print(f"Saved to {path}")


# ─── INTERACTIVE SETUP ────────────────────────────────────────────────────────

def setup_lanes():
    """
    Shows a fullscreen screenshot. Click the center of each lane's detection
    point in order — 4 TAP zones (green), then 4 HOLD zones (orange).
    Right-click undoes the last point. ENTER saves, ESC cancels.
    """
    print()
    print("Setup: switch to the game so it is visible on screen.")
    print("Screenshot in 3 seconds...")
    for i in (3, 2, 1):
        print(f"  {i}...")
        time.sleep(1)

    with mss.mss() as sct:
        mon = sct.monitors[1]          # primary monitor
        img = sct.grab(mon)

    img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
    img_bgr = cv2.cvtColor(img_np, cv2.COLOR_BGRA2BGR)

    sh = SAMPLE_HALF
    mon_left = mon['left']
    mon_top  = mon['top']

    # canvas[0] is the layer we draw markers on; use a list so the callback
    # can modify it in-place without needing nonlocal reassignment.
    canvas  = [img_bgr.copy()]
    clicks  = []   # (abs_x, abs_y) in screen coordinates

    def redraw_markers():
        """Rebuild canvas from scratch so undo is clean."""
        canvas[0] = img_bgr.copy()
        for idx, (ax, ay) in enumerate(clicks):
            rx, ry = ax - mon_left, ay - mon_top
            is_tap = idx < 4
            color  = (0, 220, 0) if is_tap else (0, 140, 255)
            cv2.rectangle(canvas[0], (rx - sh, ry - sh), (rx + sh, ry + sh), color, 2)
            cv2.circle(canvas[0], (rx, ry), 3, color, -1)
            label = ('T' if is_tap else 'H') + str(idx % 4 + 1)
            cv2.putText(canvas[0], label, (rx + sh + 3, ry + 5),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, color, 2)

    def mouse_cb(event, x, y, flags, param):
        if event == cv2.EVENT_LBUTTONDOWN and len(clicks) < 8:
            clicks.append((x + mon_left, y + mon_top))
            redraw_markers()
        elif event == cv2.EVENT_RBUTTONDOWN and clicks:
            clicks.pop()
            redraw_markers()

    win = "Setup — Rhythm Macro v2"
    cv2.namedWindow(win, cv2.WINDOW_NORMAL)
    cv2.setWindowProperty(win, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)
    cv2.setMouseCallback(win, mouse_cb)

    print()
    print("Instructions:")
    print("  LEFT-CLICK  — set detection point")
    print("  RIGHT-CLICK — undo last point")
    print("  ENTER       — save (after all 8 points)")
    print("  ESC         — cancel")
    print()
    print("  First 4 clicks  = TAP  zones  (green)  — Lane 1 to 4, left → right")
    print("  Next  4 clicks  = HOLD zones  (orange) — Lane 1 to 4, left → right")

    while True:
        display = canvas[0].copy()
        n = len(clicks)

        if n < 8:
            zone  = "TAP" if n < 4 else "HOLD"
            lane  = LANE_NAMES[n % 4]
            color_txt = (0, 220, 0) if n < 4 else (0, 140, 255)
            text  = f"{n+1}/8  Left-click center of {lane} {zone} zone"
        else:
            color_txt = (200, 200, 200)
            text  = "All 8 points set.  ENTER = save & start  |  Right-click = undo"

        # Text box background
        (tw, th), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 0.8, 2)
        cv2.rectangle(display, (8, 8), (tw + 18, th + 22), (0, 0, 0), -1)
        cv2.putText(display, text, (13, th + 14),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, color_txt, 2)

        cv2.imshow(win, display)
        key = cv2.waitKey(30) & 0xFF

        if key == 27:                    # ESC — cancel
            cv2.destroyAllWindows()
            print("Setup cancelled.")
            return False

        if key == 13 and n == 8:         # ENTER — save
            break

    cv2.destroyAllWindows()

    tap_pts  = clicks[:4]
    hold_pts = clicks[4:]

    print("\nCalibrated coordinates:")
    for i, (t, h) in enumerate(zip(tap_pts, hold_pts)):
        print(f"  {LANE_NAMES[i]}: tap={t}  hold={h}")

    _save_coords(tap_pts, hold_pts)
    return True


# ─── MACRO LOOP (identical logic to v1) ──────────────────────────────────────

def run(TAP_PIXELS, HOLD_PIXELS):
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

    states            = ['idle'] * 4
    hold_incoming     = [False]  * 4
    hold_saw_tail     = [False]  * 4
    tap_release_at    = [0.0]    * 4
    hold_released_at  = [0.0]    * 4

    active      = True
    last_toggle = 0

    with mss.mss() as sct:
        while True:
            now = time.time()

            # ── L: pause / resume ─────────────────────────────────────────────
            if keyboard.is_pressed('l') and now - last_toggle > TOGGLE_DELAY:
                active      = not active
                last_toggle = now
                if not active:
                    for i, s in enumerate(states):
                        if s == 'holding':
                            keyboard.release(KEYS[i])
                        if tap_release_at[i] > 0:
                            keyboard.release(KEYS[i])
                    states         = ['idle'] * 4
                    hold_incoming  = [False]  * 4
                    hold_saw_tail  = [False]  * 4
                    tap_release_at = [0.0]    * 4
                print("Paused" if not active else "Resumed")

            if not active:
                time.sleep(0.01)
                continue

            # ── Non-blocking tap releases ─────────────────────────────────────
            for i in range(4):
                if tap_release_at[i] > 0 and now >= tap_release_at[i]:
                    keyboard.release(KEYS[i])
                    tap_release_at[i] = 0.0

            # ── Single capture covering all 8 pixel positions ─────────────────
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            r = img_np[:, :, 2]
            g = img_np[:, :, 1]
            b = img_np[:, :, 0]

            for i in range(4):
                # ── Tap zone sample ───────────────────────────────────────────
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

                # ── Hold zone sample ──────────────────────────────────────────
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

                key   = KEYS[i]
                state = states[i]

                # ── Hold zone: arm flag ────────────────────────────────────────
                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True

                # ── HOLDING: watch tap zone for gray tail ─────────────────────
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

                # ── IDLE: decide tap or hold on white ─────────────────────────
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

                # ── TAPPED: reset once tap zone clears ────────────────────────
                elif state == 'tapped':
                    if white_count < MIN_PIXELS and tap_release_at[i] == 0.0:
                        states[i]        = 'idle'
                        hold_incoming[i] = False


# ─── ENTRY POINT ─────────────────────────────────────────────────────────────

if __name__ == '__main__':
    if '--setup' in sys.argv:
        if not setup_lanes():
            sys.exit(0)
        print("\nCalibration done. Starting macro in 2 seconds...")
        time.sleep(2)

    tap, hold = _load_coords()
    print("L = pause/resume  |  Ctrl+C = quit")
    try:
        run(tap, hold)
    except KeyboardInterrupt:
        for k in KEYS:
            try:
                keyboard.release(k)
            except Exception:
                pass
        print("Stopped.")
    except Exception as e:
        print(f"Error: {e}")
        time.sleep(0.5)
