namespace PinkieSysMon;

internal static class PowerMetricContract
{
    // Widget source identifiers are dashboard semantics, not telemetry metric IDs.
    public const string UpsSource = "power.ups";
    public const string BatterySource = "power.battery";

    public const string UpsMetricSource = "system.power.ups";
    public const string BatteryMetricSource = "system.power.battery";

    public const string StateUnavailable = "unavailable";
    public const string StateUnknown = "unknown";
    public const string StateOnline = "online";
    public const string StateOnBattery = "on-battery";
    public const string StateCharging = "charging";
    public const string StateLow = "low";
    public const string StateCritical = "critical";
    public const string StateFullyCharged = "fully-charged";
    public const string StateNormal = "normal";

    public const string UpsChargeMetric = UpsMetricSource + ".charge";
    public const string UpsRuntimeRemainingMetric = UpsMetricSource + ".runtime.remaining";
    public const string UpsStateMetric = UpsMetricSource + ".state";
    public const string BatteryChargeMetric = BatteryMetricSource + ".charge";
    public const string BatteryRuntimeRemainingMetric = BatteryMetricSource + ".runtime.remaining";
    public const string BatteryStateMetric = BatteryMetricSource + ".state";

    public static readonly MetricDescriptor[] Descriptors =
    [
        new(UpsChargeMetric, MetricValueKind.Percent, MetricUnit.None),
        new(UpsRuntimeRemainingMetric, MetricValueKind.Duration, MetricUnit.Seconds),
        new(UpsStateMetric, MetricValueKind.Text, MetricUnit.None),
        new(BatteryChargeMetric, MetricValueKind.Percent, MetricUnit.None),
        new(BatteryRuntimeRemainingMetric, MetricValueKind.Duration, MetricUnit.Seconds),
        new(BatteryStateMetric, MetricValueKind.Text, MetricUnit.None)
    ];

    public static readonly string[] MetricNames = Descriptors.Select(x => x.Id).ToArray();

    public static readonly string[] SupportedSources = [UpsSource, BatterySource];

    public static readonly string[] States =
    [
        StateOnline,
        StateOnBattery,
        StateCharging,
        StateLow,
        StateCritical,
        StateFullyCharged,
        StateNormal,
        StateUnknown,
        StateUnavailable
    ];

    public static string StateMetric(string source) =>
        string.Equals(source, BatterySource, StringComparison.OrdinalIgnoreCase)
            ? BatteryStateMetric
            : UpsStateMetric;

    public static bool IsSupportedSource(string? source) =>
        !string.IsNullOrWhiteSpace(source) &&
        SupportedSources.Contains(source.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string NormalizeState(object? raw)
    {
        var text = Convert.ToString(raw)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return StateUnavailable;

        return text.ToLowerInvariant() switch
        {
            "unavailable" => StateUnavailable,
            "unknown" => StateUnknown,
            "online" => StateOnline,
            "onbattery" or "on-battery" => StateOnBattery,
            "charging" => StateCharging,
            "low" => StateLow,
            "critical" => StateCritical,
            "fullycharged" or "fully-charged" => StateFullyCharged,
            "normal" => StateNormal,
            _ => StateUnknown
        };
    }

    public static string FromWmiBatteryStatus(ushort? status) => status switch
    {
        // Win32_Battery documents 1 as discharging and 2 as AC available / not discharging.
        1 => StateOnBattery,
        2 => StateOnline,
        3 => StateFullyCharged,
        4 => StateLow,
        5 => StateCritical,
        6 => StateCharging,
        7 => StateCharging,
        // Preserve the more urgent condition when WMI reports charging+low/critical.
        8 => StateLow,
        9 => StateCritical,
        // 10 represents an undefined/no-battery condition in the underlying DMI semantics.
        10 => StateUnavailable,
        11 => StateNormal,
        _ => StateUnknown
    };
}
