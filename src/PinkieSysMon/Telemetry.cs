using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using System.Text.Json;

namespace PinkieSysMon;

internal sealed class TelemetryConfig
{
    public Dictionary<string, MetricPollingConfig> Metrics { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static TelemetryConfig Load(string path, IReadOnlyCollection<IMetricSource> sources, FileLogger log)
    {
        if (!File.Exists(path))
        {
            log.Info("Telemetry config file not found; using defaults.");
            return CreateDefaults(sources);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<TelemetryConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new TelemetryConfig();

        if (config.Metrics is null)
            throw new InvalidDataException("Telemetry config 'metrics' must be an object.");

        config = config.NormalizeMetrics(sources, log);
        config.Validate();
        return config;
    }

    public static TelemetryConfig CreateDefaults(IReadOnlyCollection<IMetricSource> sources)
    {
        var metricIndex = MetricSourceCatalog.BuildMetricIndex(sources);
        return new TelemetryConfig
        {
            Metrics = metricIndex.ToDictionary(
                pair => pair.Key,
                pair => new MetricPollingConfig { Enabled = true, IntervalMs = pair.Value.DefaultIntervalMs },
                StringComparer.OrdinalIgnoreCase)
        };
    }


    private TelemetryConfig NormalizeMetrics(
        IReadOnlyCollection<IMetricSource> sources,
        FileLogger log)
    {
        var metricIndex = MetricSourceCatalog.BuildMetricIndex(sources);
        var normalized = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var (name, policy) in Metrics)
        {
            if (policy is null)
                throw new InvalidDataException($"Telemetry metric {name} configuration must be an object.");

            if (MetricSourceCatalog.TryResolveSource(sources, metricIndex, name, out _))
                normalized[name] = policy;
            else
                removed++;
        }

        var added = 0;
        foreach (var (name, source) in metricIndex)
        {
            if (normalized.ContainsKey(name))
                continue;
            normalized[name] = new MetricPollingConfig { Enabled = true, IntervalMs = source.DefaultIntervalMs };
            added++;
        }

        if (removed > 0 || added > 0)
            log.Info($"Telemetry config normalized in memory: removed={removed}, added={added}.");

        return removed == 0 && added == 0 ? this : new TelemetryConfig { Metrics = normalized };
    }

    private void Validate()
    {
        foreach (var (name, policy) in Metrics)
        {
            if (policy.IntervalMs < 100)
                throw new InvalidDataException($"Telemetry metric {name} IntervalMs must be at least 100 ms.");
        }
    }
}

internal sealed class MetricPollingConfig
{
    public bool Enabled { get; init; } = true;
    public int IntervalMs { get; init; } = 1000;
}

internal interface IMetricSource
{
    string ProviderId { get; }
    string Name { get; }
    IReadOnlyCollection<string> MetricNames { get; }
    int DefaultIntervalMs => 1000;
    IReadOnlyDictionary<string, object?> Capture();

    bool CanProvide(string metricName) =>
        MetricNames.Contains(metricName, StringComparer.OrdinalIgnoreCase);

    IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics) =>
        Capture();
}

internal static class MetricSourceCatalog
{
    public static IReadOnlyDictionary<string, IMetricSource> BuildMetricIndex(IEnumerable<IMetricSource> sources)
    {
        var index = new Dictionary<string, IMetricSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var metric in source.MetricNames)
            {
                if (!index.TryAdd(metric, source))
                    throw new InvalidOperationException($"Metric '{metric}' is provided by more than one source.");
            }
        }

        return index;
    }

    public static bool TryResolveSource(
        IReadOnlyCollection<IMetricSource> sources,
        IReadOnlyDictionary<string, IMetricSource> metricIndex,
        string metricName,
        out IMetricSource? source)
    {
        if (metricIndex.TryGetValue(metricName, out var exactSource))
        {
            source = exactSource;
            return true;
        }

        source = null;
        foreach (var candidate in sources)
        {
            if (!candidate.CanProvide(metricName))
                continue;

            if (source is not null && !ReferenceEquals(source, candidate))
                throw new InvalidOperationException($"Metric '{metricName}' is claimed by more than one source.");
            source = candidate;
        }

        return source is not null;
    }
}

