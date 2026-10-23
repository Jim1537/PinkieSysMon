using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class GraphPreviewLifecycleTests
{
    public static void DiscoveryCoalescingAndTerminalShutdown()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var source = new GraphTelemetryTests.Source(SystemMetricContract.Uptime, MetricProviderContract.System, _ =>
        { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Capture release timed out."); return 120; });
        var discoveries = 0;
        var preview = new EditorPreviewTelemetry([source], GraphTelemetryTests.Policy(source.Metric), GraphTelemetryTests.Log(),
            _ => { Check(source.Active == 0, "Discovery overlapped preview capture."); discoveries++; return EditorPreviewCatalog.Empty; }, _ => { });
        try
        {
            preview.Configure(new AppConfig(), Board());
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Preview capture did not start.");
            var first = preview.RefreshCatalogAsync(); var second = preview.RefreshCatalogAsync();
            Check(ReferenceEquals(first, second) && !first.IsCompleted, "Pending discovery must be coalesced without blocking the caller.");
            release.Set(); Check(first.Wait(TimeSpan.FromSeconds(10)) && discoveries == 1, "Serialized discovery did not complete once.");
            var previous = preview.History.CopyTo(source.Metric, 60, preview.History.GetTimestamp(), new MetricHistorySample[8192]);
            var board = Board(); ((C.GraphWidgetDefinition)board.Widgets[0]).Color = "#123456";
            preview.Configure(new AppConfig(), board);
            var next = preview.History.CopyTo(source.Metric, 60, preview.History.GetTimestamp(), new MetricHistorySample[8192]);
            Check(previous.Epoch == next.Epoch && previous.Count == next.Count, "An ordinary appearance edit must retain preview history.");
        }
        finally { release.Set(); Check(preview.StopAsync().Wait(TimeSpan.FromSeconds(10)), "Preview shutdown did not complete."); }
        Check(source.Disposed && source.Active == 0, "Preview must dispose its source only after terminal worker completion.");
    }

    public static void BlockedCaptureCloseFenceAndIsolation()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var source = new GraphTelemetryTests.Source(SystemMetricContract.Uptime, MetricProviderContract.System, _ =>
        { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Capture release timed out."); return 999; });
        var preview = new EditorPreviewTelemetry([source], GraphTelemetryTests.Policy(source.Metric), GraphTelemetryTests.Log(),
            _ => EditorPreviewCatalog.Empty, _ => { });
        try
        {
            preview.Configure(new AppConfig(), Board());
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Blocked preview capture did not start.");
            var terminal = preview.StopAsync();
            Check(!terminal.IsCompleted && !source.Disposed, "Confirmed close must return pending completion without disposing an active source.");
            release.Set(); Check(terminal.Wait(TimeSpan.FromSeconds(10)), "Terminal preview shutdown timed out.");
            Check(!preview.Store.Snapshot().ContainsKey(source.Metric) && preview.History.SeriesCount == 0,
                "A late capture must not publish after closing or retain history ownership.");
        }
        finally { release.Set(); preview.StopAsync().GetAwaiter().GetResult(); }
        var runtimeHistory = new MetricHistoryStore(); var runtime = new MetricStore(runtimeHistory);
        runtimeHistory.SetDemand([new(SystemMetricContract.Uptime, 60)]); runtime.Publish(SystemMetricContract.Uptime, 42);
        Check(!preview.Store.Snapshot().ContainsKey(SystemMetricContract.Uptime) && runtimeHistory.SeriesCount == 1,
            "Editor disposal/data must remain independent of a Runtime-owned history instance.");
    }

    public static void StaleDiscoveryAndIllustrativeOutputHistory()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var source = new GraphTelemetryTests.Source(SystemMetricContract.Uptime, MetricProviderContract.System, _ => 1);
        var preview = new EditorPreviewTelemetry([source], GraphTelemetryTests.Policy(source.Metric), GraphTelemetryTests.Log(), _ =>
        {
            entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Discovery release timed out.");
            return new EditorPreviewCatalog([], [], [], true, true);
        }, _ => { });
        try
        {
            var config = new AppConfig(); preview.Configure(config, Board());
            var pending = preview.RefreshCatalogAsync();
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Discovery did not start.");
            config.MetricProviders[MetricProviderContract.System] = false;
            preview.Configure(config, Board()); release.Set();
            Check(pending.Wait(TimeSpan.FromSeconds(10)) && !preview.Catalog.LhmAvailable,
                "A superseded catalog result must not overwrite the committed preview generation.");
            var board = Board(); var metric = OutputMetricContract.Id("editor-fixture", OutputMetricContract.Fps);
            ((C.GraphWidgetDefinition)board.Widgets[0]).Metric = metric;
            preview.Configure(config, board);
            Check(preview.History.CopyTo(metric, 60, preview.History.GetTimestamp(), new MetricHistorySample[8192]).Count == 0,
                "Illustrative Editor Output readings must never be periodically republished as real Graph history.");
        }
        finally { release.Set(); preview.StopAsync().GetAwaiter().GetResult(); }
    }

    private static C.DashboardDefinition Board() => GraphWidgetTests.Board(new C.GraphWidgetDefinition
    { Id = "preview-graph", Metric = SystemMetricContract.Uptime, WindowSeconds = 60, Width = 100, Height = 100 });
    private static void Check(bool value, string message) => GraphWidgetTests.Check(value, message);
}
