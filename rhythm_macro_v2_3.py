import sys
import os
import json
import time
import random
import mss
import numpy as np
import keyboard

# ─── PIXEL COORDINATES ────────────────────────────────────────────────────────
# Sampling area: SAMPLE_HALF controls size — 2 = 5x5, 3 = 7x7
SAMPLE_HALF = 3

_DEFAULT_TAP  = [(720, 950), (881, 950), (1039, 950), (1200, 950)]  # z x , .
_DEFAULT_HOLD = [(720, 824), (878, 824), (1040, 824), (1197, 824)]


def _load_coords():
    base = os.path.dirname(
        sys.executable if getattr(sys, "frozen", False) else os.path.abspath(__file__)
    )
    path = os.path.join(base, "coords.txt")
    if os.path.exists(path):
        try:
            with open(path) as f:
                d = json.load(f)
            print(f"Loaded coords from {path}")
            return [tuple(p) for p in d["tap"]], [tuple(p) for p in d["hold"]]
        except Exception as e:
            print(f"Could not read {path}: {e}")
    return _DEFAULT_TAP, _DEFAULT_HOLD


TAP_PIXELS, HOLD_PIXELS = _load_coords()

KEYS = ["z", "x", ",", "."]

# ─── COLOR THRESHOLDS ─────────────────────────────────────────────────────────
WHITE_MIN  = 240
GRAY_MIN   = 130
GRAY_MAX   = 170
MIN_PIXELS = 3

TAP_KEY_DURATION      = 0.03  # seconds to hold a tap key down (non-blocking)
HOLD_RELEASE_COOLDOWN = 0.06  # seconds to ignore detections after a hold releases
TOGGLE_DELAY          = 0.3
# ──────────────────────────────────────────────────────────────────────────────


# ─── TIMING VARIATION (v2.3) ──────────────────────────────────────────────────
# Variation is applied ONLY to tap notes (not holds).
# It adds a small random sleep BEFORE pressing the tap key.
JITTER_ENABLED        = True    # turn variation on/off globally
MIN_TAP_JITTER_MS     = 0.0     # minimum extra delay (ms) before tap press
MAX_TAP_JITTER_MS     = 70.0    # maximum extra delay (ms) before tap press


def _tap_jitter_sleep():
    """Optional blocking sleep used only for tap presses."""
    if not JITTER_ENABLED or MAX_TAP_JITTER_MS <= MIN_TAP_JITTER_MS:
        return
    delay_s = random.uniform(MIN_TAP_JITTER_MS, MAX_TAP_JITTER_MS) / 1000.0
    if delay_s > 0:
        time.sleep(delay_s)


