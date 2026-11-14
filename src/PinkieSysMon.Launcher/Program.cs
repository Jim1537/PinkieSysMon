using System.Windows.Forms;
using PinkieSysMon.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        LauncherErrorLog? log = null;
        try
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            log = new LauncherErrorLog(root);
            var logger = log;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                logger.Report("Unhandled non-UI Launcher exception.", e.ExceptionObject as Exception);
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            var route = LauncherCommandLine.Parse(args);
            if (route.IsDirectAction)
            {
                ProjectActionDiagnostics.Execute("launcher.launch", route.DirectAction!, progress =>
                {
                    progress("arguments.validate");
                    ProjectLauncherActions.Run(root, route.DirectAction!, progress);
                }, logger.Report);
                return;
            }

            // Acquire ownership BEFORE constructing any Settings window or
            // draft. A second invocation only selects a tab on this owner.
            var identity = LauncherUiLease.CurrentNames(root);
            using var owner = LauncherUiLease.TryAcquire(root);
            if (owner is null)
            {
                LauncherUiLease.ActivateAsync(identity.PipeName, route.Tab!.Value).GetAwaiter().GetResult();
                return;
            }

            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            using var window = new UnifiedLauncherWindow(root, version, logger.Report);
            _ = window.Handle;
            window.SelectTab(route.Tab!.Value);
            owner.Listen(tab =>
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.BeginInvoke((Action)(() =>
                {
                    try { window.SelectTab(tab); completed.TrySetResult(); }
                    catch (Exception error) { completed.TrySetException(error); }
                }));
                completed.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
            }, error => logger.Report("Launcher tab activation failed.", error));
            Application.Run(window);
        }
        catch (Exception ex)
        {
            log?.Report("Launcher startup or UI dispatch failed.", ex);
            MessageBox.Show(ex.Message, "PinkieSysMon Launcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
