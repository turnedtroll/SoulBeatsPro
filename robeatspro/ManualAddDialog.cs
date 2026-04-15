namespace SoulBeatsPro;

/// <summary>
/// Modal for the Calibration tab's "Manual Add" button. Lets the user pick a lane
/// and capture a single pixel via ScreenPicker. The chosen color and lane are
/// exposed as PickedColor and SelectedLane after DialogResult.OK.
/// </summary>
internal sealed class ManualAddDialog : Form
{
    private readonly ComboBox _laneCombo;
    private readonly Button _pickBtn;
    private readonly Panel _swatch;
    private readonly Label _pickedLabel;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;

    public Color? PickedColor { get; private set; }
    public int SelectedLane => _laneCombo.SelectedIndex;

    public ManualAddDialog()
    {
        Text = "Manual Add Color";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(280, 160);
        Font = new Font("MS Sans Serif", 8.25f);

        var laneLbl = new Label
        {
            Text = "Lane:",
            AutoSize = true,
            Location = new Point(12, 16)
        };
        Controls.Add(laneLbl);

        _laneCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(60, 12),
            Size = new Size(80, 22)
        };
        _laneCombo.Items.AddRange(new object[] { "1 (Z)", "2 (X)", "3 (,)", "4 (.)" });
        _laneCombo.SelectedIndex = 0;
        Controls.Add(_laneCombo);

        _pickBtn = new Button
        {
            Text = "Pick Pixel",
            Location = new Point(12, 50),
            Size = new Size(100, 26)
        };
        _pickBtn.Click += PickBtn_Click;
        Controls.Add(_pickBtn);

        _swatch = new Panel
        {
            Location = new Point(124, 50),
            Size = new Size(40, 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };
        Controls.Add(_swatch);

        _pickedLabel = new Label
        {
            Text = "(no pixel picked)",
            AutoSize = true,
            Location = new Point(12, 86),
            ForeColor = Color.Gray
        };
        Controls.Add(_pickedLabel);

        _okBtn = new Button
        {
            Text = "Add",
            DialogResult = DialogResult.OK,
            Location = new Point(110, 120),
            Size = new Size(75, 26),
            Enabled = false
        };
        Controls.Add(_okBtn);
        AcceptButton = _okBtn;

        _cancelBtn = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(193, 120),
            Size = new Size(75, 26)
        };
        Controls.Add(_cancelBtn);
        CancelButton = _cancelBtn;
    }

    private void PickBtn_Click(object? sender, EventArgs e)
    {
        // Hide while picking so the picker can capture the underlying screen.
        Hide();
        try
        {
            using var picker = new ScreenPicker();
            if (picker.ShowDialog() == DialogResult.OK && picker.PickedColor != null)
            {
                PickedColor = picker.PickedColor;
                _swatch.BackColor = PickedColor.Value;
                _pickedLabel.Text = $"R={PickedColor.Value.R} G={PickedColor.Value.G} B={PickedColor.Value.B}  ±12";
                _pickedLabel.ForeColor = Color.Black;
                _okBtn.Enabled = true;
            }
        }
        finally
        {
            Show();
            BringToFront();
        }
    }
}
