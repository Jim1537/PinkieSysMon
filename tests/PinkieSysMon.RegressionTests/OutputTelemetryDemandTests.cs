using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class OutputTelemetryDemandTests
{
    private const string Provider = "test.demand";
    private const string Shared = "test.demand.shared";
    private const string Left = "test.demand.left";
    private const string Right = "test.demand.right";
    private const string Changed = "test.demand.changed";

    public static void ActiveUnion()
    {
        InSandbox(log =>
        {
            var cfg = Config(("a", true), ("b", true));
            var boards = Boards(("a", Board(Shared, Left)), ("b", Board(Shared, Right)));
            var source = new CountingSource(Shared, Left, Right, Changed);
            using var manager = Manager(cfg, boards, log);
            var metrics = new MetricStore();
            var policy = TelemetryConfig.CreateDefaults([source]);
            using var engine = new TelemetryEngine(metrics, [source], policy,
                [Provider], Array.Empty<string>(), log);
            engine.Start();

            Check(!SpinWait.SpinUntil(() => source.Captures > 0, TimeSpan.FromMilliseconds(250)),
                "With no active output demand, the shared source must not be polled.");
            manager.Start();
            var active = manager.GetRequiredMetrics();
            Check(active.Count == 3 && active.SetEquals(new[] { Shared, Left, Right }),
                "Two active boards must request the union of three unique metric IDs.");
            engine.Reconfigure(policy, [Provider], active);

            Check(SpinWait.SpinUntil(() => metrics.Snapshot().TryGetValue(Right, out var v) && v is not null,
                    TimeSpan.FromSeconds(3)),
                "Shared acquisition must publish both dashboards' requested metrics.");
            Check(source.DuplicateRequests == 0 && source.Captures > 0,
                "One shared acquisition schedule must not request the same metric twice.");

            // The due set is captured once for the shared source, not once per board.
            Check(source.FullUnionBatches > 0,
                "One shared source capture must request the three unique metrics together.");
        });
    }

    public static void InitialCaptureNotifiesOnce()
    {
        InSandbox(log =>
        {
            var source = new CountingSource(Shared);
            var store = new MetricStore();
            var config = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [Shared] = new() { Enabled = true, IntervalMs = 600_000 }
                }
            };
            using var engine = new TelemetryEngine(store, [source], config,
                [Provider], Array.Empty<string>(), log);
            var callbacks = 0;
            engine.InitialValuesPublished += () =>
            {
                Check(store.Snapshot().TryGetValue(Shared, out var value) && Equals(value, 42d),
                    "Initial telemetry notification must follow publication of the first value.");
                Interlocked.Increment(ref callbacks);
            };
            engine.Start();
            engine.Reconfigure(config, [Provider], [Shared]);
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref callbacks) == 1, TimeSpan.FromSeconds(3)),
                "A ten-minute polling interval must still publish and notify immediately on activation.");
            Thread.Sleep(200);
            Check(Volatile.Read(ref callbacks) == 1,
                "An initial capture must not repeat a notification on normal scheduler wakeups.");

            engine.SetIdlePaused(true);
            engine.SetIdlePaused(false);
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref callbacks) == 2, TimeSpan.FromSeconds(3)),
                "Idle wake must notify after its fresh capture, not wait for the full interval.");
            engine.Reconfigure(config, Array.Empty<string>(), [Shared]);
            Thread.Sleep(200);
            Check(Volatile.Read(ref callbacks) == 2,
                "Disabled providers must not fabricate an initial-data notification.");
        });
    }

    public static void Transitions()
    {
        InSandbox(log =>
        {
            var cfg = Config(("a", true), ("b", true));
            var boards = Boards(("a", Board(Shared, Left)), ("b", Board(Shared, Right)));
            var newA = Board(Shared, Changed);
            using var manager = Manager(cfg, boards, log);
            var source = new CountingSource(Shared, Left, Right, Changed);
            var store = new MetricStore();
            var telemetryCfg = TelemetryConfig.CreateDefaults([source]);
            using var engine = new TelemetryEngine(store, [source], telemetryCfg,
                [Provider], Array.Empty<string>(), log);

            void Reconcile() => engine.Reconfigure(
                telemetryCfg, [Provider], manager.GetRequiredMetrics());
            engine.Start();
            manager.Start();
            Reconcile();
            Check(SpinWait.SpinUntil(() =>
                store.Snapshot().TryGetValue(Left, out var v) && v is not null &&
                store.Snapshot().TryGetValue(Right, out var w) && w is not null,
                TimeSpan.FromSeconds(3)), "Both active boards must start publishing.");

            manager.Suspend();
            Reconcile();
            Check(manager.GetRequiredMetrics().Count == 0 &&
                  store.Snapshot().TryGetValue(Shared, out var sleepValue) && sleepValue is null,
                  "Suspended sessions must not demand telemetry or retain valid readings.");
            manager.Resume();
            Reconcile();
            Check(SpinWait.SpinUntil(() =>
                store.Snapshot().TryGetValue(Left, out var awake) && awake is not null,
                TimeSpan.FromSeconds(3)),
                "Resume must reactivate live board telemetry without needing to restart outputs.");

            var disabledA = OutputLifecycleTestConfiguration.WithEnabled(cfg, "a", false);
            manager.Reconfigure(disabledA, manager.GetDashboardAssignments());
            Reconcile();
            Check(store.Snapshot().TryGetValue(Left, out var gone) && gone is null &&
                manager.GetRequiredMetrics().SetEquals(new[] { Shared, Right }),
                "Disabling A must clear its exclusive metric, retaining B's shared metric.");
            Check(store.Snapshot().TryGetValue(Shared, out var still) && still is not null,
                "Disabling A must not clear a metric still required by B.");

            var disabledBoth = OutputLifecycleTestConfiguration.WithEnabled(disabledA, "b", false);
            manager.Reconfigure(disabledBoth, manager.GetDashboardAssignments());
            Reconcile();
            Check(store.Snapshot().TryGetValue(Shared, out var cleared) && cleared is null &&
                  store.Snapshot().TryGetValue(Right, out var clearedB) && clearedB is null &&
                  manager.GetRequiredMetrics().Count == 0,
                "Disabling the last requesting Output must publish unavailable values immediately.");
            Thread.Sleep(240);
            var count = source.Captures;
            Thread.Sleep(240);
            Check(source.Captures == count, "After demand has drained, source capture must cease.");

            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(disabledBoth, "a", true),
                manager.GetDashboardAssignments());
            Reconcile();
            Check(SpinWait.SpinUntil(() =>
                    store.Snapshot().TryGetValue(Left, out var live) && live is not null,
                    TimeSpan.FromSeconds(3)),
                "Enabling a target must resume its metrics without a second Start command.");

            manager.ReloadTarget(cfg.Outputs.Targets.Single(t => t.Id == "a"), newA, _ => { });
            Reconcile();
            Check(store.Snapshot().TryGetValue(Left, out var removed) && removed is null &&
                  manager.GetRequiredMetrics().SetEquals(new[] { Shared, Changed }),
                "Target-specific board reload must remove old demand without touching siblings.");
            Check(SpinWait.SpinUntil(() =>
                    store.Snapshot().TryGetValue(Changed, out var updated) && updated is not null,
                    TimeSpan.FromSeconds(3)), "New board metrics must enter the shared schedule.");

            var noPhysical = Config(("a", false), ("b", false));
            manager.Reconfigure(noPhysical, Boards(("a", newA), ("b", Board(Shared, Right))));
            Reconcile();
            Check(manager.GetRequiredMetrics().Count == 0 &&
                  store.Snapshot().TryGetValue(Changed, out var off) && off is null,
                  "Disabling physical devices on configuration reload must drop board demand.");

            // Global startup/shutdown retains the singleton scheduler and saved
            // enablement. User Reload All is covered by SavedConfigurationReloadTests.
            manager.StopAll();
            Reconcile();
            Check(manager.GetRequiredMetrics().Count == 0,
                "Stop All must clear demand from every output.");
            manager.Start();
            Reconcile();
            Check(manager.GetRequiredMetrics().Count == 0 &&
                  manager.GetStatuses().All(status => !status.IsActive),
                "Global startup must not reactivate devices explicitly disabled in configuration.");

            // Restore the original fixture board and explicitly re-enable
            // both physical devices BEFORE testing restoration after Stop All.
            manager.Reconfigure(cfg, boards);
            Reconcile();
            Check(manager.GetRequiredMetrics().SetEquals(new[] { Shared, Left, Right }),
                "Explicitly re-enabled physical targets must restore shared metric demand.");

            manager.StopAll();
            Reconcile();
            Check(manager.GetRequiredMetrics().Count == 0 &&
                  store.Snapshot().TryGetValue(Shared, out var allStopped) && allStopped is null,
                "Stop All must clear previously active shared metric values.");
            manager.Start();
            Reconcile();
            Check(manager.GetRequiredMetrics().SetEquals(new[] { Shared, Left, Right }),
                "Global startup must restore the deduplicated union of enabled devices after a complete stop.");

            // Provider availability and per-metric policy remain authoritative
            // even when an active board requests a metric.
            manager.Reconfigure(cfg, boards);
            Reconcile();
            engine.Reconfigure(telemetryCfg, Array.Empty<string>(), manager.GetRequiredMetrics());
            Check(store.Snapshot().TryGetValue(Shared, out var disabled) && disabled is null,
                "A globally disabled provider must clear its values despite board demand.");
        });
    }

    public static void DesktopPolicy()
    {
        var cfg = new AppConfig
        {
            Outputs = new OutputConfig
            {
                Targets =
                [
                    new OutputTargetConfig
                    {
                        Id = "desktop-a", Name = "Desktop A",
                        Kind = OutputTargetContract.DesktopKind,
                        Transport = OutputTargetContract.DesktopTransport,
                        ProviderEnabled = true,
                        Desktop = new DesktopOutputConfig { BoardEnabled = false }
                    },
                    new OutputTargetConfig
                    {
                        Id = "desktop-b", Name = "Desktop B",
                        Kind = OutputTargetContract.DesktopKind,
                        Transport = OutputTargetContract.DesktopTransport,
                        ProviderEnabled = false,
                        Desktop = new DesktopOutputConfig { BoardEnabled = true }
                    },
                    Physical("device", true)
                ]
            }
        };
        var boards = Boards(("desktop-a", Board(Left)),
            ("desktop-b", Board(Right)), ("device", Board(Shared)));

        var demand = OutputTelemetryDemand.Collect(cfg, boards,
            ["desktop-a", "desktop-b", "device"]);
        Check(demand.SetEquals(new[] { Shared, Right }),
            "Hidden Desktop must not demand metrics even with ProviderEnabled ON; visible board must demand with ProviderEnabled OFF.");

        cfg.Outputs.Targets[0].Desktop!.BoardEnabled = true;
        cfg.Outputs.Targets[1].Desktop!.BoardEnabled = false;
        demand = OutputTelemetryDemand.Collect(cfg, boards,
            ["desktop-a", "desktop-b", "device"]);
        Check(demand.SetEquals(new[] { Shared, Left }),
            "Changing Board Enabled must change demand independently of Provider Enabled.");

        cfg.Outputs.Targets[2].DeviceEnabled = false;
        demand = OutputTelemetryDemand.Collect(cfg, boards, ["device"]);
        Check(demand.Count == 0, "Disabled hardware must not demand or open a device.");
        cfg.Outputs.Targets[2].DeviceEnabled = true;
        demand = OutputTelemetryDemand.Collect(cfg, boards, ["desktop-a"]);
        Check(demand.SetEquals(new[] { Left }), "No configured but inactive hardware may create demand.");

        cfg.Outputs.Targets[0].Desktop!.BoardEnabled = false;
        demand = OutputTelemetryDemand.Collect(cfg, boards, ["desktop-a"]);
        Check(demand.Count == 0,
            "An active, hidden Desktop has no dashboard demand even if it remains a status provider.");
    }

    private static AppConfig Config(params (string Id, bool Enabled)[] physicals) => new()
    {
        Outputs = new OutputConfig
        {
            Targets = physicals.Select(p => Physical(p.Id, p.Enabled)).ToList()
        }
    };

    private static OutputTargetConfig Physical(string id, bool enabled) => new()
    {
        Id = id, Name = id, Dashboard = "Default",
        DeviceId = "trofeo:" + new string(id[0], 64),
        DeviceEnabled = enabled
    };

    private static Dictionary<string, C.DashboardDefinition> Boards(
        params (string Id, C.DashboardDefinition Board)[] entries) =>
        entries.ToDictionary(p => p.Id, p => p.Board, StringComparer.OrdinalIgnoreCase);

    private static C.DashboardDefinition Board(params string[] names) => new()
    {
        Widgets = names.Select((name, i) => (C.WidgetDefinition)new C.ValueWidgetDefinition
        {
            Id = $"metric-{i}", SourceKind = C.ValueSourceKind.Metric, Metric = name
        }).ToList()
    };

    private static OutputSessionManager Manager(
        AppConfig cfg, IReadOnlyDictionary<string, C.DashboardDefinition> boards,
        FileLogger log) =>
        new(cfg, boards, new MetricStore(), log,
            (app, target, board, store, logger) => new FakeSession(target.Id, target.Name));

    private static void InSandbox(Action<FileLogger> work)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-stage05-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try { work(new FileLogger(Path.Combine(dir, "test.log"))); }
        finally { Directory.Delete(dir, true); }
    }

    private static void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException(text);
    }

    private sealed class FakeSession(string id, string name) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => name;
        public bool IsConnected => false;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig app, OutputTargetConfig target, C.DashboardDefinition board) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration plan) { }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
        private sealed class Plan : IOutputSessionReconfiguration { public void Dispose() { } }
    }

    private sealed class CountingSource(params string[] names) : IMetricSource
    {
        private int _captures;
        private int _duplicates;
        private int _fullUnionBatches;
        public int Captures => Volatile.Read(ref _captures);
        public int DuplicateRequests => Volatile.Read(ref _duplicates);
        public int FullUnionBatches => Volatile.Read(ref _fullUnionBatches);
        public string ProviderId => Provider;
        public string Name => "Counting shared source";
        public int DefaultIntervalMs => 100;
        public IReadOnlyCollection<string> MetricNames { get; } = names;
        public IReadOnlyDictionary<string, object?> Capture() => Capture(MetricNames);
        public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
        {
            Interlocked.Increment(ref _captures);
            if (requestedMetrics.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedMetrics.Count)
                Interlocked.Increment(ref _duplicates);
            if (requestedMetrics.Count == 3 &&
                requestedMetrics.Contains(Shared) && requestedMetrics.Contains(Left) &&
                requestedMetrics.Contains(Right))
                Interlocked.Increment(ref _fullUnionBatches);
            return requestedMetrics.ToDictionary(n => n, n => (object?)42d,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
