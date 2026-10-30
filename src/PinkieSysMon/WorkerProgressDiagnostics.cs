using System.Diagnostics;
using System.Text.Json;
using Timer = System.Threading.Timer;

namespace PinkieSysMon;

// Observation never cancels, frees resources or completes the observed worker.
internal sealed class WorkerProgressDiagnostics : IDisposable
{
    private readonly object _stateSync = new(), _outputSync = new();
    private readonly FileLogger _log;
    private readonly string _component, _pendingKey;
    private readonly Func<long> _timestamp;
    private Timer? _timer;
    private readonly DiagnosticStateObserver _states;
    private string _phase = "waiting", _subject = "";
    private long _started, _completed, _rejected, _lastDuration, _reportedRejected, _reportedCompletion;
    private Exception? _firstRejection;
    private bool _active, _closed, _reportedPending;

    internal WorkerProgressDiagnostics(FileLogger log, string component,
        Func<long>? timestamp = null, bool startTimer = true)
    {
        _log = log; _component = component;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _pendingKey = component + ".pending." + Guid.NewGuid().ToString("N");
        _states = new(log, component, _timestamp);
        if (startTimer) StartMonitoring();
    }

    internal void StartMonitoring()
    {
        lock (_outputSync)
            if (!_closed && _timer is null)
                _timer = new Timer(_ => Observe(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    // These methods copy counters only; safe even when the caller owns app state.
    internal void Begin(string phase, string subject = "")
    {
        lock (_stateSync) { _phase = phase; _subject = subject; _started = _timestamp(); _active = true; }
    }
    internal void Complete()
    {
        lock (_stateSync)
        {
            if (!_active) return;
            _lastDuration = _timestamp() - _started;
            _completed++; _active = false;
        }
    }
    internal void Rejected(Exception error)
    {
        lock (_stateSync) { _rejected++; _firstRejection ??= error; }
    }

    internal void Observe()
    {
        lock (_outputSync)
        {
            if (_closed) return;
            string phase, subject;
            long age, completed, rejected, lastDuration;
            bool active;
            Exception? rejection;
            lock (_stateSync)
            {
                phase = _phase; subject = _subject; active = _active;
                age = active ? _timestamp() - _started : 0;
                completed = _completed; rejected = _rejected; lastDuration = _lastDuration;
                rejection = _firstRejection;
            }
            _states.Observe("worker", active ? phase : "waiting",
                $"subject={subject}; active={active}; ageMs={age * 1000d / Stopwatch.Frequency}; completed={completed}; rejected={rejected}; lastDurationMs={lastDuration * 1000d / Stopwatch.Frequency}");
            if (rejected > _reportedRejected)
            {
                _log.Error(System.Text.Json.JsonSerializer.Serialize(new
                { eventName = _component + ".queue.rejected", rejected, firstExceptionRetained = true }), rejection);
                _reportedRejected = rejected;
            }
            if (completed != _reportedCompletion && lastDuration >= 30 * Stopwatch.Frequency)
                _log.Info(System.Text.Json.JsonSerializer.Serialize(new
                { eventName = _component + ".slow.returned", completed, durationMs = lastDuration * 1000d / Stopwatch.Frequency }));
            _reportedCompletion = completed;
            if (active && age >= 30 * Stopwatch.Frequency)
            {
                _reportedPending = true;
                _log.ErrorThrottled(_pendingKey, TimeSpan.FromSeconds(30), JsonSerializer.Serialize(new
                {
                    eventName = _component + ".pending", phase,
                    subject = subject.Length <= 128 ? subject : subject[..128] + " [TRUNCATED]",
                    thresholdMs = 30000, completionConfirmed = false,
                    ownershipChanged = false
                }));
            }
            else if (_reportedPending)
            {
                _log.FlushSuppressed(_pendingKey);
                _log.Info(JsonSerializer.Serialize(new
                {
                    eventName = _component + ".progress.resumed", completed,
                    activeAtObservation = active, ownershipChanged = false
                }));
                _reportedPending = false;
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        lock (_outputSync)
        {
            if (_closed) return;
            Observe();
            _closed = true;
            _log.FlushSuppressed(_pendingKey);
            _states.Dispose();
        }
    }
}
