using System.Diagnostics;
using PinkieSysMon;

namespace PinkieSysMon.Editor;

// UI-thread-owned current-value cache. It never publishes to the local MetricStore
// or Graph history, and a missed Runtime refresh cannot keep a connection "alive".
internal sealed class EditorRuntimeMetricCache(Func<long>? timestamp = null)
{
    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(3);
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private RuntimeMetricSnapshot? _snapshot;
    private long _acceptedAt;

    internal void Accept(RuntimeMetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = new RuntimeMetricSnapshot(snapshot.InstanceId,
            new Dictionary<string, object?>(snapshot.Values, StringComparer.OrdinalIgnoreCase));
        _acceptedAt = _timestamp();
    }

    internal void Clear() => _snapshot = null;

    internal IReadOnlyDictionary<string, object?> Current()
    {
        if (_snapshot is null || Stopwatch.GetElapsedTime(_acceptedAt, _timestamp()) > MaximumAge)
            return Empty;
        return _snapshot.Values;
    }

    internal static IReadOnlyDictionary<string, object?> Compose(
        IReadOnlyDictionary<string, object?> editorLocal,
        IReadOnlyDictionary<string, object?> runtimeOwned,
        IReadOnlySet<string> required)
    {
        var merged = new Dictionary<string, object?>(editorLocal, StringComparer.OrdinalIgnoreCase);
        // No Editor-produced Output value, including any legacy illustrative sample,
        // may be interpreted as real device/manager telemetry.
        foreach (var id in merged.Keys.Where(id =>
                     id.StartsWith(OutputMetricContract.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            merged.Remove(id);

        foreach (var (id, value) in runtimeOwned)
        {
            if (required.Contains(id) && OutputMetricContract.DescribeKnownId(id) is not null)
                merged[id] = value;
        }
        return merged;
    }
}
