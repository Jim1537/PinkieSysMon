namespace PinkieSysMon;

internal sealed class MetricStore
{
    private readonly object _writeSync = new();
    private Dictionary<string, object?> _snapshot = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(IReadOnlyDictionary<string, object?> batch)
    {
        if (batch.Count == 0)
            return;

        lock (_writeSync)
        {
            var next = new Dictionary<string, object?>(_snapshot, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in batch)
                next[pair.Key] = pair.Value;
            Volatile.Write(ref _snapshot, next);
        }
    }

    public void Publish(string name, object? value)
    {
        lock (_writeSync)
        {
            var next = new Dictionary<string, object?>(_snapshot, StringComparer.OrdinalIgnoreCase)
            {
                [name] = value
            };
            Volatile.Write(ref _snapshot, next);
        }
    }

    // Published dictionaries are never mutated after publication, so readers can share
    // the current snapshot without a per-frame clone or a read lock.
    public IReadOnlyDictionary<string, object?> Snapshot() => Volatile.Read(ref _snapshot);
}
