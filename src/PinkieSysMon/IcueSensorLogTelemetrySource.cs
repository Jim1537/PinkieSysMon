using System.Globalization;
using System.Text;

namespace PinkieSysMon;

internal sealed record IcueSensorLogMetricEntry(
    string MetricId,
    string ColumnName,
    int ColumnIndex,
    string DeviceName,
    string SensorName,
    string SensorGroupName,
    MetricValueKind ValueKind,
    MetricUnit BaseUnit);

internal sealed record IcueSensorLogMetricCatalog(
    IReadOnlyDictionary<string, IcueSensorLogMetricEntry> Metrics,
    IReadOnlySet<string> AmbiguousMetricIds);

internal static class IcueSensorLogMetricContract
{
    public const string Prefix = "icue.";

    public static bool IsMetricId(string? metricId) =>
        !string.IsNullOrWhiteSpace(metricId) &&
        metricId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
        metricId.Length > Prefix.Length;

    public static bool TryFromColumnName(string? columnName, out string metricId)
    {
        metricId = string.Empty;
        if (string.IsNullOrWhiteSpace(columnName))
            return false;

        var normalized = columnName.Trim();
        if (normalized.Length == 0 || normalized.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
            return false;

        metricId = Prefix + normalized;
        return true;
    }

    public static MetricDescriptor CreateDescriptor(IcueSensorLogMetricEntry entry) =>
        new(entry.MetricId, entry.ValueKind, entry.BaseUnit);
}

internal sealed class IcueSensorLogTelemetrySource : IMetricSource
{
    private const string LogFilePattern = "corsair_cue_*.csv";
    private const int TailProbeBytes = 1024 * 1024;
    private const int MaximumPendingTextChars = 64 * 1024;
    private const int BackgroundDiscoveryIntervalMs = 5_000;
    private const int RetainedLogFileCount = 2;

    private static readonly TimeSpan SampleStaleAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupMinimumAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly FileLogger _log;

    private IReadOnlyDictionary<string, IcueSensorLogMetricEntry> _catalog =
        new Dictionary<string, IcueSensorLogMetricEntry>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _ambiguousMetricIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _latestValues = new(StringComparer.OrdinalIgnoreCase);

    private string? _activeLogPath;
    private long _activePosition;
    private string _pendingText = string.Empty;
    private DateTime _lastSampleWriteUtc = DateTime.MinValue;
    private long _nextDiscoveryAtMs;

    public IcueSensorLogTelemetrySource(string applicationRoot, FileLogger log)
    {
        if (string.IsNullOrWhiteSpace(applicationRoot))
            throw new ArgumentException("Application root cannot be empty.", nameof(applicationRoot));

        _logDirectory = Path.Combine(applicationRoot, "logs");
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public string ProviderId => MetricProviderContract.Icue;
    public string Name => "icue-sensor-log";
    public int DefaultIntervalMs => 1000;

    public IReadOnlyCollection<string> MetricNames
    {
        get
        {
            lock (_sync)
                return _catalog.Keys.ToArray();
        }
    }

    public IReadOnlyCollection<IcueSensorLogMetricEntry> MetricEntries
    {
        get
        {
            lock (_sync)
                return _catalog.Values.ToArray();
        }
    }

    public bool CanProvide(string metricName) => IcueSensorLogMetricContract.IsMetricId(metricName);

    public IReadOnlyDictionary<string, object?> Capture() => Capture(MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        if (requestedMetrics.Count == 0)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            EnsureActiveLog(forceCatalogRefresh: false);
            ReadAppendedSamples();

            var fresh = _lastSampleWriteUtc != DateTime.MinValue &&
                        DateTime.UtcNow - _lastSampleWriteUtc <= SampleStaleAfter;
            var result = new Dictionary<string, object?>(requestedMetrics.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var metricId in requestedMetrics)
            {
                if (!IcueSensorLogMetricContract.IsMetricId(metricId))
                    continue;

                if (_ambiguousMetricIds.Contains(metricId) || !_catalog.ContainsKey(metricId) || !fresh)
                {
                    result[metricId] = null;
                    continue;
                }

                result[metricId] = _latestValues.TryGetValue(metricId, out var value) ? value : null;
            }

            return result;
        }
    }

    public bool RefreshCatalog()
    {
        lock (_sync)
            return EnsureActiveLog(forceCatalogRefresh: true);
    }