internal static class WindowsManagementQuery
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static ManagementObjectSearcher CreateSearcher(string query)
    {
        var searcher = new ManagementObjectSearcher("root\\CIMV2", query);
        searcher.Options.Timeout = DefaultTimeout;
        return searcher;
    }
}

internal sealed class TelemetryEngine : IDisposable
{
    private const int MinimumSchedulerDelayMs = 100;
    private const int MaximumSchedulerDelayMs = 60_000;
    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly MetricStore _store;
    private readonly FileLogger _log;
    private readonly IMetricSource[] _sources;
    private readonly IReadOnlyDictionary<string, IMetricSource> _metricIndex;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    // Scheduling state is rebuilt only on configuration/dashboard changes. The worker reuses
    // source due-lists and the capture batch on every polling turn, avoiding LINQ/group/array
    // churn in the steady-state loop.
    private List<MetricSchedule> _metricSchedules = [];
    private List<SourceSchedule> _sourceSchedules = [];
    private readonly List<SourceSchedule> _dueSources = [];
    private readonly Dictionary<string, object?> _captureBatch = new(StringComparer.OrdinalIgnoreCase);

    private TelemetryConfig _config;
    private HashSet<string> _enabledProviders;
    private HashSet<string> _requiredMetrics;
    private long _scheduleVersion;
    private Task? _worker;
    private int _disposeState;

    public TelemetryEngine(
        MetricStore store,
        IEnumerable<IMetricSource> sources,
        TelemetryConfig config,
        IEnumerable<string> enabledProviders,
        IEnumerable<string> requiredMetrics,
        FileLogger log)
    {
        _store = store;
        _log = log;
        _sources = sources.ToArray();
        _metricIndex = BuildMetricIndex(_sources);
        _config = config;
        _enabledProviders = NormalizeEnabledProviders(enabledProviders);
        _requiredMetrics = NormalizeRequiredMetrics(requiredMetrics);
        RebuildScheduleLocked();
    }

    public static IReadOnlyDictionary<string, IMetricSource> BuildMetricIndex(IEnumerable<IMetricSource> sources) =>
        MetricSourceCatalog.BuildMetricIndex(sources);

