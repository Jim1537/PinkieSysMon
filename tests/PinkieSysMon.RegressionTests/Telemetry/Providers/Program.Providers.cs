using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void TelemetryProviderFailureIsIsolated()
    {
        const string badMetric = "test.bad.value";
        const string goodMetric = "test.good.value";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ThrowingMetricSource("test.bad", "bad-source", badMetric),
                new ConstantMetricSource("test.good", "good-source", goodMetric, 42d)
            ];
            var config = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [badMetric] = new() { Enabled = true, IntervalMs = 100 },
                    [goodMetric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };

            using var engine = new TelemetryEngine(
                store,
                sources,
                config,
                new[] { "test.bad", "test.good" },
                new[] { badMetric, goodMetric },
                log);
            engine.Start();

            var published = SpinWait.SpinUntil(
                () => store.Snapshot().ContainsKey(goodMetric),
                TimeSpan.FromSeconds(2));
            Assert(published, "Healthy provider was never published after the preceding provider threw.");

            var snapshot = store.Snapshot();
            Assert(snapshot.TryGetValue(goodMetric, out var good) && Convert.ToDouble(good) == 42d,
                "Healthy provider value was not preserved.");
            Assert(snapshot.TryGetValue(badMetric, out var bad) && bad is null,
                "Failed provider metric must be explicitly published as unavailable.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TelemetryReconfigurationClearsDeactivatedMetrics()
    {
        const string metric = "test.reconfigure.value";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ConstantMetricSource("test.reconfigure", "reconfigure-source", metric, 42d)
            ];
            var enabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };
            var disabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = false, IntervalMs = 100 }
                }
            };

            using var engine = new TelemetryEngine(
                store,
                sources,
                enabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric },
                log);
            engine.Start();

            var published = SpinWait.SpinUntil(
                () => store.Snapshot().TryGetValue(metric, out var value) && value is not null,
                TimeSpan.FromSeconds(2));
            Assert(published, "Telemetry metric was never published before the reconfiguration test.");

            engine.Reconfigure(
                disabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric });
            Assert(store.Snapshot().TryGetValue(metric, out var disabledValue) && disabledValue is null,
                "A metric disabled by telemetry configuration must be published as unavailable immediately.");

            engine.Reconfigure(
                enabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric });
            var republished = SpinWait.SpinUntil(
                () => store.Snapshot().TryGetValue(metric, out var value) && value is not null,
                TimeSpan.FromSeconds(2));
            Assert(republished, "Telemetry metric did not resume publishing after being re-enabled.");

            engine.Reconfigure(
                enabledConfig,
                new[] { "test.reconfigure" },
                Array.Empty<string>());
            Assert(store.Snapshot().TryGetValue(metric, out var noLongerRequiredValue) && noLongerRequiredValue is null,
                "A metric removed from dashboard demand must be published as unavailable immediately.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
    private static void TelemetryReconfigurationPrepareCommitIsolation()
    {
        const string metric = "test.reconfigure.prepare";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ConstantMetricSource("test.reconfigure.prepare", "prepare-source", metric, 42d)
            ];
            var enabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };
            var disabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = false, IntervalMs = 100 }
                }
            };

            var engine = new TelemetryEngine(
                store,
                sources,
                enabledConfig,
                new[] { "test.reconfigure.prepare" },
                new[] { metric },
                log);
            try
            {
                store.Publish(metric, 42d);
                var plan = engine.PrepareReconfiguration(
                    disabledConfig,
                    new[] { "test.reconfigure.prepare" },
                    new[] { metric });

                Assert(store.Snapshot().TryGetValue(metric, out var beforeCommit) &&
                       Convert.ToDouble(beforeCommit, CultureInfo.InvariantCulture) == 42d,
                    "Preparing telemetry reconfiguration must not mutate the live metric store or active schedule.");

                engine.CommitReconfiguration(plan);
                Assert(store.Snapshot().TryGetValue(metric, out var afterCommit) && afterCommit is null,
                    "Committing a prepared telemetry reconfiguration must apply deactivation semantics.");

                engine.Dispose();
                engine.Dispose();
            }
            finally
            {
                engine.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }


}
