using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoulBeatsAdmin;

/// <summary>
/// Password gate — opens the dashboard only if the correct password is entered.
/// Password is stored as a SHA-256 hash in the config directory.
/// On first run, the user is prompted to create a password.
/// </summary>
internal sealed class LoginForm : Form
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulBeatsAdmin");
    private static readonly string PasswordPath = Path.Combine(ConfigDir, "auth.json");

    public LoginForm()
    {
        Directory.CreateDirectory(ConfigDir);

        bool firstRun = !File.Exists(PasswordPath) || !HasStoredPassword();

        Text = firstRun ? "SoulBeats Admin — Set Password" : "SoulBeats Admin — Login";
        Size = new Size(360, firstRun ? 240 : 200);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(0x12, 0x12, 0x20);

        var title = new Label
        {
            Text = "SoulBeats Admin",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x4C, 0xAF, 0x50),
            Location = new Point(20, 16),
            AutoSize = true
        };
        Controls.Add(title);

        if (firstRun)
            BuildSetPasswordUI();
        else
            BuildLoginUI();
    }

    private void BuildLoginUI()
    {
        var lbl = new Label
        {
            Text = "Enter admin password:",
            ForeColor = Color.FromArgb(0xB0, 0xB0, 0xC0),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(20, 58),
            AutoSize = true
        };
        Controls.Add(lbl);

        var txtPw = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(20, 80),
            Size = new Size(300, 24),
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x40),
            ForeColor = Color.White
        };
        Controls.Add(txtPw);

        var btnLogin = new Button
        {
            Text = "Login",
            Location = new Point(20, 115),
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x4C, 0xAF, 0x50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnLogin.FlatAppearance.BorderColor = Color.FromArgb(0x66, 0xBB, 0x6A);

        var lblError = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(255, 120, 120),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(130, 122),
            AutoSize = true
        };
        Controls.Add(lblError);

        btnLogin.Click += (_, _) =>
        {
            if (VerifyPassword(txtPw.Text))
            {
                Hide();
                var dashboard = new DashboardForm();
                dashboard.FormClosed += (_, _) => Close();
                dashboard.Show();
            }
            else
            {
                lblError.Text = "Incorrect password.";
                txtPw.SelectAll();
                txtPw.Focus();
            }
        };
        Controls.Add(btnLogin);

        AcceptButton = btnLogin;
        ActiveControl = txtPw;
    }

    private void BuildSetPasswordUI()
    {
        var lbl = new Label
        {
            Text = "Create your admin password:",
            ForeColor = Color.FromArgb(0xB0, 0xB0, 0xC0),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(20, 58),
            AutoSize = true
        };
        Controls.Add(lbl);

        var txtPw = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(20, 80),
            Size = new Size(300, 24),
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x40),
            ForeColor = Color.White
        };
        Controls.Add(txtPw);

        var lblConfirm = new Label
        {
            Text = "Confirm password:",
            ForeColor = Color.FromArgb(0xB0, 0xB0, 0xC0),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(20, 110),
            AutoSize = true
        };
        Controls.Add(lblConfirm);

        var txtConfirm = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(20, 130),
            Size = new Size(300, 24),
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x40),
            ForeColor = Color.White
        };
        Controls.Add(txtConfirm);

        var btnSet = new Button
        {
            Text = "Set Password",
            Location = new Point(20, 165),
            Size = new Size(120, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x4C, 0xAF, 0x50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnSet.FlatAppearance.BorderColor = Color.FromArgb(0x66, 0xBB, 0x6A);

        var lblError = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(255, 120, 120),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(150, 172),
            AutoSize = true
        };
        Controls.Add(lblError);

        btnSet.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtPw.Text))
            {
                lblError.Text = "Password cannot be empty.";
                return;
            }
            if (txtPw.Text.Length < 4)
            {
                lblError.Text = "Password must be at least 4 characters.";
                return;
            }
            if (txtPw.Text != txtConfirm.Text)
            {
                lblError.Text = "Passwords do not match.";
                txtConfirm.SelectAll();
                txtConfirm.Focus();
                return;
            }

            SavePasswordHash(txtPw.Text);
            Hide();
            var dashboard = new DashboardForm();
            dashboard.FormClosed += (_, _) => Close();
            dashboard.Show();
        };
        Controls.Add(btnSet);

        AcceptButton = btnSet;
        ActiveControl = txtPw;
    }

    // ── Password hashing ──────────────────────────────────────

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void SavePasswordHash(string password)
    {
        var json = JsonSerializer.Serialize(new { passwordHash = HashPassword(password) });
        File.WriteAllText(PasswordPath, json);
    }

    private static bool HasStoredPassword()
    {
        try
        {
            var json = File.ReadAllText(PasswordPath);
            var doc = JsonDocument.Parse(json);
            var hash = doc.RootElement.GetProperty("passwordHash").GetString();
            return !string.IsNullOrWhiteSpace(hash);
        }
        catch { return false; }
    }

    private static bool VerifyPassword(string password)
    {
        try
        {
            var json = File.ReadAllText(PasswordPath);
            var doc = JsonDocument.Parse(json);
            var storedHash = doc.RootElement.GetProperty("passwordHash").GetString() ?? "";
            return storedHash == HashPassword(password);
        }
        catch { return false; }
    }

    public static void ResetPassword(string newPassword)
    {
        SavePasswordHash(newPassword);
    }
}
