namespace SoulBeatsPro;

/// <summary>
/// Live, editable view of the active profile's per-lane color signatures.
/// Each entry has a swatch, [×] delete button, tolerance slider, and [L] badge
/// when learned. Per-lane [+ Add color] uses ScreenPicker. [Reset learned]
/// removes only learned entries; [Clear signatures] removes everything.
/// </summary>
internal sealed class ColorsTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private static readonly Font HeaderFont = new("MS Sans Serif", 10f, FontStyle.Bold);
    private static readonly Font BadgeFont = new("MS Sans Serif", 7f, FontStyle.Bold);

    // Debounce SaveSettings while dragging the tolerance slider so we don't
    // hit disk on every value change.
    private readonly System.Windows.Forms.Timer _saveDebounce;

    public ColorsTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        Dock = DockStyle.Fill;
        AutoScroll = true;

        _saveDebounce = new System.Windows.Forms.Timer { Interval = 200 };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            ConfigManager.Instance.SaveSettings();
        };

        VisibleChanged += (_, _) => { if (Visible) RenderSignatures(); };
        ConfigManager.Instance.ProfileSignaturesChanged += RenderSignatures;
        RenderSignatures();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ConfigManager.Instance.ProfileSignaturesChanged -= RenderSignatures;
            _saveDebounce.Dispose();
        }
        base.Dispose(disposing);
    }

    private void RenderSignatures()
    {
        if (InvokeRequired) { BeginInvoke(new Action(RenderSignatures)); return; }

        SuspendLayout();
        Controls.Clear();

        var profile = ConfigManager.Instance.ActiveProfile;
        int y = 12;

        Controls.Add(new Label
        {
            Text = $"Active: {profile.Name}",
            AutoSize = true,
            Location = new Point(12, y),
            Font = HeaderFont
        });
        y += 28;

        const int laneLabelW = 56;
        const int entryW = 56;
        const int entryH = 60;
        const int addBtnW = 80;

        for (int lane = 0; lane < 4; lane++)
        {
            int laneIdx = lane;
            Controls.Add(new Label
            {
                Text = $"Lane {lane + 1}:",
                AutoSize = true,
                Location = new Point(12, y + 4),
                Font = RetroFont
            });

            int x = 12 + laneLabelW;
            var sig = profile.Signatures[lane];

            if (sig.Entries.Count == 0)
            {
                Controls.Add(new Label
                {
                    Text = "(no entries)",
                    AutoSize = true,
                    Location = new Point(x, y + 4),
                    Font = RetroFont,
                    ForeColor = Color.FromArgb(180, 180, 180)
                });
                x += 90;
            }
            else
            {
                for (int e = 0; e < sig.Entries.Count; e++)
                {
                    int entryIdx = e;
                    AddEntryControls(sig.Entries[e], laneIdx, entryIdx, x, y);
                    x += entryW;
                }
            }

            var addBtn = new Button
            {
                Text = "+ Add",
                Size = new Size(addBtnW, 24),
                Location = new Point(x + 4, y + 6),
                Font = RetroFont
            };
            addBtn.Click += (_, _) => AddPickedColorToLane(laneIdx);
            Controls.Add(addBtn);

            y += entryH;
        }

        y += 8;

        var resetBtn = new Button
        {
            Text = "Reset learned",
            Location = new Point(12, y),
            Size = new Size(140, 26),
            Font = RetroFont
        };
        resetBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Remove all learned signature entries on the active profile?\n(Manual entries are preserved.)",
                    "Reset learned", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var s in profile.Signatures)
                s.Entries.RemoveAll(en => en.Learned);
            ConfigManager.Instance.SaveSettings();
            ConfigManager.Instance.NotifySignaturesChanged();
        };
        Controls.Add(resetBtn);

        var clearBtn = new Button
        {
            Text = "Clear signatures",
            Location = new Point(160, y),
            Size = new Size(140, 26),
            Font = RetroFont
        };
        clearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear ALL signatures on the active profile?", "Clear signatures",
                    MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var s in profile.Signatures) s.Entries.Clear();
            ConfigManager.Instance.SaveSettings();
            ConfigManager.Instance.NotifySignaturesChanged();
        };
        Controls.Add(clearBtn);

        ResumeLayout();
    }

    private void AddEntryControls(ColorSignatureEntry entry, int lane, int entryIndex, int x, int y)
    {
        var swatch = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(40, 24),
            BackColor = Color.FromArgb(entry.R, entry.G, entry.B),
            BorderStyle = BorderStyle.FixedSingle
        };
        new ToolTip().SetToolTip(swatch, $"R={entry.R} G={entry.G} B={entry.B}  ±{entry.Tolerance}{(entry.Learned ? "  [learned]" : "  [manual]")}");

        var deleteBtn = new Button
        {
            Text = "x",
            Size = new Size(14, 14),
            Location = new Point(26, 0),
            Font = BadgeFont,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 80, 80),
            ForeColor = Color.White,
            Margin = Padding.Empty,
            TabStop = false
        };
        deleteBtn.FlatAppearance.BorderSize = 0;
        deleteBtn.Click += (_, _) => DeleteEntry(lane, entryIndex);
        swatch.Controls.Add(deleteBtn);

        if (entry.Learned)
        {
            var badge = new Label
            {
                Text = "L",
                AutoSize = false,
                Size = new Size(12, 12),
                Location = new Point(0, 12),
                Font = BadgeFont,
                BackColor = Color.FromArgb(80, 160, 255),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter
            };
            swatch.Controls.Add(badge);
        }

        Controls.Add(swatch);

        var slider = new TrackBar
        {
            Minimum = 0,
            Maximum = 80,
            TickStyle = TickStyle.None,
            Value = Math.Clamp(entry.Tolerance, 0, 80),
            Location = new Point(x - 4, y + 26),
            Size = new Size(52, 28)
        };
        slider.ValueChanged += (_, _) =>
        {
            entry.Tolerance = slider.Value;
            // Immediate in-memory mutation so detection responds live; debounce
            // disk save so we don't thrash while dragging.
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };
        Controls.Add(slider);
    }

    private void DeleteEntry(int lane, int entryIndex)
    {
        var sig = ConfigManager.Instance.ActiveProfile.Signatures[lane];
        if (entryIndex < 0 || entryIndex >= sig.Entries.Count) return;
        sig.Entries.RemoveAt(entryIndex);
        ConfigManager.Instance.SaveSettings();
        ConfigManager.Instance.NotifySignaturesChanged();
    }

    private void AddPickedColorToLane(int lane)
    {
        Color? picked = null;
        using (var picker = new ScreenPicker())
        {
            if (picker.ShowDialog(this) == DialogResult.OK && picker.PickedColor != null)
                picked = picker.PickedColor;
        }
        if (picked == null) return;

        var c = picked.Value;
        var sig = ConfigManager.Instance.ActiveProfile.Signatures[lane];
        sig.Entries.Add(new ColorSignatureEntry(c.R, c.G, c.B, tolerance: 12, learned: false));
        ConfigManager.Instance.SaveSettings();
        ConfigManager.Instance.NotifySignaturesChanged();
    }
}
