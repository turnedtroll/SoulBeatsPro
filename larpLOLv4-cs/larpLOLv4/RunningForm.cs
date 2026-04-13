namespace larpLOLv4;

/// Small status window while the macro is running (no debug overlay).
/// P = open debug window, L = pause/resume (handled by engine), ESC = quit.
internal sealed class RunningForm : Form
{
    private readonly MacroEngine _engine;
    private readonly Label _statusLabel;
    private readonly Label _fpsLabel;
    private readonly System.Windows.Forms.Timer _timer;
    private DebugForm? _debugForm;
    private double _lastPToggle;

    public RunningForm(MacroEngine engine)
    {
        _engine = engine;

        Text = "Rhythm Macro — Running";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 26, 26);
        ClientSize = new Size(300, 150);
        KeyPreview = true;

        var title = new Label
        {
            Text = "Macro Running",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(300, 30),
            Location = new Point(0, 14)
        };
        Controls.Add(title);

        _statusLabel = new Label
        {
            Text = "ACTIVE",
            ForeColor = Color.LimeGreen,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(300, 24),
            Location = new Point(0, 48)
        };
        Controls.Add(_statusLabel);

        _fpsLabel = new Label
        {
            Text = "FPS: --",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(300, 20),
            Location = new Point(0, 72)
        };
        Controls.Add(_fpsLabel);

        var helpLabel = new Label
        {
            Text = "L = pause/resume    P = debug    ESC = quit",
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(300, 20),
            Location = new Point(0, 105)
        };
        Controls.Add(helpLabel);

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        engine.OnStopped += () =>
        {
            if (InvokeRequired) BeginInvoke(Close);
            else Close();
        };
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        // Update status
        if (_engine.Active)
        {
            _statusLabel.Text = "ACTIVE";
            _statusLabel.ForeColor = Color.LimeGreen;
        }
        else
        {
            _statusLabel.Text = "PAUSED";
            _statusLabel.ForeColor = Color.FromArgb(220, 80, 80);
        }

        _fpsLabel.Text = $"FPS: {_engine.Fps}";

        // P key to toggle debug (using GetAsyncKeyState for global detection)
        double now = Environment.TickCount64 / 1000.0;
        if (NativeApi.IsKeyDown(NativeApi.VK_P) && now - _lastPToggle > 0.3)
        {
            _lastPToggle = now;
            if (_debugForm == null || _debugForm.IsDisposed)
            {
                _debugForm = new DebugForm(_engine);
                _debugForm.Show();
            }
            else
            {
                _debugForm.Close();
                _debugForm = null;
            }
        }

        // ESC to quit
        if (NativeApi.IsKeyDown(NativeApi.VK_ESCAPE))
        {
            _engine.Stop();
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _debugForm?.Close();
        _engine.Stop();
        base.OnFormClosed(e);
    }
}
