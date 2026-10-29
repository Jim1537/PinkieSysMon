using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

// Producers can own the Output manager gate: enqueue managed evidence only.
// The Runtime UI drains it after leaving application gates, and at shutdown.
internal sealed class OutputDiagnosticEvents(FileLogger log)
{
    internal const int Capacity = 128;
    private readonly object _sync = new();
    private readonly Queue<Entry> _events = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly string _id = Guid.NewGuid().ToString("N");
    private long _omitted;

    internal void Record(string phase, string subject, string detail)
    {
        lock (_sync)
        {
            if (_events.Count >= Capacity) { _omitted++; return; }
            _events.Enqueue(new(DateTimeOffset.Now, Stopwatch.GetTimestamp(), Bound(phase), Bound(subject), Bound(detail),
                RuntimeIpcTrace.Current?.Id.ToString("N")));
        }
    }

    internal void Flush()
    {
        Entry[] entries;
        long omitted;
        lock (_sync)
        {
            entries = _events.ToArray(); _events.Clear();
            omitted = _omitted; _omitted = 0;
        }
        foreach (var entry in entries)
            log.Info(JsonSerializer.Serialize(new
            {
                eventName = "runtime.output.observed", observerId = _id,
                observedAt = entry.At, elapsedMs = Stopwatch.GetElapsedTime(_started, entry.Tick).TotalMilliseconds,
                phase = entry.Phase, subject = entry.Subject, detail = entry.Detail,
                observedIpcRequestId = entry.RequestId
            }));
        if (omitted > 0) log.Info(JsonSerializer.Serialize(new
        { eventName = "runtime.output.observations.omitted", observerId = _id, omitted, capacity = Capacity }));
    }

    private static string Bound(string value) => value.Length <= 256 ? value : value[..256] + " [TRUNCATED]";
    private sealed record Entry(DateTimeOffset At, long Tick, string Phase, string Subject, string Detail, string? RequestId);
}
