using System.Drawing;
using System.Text.Json;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105DesktopLoggingTests
{
    public static void BufferedTransitionsAndOwnership()
    {
        Sandbox((root, log) =>
        {
            var events = new OutputDiagnosticEvents(log);
            for (var i = 0; i < OutputDiagnosticEvents.Capacity + 2; i++) events.Record("fixture", "logical", "managed-only");
            Check(!File.Exists(log.Path), "Producer observation must perform no diagnostic I/O.");
            events.Flush();
            Check(Text(log.Path).Contains("\"omitted\":2"), "A full buffer reports exact omitted count.");
            var target = new OutputTargetConfig
            {
                Id = "desktop-evidence", Kind = OutputTargetContract.DesktopKind,
                Transport = OutputTargetContract.DesktopTransport,
                Desktop = new() { BoardEnabled = true }, ProviderEnabled = false
            };
            var board = new C.DashboardDefinition
            { BaseDirectory = root, Canvas = new() { Width = 32, Height = 32 }, Widgets = [] };
            Window? window = null;
            using (var session = new DesktopOutputSession(new AppConfig(), target, board, new MetricStore(), log,
                render => window = new Window(render), events))
            {
                session.Start(); window!.RequestFrame();
                session.SetIdlePaused(true); session.Suspend(); session.Resume(); session.SetIdlePaused(false);
                Check(window.IsVisible && window.Presented == 1, "Diagnostics cannot introduce frames while paused/suspended.");
            }
            events.Flush();
            var observed = Events(log.Path, "runtime.output.observed");
            var states = observed.Where(e => e.GetProperty("phase").GetString() == "desktop.state.changed")
                .Select(e => e.GetProperty("detail").GetString()!).ToArray();
            Check(states.Length == 5 && states[0].Contains("current=DISPLAYING") && states[1].Contains("current=IDLE_PAUSED") &&
                states[2].Contains("current=SUSPENDED") && states[^1].Contains("current=DISPLAYING"), "Observed lifecycle transitions are retained in order.");
            Check(observed.Count(e => e.GetProperty("phase").GetString() == "desktop.first.frame.presented") == 1 &&
                observed.Count(e => e.GetProperty("phase").GetString() == "desktop.disposed") == 1 && window!.Disposals == 1,
                "First frame and terminal cleanup do not alter window ownership.");
        });
    }

    public static void ShellFailureCancellationAndPrivacy()
    {
        Sandbox((root, log) =>
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var failureReported = new ManualResetEventSlim(); using var done = new ManualResetEventSlim();
            var nativeCalls = 0;
            var dispatcher = new ShortcutShellDispatcher(code => { if (code == 5) failureReported.Set(); }, (target, args) =>
            {
                Interlocked.Increment(ref nativeCalls);
                if (target == "PRIVATE-FIRST") { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Shell fixture release."); return 5; }
                done.Set(); return 0;
            }, log);
            ShortcutInteraction Payload(string target) => new("logical-widget", new(0, 0, 20, 20), 0, 0, 0, target, "PRIVATE-ARGS", true);
            try
            {
                Check(!dispatcher.TryDispatch(Payload("")), "Empty target is refused.");
                Check(dispatcher.TryDispatch(Payload("PRIVATE-FIRST")) && entered.Wait(TimeSpan.FromSeconds(10)), "First action must enter once.");
                Check(dispatcher.TryDispatch(Payload("PRIVATE-CANCELED")), "Second action queues.");
                dispatcher.CancelPending();
                Check(dispatcher.TryDispatch(Payload("PRIVATE-LAST")), "New generation action queues.");
                release.Set();
                Check(failureReported.Wait(TimeSpan.FromSeconds(10)) && done.Wait(TimeSpan.FromSeconds(10)), "Shell must report failure and continue.");
                dispatcher.Dispose();
                Check(dispatcher.Completion.Wait(TimeSpan.FromSeconds(10)), "Terminal dispatch/logging fence.");
                Check(Events(log.Path, "desktop.shortcut.succeeded").Length == 1 && nativeCalls == 2,
                    "Superseded request never invokes Shell; success is only acceptance.");
                Check(Events(log.Path, "desktop.shortcut.cancelled").Length == 1 &&
                    Events(log.Path, "runtime.output.observed").Count(e => e.GetProperty("phase").GetString() == "desktop.shortcut.refused") == 1 &&
                    Events(log.ErrorsPath, "desktop.shortcut.failed").Length == 1 && Text(log.ErrorsPath).Contains("\"nativeCode\":5"),
                    "Refusal, cancellation and native failure have separate truthful outcomes.");
                Check(!Text(log.Path).Contains("PRIVATE-") && !Text(log.ErrorsPath).Contains("PRIVATE-"), "No target or argument payload is logged.");
            }
            finally { release.Set(); dispatcher.Dispose(); Check(dispatcher.Completion.Wait(TimeSpan.FromSeconds(10)), "Final Shell fence."); }
        });
    }

    private sealed class Window(Action render) : IDesktopOutputWindow
    {
        public bool IsVisible { get; private set; }
        internal int Presented, Disposals;
        public void Configure(string title, Size size, Point point, int interval, bool locked) { }
        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() => render();
        public void Present(SKBitmap bitmap) => Presented++;
        public void Dispose() { Disposals++; IsVisible = false; }
    }

    private static JsonElement[] Events(string path, string name) => Text(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var j = JsonDocument.Parse(line[line.IndexOf('{')..]); return j.RootElement.GetProperty("message").GetString(); })
        .Where(m => m?.StartsWith('{') == true)
        .Select(m => { using var j = JsonDocument.Parse(m!); return j.RootElement.Clone(); })
        .Where(e => e.GetProperty("eventName").GetString() == name).ToArray();
    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () => File.Exists(path) ? File.ReadAllText(path) : "");
    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-desktop-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root, new FileLogger(Path.Combine(root, "session.log"), "Runtime")); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
