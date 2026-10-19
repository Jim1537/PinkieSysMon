namespace PinkieSysMon;

internal static class SystemMetricContract
{
    public const string Edition = "system.os.edition";
    public const string Release = "system.os.release";
    public const string Build = "system.os.build";
    public const string Installed = "system.os.installed";
    public const string DateTime = "system.os.datetime";
    public const string Uptime = "system.os.uptime";
    public const string PcName = "system.pc.name";
    public const string PcProcessor = "system.pc.processor";
    public const string PcRam = "system.pc.ram";
    public const string PcType = "system.pc.type";
    public const string PcManufacturer = "system.pc.manufacturer";
    public const string PcModel = "system.pc.model";
    public const string IntegratedGpuLoad = "system.gpu.integrated.load";

    public static readonly string[] MetricNames =
    [
        Edition,
        Release,
        Build,
        Installed,
        DateTime,
        Uptime,
        PcName,
        PcProcessor,
        PcRam,
        PcType,
        PcManufacturer,
        PcModel,
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
        new(PcName, MetricValueKind.Text, MetricUnit.None),
        new(PcProcessor, MetricValueKind.Text, MetricUnit.None),
        new(PcRam, MetricValueKind.DataSize, MetricUnit.Bytes),
        new(PcType, MetricValueKind.Text, MetricUnit.None),
        new(PcManufacturer, MetricValueKind.Text, MetricUnit.None),
        new(PcModel, MetricValueKind.Text, MetricUnit.None),
        new(IntegratedGpuLoad, MetricValueKind.Percent, MetricUnit.None)
    ];
}
