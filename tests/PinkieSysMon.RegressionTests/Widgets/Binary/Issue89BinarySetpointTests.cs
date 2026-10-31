using System.ComponentModel;
using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue89BinarySetpointTests
{
    public static void NumericPatterns()
    {
        Expect(5, "5", "=", true);
        Expect(6, "5|6|7", "=", true);
        Expect(6, "5 | 6 | 7", "=", true);
        Expect(6, "5|6 | 7", "=", true);
        Expect(6, "5 |6|7", "=", true);
        Expect(8, "5|6|7", "=", false);
        Expect(5, "  5  ", "=", true);
        Expect(1234, "1,234", "=", true);
        Expect(1234, "1,234 | 5", "=", true);
        Expect(5, "1,234 | 5", "=", true);
        Expect(234, "1,234 | 5", "=", false);
        Expect(0.001, "1e-3", "=", true);
        Expect(-5, "-5", "=", true);
        Expect(-4, "-5--3", "=", true);
        Expect(-4, " -5 - -3 ", "=", true);
        Expect(0.005, "1e-3-1e-2", "=", true);
        Expect(1500, "1,000-2,000", "=", true);

        Expect(5, "5-7", "=", true);
        Expect(7, "5-7", "=", true);
        Expect(6.5, "5-7", "=", true);
        Expect(7.01, "5-7", "=", false);
        Expect(3, "3|5-7", "=", true);
        Expect(6.5, "3|5-7", "=", true);
        Expect(8, "2-4 | 7-9", "=", true);
        Expect(2, " 2 - 4|7 - 9 ", "=", true);
        Expect(5, "2-4 | 7-9", "=", false);

        // Relational operators mean existence of a matching reference value
        // anywhere in each interval, not comparison against every endpoint.
        Expect(6, "5-7", ">", true);
        Expect(5, "5-7", ">", false);
        Expect(6, "5-7", "<", true);
        Expect(7, "5-7", "<", false);
        Expect(5, "5-7", ">=", true);
        Expect(4.9, "5-7", ">=", false);
        Expect(7, "5-7", "<=", true);
        Expect(7.1, "5-7", "<=", false);
        Expect(6, "5-7", "=", true);
        Expect(8, "5-7", "=", false);
        Expect(6, "5-7", "!=", true);
        Expect(5, "5-7", "!=", true);
        Expect(5, "5-5", "!=", false);
        Expect(6, "5-5", "!=", true);
        Expect(5, "5|6", "!=", true);
        Expect(6, "5|6", "!=", true);
        Expect(5, "5|6", ">", false);
        Expect(6, "5|10", "<", true);
    }

    public static void InvalidPatterns()
    {
        foreach (var pattern in new[]
        {
            "", " ", "5||7", "|5", "5|", "5 |  | 7", "5|oops",
            "5-", "-5-", "5-7-9", "7-5", "-3--5",
            "NaN", "Infinity", "1e309", "5|Infinity",
            "5-1e309", "1e309-5", "1..2", "1:2"
        })
        {
            Unavailable(5d, pattern, "=", "Malformed term must remain unavailable.");
        }

        Unavailable(5d, "5", "==", "Unsupported operator must be unavailable.");
        Unavailable(null, "5", "=", "Missing reading must be unavailable.");
        Unavailable(double.NaN, "5", "=", "NaN reading must be unavailable.");
        Unavailable(double.PositiveInfinity, "5-7", "=", "Infinite reading must be unavailable.");
        Unavailable(new object(), "5", "=", "Unsupported reading type must be unavailable.");

        // Auto, Boolean and text retain their original contracts and do not
        // apply the pipe/range parser.
        Check(BinarySignalContract.TryResolve(0d, "Auto", "invalid|", "=", out var auto) && !auto,
            "Auto must ignore Setpoint.");
        Check(BinarySignalContract.TryResolve(true, "Setpoint", "true|false", "=", out var boolean) && !boolean,
            "Boolean Setpoint must not interpret pipes as alternatives.");
        Check(BinarySignalContract.TryResolve("5|7", "Setpoint", "5|7", "=", out var text) && text,
            "Text Setpoint must compare the literal string.");
        Check(BinarySignalContract.TryResolve("5", "Setpoint", "5|7", "=", out text) && !text,
            "Text Setpoint must not split the literal string.");
        Unavailable("undefined", "undefined", "=", "Unavailable text must stay unavailable.");
    }

    public static void EditorAndSerialization()
    {
        var widget = CreateWidget();
        var fields = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var evaluation = fields["Evaluation"]?.GetValue(null)
            ?? throw new InvalidOperationException("Binary Evaluation group is missing.");
        var setpoint = TypeDescriptor.GetProperties(evaluation)[nameof(C.BinaryWidgetDefinition.Setpoint)]
            ?? throw new InvalidOperationException("Binary Setpoint field is missing.");
        Check(!setpoint.IsReadOnly, "Setpoint text field must remain editable.");
        const string expression = " 2 - 4|7 - 9 ";
        setpoint.SetValue(evaluation, expression);
        Check(widget.Setpoint == expression, "Editor must preserve the original expression verbatim.");

        foreach (var profile in widget.Profiles.Values)
            profile.Asset.Source = null;
        var definition = new C.DashboardDefinition
        {
            Widgets = [widget],
            BaseDirectory = Path.GetTempPath()
        };
        var filePath = Path.Combine(Path.GetTempPath(), "issue89-binary-setpoint.json");
        var json = DashboardJson.Serialize(definition, indented: false);
        var parsed = DashboardJson.ParseCurrent(json, filePath);
        var persisted = (C.BinaryWidgetDefinition)parsed.Widgets[0];
        Check(persisted.Setpoint == expression, "JSON must retain the unchanged setpoint string.");
        var savedAgain = DashboardJson.Serialize(parsed, indented: false);
        Check(json == savedAgain, "Repeated parse/save must be idempotent without schema migration.");
    }

    public static void RenderContextAndLegacyTypes()
    {
        const string metric = "issue89.bytes";
        MetricContract.ReplaceProviderDescriptors("issue89", [
            new MetricDescriptor(metric, MetricValueKind.Number, MetricUnit.BytesPerSecond)
        ]);
        var widget = CreateWidget();
        widget.Metric = metric;
        widget.Unit = "Mbit/s";
        widget.Setpoint = "900-1,100";
        using var typefaces = new TypefaceCache(
            AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [metric] = 125_000_000d },
            antialias: false, typefaces);
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "Numeric ranges must compare in the widget-selected unit.");
        widget.Setpoint = "1,200-1,300";
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "A valid range that misses must display False.");

        widget.Metric = SystemMetricContract.DateTime;
        widget.Component = "minute";
        widget.Unit = null;
        widget.Setpoint = "12|58-59";
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2026, 10, 19, 12, 59, 0)
        });
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "DateTime Component projection must support numeric ranges.");
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2026, 10, 19, 12, 12, 0)
        });
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "DateTime Component projection must support separate alternatives.");
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2026, 10, 19, 12, 22, 0)
        });
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "A well-formed nonmatching DateTime component must be False.");
        widget.Component = null;
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "DateTime without Component must be unavailable.");
        widget.Component = "minute";
        context.UpdateMetrics(new Dictionary<string, object?>());
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "Missing metric must not select the False profile.");
    }

    private static C.BinaryWidgetDefinition CreateWidget()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "issue89-binary",
            Metric = SystemMetricContract.Uptime,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            TrueIf = BinarySignalContract.TrueIfEqual,
            Setpoint = "5",
            Width = 96,
            Height = 96
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        return widget;
    }

    private static void Expect(double value, string pattern, string op, bool expected)
    {
        Check(BinarySignalContract.TryResolve(value, "Setpoint", pattern, op, out var actual) &&
              actual == expected,
            $"Expected {value} {op} [{pattern}] to resolve {expected}.");
    }

    private static void Unavailable(object? value, string? pattern, string op, string message) =>
        Check(!BinarySignalContract.TryResolve(value, "Setpoint", pattern, op, out _), message + " " + pattern);

    private static void CheckState(WidgetRenderContext context, C.BinaryWidgetDefinition widget, string expected, string message)
    {
        Check(context.TryResolveBinaryVisualProfile(widget, out var actual, out _) && actual == expected, message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
