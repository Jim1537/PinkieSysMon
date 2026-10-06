using Microsoft.UI.Dispatching;
using PinkieSysMon;

namespace PinkieSysMon.Editor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        global::ApplicationConfiguration.Initialize();
        WinFormsFatalExceptionPolicy.Configure();

        DispatcherQueueController? dispatcherQueueController = null;
        FileLogger? log = null;

        try
        {
            if (DispatcherQueue.GetForCurrentThread() is null)
                dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();

            var root = GetArgument(args, "--root") ?? ApplicationRootLocator.Find(AppContext.BaseDirectory);
            var editorLog = new FileLogger(Path.Combine(root, "logs", "PinkieSysMon.Editor.log"));
            log = editorLog;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                editorLog.Error("Unhandled non-UI Editor exception", e.ExceptionObject as Exception);

            var requestedDashboard = GetArgument(args, "--dashboard");
            var editorSettings = EditorSettings.Load(Path.Combine(root, "config", "editor.json"));
            EditorThemeContract.ApplyApplicationTheme(EditorThemeContract.Parse(editorSettings.Theme));

            if (string.IsNullOrWhiteSpace(requestedDashboard))
                requestedDashboard = AppConfig.Load(Path.Combine(root, "config", "app.json"), log).Dashboard.Active;

            Application.Run(new EditorForm(root, requestedDashboard!));
        }
        catch (Exception ex)
        {
            log?.Error("Fatal Editor application error", ex);
            MessageBox.Show(
                $"PinkieSysMon Editor stopped due to an unexpected error.\n\n{ex.Message}",
                "PinkieSysMon Editor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (dispatcherQueueController is not null)
            {
                try { dispatcherQueueController.ShutdownQueue(); }
                catch { }
            }
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
