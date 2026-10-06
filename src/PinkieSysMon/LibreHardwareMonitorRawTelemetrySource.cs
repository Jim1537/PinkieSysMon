namespace PinkieSysMon;

internal sealed record LibreHardwareMonitorHardwarePathSegment(
    string HardwareId,
    string HardwareName);

internal sealed record LibreHardwareMonitorMetricEntry(
    string MetricId,
    string SensorId,
    string SensorType,
    string? SensorName,
    string? HardwareId,
    string? HardwareName,
    string? SensorGroupName,
    IReadOnlyList<LibreHardwareMonitorHardwarePathSegment> HardwarePath,
    bool ReadFromTreeSnapshot,
    double? TreeValue);

internal sealed record LibreHardwareMonitorMetricCatalog(
    IReadOnlyDictionary<string, LibreHardwareMonitorMetricEntry> Metrics,
    IReadOnlySet<string> AmbiguousMetricIds)
{
    public static LibreHardwareMonitorMetricCatalog Build(LibreHardwareMonitorTreeSnapshot tree, FileLogger? log = null)
    {
        var candidates = new Dictionary<string, List<LibreHardwareMonitorMetricEntry>>(StringComparer.OrdinalIgnoreCase);
        Traverse(
            tree.Root,
            Array.Empty<LibreHardwareMonitorHardwarePathSegment>(),
            currentSensorGroupName: null,
            candidates);

        var metrics = new Dictionary<string, LibreHardwareMonitorMetricEntry>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (metricId, entries) in candidates)
        {
            if (entries.Count == 1)
            {
                metrics.Add(metricId, entries[0]);
                continue;
            }

            if (!TryPublishDuplicateEntries(metricId, entries, metrics, out var publishedIds))
            {
                ambiguous.Add(metricId);
                log?.WarnThrottled(
                    $"lhm.catalog.duplicate.{metricId}",
                    TimeSpan.FromSeconds(30),
                    $"Libre Hardware Monitor exposes metric '{metricId}' more than once and the entries cannot be safely distinguished; PinkieSysMon will not publish it.");
                continue;
            }

            log?.WarnThrottled(
                $"lhm.catalog.duplicate-resolved.{metricId}",
                TimeSpan.FromSeconds(30),
                $"Libre Hardware Monitor exposes duplicate sensor ID '{metricId}'. PinkieSysMon disambiguated it as {string.Join(", ", publishedIds)} and will read those values from data.json.");
        }

        return new LibreHardwareMonitorMetricCatalog(metrics, ambiguous);
    }

    private static bool TryPublishDuplicateEntries(
        string baseMetricId,
        IReadOnlyList<LibreHardwareMonitorMetricEntry> entries,
        IDictionary<string, LibreHardwareMonitorMetricEntry> metrics,
        out IReadOnlyList<string> publishedIds)
    {
        publishedIds = Array.Empty<string>();

        var names = entries
            .Select(entry => entry.SensorName?.Trim())
            .ToArray();
        if (names.Any(string.IsNullOrWhiteSpace))
            return false;

        // A duplicate provider ID is only safe to expose if stable semantic metadata can
        // distinguish every entry. The LHM NVIDIA collision currently has distinct names
        // (for example "GPU Bus" and "GPU Memory"). Never fall back to tree order/node id.
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            return false;

        var slugs = names.Select(name => Slugify(name!)).ToArray();
        var slugCounts = slugs
            .GroupBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var created = new List<string>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var slug = slugs[i];
            if (slugCounts[slug] > 1)
                slug += "-" + StableShortHash(names[i]!);

            var syntheticMetricId = baseMetricId + "~" + slug;
            if (metrics.ContainsKey(syntheticMetricId))
                return false;

            metrics.Add(
                syntheticMetricId,
                entries[i] with
                {
                    MetricId = syntheticMetricId,
                    ReadFromTreeSnapshot = true
                });
            created.Add(syntheticMetricId);
        }

        publishedIds = created;
        return true;
    }

    private static string Slugify(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && result.Length > 0)
                    result.Append('-');
                result.Append(ch);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = result.Length > 0;
            }
        }

        return result.Length == 0 ? "sensor" : result.ToString();
    }

    private static string StableShortHash(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static void Traverse(
        LibreHardwareMonitorJsonNode node,
        IReadOnlyList<LibreHardwareMonitorHardwarePathSegment> currentHardwarePath,
        string? currentSensorGroupName,
        IDictionary<string, List<LibreHardwareMonitorMetricEntry>> candidates)
    {
        var isHardwareNode = !string.IsNullOrWhiteSpace(node.HardwareId);
        IReadOnlyList<LibreHardwareMonitorHardwarePathSegment> hardwarePath = currentHardwarePath;
        var sensorGroupName = currentSensorGroupName;

        if (isHardwareNode)
        {
            var hardwareName = string.IsNullOrWhiteSpace(node.Text) ? node.HardwareId! : node.Text!.Trim();
            var nextPath = new List<LibreHardwareMonitorHardwarePathSegment>(currentHardwarePath.Count + 1);
            nextPath.AddRange(currentHardwarePath);
            nextPath.Add(new LibreHardwareMonitorHardwarePathSegment(node.HardwareId!, hardwareName));
            hardwarePath = nextPath;
            sensorGroupName = null;
        }
        else if (string.IsNullOrWhiteSpace(node.SensorId) &&
                 node.Children is { Count: > 0 } &&
                 node.Children.Any(child => !string.IsNullOrWhiteSpace(child.SensorId)))
        {
            // LHM's TypeNode text is already the human-readable sensor group used by its own UI
            // (for example "Temperatures", "Loads", or "Powers"). Preserve it as presentation metadata.
            sensorGroupName = string.IsNullOrWhiteSpace(node.Text) ? null : node.Text!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(node.SensorId) &&
            !string.IsNullOrWhiteSpace(node.SensorType) &&
            LibreHardwareMonitorMetricContract.TryFromSensorId(node.SensorId, out var metricId))
        {
            if (!candidates.TryGetValue(metricId, out var list))
            {
                list = [];
                candidates.Add(metricId, list);
            }

            var nearestHardware = hardwarePath.Count > 0 ? hardwarePath[^1] : null;
            list.Add(new LibreHardwareMonitorMetricEntry(
                metricId,
                node.SensorId,
                node.SensorType,
                node.Text,
                nearestHardware?.HardwareId,
                nearestHardware?.HardwareName,
                sensorGroupName,
                hardwarePath.ToArray(),
                ReadFromTreeSnapshot: false,
                TreeValue: node.TryGetNumericValue(out var rawValue) ? rawValue : null));
        }

        if (node.Children is null)
            return;

        foreach (var child in node.Children)
            Traverse(child, hardwarePath, sensorGroupName, candidates);
    }
}

