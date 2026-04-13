/*
 * larpLOL_variation.cpp  —  Funky Friday macro, 30% Good / 70% Sick with density detection
 *
 * Build:
 *   g++ -O2 -std=c++17 -o larpLOL_variation.exe larpLOL_variation.cpp -lgdi32 -luser32 -lkernel32
 *
 * Handles:
 *   - Tap notes (instant press)
 *   - Hold notes (held until tail clears)
 *   - Tap→Tap overlap (two taps close together on same lane)
 *   - Hold→Hold overlap (second hold queued while first is active)
 *   - Hold→Tap overlap (tap arriving during a hold — skipped, can't register without breaking hold)
 *   - Run-style streams (rapid same-lane repeats at any scroll speed)
 *
 * Controls: L = pause/resume
 */

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <iostream>
#include <fstream>
#include <string>
#include <array>
#include <cstring>
#include <cwchar>
#include <algorithm>
#include <atomic>
#include <random>

#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "kernel32.lib")

// ─── TIMING ──────────────────────────────────────────────────────────────────

static LARGE_INTEGER g_qpf;
static void timer_init() { QueryPerformanceFrequency(&g_qpf); }
static double now_sec() {
    LARGE_INTEGER c; QueryPerformanceCounter(&c);
    return (double)c.QuadPart / (double)g_qpf.QuadPart;
}
static void spin_sleep(double s) {
    if (s <= 0.0) return;
    double end = now_sec() + s;
    double bulk = s - 0.001;
    if (bulk > 0.0) Sleep((DWORD)(bulk * 1000.0));
    while (now_sec() < end) YieldProcessor();
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

    // How long to hold a tap key down
    double tap_key_duration      = 0.030;
    // Minimum gap between presses on same lane (prevents double-fire)
    double press_cooldown        = 0.018;
    // Grace window: if hold-gray was seen within this time, treat white as hold
    double hold_grace_sec        = 0.120;
    // Safety: force-release holds longer than this
    double max_hold_duration     = 8.0;
    // How long the tap zone stays white for ONE note at current scroll speed.
    // If white stays longer than this, a second note overlapped → fire again.
    double overlap_threshold     = 0.080;
    // How long with zero pixels before we consider a hold done (no-tail fallback)
    double hold_gone_ms          = 0.055;
    // Pause toggle debounce
    double toggle_delay          = 0.3;

    // Good variation
    double good_chance     = 0.30;  // base Good chance
    double good_delay_min  = 0.052; // 52ms into Good window
    double good_delay_max  = 0.062; // 62ms
    double jack_threshold  = 0.150; // same lane within 150ms = jack, skip Good
    // Density: if lane fires >= DENSITY_HIGH times in DENSITY_WIN seconds,
    // drop Good to good_dense to avoid misses on bursts
    double density_win     = 0.500;
    int    density_high    = 4;
    double good_dense      = 0.04;
};

// ─── KEYS ────────────────────────────────────────────────────────────────────

static const WORD SCAN[4] = { 0x2C, 0x2D, 0x33, 0x34 }; // z x , .

static void send_key(int lane, bool up) {
    INPUT inp; memset(&inp, 0, sizeof(inp));
    inp.type       = INPUT_KEYBOARD;
    inp.ki.wScan   = SCAN[lane];
    inp.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
    SendInput(1, &inp, sizeof(INPUT));
}
static void key_press  (int i) { send_key(i, false); }
static void key_release(int i) { send_key(i, true);  }

// ─── SCREEN ──────────────────────────────────────────────────────────────────

struct Screen {
    int left=0,top=0,w=0,h=0;
    HDC hdc_s=nullptr, hdc_m=nullptr;
    HBITMAP hbm=nullptr;
    RGBQUAD* bits=nullptr;
    bool ready=false;

