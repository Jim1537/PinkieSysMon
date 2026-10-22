using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PinkieSysMon;

// Configuration belongs to the process's committed Graph demand, not to a renderer.
// Descriptor and OwnerGeneration pin the meaning of a metric across catalog/Output reuse.
internal sealed record MetricHistoryDemand(
    string Metric, double WindowSeconds, double? IntervalSeconds = null,
    MetricDescriptor? Descriptor = null, long OwnerGeneration = 0);

internal enum MetricHistoryValueKind : byte { Gap, Number, DateTime, DateTimeOffset }

// Fixed-size value storage: never retain provider-owned objects, strings or mutable payloads.
internal readonly record struct MetricHistorySample(
    long Timestamp, long Sequence, double Number, long TemporalValue, short OffsetMinutes,
    MetricHistoryValueKind Kind, bool ConnectsToPrevious)
{
    internal static MetricHistorySample Create(long timestamp, long sequence, object? value, bool connects)
    {
        if (value is DateTime date)
            return new(timestamp, sequence, 0, date.ToBinary(), 0, MetricHistoryValueKind.DateTime, connects);
        if (value is DateTimeOffset offset)
            return new(timestamp, sequence, 0, offset.Ticks, (short)offset.Offset.TotalMinutes,
                MetricHistoryValueKind.DateTimeOffset, connects);
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsFinite(number))
                return new(timestamp, sequence, number, 0, 0, MetricHistoryValueKind.Number, connects);
        }
        return new(timestamp, sequence, 0, 0, 0, MetricHistoryValueKind.Gap, false);
    }

    public object? ToReading() => Kind switch
    {
        MetricHistoryValueKind.Number => Number,
        MetricHistoryValueKind.DateTime => DateTime.FromBinary(TemporalValue),
        MetricHistoryValueKind.DateTimeOffset => new DateTimeOffset(TemporalValue, TimeSpan.FromMinutes(OffsetMinutes)),
        _ => null
    };
}

internal readonly record struct MetricHistoryQuery(
    int Count, long Now, long WindowStart, MetricDescriptor? Descriptor,
    bool Truncated, long Epoch);

/// <summary>
/// Bounded, publication-driven raw history. No provider calls, render callbacks or timer.
/// MetricStore serializes publishers; this service never acquires a MetricStore/session lock.
/// Copy/query work holds only this service's gate; conversion and drawing happen afterwards.
/// </summary>
internal sealed class MetricHistoryStore
{
    public const int DefaultSamplesPerMetric = 8192;
    public const long DefaultStorageBudgetBytes = 32L * 1024 * 1024;
    public const double MaximumWindowSeconds = 600;
    // Conservative reservation for Series, identity, dictionary entry and collection headers.
    // Sample arrays are charged at their actual managed element size, not scalar payload size.
    private const int SeriesOverheadBytes = 1024;
    private const int MaximumMetricIdLength = 1024;
    private readonly object _sync = new();
    private readonly Func<long> _clock;
    private readonly int _capacity;
    private readonly long _budget;
    private Dictionary<string, Series> _series = new(StringComparer.OrdinalIgnoreCase);
    private long _generation;
    private long _nextEpoch;
    private long _sequence;
    private long _lastTimestamp;
    private long _reservedBytes;
    private bool _hasPreparedDemand;

    public MetricHistoryStore(Func<long>? clock = null, int samplesPerMetric = DefaultSamplesPerMetric,
        long storageBudgetBytes = DefaultStorageBudgetBytes)
    {
        if (samplesPerMetric is < 2 or > DefaultSamplesPerMetric)
            throw new ArgumentOutOfRangeException(nameof(samplesPerMetric));
        if (storageBudgetBytes <= 0 || storageBudgetBytes > DefaultStorageBudgetBytes)
            throw new ArgumentOutOfRangeException(nameof(storageBudgetBytes));
        _capacity = samplesPerMetric;
        _budget = storageBudgetBytes;
        var origin = Stopwatch.GetTimestamp();
        _clock = clock ?? (() => Stopwatch.GetElapsedTime(origin).Ticks);
    }

    public int SamplesPerMetric => _capacity;
    public int SeriesCount { get { lock (_sync) return _series.Count; } }
    public long ReservedBytes { get { lock (_sync) return _reservedBytes; } }
    public long GetTimestamp() => _clock();

