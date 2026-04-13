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

TAP_KEY_DURATION     = 0.03   # seconds to hold a tap key down (non-blocking)
HOLD_RELEASE_COOLDOWN = 0.06  # seconds to ignore detections after a hold releases
TOGGLE_DELAY          = 0.3
# ──────────────────────────────────────────────────────────────────────────────


JITTER_ENABLED        = True   # set to False to turn off timing jitter
MIN_PRESS_JITTER_MS   = -40.0  # minimum jitter in ms  (negative = try to be earlier)
MAX_PRESS_JITTER_MS   = 110.0  # maximum jitter in ms  (positive = later)
RAPID_NOTE_WINDOW_MS  = 15.0   # if two notes in this lane start within this window,
                               # jitter is disabled for the later note


def _press_time_with_jitter(now: float, allow_jitter: bool = True) -> float:
    """
    Return the time at which a key should be pressed.
    Adds a random offset between MIN_PRESS_JITTER_MS and MAX_PRESS_JITTER_MS
    when jitter is enabled *and allowed*, without blocking the main loop.

    Note: we cannot actually press "in the past", so any negative jitter
    just results in pressing as soon as we detect the note.
    """
    if (not allow_jitter) or (not JITTER_ENABLED) or (MAX_PRESS_JITTER_MS <= MIN_PRESS_JITTER_MS):
        return now
    offset_ms = random.uniform(MIN_PRESS_JITTER_MS, MAX_PRESS_JITTER_MS)
    if offset_ms < 0.0:
        offset_ms = 0.0
    return now + offset_ms / 1000.0


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
    # states: 'idle' | 'tap_pending' | 'tapped' | 'hold_pending' | 'holding'
    states            = ['idle']  * 4
    hold_incoming     = [False]   * 4  # gray tail seen at hold zone
    hold_saw_tail     = [False]   * 4  # gray tail reached tap zone during hold
    tap_press_at      = [0.0]     * 4  # scheduled time to press tap key
    hold_press_at     = [0.0]     * 4  # scheduled time to press hold key
    tap_release_at    = [0.0]     * 4  # timestamp to release tap key (0 = none pending)
    hold_released_at  = [0.0]     * 4  # timestamp when hold last released
    last_note_time    = [0.0]     * 4  # last time a new note was seen in this lane

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
                    states         = ['idle']  * 4
                    hold_incoming  = [False]   * 4
                    hold_saw_tail  = [False]   * 4
                    tap_release_at = [0.0]     * 4
                print("Paused" if not active else "Resumed")

            if not active:
                time.sleep(0.01)
                continue

            # ── Non-blocking scheduled presses / releases ─────────────────────
            # Handle scheduled key presses and releases based on timers
            for i in range(4):
                # Presses for taps
                if states[i] == 'tap_pending' and tap_press_at[i] > 0 and now >= tap_press_at[i]:
                    keyboard.press(KEYS[i])
                    tap_release_at[i] = now + TAP_KEY_DURATION
                    states[i]         = 'tapped'
                    tap_press_at[i]   = 0.0

                # Presses for holds
                if states[i] == 'hold_pending' and hold_press_at[i] > 0 and now >= hold_press_at[i]:
                    keyboard.press(KEYS[i])
                    states[i]       = 'holding'
                    hold_press_at[i] = 0.0

                # Releases for taps
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

                # ── Dense sections override jitter ─────────────────────────────
                # If we still see a note in the tap zone while a jittered press
                # is pending, fire immediately so fast streams don't get skipped.
                if white_count >= MIN_PIXELS and state == 'tap_pending':
                    keyboard.press(key)
                    tap_release_at[i] = now + TAP_KEY_DURATION
                    states[i]         = 'tapped'
                    tap_press_at[i]   = 0.0
                    state             = states[i]
                elif white_count >= MIN_PIXELS and state == 'hold_pending':
                    keyboard.press(key)
                    states[i]        = 'holding'
                    hold_press_at[i] = 0.0
                    state            = states[i]

                # ── Hold zone: arm flag (no key press) ────────────────────────
                if hold_gray_count >= MIN_PIXELS and not hold_has_white:
                    hold_incoming[i] = True

                # ── HOLDING: watch tap zone for gray tail ─────────────────────
                if state == 'holding':
                    if tap_gray_count >= MIN_PIXELS:
                        hold_saw_tail[i] = True    # tail is passing through
                    elif hold_saw_tail[i]:
                        # tail has passed — release
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

                # ── IDLE: decide tap or hold on white ─────────────────────────
                # Cooldown after hold release prevents spurious taps from
                # lingering white pixels of the just-finished hold note
                if state == 'idle' and now - hold_released_at[i] >= HOLD_RELEASE_COOLDOWN:
                    if white_count >= MIN_PIXELS:
                        # Determine whether to allow jitter based on how quickly
                        # this note followed the previous one in the same lane.
                        dt_ms = (now - last_note_time[i]) * 1000.0 if last_note_time[i] > 0 else 9999.0
                        allow_jitter = dt_ms >= RAPID_NOTE_WINDOW_MS
                        last_note_time[i] = now

                        if hold_incoming[i]:
                            # schedule a hold press (with or without jitter)
                            hold_press_at[i] = _press_time_with_jitter(now, allow_jitter)
                            states[i]        = 'hold_pending'
                            hold_incoming[i] = False
                        else:
                            # schedule a tap press (with or without jitter)
                            tap_press_at[i] = _press_time_with_jitter(now, allow_jitter)
                            states[i]       = 'tap_pending'

                # ── TAPPED: reset once tap zone clears and release is done ─────
                elif state == 'tapped':
                    if white_count < MIN_PIXELS and tap_release_at[i] == 0.0:
                        states[i]        = 'idle'
                        hold_incoming[i] = False


if __name__ == '__main__':
    print("cool lil thing i made - Turned L to pause/resume, Ctrl+C to quit")
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
