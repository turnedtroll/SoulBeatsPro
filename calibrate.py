import sys
import os
import json
import mss
import numpy as np
import cv2

# ─── paths ────────────────────────────────────────────────────────────────────
if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))

COORDS_FILE = os.path.join(BASE_DIR, 'coords.txt')

# ─── defaults (kept as fallback if no coords.txt exists) ──────────────────────
DEFAULT_TAP  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]
DEFAULT_HOLD = [(720, 824), (878, 824), (1040, 824), (1197, 824)]
SAMPLE_HALF  = 3

# ─── visuals ──────────────────────────────────────────────────────────────────
LANE_COLORS = [
    (80,  80,  255),   # Z  - red
    (80,  255, 80),    # X  - green
    (0,   180, 255),   # ,  - orange
    (200, 80,  255),   # .  - purple
]
LANE_NAMES = ['Z', 'X', ',', '.']

SCALE     = 0.5   # screen → display scale
GRAB_R    = 18    # grab radius in display pixels
HANDLE_H  = 13    # half-size of drawn handle box in display pixels


def load_coords():
    if os.path.exists(COORDS_FILE):
        try:
            with open(COORDS_FILE) as f:
                d = json.load(f)
            print(f"Loaded existing coords from {COORDS_FILE}")
            return [tuple(p) for p in d['tap']], [tuple(p) for p in d['hold']]
        except Exception as e:
            print(f"Could not read {COORDS_FILE}: {e}")
    print("No coords.txt found — showing built-in defaults")
    return list(DEFAULT_TAP), list(DEFAULT_HOLD)


def save_coords(tap, hold):
    with open(COORDS_FILE, 'w') as f:
        json.dump({'tap':  [list(p) for p in tap],
                   'hold': [list(p) for p in hold]}, f, indent=2)
    print(f"Saved to {COORDS_FILE}")


def s2d(sx, sy):
    """Screen → display coords"""
    return int(round(sx * SCALE)), int(round(sy * SCALE))


def d2s(dx, dy):
    """Display → screen coords"""
    return int(round(dx / SCALE)), int(round(dy / SCALE))