    private bool EnsureActiveLog(bool forceCatalogRefresh)
    {
        var nowMs = Environment.TickCount64;
        if (!forceCatalogRefresh && nowMs < _nextDiscoveryAtMs)
            return _activeLogPath is not null;

        _nextDiscoveryAtMs = nowMs + BackgroundDiscoveryIntervalMs;
        if (!TryFindLatestValidLog(out var candidate, out var headerColumns))
        {
            if (forceCatalogRefresh)
            {
                _log.WarnThrottled(
                    "icue.sensor-log.discovery",
                    ErrorLogThrottleInterval,
                    $"No valid iCUE Sensor Logging CSV was found in '{_logDirectory}'.");
            }
            return _activeLogPath is not null;
        }

        if (!forceCatalogRefresh &&
            _activeLogPath is not null &&
            candidate.Equals(_activeLogPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryReadTailSnapshot(candidate, out var sampleFields, out var nextPosition, out var sampleWriteUtc))
        {
            sampleFields = null;
            nextPosition = 0;
            sampleWriteUtc = DateTime.MinValue;
        }

        var catalog = BuildCatalog(headerColumns, sampleFields);
        var descriptors = catalog.Metrics.Values
            .Select(IcueSensorLogMetricContract.CreateDescriptor)
            .ToArray();

        _activeLogPath = candidate;
        _activePosition = nextPosition;
        _pendingText = string.Empty;
        _catalog = catalog.Metrics;
        _ambiguousMetricIds = catalog.AmbiguousMetricIds;
        _latestValues.Clear();
        _lastSampleWriteUtc = DateTime.MinValue;

        if (sampleFields is not null)
            ApplySample(sampleFields, sampleWriteUtc);

        MetricContract.ReplaceProviderDescriptors(ProviderId, descriptors);
        CleanupOldLogs(candidate);

        _log.Info(
            $"iCUE sensor log catalog refreshed: file={Path.GetFileName(candidate)}, " +
            $"metrics={catalog.Metrics.Count}, ambiguous={catalog.AmbiguousMetricIds.Count}.");
        return true;
    }

    private bool TryFindLatestValidLog(out string path, out IReadOnlyList<string> headerColumns)
    {
        path = string.Empty;
        headerColumns = Array.Empty<string>();

        if (!Directory.Exists(_logDirectory))
            return false;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_logDirectory, LogFilePattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(GetLastWriteTimeUtcSafe)
                .ThenByDescending(path => Path.GetFileName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WarnThrottled(
                "icue.sensor-log.enumerate",
                ErrorLogThrottleInterval,
                "Could not enumerate iCUE Sensor Logging CSV files.",
                ex);
            return false;
        }

        foreach (var file in files)
        {
            if (!TryReadHeader(file, out var columns))
                continue;

            path = file;
            headerColumns = columns;
            return true;
        }

        return false;
    }

    private static DateTime GetLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private bool TryReadHeader(string path, out IReadOnlyList<string> columns)
    {
        columns = Array.Empty<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parsed = ParseCsvLine(line);
            if (parsed.Count < 2 || !parsed[0].Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
                return false;

            columns = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WarnThrottled(
                $"icue.sensor-log.header.{Path.GetFileName(path)}",
                ErrorLogThrottleInterval,
                $"Could not read iCUE Sensor Logging header from '{path}'.",
                ex);
            return false;
        }
    }

    private IcueSensorLogMetricCatalog BuildCatalog(
        IReadOnlyList<string> headerColumns,
        IReadOnlyList<string>? sampleFields)
    {
        var candidates = new Dictionary<string, List<IcueSensorLogMetricEntry>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < headerColumns.Count; index++)
        {
            var columnName = headerColumns[index].Trim();
            if (!IcueSensorLogMetricContract.TryFromColumnName(columnName, out var metricId))
                continue;

            var sampleValue = sampleFields is not null && index < sampleFields.Count
                ? sampleFields[index]
                : null;
            var (deviceName, sensorName) = SplitSensorIdentity(columnName);
            var (valueKind, unit) = InferValueContract(sensorName, sampleValue);
            var groupName = InferSensorGroup(sensorName, unit);
            var entry = new IcueSensorLogMetricEntry(
                metricId,
                columnName,
                index,
                deviceName,
                sensorName,
                groupName,
                valueKind,
                unit);

            if (!candidates.TryGetValue(metricId, out var entries))
            {
                entries = [];
                candidates.Add(metricId, entries);
            }
            entries.Add(entry);
        }

        var metrics = new Dictionary<string, IcueSensorLogMetricEntry>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (metricId, entries) in candidates)
        {
            if (entries.Count != 1)
            {
                ambiguous.Add(metricId);
                _log.WarnThrottled(
                    $"icue.sensor-log.duplicate.{metricId}",
                    ErrorLogThrottleInterval,
                    $"iCUE Sensor Logging exposes metric '{metricId}' more than once; PinkieSysMon will not publish it.");
                continue;
            }

            metrics.Add(metricId, entries[0]);
        }

