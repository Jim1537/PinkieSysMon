using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

internal sealed record CatalogUsbTestReply(CatalogUsbTestHandle Handle, RuntimeUsbTestStatus Status);

// One transient owner. UI/IPC admission is short; rendering and native release
// run on a worker. The existing gate alone decides when ownership is released.
internal sealed class RuntimeUsbTestService
{
    internal const int FrameCount = 80;
    internal const int RefreshMs = 500;
    internal static readonly TimeSpan ObservationDuration = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly RuntimeUsbTestSessionGate _gate = new(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(90));
    private readonly TimeSpan _observationDuration;
    private readonly Func<CatalogUsbCandidateSnapshot> _capture;
    private readonly Func<CatalogUsbTrialIntent, CatalogUsbCandidateSnapshot, bool> _isOwned;
    private readonly Func<CatalogUsbTrialIntent, FramePump> _createPump;
    private readonly Func<IReadOnlySet<string>> _readIgnoredNodes;
    private readonly FileLogger _log;
    private CatalogUsbTestHandle? _active;
    private long _observationStarted;
    private bool _disconnecting;
    private long _completedAt;
    internal bool HasActive => Volatile.Read(ref _active) is not null;
    internal long CompletedAt => Interlocked.Read(ref _completedAt);
    internal event Action? TestFailed;

    internal RuntimeUsbTestService(Func<CatalogUsbCandidateSnapshot> capture,
        Func<CatalogUsbTrialIntent, CatalogUsbCandidateSnapshot, bool> isOwned,
        Func<CatalogUsbTrialIntent, FramePump> createPump, FileLogger log,
        Func<IReadOnlySet<string>>? readIgnoredNodes = null, TimeSpan? observationDuration = null)
    { _capture = capture; _isOwned = isOwned; _createPump = createPump; _log = log;
      _readIgnoredNodes = readIgnoredNodes ?? (() => new HashSet<string>());
      _observationDuration = observationDuration ?? ObservationDuration;
      if (_observationDuration <= TimeSpan.Zero || _observationDuration > ObservationDuration)
          throw new ArgumentOutOfRangeException(nameof(observationDuration)); }

    internal RuntimeIpcResponse Execute(RuntimeIpcCommand command)
    {
        var intent = command.UsbTestBegin?.Intent;
        var attempt = intent?.AttemptId ?? command.UsbTestHandle?.AttemptId;
        using var operation = command.Kind == RuntimeIpcCommandKind.UsbTestStatus || attempt is null ? null
            : new LogOperation(_log, "runtime.usb-test.request", "usb-test-" + attempt.Value.ToString("N"), attempt.Value.ToString("N"));
        operation?.Phase(command.Kind == RuntimeIpcCommandKind.UsbTestBegin ? "admission" : "cancel.requested");
        RuntimeIpcResponse response;
        string reason;
        Exception? admissionFailure;
        lock (_sync) response = ExecuteCore(command, out reason, out admissionFailure);
        // Diagnostic I/O is outside the existing session/admission gate.
        if (admissionFailure is not null) operation?.Fail(admissionFailure, $"reason={reason}; protocol={intent?.Protocol}; geometry={intent?.Width}x{intent?.Height}");
        else if (!response.Success) operation?.Cancel(reason);
        else operation?.Complete($"responseSuccess=true; reason={reason}; releaseNotAsserted=true");
        return response;
    }
    private RuntimeIpcResponse ExecuteCore(RuntimeIpcCommand command, out string reason, out Exception? admissionFailure)
    {
        reason = "status";
        admissionFailure = null;
        if (command.Kind == RuntimeIpcCommandKind.UsbTestBegin && command.UsbTestBegin is { } request)
        {
            try
            {
                var snapshot = _capture();
                var preflight = RuntimeUsbTestPreflight.Check(request.Intent, snapshot, _readIgnoredNodes());
                if (preflight != RuntimeUsbTestPreflightStatus.Admitted)
                {
                    reason = "preflight." + preflight;
                    return new(false, $"Test not passed: current interface validation returned {preflight}. Reconnect and retry.");
                }
                if (_isOwned(request.Intent, snapshot))
                {
                    reason = "ordinary-output-owner";
                    return new(false, "Test not passed: this physical device is owned by an ordinary Output. Disable that Output first.");
                }
                var admission = _gate.TryBegin(request, Stopwatch.GetTimestamp(), out var lease);
                reason = "admission." + admission;
                if (admission is not (RuntimeUsbTestAdmission.Started or RuntimeUsbTestAdmission.AlreadyRunning))
                    return new(false, "Test not started: another attempt owns Runtime or this attempt ID was already used.");
                if (lease is not null)
                {
                    Interlocked.Exchange(ref _observationStarted, 0);
                    _disconnecting = false;
                    Volatile.Write(ref _active, lease.Handle);
                    _ = Task.Run(() => RunAsync(lease));
                }
                return Status(new(request.ClientId, request.Intent.AttemptId));
            }
            catch (Exception error)
            {
                reason = "discovery-or-admission-failed";
                admissionFailure = error;
                return new(false, "Test not started: discovery or admission failed. Check the Runtime log and retry.");
            }
        }
        if (command.UsbTestHandle is { } handle)
        {
            if (command.Kind == RuntimeIpcCommandKind.UsbTestCancel)
            { reason = "cancel-processed-not-proof-of-release"; _gate.RequestCancel(handle); }
            return Status(handle);
        }
        return new(false, "Invalid USB Test request.");
    }

