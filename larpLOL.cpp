/*
 * larpLOL - Funky Friday Macro (C++ rewrite)
 *
 * Build (MinGW):
 *   g++ -O2 -std=c++17 -o larpLOL.exe larpLOL.cpp -lgdi32 -luser32 -lkernel32
 *
 * Build (MSVC):
 *   cl /O2 /std:c++17 larpLOL.cpp /link gdi32.lib user32.lib kernel32.lib
 *
 * Controls:
 *   L key = pause / resume
 *   Close window = stop
 *
 * coords.txt is loaded from the same folder as the .exe (same format as Python version)
 */

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <iostream>
#include <fstream>
#include <string>
#include <array>
#include <vector>
#include <thread>
#include <atomic>
#include <random>
#include <algorithm>
#include <cstring>
#include <cwchar>

#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "kernel32.lib")

// ─── PRECISE TIMING ──────────────────────────────────────────────────────────

static LARGE_INTEGER g_qpf;

static void timer_init() {
    QueryPerformanceFrequency(&g_qpf);
}

static double now_sec() {
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return (double)c.QuadPart / (double)g_qpf.QuadPart;
}

// High-precision sleep: OS sleep for bulk, spin for final 1ms
static void precise_sleep(double secs) {
    if (secs <= 0.0) return;
    double target = now_sec() + secs;
    double bulk = secs - 0.001;
    if (bulk > 0.0) Sleep((DWORD)(bulk * 1000.0));
    while (now_sec() < target) { YieldProcessor(); }
}

// ─── CONFIG ──────────────────────────────────────────────────────────────────

struct Config {
    std::array<int,4> tap_x  = {720, 881, 1039, 1200};
    std::array<int,4> tap_y  = {950, 950,  950,  950};
    std::array<int,4> hold_x = {720, 878, 1040, 1197};
    std::array<int,4> hold_y = {824, 824,  824,  824};

    int    sample_half           = 3;
    int    white_min             = 240;
    int    gray_min              = 130;
    int    gray_max              = 170;
    int    min_pixels            = 3;
    int    timing_offset         = 0;

    double tap_key_duration      = 0.030;
    double hold_release_cooldown = 0.020;
    double hold_grace_sec        = 0.100;
    double max_hold_duration     = 5.0;
    double toggle_delay          = 0.3;

    double good_chance           = 0.30;
    double good_delay_min        = 0.052;
    double good_delay_max        = 0.062;
    double jack_threshold        = 0.150;
};

// ─── KEYS ────────────────────────────────────────────────────────────────────

static const WORD SCAN[4] = { 0x2C, 0x2D, 0x33, 0x34 };

static void send_key(int lane, bool up) {
    INPUT inp;
    memset(&inp, 0, sizeof(inp));
    inp.type       = INPUT_KEYBOARD;
    inp.ki.wScan   = SCAN[lane];
    inp.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
    SendInput(1, &inp, sizeof(INPUT));
}

static void key_press(int lane)   { send_key(lane, false); }
static void key_release(int lane) { send_key(lane, true);  }

// ─── SCREEN CAPTURE ──────────────────────────────────────────────────────────

struct Screen {
    int      left=0, top=0, w=0, h=0;
    HDC      hdc_screen=nullptr, hdc_mem=nullptr;
    HBITMAP  hbm=nullptr;
    RGBQUAD* bits=nullptr;
    bool     ready=false;

    bool init(int l, int t, int width, int height) {
        left=l; top=t; w=width; h=height;

        hdc_screen = GetDC(NULL);
        if (!hdc_screen) return false;

        hdc_mem = CreateCompatibleDC(hdc_screen);
        if (!hdc_mem) { ReleaseDC(NULL,hdc_screen); return false; }

        BITMAPINFO bmi={};
        bmi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = w;
        bmi.bmiHeader.biHeight      = -h;
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        void* raw=nullptr;
        hbm = CreateDIBSection(hdc_mem, &bmi, DIB_RGB_COLORS, &raw, NULL, 0);
        if (!hbm || !raw) {
            DeleteDC(hdc_mem);
            ReleaseDC(NULL,hdc_screen);
            return false;
        }
        bits = (RGBQUAD*)raw;
        SelectObject(hdc_mem, hbm);
        ready = true;
        return true;
    }

