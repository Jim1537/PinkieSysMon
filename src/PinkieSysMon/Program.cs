namespace PinkieSysMon;

internal static class Program
{
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
            Application.Run(new PinkieApplicationContext(appRoot, logger, RuntimeStartupOptions.ShouldStartOutputs(args)));
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
