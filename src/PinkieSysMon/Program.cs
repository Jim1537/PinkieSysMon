namespace PinkieSysMon;

internal static class Program
{
    // Keep ownership until process termination, including fatal-error UI.
    private static RuntimeProcessLease? _processLease;

    [STAThread]
    private static void Main(string[] args)
    {
        FileLogger? log = null;
        ApplicationLogSession? session = null;
        try
        {
            var appRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
            log = new FileLogger(Path.Combine(appRoot, "logs", "PinkieSysMon.Runtime.log"), "Runtime");
            var logger = log;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                logger.Error("Unhandled non-UI exception", e.ExceptionObject as Exception);
                logger.FlushSuppressed();
            };

            RuntimeStartupOptions.Validate(args);
            _processLease = RuntimeProcessLease.TryAcquireForCurrentUser();
            if (_processLease is null)
            {
                // Append without resetting the live owner's session.
                log.Warn("Runtime startup refused: another Runtime owns this Windows user lease.");
                MessageBox.Show(
                    "Another PinkieSysMon Runtime is already running or has not finished shutting down. " +
                    "The existing Runtime may be unresponsive; this instance will not open Outputs. " +
                    "Check the existing Runtime instead of starting a second one.",
                    "PinkieSysMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            session = ApplicationLogSession.Start(log, args.Length == 1 ? "usb-test-only" : "ordinary");
            ApplicationConfiguration.Initialize();
            WinFormsFatalExceptionPolicy.Configure();
            using (ApplicationContext context = args.Length == 1
                ? new UsbTestApplicationContext(appRoot, log)
                : new PinkieApplicationContext(appRoot, log))
            {
                session.Ready();
                Application.Run(context);
            }
            session.MarkCompleted();
        }
        catch (Exception ex)
        {
            log?.Error("Fatal application error", ex);
            MessageBox.Show(
                $"PinkieSysMon stopped due to an unexpected error.\n\n{ex.Message}\n\nSee logs\\PinkieSysMon.Errors.log",
                "PinkieSysMon", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { session?.Dispose(); }
    }
}
