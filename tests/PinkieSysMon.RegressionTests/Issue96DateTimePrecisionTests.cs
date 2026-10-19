using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue96DateTimePrecisionTests
{
    public static void ProjectionAndBoundaries()
    {
        var leapDay = new DateTime(2024, 2, 29, 12, 30, 45, 987);
        Expect(leapDay, "year", "0", 2024);
        Expect(leapDay, "year", "0.0", 2024.1);
        Expect(leapDay, "month", "0.0", 2.9);
        Expect(leapDay, "dayOfMonth", "0.0", 29.5);
        Expect(leapDay, "dayOfYear", "0.0", 60.5);
        Expect(leapDay, "dayOfWeek", "0.0", 4.5);
        Expect(leapDay, "hour24", "0.0", 12.5);
        Expect(leapDay, "hour12", "0.0", 12.5);
        Expect(leapDay, "minute", "0.0", 30.7);
        Expect(leapDay, "second", "0.00", 45.98);
        Expect(new DateTime(2024, 1, 1, 0, 30, 0), "hour12", "0.0", 12.5);
        Expect(new DateTime(2024, 1, 1, 13, 30, 0), "hour12", "0.0", 1.5);
        Expect(new DateTime(2024, 1, 1, 12, 0, 0), "dayOfWeek", "0.0", 1.5);
        Expect(new DateTime(2024, 1, 7, 23, 59, 59, 999), "dayOfWeek", "0.00", 7.99);
        Expect(new DateTime(2023, 2, 28, 23, 59, 59, 999), "month", "0.00", 2.99);
        Expect(new DateTime(2024, 2, 29, 23, 59, 59, 999), "month", "0.00", 2.99);
        Expect(new DateTime(2026, 10, 31, 23, 59, 59, 999), "month", "0", 10);
        Expect(new DateTime(2026, 10, 31, 23, 59, 59, 999), "month", "0.00", 10.99);
        Expect(new DateTime(2026, 11, 1), "month", "0.00", 11);
        Expect(new DateTime(2024, 12, 31, 23, 59, 59, 999), "year", "0.00", 2024.99);
        Expect(new DateTime(2024, 12, 31, 23, 59, 59, 999), "dayOfYear", "0.00", 366.99);
        Expect(new DateTime(2023, 12, 31, 23, 59, 59, 999), "dayOfYear", "0.00", 365.99);
        Expect(new DateTime(2024, 2, 29, 23, 59, 59, 999), "hour24", "0.00", 23.99);
        Expect(new DateTime(2024, 2, 29, 23, 59, 59, 999), "minute", "0.00", 59.99);
        Expect(new DateTime(2024, 2, 29, 23, 59, 59, 999), "second", "0.00", 59.99);
        Expect(DateTime.MaxValue, "year", "0.00", 9999.99);
        var offset = new DateTimeOffset(2024, 2, 29, 23, 59, 59, TimeSpan.FromHours(2));
        Check(DateTimeComponentContract.TryProject(offset, "hour24", "0.00", out var actual) &&
              DateTimeComponentContract.TryProject(offset.LocalDateTime, "hour24", "0.00", out var local) &&
              actual == local, "DateTimeOffset component must follow local-time projection.");

        Check(!DateTimeComponentContract.TryProject(null, "month", "0.0", out _) &&
              !DateTimeComponentContract.TryProject("2026-10-01", "month", "0.0", out _) &&
              !DateTimeComponentContract.TryProject(leapDay, null, "0.0", out _) &&
              !DateTimeComponentContract.TryProject(leapDay, "month", "raw", out _) &&
              !DateTimeComponentContract.TryProject(leapDay, "unknown", "0.0", out _),
            "Missing, invalid, or unselected projections must be unavailable.");
        Check(DateTimeComponentContract.PrecisionFormats.Select(x => x.Token)
                  .SequenceEqual(new[] { "0", "0.0", "0.00" }),
            "Only supported numeric precision Formats may be offered.");
    }

    public static void EditorCacheAndBinary()
    {
        var timestamp = new DateTime(2026, 10, 16, 12, 30, 45, 987);
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "precision-gauge", Metric = SystemMetricContract.DateTime,
            Component = "month", Width = 200, Height = 200
        };
        var bar = new C.BarWidgetDefinition
        {
            Id = "precision-bar", Metric = SystemMetricContract.DateTime,
            Component = "month", ComponentFormat = "0.0", Width = 300, Height = 20
        };
        var binary = CreateBinary();
        binary.Component = "month";
        binary.Setpoint = "10";
        using var typefaces = new TypefaceCache(
            AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.DateTime] = timestamp },
            antialias: false, typefaces);

        Check(context.TryGetMetricDouble(gauge, out var whole) && whole == 10 &&
              context.TryGetMetricDouble(bar, out var tenth) && tenth == 10.5,
            "Gauge and Bar must isolate integer and fractional projections of one metric.");
        gauge.ComponentFormat = "0.00";
        Check(context.TryGetMetricDouble(gauge, out var hundredth) && hundredth == 10.5,
            "Precision changes must not reuse another cache entry.");
        gauge.Component = "hour24";
        Check(context.TryGetMetricDouble(gauge, out var hour) && hour == 12.51,
            "Component changes must not reuse another cache entry.");
        gauge.Component = null;
        Check(!context.TryGetMetricDouble(gauge, out _),
            "A missing Component must remain unavailable even with a Format selected.");

        CheckState(context, binary, BinarySignalContract.TrueKey, "October must equal 10 by default.");
        binary.Setpoint = "11";
        binary.TrueIf = BinarySignalContract.TrueIfNotEqual;
        CheckState(context, binary, BinarySignalContract.TrueKey, "October must not equal 11.");
        binary.TrueIf = BinarySignalContract.TrueIfEqual;
        binary.Setpoint = "10";
        binary.ComponentFormat = "0.0";
        CheckState(context, binary, BinarySignalContract.FalseKey,
            "Fractional October must not equal integer 10 after its initial quantized interval.");
        binary.Setpoint = "10.5";
        CheckState(context, binary, BinarySignalContract.TrueKey,
            "Fractional numeric setpoint equality must use quantized numeric value.");
        binary.Setpoint = "10.6-10.9";
        CheckState(context, binary, BinarySignalContract.FalseKey,
            "Range comparisons must operate on the actual fractional scalar.");
        binary.EvaluationMode = BinarySignalContract.EvaluationModeAuto;
        binary.Component = "hour24";
        CheckState(context, binary, BinarySignalContract.TrueKey, "Auto uses the fractional scalar.");
        context.UpdateMetrics(new Dictionary<string, object?>());
        Check(!context.TryResolveBinaryVisualProfile(binary, out _),
            "Missing readings must remain unavailable rather than selecting False.");

        var quantitativeRows = new EditorPropertyView(bar, section: EditorPropertySection.Data).GetProperties();
        var qGroup = quantitativeRows["Value"]!.GetValue(null)!;
        var qFields = TypeDescriptor.GetProperties(qGroup);
        var qFormat = qFields[nameof(C.QuantitativeWidgetDefinition.ComponentFormat)]!;
        Check(qFormat.DisplayName == "Format" && !qFormat.IsReadOnly &&
              qFormat.Converter.GetStandardValuesExclusive(null),
            "DateTime Bar must offer an exclusive numeric Format next to Component.");
        qFormat.SetValue(qGroup, "0.00");
        Check(bar.ComponentFormat == "0.00", "Editor must store the numeric precision token.");
        var rejected = false;
        try { qFormat.SetValue(qGroup, "raw"); }
        catch (FormatException) { rejected = true; }
        Check(rejected && bar.ComponentFormat == "0.00",
            "Unsupported precision must be rejected without mutation.");

        binary.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Image;
        binary.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Image;
        var bRows = new EditorPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        var bGroup = bRows["Value"]!.GetValue(null)!;
        var bFields = TypeDescriptor.GetProperties(bGroup);
        Check(bFields[nameof(C.BinaryWidgetDefinition.ComponentFormat)] is { IsReadOnly: false } &&
              bFields[nameof(C.BinaryWidgetDefinition.Format)] is { IsReadOnly: true },
            "Icon-only Binary must edit numeric Format but not Value-state text Format.");

        binary.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        bRows = new EditorPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        bGroup = bRows["Value"]!.GetValue(null)!;
        var textFormat = TypeDescriptor.GetProperties(bGroup)[nameof(C.BinaryWidgetDefinition.Format)]!;
        Check(textFormat.DisplayName == "Text Format" && !textFormat.IsReadOnly,
            "Binary Value states must expose the separate existing text formatter.");
        textFormat.SetValue(bGroup, "HH:mm");
        Check(binary.Format == "HH:mm" && binary.ComponentFormat == "0.0",
            "Text Format must never alter numeric precision.");

        bar.Metric = SystemMetricContract.Uptime;
        qGroup = new EditorPropertyView(bar, section: EditorPropertySection.Data)
            .GetProperties()["Value"]!.GetValue(null)!;
        Check(TypeDescriptor.GetProperties(qGroup)[nameof(C.QuantitativeWidgetDefinition.ComponentFormat)] is null,
            "Non-DateTime metrics must not expose component precision.");
    }

    public static void SchemaCompatibility()
    {
        var binary = CreateBinary();
        binary.Component = "month";
        binary.Format = "yyyy-MM-dd";
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "schema-gauge", Metric = SystemMetricContract.DateTime,
            Component = "minute", Width = 200, Height = 200
        };
        var dashboard = new C.DashboardDefinition
        {
            Widgets = [binary, gauge], BaseDirectory = Path.GetTempPath()
        };
        var path = Path.Combine(Path.GetTempPath(), "issue96-precision-dashboard.json");
        var serialized = DashboardJson.Serialize(dashboard, indented: false);
        Check(!serialized.Contains("componentFormat", StringComparison.Ordinal),
            "Default zero precision must not insert redundant fields.");

        var old = JsonNode.Parse(serialized)!.AsObject();
        old["schemaVersion"] = DashboardSchemaMigration.BinaryDateTimeComponentSchemaVersion;
        Check(DashboardSchemaMigration.UpgradeToCurrent(old) &&
              old["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion &&
              !DashboardSchemaMigration.UpgradeToCurrent(old),
            "Schema 21 to 22 must migrate once without manufactured precision fields.");
        var migrated = DashboardJson.ParseCurrent(old.ToJsonString(), path);
        var migratedBinary = (C.BinaryWidgetDefinition)migrated.Widgets[0];
        Check(migratedBinary.Format == "yyyy-MM-dd" &&
              migratedBinary.ComponentFormat is null &&
              ((C.GaugeWidgetDefinition)migrated.Widgets[1]).ComponentFormat is null,
            "Migration must preserve Binary text masks and legacy integer precision.");
        var save = DashboardJson.Serialize(migrated, indented: false);
        Check(save == DashboardJson.Serialize(DashboardJson.ParseCurrent(save, path), indented: false),
            "Parse/migrate/save must be idempotent.");

        binary.ComponentFormat = "0.00";
        gauge.ComponentFormat = "0.0";
        serialized = DashboardJson.Serialize(dashboard, indented: false);
        migrated = DashboardJson.ParseCurrent(serialized, path);
        Check(((C.BinaryWidgetDefinition)migrated.Widgets[0]).ComponentFormat == "0.00" &&
              ((C.GaugeWidgetDefinition)migrated.Widgets[1]).ComponentFormat == "0.0" &&
              ((C.BinaryWidgetDefinition)migrated.Widgets[0]).Format == "yyyy-MM-dd",
            "Precision and pre-existing Binary text Format must serialize independently.");
        var invalid = JsonNode.Parse(serialized)!.AsObject();
        invalid["widgets"]![0]!["componentFormat"] = "raw";
        var rejected = false;
        try { _ = DashboardJson.ParseCurrent(invalid.ToJsonString(), path); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid persisted componentFormat must fail validation.");

        using var fonts = new TypefaceCache(AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.DateTime] = new DateTime(2026, 10, 16, 12, 30, 0) },
            antialias: false, fonts);
        Check(context.FormatMetric(binary) == "2026-10-16",
            "Existing Binary DateTime textual masks must still render unchanged.");
    }

    private static C.BinaryWidgetDefinition CreateBinary()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "precision-binary", Metric = SystemMetricContract.DateTime,
            Component = "month", EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            TrueIf = BinarySignalContract.TrueIfEqual, Setpoint = "10",
            Width = 96, Height = 96
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        foreach (var profile in widget.Profiles.Values)
            profile.Asset.Source = null;
        return widget;
    }

    private static void Expect(DateTime reading, string component, string precision, double expected)
    {
        Check(DateTimeComponentContract.TryProject(reading, component, precision, out var value) &&
              value == expected,
            $"Expected {component} with {precision} to equal {expected}; got {value}.");
    }

    private static void CheckState(WidgetRenderContext context, C.BinaryWidgetDefinition widget,
        string expected, string message) =>
        Check(context.TryResolveBinaryVisualProfile(widget, out var state, out _) && state == expected, message);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
