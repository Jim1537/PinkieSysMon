using System.Diagnostics;

namespace PinkieSysMon;

// Optional source hook. Notifications are schedule-only; never perform I/O here.
internal interface IMetricDemandObserver
{
    void SetDemand(IReadOnlyCollection<string> metrics, bool active);
}

// The only Runtime filesystem scanner owner. Capture remains snapshot-only.
internal sealed class FilesystemTelemetrySource : IMetricSource, IMetricDemandObserver, IDisposable
{
    private readonly long _cooldownTicks;
    private int _scanAttempts;
    internal int ScanAttempts => Volatile.Read(ref _scanAttempts);
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly FileLogger _log;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Dictionary<string, FilesystemSourceConfig> _sources = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyCollection<string> _metricNames = [];
    private readonly HashSet<string> _demanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FilesystemScanResult?> _readings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _nextDue = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _activeScan;
    private string? _activeId;
    private Task? _worker;
    private long _generation;
    private bool _disposed;

    public FilesystemTelemetrySource(FilesystemConfig config, FileLogger log, TimeSpan? refreshInterval = null)
    {
        _log = log;
        var interval = refreshInterval ?? TimeSpan.FromMinutes(5);
        if (interval < TimeSpan.FromMilliseconds(50) || interval > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        _cooldownTicks = (long)Math.Ceiling(interval.TotalSeconds * Stopwatch.Frequency);
        ReplaceSources(config);
    }

    public string ProviderId => MetricProviderContract.Filesystem;
    public string Name => "filesystem";
    public int DefaultIntervalMs => 1000;
    public IReadOnlyCollection<string> MetricNames => Volatile.Read(ref _metricNames);

    public bool CanProvide(string metricName)
    {
        var id = FilesystemSourceContract.SourceIdForMetric(metricName);
        if (id is null) return false;
        lock (_sync) return _sources.ContainsKey(id);
    }

    // Called before building a new Runtime schedule. Metadata only; scan I/O stays on the worker.
    public void ReplaceSources(FilesystemConfig config)
    {
        var next = new FilesystemConfig { Sources = config.Sources.Select(source => source.Copy()).ToList() };
        FilesystemSourceContract.Validate(next);
        var index = next.Sources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var names = FilesystemSourceContract.DescriptorsFor(next.Sources).Select(descriptor => descriptor.Id).ToArray();
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FilesystemTelemetrySource));
            if (FilesystemSourceContract.SameSources(_sources.Values.OrderBy(s => s.Id),
                    index.Values.OrderBy(s => s.Id)))
                return;

            var activeChanged = _activeId is not null &&
                (!_sources.TryGetValue(_activeId, out var oldActive) ||
                 !index.TryGetValue(_activeId, out var newActive) ||
                 !string.Equals(oldActive.Path, newActive.Path, StringComparison.OrdinalIgnoreCase) ||
                 oldActive.Kind != newActive.Kind);
            foreach (var id in _sources.Keys)
                if (!index.TryGetValue(id, out var candidate) ||
                    !_sources.TryGetValue(id, out var previous) ||
                    !string.Equals(previous.Path, candidate.Path, StringComparison.OrdinalIgnoreCase) ||
                    previous.Kind != candidate.Kind)
                {
                    _readings.Remove(id);
                    _nextDue.Remove(id);
                }

            _sources = index;
            Volatile.Write(ref _metricNames, names);
            _generation++;
            _demanded.RemoveWhere(id => !_sources.ContainsKey(id));
            if (activeChanged)
                _activeScan?.Cancel();