    // Duplicate consumers are unioned by Metric ID; only the longest window reserves storage.
    // Conflicting cadence/meaning is rejected rather than mixing two owners in one series.
    public PreparedDemand PrepareDemand(IEnumerable<MetricHistoryDemand> demands)
    {
        ArgumentNullException.ThrowIfNull(demands);
        var requested = new Dictionary<string, MetricHistoryDemand>(StringComparer.OrdinalIgnoreCase);
        long reservation = 0;
        foreach (var demand in demands)
        {
            Validate(demand);
            var normalized = demand with { Metric = demand.Metric.Trim() };
            if (requested.TryGetValue(normalized.Metric, out var previous))
            {
                if (!SameIdentity(previous, normalized))
                    throw new InvalidDataException($"Conflicting history owners/cadences for '{normalized.Metric}'.");
                requested[normalized.Metric] = previous with { WindowSeconds = Math.Max(previous.WindowSeconds, normalized.WindowSeconds) };
                continue;
            }
            reservation = checked(reservation + ReservationFor(normalized.Metric));
            if (reservation > _budget)
                throw new InvalidDataException($"Graph history demand exceeds the {_budget}-byte storage budget.");
            requested.Add(normalized.Metric, normalized);
        }

        Dictionary<string, Series> current;
        long generation;
        lock (_sync)
        {
            if (_hasPreparedDemand)
                throw new InvalidOperationException("A history demand plan is already prepared; commit or dispose it first.");
            generation = _generation;
            current = new(_series, StringComparer.OrdinalIgnoreCase);
            _hasPreparedDemand = true;
        }
        try
        {
            var next = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var (metric, demand) in requested)
                next.Add(metric, current.TryGetValue(metric, out var existing) && SameIdentity(existing.Demand, demand)
                    ? existing : new Series(demand, _capacity));
            return new PreparedDemand(this, generation, requested, next, reservation);
        }
        catch
        {
            lock (_sync) _hasPreparedDemand = false;
            throw;
        }
    }

    public void SetDemand(IEnumerable<MetricHistoryDemand> demands)
    {
        using var prepared = PrepareDemand(demands);
        prepared.Commit();
    }

    private long ReservationFor(string metric) =>
        checked((long)_capacity * Unsafe.SizeOf<MetricHistorySample>() + SeriesOverheadBytes + 4L * metric.Length);

    private static void Validate(MetricHistoryDemand demand)
    {
        ArgumentNullException.ThrowIfNull(demand);
        if (string.IsNullOrWhiteSpace(demand.Metric) || demand.Metric.Length > MaximumMetricIdLength)
            throw new InvalidDataException("Graph history requires a nonempty Metric ID of at most 1024 characters.");
        if (!double.IsFinite(demand.WindowSeconds) || demand.WindowSeconds is < 1 or > MaximumWindowSeconds)
            throw new InvalidDataException("Graph history window must be between 1 and 600 seconds.");
        if (demand.IntervalSeconds is { } interval && (!double.IsFinite(interval) || interval <= 0 || interval > TimeSpan.MaxValue.TotalSeconds / 2))
            throw new InvalidDataException("Graph history cadence must be positive and finite, or unknown.");
        if (demand.Descriptor is { } descriptor &&
            !string.Equals(descriptor.Id, demand.Metric.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Graph history descriptor must describe its demanded Metric ID.");
    }

    private static bool SameIdentity(MetricHistoryDemand a, MetricHistoryDemand b) =>
        a.IntervalSeconds == b.IntervalSeconds && a.OwnerGeneration == b.OwnerGeneration &&
        a.Descriptor?.ValueKind == b.Descriptor?.ValueKind && a.Descriptor?.BaseUnit == b.Descriptor?.BaseUnit;

    // Called only at MetricStore's publication boundary, with the actual changed keys.
    internal void Record(IReadOnlyDictionary<string, object?> batch)
    {
        lock (_sync)
        {
            if (_series.Count == 0 || batch.Count == 0)
                return;
            var now = ReadClockLocked();
            foreach (var (metric, value) in batch)
                if (_series.TryGetValue(metric, out var series))
                    Append(series, value, now);
        }
    }

    internal void Record(string metric, object? value)
    {
        lock (_sync)
        {
            if (_series.TryGetValue(metric, out var series))
                Append(series, value, ReadClockLocked());
        }
    }

    private long ReadClockLocked() => _lastTimestamp = Math.Max(_lastTimestamp, _clock());

    private void Append(Series series, object? value, long now)
    {
        var previous = series.Count > 0 ? series.At(series.Count - 1) : default;
        var connects = !series.BreakBeforeNext && series.Count > 0 && previous.Kind != MetricHistoryValueKind.Gap &&
            series.Demand.IntervalSeconds is { } cadence &&
            (now - previous.Timestamp) / (double)TimeSpan.TicksPerSecond <= 2 * cadence;
        var sample = MetricHistorySample.Create(now, ++_sequence, value, connects);
        var kind = series.Demand.Descriptor?.ValueKind;
        if ((kind == MetricValueKind.DateTime && sample.Kind is not (MetricHistoryValueKind.DateTime or MetricHistoryValueKind.DateTimeOffset)) ||
            (kind is not null && kind != MetricValueKind.DateTime && sample.Kind != MetricHistoryValueKind.Number) ||
            kind is MetricValueKind.Boolean or MetricValueKind.Text)
            sample = sample with { Kind = MetricHistoryValueKind.Gap, ConnectsToPrevious = false };
        series.Prune(now);
        if (series.Count == _capacity)
        {
            series.LastEvictedTimestamp = series.At(0).Timestamp;
            series.RemoveFirst();
        }
        series.Put(sample);
        series.BreakBeforeNext = sample.Kind == MetricHistoryValueKind.Gap;
    }

    // Lifecycle boundaries are metadata; never fabricate a measurement or a timestamped gap.
    public void BreakContinuity()
    {
        lock (_sync)
            foreach (var series in _series.Values)
                series.BreakBeforeNext = true;
    }

    // Caller owns/reuses destination. The returned frame is consistent under one short lock.
    // A boundary predecessor is copied only if an in-window observation also exists.
    public MetricHistoryQuery CopyTo(string metric, double windowSeconds, long now, Span<MetricHistorySample> destination)
    {
        if (!double.IsFinite(windowSeconds) || windowSeconds is < 1 or > MaximumWindowSeconds)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        var start = now - (long)(windowSeconds * TimeSpan.TicksPerSecond);
        lock (_sync)
        {
            if (!_series.TryGetValue(metric, out var series))
                return new(0, now, start, null, false, 0);
            if (windowSeconds > series.Demand.WindowSeconds)
                throw new InvalidOperationException("Query window exceeds committed history demand.");
            var first = 0;
            while (first < series.Count && series.At(first).Timestamp < start) first++;
            var end = first;
            while (end < series.Count && series.At(end).Timestamp <= now) end++;
            if (end > first && first > 0) first--;
            var count = end - first;
            if (destination.Length < count)
                throw new ArgumentException("History query destination is too small.", nameof(destination));
            for (var i = 0; i < count; i++) destination[i] = series.At(first + i);
            return new(count, now, start, series.Demand.Descriptor,
                series.LastEvictedTimestamp is { } evicted && evicted >= start && evicted <= now, series.Epoch);
        }
    }

    internal sealed class PreparedDemand : IDisposable
    {
        private MetricHistoryStore? _owner;
        private readonly long _generation;
        private Dictionary<string, MetricHistoryDemand>? _requested;
        private Dictionary<string, Series>? _next;
        private readonly long _reservation;

        internal PreparedDemand(MetricHistoryStore owner, long generation,
            Dictionary<string, MetricHistoryDemand> requested, Dictionary<string, Series> next, long reservation)
        {
            _owner = owner; _generation = generation; _requested = requested; _next = next; _reservation = reservation;
        }

        public void Commit()
        {
            var owner = _owner ?? throw new InvalidOperationException("History demand plan is already consumed.");
            lock (owner._sync)
            {
                if (owner._generation != _generation)
                    throw new InvalidOperationException("History demand changed after preparation; prepare again.");
                var now = owner.ReadClockLocked();
                foreach (var (metric, series) in _next!)
                {
                    if (series.Epoch == 0) series.Epoch = ++owner._nextEpoch;
                    series.Demand = _requested![metric];
                    series.Prune(now);
                }
                owner._series = _next!;
                owner._reservedBytes = _reservation;
                owner._generation++;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (_owner is { } owner)
                lock (owner._sync) owner._hasPreparedDemand = false;
            _owner = null; _requested = null; _next = null;
        }
    }

    internal sealed class Series
    {
        private readonly MetricHistorySample[] _samples;
        private int _head;
        public int Count { get; private set; }
        public MetricHistoryDemand Demand { get; set; }
        public long Epoch { get; set; }
        public bool BreakBeforeNext { get; set; } = true;
        public long? LastEvictedTimestamp { get; set; }

        public Series(MetricHistoryDemand demand, int capacity) { Demand = demand; _samples = new MetricHistorySample[capacity]; }
        public MetricHistorySample At(int index) => _samples[(_head + index) % _samples.Length];
        public void RemoveFirst() { _head = (_head + 1) % _samples.Length; Count--; }
        public void Put(MetricHistorySample sample) { _samples[(_head + Count) % _samples.Length] = sample; Count++; }
        public void Prune(long now)
        {
            var cutoff = now - (long)(Demand.WindowSeconds * TimeSpan.TicksPerSecond);
            // Keep at most one expired predecessor, including a gap if it is the nearest record.
            while (Count > 1 && At(1).Timestamp < cutoff) RemoveFirst();
        }
    }
}
