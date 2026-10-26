using System.Text.Json;
using System.ComponentModel;

namespace PinkieSysMon;

// Observation only: never cancels, retries, polls USB, or releases native resources.
// The timer copies managed evidence under its own short lock and writes outside it.
internal sealed class PhysicalIoDiagnostics : IDisposable
{
    internal const int RecentOperationLimit = 16;
    internal const int ResponseByteLimit = 512;
    private readonly object _sync = new();
    private readonly FileLogger _log;
    private readonly string _targetId, _targetName, _protocol;
    private readonly int _timeoutMs;
    private readonly CancellationToken _token;
    private readonly TimeProvider _time;
    private readonly ITimer? _timer;
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private readonly IoRecord?[] _recent = new IoRecord?[RecentOperationLimit];
    private readonly long _created;
    private long _operation, _ioSequence, _frames, _failures, _lastSummary, _lastPendingReport;
    private long _operationStarted, _phaseStarted;
    private int _polling;
    private string _operationName = "none", _phase = "none";
    private int _recentIndex, _recentCount, _payloadBytes;
    private double _renderMs, _encodeMs, _maximumPhaseMs;
    private bool _active, _slowReported, _unexpectedNativeFailure, _disposed;
    private IoRecord? _current, _lastNonemptyReply;

    internal PhysicalIoDiagnostics(OutputTargetConfig target, int timeoutMs, FileLogger log,
        CancellationToken token, TimeProvider? time = null, bool monitor = true)
    {
        _log = log;
        _targetId = Limit(target.Id); _targetName = Limit(target.Name); _protocol = Limit(target.Transport);
        _timeoutMs = timeoutMs; _token = token; _time = time ?? TimeProvider.System;
        _created = _lastSummary = _time.GetTimestamp();
        if (monitor)
            _timer = _time.CreateTimer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    internal void Begin(string operation, int payloadBytes = 0, FrameRenderDiagnostics render = default)
    {
        string? message = null;
        lock (_sync)
        {
            _operation++;
            _operationName = operation; _phase = operation;
            _operationStarted = _phaseStarted = _time.GetTimestamp();
            _payloadBytes = payloadBytes; _renderMs = render.RenderMs; _encodeMs = render.EncodeMs;
            _active = true; _slowReported = _unexpectedNativeFailure = false; _current = null;
            _maximumPhaseMs = 0;
            if (operation != "frame") message = FormatLocked("begin");
        }
        if (message is not null) _log.Info(message);
    }

    internal void Stage(string phase)
    {
        lock (_sync)
        {
            ObservePhaseAgeLocked();
            _phase = phase; _phaseStarted = _time.GetTimestamp();
        }
    }

    internal void IoBegin(string stage, bool read, byte pipe, int requested)
    {
        lock (_sync)
        {
            ObservePhaseAgeLocked();
            _phase = stage; _phaseStarted = _time.GetTimestamp();
            _current = new IoRecord(++_ioSequence, stage, read ? "read" : "write", $"0x{pipe:X2}", requested, _phaseStarted);
            AddLocked(_current);
        }
    }

    internal void IoResult(bool success, uint actual, int nativeCode, string completion)
    {
        lock (_sync)
        {
            if (_current is not null)
            {
                _current.ElapsedMs = _time.GetElapsedTime(_current.Started, _time.GetTimestamp()).TotalMilliseconds;
                _current.Completion = completion;
                _current.NativeCode = nativeCode;
                _current.ActualBytes = success ? actual : null;
            }
            if (!success && nativeCode != 995) _unexpectedNativeFailure = true;
        }
    }

    internal void CancellationFailure(Exception error)
    {
        lock (_sync)
        {
            _unexpectedNativeFailure = true;
            if (_current is not null)
            {
                _current.CancellationErrorType = error.GetType().FullName;
                _current.CancellationHResult = error.HResult;
                _current.CancellationNativeCode = error is Win32Exception native ? native.NativeErrorCode : null;
            }
        }
    }

    internal void ReturnedWrite(int actual)
    {
        lock (_sync)
        {
            if (_current is null) return;
            _current.ReturnedCount = actual;
            _current.ActualBytes = actual >= 0 ? (uint)actual : null;
            _current.ElapsedMs = _time.GetElapsedTime(_current.Started, _time.GetTimestamp()).TotalMilliseconds;
            _current.Completion ??= "connection-returned";
        }
    }

    internal void Reply(byte[] response, int actual)
    {
        lock (_sync)
        {
            if (_current is null) return;
            _current.ReturnedCount = actual;
            _current.ActualBytes = actual >= 0 ? (uint)actual : null;
            if (actual >= 0 && actual <= response.Length)
            {
                _current.Response = response.AsSpan(0, Math.Min(actual, ResponseByteLimit)).ToArray();
                _current.ResponseTruncated = actual > ResponseByteLimit;
                if (actual > 0) _lastNonemptyReply = _current;
            }
        }
    }

    internal void Complete(string outcome)
    {
        string? message;
        bool unexpected;
        lock (_sync)
        {
            ObservePhaseAgeLocked();
            unexpected = _unexpectedNativeFailure || (!_slowReported && _maximumPhaseMs >= _timeoutMs + 250.0);
            if (_operationName == "frame" && outcome == "completed") _frames++;
            _active = false;
            message = _operationName != "frame" || _frames == 1 || _slowReported || unexpected
                ? FormatLocked(outcome) : null;
        }
        if (message is not null)
        {
            if (unexpected) _log.Error(message);
            else _log.Info(message);
        }
    }

    internal void Fail(Exception error)
    {
        string message;
        lock (_sync) { ObservePhaseAgeLocked(); _failures++; _active = false; message = FormatLocked("failed"); }
        _log.Error(message, error); // The propagated original is deduplicated by FileLogger.
    }

    internal void NativeReleaseFailure(int code)
    {
        string message;
        lock (_sync) { _unexpectedNativeFailure = true; message = FormatLocked("native-release-unconfirmed", code); }
        _log.Error(message);
    }

    // Tests can advance a synthetic monotonic clock and sample the real observer.
    internal void Poll()
    {
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
        try
        {
            string? message = null;
            bool pending = false;
            lock (_sync)
            {
                if (_disposed) return;
                var now = _time.GetTimestamp();
                var phaseAge = _time.GetElapsedTime(_phaseStarted, now).TotalMilliseconds;
                if (_active && phaseAge >= _timeoutMs + 250.0 &&
                    (!_slowReported || _time.GetElapsedTime(_lastPendingReport, now) >= TimeSpan.FromSeconds(30)))
                {
                    _slowReported = true; _lastPendingReport = now; pending = true;
                    message = FormatLocked("pending-observed");
                }
                else if (_time.GetElapsedTime(_lastSummary, now) >= TimeSpan.FromSeconds(30))
                {
                    _lastSummary = now;
                    message = FormatLocked("progress-snapshot");
                }
            }
            // Evidence describes the sampled instant; completion may race the write.
            if (message is not null)
            {
                if (pending) _log.Error(message);
                else _log.Info(message);
            }
        }
        catch (Exception error) { _log.Error("Physical USB diagnostic observation failed.", error); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void ObservePhaseAgeLocked() =>
        _maximumPhaseMs = Math.Max(_maximumPhaseMs,
            _time.GetElapsedTime(_phaseStarted, _time.GetTimestamp()).TotalMilliseconds);

    private void AddLocked(IoRecord record)
    {
        _recent[_recentIndex] = record;
        _recentIndex = (_recentIndex + 1) % RecentOperationLimit;
        _recentCount = Math.Min(_recentCount + 1, RecentOperationLimit);
    }

    // Caller holds only this observer's lock. No disk I/O or native handle access.
    private string FormatLocked(string outcome, int? releaseError = null)
    {
        var now = _time.GetTimestamp();
        return "PHYSICAL_IO " + JsonSerializer.Serialize(new
        {
            runtime = RuntimeVersion.Current, connectionId = _connectionId,
            targetId = _targetId, targetName = _targetName, protocol = _protocol,
            operation = _operationName, operationId = _operation, phase = _phase, outcome,
            observedAt = _time.GetUtcNow(), configuredTimeoutMs = _timeoutMs,
            elapsedMs = _time.GetElapsedTime(_operationStarted, now).TotalMilliseconds,
            phaseAgeMs = _time.GetElapsedTime(_phaseStarted, now).TotalMilliseconds,
            maximumPhaseMs = _maximumPhaseMs,
            currentIoElapsedMs = _current is null ? (double?)null :
                _current.ElapsedMs ?? _time.GetElapsedTime(_current.Started, now).TotalMilliseconds,
            connectionAgeMs = _time.GetElapsedTime(_created, now).TotalMilliseconds,
            activeAtObservation = _active, cancellationRequested = _token.IsCancellationRequested,
            framesCompleted = _frames, failures = _failures, ioSequence = _ioSequence,
            payloadBytes = _payloadBytes,
            renderMs = double.IsFinite(_renderMs) ? (double?)_renderMs : null,
            encodeMs = double.IsFinite(_encodeMs) ? (double?)_encodeMs : null,
            unexpectedNativeFailure = _unexpectedNativeFailure, releaseError,
            omittedIoOperations = _ioSequence - _recentCount,
            lastNonemptyReply = _lastNonemptyReply is null ? null : new
            {
                _lastNonemptyReply.Sequence, _lastNonemptyReply.Stage, _lastNonemptyReply.ActualBytes,
                responseHex = _lastNonemptyReply.Response is null ? null : Convert.ToHexString(_lastNonemptyReply.Response)
            },
            recent = Enumerable.Range(0, _recentCount)
                .Select(i => _recent[(_recentIndex - _recentCount + i + RecentOperationLimit) % RecentOperationLimit]!)
                .Select(r => new { r.Sequence, r.Stage, r.Direction, r.Pipe, r.RequestedBytes,
                    r.ActualBytes, r.ReturnedCount, r.NativeCode, r.Completion, r.ElapsedMs,
                    r.CancellationErrorType, r.CancellationHResult, r.CancellationNativeCode,
                    responseHex = r.Response is null ? null : Convert.ToHexString(r.Response), r.ResponseTruncated })
        });
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; }
        _timer?.Dispose(); // No wait for a logging callback; it owns no native resources.
    }

    private static string Limit(string value) => value.Length <= 256 ? value : value[..256];

    private sealed class IoRecord(long sequence, string stage, string direction, string pipe, int requested, long started)
    {
        internal long Started { get; } = started;
        public double? ElapsedMs { get; set; }
        public long Sequence { get; } = sequence;
        public string Stage { get; } = stage;
        public string Direction { get; } = direction;
        public string Pipe { get; } = pipe;
        public int RequestedBytes { get; } = requested;
        public uint? ActualBytes { get; set; }
        public int? ReturnedCount { get; set; }
        public int? NativeCode { get; set; }
        public string? Completion { get; set; }
        public string? CancellationErrorType { get; set; }
        public int? CancellationHResult { get; set; }
        public int? CancellationNativeCode { get; set; }
        public byte[]? Response { get; set; }
        public bool ResponseTruncated { get; set; }
    }
}

internal sealed class ObservedPhysicalFrameTransport(
    IPhysicalFrameTransport inner, PhysicalIoDiagnostics diagnostics, CancellationToken token) : IPhysicalFrameTransport
{
    public int WireRotationDegrees => inner.WireRotationDegrees;
    public PhysicalFrameProfile Profile => inner.Profile;

    internal static IPhysicalFrameTransport? Open(OutputTargetConfig target, int timeoutMs, FileLogger log,
        CancellationToken token, Func<PhysicalIoDiagnostics, IPhysicalFrameTransport?> open,
        TimeProvider? time = null, bool monitor = true)
    {
        var observation = new PhysicalIoDiagnostics(target, timeoutMs, log, token, time, monitor);
        observation.Begin("open");
        try
        {
            var inner = open(observation);
            observation.Complete(inner is null ? "missing" : "opened");
            if (inner is not null) return new ObservedPhysicalFrameTransport(inner, observation, token);
            observation.Dispose();
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            observation.Complete("cancelled-after-open-return");
            observation.Dispose();
            throw;
        }
        catch (Exception error) { observation.Fail(error); observation.Dispose(); throw; }
    }

    public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics render)
    {
        diagnostics.Begin("frame", jpeg.Length, render);
        try { inner.SendJpegWithDiagnostics(jpeg, render); diagnostics.Complete("completed"); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { diagnostics.Complete("cancelled-after-transfer-return"); throw; }
        catch (Exception error) { diagnostics.Fail(error); throw; }
    }

    public void RecordLifecycle(string state, string reason) => inner.RecordLifecycle(state, reason);

    public void Dispose()
    {
        diagnostics.Begin("dispose");
        try { inner.Dispose(); diagnostics.Complete("dispose-returned"); }
        catch (Exception error) { diagnostics.Fail(error); throw; }
        finally { diagnostics.Dispose(); }
    }
}
