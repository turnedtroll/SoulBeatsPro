namespace SoulBeatsPro;

/// Tab for sending feedback to the developer's Discord webhook.
/// Messages are posted via TelemetryManager.SendFeedbackAsync and also
/// logged locally to %LOCALAPPDATA%\bigbart\telemetry\feedback.jsonl.
internal sealed class FeedbackTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);

    private readonly TextBox _txtMessage;
    private readonly Button _btnSend;
    private readonly Label _lblStatus;
    private readonly Label _lblCharCount;
    private LayoutScaler? _scaler;

    private const int MaxLength = 1500;

    public FeedbackTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        Dock = DockStyle.Fill;
        AutoScroll = true;

        // ── Intro ─────────────────────────────────────────────
        var lblIntro = new Label
        {
            Text = "Send feedback, bug reports, or feature requests.\n" +
                   "Your message is delivered straight to the developer.",
            Font = RetroFont,
            Location = new Point(12, 8),
            Size = new Size(420, 32),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(lblIntro);

        // ── Message GroupBox ──────────────────────────────────
        var grpMessage = new GroupBox
        {
            Text = "Your Message",
            Font = RetroFont,
            Location = new Point(12, 46),
            Size = new Size(420, 200),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _txtMessage = new TextBox
        {
            Multiline = true,
            Location = new Point(10, 20),
            Size = new Size(400, 150),
            Font = RetroFont,
            BorderStyle = BorderStyle.Fixed3D,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = MaxLength,
            AcceptsReturn = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
        };
        _txtMessage.TextChanged += (_, _) => UpdateCharCount();
        grpMessage.Controls.Add(_txtMessage);

        _lblCharCount = new Label
        {
            Text = $"0 / {MaxLength}",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            Location = new Point(340, 175),
            Size = new Size(70, 16),
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        grpMessage.Controls.Add(_lblCharCount);

        Controls.Add(grpMessage);

        // ── Send Button ───────────────────────────────────────
        _btnSend = new Button
        {
            Text = "Send Feedback",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(140, 26),
            Location = new Point(12, 256)
        };
        _btnSend.Click += BtnSend_Click;
        Controls.Add(_btnSend);

        // ── Status Label ──────────────────────────────────────
        _lblStatus = new Label
        {
            Text = "",
            Font = RetroFont,
            Location = new Point(158, 260),
            Size = new Size(270, 20),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(_lblStatus);
    }

    private void UpdateCharCount()
    {
        var len = _txtMessage.Text.Length;
        _lblCharCount.Text = $"{len} / {MaxLength}";
        _lblCharCount.ForeColor = len >= MaxLength
            ? Color.FromArgb(255, 120, 120)
            : Color.FromArgb(160, 160, 170);
    }

    private async void BtnSend_Click(object? sender, EventArgs e)
    {
        var message = _txtMessage.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            _lblStatus.ForeColor = Color.FromArgb(255, 120, 120);
            _lblStatus.Text = "Please type a message first.";
            return;
        }

        _btnSend.Enabled = false;
        _btnSend.Text = "Sending...";
        _lblStatus.ForeColor = Color.FromArgb(160, 160, 170);
        _lblStatus.Text = "";

        try
        {
            await TelemetryManager.Instance.SendFeedbackAsync(message);
            TelemetryManager.Instance.TrackFeature("feedback_sent");

            _lblStatus.ForeColor = Color.FromArgb(0x4C, 0xAF, 0x50);
            _lblStatus.Text = "Feedback sent — thanks!";
            _txtMessage.Clear();
        }
        catch
        {
            _lblStatus.ForeColor = Color.FromArgb(255, 120, 120);
            _lblStatus.Text = "Failed to send. Try again.";
        }
        finally
        {
            _btnSend.Enabled = true;
            _btnSend.Text = "Send Feedback";
        }
    }

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
}
