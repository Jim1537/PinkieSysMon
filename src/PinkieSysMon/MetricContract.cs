namespace PinkieSysMon;

internal enum MetricValueKind
{
    Number,
    Percent,
    Boolean,
    Duration,
    DateTime,
    DataSize,
    Text
}

internal enum MetricUnit
{
    None,
    Milliseconds,
    Seconds,
    Minutes,
    Hours,
    Celsius,
    Fahrenheit,
    Watts,
    Volts,
    Amperes,
    Hertz,
    Kilohertz,
    Megahertz,
    Gigahertz,
    RevolutionsPerMinute,
    LitersPerHour,
    Nanoseconds,
    MilliWattHours,
    DecibelsA,
    MicroSiemensPerCentimeter,
    FramesPerSecond,
    Bytes,
    Kilobytes,
    Megabytes,
    Gigabytes,
    Terabytes,
    Kibibytes,
    Mebibytes,
    Gibibytes,
    Tebibytes,
    BytesPerSecond,
    KilobytesPerSecond,
    MegabytesPerSecond,
    GigabytesPerSecond,
    TerabytesPerSecond,
    KibibytesPerSecond,
    MebibytesPerSecond,
    GibibytesPerSecond,
    TebibytesPerSecond,
    BitsPerSecond,
    KilobitsPerSecond,
    MegabitsPerSecond,
    GigabitsPerSecond,
    TerabitsPerSecond
}

internal sealed record MetricPresentationOption(string? Token, string Label);

internal sealed record MetricDescriptor(
    string Id,
    MetricValueKind ValueKind,
    MetricUnit BaseUnit);

/// <summary>
/// Provider-qualified metric metadata. Static system metrics and dynamic provider-native
/// metrics share the same lookup path; providers own their namespace roots.
/// </summary>
internal static class MetricContract
{
    public const int DashboardSchemaVersion = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;

    public static readonly IReadOnlyList<MetricPresentationOption> NumericFormats =
    [
        new(null, "raw"),
        new("0", "0"),
        new("0.0", "0.0"),
        new("0.00", "0.00")
    ];

    private static readonly IReadOnlyList<MetricPresentationOption> DurationFormats =
    [
        .. NumericFormats,
        new("h:mm", "h:mm"),
        new("h:mm:ss", "h:mm:ss")
    ];

    public static readonly IReadOnlyList<MetricPresentationOption> DurationUnits =
    [
        new(null, "raw"),
        new("auto", "auto"),
        new("seconds", "seconds"),
        new("minutes", "minutes"),
        new("hours", "hours")
    ];

    public static readonly IReadOnlyList<MetricPresentationOption> DataSizeUnits =
    [
        new(null, "raw"),
        new("B", "B"),
        new("KB", "KB"),
        new("MB", "MB"),
        new("GB", "GB"),
        new("TB", "TB"),
        new("KiB", "KiB"),
        new("MiB", "MiB"),
        new("GiB", "GiB"),
        new("TiB", "TiB")
    ];

    private static readonly IReadOnlyList<MetricPresentationOption> TemperatureUnits =
    [
        new(null, "raw"),
        new("C", "C"),
        new("F", "F")
    ];

    private static readonly IReadOnlyList<MetricPresentationOption> FrequencyUnits =
    [
        new(null, "raw"),
        new("Hz", "Hz"),
        new("kHz", "kHz"),
        new("MHz", "MHz"),
        new("GHz", "GHz")
    ];

    private static readonly IReadOnlyList<MetricPresentationOption> DataRateUnits =
    [
        new(null, "raw"),
        new("B/s", "B/s (bytes/s)"),
        new("KB/s", "KB/s (10^3 bytes/s)"),
        new("MB/s", "MB/s (10^6 bytes/s)"),
        new("GB/s", "GB/s (10^9 bytes/s)"),
        new("TB/s", "TB/s (10^12 bytes/s)"),
        new("KiB/s", "KiB/s (2^10 bytes/s)"),
        new("MiB/s", "MiB/s (2^20 bytes/s)"),
        new("GiB/s", "GiB/s (2^30 bytes/s)"),
        new("TiB/s", "TiB/s (2^40 bytes/s)"),
        new("bit/s", "bit/s"),
        new("kbit/s", "kbit/s (10^3 bits/s)"),
        new("Mbit/s", "Mbit/s (10^6 bits/s)"),
        new("Gbit/s", "Gbit/s (10^9 bits/s)"),
        new("Tbit/s", "Tbit/s (10^12 bits/s)")
    ];

