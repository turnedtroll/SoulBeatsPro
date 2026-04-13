import sys
import os
import json
import time
import mss
import numpy as np
import keyboard
import cv2

# ─── PIXEL COORDINATES ────────────────────────────────────────────────────────
# Sampling area: SAMPLE_HALF controls size — 2 = 5x5, 3 = 7x7
SAMPLE_HALF = 3

_DEFAULT_TAP  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]  # z x , .
_DEFAULT_HOLD = [(720, 824), (878, 824), (1040, 824), (1197, 824)]

def _load_coords():
    base = os.path.dirname(sys.executable if getattr(sys, 'frozen', False)
                           else os.path.abspath(__file__))
    path = os.path.join(base, 'coords.txt')
    if os.path.exists(path):
        try:
            with open(path) as f:
                d = json.load(f)
            print(f"Loaded coords from {path}")
            return [tuple(p) for p in d['tap']], [tuple(p) for p in d['hold']]
        except Exception as e:
            print(f"Could not read {path}: {e}")
    return _DEFAULT_TAP, _DEFAULT_HOLD

TAP_PIXELS, HOLD_PIXELS = _load_coords()

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

LANE_NAMES  = ['Z', 'X', ',', '.']
LANE_COLORS = [(255, 80, 80), (80, 255, 80), (80, 80, 255), (255, 200, 0)]
ZOOM        = 3
PANEL_W     = 235
WIN_NAME    = "Rhythm Macro — Debug"


