using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

internal sealed record CatalogUsbTestReply(CatalogUsbTestHandle Handle, RuntimeUsbTestStatus Status);

// One transient owner. UI/IPC admission is short; rendering and native release
// run on a worker. The existing gate alone decides when ownership is released.
internal sealed class RuntimeUsbTestService
{
    internal const int FrameCount = 20;
    internal const int RefreshMs = 500;
    private readonly object _sync = new();
    private readonly RuntimeUsbTestSessionGate _gate = new(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30));
    private readonly Func<CatalogUsbCandidateSnapshot> _capture;
    private readonly Func<CatalogUsbTrialIntent, CatalogUsbCandidateSnapshot, bool> _isOwned;
    private readonly Func<CatalogUsbTrialIntent, FramePump> _createPump;
    private readonly FileLogger _log;
    private CatalogUsbTestHandle? _active;
    private long _completedAt;
    internal bool HasActive => Volatile.Read(ref _active) is not null;
    internal long CompletedAt => Interlocked.Read(ref _completedAt);
    internal event Action? TestFailed;

    internal RuntimeUsbTestService(Func<CatalogUsbCandidateSnapshot> capture,
        Func<CatalogUsbTrialIntent, CatalogUsbCandidateSnapshot, bool> isOwned,
        Func<CatalogUsbTrialIntent, FramePump> createPump, FileLogger log)
    { _capture = capture; _isOwned = isOwned; _createPump = createPump; _log = log; }

    internal RuntimeIpcResponse Execute(RuntimeIpcCommand command)
    {
        lock (_sync) return ExecuteCore(command);
    }
    private RuntimeIpcResponse ExecuteCore(RuntimeIpcCommand command)
    {
        if (command.Kind == RuntimeIpcCommandKind.UsbTestBegin && command.UsbTestBegin is { } request)
        {
            try
            {
                var snapshot = _capture();
                var preflight = RuntimeUsbTestPreflight.Check(request.Intent, snapshot, new HashSet<string>());
                if (preflight != RuntimeUsbTestPreflightStatus.Admitted)
                    return new(false, $"Test not passed: current interface validation returned {preflight}. Reconnect and retry.");
                if (_isOwned(request.Intent, snapshot))
                    return new(false, "Test not passed: this physical device is owned by an ordinary Output. Disable that Output first.");
                var admission = _gate.TryBegin(request, Stopwatch.GetTimestamp(), out var lease);
                if (admission is not (RuntimeUsbTestAdmission.Started or RuntimeUsbTestAdmission.AlreadyRunning))
                    return new(false, "Test not started: another attempt owns Runtime or this attempt ID was already used.");
                if (lease is not null)
                {
                    Volatile.Write(ref _active, lease.Handle);
                    _ = Task.Run(() => RunAsync(lease));
                }
                return Status(new(request.ClientId, request.Intent.AttemptId));
            }
            catch (Exception error)
            {
                LogFailure(request.Intent, "admission", error);
                return new(false, "Test not started: discovery or admission failed. Check the Runtime log and retry.");
            }
        }
        if (command.UsbTestHandle is { } handle)
        {
            if (command.Kind == RuntimeIpcCommandKind.UsbTestCancel) _gate.RequestCancel(handle);
            return Status(handle);
        }
        return new(false, "Invalid USB Test request.");
    }

    private RuntimeIpcResponse Status(CatalogUsbTestHandle handle) => new(true,
        JsonSerializer.Serialize(new CatalogUsbTestReply(handle, _gate.GetStatus(handle, Stopwatch.GetTimestamp()))));
    internal void Tick() => _gate.Sweep(Stopwatch.GetTimestamp());
    internal void CancelActive()
    {
        if (Volatile.Read(ref _active) is { } handle) _gate.RequestCancel(handle);
    }

    private async Task RunAsync(RuntimeUsbTestLease lease)
    {
        FramePump? pump = null;
        Exception? failure = null;
        var complete = false;
        var cleanupFailed = false;
        try
        {
            lease.StopRequested.ThrowIfCancellationRequested();
            if (RuntimeUsbTestPreflight.Check(lease.Request.Intent, _capture(), new HashSet<string>()) != RuntimeUsbTestPreflightStatus.Admitted)
                throw new InvalidOperationException("Exact Test binding changed before acquisition.");
            pump = _createPump(lease.Request.Intent);
            lease.StopRequested.ThrowIfCancellationRequested();
            pump.Start();
            while (!lease.StopRequested.IsCancellationRequested && pump.TestFailure is null && pump.SentFrames < FrameCount)
            {
                _gate.Sweep(Stopwatch.GetTimestamp());
                await Task.Delay(50).ConfigureAwait(false);
            }
            complete = pump.SentFrames == FrameCount && pump.TestFailure is null;
            failure = pump.TestFailure;
        }
        catch (OperationCanceledException) when (lease.StopRequested.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            // Dispose may wait indefinitely for a native driver. This worker
            // retains the reservation; IPC and the UI stay responsive.
            try { pump?.Dispose(); }
            catch (Exception error) { failure ??= error; cleanupFailed = true; }
            cleanupFailed |= pump?.TestCleanupFailed == true;
            failure ??= pump?.TestFailure;
            if (failure is not null) LogFailure(lease.Request.Intent, "worker-or-cleanup", failure);
            if (cleanupFailed)
            {
                _gate.ReportTechnicalOutcome(lease, CatalogUsbTestTechnicalOutcome.CleanupPending);
                NotifyFailure();
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
                _log.Info($"USB Test attempt={lease.Handle.AttemptId:N}; result={outcome}; nativeRelease=confirmed.");
            }
        }
    }

    private void NotifyFailure()
    {
        try { TestFailed?.Invoke(); }
        catch (Exception error) { _log.Warn("USB Test failure notification could not be delivered.", error); }
    }
    private void LogFailure(CatalogUsbTrialIntent intent, string stage, Exception error) =>
        _log.Warn($"USB Test attempt={intent.AttemptId:N}; stage={stage}; protocol={intent.Protocol}; geometry={intent.Width}x{intent.Height}; Test not passed.", error);

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
