using System.ComponentModel;
using static PinkieSysMon.RegressionTests.Issue71AdoptionTests;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71RecoveryTests
{
    public static void ReattachmentRecoversSendAndOpenFailure()
    {
        foreach (var failOpen in new[] { false, true })
        {
            var device = Device("turzx", 'b');
            var target = EnabledTarget();
            var present = 0; var opens = 0; var disposed = 0; var scans = 0;
            using var pump = new FramePump(new AppConfig(), target, Board(), new MetricStore(), Log(),
                (selected, _, _, _, _) =>
                {
                    Check(selected.DeviceId == device.DeviceId, "Recovery cannot substitute another binding.");
                    var attempt = Interlocked.Increment(ref opens);
                    if (attempt > 1) Check(failOpen || Volatile.Read(ref disposed) == 1,
                        "The failed connection must be disposed before another open.");
                    if (attempt == 1 && failOpen) throw new Win32Exception(22, "Synthetic submit failure");
                    return new Frame(attempt == 1, () => Interlocked.Increment(ref disposed));
                }, turzxRecoveryDiscovery: () =>
                {
                    Check(failOpen || Volatile.Read(ref disposed) == 1,
                        "Even passive recovery must follow terminal send/disposal.");
                    Interlocked.Increment(ref scans);
                    return Volatile.Read(ref present) == 0 ? [] : [device];
                });
            pump.Start();
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) >= 2 && pump.UsbState == "WAITING", 5000),
                "An observed missing binding must enter WAITING and continue passive polling.");
            Check(Volatile.Read(ref opens) == 1 && pump.SentFrames == 0 && !pump.IsConnected,
                "Absence must not admit a native retry or publish a frame.");
            Volatile.Write(ref present, 1);
            Check(SpinWait.SpinUntil(() => pump.SentFrames >= 2 && pump.IsConnected, 5000) && Volatile.Read(ref opens) == 2,
                "Exact reattachment must resume frames through one fresh connection without Enabled off/on.");
        }
    }

    public static void RecoveryRequiresTrustworthyAbsenceAndExactReturn()
    {
        var device = Device("turzx", 'b');
        var phase = 0; var opens = 0; var scans = 0;
        using var pump = new FramePump(new AppConfig(), EnabledTarget(), Board(), new MetricStore(), Log(),
            (_, _, _, _, _) => new Frame(Interlocked.Increment(ref opens) == 1),
            turzxRecoveryDiscovery: () =>
            {
                Interlocked.Increment(ref scans);
                return Volatile.Read(ref phase) switch
                {
                    1 => throw new InvalidOperationException("Synthetic unavailable metadata"),
                    2 or 4 => [device, device],
                    3 => [],
                    5 => [Device("turzx", 'c'), Device("trofeo", 'b')],
                    _ => [device]
                };
            });
        pump.Start();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) > 0, 5000), "Precondition: faulted worker began passive observation.");
        foreach (var next in new[] { 0, 1, 2, 0 })
        {
            Volatile.Write(ref phase, next);
            var before = Volatile.Read(ref scans);
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) > before, 3000), "Faulted session must poll passively.");
            Thread.Sleep(50);
            Check(Volatile.Read(ref opens) == 1 && pump.SentFrames == 0,
                "Continued presence, failed discovery and ambiguity cannot establish unplug/replug or retry USB.");
        }
        Volatile.Write(ref phase, 3);
        Check(SpinWait.SpinUntil(() => pump.UsbState == "WAITING", 3000), "A complete missing snapshot establishes absence.");
        foreach (var next in new[] { 4, 5 })
        {
            Volatile.Write(ref phase, next);
            var before = Volatile.Read(ref scans);
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) > before, 3000), "Reattachment scan must run.");
            Thread.Sleep(50);
            Check(Volatile.Read(ref opens) == 1 && pump.SentFrames == 0,
                "An ambiguous, different-unit or different-adapter result cannot satisfy exact reattachment.");
        }
        Volatile.Write(ref phase, 0);
        Check(SpinWait.SpinUntil(() => pump.SentFrames >= 1, 5000) && Volatile.Read(ref opens) == 2,
            "Only the exact unique binding after actual absence permits a new exchange.");
    }

    public static void StaleRecoveryScanCannotSurviveLifecycleChange()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = Device("turzx", 'b'); var opens = 0; var scans = 0; var missing = 0;
        using var pump = new FramePump(new AppConfig(), EnabledTarget(), Board(), new MetricStore(), Log(),
            (_, _, _, _, _) => new Frame(Interlocked.Increment(ref opens) == 1),
            turzxRecoveryDiscovery: () =>
            {
                if (Interlocked.Increment(ref scans) == 1)
                {
                    entered.Set();
                    Check(release.Wait(5000), "Test must release the passive scan.");
                    return [];
                }
                return Volatile.Read(ref missing) == 1 ? [] : [device];
            });
        pump.Start();
        try
        {
            Check(entered.Wait(5000), "Precondition: passive recovery scan is in flight.");
            pump.Suspend(); pump.Resume();
        }
        finally { release.Set(); }
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref scans) >= 3, 5000) && Volatile.Read(ref opens) == 1 && pump.UsbState == "ERROR",
            "A missing result superseded by suspend/resume cannot unlock a still-present failed binding.");
        Volatile.Write(ref missing, 1);
        Check(SpinWait.SpinUntil(() => pump.UsbState == "WAITING", 3000), "A new current-generation absence must be observed.");
        pump.SetIdlePaused(true);
        Volatile.Write(ref missing, 0);
        Thread.Sleep(650);
        Check(Volatile.Read(ref opens) == 1, "Idle pause cannot acquire a reattached transport.");
        pump.SetIdlePaused(false);
        Check(SpinWait.SpinUntil(() => pump.SentFrames >= 1, 5000), "Idle wake may recover after a valid retained absence.");
    }

    public static void BoundedCheckpointNeverRecoversAutomatically()
    {
        var opens = 0; var scans = 0; var disposed = 0;
        using var pump = new FramePump(new AppConfig(), EnabledTarget(), Board(), new MetricStore(), Log(),
            (_, _, _, _, _) => { Interlocked.Increment(ref opens); return new Frame(true, () => Interlocked.Increment(ref disposed)); },
            maximumFrames: 1, turzxRecoveryDiscovery: () => { Interlocked.Increment(ref scans); return []; });
        pump.Start();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref disposed) == 1, 5000), "Failed bounded checkpoint must retire its transport.");
        Thread.Sleep(650);
        Check(Volatile.Read(ref opens) == 1 && Volatile.Read(ref scans) == 0 && pump.SentFrames == 0 && pump.UsbState == "ERROR",
            "A developer checkpoint must retain its single-attempt contract even if hardware is reattached.");
    }

    public static void FailedDisposalCannotAuthorizeRecovery()
    {
        var opens = 0; var scans = 0; var cleanup = 0;
        using var pump = new FramePump(new AppConfig(), EnabledTarget(), Board(), new MetricStore(), Log(),
            (_, _, _, _, _) =>
            {
                Interlocked.Increment(ref opens);
                return new Frame(true, () => { Interlocked.Increment(ref cleanup); throw new IOException("Synthetic cleanup failure"); });
            }, turzxRecoveryDiscovery: () => { Interlocked.Increment(ref scans); return []; });
        pump.Start();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref cleanup) == 1, 5000), "Precondition: failed cleanup reported.");
        Thread.Sleep(650);
        Check(Volatile.Read(ref opens) == 1 && Volatile.Read(ref scans) == 0 && pump.UsbState == "ERROR",
            "A failed cleanup cannot authorize recovery polling or another acquisition on unconfirmed ownership.");
    }

    private static OutputTargetConfig EnabledTarget()
    {
        var target = Target(Device("turzx", 'b'));
        target.DeviceEnabled = true; target.RefreshIntervalMs = 10; target.Usb!.RetryIntervalMs = 500;
        return target;
    }
    private static FileLogger Log() => new(Path.Combine(Path.GetTempPath(), "PinkieSysMon-Issue71-recovery-tests", "worker.log"));
    private sealed class Frame(bool fail, Action? disposed = null) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => 90;
        public PhysicalFrameProfile Profile => PhysicalFrameProfile.Turzx;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        { if (fail) throw new Win32Exception(22, "Synthetic submit failure"); }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => disposed?.Invoke();
    }
}
