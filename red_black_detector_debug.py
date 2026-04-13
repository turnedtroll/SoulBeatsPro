import time
import pyautogui
import mss
import numpy as np
import keyboard
import cv2

pyautogui.PAUSE = 0  # remove pyautogui's built-in 0.1 s delay after every action


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

    # Precompute kernels once
    kernel_vert     = cv2.getStructuringElement(cv2.MORPH_RECT, (1, 2 * VERTICAL_HEIGHT + 1))
    kernel_neighbor = cv2.getStructuringElement(cv2.MORPH_RECT,
                                                (2 * NEIGHBOR_DISTANCE + 1,
                                                 2 * NEIGHBOR_DISTANCE + 1))

    active           = True
    disabled_by_3    = False
    last_toggle_time = 0
    TOGGLE_DELAY     = 0.3
    CLICK_COOLDOWN   = 0.25
    last_click_time  = 0

    WIN_NAME  = "Red-Black Detector — Debug"
    PANEL_W   = 180
    cv2.namedWindow(WIN_NAME, cv2.WINDOW_NORMAL)

    with mss.mss() as sct:
        while True:
            now = time.time()

            # Key 3 disables the script
            if keyboard.is_pressed('3') and now - last_toggle_time > TOGGLE_DELAY:
                active           = False
                disabled_by_3    = True
                last_toggle_time = now
                print("Script disabled")

            # Keys 1 or 2 re-enable the script
            if disabled_by_3 and not active and now - last_toggle_time > TOGGLE_DELAY:
                if keyboard.is_pressed('1') or keyboard.is_pressed('2'):
                    active           = True
                    disabled_by_3    = False
                    last_toggle_time = now
                    print("Script enabled")

            # --- Always capture so the debug window never goes black ---
            img    = sct.grab(monitor)
            img_np = np.frombuffer(img.raw, dtype=np.uint8).reshape(img.height, img.width, 4)
            b, g, r = img_np[:, :, 0], img_np[:, :, 1], img_np[:, :, 2]

            red_mask_u8   = ((r >= RED_MIN)       & (g <= RED_MAX_GB)    & (b <= RED_MAX_GB)  ).astype(np.uint8) * 255
            black_mask_u8 = ((r <= BLACK_MAX_RGB) & (g <= BLACK_MAX_RGB) & (b <= BLACK_MAX_RGB)).astype(np.uint8) * 255

            # These are shown in the overlay even when no click happens
            vertical_red_u8 = np.zeros_like(red_mask_u8)
            target_mask_u8  = np.zeros_like(black_mask_u8)
            clicked         = None

            if active and not (black_mask_u8.any() and not red_mask_u8.any()):
                # Vertical continuity filter via erosion
                vertical_red_u8 = cv2.erode(red_mask_u8, kernel_vert,
                                             borderType=cv2.BORDER_CONSTANT, borderValue=0)

                if vertical_red_u8.any():
                    # Expand red outward to find neighboring black pixels via dilation
                    red_neighbors_u8 = cv2.dilate(vertical_red_u8, kernel_neighbor,
                                                   borderType=cv2.BORDER_CONSTANT, borderValue=0)

                    target_mask_u8 = black_mask_u8 & red_neighbors_u8

                    # Remove black regions that are too tall
                    if target_mask_u8.any():
                        num_labels, labels_im, stats, _ = cv2.connectedComponentsWithStats(
                            target_mask_u8, connectivity=8)
                        for lbl in range(1, num_labels):
                            if stats[lbl, cv2.CC_STAT_HEIGHT] > MAX_BLACK_HEIGHT:
                                target_mask_u8[labels_im == lbl] = 0

                    # Click
                    if target_mask_u8.any() and now - last_click_time > CLICK_COOLDOWN:
                        y, x = np.argwhere(target_mask_u8)[0]
                        clicked = (y, x)
                        pyautogui.click(start_x + x, start_y + y)
                        last_click_time = now

            # --- Build overlay (always, every frame) ---
            vis = (img_np[:, :, :3] * 0.4).astype(np.uint8)
            vis[red_mask_u8    > 0]                              = (0,   0, 220)   # raw red
            vis[vertical_red_u8 > 0]                             = (0, 140, 255)   # vert-filtered red
            vis[(black_mask_u8 > 0) & (target_mask_u8 == 0)]    = (180, 180,  0)  # black near red
            vis[target_mask_u8 > 0]                              = (0, 255,  80)   # click target

            if clicked is not None:
                cy, cx = clicked
                cv2.drawMarker(vis, (cx, cy), (255, 255, 255),
                               cv2.MARKER_CROSS, 20, 2, cv2.LINE_AA)

            # --- Legend panel ---
            h = vis.shape[0]
            panel = np.zeros((h, PANEL_W, 3), dtype=np.uint8)

            def put(text, row, color=(220, 220, 220)):
                cv2.putText(panel, text, (6, row), cv2.FONT_HERSHEY_SIMPLEX,
                            0.42, color, 1, cv2.LINE_AA)

            put("=== DEBUG ===",  20, (255, 255, 100))
            put("1/2 : enable",   44)
            put("3   : disable",  60)

            put("-- overlays --",  84, (160, 160, 160))
            cv2.rectangle(panel, (6,  92), (18, 104), (0,   0, 220), -1);  put("raw red",   106)
            cv2.rectangle(panel, (6, 114), (18, 126), (0, 140, 255), -1);  put("vert red",  128)
            cv2.rectangle(panel, (6, 136), (18, 148), (180, 180,  0), -1); put("blk/red",   150)
            cv2.rectangle(panel, (6, 158), (18, 170), (0, 255,  80), -1);  put("TARGET",    172, (0, 255, 80))

            state_color = (0, 255, 0) if active else (0, 0, 255)
            put("ACTIVE" if active else "DISABLED", h - 12, state_color)

            # --- Show frame (always called — fixes the black window bug) ---
            cv2.imshow(WIN_NAME, np.hstack([panel, vis]))
            cv2.waitKey(1)

            if not active:
                time.sleep(0.01)


if __name__ == '__main__':
    print("made by the goat turned lol")
    print("Press D to toggle the debug window")
    try:
        while True:
            check_overlap()
    except KeyboardInterrupt:
        cv2.destroyAllWindows()
        print("Stopped")
    except Exception as e:
        cv2.destroyAllWindows()
        print(f"Error: {e}")
        time.sleep(0.5)
