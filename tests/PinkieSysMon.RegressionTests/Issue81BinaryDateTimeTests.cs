using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue81BinaryDateTimeTests
{
    private static readonly DateTime LeapEvening =
        new(2024, 2, 29, 23, 59, 30, 500, DateTimeKind.Local);

    public static void EvaluationAndEditor()
    {
        var widget = CreateWidget();
        using var typefaces = new TypefaceCache(
            AppContext.BaseDirectory, Array.Empty<C.WidgetDefinition>());
        using var context = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.DateTime] = LeapEvening },
            antialias: false, typefaces);

        CheckState(context, widget, BinarySignalContract.TrueKey,
            "DateTime minute fractions must drive numeric Setpoint evaluation.");
        widget.TrueIf = BinarySignalContract.TrueIfLessThan;
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "A valid false comparison must select False, not unavailable.");
        widget.TrueIf = BinarySignalContract.TrueIfEqual;
        widget.Setpoint = "60";
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "The same numeric comparison rules must apply to DateTime components.");
        widget.Setpoint = "invalid";
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "An invalid DateTime reference must be unavailable, not False.");

        widget.EvaluationMode = BinarySignalContract.EvaluationModeAuto;
        widget.Component = "dayOfYear";
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "Auto must treat a nonzero DateTime projection as True.");
        widget.Component = "hour24";
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTime(2024, 3, 1, 0, 0, 0)
        });
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "Auto must treat a selected zero-valued component as a genuine False.");
        widget.Component = null;
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "A legacy Binary with no DateTime Component must remain unavailable.");

        widget.Component = "dayOfYear";
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = new DateTimeOffset(LeapEvening)
        });
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "DateTimeOffset readings must use the shared local-time numeric projection.");
        context.UpdateMetrics(new Dictionary<string, object?>
        {
            [SystemMetricContract.DateTime] = "2024-02-29"
        });
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "An invalid DateTime reading must not be coerced from formatted text.");
        context.UpdateMetrics(new Dictionary<string, object?>());
        Check(!context.TryResolveBinaryVisualProfile(widget, out _),
            "Missing telemetry must not silently select the False profile.");

        var data = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var valueGroup = data["Value"]?.GetValue(null)
            ?? throw new InvalidOperationException("DateTime Binary must expose Data/Value even with icon states.");
        var rows = TypeDescriptor.GetProperties(valueGroup);
        var component = rows[nameof(C.BinaryWidgetDefinition.Component)]
            ?? throw new InvalidOperationException("Binary DateTime Component selector is missing.");
        Check(!component.IsReadOnly && component.Converter.GetStandardValuesExclusive(null),
            "Binary DateTime Component must use the exclusive shared options.");
        component.SetValue(valueGroup, "Hour (12-hour)");
        Check(widget.Component == "hour12",
            "Editor must persist a canonical DateTime component token.");
        var rejected = false;
        try { component.SetValue(valueGroup, "HH:mm"); }
        catch (FormatException) { rejected = true; }
        Check(rejected && widget.Component == "hour12",
            "Editor must reject formatting masks without changing the numeric component.");
        Check(rows[nameof(C.BinaryWidgetDefinition.Format)] is { IsReadOnly: true },
            "Icon-only Binary must not enable Value-state text Format.");

        widget.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        var valueData = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var displayGroup = valueData["Value"]!.GetValue(null)!;
        var format = TypeDescriptor.GetProperties(displayGroup)[nameof(C.BinaryWidgetDefinition.Format)];
        Check(format is { IsReadOnly: false },
            "Value-backed DateTime Binary must retain an independent text Format editor.");
        format!.SetValue(displayGroup, "HH:mm");
        Check(widget.Format == "HH:mm" && widget.Component == "hour12",
            "Text Format changes must not overwrite the numeric evaluation Component.");

        widget.Metric = SystemMetricContract.Uptime;
        var numericData = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        Check(TypeDescriptor.GetProperties(numericData["Value"]!.GetValue(null)!)[nameof(C.BinaryWidgetDefinition.Component)] is null,
            "Non-DateTime metrics must not show the DateTime Component selector.");
        widget.EvaluationMode = BinarySignalContract.EvaluationModeSetpoint;
        widget.Setpoint = "120";
        widget.TrueIf = BinarySignalContract.TrueIfEqual;
        context.UpdateMetrics(new Dictionary<string, object?> { [SystemMetricContract.Uptime] = 120d });
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "Dormant DateTime Component must not affect numeric Setpoint evaluation.");

        widget.Metric = "test.binary.boolean";
        widget.EvaluationMode = BinarySignalContract.EvaluationModeAuto;
        context.UpdateMetrics(new Dictionary<string, object?> { [widget.Metric] = false });
        CheckState(context, widget, BinarySignalContract.FalseKey,
            "Dormant DateTime Component must not affect Boolean Auto evaluation.");
        widget.Metric = "test.binary.text";
        context.UpdateMetrics(new Dictionary<string, object?> { [widget.Metric] = "ready" });
        CheckState(context, widget, BinarySignalContract.TrueKey,
            "Dormant DateTime Component must not affect text Auto evaluation.");
    }

    public static void SchemaMigration()
    {
        var widget = CreateWidget();
        // This model-only fixture has no application-root icon library. Source-free
        // icon profiles remain valid for JSON evaluation/migration coverage.
        foreach (var profile in widget.Profiles.Values)
            profile.Asset.Source = null;
        var definition = new C.DashboardDefinition
        {
            Widgets = [widget],
            BaseDirectory = Path.GetTempPath()
        };
        var path = Path.Combine(Path.GetTempPath(), "issue81-binary-datetime.json");
        var current = DashboardJson.Serialize(definition, indented: false);
        var parsed = DashboardJson.ParseCurrent(current, path);
        Check(((C.BinaryWidgetDefinition)parsed.Widgets[0]).Component == "minute",
            "Schema 21 must round-trip the Binary DateTime component.");

        var old = JsonNode.Parse(current)!.AsObject();
        old["schemaVersion"] = DashboardSchemaMigration.DateTimeComponentSchemaVersion;
        old["widgets"]![0]!.AsObject().Remove("component");
        Check(DashboardSchemaMigration.UpgradeToCurrent(old) &&
              old["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 20 must migrate to schema 21.");
        var upgraded = old.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(old) && old.ToJsonString() == upgraded,
            "Repeated migration of schema 21 must be a no-op.");

        var migrated = DashboardJson.ParseCurrent(upgraded, path);
        Check(((C.BinaryWidgetDefinition)migrated.Widgets[0]).Component is null,
            "Migrating an old Binary must never invent a DateTime evaluation component.");
        var firstSave = DashboardJson.Serialize(migrated, indented: false);
        var secondSave = DashboardJson.Serialize(DashboardJson.ParseCurrent(firstSave, path), indented: false);
        Check(firstSave == secondSave,
            "Migration and repeated parse/save must preserve canonical content.");

        var invalid = JsonNode.Parse(current)!.AsObject();
        invalid["widgets"]![0]!["component"] = "HH:mm";
        var rejected = false;
        try { _ = DashboardJson.ParseCurrent(invalid.ToJsonString(), path); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid persisted Binary components must be rejected.");
    }

    private static C.BinaryWidgetDefinition CreateWidget()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "clock-binary",
            Metric = SystemMetricContract.DateTime,
            Component = "minute",
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            TrueIf = BinarySignalContract.TrueIfGreaterThan,
            Setpoint = "59",
            Width = 96,
            Height = 96
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        return widget;
    }

    private static void CheckState(
        WidgetRenderContext context, C.BinaryWidgetDefinition widget, string expected, string message)
    {
        Check(context.TryResolveBinaryVisualProfile(widget, out var stateKey, out _) && stateKey == expected, message);
    }

    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
    }
}
