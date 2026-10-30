using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105TelemetryLoggingTests
{
    public static void CaptureUnavailableRecoveryAndFirstPublication()
    {
        Sandbox((root, log) =>
        {
            var source = new Source();
            using var published = new ManualResetEventSlim();
            var store = new MetricStore();
            using (var engine = new TelemetryEngine(store, [source], GraphTelemetryTests.Policy(source.Metric),
                [source.ProviderId], [source.Metric], log))
            {
                engine.InitialValuesPublished += published.Set;
                engine.Start();
                Check(published.Wait(TimeSpan.FromSeconds(10)), "Failed initial capture must still publish unavailable.");
                Check(engine.RunSourceOperationAsync(() => true).Wait(TimeSpan.FromSeconds(10)), "First capture fence.");
                Check(store.Snapshot()[source.Metric] is null, "Capture failure cannot fabricate a value.");
                for (var mode = 1; mode <= 2; mode++)
                {
                    published.Reset(); source.Mode = mode;
                    engine.Reconfigure(GraphTelemetryTests.Policy(source.Metric), [source.ProviderId], [source.Metric]);
                    Check(published.Wait(TimeSpan.FromSeconds(10)), "Reconfigured batch did not publish.");
                    Check(engine.RunSourceOperationAsync(() => true).Wait(TimeSpan.FromSeconds(10)), "Publication fence.");
                }
                engine.SetIdlePaused(true);
                Check(engine.RunSourceOperationAsync(() => true).Wait(TimeSpan.FromSeconds(10)), "Pause fence.");
                Check(store.Snapshot()[source.Metric] is double, "Recovery publishes the real source value.");
            }
            var states = Events(log.Path, "telemetry.state");
            Check(states.Any(e => e.GetProperty("state").GetString() == "capture.failed") &&
                states.Any(e => e.GetProperty("state").GetString() == "requested=1; available=0") &&
                states.Any(e => e.GetProperty("state").GetString() == "requested=1; available=1"),
                "Failure, returned unavailable and recovered captures remain distinct.");
            Check(Text(log.ErrorsPath).Contains("Injected provider failure") &&
                !Text(log.Path).Contains("Injected provider failure") &&
                Text(log.Path).Contains("Telemetry first batch published"), "Original error routing and actual first publication are retained.");
        });
    }

    public static void PendingWorkerAndQueueCounts()
    {
        Sandbox((root, log) =>
        {
            long now = Stopwatch.GetTimestamp();
            using (var progress = new WorkerProgressDiagnostics(log, "fixture.worker", () => now, startTimer: false))
            {
                progress.Begin("source.capture", "fixture");
                now += 31 * Stopwatch.Frequency;
                progress.Observe(); progress.Observe();
                Check(Events(log.ErrorsPath, "fixture.worker.pending").Single().GetProperty("ownershipChanged").GetBoolean() == false,
                    "Pending observation cannot release or complete the worker.");
                progress.Complete(); progress.Observe();
            }
            Check(Events(log.Path, "fixture.worker.progress.resumed").Length == 1 &&
                Text(log.ErrorsPath).Contains("suppressed=1"), "Return flushes pending repetition and preserves recovery.");
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using (var engine = new TelemetryEngine(new MetricStore(), [], new(), [], [], log))
            {
                engine.Start();
                var blocker = engine.RunSourceOperationAsync(() =>
                { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Queue release timed out."); return true; });
                try
                {
                    Check(entered.Wait(TimeSpan.FromSeconds(10)), "Worker did not enter queued operation.");
                    var queued = Enumerable.Range(0, 64).Select(i => engine.RunSourceOperationAsync(() => i)).ToArray();
                    var denied = engine.RunSourceOperationAsync(() => -1);
                    Check(denied.IsFaulted && denied.Exception!.InnerException is InvalidOperationException,
                        "Full queue retains the existing explicit rejection contract.");
                    release.Set();
                    Check(Task.WhenAll(queued).Wait(TimeSpan.FromSeconds(10)) && blocker.Wait(TimeSpan.FromSeconds(10)),
                        "Accepted operations drain without replacement or retry.");
                }
                finally { release.Set(); }
            }
            Check(Text(log.Path).Contains("rejected=1") && Text(log.Path).Contains("completed=65"),
                "Final observer totals retain quiet queue completions and saturation.");
        });
    }

    public static void IcueFreshnessFileAndCatalogGenerations()
    {
        Sandbox((root, log) =>
        {
            var directory = Path.Combine(root, "logs"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "corsair_cue_PRIVATE-FILE.csv");
            File.WriteAllText(path, "Timestamp,PRIVATE-SENSOR\n0,123456789\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            using (var source = new IcueSensorLogTelemetrySource(root, log))
            {
                Check(source.RefreshCatalog(), "Synthetic CSV catalog should load.");
                var metric = source.MetricNames.Single();
                Check(source.Capture([metric])[metric] is null, "Stale sample must remain unavailable.");
                File.AppendAllText(path, "1,42\n");
                Check(source.Capture([metric])[metric] is not null, "New complete sample restores freshness.");
                source.RefreshCatalog(); // Same schema is not a new catalog generation.
                File.WriteAllText(path, "Timestamp,PRIVATE-SENSOR\n2,1\n");
                source.Capture([metric]);
            }
            var states = Events(log.Path, "telemetry.icue.state");
            Check(states.Any(e => e.GetProperty("state").GetString() == "stale") &&
                states.Any(e => e.GetProperty("state").GetString() == "fresh") &&
                Text(log.Path).Contains("truncations=1") && Text(log.Path).Contains("catalogGeneration=1"),
                "Freshness, truncation and unchanged catalog generation remain observable.");
            Check(!Text(log.Path).Contains("PRIVATE-FILE") && !Text(log.Path).Contains("PRIVATE-SENSOR"),
                "Routine provider diagnostics contain counts/generations rather than file or sensor names.");
        });
    }

    public static void NetworkOfflineFailureAndRecovery()
    {
        Sandbox((root, log) =>
        {
            var mode = 0;
            using (var source = new WindowsNetworkTelemetrySource(log, () => mode switch
            { 0 => false, 1 => throw new System.ComponentModel.Win32Exception(5, "Injected NLM failure"), _ => true }))
            {
                source.Capture(); mode = 1; source.Capture(); source.Capture(); mode = 2; source.Capture();
            }
            var states = Events(log.Path, "telemetry.network.state");
            Check(states.Any(e => e.GetProperty("state").GetString() == "offline") &&
                states.Any(e => e.GetProperty("state").GetString() == "query-failed") &&
                states.Any(e => e.GetProperty("state").GetString() == "online"),
                "No internet is a normal state, distinct from a failed NLM query.");
            Check(Text(log.ErrorsPath).Contains("\"nativeCode\":5") && Text(log.ErrorsPath).Contains("suppressed=1") &&
                !Text(log.Path).Contains("Injected NLM failure"), "Native exception and final repetition remain only in Errors.");
        });
    }

    private sealed class Source : IMetricSource
    {
        internal int Mode;
        public string Metric => SystemMetricContract.Uptime;
        public string ProviderId => MetricProviderContract.System;
        public string Name => "logging-fixture";
        public IReadOnlyCollection<string> MetricNames => [Metric];
        public int DefaultIntervalMs => 60000;
        public IReadOnlyDictionary<string, object?> Capture()
        {
            if (Mode == 0) throw new InvalidOperationException("Injected provider failure");
            return new Dictionary<string, object?> { [Metric] = Mode == 1 ? null : 42d };
        }
    }

    private static JsonElement[] Events(string path, string name) => Records(path)
        .Select(e => e.GetProperty("message").GetString()).Where(m => m?.StartsWith('{') == true)
        .Select(m => { using var j = JsonDocument.Parse(m!); return j.RootElement.Clone(); })
        .Where(e => e.GetProperty("eventName").GetString() == name).ToArray();
    private static JsonElement[] Records(string path) => Text(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var j = JsonDocument.Parse(line[line.IndexOf('{')..]); return j.RootElement.Clone(); }).ToArray();
    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () =>
        string.Concat(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.archive").Order()
            .Select(File.ReadAllText)) + (File.Exists(path) ? File.ReadAllText(path) : ""));
    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-telemetry-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = new FileLogger(Path.Combine(root, "session.log"), "Diagnostic");
        try
        {
            action(root, log);
            Check(Records(log.Path).Concat(Records(log.ErrorsPath)).All(e => e.GetProperty("loggingFailures").GetInt32() == 0),
                "No fixture logging loss is allowed.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
