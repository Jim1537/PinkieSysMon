using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105ProcessLoggingTests
{
    public static void LauncherSharedErrorsAndFailureIsolation()
    {
        Sandbox(root =>
        {
            var sink = new LauncherErrorLog(root);
            sink.Report("Successful standalone actions stay quiet.", null);
            var directory = Path.Combine(root, "logs");
            Check(!Directory.Exists(directory), "A successful standalone action creates no fourth/session stream.");
            var runtime = new FileLogger(Path.Combine(directory, "PinkieSysMon.Runtime.log"), "Runtime");
            runtime.Info("Existing Runtime session marker.");
            var native = new Win32Exception(5, "Injected native launch failure");
            try
            {
                ProjectActionDiagnostics.Execute("launcher.launch", "--both", progress =>
                {
                    progress("runtime.start.returned");
                    progress("editor.start");
                    throw native;
                }, sink.Report);
                throw new InvalidOperationException("Expected injected launch failure.");
            }
            catch (Win32Exception error) { Check(ReferenceEquals(error, native), "Original action failure must be rethrown unchanged."); }
            sink.Report("Generic outer boundary.", native);
            var errors = Records(runtime.ErrorsPath);
            Check(errors.Length == 1 && errors[0].GetProperty("component").GetString() == "Launcher" &&
                  errors[0].GetProperty("nativeCode").GetInt32() == 5 &&
                  errors[0].GetProperty("exception").GetString()!.Contains("Injected native launch failure"),
                "Launcher retains one original native error in the shared Errors stream.");
            using (var action = JsonDocument.Parse(errors[0].GetProperty("message").GetString()!))
                Check(action.RootElement.GetProperty("phase").GetString() == "editor.start" &&
                      action.RootElement.GetProperty("reachedPhases").EnumerateArray()
                          .Any(phase => phase.GetString() == "runtime.start.returned") &&
                      !action.RootElement.GetProperty("childReadinessConfirmed").GetBoolean(),
                    "Partial Both dispatch retains the prior boundary without claiming child readiness.");

            try
            {
                ProjectActionDiagnostics.Execute("launcher.launch", "--editor",
                    progress => ProjectLauncherActions.Run(root, "--editor", progress), sink.Report);
                throw new InvalidOperationException("Missing fixture executable must be refused.");
            }
            catch (FileNotFoundException) { }
            Parallel.For(0, 16, index =>
            {
                runtime.Error("Runtime concurrent fixture " + index);
                sink.Report("Launcher concurrent fixture " + index, new IOException("Injected concurrent failure " + index));
            });
            errors = Records(runtime.ErrorsPath);
            Check(errors.Length == 34 && errors.Select(record => record.GetProperty("eventId").GetString()).Distinct().Count() == 34 &&
                  errors.All(record => record.GetProperty("loggingFailures").GetInt32() == 0),
                "Source-shared writer preserves complete concurrent Runtime/Launcher records and zero loss.");
            Check(Records(runtime.Path).Any(record => record.GetProperty("message").GetString() == "Existing Runtime session marker.") &&
                  Directory.GetFiles(directory, "*.log").Select(Path.GetFileName).Order().SequenceEqual(
                      new[] { "PinkieSysMon.Errors.log", "PinkieSysMon.Runtime.log" }),
                "Launcher appends without resetting the existing session or adding a log stream.");

            var repository = ApplicationRootLocator.Find(AppContext.BaseDirectory);
            var project = XDocument.Load(Path.Combine(repository, "src", "PinkieSysMon.Launcher", "PinkieSysMon.Launcher.csproj"));
            Check(!project.Descendants("Reference").Any() && !project.Descendants("ProjectReference").Any() &&
                  project.Descendants("Compile").Any(item => ((string?)item.Attribute("Include"))?.EndsWith("LogFileWriter.cs") == true),
                "Standalone Launcher compiles the canonical writer without an application assembly reference.");
        });
    }

    public static void ActionResultsSessionProgressAndDeniedSink()
    {
        Sandbox(root =>
        {
            var observations = new List<(string Message, Exception? Error)>();
            using (var launcher = new ProjectInfoWindow(root, true, "fixture", (message, error) => observations.Add((message, error))))
            {
                var calls = 0;
                launcher.LaunchAndClose("--editor", (_, _) => calls++);
                Check(calls == 1 && launcher.IsDisposed, "Successful launch retains the established close contract.");
            }
            var actionEvents = observations.Where(item => item.Message.StartsWith('{'))
                .Select(item => JsonSerializer.Deserialize<JsonElement>(item.Message)).ToArray();
            Check(actionEvents.First().GetProperty("outcome").GetString() == "started" &&
                  actionEvents.Last().GetProperty("outcome").GetString() == "returned" &&
                  actionEvents.Select(item => item.GetProperty("operationId").GetString()).Distinct().Count() == 1 &&
                  actionEvents.All(item => !item.GetProperty("childReadinessConfirmed").GetBoolean()),
                "Action lifecycle has one identity and never promotes Shell acceptance to readiness.");
            using (var launcher = new ProjectInfoWindow(root, true, "fixture", (_, _) => throw new IOException("Injected observer failure")))
            {
                var original = new InvalidOperationException("Injected action failure");
                try
                {
                    launcher.LaunchAndClose("--editor", (_, _) => throw original);
                    throw new InvalidOperationException("Expected action refusal.");
                }
                catch (InvalidOperationException error) { Check(ReferenceEquals(error, original), "Observer failure cannot replace action failure."); }
                Check(!launcher.IsDisposed, "Failed start leaves Launcher available for retry.");
            }

            var log = new FileLogger(Path.Combine(root, "session.log"), "Editor");
            using (var session = ApplicationLogSession.Start(log, "editor"))
            {
                session.MaintainNow();
                Check(!Records(log.Path).Any(record => Message(record).StartsWith("SESSION_PROGRESS")),
                    "Maintenance before readiness cannot imply a ready application.");
                session.Ready();
                session.MaintainNow();
                Check(Records(log.Path).Any(record => Message(record).Contains("maintenanceObserverReturned=true; uiOrWorkerHealthConfirmed=false")),
                    "Existing maintenance cadence reports only its own observed progress.");
                session.MarkCompleted();
                session.Dispose();
                var count = Records(log.Path).Length;
                session.MaintainNow();
                Check(Records(log.Path).Length == count, "Disposed session rejects later progress observations.");
            }
            Check(Records(log.Path).All(record => record.GetProperty("loggingFailures").GetInt32() == 0) &&
                  Records(log.Path).Any(record => Message(record) == "SESSION_END normalExit=true"),
                "Progress evidence preserves zero-loss session completion.");

            var deniedRoot = Path.Combine(root, "denied");
            var deniedPath = Path.Combine(deniedRoot, "logs", "PinkieSysMon.Errors.log");
            Directory.CreateDirectory(deniedPath);
            var denied = new LauncherErrorLog(deniedRoot);
            using var captured = new StringWriter();
            var previous = Console.Error;
            try { Console.SetError(captured); denied.Report("Expected denied fixture sink.", new IOException("Original fixture failure")); }
            finally { Console.SetError(previous); }
            var fallback = captured.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Check(fallback.Length == 1 && fallback[0].StartsWith("PinkieSysMon logging unavailable: UnauthorizedAccessException:") &&
                  fallback[0].Contains(deniedPath), "Exactly the deliberate denied-sink diagnostic must be captured: " + captured);
        });
    }

    private static string Message(JsonElement record) => record.GetProperty("message").GetString()!;
    private static JsonElement[] Records(string path) => LogFileWriter.UnderGate<JsonElement[]>(path, "write", () =>
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return [];
        var text = string.Concat(Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.archive")
            .Order(StringComparer.Ordinal).Select(File.ReadAllText)) + (File.Exists(path) ? File.ReadAllText(path) : "");
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line =>
        { using var document = JsonDocument.Parse(line[line.IndexOf('{')..]); return document.RootElement.Clone(); }).ToArray();
    });

    private static void Sandbox(Action<string> scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-process-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { scenario(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