internal static class LibreHardwareMonitorMetricContract
{
    public const string Prefix = "lhm.";

    public static bool IsMetricId(string? metricId) =>
        !string.IsNullOrWhiteSpace(metricId) &&
        metricId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
        metricId.Length > Prefix.Length;

    public static bool TryFromSensorId(string? sensorId, out string metricId)
    {
        metricId = string.Empty;
        if (string.IsNullOrWhiteSpace(sensorId) || sensorId[0] != '/')
            return false;

        var path = sensorId[1..];
        if (path.Length == 0 || path.Contains("//", StringComparison.Ordinal))
            return false;

        metricId = Prefix + path.Replace('/', '.');
        return true;
    }

    public static MetricDescriptor CreateDescriptor(string metricId, string sensorType, string? sensorName = null)
    {
        var normalizedSensorType = sensorType.Trim().ToLowerInvariant();
        var (kind, unit) = normalizedSensorType switch
        {
            "voltage" => (MetricValueKind.Number, MetricUnit.Volts),
            "current" => (MetricValueKind.Number, MetricUnit.Amperes),
            "power" => (MetricValueKind.Number, MetricUnit.Watts),
            "clock" => (MetricValueKind.Number, MetricUnit.Megahertz),
            "temperature" => (MetricValueKind.Number, MetricUnit.Celsius),
            "load" => (MetricValueKind.Percent, MetricUnit.None),
            "frequency" => (MetricValueKind.Number, MetricUnit.Hertz),
            "fan" => (MetricValueKind.Number, MetricUnit.RevolutionsPerMinute),
            "flow" => (MetricValueKind.Number, MetricUnit.LitersPerHour),
            "control" => (MetricValueKind.Percent, MetricUnit.None),
            "level" => (MetricValueKind.Percent, MetricUnit.None),
            "factor" => (MetricValueKind.Number, MetricUnit.None),
            "data" => (MetricValueKind.DataSize, MetricUnit.Gibibytes),
            "smalldata" => (MetricValueKind.DataSize, MetricUnit.Mebibytes),
            "throughput" when string.Equals(sensorName?.Trim(), "Connection Speed", StringComparison.OrdinalIgnoreCase) =>
                (MetricValueKind.Number, MetricUnit.BitsPerSecond),
            "throughput" => (MetricValueKind.Number, MetricUnit.BytesPerSecond),
            "timespan" => (MetricValueKind.Duration, MetricUnit.Seconds),
            "timing" => (MetricValueKind.Number, MetricUnit.Nanoseconds),
            "energy" => (MetricValueKind.Number, MetricUnit.MilliWattHours),
            "noise" => (MetricValueKind.Number, MetricUnit.DecibelsA),
            "conductivity" => (MetricValueKind.Number, MetricUnit.MicroSiemensPerCentimeter),
            "humidity" => (MetricValueKind.Percent, MetricUnit.None),
            _ => (MetricValueKind.Number, MetricUnit.None)
        };

        return new MetricDescriptor(metricId, kind, unit);
    }
}

internal sealed class LibreHardwareMonitorRawTelemetrySource : IMetricSource, IDisposable
{
    private static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly object _sync = new();
    private readonly LibreHardwareMonitorHttpClient _client;
    private readonly FileLogger _log;

