namespace PinkieSysMon;

// Application identity is global. All per-session counters live in
// OutputMetricContract; the former global device statistics are retired.
internal static class RuntimeMetricContract
{
    public const string Version = "system.runtime.version";
    public static readonly MetricDescriptor[] Descriptors =
    [
        new(Version, MetricValueKind.Text, MetricUnit.None)
    ];
}
