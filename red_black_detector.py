import time
import pyautogui
import mss
import numpy as np
import keyboard
import cv2
import tkinter as tk
import threading
import sys
import os
import ctypes
from PIL import Image, ImageTk
import pygame

pyautogui.PAUSE = 0  # remove pyautogui's built-in 0.1 s delay after every action

# ── Global GUI references ──────────────────────────────────────────────
root = None
canvas = None
status_text_id = None
status_shadow_ids = []
log_text = None
log_buffer = []
sound_muted = False
snd_on = None
snd_off = None

BASE_DIR = getattr(sys, '_MEIPASS', os.path.dirname(os.path.abspath(__file__)))


def _init_sounds():
    global snd_on, snd_off
    try:
        pygame.mixer.init()
        snd_on = pygame.mixer.Sound(os.path.join(BASE_DIR, "on.mp3"))
        snd_off = pygame.mixer.Sound(os.path.join(BASE_DIR, "off.mp3"))
    except Exception:
        pass


def log(msg):
    """Thread-safe logging to the GUI text widget."""
    if root and log_text:
        root.after(0, _append_log, msg)
    else:
        log_buffer.append(msg)


def _append_log(msg):
    log_text.configure(state="normal")
    log_text.insert(tk.END, f"{msg}\n")
    log_text.see(tk.END)
    log_text.configure(state="disabled")


def update_status(on: bool, play_sound: bool = False):
    """Thread-safe status indicator update."""
    if root and canvas and status_text_id:
        root.after(0, _set_status, on, play_sound)


def _set_status(on: bool, play_sound: bool = False):
    text = "Status: ON" if on else "Status: OFF"
    fill = "#00ff88" if on else "#ff4444"
    canvas.itemconfigure(status_text_id, text=text, fill=fill)
    for sid in status_shadow_ids:
        canvas.itemconfigure(sid, text=text)
    if play_sound and not sound_muted:
        snd = snd_on if on else snd_off
        if snd:
            snd.play()


def check_overlap():
    screen_width, screen_height = pyautogui.size()

    start_x = int(screen_width * 0.2)
    end_x   = int(screen_width * 0.8)
    start_y = int(screen_height * 0.2)
    end_y   = int(screen_height * 0.8)

    monitor = {
        'top':    start_y,
        'left':   start_x,
        'width':  end_x - start_x,
        'height': end_y - start_y,
    }

    RED_MIN           = 255
    RED_MAX_GB        = 0
    BLACK_MAX_RGB     = 0
    VERTICAL_HEIGHT   = 5
    NEIGHBOR_DISTANCE = 2
    MAX_BLACK_HEIGHT  = 14.75

    # Precompute kernels once (not every frame)
    kernel_vert     = cv2.getStructuringElement(cv2.MORPH_RECT, (1, 2 * VERTICAL_HEIGHT + 1))
    kernel_neighbor = cv2.getStructuringElement(cv2.MORPH_RECT,
                                                (2 * NEIGHBOR_DISTANCE + 1,
                                                 2 * NEIGHBOR_DISTANCE + 1))

    active           = True
    disabled_by_3    = False
    paused_by_l      = False
    last_toggle_time = 0
    TOGGLE_DELAY     = 0.3
    CLICK_COOLDOWN   = 0.25
    last_click_time  = 0

    update_status(True)

    with mss.mss() as sct:
        while True:
            now = time.time()

            # Key L toggles pause on/off
            if keyboard.is_pressed('l') and now - last_toggle_time > TOGGLE_DELAY:
                paused_by_l      = not paused_by_l
                active           = not paused_by_l
                last_toggle_time = now
                msg = "Script paused" if paused_by_l else "Script resumed"
                log(msg)
                update_status(active, play_sound=True)

            # Key 3 disables the script
            if keyboard.is_pressed('3') and now - last_toggle_time > TOGGLE_DELAY:
                active           = False
                disabled_by_3    = True
                last_toggle_time = now
                log("Script disabled")
                update_status(False)

            # Keys 1 or 2 re-enable the script
            if disabled_by_3 and not active and now - last_toggle_time > TOGGLE_DELAY:
                if keyboard.is_pressed('1') or keyboard.is_pressed('2'):
                    active           = True
                    disabled_by_3    = False
                    paused_by_l      = False
                    last_toggle_time = now
                    log("Script enabled")
                    update_status(True)

            if not active:
                time.sleep(0.01)
                continue

            # Capture the center 60% of the screen
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            b, g, r = img_np[:, :, 0], img_np[:, :, 1], img_np[:, :, 2]

            # Pure-red and pure-black pixel masks (uint8 for OpenCV)
            red_mask_u8   = ((r >= RED_MIN)       & (g <= RED_MAX_GB)    & (b <= RED_MAX_GB)).astype(np.uint8) * 255
            black_mask_u8 = ((r <= BLACK_MAX_RGB) & (g <= BLACK_MAX_RGB) & (b <= BLACK_MAX_RGB)).astype(np.uint8) * 255

            # Skip if there is black but no red
            if black_mask_u8.any() and not red_mask_u8.any():
                continue

            # Vertical continuity filter: replaces the 10-iteration np.roll loop
            vertical_red_u8 = cv2.erode(red_mask_u8, kernel_vert,
                                         borderType=cv2.BORDER_CONSTANT, borderValue=0)

            if not vertical_red_u8.any():
                continue

            # Expand red outward to find neighboring pixels: replaces the 24-iteration np.roll loop
            red_neighbors_u8 = cv2.dilate(vertical_red_u8, kernel_neighbor,
                                           borderType=cv2.BORDER_CONSTANT, borderValue=0)

            # Target = black pixels adjacent to red
            target_mask_u8 = black_mask_u8 & red_neighbors_u8

            # Remove black regions that are too tall
            if target_mask_u8.any():
                num_labels, labels_im, stats, _ = cv2.connectedComponentsWithStats(
                    target_mask_u8, connectivity=8)
                for lbl in range(1, num_labels):
                    if stats[lbl, cv2.CC_STAT_HEIGHT] > MAX_BLACK_HEIGHT:
                        target_mask_u8[labels_im == lbl] = 0

            # Click the first qualifying target if cooldown has elapsed
            if target_mask_u8.any() and now - last_click_time > CLICK_COOLDOWN:
                y, x = np.argwhere(target_mask_u8)[0]
                pyautogui.click(start_x + x, start_y + y)
                last_click_time = now


