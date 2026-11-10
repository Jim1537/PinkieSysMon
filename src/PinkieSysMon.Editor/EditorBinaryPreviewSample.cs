using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

// Applied to a render-time copy, never to the telemetry store or dashboard definition.
internal enum BinaryPreviewSampleMode { Off, Text, Unavailable }

internal static class EditorBinaryPreviewSample
{
    public static bool Supports(C.BinaryWidgetDefinition? widget) =>
        widget is not null &&
        string.Equals(widget.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase) &&
        MetricContract.GetDescriptor(widget.Metric)?.ValueKind == MetricValueKind.Text;

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