    public void Start()
    {
        if (_worker is not null)
            return;

        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Reconfigure(
        TelemetryConfig config,
        IEnumerable<string> enabledProviders,
        IEnumerable<string> requiredMetrics)
    {
        var prepared = PrepareReconfiguration(config, enabledProviders, requiredMetrics);
        CommitReconfiguration(prepared);
    }

    public ReconfigurationPlan PrepareReconfiguration(
        TelemetryConfig config,
        IEnumerable<string> enabledProviders,
        IEnumerable<string> requiredMetrics)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(enabledProviders);
        ArgumentNullException.ThrowIfNull(requiredMetrics);

        lock (_sync)
        {
            var normalizedProviders = NormalizeEnabledProviders(enabledProviders);
            var normalizedRequiredMetrics = NormalizeRequiredMetrics(requiredMetrics);
            var (metricSchedules, sourceSchedules) =
                BuildSchedule(config, normalizedProviders, normalizedRequiredMetrics);

            var previouslyActiveMetrics = GetActiveMetricNamesLocked();
            var currentlyActiveMetrics = metricSchedules
                .Select(schedule => schedule.MetricName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            previouslyActiveMetrics.ExceptWith(currentlyActiveMetrics);

            return new ReconfigurationPlan(
                _scheduleVersion,
                config,
                normalizedProviders,
                normalizedRequiredMetrics,
                metricSchedules,
                sourceSchedules,
                previouslyActiveMetrics.ToArray());
        }
    }

    public void CommitReconfiguration(ReconfigurationPlan prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        lock (_sync)
        {
            if (prepared.ExpectedScheduleVersion != _scheduleVersion)
            {
                throw new InvalidOperationException(
                    "Telemetry reconfiguration plan is stale because another reconfiguration was committed first.");
            }

            // Publish the deactivation batch before mutating scheduler state. If allocation or
            // publication unexpectedly fails, the old telemetry schedule remains intact.
            if (prepared.DeactivatedMetricNames.Length > 0)
            {
                _store.Publish(prepared.DeactivatedMetricNames.ToDictionary(
                    metric => metric,
                    _ => (object?)null,
                    StringComparer.OrdinalIgnoreCase));
            }

            _config = prepared.Config;
            _enabledProviders = prepared.EnabledProviders;
            _requiredMetrics = prepared.RequiredMetrics;
            _metricSchedules = prepared.MetricSchedules;
            _sourceSchedules = prepared.SourceSchedules;
            _scheduleVersion++;
        }

        SignalWake();
        _log.Info(
            $"Telemetry reconfigured. providers={string.Join(",", prepared.EnabledProviders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}; " +
            $"configEnabledMetrics={prepared.Config.Metrics.Count(x => x.Value.Enabled)}; " +
            $"dashboardRequiredMetrics={prepared.RequiredMetrics.Count}; active={prepared.MetricSchedules.Count}; " +
            $"deactivated={prepared.DeactivatedMetricNames.Length}.");
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                long planVersion;
                lock (_sync)
                    planVersion = PrepareDueSourcesLocked(Stopwatch.GetTimestamp());

                CaptureDueSources();

                int delayMs;
                lock (_sync)
                {
                    if (planVersion == _scheduleVersion)
                        _store.Publish(_captureBatch);

                    delayMs = GetNextDelayMsLocked(Stopwatch.GetTimestamp());
                }

                await WaitForWakeOrDelayAsync(delayMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.ErrorThrottled("telemetry.loop", ErrorLogThrottleInterval, "Telemetry loop error", ex);
                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private long PrepareDueSourcesLocked(long nowTimestamp)
    {
        _dueSources.Clear();

        foreach (var sourceSchedule in _sourceSchedules)
        {
            sourceSchedule.DueMetricNames.Clear();

            foreach (var metric in sourceSchedule.MetricSchedules)
            {
                if (metric.NextDueTimestamp != 0 && metric.NextDueTimestamp > nowTimestamp)
                    continue;

                sourceSchedule.DueMetricNames.Add(metric.MetricName);
                metric.NextDueTimestamp = nowTimestamp + metric.IntervalTicks;
            }

            if (sourceSchedule.DueMetricNames.Count > 0)
                _dueSources.Add(sourceSchedule);
        }

        return _scheduleVersion;
    }

    private void CaptureDueSources()
    {
        _captureBatch.Clear();

        foreach (var sourceSchedule in _dueSources)
        {
            try
            {
                var snapshot = sourceSchedule.Source.Capture(sourceSchedule.DueMetricNames);
                foreach (var metric in sourceSchedule.DueMetricNames)
                {
                    if (snapshot.TryGetValue(metric, out var value))
                        _captureBatch[metric] = value;
                    else
                        _captureBatch[metric] = null;
                }
            }
            catch (Exception ex)
            {
                // A provider is an isolation boundary. One optional telemetry source must not
                // prevent unrelated providers from publishing the rest of the current batch.
                foreach (var metric in sourceSchedule.DueMetricNames)
                    _captureBatch[metric] = null;

                var source = sourceSchedule.Source;
                _log.ErrorThrottled(
                    $"telemetry.source.{source.ProviderId}.{source.Name}",
                    ErrorLogThrottleInterval,
                    $"Telemetry source '{source.Name}' ({source.ProviderId}) capture failed",
                    ex);
            }
        }
    }

    private int GetNextDelayMsLocked(long nowTimestamp)
    {
        if (_metricSchedules.Count == 0)
            return MaximumSchedulerDelayMs;

        var earliest = long.MaxValue;
        foreach (var metric in _metricSchedules)
        {
            if (metric.NextDueTimestamp < earliest)
                earliest = metric.NextDueTimestamp;
        }

        if (earliest <= nowTimestamp)
            return MinimumSchedulerDelayMs;

        var remainingTicks = earliest - nowTimestamp;
        var remainingMs = (int)Math.Ceiling(remainingTicks * 1000d / Stopwatch.Frequency);
        return Math.Clamp(remainingMs, MinimumSchedulerDelayMs, MaximumSchedulerDelayMs);
    }

    private void RebuildScheduleLocked()
    {
        (_metricSchedules, _sourceSchedules) =
            BuildSchedule(_config, _enabledProviders, _requiredMetrics);
    }

    private (List<MetricSchedule> MetricSchedules, List<SourceSchedule> SourceSchedules) BuildSchedule(
        TelemetryConfig config,
        HashSet<string> enabledProviders,
        HashSet<string> requiredMetrics)
    {
        var active = new List<MetricSchedule>();
        var sourceSchedules = new Dictionary<IMetricSource, SourceSchedule>();

        foreach (var name in requiredMetrics)
        {
            var source = ResolveMetricSource(name);
            if (source is null || !enabledProviders.Contains(source.ProviderId))
                continue;

            var policy = config.Metrics.TryGetValue(name, out var configuredPolicy)
                ? configuredPolicy
                : new MetricPollingConfig { Enabled = true, IntervalMs = source.DefaultIntervalMs };
            if (!policy.Enabled)
                continue;

            if (!sourceSchedules.TryGetValue(source, out var sourceSchedule))
            {
                sourceSchedule = new SourceSchedule(source);
                sourceSchedules.Add(source, sourceSchedule);
            }

            var intervalTicks = Math.Max(
                1L,
                (long)Math.Ceiling(policy.IntervalMs * (double)Stopwatch.Frequency / 1000d));
            var schedule = new MetricSchedule(name, intervalTicks);
            active.Add(schedule);
            sourceSchedule.MetricSchedules.Add(schedule);
        }

        return (active, sourceSchedules.Values.ToList());
    }

    private HashSet<string> GetActiveMetricNamesLocked() =>
        _metricSchedules
            .Select(schedule => schedule.MetricName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> NormalizeEnabledProviders(IEnumerable<string> enabledProviders)
    {
        return enabledProviders
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(provider => provider.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> NormalizeRequiredMetrics(IEnumerable<string> requiredMetrics)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metric in requiredMetrics)
        {
            if (string.IsNullOrWhiteSpace(metric))
                continue;

            var normalized = metric.Trim();
            if (ResolveMetricSource(normalized) is not null)
                result.Add(normalized);
        }
        return result;
    }

    private IMetricSource? ResolveMetricSource(string metricName) =>
        MetricSourceCatalog.TryResolveSource(_sources, _metricIndex, metricName, out var source) ? source : null;

    private async Task WaitForWakeOrDelayAsync(int delayMs, CancellationToken token)
    {
        await _wakeSignal.WaitAsync(TimeSpan.FromMilliseconds(delayMs), token).ConfigureAwait(false);
    }

    private void SignalWake()
    {
        try
        {
            if (_wakeSignal.CurrentCount == 0)
                _wakeSignal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    internal sealed class ReconfigurationPlan
    {
        internal ReconfigurationPlan(
            long expectedScheduleVersion,
            TelemetryConfig config,
            HashSet<string> enabledProviders,
            HashSet<string> requiredMetrics,
            List<MetricSchedule> metricSchedules,
            List<SourceSchedule> sourceSchedules,
            string[] deactivatedMetricNames)
        {
            ExpectedScheduleVersion = expectedScheduleVersion;
            Config = config;
            EnabledProviders = enabledProviders;
            RequiredMetrics = requiredMetrics;
            MetricSchedules = metricSchedules;
            SourceSchedules = sourceSchedules;
            DeactivatedMetricNames = deactivatedMetricNames;
        }

        internal long ExpectedScheduleVersion { get; }
        internal TelemetryConfig Config { get; }
        internal HashSet<string> EnabledProviders { get; }
        internal HashSet<string> RequiredMetrics { get; }
        internal List<MetricSchedule> MetricSchedules { get; }
        internal List<SourceSchedule> SourceSchedules { get; }
        internal string[] DeactivatedMetricNames { get; }
    }

    internal sealed class MetricSchedule
    {
        public MetricSchedule(string name, long intervalTicks)
        {
            MetricName = name;
            IntervalTicks = intervalTicks;
        }

        public string MetricName { get; }
        public long IntervalTicks { get; }
        public long NextDueTimestamp { get; set; }
    }

    internal sealed class SourceSchedule
    {
        public SourceSchedule(IMetricSource source)
        {
            Source = source;
        }

        public IMetricSource Source { get; }
        public List<MetricSchedule> MetricSchedules { get; } = [];
        public List<string> DueMetricNames { get; } = [];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _cts.Cancel();
        SignalWake();
        try
        {
            _worker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _wakeSignal.Dispose();
            _cts.Dispose();
        }
    }
}

internal sealed class WindowsSystemTelemetrySource : IMetricSource
{
    private readonly IReadOnlyDictionary<string, object?> _staticMetrics;
    private readonly WindowsIntegratedGpuTelemetry _integratedGpuTelemetry;

    public WindowsSystemTelemetrySource(FileLogger log)
    {
        _staticMetrics = LoadStaticMetrics(log);
        _integratedGpuTelemetry = new WindowsIntegratedGpuTelemetry(log);
    }

    public string ProviderId => MetricProviderContract.System;
    public string Name => "windows-system";
    public IReadOnlyCollection<string> MetricNames => SystemMetricContract.MetricNames;

    public IReadOnlyDictionary<string, object?> Capture() =>
        Capture(SystemMetricContract.MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        var requested = requestedMetrics as HashSet<string>
            ?? new HashSet<string>(requestedMetrics, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, object?>(requested.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _staticMetrics)
        {
            if (requested.Contains(pair.Key))
                result[pair.Key] = pair.Value;
        }

        if (requested.Contains(SystemMetricContract.DateTime))
            result[SystemMetricContract.DateTime] = DateTime.Now;
        if (requested.Contains(SystemMetricContract.Uptime))
            result[SystemMetricContract.Uptime] = Environment.TickCount64 / 1000L;
        if (requested.Contains(SystemMetricContract.IntegratedGpuLoad))
            result[SystemMetricContract.IntegratedGpuLoad] = _integratedGpuTelemetry.ReadLoadPercent();
        return result;
    }

    private static IReadOnlyDictionary<string, object?> LoadStaticMetrics(FileLogger log)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SystemMetricContract.Edition] = null,
            [SystemMetricContract.Release] = ReadRelease(),
            [SystemMetricContract.Build] = null,
            [SystemMetricContract.Installed] = null,
        };

        try
        {
            const string query = "SELECT Caption, BuildNumber, InstallDate FROM Win32_OperatingSystem";
            using var searcher = WindowsManagementQuery.CreateSearcher(query);
            using var rows = searcher.Get();
            using var os = rows.Cast<ManagementObject>().FirstOrDefault();
            if (os is null)
                return result;

            result[SystemMetricContract.Edition] = NormalizeText(os["Caption"]);
            result[SystemMetricContract.Installed] = ParseWmiDateTime(os["InstallDate"]);

            var build = NormalizeText(os["BuildNumber"]);
            var ubr = ReadUbr();
            result[SystemMetricContract.Build] = !string.IsNullOrWhiteSpace(build) && ubr is not null
                ? $"{build}.{ubr.Value}"
                : build;
        }
        catch (Exception ex)
        {
            log.Error("Windows system identity query failed; static system.os.* metrics may be unavailable", ex);
        }

        return result;
    }

    private static string? NormalizeText(object? value)
    {
        var text = Convert.ToString(value)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ReadRelease()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var displayVersion = NormalizeText(key?.GetValue("DisplayVersion"));
            return displayVersion ?? NormalizeText(key?.GetValue("ReleaseId"));
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadUbr()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var value = key?.GetValue("UBR");
            return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? ParseWmiDateTime(object? value)
    {
        var text = Convert.ToString(value)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class WindowsPowerTelemetrySource : IMetricSource
{
    private readonly FileLogger _log;
    private bool _failureLogged;

    public WindowsPowerTelemetrySource(FileLogger log)
    {
        _log = log;
    }

    public string ProviderId => MetricProviderContract.System;
    public string Name => "windows-power";
    public IReadOnlyCollection<string> MetricNames => PowerMetricContract.MetricNames;
    public int DefaultIntervalMs => 5000;

    public IReadOnlyDictionary<string, object?> Capture() =>
        Capture(PowerMetricContract.MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        var requested = requestedMetrics as HashSet<string>
            ?? new HashSet<string>(requestedMetrics, StringComparer.OrdinalIgnoreCase);

        try
        {
            var batteries = QueryBatteries();
            if (_failureLogged)
            {
                _log.Info("Windows power telemetry recovered.");
                _failureLogged = false;
            }

            var ups = batteries.FirstOrDefault(x => x.IsUps);
            var battery = batteries.FirstOrDefault(x => !x.IsUps);

            var result = new Dictionary<string, object?>(requested.Count, StringComparer.OrdinalIgnoreCase);
            AddEntity(result, requested, PowerMetricContract.UpsSource, ups);
            AddEntity(result, requested, PowerMetricContract.BatterySource, battery);
            return result;
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _log.Error("Windows power telemetry query failed; power metrics will report unavailable until WMI recovers", ex);
                _failureLogged = true;
            }

            var result = new Dictionary<string, object?>(requested.Count, StringComparer.OrdinalIgnoreCase);
            AddEntity(result, requested, PowerMetricContract.UpsSource, null);
            AddEntity(result, requested, PowerMetricContract.BatterySource, null);
            return result;
        }
    }

    private static List<PowerDeviceSnapshot> QueryBatteries()
    {
        const string query =
            "SELECT Name, Description, DeviceID, PNPDeviceID, BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime FROM Win32_Battery";

        using var searcher = WindowsManagementQuery.CreateSearcher(query);
        using var results = searcher.Get();
        var batteries = new List<PowerDeviceSnapshot>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var identity = string.Join(" ", new[]
                {
                    GetString(item, "Name"),
                    GetString(item, "Description"),
                    GetString(item, "DeviceID"),
                    GetString(item, "PNPDeviceID")
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

                var status = GetUInt16(item, "BatteryStatus");
                var charge = GetUInt16(item, "EstimatedChargeRemaining");
                var runtimeMinutes = GetUInt32(item, "EstimatedRunTime");
                batteries.Add(new PowerDeviceSnapshot(
                    IsUpsIdentity(identity),
                    charge is null ? null : Math.Clamp((int)charge.Value, 0, 100),
                    ToSeconds(runtimeMinutes),
                    PowerMetricContract.FromWmiBatteryStatus(status)));
            }
        }

        return batteries;
    }

    private static void AddEntity(
        IDictionary<string, object?> result,
        IReadOnlySet<string> requestedMetrics,
        string source,
        PowerDeviceSnapshot? snapshot)
    {
        var isUps = string.Equals(source, PowerMetricContract.UpsSource, StringComparison.OrdinalIgnoreCase);
        var chargeMetric = isUps ? PowerMetricContract.UpsChargeMetric : PowerMetricContract.BatteryChargeMetric;
        var runtimeMetric = isUps ? PowerMetricContract.UpsRuntimeRemainingMetric : PowerMetricContract.BatteryRuntimeRemainingMetric;
        var stateMetric = isUps ? PowerMetricContract.UpsStateMetric : PowerMetricContract.BatteryStateMetric;

        if (requestedMetrics.Contains(chargeMetric))
            result[chargeMetric] = snapshot?.ChargePercent;
        if (requestedMetrics.Contains(runtimeMetric))
            result[runtimeMetric] = snapshot?.RuntimeSeconds;
        if (requestedMetrics.Contains(stateMetric))
            result[stateMetric] = snapshot?.State ?? PowerMetricContract.StateUnavailable;
    }

    private static bool IsUpsIdentity(string identity) =>
        identity.Contains("UPS", StringComparison.OrdinalIgnoreCase) ||
        identity.Contains("uninterruptible", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(ManagementBaseObject item, string propertyName) =>
        Convert.ToString(item[propertyName])?.Trim();

    private static ushort? GetUInt16(ManagementBaseObject item, string propertyName)
    {
        var value = item[propertyName];
        if (value is null)
            return null;

        try
        {
            return Convert.ToUInt16(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static uint? GetUInt32(ManagementBaseObject item, string propertyName)
    {
        var value = item[propertyName];
        if (value is null)
            return null;

        try
        {
            return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static long? ToSeconds(uint? runtimeMinutes)
    {
        if (runtimeMinutes is null || runtimeMinutes == uint.MaxValue)
            return null;

        return (long)runtimeMinutes.Value * 60L;
    }

    private sealed record PowerDeviceSnapshot(
        bool IsUps,
        int? ChargePercent,
        long? RuntimeSeconds,
        string State);
}