def run():
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
    last_toggle = 0

    cv2.namedWindow(WIN_NAME, cv2.WINDOW_NORMAL)

    frame_count = 0
    fps         = 0
    fps_timer   = time.time()

    with mss.mss() as sct:
        while True:
            now = time.time()

            # FPS counter
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
                        if s == 'holding':
                            keyboard.release(KEYS[i])
                        if tap_release_at[i] > 0:
                            keyboard.release(KEYS[i])
                    states         = ['idle'] * 4
                    hold_incoming  = [False]  * 4
                    hold_saw_tail  = [False]  * 4
                    tap_release_at = [0.0]    * 4
                print("Paused" if not active else "Resumed")

            # ── Non-blocking tap releases ─────────────────────────────────────
            for i in range(4):
                if tap_release_at[i] > 0 and now >= tap_release_at[i]:
                    keyboard.release(KEYS[i])
                    tap_release_at[i] = 0.0

            # ── Capture ───────────────────────────────────────────────────────
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            r = img_np[:, :, 2]
            g = img_np[:, :, 1]
            b = img_np[:, :, 0]

            # Per-lane counts (always computed for debug display)
            white_counts     = [0]     * 4
            tap_gray_counts  = [0]     * 4
            hold_gray_counts = [0]     * 4
            hold_has_whites  = [False] * 4

            for i in range(4):
                tx, ty = tap_rel[i]
                tr = r[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                tg = g[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                tb = b[ty-sh:ty+sh+1, tx-sh:tx+sh+1]

                white_counts[i]    = int(np.sum(
                    (tr >= WHITE_MIN) & (tg >= WHITE_MIN) & (tb >= WHITE_MIN)
                ))
                tap_gray_counts[i] = int(np.sum(
                    (tr >= GRAY_MIN) & (tr <= GRAY_MAX) &
                    (tg >= GRAY_MIN) & (tg <= GRAY_MAX) &
                    (tb >= GRAY_MIN) & (tb <= GRAY_MAX)
                ))

                hx, hy = hold_rel[i]
                hr = r[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                hg = g[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                hb = b[hy-sh:hy+sh+1, hx-sh:hx+sh+1]

                hold_gray_counts[i] = int(np.sum(
                    (hr >= GRAY_MIN) & (hr <= GRAY_MAX) &
                    (hg >= GRAY_MIN) & (hg <= GRAY_MAX) &
                    (hb >= GRAY_MIN) & (hb <= GRAY_MAX)
                ))
                hold_has_whites[i] = bool(np.any(
                    (hr >= WHITE_MIN) & (hg >= WHITE_MIN) & (hb >= WHITE_MIN)
                ))

                if not active:
                    continue

                white_count     = white_counts[i]
                tap_gray_count  = tap_gray_counts[i]
                hold_gray_count = hold_gray_counts[i]
                hold_has_white  = hold_has_whites[i]

                key   = KEYS[i]
                state = states[i]

                # ── Hold zone: arm flag ───────────────────────────────────────
                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True

                # ── HOLDING ───────────────────────────────────────────────────
                if state == 'holding':
                    if tap_gray_count >= MIN_PIXELS:
                        hold_saw_tail[i] = True
                    elif hold_saw_tail[i]:
                        # tail passed through tap zone — release
                        keyboard.release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now
                        # intentionally NOT updating local `state` here
                        # so the IDLE block is skipped this frame
                    elif white_count == 0 and tap_gray_count == 0 and hold_gray_count == 0:
                        # fallback: screen fully clear, tail was missed — force release
                        keyboard.release(key)
                        states[i]           = 'idle'
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now
                    # else: tail not yet arrived, keep holding

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

            # ── Build visual overlay ──────────────────────────────────────────
            vis = img_np[:, :, :3].copy()  # BGR

            for i in range(4):
                # Tap zone — highlight detected pixels
                tx, ty = tap_rel[i]
                patch_r = r[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                patch_g = g[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                patch_b = b[ty-sh:ty+sh+1, tx-sh:tx+sh+1]

                white_m = (patch_r >= WHITE_MIN) & (patch_g >= WHITE_MIN) & (patch_b >= WHITE_MIN)
                gray_m  = ((patch_r >= GRAY_MIN) & (patch_r <= GRAY_MAX) &
                           (patch_g >= GRAY_MIN) & (patch_g <= GRAY_MAX) &
                           (patch_b >= GRAY_MIN) & (patch_b <= GRAY_MAX))

                tap_patch = vis[ty-sh:ty+sh+1, tx-sh:tx+sh+1]
                tap_patch[white_m] = (255, 255, 255)
                tap_patch[gray_m]  = (0, 220, 220)

                # Hold zone — highlight detected pixels
                hx, hy = hold_rel[i]
                h_r = r[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                h_g = g[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                h_b = b[hy-sh:hy+sh+1, hx-sh:hx+sh+1]

                hgray_m  = ((h_r >= GRAY_MIN) & (h_r <= GRAY_MAX) &
                            (h_g >= GRAY_MIN) & (h_g <= GRAY_MAX) &
                            (h_b >= GRAY_MIN) & (h_b <= GRAY_MAX))
                hwhite_m = (h_r >= WHITE_MIN) & (h_g >= WHITE_MIN) & (h_b >= WHITE_MIN)

                hold_patch = vis[hy-sh:hy+sh+1, hx-sh:hx+sh+1]
                hold_patch[hgray_m]  = (0, 160, 255)
                hold_patch[hwhite_m] = (200, 255, 200)

            # Zoom the captured region so it's actually visible
            zoomed = cv2.resize(vis,
                                (vis.shape[1] * ZOOM, vis.shape[0] * ZOOM),
                                interpolation=cv2.INTER_NEAREST)

            # Draw sample zone boxes + labels on zoomed image
            for i in range(4):
                col = LANE_COLORS[i]

                tx, ty = tap_rel[i]
                x1, y1 = (tx - sh) * ZOOM, (ty - sh) * ZOOM
                x2, y2 = (tx + sh + 1) * ZOOM - 1, (ty + sh + 1) * ZOOM - 1
                cv2.rectangle(zoomed, (x1, y1), (x2, y2), col, 1)
                cv2.putText(zoomed, 'T', (x1, max(y1 - 2, 8)),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.3, col, 1, cv2.LINE_AA)

                hx, hy = hold_rel[i]
                x1, y1 = (hx - sh) * ZOOM, (hy - sh) * ZOOM
                x2, y2 = (hx + sh + 1) * ZOOM - 1, (hy + sh + 1) * ZOOM - 1
                cv2.rectangle(zoomed, (x1, y1), (x2, y2), col, 1)
                cv2.putText(zoomed, 'H', (x1, max(y1 - 2, 8)),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.3, col, 1, cv2.LINE_AA)

            # ── Left panel ────────────────────────────────────────────────────
            panel_h = max(zoomed.shape[0], 310)
            panel   = np.zeros((panel_h, PANEL_W, 3), dtype=np.uint8)

            def put(text, row, color=(220, 220, 220)):
                cv2.putText(panel, text, (6, row), cv2.FONT_HERSHEY_SIMPLEX,
                            0.38, color, 1, cv2.LINE_AA)

            row = 18
            st_col = (0, 220, 0) if active else (80, 80, 220)
            put(f"{'ACTIVE' if active else 'PAUSED'}    FPS: {fps}", row, st_col)
            row += 16
            put("L = pause / resume", row, (140, 140, 140))
            row += 22

            put(" Ln  State   W  TG  HG  Fl", row, (255, 220, 80))
            row += 14

            for i in range(4):
                s     = states[i]
                sabb  = {'idle': 'IDLE', 'tapped': 'TAP ', 'holding': 'HOLD'}[s]
                scol  = LANE_COLORS[i] if s != 'idle' else (160, 160, 160)
                flags = ''
                if hold_incoming[i]: flags += 'I'
                if hold_saw_tail[i]: flags += 'T'
                line = (f"  {LANE_NAMES[i]}   {sabb}  "
                        f"{white_counts[i]:2d}  {tap_gray_counts[i]:2d}  "
                        f"{hold_gray_counts[i]:2d}  {flags}")
                put(line, row, scol)
                row += 14

            row += 10
            put("--- overlays ---", row, (140, 140, 140))
            row += 14

            cv2.rectangle(panel, (6, row-10), (16, row+2), (255, 255, 255), -1)
            put("  tap white  (hit)", row)
            row += 14
            cv2.rectangle(panel, (6, row-10), (16, row+2), (0, 220, 220), -1)
            put("  tap gray   (tail)", row)
            row += 14
            cv2.rectangle(panel, (6, row-10), (16, row+2), (0, 160, 255), -1)
            put("  hold gray  (arm)", row)
            row += 18

            put("Flags:", row, (120, 120, 120)); row += 13
            put("  I = hold incoming", row, (120, 120, 120)); row += 13
            put("  T = saw tail", row, (120, 120, 120)); row += 13
            put("Cols: W  TG  HG", row, (120, 120, 120)); row += 13
            put("  W =white count", row, (120, 120, 120)); row += 13
            put("  TG=tap gray ct", row, (120, 120, 120)); row += 13
            put("  HG=hold gray ct", row, (120, 120, 120))

            # Pad heights so hstack works
            if panel.shape[0] < zoomed.shape[0]:
                pad    = np.zeros((zoomed.shape[0] - panel.shape[0], PANEL_W, 3), dtype=np.uint8)
                panel  = np.vstack([panel, pad])
            elif zoomed.shape[0] < panel.shape[0]:
                pad    = np.zeros((panel.shape[0] - zoomed.shape[0], zoomed.shape[1], 3), dtype=np.uint8)
                zoomed = np.vstack([zoomed, pad])

            cv2.imshow(WIN_NAME, np.hstack([panel, zoomed]))
            cv2.waitKey(1)

            if not active:
                time.sleep(0.01)


if __name__ == '__main__':
    print("Rhythm Macro Debug — L to pause/resume, Ctrl+C to quit")
    try:
        run()
    except KeyboardInterrupt:
        cv2.destroyAllWindows()
        for key in KEYS:
            try:
                keyboard.release(key)
            except Exception:
                pass
        print("Stopped")
    except Exception as e:
        cv2.destroyAllWindows()
        print(f"Error: {e}")
        time.sleep(0.5)
