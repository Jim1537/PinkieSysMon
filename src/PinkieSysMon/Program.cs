namespace PinkieSysMon;

internal static class Program
{
    // The owning thread remains the WinForms main thread for process lifetime.
    // Deliberately no early Dispose: native I/O release may be delayed.
    private static RuntimeProcessLease? _processLease;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        WinFormsFatalExceptionPolicy.Configure();

        string appRoot;
        try
        {
            appRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PinkieSysMon failed to locate its application root.\n\n{ex.Message}",
                "PinkieSysMon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var logger = new FileLogger(Path.Combine(appRoot, "logs", "PinkieSysMon.log"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Error("Unhandled non-UI exception", e.ExceptionObject as Exception);

        try
        {
            RuntimeStartupOptions.Validate(args);
            // Reserve the current user's Runtime slot before loading any live Outputs.
            // Keep the lease until process termination, including fatal-error UI: a
            // failed native cleanup must not free the slot for another Runtime.
            _processLease = RuntimeProcessLease.TryAcquireForCurrentUser();
            if (_processLease is null)
            {
                logger.Warn("Runtime startup refused: another Runtime owns this Windows user lease.");
                MessageBox.Show(
                    "Another PinkieSysMon Runtime is already running or has not finished shutting down. " +
                    "The existing Runtime may be unresponsive; this instance will not open Outputs. " +
                    "Check the existing Runtime instead of starting a second one.",
                    "PinkieSysMon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            Application.Run(args.Length == 1
                ? new UsbTestApplicationContext(appRoot, logger)
                : new PinkieApplicationContext(appRoot, logger));
        }
        catch (Exception ex)
        {
            logger.Error("Fatal application error", ex);
            MessageBox.Show(
                $"PinkieSysMon stopped due to an unexpected error.\n\n{ex.Message}\n\nSee logs\\PinkieSysMon.log",
                "PinkieSysMon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