    private RuntimeIpcResponse Status(CatalogUsbTestHandle handle)
    {
        var status = _gate.GetStatus(handle, Stopwatch.GetTimestamp());
        if (Volatile.Read(ref _active) == handle && status.Phase == RuntimeUsbTestPhase.Running)
        {
            var started = Interlocked.Read(ref _observationStarted);
            status = _disconnecting ? status with { Phase = RuntimeUsbTestPhase.Disconnecting }
                : started == 0 ? status with { Phase = RuntimeUsbTestPhase.Connecting }
                : status with
                {
                    Phase = RuntimeUsbTestPhase.Observing,
                    SecondsRemaining = Math.Max(0, (int)Math.Ceiling(
                        (_observationDuration - Stopwatch.GetElapsedTime(started)).TotalSeconds))
                };
        }
        return new(true, JsonSerializer.Serialize(new CatalogUsbTestReply(handle, status)));
    }
    internal void Tick() => _gate.Sweep(Stopwatch.GetTimestamp());
    internal void CancelActive()
    {
        if (Volatile.Read(ref _active) is { } handle) _gate.RequestCancel(handle);
    }

    private async Task RunAsync(RuntimeUsbTestLease lease)
    {
        using var operation = new LogOperation(_log, "runtime.usb-test.worker",
            "usb-test-" + lease.Request.Intent.AttemptId.ToString("N"), lease.Request.Intent.AttemptId.ToString("N"));
        FramePump? pump = null;
        Exception? cleanupFailure = null;
        Exception? failure = null;
        var complete = false;
        var cleanupFailed = false;
        try
        {
            operation.Phase("worker.preflight", $"protocol={lease.Request.Intent.Protocol}; geometry={lease.Request.Intent.Width}x{lease.Request.Intent.Height}");
            lease.StopRequested.ThrowIfCancellationRequested();
            if (RuntimeUsbTestPreflight.Check(lease.Request.Intent, _capture(), _readIgnoredNodes()) != RuntimeUsbTestPreflightStatus.Admitted)
                throw new InvalidOperationException("Exact Test binding changed before acquisition.");
            operation.Phase("pump.create");
            pump = _createPump(lease.Request.Intent);
            lease.StopRequested.ThrowIfCancellationRequested();
            operation.Phase("pump.start");
            pump.Start();
            operation.Phase("connecting");
            while (!lease.StopRequested.IsCancellationRequested && pump.TestFailure is null && pump.SentFrames < FrameCount)
            {
                _gate.Sweep(Stopwatch.GetTimestamp());
                if (pump.SentFrames > 0 && Interlocked.Read(ref _observationStarted) == 0 &&
                    Interlocked.CompareExchange(ref _observationStarted, Stopwatch.GetTimestamp(), 0) == 0)
                    operation.Phase("observing", "firstFrameDelivered=true; observationClockStarted=true");
                var started = Interlocked.Read(ref _observationStarted);
                if (started != 0 && Stopwatch.GetElapsedTime(started) >= _observationDuration)
                {
                    complete = true;
                    break;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            failure = pump.TestFailure;
        }
        catch (OperationCanceledException) when (lease.StopRequested.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            lock (_sync) _disconnecting = true;
            operation.Phase("disconnecting", "nativeRelease=unconfirmed");
            // Dispose may wait indefinitely for a native driver. This worker
            // retains the reservation; IPC and the UI stay responsive.
            try { pump?.Dispose(); }
            catch (Exception error) { failure ??= error; cleanupFailure = error; cleanupFailed = true; }
            cleanupFailed |= pump?.TestCleanupFailed == true;
            failure ??= pump?.TestFailure;
            var context = $"protocol={lease.Request.Intent.Protocol}; geometry={lease.Request.Intent.Width}x{lease.Request.Intent.Height}";
            if (failure is not null) operation.RecordFailure(failure, "worker-or-cleanup", context);
            if (cleanupFailure is not null && !ReferenceEquals(cleanupFailure, failure))
                operation.RecordFailure(cleanupFailure, "cleanup", context);
            if (cleanupFailed)
            {
                _gate.ReportTechnicalOutcome(lease, CatalogUsbTestTechnicalOutcome.CleanupPending);
                NotifyFailure();
                operation.Pending("cleanup.pending", context + "; nativeRelease=unconfirmed; owner=Runtime");
                operation.Partial("technicalOutcome=CleanupPending; reservationRetained=true");
            }
            else
            {
                var outcome = lease.StopRequested.IsCancellationRequested ? CatalogUsbTestTechnicalOutcome.CanceledAndReleased
                    : complete && failure is null ? CatalogUsbTestTechnicalOutcome.SucceededAndReleased
                    : CatalogUsbTestTechnicalOutcome.FailedAndReleased;
                // Queue failure feedback before exposing a result that lets the
                // test-only UI exit and dispose its notification service.
                if (outcome != CatalogUsbTestTechnicalOutcome.SucceededAndReleased) NotifyFailure();
                lock (_sync)
                {
                    _gate.ReportTechnicalOutcome(lease, outcome);
                    Interlocked.Exchange(ref _completedAt, Stopwatch.GetTimestamp());
                    Volatile.Write(ref _active, null);
                }
                var detail = $"{context}; technicalOutcome={outcome}; deliveredFrames={pump?.SentFrames ?? 0}; nativeRelease=confirmed; visualApproval=not-observed-by-Runtime";
                if (outcome == CatalogUsbTestTechnicalOutcome.SucceededAndReleased) operation.Complete(detail);
                else if (outcome == CatalogUsbTestTechnicalOutcome.CanceledAndReleased) operation.Cancel(detail);
                else
                {
                    if (failure is null) operation.RecordFailure(null, "technical.result", detail);
                    operation.Partial(detail);
                }
            }
        }
    }

    private void NotifyFailure()
    {
        try { TestFailed?.Invoke(); }
        catch (Exception error) { _log.Warn("USB Test failure notification could not be delivered.", error); }
    }

    internal static bool IsOwned(CatalogUsbTrialIntent intent, CatalogUsbCandidateSnapshot snapshot,
        IEnumerable<OutputTargetConfig> owners)
    {
        foreach (var target in owners.Where(target => OutputTargetContract.IsPhysicalKind(target.Kind)))
        {
            if (target.DeviceId.Equals(intent.InterfaceId, StringComparison.OrdinalIgnoreCase)) return true;
            var matches = snapshot.Candidates.Where(candidate => candidate.Id.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            // An existing owner with unresolved metadata cannot be proved to be
            // another physical unit. Never open around that uncertainty.
            if (matches.Length != 1 || matches[0].PnpNodeKey.Equals(intent.PnpNodeKey, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static FramePump CreatePump(string root, FileLogger log, CatalogUsbTrialIntent intent)
    {
        var board = AdaptiveDashboardFactory.Create(intent.Width, intent.Height, 0, root);
        var metrics = new MetricStore();
        // Use the same System provider and per-render clock as ordinary frames.
        // Missing optional values keep their usual unavailable presentation.
        var source = new WindowsSystemTelemetrySource(log);
        metrics.Publish(source.Capture([SystemMetricContract.DateTime, SystemMetricContract.PcName,
            SystemMetricContract.PcProcessor, SystemMetricContract.PcRam, SystemMetricContract.Edition, SystemMetricContract.Uptime]));
        if (!metrics.Snapshot().TryGetValue(SystemMetricContract.DateTime, out var clock) || clock is not DateTime)
            throw new InvalidOperationException("System clock is unavailable; a changing-seconds Test cannot be verified.");
        var target = new OutputTargetConfig
        {
            Id = "usb-test-" + intent.AttemptId.ToString("N"), Name = "USB Test", Kind = OutputTargetContract.PhysicalKind,
            Transport = intent.Protocol, DeviceId = intent.InterfaceId, Width = intent.Width, Height = intent.Height,
            DeviceEnabled = true, ProviderEnabled = false, RefreshIntervalMs = RefreshMs, JpegQuality = 92, Usb = new(),
            EncryptedJpeg = intent.Protocol == OutputTargetContract.EncryptedJpegTransport ? new() : null
        };
        return new FramePump(new AppConfig(), target, board, metrics, log, maximumFrames: FrameCount, stopOnFailure: true);
    }
}
