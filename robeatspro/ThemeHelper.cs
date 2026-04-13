namespace SoulBeatsPro;

/// <summary>
/// Recursively applies theme colors and modern flat styling to all controls.
/// Skips specialized controls (color swatches, picture boxes, etc.).
/// </summary>
internal static class ThemeHelper
{
    /// <summary>Apply the current theme to a form and all its descendants.</summary>
    public static void ApplyTheme(Form form)
    {
        var theme = ConfigManager.Instance.Theme;
        form.BackColor = theme.GetWindowBg();
        form.ForeColor = theme.GetTextColor();
        form.Font = theme.GetFont();
        ApplyToChildren(form, theme);
    }

    /// <summary>Apply theme to a control tree (does not change the root's own colors).</summary>
    public static void ApplyToChildren(Control parent, ThemeSettings? theme = null)
    {
        theme ??= ConfigManager.Instance.Theme;
        foreach (Control child in parent.Controls)
            ApplyToControl(child, theme);
    }

    private static void ApplyToControl(Control control, ThemeSettings theme)
    {
        // Skip color-swatch panels (small panels with 3D borders used for picking colors)
        if (control is Panel p && IsColorSwatch(p))
        {
            return; // keep its manually-set BackColor
        }

        switch (control)
        {
            case TabControl tabs:
                tabs.Font = theme.GetFont();
                // Style each tab page
                foreach (TabPage page in tabs.TabPages)
                    ApplyToControl(page, theme);
                return; // children handled via tab pages

            case TabPage or ThemedTabPage:
                // ThemedTabPage paints its own background; standard TabPage gets theme colors
                if (control is TabPage tp && control is not ThemedTabPage)
                    tp.BackColor = theme.GetTabBg();
                control.ForeColor = theme.GetTextColor();
                break;

            case Button btn:
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = theme.GetButtonFace();
                btn.ForeColor = theme.GetButtonText();
                btn.FlatAppearance.BorderColor = theme.GetButtonHighlight();
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor =
                    Blend(theme.GetButtonFace(), theme.GetAccentColor(), 0.3);
                btn.FlatAppearance.MouseDownBackColor = theme.GetAccentColor();
                btn.Font = theme.GetFont();
                btn.Cursor = Cursors.Hand;
                return; // buttons have no children

            case GroupBox grp:
                grp.ForeColor = theme.GetTextColor();
                grp.BackColor = theme.GetPanelBg();
                grp.Font = theme.GetFont();
                break;

            case Label lbl:
                lbl.ForeColor = theme.GetTextColor();
                lbl.Font = theme.GetFont();
                return;

            case TextBox txt:
                txt.BackColor = Darken(theme.GetWindowBg(), 0.15);
                txt.ForeColor = theme.GetTextColor();
                txt.BorderStyle = BorderStyle.FixedSingle;
                txt.Font = theme.GetFont();
                return;

            case ComboBox cbo:
                cbo.BackColor = Darken(theme.GetWindowBg(), 0.1);
                cbo.ForeColor = theme.GetTextColor();
                cbo.FlatStyle = FlatStyle.Flat;
                cbo.Font = theme.GetFont();
                return;

            case NumericUpDown nud:
                nud.BackColor = Darken(theme.GetWindowBg(), 0.15);
                nud.ForeColor = theme.GetTextColor();
                nud.Font = theme.GetFont();
                return;

            case CheckBox chk:
                chk.ForeColor = theme.GetTextColor();
                chk.Font = theme.GetFont();
                return;

            case RadioButton rb:
                rb.ForeColor = theme.GetTextColor();
                rb.Font = theme.GetFont();
                return;

            case TrackBar:
                return; // trackbars don't support custom colors well

            case PictureBox:
                return; // don't touch picture boxes

            case Panel panel:
                panel.BackColor = theme.GetPanelBg();
                break;

            default:
                control.ForeColor = theme.GetTextColor();
                break;
        }

        // Recurse into children
        foreach (Control child in control.Controls)
            ApplyToControl(child, theme);
    }

    /// <summary>Check if a panel is being used as a small color swatch.</summary>
    private static bool IsColorSwatch(Panel p)
    {
        return p.Width <= 30 && p.Height <= 30 && p.BorderStyle == BorderStyle.Fixed3D;
    }

    /// <summary>Blend two colors together.</summary>
    private static Color Blend(Color a, Color b, double ratio)
    {
        int r = (int)(a.R + (b.R - a.R) * ratio);
        int g = (int)(a.G + (b.G - a.G) * ratio);
        int bl = (int)(a.B + (b.B - a.B) * ratio);
        return Color.FromArgb(
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(bl, 0, 255));
    }

    /// <summary>Darken a color by a fraction (0..1).</summary>
    private static Color Darken(Color c, double amount)
    {
        double factor = 1.0 - amount;
        return Color.FromArgb(
            c.A,
            Math.Clamp((int)(c.R * factor), 0, 255),
            Math.Clamp((int)(c.G * factor), 0, 255),
            Math.Clamp((int)(c.B * factor), 0, 255));
    }
}
