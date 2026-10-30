using System.Runtime.CompilerServices;
using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class GraphHistoryTests
{
    private static long T(double seconds) => (long)(seconds * TimeSpan.TicksPerSecond);
    private static MetricHistoryDemand Demand(string metric = "a", double window = 60, double? interval = 1,
        MetricDescriptor? descriptor = null, long owner = 0) => new(metric, window, interval, descriptor, owner);

    private static (MetricHistoryQuery Query, MetricHistorySample[] Samples) Read(
        MetricHistoryStore history, string metric = "a", double window = 60, long? now = null)
    {
        var buffer = new MetricHistorySample[history.SamplesPerMetric];
        var query = history.CopyTo(metric, window, now ?? history.GetTimestamp(), buffer);
        return (query, buffer[..query.Count]);
    }

    public static void PublicationsAndImmutableSnapshots()
    {
        long now = 0;
        var history = new MetricHistoryStore(() => now);
        var store = new MetricStore(history);
        store.Publish("a", 7);
        Check(history.SeriesCount == 0 && history.ReservedBytes == 0, "Zero consumers must allocate no series.");
        now = T(5);
        history.SetDemand([Demand()]);
        Check(Read(history).Query.Count == 0, "Demand must not seed history from an old current-value snapshot.");
        store.Publish(new Dictionary<string, object?> { ["A"] = 10, ["b"] = 20 });
        var snapshot = store.Snapshot();
        var first = Read(history);
        now = T(6);
        store.Publish("a", 10);
        for (var i = 0; i < 100; i++) { _ = store.Snapshot(); _ = Read(history); }
        var second = Read(history);
        Check(history.SeriesCount == 1 && Read(history, "b").Query.Count == 0, "Undemanded publication must allocate no series.");
        Check(second.Query.Count == 2 && second.Samples.All(s => s.Number == 10), "Equal real publications must remain distinct; reads append nothing.");
        Check(second.Samples[0].Timestamp == T(5) && second.Samples[1].Timestamp == T(6), "X must use publication time.");
        Check(first.Samples.Length == 1 && first.Samples[0] == second.Samples[0], "A reader-owned result must stay unchanged.");
        Check(!ReferenceEquals(snapshot, store.Snapshot()) && snapshot["a"] is int old && old == 10 && snapshot["b"] is int b && b == 20,
            "Adding history must preserve MetricStore immutable snapshot ownership.");
        now = T(7);
        store.Publish("b", 30);
        Check(Read(history).Query.Count == 2, "A publication of another key must not republish the merged snapshot.");
        Check(snapshot["b"] is int earlier && earlier == 20 && store.Snapshot()["b"] is int current && current == 30,
            "Publishing a changed value must not mutate an earlier snapshot.");
        history.SetDemand([Demand(), Demand("b")]);
        store.Publish(new Dictionary<string, object?> { ["a"] = 11, ["b"] = 31 });
        Check(Read(history).Samples[^1].Timestamp == Read(history, "b").Samples[^1].Timestamp,
            "Every changed key in one batch must receive the same timestamp.");
        store.Publish("a", 12);
        var tied = Read(history).Samples;
        Check(tied[^2].Timestamp == tied[^1].Timestamp && tied[^2].Sequence < tied[^1].Sequence,
            "Equal timestamps retain distinct ordered samples without fictional spacing.");
    }

    public static void DemandPrepareCommitAndRelease()
    {
        long now = 0;
        var history = new MetricHistoryStore(() => now);
        var store = new MetricStore(history);
        history.SetDemand([Demand(window: 10), Demand("A", 60)]);
        store.Publish("a", 1);
        Check(history.SeriesCount == 1 && Read(history).Query.Count == 1, "Overlapping consumers must share one source buffer.");
        using (var abandoned = history.PrepareDemand([Demand("b")]))
        {
            Check(history.SeriesCount == 1 && Read(history).Query.Count == 1, "Preparation must not adopt uncommitted demand.");
            store.Publish("b", 2);
            Check(Read(history, "b").Query.Count == 0, "Prepared series cannot admit publications before commit.");
            Throws<InvalidOperationException>(() => history.PrepareDemand([Demand("c")]));
        }
        Check(Read(history).Query.Count == 1, "Renderer preparation failure/disposal must retain committed history.");
        var oldEpoch = Read(history).Query.Epoch;
        now = T(30);
        store.Publish("a", 30);
        history.SetDemand([Demand(window: 10)]);
        Check(Read(history, window: 10).Query.Epoch == oldEpoch && history.SeriesCount == 1,
            "Changing only a window must reconcile retention while keeping the source epoch.");
        Throws<InvalidOperationException>(() => Read(history, window: 60));
        now = T(31);
        store.Publish("a", 31);
        history.SetDemand([Demand()]);
        Check(Read(history).Query.Epoch == oldEpoch, "Growing retention does not replace an unchanged measurement owner.");
        history.SetDemand([]);
        Check(history.SeriesCount == 0 && history.ReservedBytes == 0, "The final consumer must release its buffer and reservation.");
        history.SetDemand([Demand()]);
        Check(Read(history).Query.Count == 0 && Read(history).Query.Epoch != oldEpoch,
            "Recreated demand must start empty with a new epoch.");
        using var committed = history.PrepareDemand([Demand("b")]);
        committed.Commit();
        Throws<InvalidOperationException>(committed.Commit);
    }

    public static void GapsCadenceAndPauseBoundaries()
    {
        long now = 0;
        var history = new MetricHistoryStore(() => now);
        var store = new MetricStore(history);
        history.SetDemand([Demand()]);
        store.Publish("a", 10);
        now = T(2); store.Publish("a", 20);
        now = T(4) + 1; store.Publish("a", 30);
        var samples = Read(history).Samples;
        Check(!samples[0].ConnectsToPrevious && samples[1].ConnectsToPrevious && !samples[2].ConnectsToPrevious,
            "The inclusive 2I cadence boundary must not bridge a longer silence.");
        history.BreakContinuity();
        now = T(5); store.Publish("a", 40);
        Check(!Read(history).Samples[^1].ConnectsToPrevious, "A pause must split a short otherwise-continuous interval.");
        foreach (var invalid in new object?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity, "12", new object(), true })
        {
            now += T(1); store.Publish("a", invalid);
            now += T(1); store.Publish("a", 50);
            samples = Read(history).Samples;
            Check(samples[^2].Kind == MetricHistoryValueKind.Gap && !samples[^1].ConnectsToPrevious,
                "Unavailable, nonfinite and unsupported publications must produce gaps, never zero or a bridge.");
        }
        var before = Read(history).Query.Count;
        now += T(5);
        Check(Read(history).Query.Count == before, "Silence must append no fabricated points.");
        history.SetDemand([Demand(interval: null)]);
        store.Publish("a", 1); now += T(1); store.Publish("a", 2);
        Check(Read(history).Samples.All(s => !s.ConnectsToPrevious), "Unknown cadence must retain isolated observations.");
        history.SetDemand([Demand(interval: 2)]);
        Check(Read(history).Query.Count == 0, "A cadence-policy change must start a new epoch.");
    }

    public static void WindowPredecessorAndCapacity()
    {
        long now = T(1);
        var history = new MetricHistoryStore(() => now, samplesPerMetric: 3);
        var store = new MetricStore(history);
        history.SetDemand([Demand(window: 10, interval: 2)]);
        store.Publish("a", 100);
        now = T(3); store.Publish("a", 20);
        now = T(4); store.Publish("a", 30);
        now = T(12);
        var query = Read(history, window: 10);
        Check(query.Query.Count == 3 && query.Samples[0].Timestamp < query.Query.WindowStart &&
            query.Samples.Skip(1).All(s => s.Timestamp >= query.Query.WindowStart),
            "A query may return exactly one boundary predecessor before visible samples.");
        now = T(15);
        Check(Read(history, window: 10).Query.Count == 0, "An expired predecessor alone must never form a series.");
        history.SetDemand([]); history.SetDemand([Demand(window: 10)]);
        for (var i = 0; i < 4; i++) { now = T(20 + i); store.Publish("a", i); }
        query = Read(history, window: 10);
        Check(query.Query.Count == 3 && query.Query.Truncated && query.Samples.Select(s => s.Number).SequenceEqual(new double[] { 1, 2, 3 }),
            "Count pressure must evict chronologically and report truncation without stretching time.");
        var shortQuery = Read(history, window: 1);
        Check(!shortQuery.Query.Truncated, "An eviction outside a shorter visible window must not report that window as truncated.");
        Throws<ArgumentException>(() => history.CopyTo("a", 10, now, new MetricHistorySample[1]));
        var earlierFrame = Read(history, window: 10, now: T(21));
        Check(earlierFrame.Samples.All(s => s.Timestamp <= T(21)), "An earlier frame must not include a later concurrent publication.");
        now = T(100); store.Publish("a", null);
        now = T(101); store.Publish("a", 101);
        query = Read(history, window: 10);
        Check(query.Samples.Length <= 3 && query.Samples.Count(s => s.Timestamp < query.Query.WindowStart) <= 1,
            "Time pruning must retain at most one expired record including gap metadata.");
        Check(query.Samples[^2].Kind == MetricHistoryValueKind.Gap && !query.Samples[^1].ConnectsToPrevious,
            "The left boundary must not discard a gap and connect to an older valid sample.");
    }

    public static void RawTemporalValuesAndSemanticEpochs()
    {
        long now = 0;
        var descriptor = new MetricDescriptor("a", MetricValueKind.DateTime, MetricUnit.None);
        var history = new MetricHistoryStore(() => now);
        var store = new MetricStore(history);
        history.SetDemand([Demand(descriptor: descriptor)]);
        var date = new DateTime(2024, 2, 29, 12, 34, 56, 789, DateTimeKind.Utc);
        var offset = new DateTimeOffset(2024, 2, 29, 13, 34, 56, TimeSpan.FromHours(1));
        store.Publish("a", date);
        now = T(1); store.Publish("a", offset);
        var query = Read(history);
        Check(query.Samples[0].ToReading() is DateTime stored && stored == date && stored.Kind == date.Kind &&
              query.Samples[1].ToReading() is DateTimeOffset storedOffset && storedOffset.EqualsExact(offset),
            "History must preserve the published civil time and offset, independently of frame/wall-clock changes.");
        Check(DateTimeComponentContract.TryProject(query.Samples[0].ToReading(), "hour24", out var hour) && hour == 12 &&
              DateTimeComponentContract.TryProject(query.Samples[0].ToReading(), "second", "0.00", out var second) && second == 56.78,
            "Two consumers must project the same raw temporal observation independently with existing component precision.");
        var epoch = query.Query.Epoch;
        history.SetDemand([Demand(descriptor: new MetricDescriptor("A", MetricValueKind.DateTime, MetricUnit.None))]);
        Check(Read(history).Query.Epoch == epoch && Read(history).Query.Count == 2, "An identical catalog refresh must retain history.");
        descriptor = new("a", MetricValueKind.Number, MetricUnit.Celsius);
        history.SetDemand([Demand(descriptor: descriptor)]);
        Check(Read(history).Query.Count == 0 && Read(history).Query.Epoch != epoch, "A kind/unit change cannot reinterpret old observations.");
        store.Publish("a", 120d);
        query = Read(history);
        Check(query.Query.Descriptor == descriptor && query.Samples[0].ToReading() is double n && n == 120,
            "Queries must carry the pinned semantic descriptor with raw scalar data.");
        descriptor = new("a", MetricValueKind.Duration, MetricUnit.Seconds);
        history.SetDemand([Demand(descriptor: descriptor)]); store.Publish("a", 120d);
        var raw = Read(history).Samples[0].ToReading();
        Check(MetricValueConverter.TryConvertNumeric(raw, descriptor, "seconds", out var seconds) && seconds == 120 &&
              MetricValueConverter.TryConvertNumeric(raw, descriptor, "minutes", out var minutes) && minutes == 2,
            "Units must be projected from one source series using the existing converter.");
        epoch = Read(history).Query.Epoch;
        history.SetDemand([Demand(descriptor: descriptor, owner: 1)]);
        Check(Read(history).Query.Count == 0 && Read(history).Query.Epoch != epoch, "Reusing a logical owner must invalidate prior history.");
        store.Publish("a", date);
        Check(Read(history).Samples[0].Kind == MetricHistoryValueKind.Gap, "A temporal payload cannot enter a numeric semantic epoch.");
    }

    public static void BudgetValidationAndAllocationBounds()
    {
        long now = 0;
        var history = new MetricHistoryStore(() => now, samplesPerMetric: 3, storageBudgetBytes: 2048);
        history.SetDemand([Demand()]);
        var reserved = history.ReservedBytes;
        Check(reserved >= 3L * Unsafe.SizeOf<MetricHistorySample>() && reserved <= 2048, "Reservation must charge actual element size and ownership overhead.");
        Throws<InvalidDataException>(() => history.SetDemand([Demand(), Demand("b")]));
        Check(history.SeriesCount == 1 && history.ReservedBytes == reserved, "Capacity failure must preserve active demand without silently omitting consumers.");
        foreach (var invalid in new[] { 0d, -1, 601, double.NaN, double.PositiveInfinity })
            Throws<InvalidDataException>(() => history.SetDemand([Demand(window: invalid)]));
        foreach (var invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            Throws<InvalidDataException>(() => history.SetDemand([Demand(interval: invalid)]));
        Throws<InvalidDataException>(() => history.SetDemand([Demand(), Demand(interval: 2)]));
        Throws<InvalidDataException>(() => history.SetDemand([Demand(" ")]));
        Throws<InvalidDataException>(() => history.SetDemand([Demand(new string('x', 1025))]));
        Throws<InvalidDataException>(() => history.SetDemand([Demand(descriptor: new("b", MetricValueKind.Number, MetricUnit.None))]));
        Throws<ArgumentOutOfRangeException>(() => new MetricHistoryStore(samplesPerMetric: 1));
        Throws<ArgumentOutOfRangeException>(() => new MetricHistoryStore(storageBudgetBytes: 0));
        object reading = 42d;
        var destination = new MetricHistorySample[3];
        for (var i = 0; i < 1000; i++) { history.Record("a", reading); _ = history.CopyTo("a", 60, now, destination); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) { now++; history.Record("a", reading); _ = history.CopyTo("a", 60, now, destination); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Admitting and copying fixed scalar records must allocate zero managed bytes after warmup; observed {allocated}.");
        Check(history.ReservedBytes == reserved && Read(history).Query.Count == 3, "Sustained pressure cannot grow storage or gap metadata.");
    }

    public static void ConcurrentWritersReadersAndRetirement()
    {
        long now = 0;
        var history = new MetricHistoryStore(() => Interlocked.Increment(ref now), samplesPerMetric: 64);
        var store = new MetricStore(history);
        history.SetDemand([Demand(), Demand("b")]);
        using var barrier = new Barrier(3);
        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < 1000; i++) store.Publish(new Dictionary<string, object?> { ["a"] = i, ["b"] = i });
        });
        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            var buffer = new MetricHistorySample[64];
            for (var i = 0; i < 1000; i++)
            {
                var query = history.CopyTo("a", 60, history.GetTimestamp(), buffer);
                for (var j = 1; j < query.Count; j++)
                    Check(buffer[j - 1].Timestamp <= buffer[j].Timestamp && buffer[j - 1].Sequence < buffer[j].Sequence,
                        "Concurrent readers must see ordered coherent records.");
            }
        });
        barrier.SignalAndWait();
        for (var i = 0; i < 100; i++)
        {
            history.SetDemand([Demand("b")]);
            history.SetDemand([Demand(), Demand("b")]);
        }
        Check(Task.WaitAll([writer, reader], TimeSpan.FromSeconds(20)), "Concurrent publication/read/reconciliation must complete without deadlock.");
        var old = Read(history, "b");
        history.SetDemand([]);
        history.SetDemand([Demand("b")]);
        store.Publish("b", 2000);
        Check(old.Samples.Length > 0 && old.Samples.All(s => s.Number < 2000) && Read(history, "b").Query.Count == 1,
            "Retiring a buffer cannot mutate borrowed results or resurrect it in a new epoch.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
