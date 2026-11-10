using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue110BinaryPreviewTests
{
    public static void EditorOnlyTextSampleEvaluation()
    {
        const string metric = "issue110.status";
        MetricContract.ReplaceProviderDescriptors("issue110",
            [new MetricDescriptor(metric, MetricValueKind.Text, MetricUnit.None)]);
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "issue110-binary",
            Metric = metric,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            Setpoint = "CONNECTED",
            Width = 96,
            Height = 96
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        Check(EditorBinaryPreviewSample.Supports(widget), "Text Setpoint should support preview input.");
        using var typefaces = new TypefaceCache(AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        var original = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metric] = "EDITOR", ["issue110.other"] = "ORIGINAL"
        };
        using var context = new WidgetRenderContext(original, antialias: false, typefaces);
        var match = EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, "CONNECTED");
        Expect(context, widget, match, BinarySignalContract.TrueKey);
        Check((string?)original[metric] == "EDITOR", "Source snapshot changed.");
        Check((string?)match["issue110.other"] == "ORIGINAL", "Unrelated metric changed.");
        Expect(context, widget, EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, "WAITING"),
            BinarySignalContract.FalseKey);
        Expect(context, widget, EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, "Connected"),
            BinarySignalContract.FalseKey);
        widget.Setpoint = "WAITING";
        Expect(context, widget, EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, "WAITING"),
            BinarySignalContract.TrueKey);
        widget.Setpoint = "CONNECTED";

        foreach (var value in new[] { "nil", "undefined" })
        {
            context.UpdateMetrics(EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, value));
            Check(!context.TryResolveBinaryVisualProfile(widget, out _), "Reserved value must be unavailable.");
        }
        context.UpdateMetrics(EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Unavailable, null));
        Check(!context.TryResolveBinaryVisualProfile(widget, out _), "Missing reading must be unavailable.");
        context.UpdateMetrics(EditorBinaryPreviewSample.Apply(
            new Dictionary<string, object?>(), metric, BinaryPreviewSampleMode.Text, "CONNECTED"));
        Check(context.TryResolveBinaryVisualProfile(widget, out var state, out _) && state == BinarySignalContract.TrueKey,
            "Explicit sample must work with an empty/provider-disabled fixture.");
        Expect(context, widget, EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Off, "CONNECTED"),
            BinarySignalContract.FalseKey);

        widget.EvaluationMode = BinarySignalContract.EvaluationModeAuto;
        Check(!EditorBinaryPreviewSample.Supports(widget), "Auto must not expose text Setpoint input.");
        widget.EvaluationMode = BinarySignalContract.EvaluationModeSetpoint;
        widget.Metric = SystemMetricContract.Uptime;
        Check(!EditorBinaryPreviewSample.Supports(widget), "Numeric metric must not expose text input.");
        var board = new C.DashboardDefinition { Widgets = [widget], BaseDirectory = AppContext.BaseDirectory };
        var before = DashboardJson.SerializeEditorSnapshot(board);
        _ = EditorBinaryPreviewSample.Apply(original, metric, BinaryPreviewSampleMode.Text, "CONNECTED");
        Check(before == DashboardJson.SerializeEditorSnapshot(board), "Sample changed persisted widget definition.");
    }

    private static void Expect(WidgetRenderContext context, C.BinaryWidgetDefinition widget,
        IReadOnlyDictionary<string, object?> metrics, string expected)
    {
        context.UpdateMetrics(metrics);
        Check(context.TryResolveBinaryVisualProfile(widget, out var state, out _) && state == expected,
            $"Expected Binary preview state '{expected}'.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
