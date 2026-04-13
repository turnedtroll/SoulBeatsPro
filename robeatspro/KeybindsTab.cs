using System.Runtime.InteropServices;

namespace SoulBeatsPro;

/// <summary>
/// UserControl for configuring lane and control keybinds.
/// Win95/98 retro style with GroupBox sections and low-level keyboard hook capture.
/// </summary>
internal sealed class KeybindsTab : UserControl
{
    // ── Lane key buttons (indices 0-3) ──────────────────────────
    private readonly Button[] _laneButtons = new Button[4];
    private readonly string[] _laneDefaults = ["Z", "X", "OEM_COMMA", "OEM_PERIOD"];
    private readonly string[] _laneLabels = ["Lane 1", "Lane 2", "Lane 3", "Lane 4"];

    // ── Control key buttons ─────────────────────────────────────
    private readonly Button _pauseBtn, _debugBtn, _screenshotBtn, _quitBtn;
    private readonly string[] _controlDefaults = ["L", "P", "F2", "ESCAPE"];
    private readonly string[] _controlLabels = ["Pause", "Debug", "Screenshot", "Quit"];

    // ── Hook state ──────────────────────────────────────────────
    private Button? _waitingButton;
    private string? _waitingTarget;          // "lane0"-"lane3" or "pause","debug","screenshot","quit"
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeApi.LowLevelKeyboardProc? _hookProc;   // prevent GC

    // ── Style ───────────────────────────────────────────────────
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private static readonly Color BgColor = ConfigManager.Instance.Theme.GetWindowBg();
    private LayoutScaler? _scaler;

    public KeybindsTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        AutoScroll = true;

        // ── Lane Keys GroupBox ──────────────────────────────────
        var laneGroup = new GroupBox
        {
            Text = "Lane Keys",
            Font = RetroFont,
            Location = new Point(12, 8),
            Size = new Size(320, 140),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var kb = ConfigManager.Instance.Keybinds;
            string currentKey = idx switch
            {
                0 => kb.Lane1,
                1 => kb.Lane2,
                2 => kb.Lane3,
                3 => kb.Lane4,
                _ => ""
            };

            string defaultDisplay = NativeApi.DisplayName(_laneDefaults[i]);
            var lbl = new Label
            {
                Text = $"{_laneLabels[i]} ({defaultDisplay}):",
                Font = RetroFont,
                AutoSize = true,
                Location = new Point(14, 24 + i * 28)
            };
            laneGroup.Controls.Add(lbl);

            var btn = new Button
            {
                Text = NativeApi.DisplayName(currentKey),
                Font = RetroFont,
                FlatStyle = FlatStyle.Standard,
                Size = new Size(80, 23),
                Location = new Point(220, 20 + i * 28),
                Tag = $"lane{idx}",
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btn.Click += KeyButton_Click;
            laneGroup.Controls.Add(btn);
            _laneButtons[i] = btn;
        }
        Controls.Add(laneGroup);

        // ── Control Keys GroupBox ───────────────────────────────
        var ctrlGroup = new GroupBox
        {
            Text = "Control Keys",
            Font = RetroFont,
            Location = new Point(12, 156),
            Size = new Size(320, 140),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        Button MakeControlBtn(string labelText, string currentKey, string tag, int row)
        {
            var lbl = new Label
            {
                Text = $"{labelText}:",
                Font = RetroFont,
                AutoSize = true,
                Location = new Point(14, 24 + row * 28)
            };
            ctrlGroup.Controls.Add(lbl);

            var btn = new Button
            {
                Text = NativeApi.DisplayName(currentKey),
                Font = RetroFont,
                FlatStyle = FlatStyle.Standard,
                Size = new Size(80, 23),
                Location = new Point(220, 20 + row * 28),
                Tag = tag,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btn.Click += KeyButton_Click;
            ctrlGroup.Controls.Add(btn);
            return btn;
        }

        var kb2 = ConfigManager.Instance.Keybinds;
        _pauseBtn = MakeControlBtn(_controlLabels[0], kb2.Pause, "pause", 0);
        _debugBtn = MakeControlBtn(_controlLabels[1], kb2.Debug, "debug", 1);
        _screenshotBtn = MakeControlBtn(_controlLabels[2], kb2.Screenshot, "screenshot", 2);
        _quitBtn = MakeControlBtn(_controlLabels[3], kb2.Quit, "quit", 3);

        Controls.Add(ctrlGroup);

        // ── Hint label ──────────────────────────────────────────
        var hint = new Label
        {
            Text = "Click a box, then press a new key",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Location = new Point(12, 304)
        };
        Controls.Add(hint);

        // ── Reset button ────────────────────────────────────────
        var resetBtn = new Button
        {
            Text = "Reset to Defaults",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(130, 26),
            Location = new Point(12, 328)
        };
        resetBtn.Click += ResetButton_Click;
        Controls.Add(resetBtn);
    }

    // ── Button click: start listening ───────────────────────────

    private void KeyButton_Click(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;

        // Cancel any previous wait
        CancelWait();

        _waitingButton = btn;
        _waitingTarget = btn.Tag as string;
        btn.Text = "Press a key...";
        btn.BackColor = Color.FromArgb(255, 255, 200);

        InstallHook();
    }

    // ── Low-level keyboard hook ─────────────────────────────────

    private void InstallHook()
    {
        _hookProc = HookCallback;
        _hookHandle = NativeApi.SetWindowsHookEx(
            NativeApi.WH_KEYBOARD_LL,
            _hookProc,
            NativeApi.GetModuleHandle(null),
            0);
    }

    private void RemoveHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeApi.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeApi.WM_KEYDOWN && _waitingButton != null)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            string keyName = NativeApi.NameFromVk(vkCode);

            // Marshal back to UI thread
            BeginInvoke(() => ApplyKey(keyName));

            // Eat the keystroke so it doesn't propagate
            return (IntPtr)1;
        }

