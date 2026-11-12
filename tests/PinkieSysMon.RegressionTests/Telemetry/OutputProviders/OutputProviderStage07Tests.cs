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
        Check(providers.Count == 7 &&
              providers.Take(4).Select(d => d.Id).SequenceEqual(
                  MetricProviderContract.Descriptors.Select(d => d.Id)),
              "Existing System/LHM/iCUE plus disabled Filesystem provider contracts must remain stable.");
        Check(providers.Skip(4).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
            "Every configured Output needs one independent stable provider identity.");
        Check(providers.Skip(4).All(d => !d.DefaultEnabled),
            "Dynamic provider enablement defaults OFF even when device/board enablement differs.");
        var original = providers[4].Id;
        first.Name = "Renamed Trofeo";
        var renamed = MetricProviderContract.GetDescriptors([first])[4];
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
            manager.Reconfigure(changed, new C.DashboardDefinition());
            Check(store.Snapshot().TryGetValue(a, out var disabledHardware) && disabledHardware is null,
                "Provider Enabled must never force an explicitly disabled device to start.");
        });
    }

    public static void PhysicalUsbFallbackLifecycle()
    {
        InSandbox((store, log) =>
        {
            var first = Physical("screen-a", "A", true, true);
            var second = Physical("screen-b", "B", true, true);
            var desktop = Desktop("panel", "Panel", true, false);
            var config = Config(first, second, desktop);
            config.MetricProviders[MetricProviderContract.System] = false;
            var sessions = new Dictionary<string, TestSession>(StringComparer.OrdinalIgnoreCase);
            using var manager = new OutputSessionManager(config, new C.DashboardDefinition(), store, log,
                (app, target, board, metrics, logger) =>
                {
                    Check(OutputTargetContract.IsPhysicalKind(target.Kind),
                        "A disabled Desktop board must not open a session.");
                    var session = new TestSession(target.Id, metrics);
                    sessions[target.Id] = session;
                    return session;
                });

            string Key(string id, string field) => OutputMetricContract.Id(id, field);
            object? Value(string id, string field) =>
                store.Snapshot().TryGetValue(Key(id, field), out var value) ? value : null;
            void CheckState(string id, string status, string usb, string reason) =>
                Check(Equals(Value(id, OutputMetricContract.Status), status) &&
                      Equals(Value(id, OutputMetricContract.UsbState), usb), reason);
            void CheckUnavailable(string id, string status, string reason) =>
                Check(Equals(Value(id, OutputMetricContract.Status), status) &&
                      Equals(Value(id, OutputMetricContract.UsbState), OutputProviderContract.InactiveUsbState) &&
                      Value(id, OutputMetricContract.Fps) is null, reason);

            manager.Start();
            sessions[first.Id].PublishConnection("CONNECTED");
            sessions[second.Id].PublishConnection("AMBIGUOUS");
            CheckState(first.Id, "CONNECTED", "CONNECTED", "A live session must own CONNECTED.");
            CheckState(second.Id, "AMBIGUOUS", "AMBIGUOUS", "A sibling must retain its own transport state.");
            Check(!store.Snapshot().ContainsKey(Key(desktop.Id, OutputMetricContract.UsbState)) &&
                  Equals(Value(desktop.Id, OutputMetricContract.Status), "DISABLED"),
                "Desktop must publish passive status but no USB metric.");

            manager.StopAll();
            CheckUnavailable(first.Id, "STOPPED", "Stop All must expose inactive physical USB state.");
            CheckUnavailable(second.Id, "STOPPED", "Stop All must affect each independent physical Output.");
            Check(Equals(Value(desktop.Id, OutputMetricContract.Status), "DISABLED"),
                "Stop All must preserve disabled Desktop lifecycle status.");

            manager.Start();
            CheckState(first.Id, "WAITING", "WAITING",
                "Restart must replace UNAVAILABLE with the new session's actual state.");
            sessions[first.Id].PublishConnection("ERROR");
            sessions[second.Id].PublishConnection("CONNECTED");
            CheckState(first.Id, "ERROR", "ERROR", "Session errors must not be converted to UNAVAILABLE.");

            var disabled = Config(Physical("screen-a", "A", true, false),
                Physical("screen-b", "B", true, true), desktop);
            disabled.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(disabled, new C.DashboardDefinition());
            CheckUnavailable(first.Id, "DISABLED", "Disabling a device must publish passive USB inactivity.");
            CheckState(second.Id, "CONNECTED", "CONNECTED", "Disabling A must preserve connected B.");

            var providerOff = Config(Physical("screen-a", "A", false, false),
                Physical("screen-b", "B", true, true), desktop);
            providerOff.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(providerOff, new C.DashboardDefinition());
            Check(Value(first.Id, OutputMetricContract.UsbState) is null &&
                  Value(first.Id, OutputMetricContract.Status) is null,
                "Provider OFF must invalidate both USB and passive status.");
            CheckState(second.Id, "CONNECTED", "CONNECTED", "Provider OFF on A must not clear B.");

            var reenabled = Config(Physical("screen-a", "A", true, true),
                Physical("screen-b", "B", true, true), desktop);
            reenabled.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(reenabled, new C.DashboardDefinition());
            CheckState(first.Id, "WAITING", "WAITING",
                "Enabling the Output must replace the old null with actual session state.");
            sessions[first.Id].PublishConnection("CONNECTED");
            CheckState(first.Id, "CONNECTED", "CONNECTED", "Reconnected session must publish CONNECTED.");

            manager.PreparePhysicalRemoval(first.Id);
            Check(Value(first.Id, OutputMetricContract.UsbState) is null &&
                  Value(first.Id, OutputMetricContract.Status) is null &&
                  Value(first.Id, OutputMetricContract.Fps) is null,
                "Pending physical Remove must not republish an inactive USB value.");
            CheckState(second.Id, "CONNECTED", "CONNECTED", "Selected Remove must preserve sibling metrics.");

            var removed = Config(Physical("screen-b", "B", true, true), desktop);
            removed.MetricProviders[MetricProviderContract.System] = false;
            manager.Reconfigure(removed, new C.DashboardDefinition());
            Check(Value(first.Id, OutputMetricContract.UsbState) is null &&
                  Value(first.Id, OutputMetricContract.Status) is null,
                "Removed Output must leave no stale USB or lifecycle values.");
            CheckState(second.Id, "CONNECTED", "CONNECTED", "Descriptor removal must not disturb B.");

            Check(BinarySignalContract.TryResolve("CONNECTED", "Setpoint", "CONNECTED", "=", out var connected) &&
                  connected &&
                  BinarySignalContract.TryResolve(OutputProviderContract.InactiveUsbState,
                      "Setpoint", "CONNECTED", "=", out var inactive) && !inactive &&
                  !BinarySignalContract.TryResolve(null, "Setpoint", "CONNECTED", "=", out _) &&
                  !BinarySignalContract.TryResolve("undefined", "Setpoint", "CONNECTED", "=", out _),
                "Binary must select True for CONNECTED, False for UNAVAILABLE and no state for null/reserved text.");
        });
    }

    private static AppConfig Config(params OutputTargetConfig[] outputs) => new()
    {
        Outputs = new OutputConfig { Targets = outputs.ToList() }
    };

    private static OutputTargetConfig Physical(string id, string name, bool enabled, bool deviceEnabled) => new()
    { Width = 1920, Height = 480,
        Id = id, Name = name, Kind = OutputTargetContract.PhysicalKind,
        Transport = OutputTargetContract.LyBulkTransport,
        DeviceId = "usb-interface:" + new string(id.EndsWith("-a", StringComparison.OrdinalIgnoreCase) ? 'a' :
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
        public string UsbState { get; private set; } = "WAITING";
        public void Start() { }
        public void PublishConnection(string state)
        {
            UsbState = state;
            if (Enabled)
                store.Publish(new Dictionary<string, object?>
                {
                    [OutputMetricContract.Id(id, OutputMetricContract.UsbState)] = state,
                    [OutputMetricContract.Id(id, OutputMetricContract.Status)] = state
                });
        }
        public void SetRuntimeMetricsPublisher(bool enabled)
        {
            Enabled = enabled;
            if (enabled)
            {
                store.Publish(new Dictionary<string, object?>
                {
                    [OutputMetricContract.Id(id, OutputMetricContract.Fps)] = id == "screen-a" ? 51 : 52,
                    [OutputMetricContract.Id(id, OutputMetricContract.UsbState)] = UsbState
                });
            }
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