    private static readonly object DescriptorSync = new();
    private static readonly Dictionary<string, MetricDescriptor> StaticDescriptorIndex =
        BuildStaticDescriptors().ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, MetricDescriptor>> DynamicProviderDescriptors =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, MetricDescriptor> _descriptorIndex =
        new(StaticDescriptorIndex, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<MetricDescriptor> Descriptors =>
        Volatile.Read(ref _descriptorIndex).Values.ToArray();

    public static MetricDescriptor? GetDescriptor(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var index = Volatile.Read(ref _descriptorIndex);
        return index.TryGetValue(id, out var descriptor) ? descriptor : null;
    }

    public static void ReplaceProviderDescriptors(string providerId, IEnumerable<MetricDescriptor> descriptors)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(providerId));

        var normalizedProviderId = providerId.Trim();
        var prefix = normalizedProviderId + ".";
        var providerIndex = new Dictionary<string, MetricDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in descriptors)
        {
            if (!descriptor.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Dynamic metric '{descriptor.Id}' does not belong to provider namespace '{normalizedProviderId}.*'.");
            if (!providerIndex.TryAdd(descriptor.Id, descriptor))
                throw new InvalidOperationException($"Dynamic provider '{normalizedProviderId}' contains duplicate metric '{descriptor.Id}'.");
        }

        lock (DescriptorSync)
        {
            DynamicProviderDescriptors[normalizedProviderId] = providerIndex;
            var merged = new Dictionary<string, MetricDescriptor>(StaticDescriptorIndex, StringComparer.OrdinalIgnoreCase);
            foreach (var (dynamicProviderId, dynamicDescriptors) in DynamicProviderDescriptors)
            {
                foreach (var pair in dynamicDescriptors)
                {
                    if (!merged.TryAdd(pair.Key, pair.Value))
                    {
                        throw new InvalidOperationException(
                            $"Metric '{pair.Key}' from provider '{dynamicProviderId}' collides with another metric descriptor.");
                    }
                }
            }

            Volatile.Write(ref _descriptorIndex, merged);
        }
    }

    public static IReadOnlyList<MetricPresentationOption> GetFormats(MetricDescriptor descriptor) =>
        descriptor.ValueKind switch
        {
            MetricValueKind.Number or MetricValueKind.Percent or MetricValueKind.DataSize => NumericFormats,
            MetricValueKind.Duration => DurationFormats,
            _ => []
        };

    public static IReadOnlyList<MetricPresentationOption> GetUnits(MetricDescriptor descriptor)
    {
        if (descriptor.ValueKind == MetricValueKind.Duration)
            return DurationUnits;
        if (descriptor.ValueKind == MetricValueKind.DataSize)
            return DataSizeUnits;

        return descriptor.BaseUnit switch
        {
            MetricUnit.Celsius or MetricUnit.Fahrenheit => TemperatureUnits,
            MetricUnit.Hertz or MetricUnit.Kilohertz or MetricUnit.Megahertz or MetricUnit.Gigahertz => FrequencyUnits,
            MetricUnit.BytesPerSecond or MetricUnit.KilobytesPerSecond or MetricUnit.MegabytesPerSecond or
                MetricUnit.GigabytesPerSecond or MetricUnit.TerabytesPerSecond or MetricUnit.KibibytesPerSecond or
                MetricUnit.MebibytesPerSecond or MetricUnit.GibibytesPerSecond or MetricUnit.TebibytesPerSecond or
                MetricUnit.BitsPerSecond or MetricUnit.KilobitsPerSecond or MetricUnit.MegabitsPerSecond or
                MetricUnit.GigabitsPerSecond or MetricUnit.TerabitsPerSecond => DataRateUnits,
            _ => []
        };
    }

    public static bool IsNumericValueKind(MetricValueKind kind) =>
        kind is MetricValueKind.Number or
            MetricValueKind.Percent or
            MetricValueKind.Duration or
            MetricValueKind.DataSize;

    /// <summary>
    /// Units that preserve a numeric value and are therefore valid for quantitative widgets.
    /// Duration "auto" is intentionally excluded because it formats to text rather than a number.
    /// </summary>
    public static IReadOnlyList<MetricPresentationOption> GetNumericUnits(MetricDescriptor descriptor)
    {
        if (!IsNumericValueKind(descriptor.ValueKind))
            return [];

        return GetUnits(descriptor)
            .Where(option => !string.Equals(option.Token, "auto", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string GetSourceUnitLabel(MetricDescriptor descriptor)
    {
        if (descriptor.ValueKind == MetricValueKind.Percent)
            return "%";

        return descriptor.BaseUnit switch
        {
            MetricUnit.None => "raw",
            MetricUnit.Milliseconds => "milliseconds",
            MetricUnit.Seconds => "seconds",
            MetricUnit.Minutes => "minutes",
            MetricUnit.Hours => "hours",
            MetricUnit.Celsius => "C",
            MetricUnit.Fahrenheit => "F",
            MetricUnit.Watts => "W",
            MetricUnit.Volts => "V",
            MetricUnit.Amperes => "A",
            MetricUnit.Hertz => "Hz",
            MetricUnit.Kilohertz => "kHz",
            MetricUnit.Megahertz => "MHz",
            MetricUnit.Gigahertz => "GHz",
            MetricUnit.RevolutionsPerMinute => "RPM",
            MetricUnit.LitersPerHour => "L/h",
            MetricUnit.Nanoseconds => "ns",
            MetricUnit.MilliWattHours => "mWh",
            MetricUnit.DecibelsA => "dBA",
            MetricUnit.MicroSiemensPerCentimeter => "µS/cm",
            MetricUnit.FramesPerSecond => "FPS",
            MetricUnit.Bytes => "B",
            MetricUnit.Kilobytes => "KB",
            MetricUnit.Megabytes => "MB",
            MetricUnit.Gigabytes => "GB",
            MetricUnit.Terabytes => "TB",
            MetricUnit.Kibibytes => "KiB",
            MetricUnit.Mebibytes => "MiB",
            MetricUnit.Gibibytes => "GiB",
            MetricUnit.Tebibytes => "TiB",
            MetricUnit.BytesPerSecond => "B/s",
            MetricUnit.KilobytesPerSecond => "KB/s",
            MetricUnit.MegabytesPerSecond => "MB/s",
            MetricUnit.GigabytesPerSecond => "GB/s",
            MetricUnit.TerabytesPerSecond => "TB/s",
            MetricUnit.KibibytesPerSecond => "KiB/s",
            MetricUnit.MebibytesPerSecond => "MiB/s",
            MetricUnit.GibibytesPerSecond => "GiB/s",
            MetricUnit.TebibytesPerSecond => "TiB/s",
            MetricUnit.BitsPerSecond => "bit/s",
            MetricUnit.KilobitsPerSecond => "kbit/s",
            MetricUnit.MegabitsPerSecond => "Mbit/s",
            MetricUnit.GigabitsPerSecond => "Gbit/s",
            MetricUnit.TerabitsPerSecond => "Tbit/s",
            _ => descriptor.BaseUnit.ToString()
        };
    }

    public static string? NormalizeFormatForMetric(string? metricId, string? format)
    {
        var descriptor = GetDescriptor(metricId);
        var normalized = NormalizeToken(format);
        if (descriptor is null)
            return normalized;

        if (descriptor.ValueKind == MetricValueKind.DateTime)
            return normalized;
        if (descriptor.ValueKind is MetricValueKind.Text or MetricValueKind.Boolean)
            return null;

        return GetFormats(descriptor).FirstOrDefault(x => TokenEquals(x.Token, normalized))?.Token;
    }

    public static string? NormalizeUnitForMetric(string? metricId, string? unit)
    {
        var descriptor = GetDescriptor(metricId);
        var normalized = NormalizeToken(unit);
        if (descriptor is null)
            return normalized;

        return GetUnits(descriptor).FirstOrDefault(x => TokenEquals(x.Token, normalized))?.Token;
    }

    public static string? NormalizeNumericUnitForMetric(string? metricId, string? unit)
    {
        var descriptor = GetDescriptor(metricId);
        var normalized = NormalizeToken(unit);
        if (descriptor is null)
            return normalized;

        return GetNumericUnits(descriptor).FirstOrDefault(x => TokenEquals(x.Token, normalized))?.Token;
    }

    private static bool TokenEquals(string? left, string? right) =>
        string.Equals(NormalizeToken(left), NormalizeToken(right), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) || token.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : token.Trim();

    private static IEnumerable<MetricDescriptor> BuildStaticDescriptors()
    {
        foreach (var descriptor in SystemMetricContract.Descriptors)
            yield return descriptor;

        foreach (var descriptor in NetworkMetricContract.Descriptors)
            yield return descriptor;

        foreach (var descriptor in PowerMetricContract.Descriptors)
            yield return descriptor;

        foreach (var descriptor in MediaMetricContract.Descriptors)
            yield return descriptor;

        foreach (var descriptor in RuntimeMetricContract.Descriptors)
            yield return descriptor;
    }
}
