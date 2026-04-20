using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoulBeatsPro
{
    internal sealed class TuningTab : UserControl
    {
        // ── Detection ──
        private NumericUpDown nudSampleRadius = null!;
        private NumericUpDown nudMinPixels = null!;

        // ── Timing ──
        private NumericUpDown nudTapDuration = null!;
        private NumericUpDown nudHoldCooldown = null!;
        private NumericUpDown nudToggleDelay = null!;
        private NumericUpDown nudHoldArmGrace = null!;
        private NumericUpDown nudHoldReleaseGrace = null!;
        private NumericUpDown nudInputOffset = null!;

        // ── Actions ──
        private Button btnReset = null!;
        private LayoutScaler? _scaler;

        public TuningTab()
        {
            InitializeComponent();
            LoadFromConfig();
        }

        // ────────────────────────────────────────────────────────
        //  Layout
        // ────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            SuspendLayout();

            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock      = DockStyle.Fill;
            AutoScroll = true;
            Font      = new Font("MS Sans Serif", 8f);
            Padding   = new Padding(8);

            var anchorLTR = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            // ── Detection GroupBox ──────────────────────────────
            var grpDetection = new GroupBox
            {
                Text     = "Detection",
                Location = new Point(8, 8),
                Size     = new Size(Width - 16, 78),
                Anchor   = anchorLTR
            };

            var lblSampleRadius = new Label
            {
                Text      = "Sample Radius (px):",
                Location  = new Point(12, 24),
                AutoSize  = true
            };

            nudSampleRadius = MakeIntSpinner();
            nudSampleRadius.Location = new Point(180, 22);

            var lblMinPixels = new Label
            {
                Text      = "Min Pixels to Trigger:",
                Location  = new Point(12, 50),
                AutoSize  = true
            };

            nudMinPixels = MakeIntSpinner();
            nudMinPixels.Location = new Point(180, 48);

            grpDetection.Controls.AddRange(new Control[]
            {
                lblSampleRadius, nudSampleRadius,
                lblMinPixels,    nudMinPixels
            });

            // ── Timing GroupBox ─────────────────────────────────
            var grpTiming = new GroupBox
            {
                Text     = "Timing (seconds)",
                Location = new Point(8, 94),
                Size     = new Size(Width - 16, 182),
                Anchor   = anchorLTR
            };

            var lblTapDuration = new Label
            {
                Text      = "Tap Key Duration:",
                Location  = new Point(12, 24),
                AutoSize  = true
            };

            nudTapDuration = MakeDoubleSpinner();
            nudTapDuration.Location = new Point(180, 22);

            var lblHoldCooldown = new Label
            {
                Text      = "Hold Release Cooldown:",
                Location  = new Point(12, 50),
                AutoSize  = true
            };

            nudHoldCooldown = MakeDoubleSpinner();
            nudHoldCooldown.Location = new Point(180, 48);

            var lblToggleDelay = new Label
            {
                Text      = "Pause Toggle Delay:",
                Location  = new Point(12, 76),
                AutoSize  = true
            };

            nudToggleDelay = MakeDoubleSpinner();
            nudToggleDelay.Location = new Point(180, 74);

            var lblHoldArmGrace = new Label
            {
                Text      = "Hold Arm Grace Period:",
                Location  = new Point(12, 102),
                AutoSize  = true
            };

            nudHoldArmGrace = MakeDoubleSpinner();
            nudHoldArmGrace.Location = new Point(180, 100);

            var lblHoldReleaseGrace = new Label
            {
                Text      = "Hold Release Grace:",
                Location  = new Point(12, 128),
                AutoSize  = true
            };

            nudHoldReleaseGrace = MakeDoubleSpinner();
            nudHoldReleaseGrace.Location = new Point(180, 126);

            var lblInputOffset = new Label
            {
                Text     = "Input Offset (ms):",
                Location = new Point(12, 154),
                AutoSize = true
            };

            nudInputOffset = MakeMsSpinner();
            nudInputOffset.Location = new Point(180, 152);

            grpTiming.Controls.AddRange(new Control[]
            {
                lblTapDuration,  nudTapDuration,
                lblHoldCooldown, nudHoldCooldown,
                lblToggleDelay,  nudToggleDelay,
                lblHoldArmGrace, nudHoldArmGrace,
                lblHoldReleaseGrace, nudHoldReleaseGrace,
                lblInputOffset,  nudInputOffset
            });

            // ── Info GroupBox ───────────────────────────────────
            var grpInfo = new GroupBox
            {
                Text     = "Info",
                Location = new Point(8, 284),
                Size     = new Size(Width - 16, 220),
                Anchor   = anchorLTR
            };

            var lblInfo = new Label
            {
                Text =
                    "Sample Radius: size of the pixel patch sampled\r\n" +
                    "at each detection point (2*n+1 square).\r\n" +
                    "Min Pixels: how many matching color pixels are\r\n" +
                    "needed in the patch to count as a detection.\r\n" +
                    "Tap Duration: how long the key is held down for\r\n" +
                    "a single tap note.\r\n" +
                    "Hold Cooldown: minimum time between releasing\r\n" +
                    "a hold and starting a new action on that lane.\r\n" +
                    "Toggle Delay: debounce time for the pause key.\r\n" +
                    "Arm Grace: how long the hold-arm flag stays\r\n" +
                    "active after gold disappears from hold zone.\r\n" +
                    "Release Grace: how long to wait before releasing\r\n" +
                    "a hold — prevents drops from brief flickers.\r\n" +
                    "Input Offset: osu!mania only — ms to shift press\r\n" +
                    "timing. Positive = press earlier, negative = later.\r\n" +
                    "If hits land Green (Great/200) because macro fires\r\n" +
                    "early, use NEGATIVE offset (-10 to -30). If they\r\n" +
                    "land late, use positive. Range: ±50ms.",
                Location  = new Point(12, 20),
                Size      = new Size(grpInfo.Width - 24, 190),
                Anchor    = anchorLTR,
                AutoSize  = false
            };

            grpInfo.Controls.Add(lblInfo);

            // ── Reset Button ────────────────────────────────────
            btnReset = new Button
            {
                Text      = "Reset to Defaults",
                FlatStyle = FlatStyle.Standard,
                Size      = new Size(120, 28),
                Location  = new Point(8, 516),
                Anchor    = AnchorStyles.Left | AnchorStyles.Bottom
            };
            btnReset.Click += BtnReset_Click;

            // ── Add to control tree ─────────────────────────────
            Controls.AddRange(new Control[]
            {
                grpDetection,
                grpTiming,
                grpInfo,
                btnReset
            });

            ResumeLayout(false);
        }

        // ────────────────────────────────────────────────────────
        //  Spinner factories
        // ────────────────────────────────────────────────────────
        private static NumericUpDown MakeIntSpinner()
        {
            return new NumericUpDown
            {
                Minimum       = 1,
                Maximum       = 20,
                DecimalPlaces = 0,
                Increment     = 1,
                Size          = new Size(70, 20)
            };
        }

        private static NumericUpDown MakeDoubleSpinner()
        {
            return new NumericUpDown
            {
                Minimum       = 0.001m,
                Maximum       = 2.0m,
                DecimalPlaces = 3,
                Increment     = 0.005m,
                Size          = new Size(70, 20)
            };
        }

        private static NumericUpDown MakeMsSpinner()
        {
            return new NumericUpDown
            {
                Minimum       = -50m,
                Maximum       = 50m,
                DecimalPlaces = 1,
                Increment     = 1m,
                Size          = new Size(70, 20)
            };
        }

        // ────────────────────────────────────────────────────────
        //  Config ↔ UI
        // ────────────────────────────────────────────────────────
        private void LoadFromConfig()
        {
            var t = ConfigManager.Instance.Tuning;

            // Detach handlers while bulk-loading
            DetachValueChanged();

            nudSampleRadius.Value = t.SampleHalf;
            nudMinPixels.Value    = t.MinPixels;
            nudTapDuration.Value  = (decimal)t.TapKeyDuration;
            nudHoldCooldown.Value = (decimal)t.HoldReleaseCooldown;
            nudToggleDelay.Value  = (decimal)t.ToggleDelay;
            nudHoldArmGrace.Value = (decimal)t.HoldArmGrace;
            nudHoldReleaseGrace.Value = (decimal)t.HoldReleaseGrace;
            nudInputOffset.Value  = Math.Clamp((decimal)t.InputOffsetMs, nudInputOffset.Minimum, nudInputOffset.Maximum);

            AttachValueChanged();
        }

        private void SaveToConfig()
        {
            var t = ConfigManager.Instance.Tuning;

            t.SampleHalf          = (int)nudSampleRadius.Value;
            t.MinPixels           = (int)nudMinPixels.Value;
            t.TapKeyDuration      = (double)nudTapDuration.Value;
            t.HoldReleaseCooldown = (double)nudHoldCooldown.Value;
            t.ToggleDelay         = (double)nudToggleDelay.Value;
            t.HoldArmGrace        = (double)nudHoldArmGrace.Value;
            t.HoldReleaseGrace    = (double)nudHoldReleaseGrace.Value;
            t.InputOffsetMs       = (double)nudInputOffset.Value;

            ConfigManager.Instance.SaveSettings();
        }

        // ────────────────────────────────────────────────────────
        //  Event wiring helpers
        // ────────────────────────────────────────────────────────
        private void AttachValueChanged()
        {
            nudSampleRadius.ValueChanged += Spinner_ValueChanged;
            nudMinPixels.ValueChanged    += Spinner_ValueChanged;
            nudTapDuration.ValueChanged  += Spinner_ValueChanged;
            nudHoldCooldown.ValueChanged += Spinner_ValueChanged;
            nudToggleDelay.ValueChanged  += Spinner_ValueChanged;
            nudHoldArmGrace.ValueChanged += Spinner_ValueChanged;
            nudHoldReleaseGrace.ValueChanged += Spinner_ValueChanged;
            nudInputOffset.ValueChanged += Spinner_ValueChanged;
        }

        private void DetachValueChanged()
        {
            nudSampleRadius.ValueChanged -= Spinner_ValueChanged;
            nudMinPixels.ValueChanged    -= Spinner_ValueChanged;
            nudTapDuration.ValueChanged  -= Spinner_ValueChanged;
            nudHoldCooldown.ValueChanged -= Spinner_ValueChanged;
            nudToggleDelay.ValueChanged  -= Spinner_ValueChanged;
            nudHoldArmGrace.ValueChanged -= Spinner_ValueChanged;
            nudHoldReleaseGrace.ValueChanged -= Spinner_ValueChanged;
            nudInputOffset.ValueChanged -= Spinner_ValueChanged;
        }

        private void Spinner_ValueChanged(object? sender, EventArgs e)
        {
            SaveToConfig();
        }

        // ────────────────────────────────────────────────────────
        //  Reset
        // ────────────────────────────────────────────────────────
        private void BtnReset_Click(object? sender, EventArgs e)
        {
            ConfigManager.Instance.Tuning.Reset();
            LoadFromConfig();
            ConfigManager.Instance.SaveSettings();
        }

        // ── Proportional scaling ────────────────────────────────────

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
}
