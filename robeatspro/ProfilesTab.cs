namespace SoulBeatsPro;

internal sealed class ProfilesTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private readonly ListBox _list;
    private readonly Button _addBtn;
    private readonly Button _duplicateBtn;
    private readonly Button _deleteBtn;
    private readonly Button _renameBtn;
    private readonly Button _activateBtn;
    private readonly ComboBox _accuracyCombo;
    private readonly NumericUpDown _maxJudgmentInput;
    private readonly Label _judgmentLabel;

    public event Action? ActiveProfileChanged;

    public ProfilesTab()
    {
        Dock = DockStyle.Fill;
        Font = RetroFont;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        var grpList = new GroupBox
        {
            Text = "Profiles",
            Location = new Point(12, 8),
            Size = new Size(360, 240),
            Font = RetroFont,
        };
        _list = new ListBox
        {
            Location = new Point(10, 22),
            Size = new Size(220, 200),
            Font = RetroFont,
        };
        _list.SelectedIndexChanged += (_, _) => RefreshButtons();
        grpList.Controls.Add(_list);

        _activateBtn = MakeButton("Set active", new Point(238, 22));
        _activateBtn.Click += (_, _) => ActivateSelected();
        grpList.Controls.Add(_activateBtn);

        _addBtn = MakeButton("+ Add", new Point(238, 58));
        _addBtn.Click += (_, _) => AddProfile();
        grpList.Controls.Add(_addBtn);

        _duplicateBtn = MakeButton("Duplicate", new Point(238, 94));
        _duplicateBtn.Click += (_, _) => DuplicateSelected();
        grpList.Controls.Add(_duplicateBtn);

        _renameBtn = MakeButton("Rename", new Point(238, 130));
        _renameBtn.Click += (_, _) => RenameSelected();
        grpList.Controls.Add(_renameBtn);

        _deleteBtn = MakeButton("Delete", new Point(238, 166));
        _deleteBtn.Click += (_, _) => DeleteSelected();
        grpList.Controls.Add(_deleteBtn);

        Controls.Add(grpList);

        var grpAccuracy = new GroupBox
        {
            Text = "Active profile settings",
            Location = new Point(12, 260),
            Size = new Size(360, 100),
            Font = RetroFont,
        };
        var accLabel = new Label { Text = "Accuracy:", Location = new Point(10, 26), AutoSize = true };
        grpAccuracy.Controls.Add(accLabel);
        _accuracyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(78, 22),
            Size = new Size(160, 22),
            Font = RetroFont,
        };
        _accuracyCombo.Items.AddRange(AccuracyPresetTable.GenericLabels);
        _accuracyCombo.SelectedIndexChanged += (_, _) => AccuracyChanged();
        grpAccuracy.Controls.Add(_accuracyCombo);

        _judgmentLabel = new Label { Text = "Safe window (ms):", Location = new Point(10, 58), AutoSize = true };
        grpAccuracy.Controls.Add(_judgmentLabel);
        _maxJudgmentInput = new NumericUpDown
        {
            Location = new Point(130, 54),
            Size = new Size(70, 22),
            Font = RetroFont,
            Minimum = 30, Maximum = 300, DecimalPlaces = 0, Increment = 5,
        };
        _maxJudgmentInput.ValueChanged += (_, _) => JudgmentChanged();
        grpAccuracy.Controls.Add(_maxJudgmentInput);

        Controls.Add(grpAccuracy);

        RefreshList();
    }

    private static Button MakeButton(string text, Point loc) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Standard,
        Font = RetroFont,
        Size = new Size(110, 28),
        Location = loc,
    };

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in ConfigManager.Instance.Profiles)
        {
            string label = p.Name;
            if (p.Name == ConfigManager.Instance.GameMode.ActiveProfileName) label = "★ " + label;
            if (p.IsBuiltIn) label += " [built-in]";
            _list.Items.Add(label);
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
        RefreshButtons();
        RefreshActiveSettings();
    }

    private string? GetSelectedName()
    {
        int idx = _list.SelectedIndex;
        if (idx < 0) return null;
        return ConfigManager.Instance.Profiles[idx].Name;
    }

    private void RefreshButtons()
    {
        var name = GetSelectedName();
        var p = name == null ? null : ConfigManager.Instance.Profiles.Find(x => x.Name == name);
        _deleteBtn.Enabled = p != null && !p.IsBuiltIn;
        _renameBtn.Enabled = p != null;
        _duplicateBtn.Enabled = p != null;
        _activateBtn.Enabled = p != null && name != ConfigManager.Instance.GameMode.ActiveProfileName;
    }

    private void RefreshActiveSettings()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        _accuracyCombo.SelectedIndex = (int)p.AccuracyPreset;
        _maxJudgmentInput.Value = (decimal)Math.Clamp(p.MaxJudgmentMs, 30, 300);
    }

    private void ActivateSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        ConfigManager.Instance.SetActiveProfile(name);
        ActiveProfileChanged?.Invoke();
        RefreshList();
    }

    private void AddProfile()
    {
        var name = InputBox.Show("New profile name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { ConfigManager.Instance.AddProfile(name.Trim()); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void DuplicateSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        var newName = InputBox.Show($"Name for copy of '{name}':", defaultText: name + " copy");
        if (string.IsNullOrWhiteSpace(newName)) return;
        try { ConfigManager.Instance.DuplicateProfile(name, newName.Trim()); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void RenameSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        var newName = InputBox.Show($"Rename '{name}' to:", defaultText: name);
        if (string.IsNullOrWhiteSpace(newName) || newName == name) return;
        var p = ConfigManager.Instance.Profiles.Find(x => x.Name == name)!;
        p.Name = newName.Trim();
        if (ConfigManager.Instance.GameMode.ActiveProfileName == name)
            ConfigManager.Instance.GameMode.ActiveProfileName = newName.Trim();
        ConfigManager.Instance.SaveSettings();
        RefreshList();
    }

    private void DeleteSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try { ConfigManager.Instance.DeleteProfile(name); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void AccuracyChanged()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        p.AccuracyPreset = (AccuracyPreset)_accuracyCombo.SelectedIndex;
        ConfigManager.Instance.SaveSettings();
    }

    private void JudgmentChanged()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        p.MaxJudgmentMs = (double)_maxJudgmentInput.Value;
        ConfigManager.Instance.SaveSettings();
    }
}

/// <summary>Minimal modal input dialog (WinForms has none built in).</summary>
internal static class InputBox
{
    public static string? Show(string prompt, string defaultText = "")
    {
        using var f = new Form
        {
            Text = prompt,
            Width = 340, Height = 140,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
        };
        var tb = new TextBox { Left = 10, Top = 10, Width = 300, Text = defaultText };
        var ok = new Button { Text = "OK", Left = 140, Top = 50, DialogResult = DialogResult.OK };
        var ca = new Button { Text = "Cancel", Left = 225, Top = 50, DialogResult = DialogResult.Cancel };
        f.AcceptButton = ok; f.CancelButton = ca;
        f.Controls.AddRange(new Control[] { tb, ok, ca });
        return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }
}
