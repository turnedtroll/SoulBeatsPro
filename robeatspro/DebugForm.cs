using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// Debug overlay — shows live lane states, pixel counts, and detection boxes.
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

        bool wgMode = _engine.WhiteGrayMode;
        string noteLabel = wgMode ? "W" : "N";
        string holdLabel = wgMode ? "G" : "H";

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

            for (int i = 0; i < 4; i++)
            {
                var col = MacroEngine.LaneColors[i];
                var state = _engine.States[i];
                string keyDisp = NativeApi.DisplayName(laneKeys[i]);

                // Tap point
                int txD = PanelWidth + (int)((_engine.TapPixels[i].X - _monitorBounds.Left) * Scale);
                int tyD = (int)((_engine.TapPixels[i].Y - _monitorBounds.Top) * Scale);
                txD = Math.Clamp(txD, PanelWidth + BoxSize, PanelWidth + imgW - BoxSize - 1);
                tyD = Math.Clamp(tyD, BoxSize, totalH - BoxSize - 1);

                Color tapCol; int tapThick;
                if (state == MacroEngine.LaneState.Tapped)
                    { tapCol = Color.Yellow; tapThick = 3; }
                else if (state == MacroEngine.LaneState.Holding)
                    { tapCol = Color.Lime; tapThick = 3; }
                else if (_engine.NoteCounts[i] >= 3)
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

                if (state != MacroEngine.LaneState.Idle)
                    g.DrawString(state.ToString().ToUpper(), lblFont,
                        new SolidBrush(tapCol), txD - BoxSize, tyD + BoxSize + 2);

                // Hold point
                int hxD = PanelWidth + (int)((_engine.HoldPixels[i].X - _monitorBounds.Left) * Scale);
                int hyD = (int)((_engine.HoldPixels[i].Y - _monitorBounds.Top) * Scale);
                hxD = Math.Clamp(hxD, PanelWidth + BoxSize, PanelWidth + imgW - BoxSize - 1);
                hyD = Math.Clamp(hyD, BoxSize, totalH - BoxSize - 1);

                Color hldCol = _engine.HoldZoneCounts[i] >= 3 ? Color.Orange : col;
                int hldThick = _engine.HoldZoneCounts[i] >= 3 ? 2 : 1;

                using (var p = new Pen(hldCol, hldThick))
                {
                    g.DrawRectangle(p, hxD - BoxSize, hyD - BoxSize, BoxSize * 2, BoxSize * 2);
                    p.Width = 1;
                    g.DrawRectangle(p, hxD - shD, hyD - shD, shD * 2, shD * 2);
                }

                g.DrawString($"H{keyDisp}", lblFont,
                    new SolidBrush(hldCol), hxD - BoxSize, hyD - BoxSize - 13);
            }

            // Left panel
            using var panelFont = new Font("Consolas", 8f);
            using var headerFont = new Font("Consolas", 8f, FontStyle.Bold);

            int row = 18;
            string gameMode = wgMode ? "Funky Friday" : "RoBeats";
            var statusCol = _engine.Active ? Color.LimeGreen : Color.FromArgb(220, 80, 80);
            g.DrawString($"{(_engine.Active ? "ACTIVE" : "PAUSED")}    FPS: {_engine.Fps}",
                headerFont, new SolidBrush(statusCol), 6, row);
            row += 16;

            g.DrawString($"Mode: {gameMode}", panelFont, new SolidBrush(Color.FromArgb(180, 180, 255)), 6, row);
            row += 16;

            var kb = ConfigManager.Instance.Keybinds;
            g.DrawString($"{NativeApi.DisplayName(kb.Pause)}=pause  {NativeApi.DisplayName(kb.Debug)}=debug  {NativeApi.DisplayName(kb.Quit)}=quit",
                panelFont, new SolidBrush(Color.Gray), 6, row);
            row += 20;

            g.DrawString($" Ln  State  {noteLabel}  TH  HZ  Fl", headerFont,
                new SolidBrush(Color.FromArgb(255, 220, 80)), 6, row);
            row += 15;

            for (int i = 0; i < 4; i++)
            {
                var s = _engine.States[i];
                string sAbb = s switch
                {
                    MacroEngine.LaneState.Idle => "IDLE",
                    MacroEngine.LaneState.Tapped => "TAP ",
                    MacroEngine.LaneState.Holding => "HOLD",
                    _ => "????"
                };
                var sc = s != MacroEngine.LaneState.Idle ? MacroEngine.LaneColors[i] : Color.Gray;
                string fl = (_engine.HoldIncoming[i] ? "I" : "") + (_engine.HoldSawTail[i] ? "T" : "");
                string keyDisp = NativeApi.DisplayName(laneKeys[i]);

                g.DrawString(
                    $"  {keyDisp,-3} {sAbb}  {_engine.NoteCounts[i],2}  " +
                    $"{_engine.TapHoldCounts[i],2}  {_engine.HoldZoneCounts[i],2}  {fl}",
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

            string noteType = wgMode ? "white" : "note color";
            DrawLegend(Color.White, $"note arriving ({noteType})");
            DrawLegend(Color.Yellow, "key down (tap)");
            DrawLegend(Color.Lime, "key held (hold)");
            DrawLegend(Color.Orange, $"hold zone ({(wgMode ? "gray" : "hold color")})");

            row += 4;
            g.DrawString("I=incoming T=tail", panelFont, new SolidBrush(Color.DimGray), 6, row);
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