    private IReadOnlyDictionary<string, LibreHardwareMonitorMetricEntry> _catalog =
        new Dictionary<string, LibreHardwareMonitorMetricEntry>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _ambiguousMetricIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private long _catalogExpiresAtTick;
    private long _catalogTransportGeneration = -1;
    private bool _disposed;

    public LibreHardwareMonitorRawTelemetrySource(FileLogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _client = new LibreHardwareMonitorHttpClient(log);
    }

    public string ProviderId => MetricProviderContract.LibreHardwareMonitor;
    public string Name => "libre-hardware-monitor-raw";

    public IReadOnlyCollection<string> MetricNames
    {
        get
        {
            lock (_sync)
                return _catalog.Keys.ToArray();
        }
    }

    public int DefaultIntervalMs => 1000;

    public bool CanProvide(string metricName) => LibreHardwareMonitorMetricContract.IsMetricId(metricName);

    public IReadOnlyDictionary<string, object?> Capture() =>
        Capture(MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        ThrowIfDisposed();
        if (requestedMetrics.Count == 0)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        EnsureCatalog();

        var result = new Dictionary<string, object?>(requestedMetrics.Count, StringComparer.OrdinalIgnoreCase);
        var forcedCatalogRefreshAttempted = false;
        var liveTreeAttempted = false;
        LibreHardwareMonitorMetricCatalog? liveTreeCatalog = null;
        foreach (var metricId in requestedMetrics)
        {
            if (!LibreHardwareMonitorMetricContract.IsMetricId(metricId))
                continue;

            LibreHardwareMonitorMetricEntry? entry;
            bool ambiguous;
            lock (_sync)
            {
                ambiguous = _ambiguousMetricIds.Contains(metricId);
                _catalog.TryGetValue(metricId, out entry);
            }

            if (ambiguous)
            {
                result[metricId] = null;
                continue;
            }

            if (entry is null && !forcedCatalogRefreshAttempted)
            {
                // A dashboard may survive a temporary LHM outage or a provider restart. Refresh at
                // most once per capture batch before declaring missing provider-native metrics unavailable.
                forcedCatalogRefreshAttempted = true;
                EnsureCatalog(force: true);
                lock (_sync)
                {
                    ambiguous = _ambiguousMetricIds.Contains(metricId);
                    _catalog.TryGetValue(metricId, out entry);
                }
            }

            if (ambiguous || entry is null)
            {
                result[metricId] = null;
                continue;
            }

            if (entry.ReadFromTreeSnapshot)
            {
                if (!liveTreeAttempted)
                {
                    liveTreeAttempted = true;
                    if (_client.TryGetTree(out var liveTree) && liveTree is not null)
                        liveTreeCatalog = LibreHardwareMonitorMetricCatalog.Build(liveTree);
                }

                if (liveTreeCatalog is not null &&
                    liveTreeCatalog.Metrics.TryGetValue(metricId, out var liveEntry) &&
                    liveEntry.ReadFromTreeSnapshot)
                {
                    result[metricId] = liveEntry.TreeValue;
                    continue;
                }

                result[metricId] = null;
                InvalidateCatalog();
                continue;
            }

            if (_client.TryReadSensor(entry.SensorId, out var reading))
            {
                result[metricId] = reading.Value;
                continue;
            }

            result[metricId] = null;
            InvalidateCatalog();
        }

        return result;
    }

    public bool RefreshCatalog() => EnsureCatalog(force: true);

    private bool EnsureCatalog(bool force = false)
    {
        var now = Environment.TickCount64;
        lock (_sync)
        {
            if (!force &&
                _catalog.Count > 0 &&
                now < _catalogExpiresAtTick &&
                _catalogTransportGeneration == _client.StateGeneration)
            {
                return true;
            }
        }

        if (!_client.TryGetTree(out var tree) || tree is null)
            return false;

        var catalog = LibreHardwareMonitorMetricCatalog.Build(tree, _log);
        var descriptors = catalog.Metrics.Values
            .Select(entry => LibreHardwareMonitorMetricContract.CreateDescriptor(entry.MetricId, entry.SensorType, entry.SensorName))
            .ToArray();

        lock (_sync)
        {
            _catalog = catalog.Metrics;
            _ambiguousMetricIds = catalog.AmbiguousMetricIds;
            _catalogExpiresAtTick = now + (long)CatalogRefreshInterval.TotalMilliseconds;
            _catalogTransportGeneration = _client.StateGeneration;
        }

        MetricContract.ReplaceProviderDescriptors(ProviderId, descriptors);
        var treeBackedCount = catalog.Metrics.Values.Count(entry => entry.ReadFromTreeSnapshot);
        _log.Info(
            $"Libre Hardware Monitor raw catalog refreshed: metrics={catalog.Metrics.Count}, tree-backed-duplicates={treeBackedCount}, ambiguous={catalog.AmbiguousMetricIds.Count}.");
        return true;
    }

    private void InvalidateCatalog()
    {
        lock (_sync)
            _catalogExpiresAtTick = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LibreHardwareMonitorRawTelemetrySource));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.Dispose();
    }
}
