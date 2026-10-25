using Microsoft.UI.Dispatching;
using PinkieSysMon;

namespace PinkieSysMon.Editor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        DispatcherQueueController? dispatcherQueueController = null;
        FileLogger? log = null;
        ApplicationLogSession? session = null;
        var completed = false;
        try
        {
            var root = GetArgument(args, "--root") ?? ApplicationRootLocator.Find(AppContext.BaseDirectory);
            log = new FileLogger(Path.Combine(root, "logs", "PinkieSysMon.Editor.log"), "Editor");
            var logger = log;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                logger.Error("Unhandled non-UI Editor exception", e.ExceptionObject as Exception);
                logger.FlushSuppressed();
            };
            session = ApplicationLogSession.Start(log, "editor");
            global::ApplicationConfiguration.Initialize();
            WinFormsFatalExceptionPolicy.Configure();
            if (DispatcherQueue.GetForCurrentThread() is null)
                dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();

            var requestedDashboard = GetArgument(args, "--dashboard");
            var editorSettings = EditorSettings.Load(Path.Combine(root, "config", "editor.json"));
            EditorThemeContract.ApplyApplicationTheme(EditorThemeContract.Parse(editorSettings.Theme));
            using (var form = new EditorForm(root, requestedDashboard, log))
            {
                session.Ready();
                Application.Run(form);
            }
            completed = true;
        }
        catch (Exception ex)
        {
            log?.Error("Fatal Editor application error", ex);
            MessageBox.Show(
                $"PinkieSysMon Editor stopped due to an unexpected error.\n\n{ex.Message}\n\nSee logs\\PinkieSysMon.Errors.log",
                "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (dispatcherQueueController is not null)
            {
                try { dispatcherQueueController.ShutdownQueue(); }
                catch (Exception error)
                {
                    completed = false;
                    log?.Error("Editor dispatcher cleanup failed.", error);
                }
            }
            if (completed) session?.MarkCompleted();
            session?.Dispose();
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
