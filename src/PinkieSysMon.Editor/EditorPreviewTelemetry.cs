using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

internal sealed record EditorPreviewCatalog(
    IReadOnlyList<LibreHardwareMonitorMetricEntry> Lhm,
    IReadOnlyList<IcueSensorLogMetricEntry> Icue,
    IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> Endpoints,
    bool LhmAvailable, bool IcueAvailable)
{
    public static EditorPreviewCatalog Empty { get; } = new([], [], [], false, false);
}

// One process-local owner for capture, discovery, source settings and terminal disposal.
// Native work is queued on the existing TelemetryEngine worker, never the UI/render timer.
internal sealed class EditorPreviewTelemetry
{
    private readonly IMetricSource[] _sources;
    private readonly TelemetryConfig _config;
    private readonly TelemetryEngine _engine;
    private readonly Func<IReadOnlySet<string>, EditorPreviewCatalog> _discover;
    private readonly Action<IReadOnlyDictionary<string, string>> _applyMedia;
    private readonly Func<IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>> _readEndpoints;
    private readonly FileLogger _log;
    private readonly object _sync = new();
    private HashSet<string> _providers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _media = new(StringComparer.OrdinalIgnoreCase);
    private EditorPreviewCatalog _catalog = EditorPreviewCatalog.Empty;
    private Task<EditorPreviewCatalog>? _catalogTask;
    private Task? _endpointTask;
    private Task? _stopTask;
    private long _generation;
    private long _catalogGeneration = -1;
    private bool _configured;
    private bool _closing;

    public MetricHistoryStore History { get; } = new();
    public MetricStore Store { get; }
    public EditorPreviewCatalog Catalog => Volatile.Read(ref _catalog);

    public EditorPreviewTelemetry(IMetricSource[] sources, TelemetryConfig config, FileLogger log,
        Func<IReadOnlySet<string>, EditorPreviewCatalog> discover,
        Action<IReadOnlyDictionary<string, string>> applyMedia,
        Func<IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>>? readEndpoints = null)
    {
        _sources = sources; _config = config; _log = log; _discover = discover; _applyMedia = applyMedia;
        _readEndpoints = readEndpoints ?? (() => []);
        Store = new(History);
        _engine = new(Store, sources, config, [], [], log);
        _engine.Start();
    }

    // Called on the UI thread after renderer preparation, with copied committed settings.
    public void Configure(AppConfig app, C.DashboardDefinition? definition)
    {
        if (_closing) return;
        var providers = MetricProviderContract.GetEnabledProviderIds(app.MetricProviders).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = definition is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : DashboardMetricUsage.Collect(definition).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var media = new Dictionary<string, string>(app.Media.EndpointTypeOverrides, StringComparer.OrdinalIgnoreCase);
        var mediaChanged = _media.Count != media.Count || media.Any(p => !_media.TryGetValue(p.Key, out var value) || value != p.Value);
        var semanticChange = !_providers.SetEquals(providers) || mediaChanged;
        var generation = _generation + (semanticChange ? 1 : 0);
        var demands = definition is null ? [] : DashboardMetricUsage.CollectHistory(definition)
            .Select(d => d with { OwnerGeneration = generation }).ToArray();
        var scheduleChanged = !_configured || !_providers.SetEquals(providers) || !_required.SetEquals(required) || mediaChanged;
        try
        {
            if (scheduleChanged) _engine.Reconfigure(_config, providers, required);
            _engine.SetHistoryDemand(demands);
        }
        catch
        {
            _engine.Reconfigure(_config, _providers, _required);
            throw;
        }
        _providers = providers; _required = required; _configured = true;
        Volatile.Write(ref _generation, generation);
        if (mediaChanged)
        {
            _media = media;
            Observe(_engine.RunSourceOperationAsync(() =>
            {
                if (!_closing && Volatile.Read(ref _generation) == generation) _applyMedia(media);
                return true;
            }));
        }
    }

    public void SetPaused(bool paused) { if (!_closing) _engine.SetIdlePaused(paused); }

    public Task<EditorPreviewCatalog> RefreshCatalogAsync()
    {
        lock (_sync)
        {
            if (_closing) return Task.FromResult(Catalog);
            var generation = Volatile.Read(ref _generation);
            if (_catalogTask is { IsCompleted: false } && _catalogGeneration == generation) return _catalogTask;
            var providers = new HashSet<string>(_providers, StringComparer.OrdinalIgnoreCase);
            _catalogGeneration = generation;
            _catalogTask = _engine.RunSourceOperationAsync(() =>
            {
                if (_closing || Volatile.Read(ref _generation) != generation) return Catalog;
                var snapshot = _discover(providers);
                if (!_closing && Volatile.Read(ref _generation) == generation) Volatile.Write(ref _catalog, snapshot);
                return Catalog;
            });
            return _catalogTask;
        }
    }

    public Task RefreshMediaEndpointsAsync()
    {
        lock (_sync)
        {
            if (_closing) return Task.CompletedTask;
            if (_endpointTask is { IsCompleted: false }) return _endpointTask;
            var generation = Volatile.Read(ref _generation);
            var enabled = _providers.Contains(MetricProviderContract.System);
            _endpointTask = _engine.RunSourceOperationAsync(() =>
            {
                if (_closing || Volatile.Read(ref _generation) != generation) return false;
                var endpoints = enabled ? _readEndpoints() : [];
                if (!_closing && Volatile.Read(ref _generation) == generation)
                    Volatile.Write(ref _catalog, Catalog with { Endpoints = endpoints });
                return true;
            });
            return _endpointTask;
        }
    }

    private async void Observe(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception error) { _log.Warn("Editor preview source settings operation failed.", error); }
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_stopTask is not null) return _stopTask;
            _closing = true;
            Interlocked.Increment(ref _generation);
            _engine.SetIdlePaused(true);
            _stopTask = Task.Run(() =>
            {
                using var cleanup = new CleanupOperation(_log, "editor.preview.shutdown");
                // Wait for terminal capture/discovery before disposing any source.
                cleanup.Step("worker.drain", _engine.Dispose);
                cleanup.Step("sources.dispose", () =>
                {
                    var failures = new List<Exception>();
                    foreach (var source in _sources.OfType<IDisposable>())
                        try { source.Dispose(); } catch (Exception error) { failures.Add(error); }
                    History.SetDemand([]);
                    if (failures.Count > 0) throw new AggregateException("Editor preview source cleanup failed.", failures);
                });
                cleanup.Complete();
            });
            return _stopTask;
        }
    }
}
