using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

// Applied to a render-time copy, never to the telemetry store or dashboard definition.
internal enum BinaryPreviewSampleMode { Off, Text, Unavailable }

internal static class EditorBinaryPreviewSample
{
    public static bool Supports(C.BinaryWidgetDefinition? widget)
    {
        if (widget is null ||
            !string.Equals(widget.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(widget.Metric))
            return false;

        // Output status is a known text contract even if its dynamic descriptor is
        // absent (e.g. an unregistered or provider-disabled Output in the Editor).
        var descriptor = MetricContract.GetDescriptor(widget.Metric) ??
                         OutputMetricContract.DescribeKnownId(widget.Metric);
        return descriptor?.ValueKind == MetricValueKind.Text;
    }

    public static IReadOnlyDictionary<string, object?> Apply(
        IReadOnlyDictionary<string, object?> snapshot,
        string? metric,
        BinaryPreviewSampleMode mode,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode == BinaryPreviewSampleMode.Off || string.IsNullOrWhiteSpace(metric))
            return snapshot;

        var preview = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in snapshot)
            preview[pair.Key] = pair.Value;

        switch (mode)
        {
            case BinaryPreviewSampleMode.Text:
                preview[metric] = text ?? string.Empty;
                break;
            case BinaryPreviewSampleMode.Unavailable:
                preview.Remove(metric);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown preview sample mode.");
        }

        return preview;
    }
}