        return new IcueSensorLogMetricCatalog(metrics, ambiguous);
    }

    private static (string DeviceName, string SensorName) SplitSensorIdentity(string columnName)
    {
        var separator = columnName.LastIndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0 || separator + 2 >= columnName.Length)
            return (columnName, columnName);

        return (columnName[..separator].Trim(), columnName[(separator + 2)..].Trim());
    }

    private static (MetricValueKind ValueKind, MetricUnit Unit) InferValueContract(string sensorName, string? sampleValue)
    {
        var value = sampleValue?.Trim() ?? string.Empty;
        var name = sensorName.Trim();

        if (value.EndsWith("RPM", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Fan", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Pump", StringComparison.OrdinalIgnoreCase))
        {
            return (MetricValueKind.Number, MetricUnit.RevolutionsPerMinute);
        }

        if (value.EndsWith("°C", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Temp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return (MetricValueKind.Number, MetricUnit.Celsius);
        }

        if (value.EndsWith("%", StringComparison.Ordinal))
            return (MetricValueKind.Percent, MetricUnit.None);
        if (value.EndsWith("MHz", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Megahertz);
        if (value.EndsWith("GHz", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Gigahertz);
        if (value.EndsWith("kHz", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Kilohertz);
        if (value.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Hertz);
        if (value.EndsWith("W", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Watts);
        if (value.EndsWith("V", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Volts);
        if (value.EndsWith("A", StringComparison.OrdinalIgnoreCase))
            return (MetricValueKind.Number, MetricUnit.Amperes);

        return (MetricValueKind.Number, MetricUnit.None);
    }

    private static string InferSensorGroup(string sensorName, MetricUnit unit)
    {
        if (sensorName.Equals("Fan", StringComparison.OrdinalIgnoreCase))
            return "Fans";
        if (sensorName.Equals("Pump", StringComparison.OrdinalIgnoreCase))
            return "Pumps";
        if (unit is MetricUnit.Celsius or MetricUnit.Fahrenheit ||
            sensorName.Contains("Temp", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return "Temperatures";
        }

        return "Sensors";
    }

    private bool TryReadTailSnapshot(
        string path,
        out IReadOnlyList<string>? sampleFields,
        out long nextPosition,
        out DateTime sampleWriteUtc)
    {
        sampleFields = null;
        nextPosition = 0;
        sampleWriteUtc = DateTime.MinValue;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length == 0)
                return true;

            var bytesToRead = (int)Math.Min(length, TailProbeBytes);
            var start = length - bytesToRead;
            stream.Seek(start, SeekOrigin.Begin);

            var buffer = new byte[bytesToRead];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0)
                    break;
                read += count;
            }

            if (read == 0)
                return true;

            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1, read);
            if (lastNewline < 0)
            {
                nextPosition = start;
                return true;
            }

            nextPosition = start + lastNewline + 1L;
            var completeText = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
            var lines = completeText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return true;

            var line = lines[^1].TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart('\uFEFF').StartsWith("Timestamp,", StringComparison.OrdinalIgnoreCase))
                return true;

            sampleFields = ParseCsvLine(line);
            sampleWriteUtc = File.GetLastWriteTimeUtc(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WarnThrottled(
                $"icue.sensor-log.tail.{Path.GetFileName(path)}",
                ErrorLogThrottleInterval,
                $"Could not read the current iCUE Sensor Logging CSV tail from '{path}'.",
                ex);
            return false;
        }
    }

    private void ReadAppendedSamples()
    {
        var activeLogPath = _activeLogPath;
        if (activeLogPath is null)
            return;

        try
        {
            using var stream = new FileStream(
                activeLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < _activePosition)
            {
                EnsureActiveLog(forceCatalogRefresh: true);
                return;
            }

            if (stream.Length == _activePosition)
                return;

            var unreadBytes = stream.Length - _activePosition;
            if (unreadBytes > TailProbeBytes)
            {
                if (TryReadTailSnapshot(activeLogPath, out var latestFields, out var nextPosition, out var tailSampleWriteUtc))
                {
                    _activePosition = nextPosition;
                    _pendingText = string.Empty;
                    if (latestFields is not null)
                        ApplySample(latestFields, tailSampleWriteUtc);

                    _log.WarnThrottled(
                        "icue.sensor-log.catch-up",
                        ErrorLogThrottleInterval,
                        $"Skipped {unreadBytes} unread bytes in the iCUE sensor log and resumed from its latest complete sample.");
                }
                return;
            }

            stream.Seek(_activePosition, SeekOrigin.Begin);
            using var memory = new MemoryStream(checked((int)unreadBytes));
            stream.CopyTo(memory);
            _activePosition = stream.Position;

            _pendingText += Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
            var lastNewline = _pendingText.LastIndexOf('\n');
            if (lastNewline < 0)
            {
                TrimOversizedPendingText();
                return;
            }

            var completeText = _pendingText[..(lastNewline + 1)];
            _pendingText = _pendingText[(lastNewline + 1)..];
            TrimOversizedPendingText();
            var sampleWriteUtc = File.GetLastWriteTimeUtc(activeLogPath);

            foreach (var rawLine in completeText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0 || line.TrimStart('\uFEFF').StartsWith("Timestamp,", StringComparison.OrdinalIgnoreCase))
                    continue;

                ApplySample(ParseCsvLine(line), sampleWriteUtc);
            }
        }
        catch (FileNotFoundException)
        {
            EnsureActiveLog(forceCatalogRefresh: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WarnThrottled(
                "icue.sensor-log.read",
                ErrorLogThrottleInterval,
                "Could not read appended iCUE Sensor Logging samples.",
                ex);
        }
    }


    private void TrimOversizedPendingText()
    {
        if (_pendingText.Length <= MaximumPendingTextChars)
            return;

        _log.WarnThrottled(
            "icue.sensor-log.pending-overflow",
            ErrorLogThrottleInterval,
            $"Discarded an unterminated iCUE sensor-log fragment larger than {MaximumPendingTextChars} characters.");
        _pendingText = string.Empty;
    }

    private void ApplySample(IReadOnlyList<string> fields, DateTime sampleWriteUtc)
    {
        if (fields.Count == 0)
            return;

        var applied = false;
        foreach (var entry in _catalog.Values)
        {
            if (entry.ColumnIndex >= fields.Count)
                continue;

            _latestValues[entry.MetricId] = ParseMetricValue(fields[entry.ColumnIndex]);
            applied = true;
        }

        if (applied)
            _lastSampleWriteUtc = sampleWriteUtc;
    }

    private static object? ParseMetricValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.Trim();
        var length = 0;
        while (length < value.Length)
        {
            var ch = value[length];
            if (char.IsDigit(ch) || ch is '+' or '-' or '.' or ',' or 'e' or 'E')
            {
                length++;
                continue;
            }
            break;
        }

        if (length == 0)
            return null;

        var numericText = value[..length];
        if (numericText.Contains(",", StringComparison.Ordinal) &&
            !numericText.Contains(".", StringComparison.Ordinal))
        {
            numericText = numericText.Replace(',', '.');
        }

        return double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }

            if (ch == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            field.Append(ch);
        }

        fields.Add(field.ToString());
        return fields;
    }

    private void CleanupOldLogs(string activePath)
    {
        try
        {
            var files = Directory.EnumerateFiles(_logDirectory, LogFilePattern, SearchOption.TopDirectoryOnly)
                .Select(path => new { Path = path, LastWriteUtc = GetLastWriteTimeUtcSafe(path) })
                .OrderByDescending(item => item.LastWriteUtc)
                .ThenByDescending(item => Path.GetFileName(item.Path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { activePath };
            foreach (var file in files)
            {
                if (retained.Count >= RetainedLogFileCount)
                    break;
                retained.Add(file.Path);
            }

            var cutoff = DateTime.UtcNow - CleanupMinimumAge;
            foreach (var file in files)
            {
                if (retained.Contains(file.Path) || file.LastWriteUtc >= cutoff)
                    continue;

                try
                {
                    File.Delete(file.Path);
                    _log.Info($"Deleted old iCUE Sensor Logging CSV '{Path.GetFileName(file.Path)}'.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.WarnThrottled(
                        $"icue.sensor-log.cleanup.{Path.GetFileName(file.Path)}",
                        ErrorLogThrottleInterval,
                        $"Could not delete old iCUE Sensor Logging CSV '{file.Path}'.",
                        ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WarnThrottled(
                "icue.sensor-log.cleanup",
                ErrorLogThrottleInterval,
                "Could not clean up old iCUE Sensor Logging CSV files.",
                ex);
        }
    }
}
