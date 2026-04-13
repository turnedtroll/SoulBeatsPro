namespace SoulBeatsPro;

static class Program
{
    [STAThread]
    static void Main()
    {
        NativeApi.SetProcessDPIAware();
        ApplicationConfiguration.Initialize();

        // Initialize config (creates %LOCALAPPDATA%\bigbart\ if needed).
        // This must happen before the first Log.* call because Log writes into
        // the telemetry dir under ConfigManager.ConfigDir.
        _ = ConfigManager.Instance;

        Log.Info($"SoulBeatsPro v{AutoUpdater.CurrentVersion.ToString(3)} starting");

        // Clean up old data from previous versions
        CleanupLegacyData();

        // Update lane scan codes from saved keybinds
        NativeApi.UpdateLaneScans(ConfigManager.Instance.Keybinds.LaneKeys);

        // ── Revoke check ───────────────────────────────────────
        // If the admin panel has revoked this client, refuse to start. The
        // gate is persisted so the block survives a restart, which is the
        // whole point — a kicked user can't just relaunch to get back in.
        if (FeatureGate.Instance.CheckRevoked())
        {
            Log.Warn($"Launch blocked — client is revoked: {FeatureGate.Instance.RevokedReason}");
            MessageBox.Show(
                $"This copy of SoulBeats Pro has been disabled.\n\nReason: {FeatureGate.Instance.RevokedReason}",
                "SoulBeats Pro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            return;
        }

        // ── Crash reporting ────────────────────────────────────
        Application.ThreadException += (_, args) =>
        {
            Log.Error($"UI crash: {args.Exception.GetType().Name}: {args.Exception.Message}");
            _ = TelemetryManager.Instance.SendCrashReportAsync(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Error($"Unhandled crash: {ex.GetType().Name}: {ex.Message}");
                TelemetryManager.Instance.SendCrashReportAsync(ex).GetAwaiter().GetResult();
            }
        };

        Application.Run(new MainForm());
        Log.Info("SoulBeatsPro exiting");
    }

    /// Remove leftover files/folders from older versions.
    private static void CleanupLegacyData()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Old "received" folder inside bigbart (moved to hidden admin dir)
            var oldReceived = Path.Combine(appData, "bigbart", "received");
            if (Directory.Exists(oldReceived))
                Directory.Delete(oldReceived, recursive: true);

            // Old admin.dat that used to sit next to the exe (now embedded)
            var oldAdminDat = Path.Combine(AppContext.BaseDirectory, "admin.dat");
            if (File.Exists(oldAdminDat))
                File.Delete(oldAdminDat);

            // Old admin.json that used to sit next to the exe
            var oldAdminJson = Path.Combine(AppContext.BaseDirectory, "admin.json");
            if (File.Exists(oldAdminJson))
                File.Delete(oldAdminJson);
        }
        catch { }
    }
}