            Wake();
        }
    }

    public void SetDemand(IReadOnlyCollection<string> metrics, bool active)
    {
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (active)
            foreach (var metric in metrics)
            {
                var id = FilesystemSourceContract.SourceIdForMetric(metric);
                if (id is not null) next.Add(id);
            }

        lock (_sync)
        {
            if (_disposed) return;
            next.RemoveWhere(id => !_sources.ContainsKey(id));
            if (!_demanded.SetEquals(next))
            {
                if (_activeId is not null && !next.Contains(_activeId))
                    _activeScan?.Cancel();
                foreach (var id in _demanded)
                    if (!next.Contains(id))
                    {
                        _readings.Remove(id);
                        _nextDue.Remove(id);
                    }
                _demanded.Clear();
                _demanded.UnionWith(next);
                _generation++;
            }
            if (_demanded.Count > 0 && _worker is null)
                _worker = Task.Run(RunAsync);
            Wake();
        }
    }

    public IReadOnlyDictionary<string, object?> Capture() => Capture(MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            foreach (var metric in requestedMetrics)
            {
                var id = FilesystemSourceContract.SourceIdForMetric(metric);
                object? value = null;
                if (id is not null && _demanded.Contains(id) &&
                    _readings.TryGetValue(id, out var reading) && reading?.Complete == true)
                {
                    var suffix = metric[(FilesystemSourceContract.Prefix.Length + id.Length + 1)..];
                    value = suffix.ToLowerInvariant() switch
                    {
                        FilesystemSourceContract.Size => reading.SizeBytes,
                        FilesystemSourceContract.Created => reading.Created,
                        FilesystemSourceContract.Modified => reading.Modified,
                        _ => null
                    };
                }
                snapshot[metric] = value;
            }
        }
        return snapshot;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                string? selectedId = null;
                FilesystemSourceConfig? selected = null;
                CancellationTokenSource? scan = null;
                var wait = TimeSpan.FromMinutes(5);
                long generation;
                lock (_sync)
                {
                    generation = _generation;
                    var now = Stopwatch.GetTimestamp();
                    foreach (var id in _demanded.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!_sources.TryGetValue(id, out var candidate)) continue;
                        if (!_nextDue.TryGetValue(id, out var due) || now >= due)
                        {
                            selectedId = id;
                            selected = candidate.Copy();
                            scan = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                            _activeScan = scan;
                            _activeId = id;
                            break;
                        }
                        var remaining = TimeSpan.FromSeconds((due - now) / (double)Stopwatch.Frequency);
                        if (remaining < wait) wait = remaining;
                    }
                }

                if (selectedId is null || selected is null || scan is null)
                {
                    await _wake.WaitAsync(wait, _shutdown.Token).ConfigureAwait(false);
                    continue;
                }

                FilesystemScanResult? result = null;
                try
                {
                    Interlocked.Increment(ref _scanAttempts);
                    result = FilesystemScanner.Scan(selected, scan.Token);
                }
                catch (Exception ex)
                {
                    _log.ErrorThrottled("filesystem.unexpected." + selectedId, FailureLogInterval,
                        "Filesystem scanner encountered an unexpected failure.", ex);
                }
                bool accepted;
                lock (_sync)
                {
                    _activeId = null;
                    _activeScan = null;
                    accepted = !_disposed && _generation == generation && _demanded.Contains(selectedId) &&
                        _sources.ContainsKey(selectedId);
                    if (accepted)
                    {
                        _readings[selectedId] = result?.Complete == true ? result : null;
                        _nextDue[selectedId] = Stopwatch.GetTimestamp() + _cooldownTicks;
                    }
                }
                scan.Dispose();
                if (accepted && result is { Complete: false })
                    _log.WarnThrottled("filesystem." + selectedId, FailureLogInterval,
                        "Filesystem scan unavailable (" + result.Status + "): " + result.Error);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Cancellation may wait for one outstanding native metadata call.
        }
        finally
        {
            _wake.Dispose();
            _shutdown.Dispose();
        }
    }

    private void Wake()
    {
        try { if (_wake.CurrentCount == 0) _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    public void Dispose()
    {
        Task? worker;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _demanded.Clear();
            _readings.Clear();
            _shutdown.Cancel();
            _activeScan?.Cancel();
            worker = _worker;
        }
        if (worker is null)
        {
            _wake.Dispose();
            _shutdown.Dispose();
        }
        else if (!worker.Wait(TimeSpan.FromSeconds(2)))
        {
            // Never start a replacement scanner and never publish after disposal.
            // The sole worker owns its primitives until the blocked OS call completes.
            _log.Warn("Filesystem scanner shutdown is waiting for an outstanding filesystem operation.");
        }
    }
}
