using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DateTimeComponentTests
{
    private static readonly DateTime LeapDay =
        new(2024, 2, 29, 23, 59, 30, 500, DateTimeKind.Local);

    public static void ProjectionEditorAndCache()
    {
        Verify("year", 2024d);
        Verify("month", 2d);
        Verify("dayOfMonth", 29d);
        Verify("dayOfYear", 60d);
        Verify("dayOfWeek", 4d); // Thursday (ISO Monday = 1)
        Verify("hour24", 23d);
        Verify("hour12", 11d);
        Verify("minute", 59d);
        Verify("second", 30d);

        Check(DateTimeComponentContract.TryProject(new DateTime(2026, 10, 17, 14, 45, 59, 999), "month", out var october) &&
              october == 10d &&
              DateTimeComponentContract.TryProject(new DateTime(2026, 10, 31, 23, 59, 59), "month", out var endOfOctober) &&
              endOfOctober == 10d,
            "Calendar month must remain the exact integer 10 for every instant of October.");
        Check(DateTimeComponentContract.TryProject(new DateTime(2024, 1, 1, 0, 0, 0), "hour12", out var midnight) &&
              midnight == 12d &&
              DateTimeComponentContract.TryProject(new DateTime(2024, 1, 1, 12, 0, 0), "hour12", out var noon) &&
              noon == 12d &&
              DateTimeComponentContract.TryProject(new DateTime(2024, 1, 1, 13, 0, 0), "hour12", out var afternoon) &&
              afternoon == 1d,
            "12-hour clock must represent midnight and noon as 12, and 13:00 as 1.");

        var offset = new DateTimeOffset(LeapDay);
        Check(DateTimeComponentContract.TryProject(offset, "hour24", out var offsetHour) &&
              offsetHour == offset.LocalDateTime.Hour,
            "DateTimeOffset values must project in local time, like textual DateTime formatting.");
        Check(!DateTimeComponentContract.TryProject("2024-02-29", "second", out _) &&
              !DateTimeComponentContract.TryProject(null, "second", out _) &&
              !DateTimeComponentContract.TryProject(LeapDay, "hh:mm", out _) &&
              !DateTimeComponentContract.TryProject(LeapDay, null, out _),
            "Unsupported, missing, or unselected DateTime components must be unavailable.");
        Check(DateTimeComponentContract.TryProject(new DateTime(2023, 12, 31), "dayOfYear", out var day) &&
              day == 365d &&
              DateTimeComponentContract.TryProject(new DateTime(2024, 12, 31), "dayOfYear", out day) &&
              day == 366d,
            "Day-of-year must respect leap years and calendar boundaries.");
        Check(DateTimeComponentContract.TryProject(new DateTime(2024, 3, 1), "month", out var march) &&
              march == 3d,
            "Month component must equal its civil-calendar number at the month boundary.");

        var hour = new C.GaugeWidgetDefinition
        {
            Id = "clock-hour", Metric = SystemMetricContract.DateTime,
            Component = "hour24", Width = 200, Height = 200
        };
        var minute = new C.BarWidgetDefinition
        {
            Id = "clock-minute", Metric = SystemMetricContract.DateTime,
            Component = "minute", Width = 300, Height = 20
        };
        var second = new C.GaugeWidgetDefinition
        {
            Id = "clock-second", Metric = SystemMetricContract.DateTime,
            Component = "second", Width = 200, Height = 200
        };
        using var typefaces = new TypefaceCache(
            AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [SystemMetricContract.DateTime] = LeapDay
            },
            antialias: false,
            typefaces);
        Check(context.TryGetMetricDouble(hour, out var hourValue) &&
              context.TryGetMetricDouble(minute, out var minuteValue) &&
              context.TryGetMetricDouble(second, out var secondValue) &&
              hourValue == 23d &&
              minuteValue == 59d &&
              secondValue == 30d,
            "Shared metric snapshots must cache each DateTime component independently for Gauge and Bar.");

        hour.Component = null;
        Check(!context.TryGetMetricDouble(hour, out _),
            "An unmigrated DateTime Gauge must remain unavailable without an explicitly selected component.");

        var data = new EditorPropertyView(minute, section: EditorPropertySection.Data).GetProperties();
        var valueGroup = data["Value"]?.GetValue(null)
            ?? throw new InvalidOperationException("Bar has no Data/Value group.");
        var component = TypeDescriptor.GetProperties(valueGroup)[nameof(C.QuantitativeWidgetDefinition.Component)]
            ?? throw new InvalidOperationException("DateTime Component property is missing.");
        Check(!component.IsReadOnly && component.Converter.GetStandardValuesExclusive(null),
            "DateTime component selection must be editable and restricted to the supported options.");
        component.SetValue(valueGroup, "Hour (12-hour)");
        Check(minute.Component == "hour12",
            "The Editor must store canonical component tokens, not display labels.");
        var invalidRejected = false;
        try
        {
            component.SetValue(valueGroup, "hh:mm");
        }
        catch (FormatException)
        {
            invalidRejected = true;
        }
        Check(invalidRejected && minute.Component == "hour12",
            "Invalid free-form DateTime masks must be rejected without changing the selected component.");
        var numeric = new C.BarWidgetDefinition
        {
            Id = "numeric", Metric = SystemMetricContract.Uptime, Width = 300, Height = 20
        };
        var numericRows = new EditorPropertyView(numeric, section: EditorPropertySection.Data).GetProperties();
        var numericGroup = numericRows["Value"]!.GetValue(null)!;
        Check(TypeDescriptor.GetProperties(numericGroup)[nameof(C.QuantitativeWidgetDefinition.Component)] is null,
            "Non-DateTime quantitative metrics must not acquire a DateTime component selector.");
    }

    public static void SchemaMigrationAndCompatibility()
    {
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "gauge", Metric = SystemMetricContract.DateTime, Component = "second",
            Width = 200, Height = 200
        };
        var bar = new C.BarWidgetDefinition
        {
            Id = "bar", Metric = SystemMetricContract.DateTime, Component = "minute",
            Width = 300, Height = 20
        };
        var definition = new C.DashboardDefinition { Widgets = [gauge, bar], BaseDirectory = Path.GetTempPath() };
        var original = DashboardJson.Serialize(definition, indented: false);
        var path = Path.Combine(Path.GetTempPath(), "issue66-datetime-dashboard.json");

        var persisted = DashboardJson.ParseCurrent(original, path);
        Check(((C.GaugeWidgetDefinition)persisted.Widgets[0]).Component == "second" &&
              ((C.BarWidgetDefinition)persisted.Widgets[1]).Component == "minute",
            "Current schema must round-trip distinct Gauge and Bar DateTime components.");

        var previous = JsonNode.Parse(original)!.AsObject();
        previous["schemaVersion"] = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;
        foreach (var widget in previous["widgets"]!.AsArray())
            widget!.AsObject().Remove("component");

        Check(DashboardSchemaMigration.UpgradeToCurrent(previous) &&
              previous["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 19 must migrate deterministically through schema 20 to the current schema.");
        var firstMigration = previous.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(previous) &&
              previous.ToJsonString() == firstMigration,
            "Repeating the migration must not rewrite a current document.");

        var migrated = DashboardJson.ParseCurrent(firstMigration, path);
        Check(((C.GaugeWidgetDefinition)migrated.Widgets[0]).Component is null &&
              ((C.BarWidgetDefinition)migrated.Widgets[1]).Component is null,
            "Old Gauge and Bar definitions must not acquire an invented DateTime component.");
        var saved = DashboardJson.Serialize(migrated, indented: false);
        var savedTwice = DashboardJson.Serialize(DashboardJson.ParseCurrent(saved, path), indented: false);
        Check(saved == savedTwice, "Migrated dashboards must be parse/save idempotent.");

        var invalid = JsonNode.Parse(original)!.AsObject();
        invalid["widgets"]![0]!["component"] = "hh:mm";
        var rejected = false;
        try
        {
            _ = DashboardJson.ParseCurrent(invalid.ToJsonString(), path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Check(rejected, "Invalid persisted component tokens must be rejected by canonical validation.");

        var numeric = new C.GaugeWidgetDefinition
        {
            Id = "numeric", Metric = SystemMetricContract.Uptime, Unit = "minutes",
            Component = "second", Width = 200, Height = 200
        };
        using var fonts = new TypefaceCache(AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.Uptime] = 120d },
            antialias: false, fonts);
        Check(context.TryGetMetricDouble(numeric, out var minutes) && minutes == 2d,
            "A dormant DateTime Component must not change an existing numeric unit conversion.");
        Check(MetricContract.DashboardSchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion,
            "Canonical dashboard schema ownership must match the current migration version.");
    }

    private static void Verify(string component, double expected)
    {
        Check(DateTimeComponentContract.TryProject(LeapDay, component, out var actual) &&
              Math.Abs(actual - expected) < 1e-9,
            $"Incorrect DateTime projection for '{component}'.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