    void capture() {
        BitBlt(hdc_mem, 0, 0, w, h, hdc_screen, left, top, SRCCOPY);
        GdiFlush();
    }

    inline RGBQUAD get(int x, int y) const {
        if ((unsigned)x>=(unsigned)w || (unsigned)y>=(unsigned)h) return {};
        return bits[y*w+x];
    }

    ~Screen() {
        if (!ready) return;
        DeleteObject(hbm);
        DeleteDC(hdc_mem);
        ReleaseDC(NULL, hdc_screen);
    }
};

static int count_white(const Screen& sc, int cx, int cy, int half, int wmin) {
    int n=0;
    for (int dy=-half; dy<=half; dy++)
        for (int dx=-half; dx<=half; dx++) {
            RGBQUAD p=sc.get(cx+dx,cy+dy);
            if (p.rgbRed>=wmin && p.rgbGreen>=wmin && p.rgbBlue>=wmin) n++;
        }
    return n;
}

static int count_gray(const Screen& sc, int cx, int cy, int half, int gmin, int gmax) {
    int n=0;
    for (int dy=-half; dy<=half; dy++)
        for (int dx=-half; dx<=half; dx++) {
            RGBQUAD p=sc.get(cx+dx,cy+dy);
            if (p.rgbRed  >=gmin && p.rgbRed  <=gmax &&
                p.rgbGreen>=gmin && p.rgbGreen<=gmax &&
                p.rgbBlue >=gmin && p.rgbBlue <=gmax) n++;
        }
    return n;
}

// ─── ROBLOX FOCUS ────────────────────────────────────────────────────────────

static bool is_roblox_focused() {
    HWND hwnd = GetForegroundWindow();
    if (!hwnd) return false;
    DWORD pid=0;
    GetWindowThreadProcessId(hwnd, &pid);
    HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!h) return false;
    wchar_t buf[MAX_PATH]={};
    DWORD sz=MAX_PATH;
    QueryFullProcessImageNameW(h, 0, buf, &sz);
    CloseHandle(h);
    for (int i=0; buf[i]; i++) buf[i]=(wchar_t)towlower(buf[i]);
    return wcsstr(buf, L"roblox") != nullptr;
}

// ─── COORD LOAD ──────────────────────────────────────────────────────────────

static bool load_coords(Config& cfg, const std::string& path) {
    std::ifstream f(path);
    if (!f.is_open()) return false;
    std::string src, line;
    while (std::getline(f, line)) src += line;

    auto parse_pairs = [&](const std::string& key,
                            std::array<int,4>& ax, std::array<int,4>& ay) {
        size_t pos = src.find("\""+key+"\"");
        if (pos==std::string::npos) return;
        pos = src.find('[', pos);
        for (int i=0; i<4; i++) {
            pos = src.find('[', pos+1);
            if (pos==std::string::npos) return;
            size_t comma = src.find(',', pos);
            size_t close = src.find(']', pos);
            if (comma==std::string::npos || close==std::string::npos) return;
            try {
                ax[i] = std::stoi(src.substr(pos+1));
                ay[i] = std::stoi(src.substr(comma+1));
            } catch (...) { return; }
            pos = close;
        }
    };

    parse_pairs("tap",  cfg.tap_x,  cfg.tap_y);
    parse_pairs("hold", cfg.hold_x, cfg.hold_y);

    size_t off = src.find("timing_offset");
    if (off!=std::string::npos) {
        size_t colon = src.find(':', off);
        if (colon!=std::string::npos)
            try { cfg.timing_offset = std::stoi(src.substr(colon+1)); } catch(...) {}
    }
    return true;
}

// ─── MACRO LOOP ──────────────────────────────────────────────────────────────

static std::atomic<bool> g_running{false};
static std::atomic<bool> g_active{true};