def run():
    all_pixels = TAP_PIXELS + HOLD_PIXELS
    xs = [p[0] for p in all_pixels]
    ys = [p[1] for p in all_pixels]

    cap_left   = min(xs) - SAMPLE_HALF - 1
    cap_top    = min(ys) - SAMPLE_HALF - 1
    cap_right  = max(xs) + SAMPLE_HALF + 1
    cap_bottom = max(ys) + SAMPLE_HALF + 1

    monitor = {
        "left":   cap_left,
        "top":    cap_top,
        "width":  cap_right  - cap_left,
        "height": cap_bottom - cap_top,
    }

    def rel(px, py):
        return px - cap_left, py - cap_top

    tap_rel  = [rel(x, y) for x, y in TAP_PIXELS]
    hold_rel = [rel(x, y) for x, y in HOLD_PIXELS]
    sh = SAMPLE_HALF

    # Per-lane state
    states            = ["idle"] * 4  # "idle" | "tapped" | "holding"
    hold_incoming     = [False] * 4   # gray tail seen at hold zone
    hold_saw_tail     = [False] * 4   # gray tail reached tap zone during hold
    tap_release_at    = [0.0]   * 4   # timestamp to release tap key (0 = none pending)
    hold_released_at  = [0.0]   * 4   # timestamp when hold last released

    active      = True
    last_toggle = 0

    with mss.mss() as sct:
        while True:
            now = time.time()

            # ── L: pause / resume ─────────────────────────────────────────────
            if keyboard.is_pressed("l") and now - last_toggle > TOGGLE_DELAY:
                active      = not active
                last_toggle = now
                if not active:
                    for i, s in enumerate(states):
                        if s == "holding":
                            keyboard.release(KEYS[i])
                        if tap_release_at[i] > 0:
                            keyboard.release(KEYS[i])
                    states         = ["idle"] * 4
                    hold_incoming  = [False] * 4
                    hold_saw_tail  = [False] * 4
                    tap_release_at = [0.0]  * 4
                print("Paused" if not active else "Resumed")

            if not active:
                time.sleep(0.01)
                continue

            # ── Non-blocking tap releases ─────────────────────────────────────
            # Check before capturing so releases happen as soon as timer expires
            for i in range(4):
                if tap_release_at[i] > 0 and now >= tap_release_at[i]:
                    keyboard.release(KEYS[i])
                    tap_release_at[i] = 0.0

            # ── Single capture covering all 8 pixel positions ─────────────────
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(
                img.height, img.width, 4
            )
            r = img_np[:, :, 2]
            g = img_np[:, :, 1]
            b = img_np[:, :, 0]

            for i in range(4):
                # ── Tap zone sample ───────────────────────────────────────────
                tx, ty = tap_rel[i]
                tr = r[ty - sh : ty + sh + 1, tx - sh : tx + sh + 1]
                tg = g[ty - sh : ty + sh + 1, tx - sh : tx + sh + 1]
                tb = b[ty - sh : ty + sh + 1, tx - sh : tx + sh + 1]

                white_count = int(
                    np.sum((tr >= WHITE_MIN) & (tg >= WHITE_MIN) & (tb >= WHITE_MIN))
                )
                tap_gray_count = int(
                    np.sum(
                        (tr >= GRAY_MIN)
                        & (tr <= GRAY_MAX)
                        & (tg >= GRAY_MIN)
                        & (tg <= GRAY_MAX)
                        & (tb >= GRAY_MIN)
                        & (tb <= GRAY_MAX)
                    )
                )

                # ── Hold zone sample ──────────────────────────────────────────
                hx, hy = hold_rel[i]
                hr = r[hy - sh : hy + sh + 1, hx - sh : hx + sh + 1]
                hg = g[hy - sh : hy + sh + 1, hx - sh : hx + sh + 1]
                hb = b[hy - sh : hy + sh + 1, hx - sh : hx + sh + 1]

                hold_gray_count = int(
                    np.sum(
                        (hr >= GRAY_MIN)
                        & (hr <= GRAY_MAX)
                        & (hg >= GRAY_MIN)
                        & (hg <= GRAY_MAX)
                        & (hb >= GRAY_MIN)
                        & (hb <= GRAY_MAX)
                    )
                )
                hold_has_white = bool(
                    np.any(
                        (hr >= WHITE_MIN)
                        & (hg >= WHITE_MIN)
                        & (hb >= WHITE_MIN)
                    )
                )

                key   = KEYS[i]
                state = states[i]

                # ── Hold zone: arm flag (no key press) ────────────────────────
                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True

                # ── HOLDING: watch tap zone for gray tail ─────────────────────
                if state == "holding":
                    if tap_gray_count >= MIN_PIXELS:
                        hold_saw_tail[i] = True  # tail is passing through
                    elif hold_saw_tail[i]:
                        # tail has passed — release
                        keyboard.release(key)
                        states[i]           = "idle"
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now
                        # intentionally NOT updating local `state` here
                        # so the IDLE block is skipped this frame
                    elif (
                        white_count == 0
                        and tap_gray_count == 0
                        and hold_gray_count == 0
                    ):
                        # fallback: screen fully clear, tail was missed — force release
                        keyboard.release(key)
                        states[i]           = "idle"
                        hold_saw_tail[i]    = False
                        hold_incoming[i]    = False
                        hold_released_at[i] = now
                    # else: tail not yet arrived, keep holding

                # ── IDLE: decide tap or hold on white ─────────────────────────
                # Cooldown after hold release prevents spurious taps from
                # lingering white pixels of the just-finished hold note
                if (
                    state == "idle"
                    and now - hold_released_at[i] >= HOLD_RELEASE_COOLDOWN
                ):
                    if white_count >= MIN_PIXELS:
                        if hold_incoming[i]:
                            # HOLD: press immediately (no jitter, to avoid misses)
                            keyboard.press(key)
                            states[i]        = "holding"
                            hold_incoming[i] = False
                        else:
                            # TAP: add small random delay BEFORE pressing the key
                            _tap_jitter_sleep()
                            press_time = time.time()
                            keyboard.press(key)
                            tap_release_at[i] = press_time + TAP_KEY_DURATION
                            states[i]         = "tapped"

                # ── TAPPED: reset once tap zone clears and release is done ─────
                elif state == "tapped":
                    if white_count < MIN_PIXELS and tap_release_at[i] == 0.0:
                        states[i]        = "idle"
                        hold_incoming[i] = False


if __name__ == "__main__":
    print("cool lil thing i made v2.3 - L to pause/resume, Ctrl+C to quit")
    try:
        run()
    except KeyboardInterrupt:
        for key in KEYS:
            try:
                keyboard.release(key)
            except Exception:
                pass
        print("Stopped")
    except Exception as e:
        print(f"Error: {e}")
        time.sleep(0.5)

