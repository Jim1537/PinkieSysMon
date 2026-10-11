using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class OutputPerformanceMetricTests
{
    public static void IdentityAndCatalog()
    {
        var physical = MakeTarget("screen-a", OutputTargetContract.TrofeoKind);
        var desktop = MakeTarget("desktop-a", OutputTargetContract.DesktopKind);
        var physicalIds = OutputMetricContract.DescriptorsFor(physical);
        var desktopIds = OutputMetricContract.DescriptorsFor(desktop);

        Check(physicalIds.Length == 9 && desktopIds.Length == 5,
            "Physical and Desktop Outputs must expose their applicable performance and passive-status fields.");
        Check(physicalIds.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 9,
            "Per-Output metric suffixes must be unique.");
        Check(desktopIds.All(d => d.Id.StartsWith("system.output.desktop-a.", StringComparison.Ordinal)) &&
              desktopIds.All(d => !d.Id.Contains(".usb", StringComparison.Ordinal) &&
                                  !d.Id.Contains(".jpeg", StringComparison.Ordinal) &&
                                  !d.Id.Contains(".encode", StringComparison.Ordinal)),
            "Desktop outputs must never advertise fictional USB/JPEG/encoding counters.");

        var idA = OutputMetricContract.Id("screen-a", OutputMetricContract.Fps);
        var idB = OutputMetricContract.Id("screen-b", OutputMetricContract.Fps);
        Check(idA != idB && idA == "system.output.screen-a.fps",
            "Different immutable output IDs must create distinct, predictable namespaces.");

        MetricContract.ReplaceOutputDescriptors([physical, desktop]);
        Check(MetricContract.GetDescriptor(idA)?.BaseUnit == MetricUnit.FramesPerSecond &&
              MetricContract.GetDescriptor(OutputMetricContract.Id("screen-a", OutputMetricContract.JpegSize))?.BaseUnit == MetricUnit.Bytes,
            "Output-specific descriptors must preserve numerical dimensions.");
        Check(MetricContract.GetDescriptor(RuntimeMetricContract.Version)?.ValueKind == MetricValueKind.Text,
            "Application version must remain a global System metric.");

        var legacy = new[]
        {
            "system.runtime.frame", "system.runtime.render.duration", "system.runtime.encode.duration",
            "system.runtime.usb.duration", "system.runtime.frame.duration", "system.runtime.fps",
            "system.runtime.jpeg.size", "system.runtime.usb"
        };
        Check(legacy.All(id => MetricContract.GetDescriptor(id) is null &&
                              !MetricContract.Descriptors.Any(d => d.Id == id)),
            "Retired global device metrics must not appear as descriptors or compatibility aliases.");
        Check(!MetricContract.Descriptors.Any(d => d.Id.StartsWith("system.output.screen-b.")),
            "The visible metric catalog must advertise only configured Output identities.");

        MetricContract.ReplaceOutputDescriptors([physical, MakeTarget("screen-b", OutputTargetContract.TrofeoKind)]);
        Check(MetricContract.Descriptors.Any(d => d.Id == idB) &&
              !MetricContract.Descriptors.Any(d => d.Id.StartsWith("system.output.desktop-a.")),
            "Replacing configured Outputs must withdraw obsolete descriptor identities.");
    }

    public static void PublisherIsolation()
    {
        InTemp((metrics, log) =>
        {
            var config = Config();
            var sessions = new Dictionary<string, SimulatedPublisher>();
            using var manager = Manager(config, metrics, log, sessions);
            manager.Start();

            var a = Key("left", OutputMetricContract.Fps);
            var b = Key("right", OutputMetricContract.Fps);
            var snapshot = metrics.Snapshot();
            Check(snapshot.TryGetValue(a, out var av) && Convert.ToInt32(av) == 11 &&
                  snapshot.TryGetValue(b, out var bv) && Convert.ToInt32(bv) == 22,
                "Two concurrently enabled sessions must publish distinct FPS values.");
            Check(sessions.Count == 2 && sessions.Values.All(s => s.Enabled),
                "Both active sessions must publish independently; no elected global publisher.");
            Check(!snapshot.ContainsKey("system.runtime.fps"),
                "A per-Output publisher must never write the retired global FPS metric.");

            var oldRight = metrics.Snapshot()[b];
            manager.StopTarget("left");
            snapshot = metrics.Snapshot();
            Check(snapshot.TryGetValue(a, out var cleared) && cleared is null &&
                  Equals(snapshot[b], oldRight) && sessions["right"].Enabled,
                "Stopping one session must clear only its own counters and leave sibling metrics intact.");
        });
    }

    public static void StopAndRestart()
    {
        InTemp((metrics, log) =>
        {
            var config = Config();
            var sessions = new Dictionary<string, SimulatedPublisher>();
            using var manager = Manager(config, metrics, log, sessions);
            manager.Start();

            manager.StopAll();
            var frameA = Key("left", OutputMetricContract.FrameCount);
            var frameB = Key("right", OutputMetricContract.FrameCount);
            var stopped = metrics.Snapshot();
            Check(stopped.TryGetValue(frameA, out var a) && a is null &&
                  stopped.TryGetValue(frameB, out var b) && b is null,
                "Stop All must invalidate values from every active publisher.");
            Check(metrics.Snapshot().Keys.All(id => !id.StartsWith("system.output.desktop.", StringComparison.Ordinal)),
                "Physical session stop must not invent Desktop metrics.");

            manager.StartOrReloadAll();
            var restarted = metrics.Snapshot();
            Check(restarted.TryGetValue(frameA, out a) && a is not null &&
                  restarted.TryGetValue(frameB, out b) && b is not null,
                "Restart must publish fresh per-output values without reviving global counters.");

            var disabled = Config();
            disabled.Outputs.Targets[0].ProviderEnabled = false;
            manager.Reconfigure(disabled, new C.DashboardDefinition());
            var afterDisable = metrics.Snapshot();
            Check(afterDisable.TryGetValue(Key("left", OutputMetricContract.Fps), out var noValue) &&
                  noValue is null &&
                  afterDisable.TryGetValue(Key("right", OutputMetricContract.Fps), out var sibling) &&
                  sibling is not null,
                "Disabling one Output provider must invalidate only that Output's measurements.");
        });
    }

    private static string Key(string id, string suffix) => OutputMetricContract.Id(id, suffix);

    private static OutputTargetConfig MakeTarget(string id, string kind) => new()
    {
        Id = id, Name = id,
        Kind = kind,
        ProviderEnabled = true,
        DeviceEnabled = kind == OutputTargetContract.TrofeoKind
    };

    private static AppConfig Config() => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                {
                    Id = "left", Name = "Left", ProviderEnabled = true, DeviceEnabled = true,
                    DeviceId = "trofeo:" + new string('a', 64)
                },
                new OutputTargetConfig
                {
                    Id = "right", Name = "Right", ProviderEnabled = true, DeviceEnabled = true,
                    DeviceId = "trofeo:" + new string('b', 64)
                }
            ]
        }
    };

    private static OutputSessionManager Manager(AppConfig config, MetricStore store, FileLogger log,
        Dictionary<string, SimulatedPublisher> created) =>
        new(config, new C.DashboardDefinition(), store, log,
            (app, target, dashboard, metrics, logger) =>
            {
                var value = target.Id == "left" ? 11 : 22;
                var session = new SimulatedPublisher(target.Id, target.Name, value, metrics);
                created[target.Id] = session;
                return session;
            });

    private static void InTemp(Action<MetricStore, FileLogger> work)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-stage06-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { work(new MetricStore(), new FileLogger(Path.Combine(root, "tests.log"))); }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool passed, string reason)
    {
        if (!passed) throw new InvalidOperationException(reason);
    }

    private sealed class SimulatedPublisher(string id, string name, int value, MetricStore metrics) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => name;
        public bool Enabled { get; private set; }
        public bool IsConnected => false;
        public long SentFrames => value;
        public double LastFrameMs => 1;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public void SetRuntimeMetricsPublisher(bool enabled)
        {
            Enabled = enabled;
            if (enabled)
                metrics.Publish(new Dictionary<string, object?>
                {
                    [Key(id, OutputMetricContract.Fps)] = value,
                    [Key(id, OutputMetricContract.FrameCount)] = value
                });
            else
                metrics.Publish(OutputMetricContract.Unavailable(id, isPhysical: true));
        }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition dashboard) =>
            new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration plan) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
        private sealed class Plan : IOutputSessionReconfiguration { public void Dispose() { } }
    }
}
