namespace SoulBeatsPro;

/// <summary>
/// TabPage that paints the shared background image stretched across the full area,
/// with a semi-transparent color overlay controlled by PanelAlpha for readability.
/// </summary>
internal sealed class ThemedTabPage : TabPage
{
    public ThemedTabPage(string text) : base(text)
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var theme = ConfigManager.Instance.Theme;

        // 1. Base color fill
        using (var baseBrush = new SolidBrush(theme.GetWindowBg()))
            e.Graphics.FillRectangle(baseBrush, ClientRectangle);

        // 2. Background image (stretched to fill)
        var img = ConfigManager.CachedBackgroundImage;
        if (img != null)
        {
            e.Graphics.DrawImage(img, 0, 0, ClientSize.Width, ClientSize.Height);

            // 3. Semi-transparent overlay so controls remain readable
            using var overlayBrush = new SolidBrush(theme.GetTabBgAlpha());
            e.Graphics.FillRectangle(overlayBrush, ClientRectangle);
        }
        else
        {
            // No image — just solid tab background
            using var solidBrush = new SolidBrush(theme.GetTabBg());
            e.Graphics.FillRectangle(solidBrush, ClientRectangle);
        }
    }
}
