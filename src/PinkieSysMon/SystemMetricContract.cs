namespace PinkieSysMon;

internal static class SystemMetricContract
{
    public const string Edition = "system.os.edition";
    public const string Release = "system.os.release";
    public const string Build = "system.os.build";
    public const string Installed = "system.os.installed";
    public const string DateTime = "system.os.datetime";
    public const string Uptime = "system.os.uptime";
    public const string IntegratedGpuLoad = "system.gpu.integrated.load";

    public static readonly string[] MetricNames =
    [
        Edition,
        Release,
        Build,
        Installed,
        DateTime,
        Uptime,
        IntegratedGpuLoad
    ];

    public static readonly MetricDescriptor[] Descriptors =
    [
        new(Edition, MetricValueKind.Text, MetricUnit.None),
        new(Release, MetricValueKind.Text, MetricUnit.None),
        new(Build, MetricValueKind.Text, MetricUnit.None),
        new(Installed, MetricValueKind.DateTime, MetricUnit.None),
        new(DateTime, MetricValueKind.DateTime, MetricUnit.None),
        new(Uptime, MetricValueKind.Duration, MetricUnit.Seconds),
        new(IntegratedGpuLoad, MetricValueKind.Percent, MetricUnit.None)
    ];
}
