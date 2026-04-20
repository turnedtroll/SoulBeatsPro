using System.Runtime.InteropServices;

namespace SoulBeatsPro;

/// <summary>
/// UserControl for configuring lane and control keybinds.
/// Sections stack vertically inside a scrollable FlowLayoutPanel; each section is a
/// GroupBox with a two-column TableLayoutPanel so labels/buttons always line up
/// regardless of the window size.
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

    // ── Mania key buttons (populated only on BeatmapFile profiles) ──
    private Button[]? _maniaButtons;

    // ── Style ───────────────────────────────────────────────────
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);

    public KeybindsTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;

        // Root scrollable flow panel — stacks sections vertically.
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
            BackColor = Color.Transparent
        };

        // ── Lane Keys GroupBox ──────────────────────────────────
        var laneGroup = BuildSection("Lane Keys");
        var laneTable = (TableLayoutPanel)laneGroup.Controls[0];

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
            _laneButtons[i] = AddKeyRow(laneTable, $"{_laneLabels[i]} ({defaultDisplay}):", currentKey, $"lane{idx}");
        }
        root.Controls.Add(laneGroup);

        // ── Control Keys GroupBox ───────────────────────────────
        var ctrlGroup = BuildSection("Control Keys");
        var ctrlTable = (TableLayoutPanel)ctrlGroup.Controls[0];

        var kb2 = ConfigManager.Instance.Keybinds;
        _pauseBtn      = AddKeyRow(ctrlTable, $"{_controlLabels[0]}:", kb2.Pause,      "pause");
        _debugBtn      = AddKeyRow(ctrlTable, $"{_controlLabels[1]}:", kb2.Debug,      "debug");
        _screenshotBtn = AddKeyRow(ctrlTable, $"{_controlLabels[2]}:", kb2.Screenshot, "screenshot");
        _quitBtn       = AddKeyRow(ctrlTable, $"{_controlLabels[3]}:", kb2.Quit,       "quit");
        root.Controls.Add(ctrlGroup);

        // ── osu!mania Keys + Songs Folder (BeatmapFile profiles only) ──
        var activeProfile = ConfigManager.Instance.ActiveProfile;
        if (activeProfile.DetectionMode == DetectionMode.BeatmapFile)
        {
            var maniaGroup = BuildSection("osu!mania Keys (key count auto-detected from beatmap)");
            var maniaTable = (TableLayoutPanel)maniaGroup.Controls[0];

            var maniaKeys = activeProfile.ManiaKeys;
            int slots = Math.Min(maniaKeys.Length, 10);
            _maniaButtons = new Button[slots];
            for (int i = 0; i < slots; i++)
                _maniaButtons[i] = AddKeyRow(maniaTable, $"Key {i + 1}:", maniaKeys[i], $"mania{i}");
            root.Controls.Add(maniaGroup);

            root.Controls.Add(BuildSongsFolderGroup(activeProfile));
        }

        // ── Hint + Reset button ─────────────────────────────────
        var hint = new Label
        {
            Text = "Click a box, then press a new key",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Margin = new Padding(4, 6, 4, 2)
        };
        root.Controls.Add(hint);

        var resetBtn = new Button
        {
            Text = "Reset to Defaults",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(4, 2, 4, 8)
        };
        resetBtn.Click += ResetButton_Click;
        root.Controls.Add(resetBtn);

        Controls.Add(root);
    }

    // ── Section builders ────────────────────────────────────────

    private static GroupBox BuildSection(string title)
    {
        var grp = new GroupBox
        {
            Text = title,
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
            Margin = new Padding(4, 4, 4, 8),
            MinimumSize = new Size(320, 0)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grp.Controls.Add(table);
        return grp;
    }

    private Button AddKeyRow(TableLayoutPanel table, string labelText, string currentKey, string tag)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text = labelText,
            Font = RetroFont,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 6, 12, 4)
        };
        table.Controls.Add(lbl, 0, row);

        var btn = new Button
        {
            Text = NativeApi.DisplayName(currentKey),
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(100, 23),
            Tag = tag,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 2, 2, 2)
        };
        btn.Click += KeyButton_Click;
        table.Controls.Add(btn, 1, row);
        return btn;
    }

    private GroupBox BuildSongsFolderGroup(Profile activeProfile)
    {
        var grp = new GroupBox
        {
            Text = "osu! Songs Folder & Converts",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
            Margin = new Padding(4, 4, 4, 8),
            MinimumSize = new Size(320, 0)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grp.Controls.Add(stack);

        // Row 0 – label
        var songsLbl = new Label
        {
            Text = "Songs folder (blank = default):",
            Font = RetroFont,
            AutoSize = true,
            Margin = new Padding(2, 4, 2, 2)
        };
        stack.Controls.Add(songsLbl, 0, 0);

        // Row 1 – textbox + Browse (inner two-column table)
        var pathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var songsBox = new TextBox
        {
            Font = RetroFont,
            Text = activeProfile.OsuSongsPath,
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 2, 6, 2)
        };
        pathRow.Controls.Add(songsBox, 0, 0);

        var browseBtn = new Button
        {
            Text = "Browse...",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(2)
        };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select your osu! Songs folder",
                SelectedPath = string.IsNullOrEmpty(songsBox.Text)
                    ? OsuMapDetector.DefaultSongsPath
                    : songsBox.Text
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                songsBox.Text = dlg.SelectedPath;
        };
        pathRow.Controls.Add(browseBtn, 1, 0);

        songsBox.Leave += (_, _) =>
        {
            ConfigManager.Instance.ActiveProfile.OsuSongsPath = songsBox.Text.Trim();
            ConfigManager.Instance.SaveSettings();
            OsuMapDetector.ClearCache();
        };
        stack.Controls.Add(pathRow, 0, 1);

        // Row 2 – "Convert key count:" label + NumericUpDown
        var convRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            BackColor = Color.Transparent
        };
        convRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        convRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var convLbl = new Label
        {
            Text = "Convert key count:",
            Font = RetroFont,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 6, 12, 2)
        };
        convRow.Controls.Add(convLbl, 0, 0);

        var convSpin = new NumericUpDown
        {
            Minimum = 4,
            Maximum = 10,
            DecimalPlaces = 0,
            Increment = 1,
            Value = Math.Clamp(activeProfile.ManiaConvertKeyCount, 4, 10),
            Width = 60,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 2, 2, 2)
        };
        convSpin.ValueChanged += (_, _) =>
        {
            ConfigManager.Instance.ActiveProfile.ManiaConvertKeyCount = (int)convSpin.Value;
            ConfigManager.Instance.SaveSettings();
            OsuMapDetector.ClearCache();
        };
        convRow.Controls.Add(convSpin, 1, 0);
        stack.Controls.Add(convRow, 0, 2);

        // Row 3 – hint
        var convHint = new Label
        {
            Text = "(must match osu!'s convert setting — cycle with F4 at song select)",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Margin = new Padding(2, 2, 2, 2)
        };
        stack.Controls.Add(convHint, 0, 3);

        // Row 4 – import button
        var importBtn = new Button
        {
            Text = "Import keys from osu!",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(2, 6, 2, 2)
        };
        importBtn.Click += (_, _) => ImportKeysFromOsu();
        stack.Controls.Add(importBtn, 0, 4);

        return grp;
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
        else if (target.StartsWith("mania") && int.TryParse(target.AsSpan(5), out int maniaIdx))
        {
            var profile = ConfigManager.Instance.ActiveProfile;
            if (maniaIdx >= 0 && maniaIdx < profile.ManiaKeys.Length)
            {
                profile.ManiaKeys[maniaIdx] = keyName;
            }
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

        string key;
        if (target.StartsWith("mania") && int.TryParse(target.AsSpan(5), out int mi))
        {
            var maniaKeys = ConfigManager.Instance.ActiveProfile.ManiaKeys;
            key = mi < maniaKeys.Length ? maniaKeys[mi] : "";
        }
        else
        {
            key = target switch
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
        }
        btn.Text = NativeApi.DisplayName(key);
    }

    // ── Import mania keys from osu!stable config ────────────────

    private void ImportKeysFromOsu()
    {
        var profile = ConfigManager.Instance.ActiveProfile;
        int kc = Math.Clamp(profile.ManiaConvertKeyCount, 1, 10);

        string? cfg = OsuConfigImporter.FindConfigFile(profile.OsuSongsPath);
        if (cfg == null)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Locate your osu!.<Username>.cfg",
                Filter = "osu! config|osu!.*.cfg|All files|*.*"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            cfg = dlg.FileName;
        }

        var bindings = OsuConfigImporter.ImportManiaBindings(cfg, kc);
        if (bindings == null)
        {
            MessageBox.Show(this,
                $"Couldn't find complete {kc}K mania bindings in:\n{cfg}\n\n" +
                "Confirm the convert key count matches what you use in osu!, " +
                "or open the .cfg and check that Mania" + kc + "K entries exist.",
                "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Ensure the array is large enough, then overwrite the first kc slots.
        if (profile.ManiaKeys.Length < kc)
        {
            var grown = new string[Math.Max(10, kc)];
            Array.Copy(profile.ManiaKeys, grown, profile.ManiaKeys.Length);
            profile.ManiaKeys = grown;
        }
        for (int i = 0; i < kc; i++)
            profile.ManiaKeys[i] = bindings[i];

        ConfigManager.Instance.SaveSettings();

        // Refresh displayed button text without rebuilding the tab.
        if (_maniaButtons != null)
        {
            for (int i = 0; i < kc && i < _maniaButtons.Length; i++)
                if (_maniaButtons[i] != null)
                    _maniaButtons[i].Text = NativeApi.DisplayName(bindings[i]);
        }

        MessageBox.Show(this,
            $"Imported {kc} mania keybinds from osu!stable.",
            "Import successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // ── Cleanup ─────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            RemoveHook();
        base.Dispose(disposing);
    }
}