def main():
    tap_pts, hold_pts = load_coords()

    WIN = "Rhythm Macro — Calibration"
    cv2.namedWindow(WIN, cv2.WINDOW_AUTOSIZE)

    dragging = None   # ('tap'|'hold', index) or None
    hover    = None   # same

    def mouse_cb(event, mx, my, flags, _):
        nonlocal dragging, hover

        def nearest():
            best_d, best = GRAB_R ** 2, None
            for i in range(4):
                dx, dy = s2d(*tap_pts[i])
                d = (mx - dx) ** 2 + (my - dy) ** 2
                if d <= best_d:
                    best_d, best = d, ('tap', i)
            for i in range(4):
                dx, dy = s2d(*hold_pts[i])
                d = (mx - dx) ** 2 + (my - dy) ** 2
                if d <= best_d:
                    best_d, best = d, ('hold', i)
            return best

        if event == cv2.EVENT_LBUTTONDOWN:
            dragging = nearest()
        elif event == cv2.EVENT_MOUSEMOVE:
            hover = nearest()
            if dragging:
                sx, sy = d2s(mx, my)
                t, i   = dragging
                if t == 'tap':
                    tap_pts[i]  = (max(0, sx), max(0, sy))
                else:
                    hold_pts[i] = (max(0, sx), max(0, sy))
        elif event == cv2.EVENT_LBUTTONUP:
            dragging = None

    cv2.setMouseCallback(WIN, mouse_cb)

    print()
    print("  1. Open your game so the lane receptors are visible on screen.")
    print("  2. Drag the T boxes onto the tap receptors (where notes land).")
    print("  3. Drag the H boxes just above the receptors (where hold tails appear).")
    print("  T = thick border  |  H = thin border")
    print("  S = save and quit   ESC = quit without saving")
    print()

    with mss.mss() as sct:
        mon = sct.monitors[1]   # primary monitor

        while True:
            img    = sct.grab(mon)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            frame  = img_np[:, :, :3].copy()   # BGR

            dw = int(frame.shape[1] * SCALE)
            dh = int(frame.shape[0] * SCALE)
            disp = cv2.resize(frame, (dw, dh), interpolation=cv2.INTER_LINEAR)

            # Darken background so handles stand out
            disp = (disp.astype(np.float32) * 0.65).astype(np.uint8)

            for i in range(4):
                col  = LANE_COLORS[i]
                name = LANE_NAMES[i]

                # ── Tap handle (thick border) ──────────────────────────────────
                tx, ty = s2d(*tap_pts[i])
                tx = max(HANDLE_H, min(dw - HANDLE_H - 1, tx))
                ty = max(HANDLE_H, min(dh - HANDLE_H - 1, ty))

                hot = (dragging == ('tap', i) or
                       (dragging is None and hover == ('tap', i)))

                if hot:
                    y1 = max(0, ty - HANDLE_H); y2 = min(dh, ty + HANDLE_H + 1)
                    x1 = max(0, tx - HANDLE_H); x2 = min(dw, tx + HANDLE_H + 1)
                    roi = disp[y1:y2, x1:x2].astype(np.float32)
                    fill = np.full_like(roi, [col[0], col[1], col[2]], dtype=np.float32)
                    disp[y1:y2, x1:x2] = (roi * 0.55 + fill * 0.45).astype(np.uint8)

                cv2.rectangle(disp,
                              (tx - HANDLE_H, ty - HANDLE_H),
                              (tx + HANDLE_H, ty + HANDLE_H),
                              col, 3 if hot else 2)
                cv2.drawMarker(disp, (tx, ty), col,
                               cv2.MARKER_CROSS, 10, 1, cv2.LINE_AA)
                # Tiny inner box = actual 7×7 sample area
                sh = max(1, int(SAMPLE_HALF * SCALE))
                cv2.rectangle(disp, (tx - sh, ty - sh), (tx + sh, ty + sh),
                              (255, 255, 255), 1)
                cv2.putText(disp, f'T{name}',
                            (tx - HANDLE_H, ty - HANDLE_H - 4),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.4, col, 1, cv2.LINE_AA)
                if hot:
                    sx_r, sy_r = tap_pts[i]
                    cv2.putText(disp, f'({sx_r}, {sy_r})',
                                (tx + HANDLE_H + 3, ty + 5),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.35, col, 1, cv2.LINE_AA)

                # ── Hold handle (thin border) ──────────────────────────────────
                hx, hy = s2d(*hold_pts[i])
                hx = max(HANDLE_H, min(dw - HANDLE_H - 1, hx))
                hy = max(HANDLE_H, min(dh - HANDLE_H - 1, hy))

                hot = (dragging == ('hold', i) or
                       (dragging is None and hover == ('hold', i)))

                if hot:
                    y1 = max(0, hy - HANDLE_H); y2 = min(dh, hy + HANDLE_H + 1)
                    x1 = max(0, hx - HANDLE_H); x2 = min(dw, hx + HANDLE_H + 1)
                    roi = disp[y1:y2, x1:x2].astype(np.float32)
                    fill = np.full_like(roi, [col[0], col[1], col[2]], dtype=np.float32)
                    disp[y1:y2, x1:x2] = (roi * 0.55 + fill * 0.45).astype(np.uint8)

                cv2.rectangle(disp,
                              (hx - HANDLE_H, hy - HANDLE_H),
                              (hx + HANDLE_H, hy + HANDLE_H),
                              col, 2 if hot else 1)
                cv2.drawMarker(disp, (hx, hy), col,
                               cv2.MARKER_CROSS, 10, 1, cv2.LINE_AA)
                sh = max(1, int(SAMPLE_HALF * SCALE))
                cv2.rectangle(disp, (hx - sh, hy - sh), (hx + sh, hy + sh),
                              (200, 200, 200), 1)
                cv2.putText(disp, f'H{name}',
                            (hx - HANDLE_H, hy - HANDLE_H - 4),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.4, col, 1, cv2.LINE_AA)
                if hot:
                    sx_r, sy_r = hold_pts[i]
                    cv2.putText(disp, f'({sx_r}, {sy_r})',
                                (hx + HANDLE_H + 3, hy + 5),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.35, col, 1, cv2.LINE_AA)

            # ── Top instruction bar ────────────────────────────────────────────
            disp[:32] = (disp[:32].astype(np.float32) * 0.25).astype(np.uint8)

            if dragging:
                t, i   = dragging
                pts    = tap_pts if t == 'tap' else hold_pts
                sx, sy = pts[i]
                msg    = (f"Dragging  {t.upper()}  Lane {LANE_NAMES[i]}"
                          f"  ->  screen ({sx}, {sy})"
                          f"   |   S = Save & Quit   ESC = Cancel")
                msg_col = LANE_COLORS[i]
            else:
                msg     = ("Drag T / H boxes to align with game  "
                           "|  T=thick=tap  H=thin=hold  "
                           "|  S = Save & Quit   ESC = Cancel")
                msg_col = (0, 220, 255)

            cv2.putText(disp, msg, (8, 22),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, msg_col, 1, cv2.LINE_AA)

            cv2.imshow(WIN, disp)

            key = cv2.waitKey(16) & 0xFF
            if key in (ord('s'), ord('S')):
                save_coords(tap_pts, hold_pts)
                print("Done! You can close this window.")
                cv2.waitKey(0)
                break
            elif key == 27:  # ESC
                print("Exited without saving.")
                break

    cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