        return NativeApi.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ── Apply captured key ──────────────────────────────────────

    private void ApplyKey(string keyName)
    {
        if (_waitingButton == null || _waitingTarget == null) return;

        var btn = _waitingButton;
        var target = _waitingTarget;

        CancelWait();

        var kb = ConfigManager.Instance.Keybinds;

        bool isLane = false;
        if (target.StartsWith("lane") && int.TryParse(target.AsSpan(4), out int laneIdx))
        {
            kb.SetLane(laneIdx, keyName);
            isLane = true;
        }
        else
        {
            switch (target)
            {
                case "pause": kb.Pause = keyName; break;
                case "debug": kb.Debug = keyName; break;
                case "screenshot": kb.Screenshot = keyName; break;
                case "quit": kb.Quit = keyName; break;
            }
        }

        btn.Text = NativeApi.DisplayName(keyName);
        btn.BackColor = ConfigManager.Instance.Theme.GetButtonFace();

        ConfigManager.Instance.SaveSettings();

        if (isLane)
            NativeApi.UpdateLaneScans(kb.LaneKeys);
    }

    // ── Cancel wait state ───────────────────────────────────────

    private void CancelWait()
    {
        RemoveHook();

        if (_waitingButton != null)
        {
            // Restore the button text from current config if we're cancelling
            RestoreButtonText(_waitingButton, _waitingTarget);
            _waitingButton.BackColor = ConfigManager.Instance.Theme.GetButtonFace();
            _waitingButton = null;
            _waitingTarget = null;
        }
    }

    private void RestoreButtonText(Button btn, string? target)
    {
        if (target == null) return;
        var kb = ConfigManager.Instance.Keybinds;

        string key = target switch
        {
            "lane0" => kb.Lane1,
            "lane1" => kb.Lane2,
            "lane2" => kb.Lane3,
            "lane3" => kb.Lane4,
            "pause" => kb.Pause,
            "debug" => kb.Debug,
            "screenshot" => kb.Screenshot,
            "quit" => kb.Quit,
            _ => ""
        };
        btn.Text = NativeApi.DisplayName(key);
    }

    // ── Reset to defaults ───────────────────────────────────────

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        CancelWait();

        var kb = ConfigManager.Instance.Keybinds;

        // Reset lanes
        for (int i = 0; i < 4; i++)
        {
            kb.SetLane(i, _laneDefaults[i]);
            _laneButtons[i].Text = NativeApi.DisplayName(_laneDefaults[i]);
        }

        // Reset control keys
        kb.Pause = _controlDefaults[0];
        kb.Debug = _controlDefaults[1];
        kb.Screenshot = _controlDefaults[2];
        kb.Quit = _controlDefaults[3];

        _pauseBtn.Text = NativeApi.DisplayName(_controlDefaults[0]);
        _debugBtn.Text = NativeApi.DisplayName(_controlDefaults[1]);
        _screenshotBtn.Text = NativeApi.DisplayName(_controlDefaults[2]);
        _quitBtn.Text = NativeApi.DisplayName(_controlDefaults[3]);

        ConfigManager.Instance.SaveSettings();
        NativeApi.UpdateLaneScans(kb.LaneKeys);
    }

    // ── Proportional scaling ────────────────────────────────────

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_scaler == null)
        {
            if (ClientSize.Width > 300 && ClientSize.Height > 200)
                _scaler = new LayoutScaler(this);
        }
        else
        {
            _scaler.Apply(this);
        }
    }

    // ── Cleanup ─────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            RemoveHook();
        base.Dispose(disposing);
    }
}