    bool init(int l,int t,int width,int height){
        left=l;top=t;w=width;h=height;
        hdc_s=GetDC(NULL); if(!hdc_s) return false;
        hdc_m=CreateCompatibleDC(hdc_s); if(!hdc_m){ReleaseDC(NULL,hdc_s);return false;}
        BITMAPINFO bmi={};
        bmi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth=w; bmi.bmiHeader.biHeight=-h;
        bmi.bmiHeader.biPlanes=1; bmi.bmiHeader.biBitCount=32;
        bmi.bmiHeader.biCompression=BI_RGB;
        void* raw=nullptr;
        hbm=CreateDIBSection(hdc_m,&bmi,DIB_RGB_COLORS,&raw,NULL,0);
        if(!hbm||!raw){DeleteDC(hdc_m);ReleaseDC(NULL,hdc_s);return false;}
        bits=(RGBQUAD*)raw;
        SelectObject(hdc_m,hbm);
        ready=true; return true;
    }
    void capture(){ BitBlt(hdc_m,0,0,w,h,hdc_s,left,top,SRCCOPY); GdiFlush(); }
    inline RGBQUAD get(int x,int y) const {
        if((unsigned)x>=(unsigned)w||(unsigned)y>=(unsigned)h) return {};
        return bits[y*w+x];
    }
    ~Screen(){ if(!ready)return; DeleteObject(hbm);DeleteDC(hdc_m);ReleaseDC(NULL,hdc_s); }
};

static int count_white(const Screen& s,int cx,int cy,int half,int wmin){
    int n=0;
    for(int dy=-half;dy<=half;dy++) for(int dx=-half;dx<=half;dx++){
        RGBQUAD p=s.get(cx+dx,cy+dy);
        if(p.rgbRed>=wmin&&p.rgbGreen>=wmin&&p.rgbBlue>=wmin) n++;
    }
    return n;
}
static int count_gray(const Screen& s,int cx,int cy,int half,int gmin,int gmax){
    int n=0;
    for(int dy=-half;dy<=half;dy++) for(int dx=-half;dx<=half;dx++){
        RGBQUAD p=s.get(cx+dx,cy+dy);
        if(p.rgbRed>=gmin&&p.rgbRed<=gmax&&
           p.rgbGreen>=gmin&&p.rgbGreen<=gmax&&
           p.rgbBlue>=gmin&&p.rgbBlue<=gmax) n++;
    }
    return n;
}

// ─── ROBLOX FOCUS ────────────────────────────────────────────────────────────

static bool is_roblox_focused(){
    HWND hwnd=GetForegroundWindow(); if(!hwnd) return false;
    DWORD pid=0; GetWindowThreadProcessId(hwnd,&pid);
    HANDLE h=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid); if(!h) return false;
    wchar_t buf[MAX_PATH]={}; DWORD sz=MAX_PATH;
    QueryFullProcessImageNameW(h,0,buf,&sz); CloseHandle(h);
    for(int i=0;buf[i];i++) buf[i]=(wchar_t)towlower(buf[i]);
    return wcsstr(buf,L"roblox")!=nullptr;
}

// ─── COORD LOAD ──────────────────────────────────────────────────────────────

static bool load_coords(Config& cfg, const std::string& path){
    std::ifstream f(path); if(!f.is_open()) return false;
    std::string src,line; while(std::getline(f,line)) src+=line;
    auto parse=[&](const std::string& key,std::array<int,4>& ax,std::array<int,4>& ay){
        size_t pos=src.find("\""+key+"\""); if(pos==std::string::npos) return;
        pos=src.find('[',pos);
        for(int i=0;i<4;i++){
            pos=src.find('[',pos+1); if(pos==std::string::npos) return;
            size_t comma=src.find(',',pos), close=src.find(']',pos);
            if(comma==std::string::npos||close==std::string::npos) return;
            try{ ax[i]=std::stoi(src.substr(pos+1)); ay[i]=std::stoi(src.substr(comma+1)); }
            catch(...){ return; }
            pos=close;
        }
    };
    parse("tap", cfg.tap_x, cfg.tap_y);
    parse("hold",cfg.hold_x,cfg.hold_y);
    size_t off=src.find("timing_offset");
    if(off!=std::string::npos){
        size_t c=src.find(':',off);
        if(c!=std::string::npos) try{ cfg.timing_offset=std::stoi(src.substr(c+1)); }catch(...){}
    }
    return true;
}

// ─── MACRO ───────────────────────────────────────────────────────────────────

static std::atomic<bool> g_running;
static std::atomic<bool> g_active;

