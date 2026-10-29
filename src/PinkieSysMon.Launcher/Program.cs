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
            if (args.Length != 0)
            {
                ProjectActionDiagnostics.Execute("launcher.launch", args.Length == 1 &&
                    args[0] is ("--editor" or "--runtime" or "--both") ? args[0] : "invalid", progress =>
                {
                    progress("arguments.validate");
                    if (args.Length != 1 || args[0] is not ("--editor" or "--runtime" or "--both"))
                        throw new ArgumentException("Unknown launcher command.");
                    ProjectLauncherActions.Run(root, args[0], progress);
                }, logger.Report);
                return;
            }

            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            Application.Run(new ProjectInfoWindow(root, showLaunchButtons: true, version, logger.Report));
        }
        catch (Exception ex)
        {
            log?.Report("Launcher startup or UI dispatch failed.", ex);
            MessageBox.Show(ex.Message, "PinkieSysMon Launcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