# ── GUI ─────────────────────────────────────────────────────────────────
def _load_custom_font():
    """Load the Darkmode demo font on Windows using AddFontResourceEx."""
    font_path = os.path.join(BASE_DIR, "Darkmode demo-Regular.ttf")
    try:
        FR_PRIVATE = 0x10
        ctypes.windll.gdi32.AddFontResourceExW(font_path, FR_PRIVATE, 0)
    except Exception:
        pass


def create_gui():
    global root, canvas, status_text_id, log_text

    _init_sounds()

    WIN_W, WIN_H = 420, 340

    root = tk.Tk()
    root.title("lol")
    root.geometry(f"{WIN_W}x{WIN_H}")
    root.resizable(False, False)
    root.configure(bg="#000000")

    # Taskbar / window icon
    ico_path = os.path.join(BASE_DIR, "lol.ico")
    try:
        root.iconbitmap(ico_path)
    except Exception:
        pass

    # Load custom font
    _load_custom_font()

    # ── Background image (ico stretched to fill) ──
    bg_img = Image.open(ico_path).resize((WIN_W, WIN_H), Image.LANCZOS).convert("RGBA")
    bg_photo = ImageTk.PhotoImage(bg_img)

    canvas = tk.Canvas(root, width=WIN_W, height=WIN_H, highlightthickness=0, bd=0)
    canvas.pack(fill=tk.BOTH, expand=True)
    canvas.create_image(0, 0, image=bg_photo, anchor="nw")
    canvas._bg_ref = bg_photo  # prevent garbage collection

    # ── "Made by Turned" title (white outline for visibility) ──
    tx, ty = WIN_W // 2, 28
    for dx, dy in [(-2,0),(2,0),(0,-2),(0,2),(-1,-1),(1,-1),(-1,1),(1,1)]:
        canvas.create_text(
            tx + dx, ty + dy,
            text="Made by Turned",
            font=("Darkmode demo", 32),
            fill="#ffffff",
            anchor="n",
        )
    canvas.create_text(
        tx, ty,
        text="Made by Turned",
        font=("Darkmode demo", 32),
        fill="#000000",
        anchor="n",
    )

    # ── Status indicator (black outline for visibility) ──
    sx, sy = WIN_W // 2, 82
    for dx, dy in [(-1,0),(1,0),(0,-1),(0,1)]:
        status_shadow_ids.append(canvas.create_text(
            sx + dx, sy + dy,
            text="Status: ON",
            font=("Consolas", 18, "bold"),
            fill="#000000",
            anchor="n",
        ))
    status_text_id = canvas.create_text(
        sx, sy,
        text="Status: ON",
        font=("Consolas", 18, "bold"),
        fill="#00ff88",
        anchor="n",
    )

    # ── Keybinds header (white outline) ──
    hx, hy = WIN_W // 2, 118
    for dx, dy in [(-1,0),(1,0),(0,-1),(0,1)]:
        canvas.create_text(
            hx + dx, hy + dy,
            text="Keybinds",
            font=("Consolas", 13, "bold"),
            fill="#ffffff",
            anchor="n",
        )
    canvas.create_text(
        hx, hy,
        text="Keybinds",
        font=("Consolas", 13, "bold"),
        fill="#000000",
        anchor="n",
    )

    # ── Keybind rows inside a white box ──
    binds = [
        ("L", "Pause / Resume"),
        ("3", "Disable"),
        ("1 / 2", "Enable"),
    ]

    box_x1, box_y1 = 80, 142
    box_x2, box_y2 = WIN_W - 80, 142 + len(binds) * 26 + 12
    canvas.create_rectangle(
        box_x1, box_y1, box_x2, box_y2,
        fill="#ffffff", outline="#cccccc", width=2,
    )

    y = box_y1 + 14
    for key, desc in binds:
        canvas.create_text(
            box_x1 + 20, y,
            text=key,
            font=("Consolas", 11, "bold"),
            fill="#008800",
            anchor="w",
        )
        canvas.create_text(
            box_x1 + 80, y,
            text=f"—  {desc}",
            font=("Consolas", 11),
            fill="#000000",
            anchor="w",
        )
        y += 26

    # ── Toggle Log button ──
    log_visible = [False]
    log_win = [None]

    def toggle_log():
        if log_visible[0]:
            # Hide
            if log_win[0]:
                log_win[0].withdraw()
            log_visible[0] = False
            log_btn.configure(text="Show Log")
        else:
            # Show
            if log_win[0]:
                log_win[0].deiconify()
            else:
                _create_log_window()
            log_visible[0] = True
            log_btn.configure(text="Hide Log")

    log_btn = tk.Button(
        root,
        text="Show Log",
        font=("Consolas", 11, "bold"),
        fg="#ffffff",
        bg="#333333",
        activeforeground="#ffffff",
        activebackground="#555555",
        relief=tk.FLAT,
        padx=16, pady=4,
        command=toggle_log,
    )

    def toggle_mute():
        global sound_muted
        sound_muted = not sound_muted
        mute_btn.configure(text="Unmute" if sound_muted else "Mute")

    mute_btn = tk.Button(
        root,
        text="Mute",
        font=("Consolas", 11, "bold"),
        fg="#ffffff",
        bg="#333333",
        activeforeground="#ffffff",
        activebackground="#555555",
        relief=tk.FLAT,
        padx=16, pady=4,
        command=toggle_mute,
    )

    btn_y = box_y2 + 24
    canvas.create_window(WIN_W // 2 - 60, btn_y, window=log_btn, anchor="n")
    canvas.create_window(WIN_W // 2 + 60, btn_y, window=mute_btn, anchor="n")

    def _create_log_window():
        global log_text
        win = tk.Toplevel(root)
        win.title("lol - Log")
        win.geometry("460x300")
        win.configure(bg="#0f0f23")
        try:
            win.iconbitmap(ico_path)
        except Exception:
            pass

        scrollbar = tk.Scrollbar(win)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

        log_text = tk.Text(
            win,
            bg="#0f0f23",
            fg="#cccccc",
            font=("Consolas", 10),
            insertbackground="#cccccc",
            selectbackground="#333355",
            relief=tk.FLAT,
            wrap=tk.WORD,
            state="disabled",
            yscrollcommand=scrollbar.set,
        )
        log_text.pack(fill=tk.BOTH, expand=True)
        scrollbar.configure(command=log_text.yview)

        log_win[0] = win

        # Replay any messages buffered before the log window existed
        for msg in log_buffer:
            _append_log(msg)
        log_buffer.clear()

        def on_log_close():
            win.withdraw()
            log_visible[0] = False
            log_btn.configure(text="Show Log")

        win.protocol("WM_DELETE_WINDOW", on_log_close)

    # ── Close handler ──
    def on_close():
        os._exit(0)

    root.protocol("WM_DELETE_WINDOW", on_close)

    # ── Start detection in a daemon thread ──
    log("lol")
    t = threading.Thread(target=_run_detector, daemon=True)
    t.start()

    root.mainloop()


def _run_detector():
    try:
        check_overlap()
    except Exception as e:
        log(f"Error: {e}")


if __name__ == '__main__':
    create_gui()
