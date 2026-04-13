import time
import mss
import numpy as np
import cv2
import pyautogui
import keyboard

# ─── HOW TO USE ───────────────────────────────────────────────────────────────
# 1. Run this script — a zoomed window appears
# 2. Move your mouse to the spot you want, then press ENTER anywhere
#    (you do NOT need to be clicked into the terminal)
# 3. The green box shows the 5x5 area that will be sampled
# 4. Repeat for all 8 positions
# 5. Paste the printed config into rhythm_macro.py
# ──────────────────────────────────────────────────────────────────────────────

LABELS = [
    "Lane 1 (z)  — WHITE tap    (center of receptor circle 1)",
    "Lane 2 (x)  — WHITE tap    (center of receptor circle 2)",
    "Lane 3 (,)  — WHITE tap    (center of receptor circle 3)",
    "Lane 4 (.)  — WHITE tap    (center of receptor circle 4)",
    "Lane 1 (z)  — GRAY  hold   (above circle 1 where hold tail appears)",
    "Lane 2 (x)  — GRAY  hold   (above circle 2 where hold tail appears)",
    "Lane 3 (,)  — GRAY  hold   (above circle 3 where hold tail appears)",
    "Lane 4 (.)  — GRAY  hold   (above circle 4 where hold tail appears)",
]

CAPTURE_SIZE = 80
ZOOM         = 6
DISPLAY_SIZE = CAPTURE_SIZE * ZOOM
HALF         = CAPTURE_SIZE // 2
BOX_HALF     = 2     # 5x5 box = 2 pixels each side of center

# Global enter flag — works even when terminal is not focused
enter_flag     = False
enter_cooldown = 0

# J — horizontal lock (pins Y so mouse can only move left/right)
y_locked   = False
lock_y     = 0
j_cooldown = 0

pyautogui.PAUSE = 0   # remove pyautogui delay so mouse snapping is instant

def on_enter(e):
    global enter_flag, enter_cooldown
    now = time.time()
    if now - enter_cooldown > 0.5:
        enter_flag     = True
        enter_cooldown = now

def on_j(e):
    global y_locked, lock_y, j_cooldown
    now = time.time()
    if now - j_cooldown > 0.3:
        j_cooldown = now
        if not y_locked:
            lock_y   = pyautogui.position()[1]
            y_locked = True
            print(f"  Y locked at {lock_y} — mouse can only move left/right. Press J to unlock.")
        else:
            y_locked = False
            print("  Y unlocked.")

keyboard.on_press_key('enter', on_enter)
keyboard.on_press_key('j',     on_j)

saved = []