static void run_macro(Config cfg){
    for(int i=0;i<4;i++){ cfg.tap_y[i]-=cfg.timing_offset; cfg.hold_y[i]-=cfg.timing_offset; }

    int mn_x=9999,mn_y=9999,mx_x=0,mx_y=0;
    for(int i=0;i<4;i++){
        mn_x=std::min(mn_x,std::min(cfg.tap_x[i],cfg.hold_x[i]));
        mn_y=std::min(mn_y,std::min(cfg.tap_y[i],cfg.hold_y[i]));
        mx_x=std::max(mx_x,std::max(cfg.tap_x[i],cfg.hold_x[i]));
        mx_y=std::max(mx_y,std::max(cfg.tap_y[i],cfg.hold_y[i]));
    }
    int sh=cfg.sample_half;
    int cl=mn_x-sh-1,ct=mn_y-sh-1,cw=(mx_x+sh+1)-cl,ch=(mx_y+sh+1)-ct;
    std::array<int,4> trx,try_,hrx,hry;
    for(int i=0;i<4;i++){
        trx[i]=cfg.tap_x[i]-cl; try_[i]=cfg.tap_y[i]-ct;
        hrx[i]=cfg.hold_x[i]-cl; hry[i]=cfg.hold_y[i]-ct;
    }

    Screen sc; if(!sc.init(cl,ct,cw,ch)){ std::cerr<<"Screen init failed\n"; return; }

    // ── Per-lane state ────────────────────────────────────────────────────────
    enum State { IDLE, TAPPED, HOLDING };
    std::array<State, 4> states       = {IDLE,IDLE,IDLE,IDLE};

    // Tap state
    std::array<double,4> tap_rel_at   = {0,0,0,0}; // when to release tap key
    std::array<double,4> press_last   = {0,0,0,0}; // last press time (cooldown)
    std::array<double,4> white_since  = {0,0,0,0}; // when tap zone first went white
    std::array<bool,  4> was_white    = {false,false,false,false};

    // Hold state
    std::array<double,4> hold_start   = {0,0,0,0};
    std::array<double,4> hold_rel_at  = {0,0,0,0}; // cooldown after hold ends
    std::array<double,4> hold_seen_at = {0,0,0,0}; // when hold-gray first seen
    std::array<bool,  4> hold_queued  = {false,false,false,false}; // next hold queued
    std::array<double,4> hold_q_at    = {0,0,0,0}; // when queued hold was detected
    std::array<double,4> hold_last_px = {0,0,0,0}; // last frame any hold pixels seen
    // Tail tracking: we watch for a SUSTAINED gray at the tap zone (not a flicker)
    // to distinguish the hold tail from the hold body passing through
    std::array<double,4> tail_seen_at = {0,0,0,0}; // when tap-zone gray first appeared
    std::array<bool,  4> tail_confirmed={false,false,false,false};
    // Tail requires gray to persist for at least TAIL_CONFIRM_MS before we trust it
    constexpr double TAIL_CONFIRM  = 0.012; // 12ms — filters out body flicker
    constexpr double TAIL_CLEAR_MS = 0.008; // 8ms clear after tail seen = release

    // Good variation state
    std::array<double,4> pending_at  = {0,0,0,0}; // scheduled Good press time
    std::array<bool,  4> pending     = {false,false,false,false};
    std::array<double,4> press_count_win = {0,0,0,0}; // density window start
    std::array<int,   4> press_count = {0,0,0,0};

    std::mt19937 rng(std::random_device{}());
    std::uniform_real_distribution<double> rchance(0.0,1.0);
    std::uniform_real_distribution<double> rdelay(cfg.good_delay_min,cfg.good_delay_max);

    double last_toggle=0, focus_check=0;
    bool roblox_focus=false;

    auto do_release_hold=[&](int i, double t){
        key_release(i);
        states[i]=IDLE;
        hold_start[i]=0; hold_rel_at[i]=t;
        hold_seen_at[i]=0; hold_last_px[i]=0;
        tail_seen_at[i]=0; tail_confirmed[i]=false;
        // preserve hold_queued so IDLE picks it up immediately
    };

    auto release_all=[&](){
        for(int i=0;i<4;i++){
            if(states[i]==HOLDING) key_release(i);
            if(tap_rel_at[i]>0) key_release(i);
            states[i]=IDLE; tap_rel_at[i]=0; press_last[i]=0;
            white_since[i]=0; was_white[i]=false;
            hold_start[i]=0; hold_rel_at[i]=0; hold_seen_at[i]=0;
            hold_queued[i]=false; hold_q_at[i]=0; hold_last_px[i]=0;
            tail_seen_at[i]=0; tail_confirmed[i]=false;
            pending_at[i]=0; pending[i]=false;
        }
    };

    std::cout<<"[larpLOL_variation] Running | L = pause/resume\n";

    while(g_running.load()){
        double t=now_sec();

        if((GetAsyncKeyState(0x4C)&0x8000)&&t-last_toggle>cfg.toggle_delay){
            g_active.store(!g_active.load()); last_toggle=t;
            if(!g_active.load()){ release_all(); std::cout<<"Paused\n"; }
            else std::cout<<"Resumed\n";
        }
        if(!g_active.load()){ Sleep(10); continue; }

        if(t-focus_check>0.2){ roblox_focus=is_roblox_focused(); focus_check=t; }
        if(!roblox_focus){ release_all(); Sleep(50); continue; }

        // Fire pending Good presses
        for(int i=0;i<4;i++)
            if(pending[i] && t>=pending_at[i]){
                key_press(i);
                tap_rel_at[i]=t+cfg.tap_key_duration;
                press_last[i]=t; pending[i]=false; pending_at[i]=0;
            }

        // Non-blocking tap releases
        for(int i=0;i<4;i++)
            if(tap_rel_at[i]>0 && t>=tap_rel_at[i])
                { key_release(i); tap_rel_at[i]=0; }

        // Safety force-release stuck holds
        for(int i=0;i<4;i++)
            if(states[i]==HOLDING && hold_start[i]>0 && t-hold_start[i]>=cfg.max_hold_duration)
                do_release_hold(i,t);

        sc.capture();

        for(int i=0;i<4;i++){
            int wc  = count_white(sc,trx[i],try_[i],sh,cfg.white_min);
            int tgc = count_gray (sc,trx[i],try_[i],sh,cfg.gray_min,cfg.gray_max);
            int hgc = count_gray (sc,hrx[i],hry[i],sh,cfg.gray_min,cfg.gray_max);
            bool hhw= count_white(sc,hrx[i],hry[i],sh,cfg.white_min)>0;
            State& st=states[i];

            // ── Track continuous white for tap-overlap detection ───────────
            if(wc>=cfg.min_pixels){
                if(!was_white[i]) white_since[i]=t;
                was_white[i]=true;
            } else {
                was_white[i]=false; white_since[i]=0;
            }

            // ── Detect incoming hold (gray at hold zone, no white there yet) ──
            // Only queue when NOT already holding on this lane
            if(hgc>=cfg.min_pixels && !hhw){
                if(st==HOLDING){
                    // Second hold arriving during first — queue it
                    if(!hold_queued[i]){ hold_queued[i]=true; hold_q_at[i]=t; }
                } else if(st==IDLE){
                    // Pre-detection: hold body arriving before tap zone lights up
                    hold_seen_at[i]=t;
                }
                // TAPPED: ignore — fading gray from previous hold or unrelated
            }

            // ════════════════════════════════════════════════════════════════
            // HOLDING
            // ════════════════════════════════════════════════════════════════
            if(st==HOLDING){
                bool any_px=(wc>0||tgc>0||hgc>0);
                if(any_px) hold_last_px[i]=t;

                bool released=false;

                // ── Tail detection ────────────────────────────────────────
                // Tail = gray at TAP zone sustained for TAIL_CONFIRM ms.
                // Filters out brief gray from the hold body passing through.
                if(tgc>=cfg.min_pixels){
                    if(tail_seen_at[i]==0) tail_seen_at[i]=t;
                    if(!tail_confirmed[i] && t-tail_seen_at[i]>=TAIL_CONFIRM)
                        tail_confirmed[i]=true;
                } else {
                    if(tail_confirmed[i]){
                        // Confirmed tail has cleared — release
                        do_release_hold(i,t);
                        released=true;
                    } else {
                        tail_seen_at[i]=0; // flicker, reset
                    }
                }

                // ── No-tail fallback ──────────────────────────────────────
                if(!released && !tail_confirmed[i]){
                    if(hold_last_px[i]>0 && t-hold_last_px[i]>=cfg.hold_gone_ms){
                        do_release_hold(i,t);
                        released=true;
                    }
                }
                (void)released; // suppress unused warning
            }

            // ════════════════════════════════════════════════════════════════
            // IDLE
            // ════════════════════════════════════════════════════════════════
            if(st==IDLE){
                // Expire old hold_seen_at grace window
                if(hold_seen_at[i]>0 && t-hold_seen_at[i]>=cfg.hold_grace_sec)
                    hold_seen_at[i]=0;

                // Bypass cooldown if a queued hold is waiting
                bool bypass = hold_queued[i] && wc>=cfg.min_pixels;
                bool cooldown_ok = bypass || (t-hold_rel_at[i]>=cfg.press_cooldown);

                if(wc>=cfg.min_pixels && cooldown_ok){
                    // Determine if this is a hold or a tap
                    bool is_hold = hold_queued[i] ||
                                   hold_seen_at[i]>0;
                                   // NOTE: hgc alone is NOT enough — fading hold body
                                   // gray lingers after release and causes false holds

                    if(is_hold){
                        key_press(i);
                        st=HOLDING; hold_start[i]=t;
                        hold_seen_at[i]=0; hold_queued[i]=false; hold_q_at[i]=0;
                        hold_last_px[i]=t; tail_seen_at[i]=0; tail_confirmed[i]=false;
                        press_last[i]=t;
                    } else if(t-press_last[i]>=cfg.press_cooldown){
                        // Tap — decide Good or Sick
                        if(t-press_count_win[i]>=cfg.density_win){
                            press_count[i]=0; press_count_win[i]=t;
                        }
                        press_count[i]++;
                        bool dense   = press_count[i]>=cfg.density_high;
                        double eff_g = dense ? cfg.good_dense : cfg.good_chance;
                        bool is_jack = (t-press_last[i])<cfg.jack_threshold;
                        if(!is_jack && rchance(rng)<eff_g && !pending[i]){
                            // Good: schedule delayed press, state stays IDLE
                            pending_at[i]=t+rdelay(rng);
                            pending[i]=true;
                            press_last[i]=t; // mark pressed for jack detection
                        } else {
                            key_press(i);
                            tap_rel_at[i]=t+cfg.tap_key_duration;
                            press_last[i]=t;
                            st=TAPPED;
                        }
                    }
                }
            }

            // ════════════════════════════════════════════════════════════════
            // TAPPED
            // ════════════════════════════════════════════════════════════════
            if(st==TAPPED){
                // Overlap: tap zone stayed white longer than one note
                // = second note arrived while we were still in TAPPED
                if(wc>=cfg.min_pixels &&
                   white_since[i]>0 && t-white_since[i]>=cfg.overlap_threshold &&
                   tap_rel_at[i]==0 && t-press_last[i]>=cfg.press_cooldown){
                    key_press(i);
                    tap_rel_at[i]  = t+cfg.tap_key_duration;
                    press_last[i]  = t;
                    white_since[i] = t; // reset so we don't keep firing
                    // stay TAPPED
                }
                // Cleanup once note gone and key released
                if(wc<cfg.min_pixels && tap_rel_at[i]==0){
                    st=IDLE;
                    hold_seen_at[i]=0;
                    // don't clear hold_queued here — may have been set during tap
                }
            }
        }
    }
    release_all();
}

// ─── ENTRY POINT ─────────────────────────────────────────────────────────────

int main(){
    SetPriorityClass(GetCurrentProcess(),HIGH_PRIORITY_CLASS);
    timer_init();
    Config cfg;
    char buf[MAX_PATH]={}; GetModuleFileNameA(NULL,buf,MAX_PATH);
    std::string dir(buf);
    auto sl=dir.find_last_of("\\/");
    if(sl!=std::string::npos) dir=dir.substr(0,sl);
    bool loaded=load_coords(cfg,dir+"\\coords.txt");
    std::cout<<"larpLOL_variation | 30% Good / 70% Sick\n";
    std::cout<<(loaded?"coords.txt loaded\n":"coords.txt not found — using defaults\n");
    std::cout<<"Press ENTER to start...\n";
    std::cin.get();
    g_running.store(true); g_active.store(true);
    run_macro(cfg);
    return 0;
}
