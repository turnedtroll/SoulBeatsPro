using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// Debug overlay — shows live lane states, signature match counts, and detection boxes.
internal sealed class DebugForm : Form
{
    private new const float Scale = 0.5f;
    private const int BoxSize = 18;
    private const int PanelWidth = 235;
    private const int SampleHalf = 3;

    private readonly MacroEngine _engine;
    private readonly PictureBox _pic;
    private readonly System.Windows.Forms.Timer _timer;
    private ScreenCapture? _capture;
    private Rectangle _monitorBounds;

    public DebugForm(MacroEngine engine)
    {
        _engine = engine;

        Text = "SoulBeats Pro — Debug";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;

        _monitorBounds = FindMonitorForPoints();

        int dw = (int)(_monitorBounds.Width * Scale) + PanelWidth;
        int dh = Math.Max((int)(_monitorBounds.Height * Scale), 280);
        ClientSize = new Size(dw, dh);

        _pic = new PictureBox { Dock = DockStyle.Fill };
        Controls.Add(_pic);

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private Rectangle FindMonitorForPoints()
    {
        if (_engine.TapPixels == null) return Screen.PrimaryScreen!.Bounds;

        int cx = (_engine.TapPixels.Min(p => p.X) + _engine.TapPixels.Max(p => p.X)) / 2;
        int cy = (_engine.TapPixels.Min(p => p.Y) + _engine.HoldPixels.Max(p => p.Y)) / 2;

        foreach (var scr in Screen.AllScreens)
            if (scr.Bounds.Contains(cx, cy))
                return scr.Bounds;

        return Screen.PrimaryScreen!.Bounds;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_engine.Running) return;

        _capture ??= new ScreenCapture(
            _monitorBounds.Left, _monitorBounds.Top,
            _monitorBounds.Width, _monitorBounds.Height);

        try { _capture.Grab(); }
        catch { return; }

        int imgW = (int)(_monitorBounds.Width * Scale);
        int imgH = (int)(_monitorBounds.Height * Scale);
        int totalH = Math.Max(imgH, 280);

        var disp = new Bitmap(PanelWidth + imgW, totalH);
        using (var g = Graphics.FromImage(disp))
        {
            g.Clear(Color.Black);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Right side: screen capture
            using var darkAttr = new ImageAttributes();
            float[][] dm =
            [
                [0.7f, 0, 0, 0, 0],
                [0, 0.7f, 0, 0, 0],
                [0, 0, 0.7f, 0, 0],
                [0, 0, 0, 1, 0],
                [0, 0, 0, 0, 1]
            ];
            darkAttr.SetColorMatrix(new ColorMatrix(dm));
            g.DrawImage(_capture.Bitmap,
                new Rectangle(PanelWidth, 0, imgW, imgH),
                0, 0, _capture.Width, _capture.Height,
                GraphicsUnit.Pixel, darkAttr);

            int shD = Math.Max(1, (int)(SampleHalf * Scale));
            var laneKeys = ConfigManager.Instance.Keybinds.LaneKeys;
            var pending = _engine.PendingScheduled;
            var tapPts = _engine.TapPixels;
            int tapCount = tapPts?.Length ?? 0;
            int laneCount = Math.Min(_engine.States.Length, tapCount > 0 ? tapCount : 4);

            for (int i = 0; i < laneCount; i++)
            {
                var col = i < MacroEngine.LaneColors.Length ? MacroEngine.LaneColors[i] : Color.Gray;
                var state = _engine.States[i];
                string keyDisp = i < laneKeys.Length ? NativeApi.DisplayName(laneKeys[i]) : $"{i + 1}";

                // Tap point — skip overlay if no pixel coordinates for this lane
                if (tapPts == null || i >= tapPts.Length) continue;
                int txD = PanelWidth + (int)((tapPts[i].X - _monitorBounds.Left) * Scale);
                int tyD = (int)((tapPts[i].Y - _monitorBounds.Top) * Scale);
                txD = Math.Clamp(txD, PanelWidth + BoxSize, PanelWidth + imgW - BoxSize - 1);
                tyD = Math.Clamp(tyD, BoxSize, totalH - BoxSize - 1);

                Color tapCol; int tapThick;
                if (state == MacroEngine.LaneState.Pressing)
                    { tapCol = Color.LimeGreen; tapThick = 3; }
                else if (i < _engine.MatchCounts.Length && _engine.MatchCounts[i] >= 3)
                    { tapCol = Color.White; tapThick = 2; }
                else
                    { tapCol = col; tapThick = 1; }

                using (var p = new Pen(tapCol, tapThick))
                {
                    g.DrawRectangle(p, txD - BoxSize, tyD - BoxSize, BoxSize * 2, BoxSize * 2);
                    p.Width = 1;
                    g.DrawRectangle(p, txD - shD, tyD - shD, shD * 2, shD * 2);
                }

                using var lblFont = new Font("Segoe UI", 6.5f);
                g.DrawString($"T{keyDisp}", lblFont,
                    new SolidBrush(tapCol), txD - BoxSize, tyD - BoxSize - 13);

                if (state != MacroEngine.LaneState.Released)
                    g.DrawString(state.ToString().ToUpper(), lblFont,
                        new SolidBrush(tapCol), txD - BoxSize, tyD + BoxSize + 2);
                else if (i < pending.Length && pending[i])
                    g.DrawString("DELAYED", lblFont,
                        new SolidBrush(Color.FromArgb(255, 200, 80)), txD - BoxSize, tyD + BoxSize + 2);
            }

            // Left panel
            using var panelFont = new Font("Consolas", 8f);
            using var headerFont = new Font("Consolas", 8f, FontStyle.Bold);

            int row = 18;
            string profileName = ConfigManager.Instance.ActiveProfile.Name;
            var statusCol = _engine.Active ? Color.LimeGreen : Color.FromArgb(220, 80, 80);
            g.DrawString($"{(_engine.Active ? "ACTIVE" : "PAUSED")}    FPS: {_engine.Fps}",
                headerFont, new SolidBrush(statusCol), 6, row);
            row += 16;

            g.DrawString($"Profile: {profileName}", panelFont, new SolidBrush(Color.FromArgb(180, 180, 255)), 6, row);
            row += 16;

            var kb = ConfigManager.Instance.Keybinds;
            g.DrawString($"{NativeApi.DisplayName(kb.Pause)}=pause  {NativeApi.DisplayName(kb.Debug)}=debug  {NativeApi.DisplayName(kb.Quit)}=quit",
                panelFont, new SolidBrush(Color.Gray), 6, row);
            row += 20;

            g.DrawString(" Ln  State    Match  Sched", headerFont,
                new SolidBrush(Color.FromArgb(255, 220, 80)), 6, row);
            row += 15;

            for (int i = 0; i < laneCount; i++)
            {
                var s = _engine.States[i];
                string sAbb = s == MacroEngine.LaneState.Pressing ? "PRESS" : "RELES";
                var sc = s == MacroEngine.LaneState.Pressing ? Color.LimeGreen : Color.Gray;
                int matchCount = i < _engine.MatchCounts.Length ? _engine.MatchCounts[i] : 0;
                bool isPending = i < pending.Length && pending[i];
                string sched = isPending ? "delayed" : "";
                string keyDisp = i < laneKeys.Length ? NativeApi.DisplayName(laneKeys[i]) : $"{i + 1}";

                g.DrawString(
                    $"  {keyDisp,-3} {sAbb}    {matchCount,3}  {sched}",
                    panelFont, new SolidBrush(sc), 6, row);
                row += 15;
            }

            row += 10;
            g.DrawString("--- box colors ---", panelFont, new SolidBrush(Color.Gray), 6, row);
            row += 16;

            void DrawLegend(Color c, string text)
            {
                g.FillRectangle(new SolidBrush(c), 6, row - 2, 10, 10);
                g.DrawString(text, panelFont, Brushes.LightGray, 20, row - 3);
                row += 15;
            }

            DrawLegend(Color.White, "signature match (>=3 px)");
            DrawLegend(Color.LimeGreen, "key pressing");
        }

        var old = _pic.Image;
        _pic.Image = disp;
        old?.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _capture?.Dispose();
        base.OnFormClosed(e);
    }
}
