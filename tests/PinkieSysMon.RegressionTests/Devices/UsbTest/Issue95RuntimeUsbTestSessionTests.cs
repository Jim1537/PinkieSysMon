using System.Diagnostics;

namespace PinkieSysMon.RegressionTests;

// Synthetic lease behavior only; no WinUSB, native worker or config mutation.
internal static class Issue95RuntimeUsbTestSessionTests
{
    private static readonly Guid ClientA = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ClientB = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Attempt = new("12345678-abcd-4321-abcd-111111111111");

    private static CatalogUsbTestBeginRequest Request(Guid client, Guid attempt) =>
        new(client, new CatalogUsbTrialIntent(attempt,
            "usb-interface:" + new string('a', 64),
            "usb-pnp:" + new string('b', 64),
            OutputTargetContract.LyBulkTransport, 1920, 480, 1));

    private static RuntimeUsbTestSessionGate Gate() =>
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40));

    private static long Tick(int seconds) => checked((long)Stopwatch.Frequency * seconds);

    public static void ExclusiveAdmissionAndCorrelation()
    {
        var gate = Gate();
        var original = Request(ClientA, Attempt);
        Check(gate.TryBegin(original, Tick(0), out var lease) == RuntimeUsbTestAdmission.Started,
            "The first validated attempt must reserve a session.");
        var owner = lease ?? throw new InvalidOperationException("No owner lease returned.");
        Check(gate.TryBegin(original, Tick(1), out var duplicate) ==
              RuntimeUsbTestAdmission.AlreadyRunning && duplicate is null,
            "Retransmitting the same attempt must not start a second worker.");
        Check(gate.TryBegin(original with { Intent = original.Intent with { Width = 1024 } },
              Tick(1), out _) == RuntimeUsbTestAdmission.AttemptReused,
            "The same attempt cannot silently change its wire snapshot.");
        var other = Request(ClientB, Guid.NewGuid());
        Check(gate.TryBegin(other, Tick(1), out _) == RuntimeUsbTestAdmission.Busy,
            "Another client cannot acquire an occupied lease.");
        Check(!gate.RequestCancel(new CatalogUsbTestHandle(ClientB, Attempt)) &&
              !owner.StopRequested.IsCancellationRequested &&
              gate.GetStatus(owner.Handle, Tick(2)).Phase == RuntimeUsbTestPhase.Running,
            "Wrong-client cancellation cannot affect the owning worker.");
        Check(gate.GetStatus(new CatalogUsbTestHandle(ClientA, Guid.NewGuid()), Tick(2)).Phase ==
              RuntimeUsbTestPhase.Missing,
            "Unrelated attempts must not receive the current result.");
        Check(gate.ReportTechnicalOutcome(owner, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              gate.GetStatus(owner.Handle, Tick(3)) is
              { Phase: RuntimeUsbTestPhase.SucceededAndReleased,
                TechnicalOutcome: CatalogUsbTestTechnicalOutcome.SucceededAndReleased },
            "Only a trusted terminal native result can release an attempt.");
        Check(gate.TryBegin(original, Tick(3), out _) == RuntimeUsbTestAdmission.AttemptReused,
            "Already-completed attempts cannot be replayed.");
        Check(gate.TryBegin(other, Tick(3), out var next) == RuntimeUsbTestAdmission.Started,
            "Fresh attempts may start after confirmed native release.");
        var nextOwner = next ?? throw new InvalidOperationException("Fresh attempt has no lease.");
        Check(!gate.ReportTechnicalOutcome(owner, CatalogUsbTestTechnicalOutcome.FailedAndReleased) &&
              gate.GetStatus(nextOwner.Handle, Tick(3)).Phase == RuntimeUsbTestPhase.Running &&
              gate.ReportTechnicalOutcome(nextOwner, CatalogUsbTestTechnicalOutcome.FailedAndReleased),
            "A stale worker cannot finish a later owner's session.");
    }

    public static void CancelAndNativeRelease()
    {
        var gate = Gate();
        Check(gate.TryBegin(Request(ClientA, Attempt), Tick(0), out var lease) ==
              RuntimeUsbTestAdmission.Started, "Reservation required.");
        var owner = lease ?? throw new InvalidOperationException("No owning attempt.");
        Check(gate.RequestCancel(owner.Handle) && gate.RequestCancel(owner.Handle) &&
              owner.StopRequested.IsCancellationRequested &&
              gate.GetStatus(owner.Handle, Tick(1)).Phase == RuntimeUsbTestPhase.CancelRequested,
            "Repeated cancellation must signal exactly the owning attempt.");
        Check(gate.TryBegin(Request(ClientB, Guid.NewGuid()), Tick(1), out _) ==
              RuntimeUsbTestAdmission.Busy,
            "Cancellation is not native release.");
        Check(gate.ReportTechnicalOutcome(owner, CatalogUsbTestTechnicalOutcome.CleanupPending) &&
              gate.GetStatus(owner.Handle, Tick(1)).Phase == RuntimeUsbTestPhase.CleanupPending &&
              gate.TryBegin(Request(ClientB, Guid.NewGuid()), Tick(2), out _) ==
              RuntimeUsbTestAdmission.Busy,
            "Uncertain native completion must block another acquisition.");
        Check(gate.ReportTechnicalOutcome(owner, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              gate.GetStatus(owner.Handle, Tick(3)) is
              { Phase: RuntimeUsbTestPhase.CanceledAndReleased,
                TechnicalOutcome: CatalogUsbTestTechnicalOutcome.CanceledAndReleased },
            "Late native success must never override cancellation or an earlier cleanup problem.");

        var clean = Request(ClientB, Guid.NewGuid());
        Check(gate.TryBegin(clean, Tick(3), out var next) == RuntimeUsbTestAdmission.Started,
            "A separately identified attempt may start after confirmed native release.");
        var nextOwner = next ?? throw new InvalidOperationException("No second lease.");
        Check(gate.ReportTechnicalOutcome(nextOwner, CatalogUsbTestTechnicalOutcome.CleanupPending) &&
              gate.ReportTechnicalOutcome(nextOwner, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              gate.GetStatus(nextOwner.Handle, Tick(4)).Phase == RuntimeUsbTestPhase.FailedAndReleased,
            "A technically uncertain attempt may never be reported as a clean success.");
    }

    public static void SilenceDeadlineAndUncertainCleanup()
    {
        var gate = Gate();
        Check(gate.TryBegin(Request(ClientA, Attempt), Tick(0), out var lease) ==
              RuntimeUsbTestAdmission.Started, "Session admission required.");
        var owner = lease ?? throw new InvalidOperationException("No session lease.");
        Check(gate.GetStatus(owner.Handle, Tick(8)).Phase == RuntimeUsbTestPhase.Running,
            "Matching client status renews the heartbeat.");
        gate.Sweep(Tick(16));
        Check(!owner.StopRequested.IsCancellationRequested,
            "A recent heartbeat must protect an unexpired session.");
        gate.Sweep(Tick(19));
        Check(owner.StopRequested.IsCancellationRequested &&
              gate.GetStatus(owner.Handle, Tick(20)).Phase == RuntimeUsbTestPhase.CancelRequested,
            "Silence requests cancellation without releasing native ownership.");
        Check(gate.TryBegin(Request(ClientB, Guid.NewGuid()), Tick(20), out _) ==
              RuntimeUsbTestAdmission.Busy,
            "An abandoned worker keeps its lease until native release.");
        Check(gate.ReportTechnicalOutcome(owner, CatalogUsbTestTechnicalOutcome.CanceledAndReleased),
            "Worker must acknowledge actual cancellation release.");

        Check(gate.TryBegin(Request(ClientA, Guid.NewGuid()), Tick(21), out var next) ==
              RuntimeUsbTestAdmission.Started, "Fresh attempt after terminal release required.");
        var nextOwner = next ?? throw new InvalidOperationException("No fresh lease.");
        Check(gate.GetStatus(nextOwner.Handle, Tick(30)).Phase == RuntimeUsbTestPhase.Running,
            "Status refreshes the client-silence deadline, not absolute lifetime.");
        gate.Sweep(Tick(61));
        Check(nextOwner.StopRequested.IsCancellationRequested &&
              gate.GetStatus(nextOwner.Handle, Tick(62)).Phase == RuntimeUsbTestPhase.CancelRequested &&
              gate.ReportTechnicalOutcome(nextOwner, CatalogUsbTestTechnicalOutcome.CleanupPending) &&
              gate.GetStatus(nextOwner.Handle, Tick(62)).Phase == RuntimeUsbTestPhase.CleanupPending,
            "Absolute deadline must cancel, while unknown cleanup retains exclusive ownership.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
