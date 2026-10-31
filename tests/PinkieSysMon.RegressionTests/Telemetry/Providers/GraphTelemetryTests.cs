using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class GraphTelemetryTests
{
    public static void PublicationAndSerializedDiscovery()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var source = new Source(SystemMetricContract.Uptime, MetricProviderContract.System, call =>
        { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Capture release timed out."); return call; });
        var history = new MetricHistoryStore(); var store = new MetricStore(history);
        using var engine = new TelemetryEngine(store, [source], Policy(source.Metric), [source.ProviderId], [source.Metric], Log());
        engine.SetHistoryDemand([new(source.Metric, 60)]); engine.Start();
        Check(entered.Wait(TimeSpan.FromSeconds(10)), "Initial capture did not start.");
        var operation = engine.RunSourceOperationAsync(() => { Check(source.Active == 0, "Discovery overlapped capture."); return 42; });
        Check(!operation.IsCompleted, "Discovery must queue asynchronously behind a blocked capture.");
        release.Set();
        Check(operation.Wait(TimeSpan.FromSeconds(10)) && operation.Result == 42, "Queued discovery did not complete.");
        var samples = new MetricHistorySample[8192];
        var initial = history.CopyTo(source.Metric, 60, history.GetTimestamp(), samples);
        Check(initial.Count == 1 && samples[0].Number == 1, "One actual capture must produce one observation.");
        engine.SetHistoryDemand([new(source.Metric, 10), new(source.Metric, 5)]);
        var fence = engine.RunSourceOperationAsync(() => source.Calls);
        Check(fence.Wait(TimeSpan.FromSeconds(10)) && fence.Result == 1 && history.SeriesCount == 1,
            "A history-window-only change must neither duplicate buffers nor force provider capture.");
        Check(history.CopyTo(source.Metric, 10, history.GetTimestamp(), samples).Epoch == initial.Epoch,
            "A window-only reconciliation must retain the source epoch.");
        engine.SetHistoryDemand([]);
        Check(history.SeriesCount == 0, "Removing the final Graph must release buffers while ordinary telemetry can remain demanded.");
    }

    public static void PausedCaptureAndSemanticGenerationFences()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var published = new ManualResetEventSlim();
        using var source = new Source(SystemMetricContract.Uptime, MetricProviderContract.System, call =>
        {
            if (call == 1) { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Capture release timed out."); }
            return call * 10;
        });
        var history = new MetricHistoryStore(); var store = new MetricStore(history);
        using var engine = new TelemetryEngine(store, [source], Policy(source.Metric), [source.ProviderId], [source.Metric], Log());
        engine.InitialValuesPublished += published.Set;
        engine.SetHistoryDemand([new(source.Metric, 60)]); engine.Start();
        Check(entered.Wait(TimeSpan.FromSeconds(10)), "Initial capture did not start.");
        engine.SetIdlePaused(true); engine.SetHistoryDemand([]);
        var fence = engine.RunSourceOperationAsync(() => true); release.Set();
        Check(fence.Wait(TimeSpan.FromSeconds(10)) && !store.Snapshot().ContainsKey(source.Metric),
            "An in-flight pre-pause batch must not be published after the fence.");
        engine.SetHistoryDemand([new(source.Metric, 60, OwnerGeneration: 1)]); engine.SetIdlePaused(false);
        Check(published.Wait(TimeSpan.FromSeconds(10)), "Resume did not publish a fresh capture.");
        var samples = new MetricHistorySample[8192];
        var query = history.CopyTo(source.Metric, 60, history.GetTimestamp(), samples);
        Check(query.Count == 1 && samples[0].Number == 20 && !samples[0].ConnectsToPrevious,
            "Resume must start a fresh run with no stale batch or missed-cycle backfill.");
    }

    public static void CatalogMeaningAndDynamicPublication()
    {
        const string metric = "lhm.graph-regression";
        var previous = MetricContract.Descriptors.Where(d => d.Id.StartsWith("lhm.", StringComparison.OrdinalIgnoreCase)).ToArray();
        try
        {
            MetricContract.ReplaceProviderDescriptors("lhm", [new(metric, MetricValueKind.Number, MetricUnit.Celsius)]);
            using var published = new ManualResetEventSlim();
            using var source = new Source(metric, "lhm", call =>
            {
                if (call > 1) MetricContract.ReplaceProviderDescriptors("lhm", [new(metric, MetricValueKind.Number, MetricUnit.Watts)]);
                return 25;
            });
            var history = new MetricHistoryStore(); var store = new MetricStore(history);
            using var engine = new TelemetryEngine(store, [source], Policy(metric), ["lhm"], [metric], Log());
            engine.InitialValuesPublished += published.Set;
            engine.SetHistoryDemand([new(metric, 60)]); engine.Start();
            Check(published.Wait(TimeSpan.FromSeconds(10)), "Initial catalog epoch did not publish.");
            var old = history.CopyTo(metric, 60, history.GetTimestamp(), new MetricHistorySample[8192]);
            published.Reset(); engine.Reconfigure(Policy(metric), ["lhm"], [metric]);
            Check(published.Wait(TimeSpan.FromSeconds(10)), "Changed catalog epoch did not publish.");
            var samples = new MetricHistorySample[8192]; var next = history.CopyTo(metric, 60, history.GetTimestamp(), samples);
            Check(old.Descriptor?.BaseUnit == MetricUnit.Celsius && next.Descriptor?.BaseUnit == MetricUnit.Watts &&
                  next.Epoch != old.Epoch && next.Count == 1, "A source's changed meaning must clear history before admitting its new publication.");
        }
        finally { MetricContract.ReplaceProviderDescriptors("lhm", previous); }
        var directHistory = new MetricHistoryStore(); var directStore = new MetricStore(directHistory);
        using var directEngine = new TelemetryEngine(directStore, [], new(), [], [], Log());
        var outputMetric = OutputMetricContract.Id("graph-regression", OutputMetricContract.Fps);
        directEngine.SetHistoryDemand([new(outputMetric, 60)]);
        directStore.Publish(outputMetric, 24d); directStore.Publish(outputMetric, 24d);
        var raw = new MetricHistorySample[8192];
        Check(directHistory.CopyTo(outputMetric, 60, directHistory.GetTimestamp(), raw).Count == 2 && raw[1].ConnectsToPrevious,
            "Direct Output publications must use the same history boundary and established one-second continuity policy.");
    }

    internal static FileLogger Log() => new(Path.Combine(Path.GetTempPath(), "Pinkie-Graph-tests-" + Guid.NewGuid().ToString("N") + ".log"));
    internal static TelemetryConfig Policy(string metric) => new()
    { Metrics = new(StringComparer.OrdinalIgnoreCase) { [metric] = new() { IntervalMs = 60_000 } } };
    private static void Check(bool value, string message) => GraphWidgetTests.Check(value, message);

    internal sealed class Source(string metric, string provider, Func<int, object?> capture) : IMetricSource, IDisposable
    {
        public string Metric => metric;
        public string ProviderId => provider;
        public string Name => "graph-fixture";
        public IReadOnlyCollection<string> MetricNames => [metric];
        public int DefaultIntervalMs => 60_000;
        public int Calls;
        public int Active;
        public bool Disposed;
        public IReadOnlyDictionary<string, object?> Capture()
        {
            Check(!Disposed, "Capture after source disposal.");
            Check(Interlocked.Increment(ref Active) == 1, "Concurrent source operation detected.");
            try { return new Dictionary<string, object?> { [metric] = capture(Interlocked.Increment(ref Calls)) }; }
            finally { Interlocked.Decrement(ref Active); }
        }
        public void Dispose() { Check(Active == 0, "Source disposal raced terminal capture."); Disposed = true; }
    }
}
