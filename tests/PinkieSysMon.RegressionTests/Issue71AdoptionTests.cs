using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71AdoptionTests
{
    public static void PartialDiscoveryIsolation()
    {
        var old = Device("trofeo", 'a');
        var turzx = Device("turzx", 'b');
        Check(PhysicalDeviceDiscovery.Capture(() => [old], () => [turzx]).SequenceEqual([old, turzx]),
            "Both independently discovered adapter descriptors must survive a complete passive capture.");
        foreach (var failed in new[] { "trofeo", "turzx" })
        {
            IReadOnlyList<OutputDeviceDescriptor> Capture() => PhysicalDeviceDiscovery.Capture(
                () => failed == "trofeo" ? throw new InvalidOperationException("private old discovery") : [old],
                () => failed == "turzx" ? throw new InvalidOperationException("private new discovery") : [turzx]);
            var error = Catch<PhysicalDeviceDiscoveryException>(() => Capture());
            Check(error.UnavailableTransports.SetEquals([failed]) && error.Devices.Count == 1 &&
                error.Devices[0].Transport != failed && !error.Message.Contains("private"),
                "An unavailable family must remain explicit without discarding its healthy sibling.");
            var targets = new[] { Target(old), Target(turzx) };
            var inventory = PhysicalDeviceInventory.Capture(targets, Capture);
            Check(inventory.Single(e => e.ConfiguredTarget!.Transport == failed).Status == PhysicalDeviceInventoryStatus.DiscoveryUnavailable &&
                inventory.Single(e => e.ConfiguredTarget!.Transport != failed).CanEditSettings,
                "Runtime inventory must distinguish unavailable discovery from a disconnected device.");
            var model = new DeviceSettingsModel(new AppConfig { Outputs = new() { Targets = [.. targets] } }, "Default");
            model.Refresh([old, turzx]);
            model.Refresh(error.Devices, error.UnavailableTransports);
            Check(model.Entries.Single(e => e.Target.Transport == failed).Status is null &&
                model.Entries.Single(e => e.Target.Transport != failed).CanEdit && !model.IsDirty,
                "A partial scan must revoke stale editable presence only for its failed family.");
        }
        _ = Catch<PhysicalDeviceDiscoveryException>(() => PhysicalDeviceDiscovery.Capture(() => [turzx], () => []));
    }

    public static void ConsentPersistenceAndCancel()
    {
        var device = Device("turzx", 'b');
        var config = new AppConfig();
        config.Usb.DiagnosticJournalEnabled = true;
        var draft = new DeviceSettingsModel(config, "Default");
        draft.Refresh([device]);
        var target = draft.Entries.Single().Target;
        Check(draft.Entries.Single().CanEdit && !draft.IsDirty && !target.DeviceEnabled &&
            target.Turzx?.BrightnessPercent is null && target.Usb?.DiagnosticJournalEnabled == false,
            "A passive candidate must default to supported options and remain unsaved/disabled.");
        Check(!draft.SetEnabled(target, true, false) && !draft.IsDirty && draft.ConfiguredCount == 0,
            "Declined consent cannot enable or adopt a candidate.");
        Check(draft.SetEnabled(target, true, true), "Exact present binding plus explicit consent may adopt/enable.");
        draft.ValidateForApply([device]);
        Check(config.Outputs.Targets.Count == 1, "An uncommitted enabled draft must not mutate saved configuration.");
        draft.ApplyTo(config);
        OutputTargetContract.ValidateAndNormalize(config.Outputs.Targets);
        Check(config.Outputs.Targets.Single(t => t.Kind == "turzx").DeviceEnabled && config.Outputs.Targets[0].Kind == "desktop",
            "Applying the selected physical draft must retain the sibling Desktop.");
        var path = Path.Combine(Path.GetTempPath(), "pinkie-adoption-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            var file = Path.Combine(path, "app.json"); config.Save(file);
            var loaded = AppConfig.Load(file, new FileLogger(Path.Combine(path, "worker.log")));
            Check(loaded.Outputs.Targets.Single(t => t.Kind == "turzx") is { DeviceEnabled: true, Turzx.BrightnessPercent: null },
                "Explicit enabled ownership must survive save/reload with null brightness.");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        var cancelled = new DeviceSettingsModel(config, "Default"); cancelled.Refresh([device]);
        var independent = cancelled.Entries.Single().Target; independent.Name = "Uncommitted change"; cancelled.MarkChanged(independent);
        Check(config.Outputs.Targets.Single(t => t.Kind == "turzx").Name != independent.Name,
            "Cancel/discard must leave the applied target independent from further draft edits.");
    }

    public static void StaleConsentAndUnsupportedOptions()
    {
        var device = Device("turzx", 'b');
        var config = new AppConfig();
        var draft = new DeviceSettingsModel(config, "Default"); draft.Refresh([device]);
        var target = draft.Entries.Single().Target;
        Check(draft.SetEnabled(target, true, true), "Precondition: one consented enabled draft.");
        _ = Catch<InvalidOperationException>(() => draft.ValidateForApply([]));
        _ = Catch<InvalidOperationException>(() => draft.ValidateForApply([device, device]));
        _ = Catch<InvalidOperationException>(() => draft.ValidateForApply([], new HashSet<string> { "turzx" }));
        Check(config.Outputs.Targets.Count == 1, "Lost/ambiguous/unavailable identity cannot commit stale consent.");
        var selected = Target(device); selected.DeviceEnabled = true;
        foreach (var brightness in new int?[] { 0, 100 })
        {
            selected.Turzx!.BrightnessPercent = brightness;
            _ = Catch<InvalidDataException>(() => OutputTargetContract.ValidateAndNormalize([selected]));
        }
        selected.Turzx!.BrightnessPercent = null; selected.Usb!.DiagnosticJournalEnabled = true;
        _ = Catch<InvalidDataException>(() => OutputTargetContract.ValidateAndNormalize([selected]));
        selected.Usb.DiagnosticJournalEnabled = false;
        OutputTargetContract.ValidateAndNormalize([selected]);
    }

    public static void ReconciliationUsesExactAdapter()
    {
        var device = Device("turzx", 'b') with { PnpNodeKey = "synthetic-node" };
        var observation = new UsbDisplayObservation("synthetic-observation", UsbDisplayCatalog.Find(TurzxDeviceDiscovery.Product)!,
            UsbDisplayObservationStatus.Recognized, 1, [], [], []) { NodeKeys = ["synthetic-node"] };
        Check(UsbDisplayReconciliation.ReadOnlyObservations([observation], [device]).Count == 0,
            "One exact TURZX interface/devnode must suppress its duplicate recognition-only tab.");
        Check(UsbDisplayReconciliation.ReadOnlyObservations([observation], [device with { Transport = "trofeo" }]).Count == 1,
            "A different adapter cannot satisfy a TURZX observation even with an equal devnode key.");
        var duplicate = device with { DeviceId = "turzx:" + new string('c', 64) };
        Check(UsbDisplayReconciliation.ReadOnlyObservations([observation], [device, duplicate]).Single().Status == UsbDisplayObservationStatus.Ambiguous,
            "Multiple interfaces on one node must remain ambiguous rather than deduplicating to an eligible device.");
    }

    public static void FaultedTurzxDoesNotAutomaticallyReopen()
    {
        var target = Target(Device("turzx", 'b')); target.DeviceEnabled = true; target.RefreshIntervalMs = 10;
        target.Usb!.RetryIntervalMs = 500;
        var config = new AppConfig(); var opens = 0; var disposed = 0;
        var board = Board();
        using var pump = new FramePump(config, target, board, new MetricStore(), Log(), (_, _, _, _, _) =>
        { Interlocked.Increment(ref opens); return new FailedFrame(() => Interlocked.Increment(ref disposed)); },
            turzxRecoveryDiscovery: () => [Device("turzx", 'b')]);
        pump.Start();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref disposed) == 1, 5000), "Failed send must retire the worker-owned transport.");
        Thread.Sleep(650); // Cross the otherwise-due automatic retry interval.
        Check(Volatile.Read(ref opens) == 1 && pump.SentFrames == 0 && pump.UsbState == "ERROR",
            "An uncertain TURZX exchange cannot be automatically reopened or published as a completed frame.");
        using var plan = pump.PrepareReconfiguration(config, target, board); pump.CommitReconfiguration(plan);
        pump.SetIdlePaused(true); pump.SetIdlePaused(false);
        Thread.Sleep(650);
        Check(Volatile.Read(ref opens) == 1 && pump.UsbState == "ERROR", "Ordinary reconfiguration/idle wake cannot clear or hide the failed-session latch.");
    }

    public static void FailedTurzxOpenAndTrofeoRetryIsolation()
    {
        foreach (var transport in new[] { "turzx", "trofeo" })
        {
            var target = Target(Device(transport, transport == "turzx" ? 'b' : 'a'));
            target.DeviceEnabled = true; target.RefreshIntervalMs = 10; target.Usb!.RetryIntervalMs = 500;
            var attempts = 0;
            using var pump = new FramePump(new AppConfig(), target, Board(), new MetricStore(), Log(), (_, _, _, _, _) =>
            { Interlocked.Increment(ref attempts); throw new IOException("Synthetic native open failure"); },
                turzxRecoveryDiscovery: () => [Device("turzx", 'b')]);
            pump.Start();
            Check(SpinWait.SpinUntil(() => pump.UsbState == "ERROR", 5000), "A failed open must publish an explicit error.");
            if (transport == "trofeo")
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref attempts) >= 2, 2500), "Trofeo retains its existing retry contract.");
            else
            {
                Thread.Sleep(650);
                Check(Volatile.Read(ref attempts) == 1, "An actual TURZX open/handshake failure must await a new enabled session.");
            }
        }
    }

    public static void MissingBindingStillAllowsPassiveRetry()
    {
        var target = Target(Device("turzx", 'b')); target.DeviceEnabled = true; target.RefreshIntervalMs = 10;
        target.Usb!.RetryIntervalMs = 500; var scans = 0;
        using var pump = new FramePump(new AppConfig(), target, Board(), new MetricStore(), Log(), (_, _, _, _, _) =>
        { Interlocked.Increment(ref scans); return null; });
        pump.Start();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) >= 2, 3000) && pump.UsbState == "WAITING" && pump.SentFrames == 0,
            "A missing exact binding has made no native exchange and may be passively checked again at the configured interval.");
    }

    internal static OutputDeviceDescriptor Device(string transport, char token) =>
        new(transport + ":" + new string(token, 64), transport, "Synthetic " + transport, "synthetic-private-path");
    internal static OutputTargetConfig Target(OutputDeviceDescriptor device) => new()
    {
        Id = device.Transport + "-test", Name = device.DisplayName, Kind = device.Transport, Transport = device.Transport,
        DeviceId = device.DeviceId, Dashboard = "Default", RefreshIntervalMs = 100, JpegQuality = 90,
        Usb = new(), Turzx = device.Transport == "turzx" ? new() : null
    };
    internal static C.DashboardDefinition Board(string color = "#000000") => new()
    { BaseDirectory = Path.Combine(Environment.CurrentDirectory, "dashboards", "Default"), Canvas = new() { Width = 1920, Height = 480, BackgroundColor = color } };
    private static FileLogger Log() => new(Path.Combine(Path.GetTempPath(), "PinkieSysMon-Issue71-adoption-tests", "worker.log"));
    private sealed class FailedFrame(Action disposed) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => 90;
        public PhysicalFrameProfile Profile => PhysicalFrameProfile.Turzx;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics) => throw new IOException("Synthetic exchange failure");
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => disposed();
    }
    private static T Catch<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