static void run_macro(Config cfg) {
    // Apply timing offset
    for (int i=0; i<4; i++) {
        cfg.tap_y[i]  -= cfg.timing_offset;
        cfg.hold_y[i] -= cfg.timing_offset;
    }

    // Bounding capture region
    int mn_x=9999,mn_y=9999,mx_x=0,mx_y=0;
    for (int i=0; i<4; i++) {
        mn_x=std::min(mn_x,std::min(cfg.tap_x[i],cfg.hold_x[i]));
        mn_y=std::min(mn_y,std::min(cfg.tap_y[i],cfg.hold_y[i]));
        mx_x=std::max(mx_x,std::max(cfg.tap_x[i],cfg.hold_x[i]));
        mx_y=std::max(mx_y,std::max(cfg.tap_y[i],cfg.hold_y[i]));
    }
    int sh=cfg.sample_half;
    int cl=mn_x-sh-1, ct=mn_y-sh-1;
    int cw=(mx_x+sh+1)-cl, ch=(mx_y+sh+1)-ct;

    std::array<int,4> trx,try_,hrx,hry;
    for (int i=0; i<4; i++) {
        trx[i] =cfg.tap_x[i] -cl;  try_[i]=cfg.tap_y[i] -ct;
        hrx[i] =cfg.hold_x[i]-cl;  hry[i] =cfg.hold_y[i]-ct;
    }

    Screen sc;
    if (!sc.init(cl,ct,cw,ch)) {
        std::cerr<<"[larpLOL] Screen init failed\n"; return;
    }

    enum State { IDLE, TAPPED, HOLDING };
    std::array<State, 4>  states          ={IDLE,IDLE,IDLE,IDLE};
    std::array<bool,  4>  hold_incoming   ={false,false,false,false};
    std::array<double,4>  hold_seen_at    ={0,0,0,0};
    std::array<bool,  4>  hold_saw_tail   ={false,false,false,false};
    std::array<double,4>  tap_release_at  ={0,0,0,0};
    std::array<double,4>  hold_released_at={0,0,0,0};
    std::array<double,4>  hold_started_at ={0,0,0,0};
    std::array<bool,  4>  note_visible    ={false,false,false,false};
    std::array<bool,  4>  note_pressed    ={false,false,false,false};
    std::array<double,4>  last_pressed_at  ={0,0,0,0};
    // Overlap detection: track how long tap zone has been continuously white
    std::array<double,4>  white_since      ={0,0,0,0}; // when zone first went white
    // For tap notes that arrive during a hold on the same lane
    std::array<double,4>  hold_tap_last    ={0,0,0,0}; // last tap fired during hold
    constexpr double HOLD_TAP_COOLDOWN = 0.080; // min gap between taps-during-hold
    // Track last frame with any hold pixels — if gap exceeds this, force release
    std::array<double,4>  hold_last_seen   ={0,0,0,0};
    constexpr double HOLD_GONE_TIMEOUT = 0.060; // 60ms with no pixels = hold is gone
    std::array<bool,  4>  was_white        ={false,false,false,false}; // zone was white last frame
    // If zone stays white longer than one note duration, a second note overlapped
    constexpr double OVERLAP_THRESHOLD = 0.080; // 80ms — one note takes ~60ms to pass
    // For Good taps: schedule a future key_press without threading
    std::array<double,4>  pending_press_at ={0,0,0,0}; // >0 = press queued
    std::array<bool,  4>  pending_active   ={false,false,false,false};
    // Density tracking: rolling press count in the last DENSITY_WINDOW seconds
    // If count >= DENSITY_HIGH we drop Good chance to 4%
    std::array<int,   4>  press_count     ={0,0,0,0}; // presses in current window
    std::array<double,4>  density_window  ={0,0,0,0}; // start of current window
    constexpr double DENSITY_WINDOW = 0.5;  // measure over last 500ms
    constexpr int    DENSITY_HIGH   = 4;    // 4+ presses in 500ms = dense
    constexpr double GOOD_DENSE     = 0.04; // 4% Good when dense
    constexpr double GOOD_SPARSE    = 0.30; // 30% Good when sparse

    std::mt19937 rng(std::random_device{}());
    std::uniform_real_distribution<double> chance_dist(0.0,1.0);
    std::uniform_real_distribution<double> delay_dist(cfg.good_delay_min,cfg.good_delay_max);

    double last_toggle=0, focus_check=0;
    bool roblox_focus=false;

    auto release_all=[&](){
        for (int i=0; i<4; i++){
            if (states[i]==HOLDING) key_release(i);
            if (tap_release_at[i]>0){ key_release(i); }
            tap_release_at[i]   = 0;
            pending_press_at[i] = 0;
            pending_active[i]   = false;
            white_since[i]      = 0;
            was_white[i]        = false;
            hold_tap_last[i]    = 0;
            hold_last_seen[i]   = 0;
            states[i]=IDLE; hold_incoming[i]=false; hold_seen_at[i]=0;
            hold_saw_tail[i]=false; hold_started_at[i]=0;
        }
    };

    std::cout<<"[larpLOL] Running | L = pause/resume\n";

    while (g_running.load()) {
        double t = now_sec();

        // Pause toggle
        if ((GetAsyncKeyState(0x4C)&0x8000) && t-last_toggle>cfg.toggle_delay){
            g_active=!g_active.load(); last_toggle=t;
            if (!g_active.load()){ release_all(); std::cout<<"[larpLOL] Paused\n"; }
            else std::cout<<"[larpLOL] Resumed\n";
        }
        if (!g_active.load()){ Sleep(10); continue; }

        // Focus check
        if (t-focus_check>0.2){ roblox_focus=is_roblox_focused(); focus_check=t; }
        if (!roblox_focus){ release_all(); Sleep(50); continue; }

        // Fire pending Good presses (no thread — just check timestamp each frame)
        for (int i=0; i<4; i++) {
            if (pending_active[i] && t >= pending_press_at[i]) {
                key_press(i);
                tap_release_at[i]  = t + cfg.tap_key_duration;
                last_pressed_at[i] = t;
                pending_active[i]  = false;
                pending_press_at[i]= 0;
            }
        }

        // Non-blocking tap releases
        for (int i=0; i<4; i++)
            if (tap_release_at[i]>0 && t>=tap_release_at[i])
                { key_release(i); tap_release_at[i]=0; }

        // Force-release stuck holds
        for (int i=0; i<4; i++)
            if (states[i]==HOLDING && hold_started_at[i]>0 &&
                t-hold_started_at[i]>=cfg.max_hold_duration){
                key_release(i); states[i]=IDLE; hold_saw_tail[i]=false;
                hold_incoming[i]=false; hold_seen_at[i]=0;
                hold_released_at[i]=t; hold_started_at[i]=0;
                hold_last_seen[i]=0;
            }

        sc.capture();

        for (int i=0; i<4; i++){
            int wc  = count_white(sc,trx[i],try_[i],sh,cfg.white_min);
            int tgc = count_gray (sc,trx[i],try_[i],sh,cfg.gray_min,cfg.gray_max);
            int hgc = count_gray (sc,hrx[i],hry[i],sh,cfg.gray_min,cfg.gray_max);
            bool hhw= count_white(sc,hrx[i],hry[i],sh,cfg.white_min)>0;
            State& st=states[i];

            // Track continuous white duration for overlap detection
            if (wc >= cfg.min_pixels) {
                if (!was_white[i]) white_since[i] = t; // zone just went white
                was_white[i] = true;
            } else {
                was_white[i]  = false;
                white_since[i]= 0;
            }

            if (wc>=cfg.min_pixels && !note_visible[i])
                { note_visible[i]=true; note_pressed[i]=false; }

            if (hgc>=cfg.min_pixels && !hhw)
                { hold_incoming[i]=true; hold_seen_at[i]=t; }

            // ── HOLDING ──────────────────────────────────────────────────
            if (st==HOLDING){
                bool any_pixels = (wc>0 || tgc>0 || hgc>0);

                // Update last-seen timestamp whenever any pixels are visible
                if (any_pixels) hold_last_seen[i] = t;

                // Tail = gray at TAP zone (the end cap scrolling through)
                if (tgc>=cfg.min_pixels) hold_saw_tail[i]=true;

                // Decide whether to release:
                // - If tail was seen: release once tap-zone gray clears
                // - If no tail: release once pixels have been gone for HOLD_GONE_TIMEOUT
                bool do_release = false;
                if (hold_saw_tail[i]){
                    do_release = (tgc < cfg.min_pixels);
                } else {
                    do_release = (hold_last_seen[i]>0 && t-hold_last_seen[i]>=HOLD_GONE_TIMEOUT);
                }

                if (do_release){
                    key_release(i); st=IDLE; hold_saw_tail[i]=false;
                    if (!hold_incoming[i]) hold_seen_at[i]=0;
                    hold_released_at[i]=t; hold_started_at[i]=0;
                    hold_tap_last[i]=0; hold_last_seen[i]=0;
                } else {
                    // Queue next hold if its gray pre-detection appears at hold zone
                    if (hgc>=cfg.min_pixels && !hhw && !hold_incoming[i]){
                        hold_incoming[i] = true;
                        hold_seen_at[i]  = t;
                    }
                    // Hold→Tap: can't press without breaking hold, just mark pressed
                    if (wc>=cfg.min_pixels && t-hold_tap_last[i]>=HOLD_TAP_COOLDOWN){
                        note_pressed[i]  = true;
                        hold_tap_last[i] = t;
                    }
                }
            }

            // ── IDLE ─────────────────────────────────────────────────────────
            // For queued hold→hold overlaps, bypass the cooldown entirely
            bool _bypass_cooldown = hold_incoming[i] && wc>=cfg.min_pixels;
            if (st==IDLE && !pending_active[i] &&
                (_bypass_cooldown || t-hold_released_at[i]>=cfg.hold_release_cooldown)){
                if (hold_seen_at[i]>0 && t-hold_seen_at[i]>=cfg.hold_grace_sec)
                    hold_seen_at[i]=0;

                if (wc>=cfg.min_pixels){
                    bool is_hold = hold_incoming[i] ||
                                   (hold_seen_at[i]>0 && t-hold_seen_at[i]<cfg.hold_grace_sec);
                    if (is_hold){
                        key_press(i); st=HOLDING; hold_incoming[i]=false;
                        hold_seen_at[i]=0; hold_started_at[i]=t; note_pressed[i]=true;
                        hold_tap_last[i]=0;
                    } else {
                        // Tap note — density-aware Good/Sick decision
                        if (t - density_window[i] >= DENSITY_WINDOW) {
                            press_count[i]    = 0;
                            density_window[i] = t;
                        }
                        press_count[i]++;
                        bool is_dense   = press_count[i] >= DENSITY_HIGH;
                        double eff_good = is_dense ? GOOD_DENSE : GOOD_SPARSE;
                        bool is_jack    = (t-last_pressed_at[i]) < cfg.jack_threshold;

                        if (!is_jack && chance_dist(rng)<eff_good){
                            double d = delay_dist(rng);
                            pending_press_at[i] = t + d;
                            pending_active[i]   = true;
                            note_pressed[i]     = true;
                        } else {
                            key_press(i);
                            tap_release_at[i]  = t + cfg.tap_key_duration;
                            last_pressed_at[i] = t;
                            st=TAPPED; note_pressed[i]=true;
                        }
                    }
                }
            }

            // ── TAPPED ───────────────────────────────────────────────────────
            // Overlap: zone stays white past one-note duration = second note arrived
            if (st==TAPPED && wc>=cfg.min_pixels &&
                white_since[i]>0 && t-white_since[i]>=OVERLAP_THRESHOLD &&
                tap_release_at[i]==0){
                key_press(i);
                tap_release_at[i]  = t + cfg.tap_key_duration;
                last_pressed_at[i] = t;
                note_pressed[i]    = true;
                white_since[i]     = t;
                st = TAPPED;
            }
            // Cleanup: note gone and key released
            if (st==TAPPED && wc<cfg.min_pixels && tap_release_at[i]==0){
                st=IDLE; hold_incoming[i]=false; hold_seen_at[i]=0;
            }

            if (wc<cfg.min_pixels && note_visible[i])
                { note_visible[i]=false; note_pressed[i]=false; }
        }
    }

    release_all();
}

// ─── ENTRY POINT ─────────────────────────────────────────────────────────────

int main(){
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    timer_init();

    Config cfg;
    char buf[MAX_PATH]={};
    GetModuleFileNameA(NULL,buf,MAX_PATH);
    std::string dir(buf);
    auto sl=dir.find_last_of("\\/");
    if (sl!=std::string::npos) dir=dir.substr(0,sl);

    bool loaded=load_coords(cfg, dir+"\\coords.txt");
    std::cout<<"larpLOL C++ | 30% Good / 70% Sick\n";
    std::cout<<(loaded?"coords.txt loaded\n":"coords.txt not found — using defaults\n");
    std::cout<<"Press ENTER to start...\n";
    std::cin.get();

    g_running=true; g_active=true;
    run_macro(cfg);
    return 0;
}
