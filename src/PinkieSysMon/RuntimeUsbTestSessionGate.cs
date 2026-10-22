using System.Diagnostics;

namespace PinkieSysMon;

// Reservation state only. Actual USB opening and trusted native completion
// are reported by the Runtime worker; IPC callers never release a lease.
internal sealed class RuntimeUsbTestLease(
    CatalogUsbTestBeginRequest request, CancellationTokenSource cancellation)
{
    internal CatalogUsbTestBeginRequest Request { get; } = request;
    internal CatalogUsbTestHandle Handle { get; } = new(request.ClientId, request.Intent.AttemptId);
    internal CancellationTokenSource Cancellation { get; } = cancellation;
    internal CancellationToken StopRequested => Cancellation.Token;
}

internal enum RuntimeUsbTestAdmission { Started, AlreadyRunning, Busy, AttemptReused, CandidateRejected }
internal enum RuntimeUsbTestPhase
{
    Missing, Running, CancelRequested, CleanupPending,
    SucceededAndReleased, FailedAndReleased, CanceledAndReleased
}

internal sealed record RuntimeUsbTestStatus(
    RuntimeUsbTestPhase Phase, CatalogUsbTestTechnicalOutcome? TechnicalOutcome = null);

// Caller must invoke Sweep(Stopwatch.GetTimestamp()) on the existing Runtime
// heartbeat. This state gate does not start background polling.
internal sealed class RuntimeUsbTestSessionGate(
    TimeSpan clientSilenceLimit, TimeSpan absoluteLifetime)
{
    private const int MaxResults = 32;
    private readonly object _sync = new();
    private readonly Dictionary<CatalogUsbTestHandle, RuntimeUsbTestStatus> _results = new();
    private readonly Queue<CatalogUsbTestHandle> _order = new();
    private Active? _active;

    private sealed class Active(RuntimeUsbTestLease lease, long started)
    {
        internal RuntimeUsbTestLease Lease { get; } = lease;
        internal long Started { get; } = started;
        internal long LastHeartbeat { get; set; } = started;
        internal bool CancelRequested { get; set; }
        internal bool CleanupUncertain { get; set; }
    }

    internal RuntimeUsbTestAdmission TryBegin(
        CatalogUsbTestBeginRequest request, long timestamp, out RuntimeUsbTestLease? lease)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = CatalogUsbTestIpcProtocol.BuildBegin(request.ClientId, request.Intent);
        Sweep(timestamp);
        lease = null;
        var key = new CatalogUsbTestHandle(request.ClientId, request.Intent.AttemptId);
        lock (_sync)
        {
            if (_results.ContainsKey(key))
                return RuntimeUsbTestAdmission.AttemptReused;
            if (_active is { } busy)
                return busy.Lease.Handle == key
                    ? busy.Lease.Request == request
                        ? RuntimeUsbTestAdmission.AlreadyRunning
                        : RuntimeUsbTestAdmission.AttemptReused
                    : RuntimeUsbTestAdmission.Busy;

            lease = new RuntimeUsbTestLease(request, new CancellationTokenSource());
            _active = new Active(lease, timestamp);
            return RuntimeUsbTestAdmission.Started;
        }
    }

    internal RuntimeUsbTestStatus GetStatus(CatalogUsbTestHandle key, long timestamp)
    {
        Sweep(timestamp);
        lock (_sync)
        {
            if (_active is { } session && session.Lease.Handle == key)
            {
                if (!session.CancelRequested)
                    session.LastHeartbeat = timestamp;
                var phase = session.CleanupUncertain ? RuntimeUsbTestPhase.CleanupPending
                    : session.CancelRequested ? RuntimeUsbTestPhase.CancelRequested
                    : RuntimeUsbTestPhase.Running;
                return new(phase);
            }
            return _results.TryGetValue(key, out var terminal)
                ? terminal : new(RuntimeUsbTestPhase.Missing);
        }
    }

    internal bool RequestCancel(CatalogUsbTestHandle key)
    {
        CancellationTokenSource? token = null;
        lock (_sync)
        {
            if (_active is not { } session || session.Lease.Handle != key)
                return false;
            token = MarkCancel(session);
        }
        Signal(token);
        return true;
    }

    // Silence or absolute lifetime may stop fresh work, but cannot release a
    // native handle; only a trusted terminal worker result does that.
    internal void Sweep(long timestamp)
    {
        CancellationTokenSource? token = null;
        lock (_sync)
        {
            if (_active is { } session && !session.CancelRequested &&
                (Stopwatch.GetElapsedTime(session.Started, timestamp) >= absoluteLifetime ||
                 Stopwatch.GetElapsedTime(session.LastHeartbeat, timestamp) >= clientSilenceLimit))
                token = MarkCancel(session);
        }
        Signal(token);
    }

    internal bool ReportTechnicalOutcome(
        RuntimeUsbTestLease lease, CatalogUsbTestTechnicalOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!Enum.IsDefined(outcome))
            return false;
        lock (_sync)
        {
            if (_active is not { } session ||
                !ReferenceEquals(session.Lease, lease))
                return false;
            if (outcome == CatalogUsbTestTechnicalOutcome.CleanupPending)
            {
                session.CleanupUncertain = true;
                return true;
            }

            var terminal = session.CancelRequested
                ? CatalogUsbTestTechnicalOutcome.CanceledAndReleased
                : session.CleanupUncertain &&
                  outcome == CatalogUsbTestTechnicalOutcome.SucceededAndReleased
                    ? CatalogUsbTestTechnicalOutcome.FailedAndReleased
                    : outcome;
            var phase = terminal switch
            {
                CatalogUsbTestTechnicalOutcome.SucceededAndReleased => RuntimeUsbTestPhase.SucceededAndReleased,
                CatalogUsbTestTechnicalOutcome.FailedAndReleased => RuntimeUsbTestPhase.FailedAndReleased,
                _ => RuntimeUsbTestPhase.CanceledAndReleased
            };
            _active = null;
            _results.Add(lease.Handle, new(phase, terminal));
            _order.Enqueue(lease.Handle);
            while (_order.Count > MaxResults)
                _results.Remove(_order.Dequeue());
            lease.Cancellation.Dispose();
            return true;
        }
    }

    private static CancellationTokenSource? MarkCancel(Active session)
    {
        if (session.CancelRequested) return null;
        session.CancelRequested = true;
        return session.Lease.Cancellation;
    }

    private static void Signal(CancellationTokenSource? token)
    {
        if (token is null) return;
        try { token.Cancel(); }
        catch (ObjectDisposedException) { /* Terminal native release raced cancellation. */ }
    }
}
