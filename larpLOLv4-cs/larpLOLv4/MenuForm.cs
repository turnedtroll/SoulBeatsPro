namespace larpLOLv4;

/// Main menu — Run Macro, Calibrate, with debug checkbox.
internal sealed class MenuForm : Form
{
    public enum Choice { None, Run, Calibrate }

    public Choice Result { get; private set; } = Choice.None;
    public bool StartDebug { get; private set; }

    private readonly CheckBox _debugCheck;

    public MenuForm()
    {
        Text = "Rhythm Macro";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 26, 26);
        ClientSize = new Size(300, 220);

        var title = new Label
        {
            Text = "Rhythm Macro",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.None,
            AutoSize = false,
            Size = new Size(300, 36),
            Location = new Point(0, 16)
        };
        Controls.Add(title);

        var subtitle = new Label
        {
            Text = "select a mode",
            ForeColor = Color.FromArgb(102, 102, 102),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(300, 20),
            Location = new Point(0, 50)
        };
        Controls.Add(subtitle);

        var runBtn = MakeButton("Run Macro", 78);
        runBtn.Click += (_, _) => Pick(Choice.Run);
        Controls.Add(runBtn);

        _debugCheck = new CheckBox
        {
            Text = "open debug window on start",
            ForeColor = Color.FromArgb(136, 136, 136),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(60, 118)
        };
        Controls.Add(_debugCheck);

        // Separator
        var sep = new Panel
        {
            BackColor = Color.FromArgb(51, 51, 51),
            Size = new Size(260, 1),
            Location = new Point(20, 148)
        };
        Controls.Add(sep);

        var calBtn = MakeButton("Calibrate Detection Zones", 162);
        calBtn.Click += (_, _) => Pick(Choice.Calibrate);
        Controls.Add(calBtn);
    }

    private Button MakeButton(string text, int y)
    {
        var btn = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 42, 42),
            ForeColor = Color.FromArgb(238, 238, 238),
            Font = new Font("Segoe UI", 10),
            Size = new Size(260, 36),
            Location = new Point(20, y),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 56, 56);
        return btn;
    }

    private void Pick(Choice choice)
    {
        Result = choice;
        StartDebug = _debugCheck.Checked;
        Close();
    }
}
