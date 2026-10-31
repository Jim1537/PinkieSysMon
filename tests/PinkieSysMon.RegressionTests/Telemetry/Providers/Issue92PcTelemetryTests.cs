using PinkieSysMon;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue92PcTelemetryTests
{
    private static readonly string[] PcIds =
    [
        SystemMetricContract.PcName,
        SystemMetricContract.PcProcessor,
        SystemMetricContract.PcRam,
        SystemMetricContract.PcType,
        SystemMetricContract.PcManufacturer,
        SystemMetricContract.PcModel
    ];

    public static void CatalogAndSelector()
    {
        Check(PcIds.Length == 6 && PcIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6,
            "PC must expose exactly six distinct stable metric IDs.");
        foreach (var id in PcIds)
        {
            var descriptor = MetricContract.GetDescriptor(id);
            Check(descriptor is not null &&
                  descriptor.ValueKind == (id == SystemMetricContract.PcRam
                      ? MetricValueKind.DataSize : MetricValueKind.Text) &&
                  descriptor.BaseUnit == (id == SystemMetricContract.PcRam
                      ? MetricUnit.Bytes : MetricUnit.None),
                "PC metric descriptors must preserve the approved types and base units.");
        }

        Check(MetricContract.GetDescriptor(SystemMetricContract.Edition)?.ValueKind == MetricValueKind.Text &&
              MetricContract.GetDescriptor(SystemMetricContract.Installed)?.ValueKind == MetricValueKind.DateTime &&
              MetricContract.GetDescriptor(SystemMetricContract.IntegratedGpuLoad)?.ValueKind == MetricValueKind.Percent,
            "Existing System metric contracts must remain intact.");
        Check(MetricProviderContract.Descriptors.Count(d => d.Id == MetricProviderContract.System) == 1,
            "PC is not a separate provider.");

        WithTemporaryLog((root, log) =>
        {
            var config = new AppConfig();
            config.MetricProviders[MetricProviderContract.LibreHardwareMonitor] = false;
            config.MetricProviders[MetricProviderContract.Icue] = false;
            MetricSelectorEnvironment.Configure(root, log, () => config);
            var enabled = MetricSelectorEnvironment.BuildSnapshot(null);
            foreach (var id in PcIds)
            {
                var entry = enabled.Metrics.SingleOrDefault(metric => metric.Id == id);
                Check(entry is { Available: true } &&
                      entry.DisplayPath.Select(segment => segment.Text)
                          .SequenceEqual(new[] { "system", "pc", id["system.pc.".Length..] }),
                    "Every PC metric must appear under the existing System → pc selector hierarchy.");
            }

            config.MetricProviders[MetricProviderContract.System] = false;
            var disabled = MetricSelectorEnvironment.BuildSnapshot(null);
            Check(disabled.Metrics.All(metric => !PcIds.Contains(metric.Id, StringComparer.OrdinalIgnoreCase)),
                "Disabling the System provider must remove all PC metrics from the selector.");
        });
    }

    public static void ValueNormalization()
    {
        Check(WindowsSystemTelemetrySource.SumInstalledMemoryBytes(
                  new object?[] { 16UL * 1024 * 1024 * 1024, 8UL * 1024 * 1024 * 1024 })
                  == 24L * 1024 * 1024 * 1024,
            "Installed RAM must sum full physical-module byte capacities.");
        Check(WindowsSystemTelemetrySource.SumInstalledMemoryBytes(Array.Empty<object?>()) is null &&
              WindowsSystemTelemetrySource.SumInstalledMemoryBytes(new object?[] { 4096UL, null }) is null &&
              WindowsSystemTelemetrySource.SumInstalledMemoryBytes(new object?[] { 4096UL, "invalid" }) is null &&
              WindowsSystemTelemetrySource.SumInstalledMemoryBytes(new object?[] { long.MaxValue, 1UL }) is null,
            "Missing, corrupt and overflowing module data cannot become a partial RAM reading.");

        Check(WindowsSystemTelemetrySource.FormatSystemType((ushort)9, true) ==
                  "64-bit operating system, x64-based processor" &&
              WindowsSystemTelemetrySource.FormatSystemType((ushort)12, true) ==
                  "64-bit operating system, ARM64-based processor" &&
              WindowsSystemTelemetrySource.FormatSystemType((ushort)0, false) ==
                  "32-bit operating system, x86-based processor",
            "System type must distinguish Windows bitness and WMI processor architecture.");
        Check(WindowsSystemTelemetrySource.FormatSystemType(null, true) is null &&
              WindowsSystemTelemetrySource.FormatSystemType(ushort.MaxValue, true) is null,
            "Unknown architecture must never be guessed.");
    }

    public static void StaticSnapshotAndIsolation()
    {
        WithTemporaryLog((_, log) =>
        {
            var loaderCalls = 0;
            var staticMetrics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [SystemMetricContract.Edition] = "Synthetic Windows",
                [SystemMetricContract.PcName] = "EXAMPLE-PC",
                [SystemMetricContract.PcProcessor] = "Example CPU",
                [SystemMetricContract.PcRam] = 16L * 1024 * 1024 * 1024,
                [SystemMetricContract.PcType] = "64-bit operating system, x64-based processor",
                [SystemMetricContract.PcManufacturer] = null,
                [SystemMetricContract.PcModel] = "Example Model"
            };
            var source = new WindowsSystemTelemetrySource(log, () =>
            {
                loaderCalls++;
                return staticMetrics;
            });

            Check(source.ProviderId == MetricProviderContract.System &&
                  PcIds.All(id => source.MetricNames.Contains(id)) &&
                  MetricSourceCatalog.BuildMetricIndex(new IMetricSource[] { source }).Count ==
                      SystemMetricContract.MetricNames.Length,
                "New PC IDs must be claimed by the existing Windows System source.");
            var first = source.Capture(new[] { SystemMetricContract.PcName, SystemMetricContract.PcRam });
            var second = source.Capture(new[]
            {
                SystemMetricContract.PcManufacturer,
                SystemMetricContract.PcModel,
                SystemMetricContract.Edition,
                SystemMetricContract.Uptime
            });
            Check(loaderCalls == 1 &&
                  first.Count == 2 &&
                  Equals(first[SystemMetricContract.PcName], "EXAMPLE-PC") &&
                  first[SystemMetricContract.PcRam] is long bytes && bytes == 16L * 1024 * 1024 * 1024 &&
                  second.Count == 4 &&
                  second[SystemMetricContract.PcManufacturer] is null &&
                  Equals(second[SystemMetricContract.PcModel], "Example Model") &&
                  Equals(second[SystemMetricContract.Edition], "Synthetic Windows") &&
                  second[SystemMetricContract.Uptime] is long,
                "PC values must be cached once, requested on demand and isolated from unavailable peers.");
        });
    }

    private static void WithTemporaryLog(Action<string, FileLogger> run)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-issue92-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            run(root, new FileLogger(Path.Combine(root, "test.log")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
