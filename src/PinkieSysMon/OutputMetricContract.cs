namespace PinkieSysMon;

// Stable per-Output metric identity. The ID is the immutable OutputTargetConfig.Id;
// physical-only fields are deliberately absent for a Desktop board.
internal static class OutputMetricContract
{
    public const string Prefix = "system.output.";
    public const string FrameCount = "frame";
    public const string RenderDuration = "render.duration";
    public const string FrameDuration = "frame.duration";
    public const string Fps = "fps";
    public const string EncodeDuration = "encode.duration";
    public const string UsbDuration = "usb.duration";
    public const string JpegSize = "jpeg.size";
    public const string UsbState = "usb";
    public const string Status = "status";

    private static readonly (string Suffix, MetricValueKind Kind, MetricUnit Unit)[] Common =
    [
        (FrameCount, MetricValueKind.Number, MetricUnit.None),
        (RenderDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        (FrameDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        (Fps, MetricValueKind.Number, MetricUnit.FramesPerSecond),
        (Status, MetricValueKind.Text, MetricUnit.None)
    ];

    private static readonly (string Suffix, MetricValueKind Kind, MetricUnit Unit)[] Physical =
    [
        (EncodeDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        (UsbDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        (JpegSize, MetricValueKind.DataSize, MetricUnit.Bytes),
        (UsbState, MetricValueKind.Text, MetricUnit.None)
    ];

    // Dot-delimited IDs are valid: the fixed metric suffix is never inferred from
    // string splitting, and each ID's full prefix is used when building names.
    public static string Id(string outputId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(outputId))
            throw new ArgumentException("Output ID must not be empty.", nameof(outputId));
        if (!Common.Any(x => x.Suffix == suffix) && !Physical.Any(x => x.Suffix == suffix))
            throw new ArgumentException($"Unknown Output metric suffix '{suffix}'.", nameof(suffix));
        return Prefix + outputId + "." + suffix;
    }

    public static MetricDescriptor[] DescriptorsFor(OutputTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var fields = target.Kind == OutputTargetContract.TrofeoKind
            ? Common.Concat(Physical) : Common.AsEnumerable();
        return fields.Select(field => new MetricDescriptor(
            Id(target.Id, field.Suffix), field.Kind, field.Unit)).ToArray();
    }

    public static IReadOnlyDictionary<string, object?> Unavailable(OutputTargetConfig target) =>
        DescriptorsFor(target).ToDictionary(
            descriptor => descriptor.Id, _ => (object?)null,
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, object?> Unavailable(string outputId, bool isPhysical) =>
        Unavailable(new OutputTargetConfig
        {
            Id = outputId,
            Kind = isPhysical ? OutputTargetContract.TrofeoKind : OutputTargetContract.DesktopKind
        });

    public static MetricDescriptor? DescribeKnownId(string id)
    {
        if (!id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var field in Common.Concat(Physical))
        {
            var tail = "." + field.Suffix;
            if (!id.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                continue;
            var idLength = id.Length - Prefix.Length - tail.Length;
            if (idLength <= 0)
                continue;
            return new MetricDescriptor(id, field.Kind, field.Unit);
        }
        return null;
    }
}
