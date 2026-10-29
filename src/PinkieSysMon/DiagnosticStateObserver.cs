using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

// Owns only diagnostic state. Never call under an application/session gate.
// Callers supply categorical states/counts, never sensor values or private paths.
internal sealed class DiagnosticStateObserver(FileLogger log, string component,
    Func<long>? timestamp = null) : IDisposable
{
    internal const int MaximumKeys = 64;
    private readonly object _sync = new();
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly string _observerId = Guid.NewGuid().ToString("N");
    private long _omitted;
    private bool _closed;

    internal void Observe(string key, string value, string? detail = null)
    {
        lock (_sync)
        {
            if (_closed) return;
            var now = _timestamp();
            if (!_states.TryGetValue(key, out var state))
            {
                if (_states.Count >= MaximumKeys) { _omitted++; return; }
                state = new(value, now);
                _states.Add(key, state);
                Write(key, state, "initial", detail);
                return;
            }
            if (state.Value != value)
            {
                Write(key, state, "transition.end", null);
                state = new(value, now);
                _states[key] = state;
                Write(key, state, "changed", detail);
            }
            else
            {
                state.Observations++;
                state.Last = now;
                if (Stopwatch.GetElapsedTime(state.Emitted, now) >= TimeSpan.FromSeconds(30))
                    Write(key, state, "summary", detail);
            }
        }
    }

    private void Write(string key, State state, string phase, string? detail)
    {
        log.Info(JsonSerializer.Serialize(new
        {
            eventName = component + ".state", observerId = _observerId,
            key = Bound(key), state = Bound(state.Value), phase,
            observations = state.Observations,
            observedDurationMs = Stopwatch.GetElapsedTime(state.First, state.Last).TotalMilliseconds,
            detail = detail is null ? null : Bound(detail)
        }));
        state.Emitted = state.Last;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            foreach (var (key, state) in _states) Write(key, state, "final", null);
            if (_omitted > 0) log.Info(JsonSerializer.Serialize(new
            {
                eventName = component + ".omitted", observerId = _observerId,
                observations = _omitted, maximumKeys = MaximumKeys
            }));
            _states.Clear();
        }
    }

    private static string Bound(string value) => value.Length <= 256
        ? value : value[..256] + " [TRUNCATED]";

    private sealed class State(string value, long now)
    {
        internal string Value = value;
        internal long First = now, Last = now, Emitted = now;
        internal long Observations = 1;
    }
}
