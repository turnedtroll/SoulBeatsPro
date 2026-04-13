namespace larpLOLv4;

static class Program
{
    [STAThread]
    static void Main()
    {
        NativeApi.SetProcessDPIAware();
        ApplicationConfiguration.Initialize();

        while (true)
        {
            var menu = new MenuForm();
            Application.Run(menu);

            if (menu.Result == MenuForm.Choice.None)
                break;

            if (menu.Result == MenuForm.Choice.Calibrate)
            {
                Application.Run(new CalibrationForm());
                continue;
            }

            if (menu.Result == MenuForm.Choice.Run)
            {
                var engine = new MacroEngine();
                DebugForm? debugForm = null;

                if (menu.StartDebug)
                {
                    engine.Start();
                    debugForm = new DebugForm(engine);
                    debugForm.FormClosed += (_, _) => engine.Stop();
                    Application.Run(debugForm);
                }
                else
                {
                    // Run with a hidden notification form for hotkey handling
                    engine.Start();
                    var runForm = new RunningForm(engine);
                    Application.Run(runForm);
                }

                engine.Stop();
                // Wait for engine thread to finish
                while (engine.Running) Thread.Sleep(10);
                break;
            }
        }
    }
}
