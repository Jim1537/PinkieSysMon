using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue107HourCycleTests
{
    public static void ProjectionAndBoundaries()
    {
        var cases = new (DateTime Time, double Whole, double Tenth, double Hundredth)[]
        {
            (new(2026, 11, 9, 0, 0, 0), 0, 0, 0),
            (new(2026, 11, 9, 0, 30, 0), 0, .5, .5),
            (new(2026, 11, 9, 0, 59, 59, 999), 0, .9, .99),
            (new(2026, 11, 9, 1, 0, 0), 1, 1, 1),
            (new(2026, 11, 9, 11, 30, 0), 11, 11.5, 11.5),
            (new(2026, 11, 9, 11, 59, 59, 999), 11, 11.9, 11.99),
            (new(2026, 11, 9, 12, 0, 0), 0, 0, 0),
            (new(2026, 11, 9, 12, 30, 0), 0, .5, .5),
            (new(2026, 11, 9, 12, 59, 59, 999), 0, .9, .99),
            (new(2026, 11, 9, 13, 0, 0), 1, 1, 1),
            (new(2026, 11, 9, 13, 30, 0), 1, 1.5, 1.5),
            (new(2026, 11, 9, 23, 30, 0), 11, 11.5, 11.5),
            (new(2026, 11, 9, 23, 59, 59, 999), 11, 11.9, 11.99),
            (new(2026, 11, 10, 0, 0, 0), 0, 0, 0)
        };
        foreach (var item in cases)
        {
            Expect(item.Time, "0", item.Whole);
            Expect(item.Time, "0.0", item.Tenth);
            Expect(item.Time, "0.00", item.Hundredth);
            Check(DateTimeComponentContract.TryProject(item.Time, "hour12Cycle", "0.00", out var actual) &&
                  actual >= 0 && actual < 12,
                "The cyclic hour must remain in [0,12) even immediately before rollover.");
        }
        var noon = new DateTime(2026, 11, 9, 12, 30, 0);
        Check(DateTimeComponentContract.TryProject(noon, "hour12", "0.0", out var old12) &&
              DateTimeComponentContract.TryProject(noon, "hour24", "0.0", out var old24) &&
              old12 == 12.5 && old24 == 12.5,
            "Legacy hour12/hour24 outputs must remain unchanged.");
        Check(DateTimeComponentContract.Options.Any(x =>
                  x.Token == "hour12Cycle" && x.Label == "Hour (12-cycle)") &&
              DateTimeComponentContract.NormalizeOrThrow(" Hour (12-cycle) ") == "hour12Cycle" &&
              DateTimeComponentContract.NormalizeOrThrow("hour12cycle") == "hour12Cycle" &&
              DateTimeComponentContract.GetLabel("hour12Cycle") == "Hour (12-cycle)",
            "New Editor label and token must normalize to one canonical choice.");
        DateTimeComponentContract.Validate("hour12Cycle", "Issue #107");
        var offset = new DateTimeOffset(2026, 11, 9, 12, 30, 0, TimeSpan.FromHours(-6));
        Check(DateTimeComponentContract.TryProject(offset, "hour12Cycle", "0.00", out var projected) &&
              DateTimeComponentContract.TryProject(offset.LocalDateTime, "hour12Cycle", "0.00", out var local) &&
              projected == local, "DateTimeOffset must preserve local-civil-time projection.");
        Check(!DateTimeComponentContract.TryProject(null, "hour12Cycle", "0.0", out _) &&
              !DateTimeComponentContract.TryProject("12:30", "hour12Cycle", "0.0", out _) &&
              !DateTimeComponentContract.TryProject(noon, null, "0.0", out _) &&
              !DateTimeComponentContract.TryProject(noon, "hour12Cycle", "raw", out _) &&
              !DateTimeComponentContract.TryProject(noon, "unknown", "0.0", out _),
            "Missing, malformed or unsupported components must remain unavailable.");
    }

    public static void EditorAndConsumers()
    {
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "cycle-gauge", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.00",
            Min = 0, Max = 12, StartAngle = 0, EndAngle = 360,
            Width = 200, Height = 200
        };
        var bar = new C.BarWidgetDefinition
        {
            Id = "cycle-bar", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.0", Width = 200, Height = 20
        };
        var graph = new C.GraphWidgetDefinition
        {
            Id = "cycle-graph", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.00",
            Min = 0, Max = 12, ScaleMode = "Fixed", Width = 200, Height = 100
        };
        var binary = MakeBinary();
        var now = new DateTime(2026, 11, 9, 12, 30, 0);
        using var fonts = new TypefaceCache(AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.DateTime] = now },
            antialias: false, fonts);
        Check(context.TryGetMetricDouble(gauge, out var hour) && hour == .5 &&
              context.TryGetMetricDouble(bar, out var barHour) && barHour == .5 &&
              context.TryGetMetricDouble(graph, out var graphHour) && graphHour == .5 &&
              context.TryGetMetricDouble(binary, out var binaryHour) && binaryHour == .5,
            "Gauge, Bar, Graph and Binary must use the shared cyclic projection.");
        Check(MetricValueConverter.TryResolveQuantitative(
                  now, MetricContract.GetDescriptor(SystemMetricContract.DateTime),
                  graph.Unit, graph.Component, graph.ComponentFormat, out var historical) &&
              historical == .5, "Graph history must use the same conversion contract.");
        Check(context.TryResolveBinaryVisualProfile(binary, out var state, out _) &&
              state == BinarySignalContract.TrueKey,
            "Binary setpoints must evaluate the numeric cyclic scalar.");

        var indicator = CompiledQuantitativeIndicator.Create(gauge, gauge.Color);
        Check(Math.Abs(indicator.GetRatio(hour) - .5 / 12d) < 1e-9,
            "Gauge 0..12 range must not clamp the 12 o'clock hour.");
        var oldGauge = new C.GaugeWidgetDefinition
        {
            Id = "old-hour", Metric = SystemMetricContract.DateTime,
            Component = "hour12", ComponentFormat = "0.00", Width = 200, Height = 200
        };
        Check(context.TryGetMetricDouble(oldGauge, out var oldHour) && oldHour == 12.5,
            "New component must not alias the legacy hour12 cache key.");
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2026, 11, 9, 11, 59, 59, 999)
        });
        Check(context.TryGetMetricDouble(gauge, out var before) && before == 11.99 &&
              indicator.GetRatio(before) > .99 && indicator.GetRatio(before) < 1,
            "The hand must approach one full revolution before noon.");
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2026, 11, 9, 12, 0, 0)
        });
        Check(context.TryGetMetricDouble(gauge, out var atNoon) && atNoon == 0 &&
              indicator.GetRatio(atNoon) == 0,
            "The hand must wrap to the same angular origin at noon.");
        binary.EvaluationMode = BinarySignalContract.EvaluationModeAuto;
        Check(context.TryResolveBinaryVisualProfile(binary, out state, out _) &&
              state == BinarySignalContract.FalseKey,
            "Binary Auto must treat the genuine zero at noon as False.");
        context.UpdateMetrics(new Dictionary<string, object?>());
        Check(!context.TryGetMetricDouble(gauge, out _) &&
              !context.TryResolveBinaryVisualProfile(binary, out _),
            "Missing telemetry must not manufacture a clock value.");

        var data = new EditorPropertyView(gauge, section: EditorPropertySection.Data).GetProperties();
        var value = data["Value"]!.GetValue(null)!;
        var field = TypeDescriptor.GetProperties(value)[nameof(C.NumericWidgetDefinition.Component)]!;
        var choices = field.Converter.GetStandardValues(null)!.Cast<string>().ToArray();
        Check(!field.IsReadOnly && field.Converter.GetStandardValuesExclusive(null) &&
              choices.Contains("Hour (12-cycle)") && choices.Contains("Hour (12-hour)") &&
              choices.Contains("Hour (24-hour)"),
            "Editor must offer the new choice without removing accepted choices.");
        gauge.Component = "hour12";
        field.SetValue(value, "Hour (12-cycle)");
        Check(gauge.Component == "hour12Cycle",
            "Editor must save the canonical token, not its display label.");
        foreach (var widget in new C.WidgetDefinition[] { bar, graph, binary })
        {
            var rows = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
            var group = rows["Value"]!.GetValue(null)!;
            var name = widget is C.BinaryWidgetDefinition
                ? nameof(C.BinaryWidgetDefinition.Component)
                : nameof(C.NumericWidgetDefinition.Component);
            var selector = TypeDescriptor.GetProperties(group)[name];
            Check(selector is { IsReadOnly: false } &&
                  selector.Converter.GetStandardValues(null)!.Cast<string>().Contains("Hour (12-cycle)"),
                $"Component choice unavailable in {widget.Type}.");
        }
    }

    public static void SchemaCompatibility()
    {
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "persisted-gauge", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.00",
            Min = 0, Max = 12, Width = 200, Height = 200
        };
        var bar = new C.BarWidgetDefinition
        {
            Id = "persisted-bar", Metric = SystemMetricContract.DateTime,
            Component = "hour12", Width = 200, Height = 20
        };
        var graph = new C.GraphWidgetDefinition
        {
            Id = "persisted-graph", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.0",
            Width = 200, Height = 100
        };
        var binary = MakeBinary();
        binary.Id = "persisted-binary";
        binary.Format = "HH:mm";
        var board = new C.DashboardDefinition
        {
            Widgets = [gauge, bar, graph, binary], BaseDirectory = Path.GetTempPath()
        };
        var path = Path.Combine(Path.GetTempPath(), "issue107-hour-cycle.json");
        var json = DashboardJson.Serialize(board, indented: false);
        var root = JsonNode.Parse(json)!.AsObject();
        Check(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "A dashboard with hour12Cycle must serialize using the current schema.");

        // Issue #107 introduced the token in schema 26. Exercise that file format independently
        // of the current writer so subsequent schema revisions still test the original contract.
        const string schema26 = """
            {"schemaVersion":26,"canvas":{"width":200,"height":200},"widgets":[
              {"type":"gauge","id":"legacy-clock","metric":"system.os.datetime","width":200,"height":200,
               "min":0,"max":12,"component":"hour12Cycle","componentFormat":"0.00"}]}
            """;
        var migrated = C.DashboardDefinition.Parse(schema26, path);
        Check(migrated.Widgets.Single() is C.GaugeWidgetDefinition
              { Component: "hour12Cycle", ComponentFormat: "0.00", Min: 0, Max: 12 },
            "Loading a schema-26 clock must preserve its cyclic hour, precision and range.");
        var migratedJson = DashboardJson.Serialize(migrated, indented: false);
        Check(DashboardJson.Serialize(DashboardJson.ParseCurrent(migratedJson, path), indented: false) == migratedJson,
            "The migrated clock must reopen in the current schema without further changes.");
        var reopened = DashboardJson.ParseCurrent(json, path);
        Check(reopened.Widgets[0] is C.GaugeWidgetDefinition { Component: "hour12Cycle", ComponentFormat: "0.00" } &&
              reopened.Widgets[1] is C.BarWidgetDefinition { Component: "hour12", ComponentFormat: null } &&
              reopened.Widgets[2] is C.GraphWidgetDefinition { Component: "hour12Cycle", ComponentFormat: "0.0" } &&
              reopened.Widgets[3] is C.BinaryWidgetDefinition { Component: "hour12Cycle", ComponentFormat: "0.0", Format: "HH:mm" },
            "Both new and original components and distinct numeric/text formats must round-trip.");
        var saved = DashboardJson.Serialize(reopened, indented: false);
        Check(saved == json && saved == DashboardJson.Serialize(DashboardJson.ParseCurrent(saved, path), indented: false),
            "New component documents must be parse/save idempotent.");
        Check(!DashboardSchemaMigration.UpgradeToCurrent(root),
            "A current dashboard must never undergo implicit component migration.");
        var invalid = JsonNode.Parse(json)!.AsObject();
        invalid["widgets"]![0]!["component"] = "hour12-cycle";
        var rejected = false;
        try { _ = DashboardJson.ParseCurrent(invalid.ToJsonString(), path); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Unknown component tokens must still fail canonical validation.");
    }

    private static C.BinaryWidgetDefinition MakeBinary()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "cycle-binary", Metric = SystemMetricContract.DateTime,
            Component = "hour12Cycle", ComponentFormat = "0.0",
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            TrueIf = BinarySignalContract.TrueIfEqual, Setpoint = "0.5",
            Width = 96, Height = 96
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        foreach (var profile in widget.Profiles.Values)
            profile.Asset.Source = null;
        return widget;
    }

    private static void Expect(DateTime time, string precision, double expected)
    {
        Check(DateTimeComponentContract.TryProject(time, "hour12Cycle", precision, out var actual) &&
              actual == expected,
            $"Hour (12-cycle) at {time:O}, Format={precision}: expected {expected}, got {actual}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