def draw_frame(sct, step_label, step_num, total):
    global enter_flag

    mx, my = pyautogui.position()

    # ── Y lock: snap mouse back to locked row if it drifts ───────────────────
    if y_locked and my != lock_y:
        pyautogui.moveTo(mx, lock_y)
        my = lock_y

    region = {
        'left':   max(0, mx - HALF),
        'top':    max(0, my - HALF),
        'width':  CAPTURE_SIZE,
        'height': CAPTURE_SIZE,
    }
    img    = sct.grab(region)
    img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
    frame  = cv2.cvtColor(img_np, cv2.COLOR_BGRA2BGR)
    zoomed = cv2.resize(frame, (DISPLAY_SIZE, DISPLAY_SIZE), interpolation=cv2.INTER_NEAREST)

    cx, cy = DISPLAY_SIZE // 2, DISPLAY_SIZE // 2

    # Crosshair — red when Y locked, green when free
    hair_col = (0, 0, 220) if y_locked else (0, 180, 0)
    cv2.line(zoomed, (0, cy), (DISPLAY_SIZE, cy), hair_col, 1)
    cv2.line(zoomed, (cx, 0), (cx, DISPLAY_SIZE), hair_col, 1)

    # 5x5 detection box
    bx1, by1 = cx - BOX_HALF * ZOOM, cy - BOX_HALF * ZOOM
    bx2, by2 = cx + BOX_HALF * ZOOM, cy + BOX_HALF * ZOOM
    cv2.rectangle(zoomed, (bx1, by1), (bx2, by2), (0, 255, 0), 2)

    # Pixel color at cursor
    px = sct.grab({'left': mx, 'top': my, 'width': 1, 'height': 1})
    r, g, b = px.pixel(0, 0)[0], px.pixel(0, 0)[1], px.pixel(0, 0)[2]

    # Text overlay
    cv2.putText(zoomed, f"[{step_num}/{total}]  {step_label}",
                (6, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 220, 255), 1)
    cv2.putText(zoomed, f"x={mx}  y={my}",
                (6, 46), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 0), 1)
    cv2.putText(zoomed, f"RGB=({r}, {g}, {b})",
                (6, 68), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 0), 1)

    # Y lock status
    if y_locked:
        cv2.putText(zoomed, f"[J] Y LOCKED at {lock_y} — left/right only",
                    (6, DISPLAY_SIZE - 28), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 0, 220), 1)
    else:
        cv2.putText(zoomed, "[J] Press J to lock Y axis",
                    (6, DISPLAY_SIZE - 28), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (80, 80, 80), 1)

    cv2.putText(zoomed, "Press ENTER to save this position",
                (6, DISPLAY_SIZE - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.48, (0, 220, 255), 1)

    cv2.imshow("Coordinate Finder", zoomed)
    cv2.waitKey(1)

    return mx, my, r, g, b


with mss.mss() as sct:

    # ── 0/8 intro screen — wait for ENTER before starting ────────────────────
    print("\nCoordinate Finder ready.")
    print("Move your mouse to position 1 then press ENTER anywhere to begin.\n")

    while True:
        mx, my = pyautogui.position()
        if y_locked and my != lock_y:
            pyautogui.moveTo(mx, lock_y)
            my = lock_y
        region = {
            'left':   max(0, mx - HALF),
            'top':    max(0, my - HALF),
            'width':  CAPTURE_SIZE,
            'height': CAPTURE_SIZE,
        }
        img    = sct.grab(region)
        img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
        frame  = cv2.cvtColor(img_np, cv2.COLOR_BGRA2BGR)
        zoomed = cv2.resize(frame, (DISPLAY_SIZE, DISPLAY_SIZE), interpolation=cv2.INTER_NEAREST)

        cx, cy = DISPLAY_SIZE // 2, DISPLAY_SIZE // 2
        hair_col = (0, 0, 220) if y_locked else (0, 180, 0)
        cv2.line(zoomed, (0, cy), (DISPLAY_SIZE, cy), hair_col, 1)
        cv2.line(zoomed, (cx, 0), (cx, DISPLAY_SIZE), hair_col, 1)
        bx1, by1 = cx - BOX_HALF * ZOOM, cy - BOX_HALF * ZOOM
        bx2, by2 = cx + BOX_HALF * ZOOM, cy + BOX_HALF * ZOOM
        cv2.rectangle(zoomed, (bx1, by1), (bx2, by2), (0, 255, 0), 2)

        cv2.putText(zoomed, "[0/8]  Move mouse to position 1",
                    (6, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 220, 255), 1)
        if y_locked:
            cv2.putText(zoomed, f"[J] Y LOCKED at {lock_y}",
                        (6, DISPLAY_SIZE - 28), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 0, 220), 1)
        else:
            cv2.putText(zoomed, "[J] Press J to lock Y axis",
                        (6, DISPLAY_SIZE - 28), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (80, 80, 80), 1)
        cv2.putText(zoomed, "Press ENTER anywhere to begin",
                    (6, DISPLAY_SIZE - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.48, (0, 220, 255), 1)

        cv2.imshow("Coordinate Finder", zoomed)
        cv2.waitKey(1)

        if enter_flag:
            enter_flag = False
            break

        time.sleep(0.03)

    # ── Steps 1–8 ─────────────────────────────────────────────────────────────
    for i, label in enumerate(LABELS):
        print(f"[{i+1}/8] {label}")
        print("  Move mouse to the spot then press ENTER anywhere.\n")

        while True:
            mx, my, r, g, b = draw_frame(sct, label, i + 1, len(LABELS))

            if enter_flag:
                enter_flag = False
                saved.append((mx, my, r, g, b))
                print(f"  Saved → x={mx}, y={my}  RGB=({r}, {g}, {b})\n")
                break

            time.sleep(0.03)

cv2.destroyAllWindows()
keyboard.unhook_all()

# ── Print final config ─────────────────────────────────────────────────────────
tap  = saved[:4]
hold = saved[4:]
keys = ['z', 'x', ',', '.']

print("\n" + "=" * 60)
print("PASTE THIS INTO rhythm_macro.py:\n")

print("# Tap pixel coordinates (white detection — 5x5 area each)")
for j, (x, y, r, g, b) in enumerate(tap):
    print(f"# Lane {j+1} ({keys[j]}) — sampled RGB=({r},{g},{b})")
print(f"TAP_PIXELS  = {[(x, y) for x, y, *_ in tap]}\n")

print("# Hold pixel coordinates (gray detection — 5x5 area each)")
for j, (x, y, r, g, b) in enumerate(hold):
    print(f"# Lane {j+1} ({keys[j]}) — sampled RGB=({r},{g},{b})")
print(f"HOLD_PIXELS = {[(x, y) for x, y, *_ in hold]}")
print("=" * 60)
