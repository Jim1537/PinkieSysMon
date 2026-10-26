namespace PinkieSysMon;

// Observation only. The calling owner retains its existing cleanup order,
// exception policy and waits; the observer owns no application/native resources.
internal sealed class CleanupOperation : IDisposable
{
    internal static readonly TimeSpan PendingThreshold = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly FileLogger _log;
    private readonly LogOperation _operation;
    private readonly TimeProvider _time;
    private readonly ITimer? _timer;
    private string _phase = "begin";
    private long _phaseStarted, _lastReport;
    private bool _active, _reported, _disposed;
    private int _polling, _failedSteps, _captureFailures;

    internal int CaptureFailures => Volatile.Read(ref _captureFailures);

    internal CleanupOperation(FileLogger log, string operation, string? subject = null,
        TimeProvider? time = null, bool monitor = true)
    {
        _log = log;
        _operation = new LogOperation(log, operation, subject);
        _time = time ?? TimeProvider.System;
        if (monitor)
        {
            try { _timer = _time.CreateTimer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); }
            catch (Exception error) { CaptureFailed(error); }
        }
    }

    internal void Step(string phase, Action cleanup)
    {
        Observe(() =>
        {
            var now = _time.GetTimestamp();
            lock (_sync)
            {
                _phase = phase; _phaseStarted = now;
                _active = true; _reported = false;
            }
            _operation.Phase(phase);
        });
        var returned = false;
        try
        {
            cleanup();
            returned = true;
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref _failedSteps);
            Observe(() => _operation.RecordFailure(error, phase));
            throw;
        }
        finally
        {
            Observe(() =>
            {
                lock (_sync) _active = false;
                if (returned) _operation.Phase(phase + ".returned");
            });
        }
    }

    // Explicit owner confirmation, reached only after its required steps return.
    internal void Complete() => Observe(() =>
    {
        if (Volatile.Read(ref _failedSteps) != 0)
            _operation.Partial("At least one cleanup step failed; full release is not confirmed.");
        else
            _operation.Complete("All observed cleanup steps returned; this is not independent native-release proof.");
    });

    internal void Poll()
    {
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
        try
        {
            Observe(() =>
            {
                string phase;
                double age;
                DateTimeOffset observedAt;
                lock (_sync)
                {
                    if (_disposed || !_active) return;
                    var now = _time.GetTimestamp();
                    var elapsed = _time.GetElapsedTime(_phaseStarted, now);
                    if (elapsed < PendingThreshold ||
                        (_reported && _time.GetElapsedTime(_lastReport, now) < RepeatInterval)) return;
                    _reported = true; _lastReport = now;
                    phase = _phase; age = elapsed.TotalMilliseconds;
                    observedAt = _time.GetUtcNow();
                }
                // The sampled phase can finish before this record reaches disk.
                _operation.Pending(phase, FormattableString.Invariant(
                    $"observedAt={observedAt:O}; activeAtObservation=True; phaseAgeMs={age:F1}; captureFailures={CaptureFailures}; no cancellation or release requested by observer."));
            });
        }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void Observe(Action observation)
    {
        try { observation(); }
        catch (Exception error) { CaptureFailed(error); }
    }

    private void CaptureFailed(Exception error)
    {
        Interlocked.Increment(ref _captureFailures);
        try { _log.Error("Cleanup diagnostic observation failed; cleanup ownership is unchanged.", error); }
        catch { } // A diagnostic fault cannot replace an application exception.
    }

    public void Dispose()
    {
        Observe(() => { lock (_sync) { _disposed = true; _active = false; } });
        Observe(() => _timer?.Dispose()); // Never wait for a logging callback.
        Observe(_operation.Dispose);
    }
}
