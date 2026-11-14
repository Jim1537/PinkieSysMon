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
        LogOperation? startup = null;
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
            startup = new LogOperation(log, "editor.startup");
            startup.Phase("root.resolved", GetArgument(args, "--root") is null ? "source=ancestor" : "source=argument");
            startup.Phase("application.initialize");
            global::ApplicationConfiguration.Initialize();
            WinFormsFatalExceptionPolicy.Configure();
            startup.Phase("dispatcher.initialize");
            if (DispatcherQueue.GetForCurrentThread() is null)
                dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();

            var requestedDashboard = GetArgument(args, "--dashboard");
            startup.Phase("settings.read");
            var editorSettings = EditorSettings.Load(Path.Combine(root, "config", "editor.json"));
            startup.Phase("theme.apply");
            EditorThemeContract.ApplyApplicationTheme(EditorThemeContract.Parse(editorSettings.Theme));
            startup.Phase("session.startup-gate");
            using var startupGate = EditorSessionWire.AcquireStartupGate(root);
            startup.Phase("form.construct");
            using (var form = new EditorForm(root, requestedDashboard, log))
            {
                // Read-only STATUS and fully acknowledged close are served by this
                // Editor process, not by the separate global Runtime pipe.
                var startedUtcTicks = System.Diagnostics.Process.GetCurrentProcess()
                    .StartTime.ToUniversalTime().Ticks;
                Task<EditorSessionSnapshot> Capture()
                {
                    var result = new TaskCompletionSource<EditorSessionSnapshot>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        form.BeginInvoke((Action)(() =>
                        {
                            try { result.TrySetResult(form.GetSessionSnapshot(startedUtcTicks, root)); }
                            catch (Exception error) { result.TrySetException(error); }
                        }));
                    }
                    catch (Exception error) { result.TrySetException(error); }
                    return result.Task;
                }

                Task<EditorSessionCloseResult> CloseEditor(string? expectedDashboard)
                {
                    var result = new TaskCompletionSource<EditorSessionCloseResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        form.BeginInvoke((Action)(async () =>
                        {
                            try
                            {
                                var closed = await form.RequestCoordinatedClose(expectedDashboard);
                                result.TrySetResult(closed);
                            }
                            catch (Exception error) { result.TrySetException(error); }
                        }));
                    }
                    catch (Exception error) { result.TrySetException(error); }
                    return result.Task;
                }

                using var endpoint = new EditorSessionEndpoint(
                    EditorSessionWire.CurrentPipeName(Environment.ProcessId),
                    Capture, CloseEditor, error => log.Error("Editor session IPC failure.", error));
                _ = form.Handle; // Create the UI thread's BeginInvoke marshaling handle.
                endpoint.Start();
                startupGate.Dispose(); // Release startup admission before the message loop.
                session.Ready();
                startup.Complete("formConstructed=true; sessionEndpointStarted=true; readinessBoundaryReached=true");
                startup.Dispose();
                startup = null;
                Application.Run(form);
                if (!endpoint.WaitForFinalCloseAcknowledgement(TimeSpan.FromSeconds(10)))
                    log.Warn("Editor closed, but coordinated close IPC acknowledgement was not confirmed.");
            }
            completed = true;
        }
        catch (Exception ex)
        {
            startup?.Fail(ex);
            log?.Error("Fatal Editor application error", ex);
            MessageBox.Show(
                $"PinkieSysMon Editor stopped due to an unexpected error.\n\n{ex.Message}\n\nSee logs\\PinkieSysMon.Errors.log",
                "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            startup?.Dispose();
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
