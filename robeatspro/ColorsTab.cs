namespace SoulBeatsPro;

/// <summary>
/// Read-only viewer for the active profile's per-lane color signatures.
/// Calibration is the authoritative capture path; this tab only displays
/// what was captured and offers a clear/re-calibrate escape hatch.
/// </summary>
internal sealed class ColorsTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private static readonly Font HeaderFont = new("MS Sans Serif", 10f, FontStyle.Bold);

    public ColorsTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        RenderSignatures();
    }

    private void RenderSignatures()
    {
        Controls.Clear();
        var profile = ConfigManager.Instance.ActiveProfile;

        int y = 12;
        var header = new Label
        {
            Text = $"Active: {profile.Name}",
            AutoSize = true,
            Location = new Point(12, y),
            Font = HeaderFont
        };
        Controls.Add(header);
        y += 28;

        for (int lane = 0; lane < 4; lane++)
        {
            var laneLabel = new Label
            {
                Text = $"Lane {lane + 1}:",
                AutoSize = true,
                Location = new Point(12, y + 4),
                Font = RetroFont
            };
            Controls.Add(laneLabel);

            var sig = profile.Signatures[lane];
            int x = 90;
            if (sig.Entries.Count == 0)
            {
                var empty = new Label
                {
                    Text = "(not calibrated)",
                    AutoSize = true,
                    Location = new Point(x, y + 4),
                    Font = RetroFont,
                    ForeColor = Color.FromArgb(180, 180, 180)
                };
                Controls.Add(empty);
            }
            else
            {
                for (int e = 0; e < sig.Entries.Count; e++)
                {
                    var entry = sig.Entries[e];
                    var sw = new Panel
                    {
                        Location = new Point(x, y),
                        Size = new Size(40, 22),
                        BackColor = Color.FromArgb(entry.R, entry.G, entry.B),
                        BorderStyle = BorderStyle.FixedSingle
                    };
                    var tip = new ToolTip();
                    tip.SetToolTip(sw, $"R={entry.R} G={entry.G} B={entry.B}  \u00B1{entry.Tolerance}");
                    Controls.Add(sw);
                    x += 50;
                }
            }
            y += 30;
        }

        var clearBtn = new Button
        {
            Text = "Clear signatures (re-calibrate)",
            Location = new Point(12, y + 10),
            Size = new Size(240, 28),
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard
        };
        clearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear all signatures on the active profile?", "Confirm",
                    MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var s in profile.Signatures) s.Entries.Clear();
            ConfigManager.Instance.SaveSettings();
            RenderSignatures();
        };
        Controls.Add(clearBtn);
    }
}
