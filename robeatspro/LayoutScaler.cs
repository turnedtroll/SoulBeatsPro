namespace SoulBeatsPro;

/// <summary>
/// Records each non-docked control's design-time bounds and font size,
/// then proportionally rescales everything when <see cref="Apply"/> is called.
/// Uses uniform scaling: sf = min(widthRatio, heightRatio).
/// </summary>
internal sealed class LayoutScaler
{
    private readonly record struct Snap(Rectangle Bounds, float FontSize, bool AutoSized);
    private readonly Dictionary<Control, Snap> _map = new();
    private readonly Size _design;

    private static readonly Dictionary<(string, float, FontStyle), Font> Fonts = new();

    public LayoutScaler(Control root)
    {
        _design = root.ClientSize;
        Capture(root);
    }

    private void Capture(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c.Dock != DockStyle.None) continue;
            _map[c] = new Snap(
                c.Bounds,
                c.Font.Size,
                c is Label { AutoSize: true } or CheckBox { AutoSize: true });
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            if (c.HasChildren && c is GroupBox or Panel or UserControl)
                Capture(c);
        }
    }

    public void Apply(Control root)
    {
        if (_design.Width <= 0 || _design.Height <= 0) return;
        float sx = (float)root.ClientSize.Width / _design.Width;
        float sy = (float)root.ClientSize.Height / _design.Height;
        float sf = Math.Min(sx, sy);
        if (sf < 0.1f) return;

        root.SuspendLayout();
        foreach (var (ctrl, snap) in _map)
        {
            if (ctrl.IsDisposed) continue;
            int x = (int)(snap.Bounds.X * sf);
            int y = (int)(snap.Bounds.Y * sf);
            if (snap.AutoSized)
                ctrl.Location = new Point(x, y);
            else
                ctrl.SetBounds(x, y,
                    (int)(snap.Bounds.Width * sf),
                    (int)(snap.Bounds.Height * sf));

            float fs = MathF.Round(Math.Max(6f, snap.FontSize * sf) * 4) / 4;
            if (MathF.Abs(ctrl.Font.Size - fs) > 0.2f)
                ctrl.Font = GetFont(ctrl.Font.FontFamily.Name, fs, ctrl.Font.Style);
        }
        root.ResumeLayout(true);
    }

    internal static Font GetFont(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (!Fonts.TryGetValue(key, out var f))
            Fonts[key] = f = new Font(family, size, style);
        return f;
    }
}
