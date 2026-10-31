using System.Drawing;
using System.Text.Json;
using PinkieSysMon;
using PinkieSysMon.Editor;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105CleanupLoggingTests
{
    public static void PendingCleanupDoesNotReleaseOwnership() => Sandbox((root, log) =>
    {
        var clock = new Clock();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cleanup = new CleanupOperation(log, "fixture.shutdown", time: clock, monitor: false);
        var released = 0;
        var worker = Task.Run(() =>
        {
            cleanup.Step("native.drain", () =>
            {
                entered.Set();
                Check(release.Wait(TimeSpan.FromSeconds(10)), "Fixture drain was not released.");
                Interlocked.Exchange(ref released, 1);
            });
            cleanup.Complete();
        });
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Cleanup did not enter its owned drain.");
            clock.Advance(CleanupOperation.PendingThreshold);
            cleanup.Poll();
            var first = Operations(log.ErrorsPath, "fixture.shutdown").Single();
            Check(first.GetProperty("outcome").GetString() == "pending" &&
                  first.GetProperty("phase").GetString() == "native.drain" &&
                  !worker.IsCompleted && Volatile.Read(ref released) == 0,
                "Pending evidence must identify the blocked phase without completing or releasing it.");
            cleanup.Poll();
            Check(Operations(log.ErrorsPath, "fixture.shutdown").Length == 1,
                "An unchanged pending phase must not repeat on every sample.");
            clock.Advance(CleanupOperation.RepeatInterval);
            cleanup.Poll();
            Check(Operations(log.ErrorsPath, "fixture.shutdown").Length == 2 &&
                  Operations(log.Path, "fixture.shutdown").All(e =>
                      e.GetProperty("outcome").GetString() is not ("pending" or "succeeded")),
                "Repeated pending details belong only to Errors, with no premature successful result.");
        }
        finally
        {
            release.Set();
            Check(worker.Wait(TimeSpan.FromSeconds(10)), "Cleanup did not finish after explicit release.");
        }
        var session = Operations(log.Path, "fixture.shutdown");
        Check(Volatile.Read(ref released) == 1 && session.Last().GetProperty("outcome").GetString() == "succeeded" &&
              session.Last().GetProperty("operationId").GetString() ==
                  Operations(log.ErrorsPath, "fixture.shutdown")[0].GetProperty("operationId").GetString(),
            "The terminal return must retain the operation identity of the earlier pending observation.");
    });

    public static void CleanupFailuresKeepFinallyAndOriginalErrors() => Sandbox((root, log) =>
    {
        var original = new IOException("fixture primary cleanup failure");
        var secondary = new InvalidOperationException("fixture secondary cleanup failure");
        using var cleanup = new CleanupOperation(log, "fixture.failed-shutdown", monitor: false);
        var order = new List<string>();
        var propagated = Capture(() =>
        {
            try { cleanup.Step("window.dispose", () => { order.Add("window"); throw original; }); }
            finally { cleanup.Step("renderer.dispose", () => { order.Add("renderer"); throw secondary; }); }
        });
        Check(ReferenceEquals(propagated, secondary) && order.SequenceEqual(["window", "renderer"]),
            "Observation must preserve the owner's existing finally order and propagated exception identity.");
        cleanup.Complete(); // An owner cannot turn already observed failures into success.
        log.Error("outer cleanup boundary", propagated);
        var errors = Read(log.ErrorsPath);
        var contexts = Operations(log.ErrorsPath, "fixture.failed-shutdown");
        Check(contexts.Length == 2 && contexts.Select(e => e.GetProperty("phase").GetString())
                  .SequenceEqual(["window.dispose", "renderer.dispose"]) &&
              contexts.Select(e => e.GetProperty("operationId").GetString()).Distinct().Count() == 1 &&
              errors.Count(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) == 2,
            "Both cleanup causes need their real phase and one full exception each, including propagation.");
        Check(Operations(log.Path, "fixture.failed-shutdown").Last().GetProperty("outcome").GetString() == "partial",
            "A failed cleanup must not produce a successful operation result.");
    });

    public static void ObserverFaultDoesNotReplaceCleanupFailure() => Sandbox((root, log) =>
    {
        var clock = new Clock { FailTimestamp = true };
        using var cleanup = new CleanupOperation(log, "fixture.broken-observer", time: clock, monitor: false);
        var original = new IOException("fixture cleanup with unavailable diagnostic clock");
        var calls = 0;
        var observed = Capture(() => cleanup.Step("worker.drain", () => { calls++; throw original; }));
        cleanup.Step("finally.release", () => calls++);
        Check(ReferenceEquals(observed, original) && calls == 2 && cleanup.CaptureFailures >= 2,
            "Diagnostic state failure must neither skip cleanup nor replace the original exception.");
        Check(Read(log.ErrorsPath).Count(e => e.GetProperty("exception").GetString()?.Contains(original.Message) == true) == 1,
            "The application error must survive a broken cleanup observer exactly once.");
    });

    public static void DesktopCleanupDoesNotReportFalseSuccess() => Sandbox((root, log) =>
    {
        var original = new IOException("fixture Desktop window cleanup failure");
        var window = new Window { Failure = original };
        var session = Desktop(root, "failed-desktop", log, window);
        Check(ReferenceEquals(Capture(session.Dispose), original), "Desktop cleanup changed the propagated window failure.");
        session.Dispose();
        var operations = Operations(log.Path, "desktop.dispose");
        Check(window.Disposals == 1 && operations.Any(e => e.GetProperty("phase").GetString() == "renderer.dispose.returned") &&
              operations.All(e => e.GetProperty("outcome").GetString() != "succeeded") &&
              Operations(log.ErrorsPath, "desktop.dispose").Single().GetProperty("phase").GetString() == "window.dispose",
            "Desktop must retain failed window cleanup, run its existing renderer finally, and avoid false full-disposal success.");
        var healthy = new Window();
        using (var other = Desktop(root, "healthy-desktop", log, healthy)) { }
        Check(healthy.Disposals == 1 && Operations(log.Path, "desktop.dispose").Count(e =>
                  e.GetProperty("outcome").GetString() == "succeeded" &&
                  e.GetProperty("subject").GetString() == "healthy-desktop") == 1,
            "A fully returned Desktop cleanup must still have an explicit terminal result.");
    });

    public static void PreviewShutdownEvidenceRespectsWorkerFence() => Sandbox((root, log) =>
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = new GraphTelemetryTests.Source(SystemMetricContract.Uptime, MetricProviderContract.System, _ =>
        {
            entered.Set();
            Check(release.Wait(TimeSpan.FromSeconds(10)), "Preview capture fixture was not released.");
            return 1;
        });
        var preview = new EditorPreviewTelemetry([source], GraphTelemetryTests.Policy(source.Metric), log,
            _ => EditorPreviewCatalog.Empty, _ => { });
        try
        {
            preview.Configure(new AppConfig(), new C.DashboardDefinition
            {
                Widgets = [new C.GraphWidgetDefinition { Id = "fixture-graph", Metric = source.Metric, WindowSeconds = 60 }]
            });
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Preview capture did not begin.");
            var terminal = preview.StopAsync();
            Check(!terminal.IsCompleted && !source.Disposed,
                "Preview must not dispose sources or claim completion while capture still owns them.");
        }
        finally
        {
            release.Set();
            Check(preview.StopAsync().Wait(TimeSpan.FromSeconds(10)), "Preview shutdown did not reach terminal completion.");
        }
        var evidence = Operations(log.Path, "editor.preview.shutdown");
        var phases = evidence.Select(e => e.GetProperty("phase").GetString()).ToArray();
        var drained = Array.IndexOf(phases, "worker.drain.returned");
        Check(source.Disposed && source.Active == 0 && preview.History.SeriesCount == 0 && drained >= 0 &&
              Array.IndexOf(phases, "sources.dispose") > drained &&
              evidence.Last().GetProperty("outcome").GetString() == "succeeded",
            "Preview terminal evidence must follow worker completion, source disposal and history release.");
    });

    public static void PreviewSourceFailuresRemainOneAggregate() => Sandbox((root, log) =>
    {
        var first = new IOException("fixture first provider cleanup failure");
        var second = new IOException("fixture second provider cleanup failure");
        var sourceA = new FailingSource(first);
        var sourceB = new FailingSource(second);
        var preview = new EditorPreviewTelemetry([sourceA, sourceB], new TelemetryConfig(), log,
            _ => EditorPreviewCatalog.Empty, _ => { });
        var error = Capture(() => preview.StopAsync().GetAwaiter().GetResult());
        Check(error is AggregateException aggregate && aggregate.InnerExceptions.SequenceEqual([first, second]) &&
              sourceA.Disposals == 1 && sourceB.Disposals == 1 && preview.StopAsync().IsFaulted,
            "Preview must keep its original aggregate and attempt every source after worker completion.");
        log.Error("Editor close propagated preview failure", error);
        Check(Read(log.ErrorsPath).Count(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) == 1 &&
              Operations(log.ErrorsPath, "editor.preview.shutdown").Single().GetProperty("phase").GetString() == "sources.dispose" &&
              Operations(log.Path, "editor.preview.shutdown").All(e => e.GetProperty("outcome").GetString() != "succeeded"),
            "A source-cleanup aggregate needs one full retained error, its actual phase and no successful shutdown claim.");
    });

    private static DesktopOutputSession Desktop(string root, string id, FileLogger log, Window window)
    {
        var target = new OutputTargetConfig
        {
            Id = id, Name = "Synthetic Desktop", Kind = OutputTargetContract.DesktopKind,
            Transport = OutputTargetContract.DesktopTransport, Desktop = new() { BoardEnabled = true }
        };
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        return new DesktopOutputSession(new AppConfig(), target, new C.DashboardDefinition
        {
            BaseDirectory = root, Canvas = new() { Width = 64, Height = 32 }, Widgets = []
        }, new MetricStore(), log, _ => window);
    }

    private sealed class Window : IDesktopOutputWindow
    {
        public Exception? Failure;
        public int Disposals;
        public bool IsVisible => false;
        public void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked) { }
        public void ShowWindow() { }
        public void HideWindow() { }
        public void RequestFrame() { }
        public void Present(SKBitmap frame) { }
        public void Dispose() { Disposals++; if (Failure is not null) throw Failure; }
    }

    private sealed class FailingSource(Exception error) : IMetricSource, IDisposable
    {
        public string ProviderId => MetricProviderContract.System;
        public string Name => "fixture-source";
        public IReadOnlyCollection<string> MetricNames => [];
        public int Disposals;
        public IReadOnlyDictionary<string, object?> Capture() => new Dictionary<string, object?>();
        public void Dispose() { Disposals++; throw error; }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public bool FailTimestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => FailTimestamp
            ? throw new InvalidOperationException("Injected cleanup diagnostic clock failure.") : Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    private static Exception Capture(Action work)
    {
        try { work(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the existing cleanup to fail.");
    }

    private static JsonElement[] Read(string path) => !File.Exists(path) ? [] :
        File.ReadAllLines(path).Select(line =>
        {
            using var json = JsonDocument.Parse(line[line.IndexOf('{')..]);
            return json.RootElement.Clone();
        }).ToArray();

    private static JsonElement[] Operations(string path, string operation) => Read(path)
        .Select(e => e.GetProperty("message").GetString())
        .Where(message => message is not null && message.StartsWith('{'))
        .Select(message =>
        {
            using var json = JsonDocument.Parse(message!);
            return json.RootElement.Clone();
        })
        .Where(e => e.TryGetProperty("operation", out var name) && name.GetString() == operation)
        .ToArray();

    private static void Sandbox(Action<string, FileLogger> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-cleanup-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root, new FileLogger(Path.Combine(root, "PinkieSysMon.Runtime.log"), "Runtime")); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
