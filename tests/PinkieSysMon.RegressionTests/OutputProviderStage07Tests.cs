using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class OutputProviderStage07Tests
{
    public static void ProviderCatalog()
    {
        var first = Physical("physical-a", "Trofeo", enabled: true, deviceEnabled: false);
        var second = Physical("physical-b", "Trofeo", enabled: false, deviceEnabled: true);
        var desktop = Desktop("desktop-a", "Panel", enabled: true, boardEnabled: false);
        var providers = MetricProviderContract.GetDescriptors([first, second, desktop]);
        Check(providers.Count == 6 &&
              providers.Take(3).Select(d => d.Id).SequenceEqual(
                  MetricProviderContract.Descriptors.Select(d => d.Id)),
              "System, LHM and iCUE provider contracts must remain unchanged.");
        Check(providers.Skip(3).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
            "Every configured Output needs one independent stable provider identity.");
        Check(providers.Skip(3).All(d => !d.DefaultEnabled),
            "Dynamic provider enablement defaults OFF even when device/board enablement differs.");
        var original = providers[3].Id;
        first.Name = "Renamed Trofeo";
        var renamed = MetricProviderContract.GetDescriptors([first])[3];
        Check(renamed.Id == original && renamed.DisplayName == "Renamed Trofeo",
            "Renaming an Output changes its display label, never its provider/metric IDs.");
        Check(Throws<InvalidDataException>(() =>
            MetricProviderContract.GetDescriptors([first, Physical("PHYSICAL-A", "Duplicate", true, true)])),
            "Case-insensitive Output provider ID collisions must be rejected.");
    }

    public static void Selector()
    {
        var first = Physical("screen-a", "Main", enabled: true, deviceEnabled: false);
        var disabled = Physical("screen-b", "Hidden", enabled: false, deviceEnabled: true);
        var desktop = Desktop("virtual-a", "Overlay", enabled: true, boardEnabled: false);
        var cfg = Config(first, disabled, desktop);
        cfg.MetricProviders[MetricProviderContract.System] = false;

        var snapshot = OutputMetricSelectorCatalog.Build(cfg, null);
        var names = snapshot.Metrics.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstStatus = OutputMetricContract.Id(first.Id, OutputMetricContract.Status);
        var desktopStatus = OutputMetricContract.Id(desktop.Id, OutputMetricContract.Status);
        Check(names.Contains(firstStatus) && names.Contains(desktopStatus) &&
              !snapshot.Metrics.Any(m => m.Id.StartsWith(OutputProviderContract.Id(disabled.Id) + ".", StringComparison.OrdinalIgnoreCase)),
              "Output providers must appear independently from System and hide disabled instances.");
        Check(snapshot.Metrics.Where(m => m.Id.StartsWith(OutputProviderContract.Id(desktop.Id) + ".", StringComparison.OrdinalIgnoreCase))
              .All(m => !m.Id.EndsWith(".usb") && !m.Id.EndsWith(".jpeg.size")),
              "Virtual output branches must not invent USB or JPEG fields.");
        var branch = snapshot.Metrics.Single(m => m.Id == firstStatus).DisplayPath;
        Check(branch.Count == 3 && branch[0].Text == "Outputs" &&
              branch[1].Text.Contains("Main") && branch[1].Key.Contains(first.Id),
              "Selector must show independent identifiable Output provider branches.");
        var oldId = OutputMetricContract.Id(disabled.Id, OutputMetricContract.Fps);
        var unavailable = OutputMetricSelectorCatalog.Build(cfg, oldId);
        Check(unavailable.GhostMetric is { Available: false } &&
              unavailable.GhostMetric.Id == oldId,
              "An assigned metric from a disabled Output must remain visible as unavailable.");

        first.ProviderEnabled = false;
        desktop.ProviderEnabled = false;
        Check(OutputMetricSelectorCatalog.Build(cfg, null).Metrics.Count == 0,
            "Output catalog must hide all disabled provider instances.");
        desktop.ProviderEnabled = true;
        Check(OutputMetricSelectorCatalog.Build(cfg, null).Metrics.Any(m => m.Id == desktopStatus),
            "Enabling the Desktop provider alone must restore its selector branch.");
    }

    public static void PassiveLifecycle()
    {
        InSandbox((store, log) =>
        {
            var physical = Physical("device", "Device", enabled: true, deviceEnabled: false);
            var desktop = Desktop("desktop", "Desktop", enabled: true, boardEnabled: false);
            var cfg = Config(physical, desktop);
            cfg.MetricProviders[MetricProviderContract.System] = false;
            var created = 0;
            using var manager = new OutputSessionManager(cfg, new C.DashboardDefinition(), store, log,
                (app, target, board, metrics, logger) =>
                {
                    created++;
                    throw new InvalidOperationException("Disabled physical and Desktop outputs must never open a session.");
                });
            manager.Start();
            var snapshot = store.Snapshot();
            var statusDevice = OutputMetricContract.Id(physical.Id, OutputMetricContract.Status);
            var statusDesktop = OutputMetricContract.Id(desktop.Id, OutputMetricContract.Status);
            Check(created == 0 &&
                  Equals(snapshot[statusDevice], "DISABLED") &&
                  Equals(snapshot[statusDesktop], "DISABLED"),
                "Provider Enabled must publish passive lifecycle state with no USB or Desktop renderer.");
            Check(!snapshot.TryGetValue(OutputMetricContract.Id("device", OutputMetricContract.Fps), out var fps) || fps is null,
                "An inactive physical device must not publish fabricated live FPS.");
            manager.StopAll();
            Check(Equals(store.Snapshot()[statusDesktop], "DISABLED"),
                "Global lifetime shutdown must not change a disabled Output's persisted policy.");

            var changed = Config(Physical("device", "Device", false, false));
            changed.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(changed, new C.DashboardDefinition());
            var after = store.Snapshot();
            Check(after.TryGetValue(statusDesktop, out var removed) && removed is null &&
                  after.TryGetValue(statusDevice, out var disabled) && disabled is null,
                "Removing a provider must invalidate its status and disabling another must clear data.");
            MetricContract.ReplaceOutputDescriptors([physical, desktop]);
            MetricContract.ReplaceOutputDescriptors(changed.Outputs.Targets);
            Check(!MetricContract.Descriptors.Any(d => d.Id == statusDesktop) &&
                  MetricContract.GetDescriptor(statusDesktop) is null,
                "Removing Output must remove its descriptors and prevent fallback ghosts in the registry.");
        });
    }

    public static void LiveIsolation()
    {
        InSandbox((store, log) =>
        {
            var first = Physical("screen-a", "A", true, true);
            var second = Physical("screen-b", "B", true, true);
            var cfg = Config(first, second);
            cfg.MetricProviders[MetricProviderContract.System] = false;
            var publishers = new Dictionary<string, TestSession>(StringComparer.OrdinalIgnoreCase);
            using var manager = new OutputSessionManager(cfg, new C.DashboardDefinition(), store, log,
                (app, target, board, metrics, logger) =>
                {
                    var worker = new TestSession(target.Id, metrics);
                    publishers[target.Id] = worker;
                    return worker;
                });
            manager.Start();
            var a = OutputMetricContract.Id(first.Id, OutputMetricContract.Fps);
            var b = OutputMetricContract.Id(second.Id, OutputMetricContract.Fps);
            Check(Equals(store.Snapshot()[a], 51) && Equals(store.Snapshot()[b], 52) &&
                  publishers.Values.All(p => p.Enabled),
                "Output metrics must remain active for each provider when global System is disabled.");
            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(cfg, first.Id, false),
                manager.GetDashboardAssignments());
            Check(store.Snapshot().TryGetValue(a, out var stopped) && stopped is null &&
                  Equals(store.Snapshot()[b], 52) &&
                  Equals(store.Snapshot()[OutputMetricContract.Id(first.Id, OutputMetricContract.Status)], "DISABLED"),
                "Disabling one Output clears only its live readings and retains passive provider status.");
            var changed = Config(Physical("screen-a", "A", true, false),
                Physical("screen-b", "B", false, true));
            changed.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(changed, new C.DashboardDefinition());
            Check(store.Snapshot().TryGetValue(b, out var noLongerPublished) && noLongerPublished is null,
                "Per-output ProviderEnabled OFF must clear active output values independently of Device Enabled.");
            manager.StartOrReloadAll();
            Check(store.Snapshot().TryGetValue(a, out var disabledHardware) && disabledHardware is null,
                "Provider Enabled must never force an explicitly disabled device to start.");
        });
    }

    private static AppConfig Config(params OutputTargetConfig[] outputs) => new()
    {
        Outputs = new OutputConfig { Targets = outputs.ToList() }
    };

    private static OutputTargetConfig Physical(string id, string name, bool enabled, bool deviceEnabled) => new()
    {
        Id = id, Name = name, Kind = OutputTargetContract.TrofeoKind,
        Transport = OutputTargetContract.TrofeoTransport,
        DeviceId = "trofeo:" + new string(id.EndsWith("-a", StringComparison.OrdinalIgnoreCase) ? 'a' :
            id.EndsWith("-b", StringComparison.OrdinalIgnoreCase) ? 'b' : 'c', 64),
        ProviderEnabled = enabled, DeviceEnabled = deviceEnabled
    };

    private static OutputTargetConfig Desktop(string id, string name, bool enabled, bool boardEnabled) => new()
    {
        Id = id, Name = name, Kind = OutputTargetContract.DesktopKind,
        Transport = OutputTargetContract.DesktopTransport,
        ProviderEnabled = enabled,
        Desktop = new DesktopOutputConfig { BoardEnabled = boardEnabled }
    };

    private static void InSandbox(Action<MetricStore, FileLogger> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-stage07-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try { test(new MetricStore(), new FileLogger(Path.Combine(path, "test.log"))); }
        finally { Directory.Delete(path, true); }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class TestSession(string id, MetricStore store) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => id;
        public bool Enabled { get; private set; }
        public bool IsConnected => false;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public void SetRuntimeMetricsPublisher(bool enabled)
        {
            Enabled = enabled;
            if (enabled)
                store.Publish(OutputMetricContract.Id(id, OutputMetricContract.Fps), id == "screen-a" ? 51 : 52);
            else
                store.Publish(OutputMetricContract.Unavailable(id, true));
        }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition board) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration plan) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { store.Publish(OutputMetricContract.Unavailable(id, true)); }
        private sealed class Plan : IOutputSessionReconfiguration { public void Dispose() { } }
    }
}
