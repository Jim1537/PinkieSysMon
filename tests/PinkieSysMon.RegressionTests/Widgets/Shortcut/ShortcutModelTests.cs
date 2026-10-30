using System.Text.Json.Nodes;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutModelTests
{
    internal static C.ShortcutWidgetDefinition Shortcut() => new()
    {
        Id = "shortcut", Name = "Editor name", X = 11.25f, Y = -20, Width = 256, Height = 128,
        Z = 7, Rotation = 33, Gap = -40, Target = "shell:Example with space/Юникод.dll",
        Arguments = "--literal \"two words\" $unchanged", Label = "A literal 0123 label",
        LabelPosition = ShortcutContract.Top, Activation = ShortcutContract.SingleClick,
        Opacity = .25f, Color = "#FF123456", BackgroundColor = "#80445566", BorderWidth = 3,
        BorderColor = "#FF334455", CornerRadius = 10, ShadowEnabled = true, ShadowOffsetX = -6,
        ShadowOffsetY = 8, ShadowBlur = 5, ShadowOpacity = .75f, ShadowColor = "#FF112233",
        LabelColor = "#FF998877", TextPresentation = new()
        {
            FontFamily = "Roboto", FontSize = 19, FontWeight = 700, Italic = true, Align = "right",
            VerticalAlign = "top", OutlineWidth = 2, OutlineColor = "#FF010203",
            OverflowMode = ValueOverflowContract.Wrap, ScrollSpeed = 45, BumpPauseMs = 123
        }
    };

    internal static C.DashboardDefinition Board(params C.WidgetDefinition[] widgets) => new()
    {
        BaseDirectory = Path.GetTempPath(), Widgets = [.. widgets], Canvas = new() { Width = 640, Height = 480 }
    };

    public static void RoundTripAndDefaults()
    {
        var shortcut = Shortcut(); var board = Board(shortcut);
        foreach (var mode in ValueOverflowContract.ConstrainedModes)
        foreach (var position in ShortcutContract.LabelPositions)
        {
            shortcut.TextPresentation.OverflowMode = mode; shortcut.LabelPosition = position;
            var json = DashboardJson.Serialize(board, false);
            var parsed = C.DashboardDefinition.Parse(json, Path.Combine(Path.GetTempPath(), "shortcut-model.json"));
            Check(DashboardJson.Serialize(parsed, false) == json, "Every Shortcut property must survive repeated parse/save exactly.");
            var clone = DashboardJson.CloneWidget(shortcut);
            Check(DashboardJson.Serialize(Board(clone), false) == json, "Clone must preserve all common and Shortcut fields.");
            Check(DashboardMetricUsage.Collect(parsed).Count == 0 && !DashboardMetricUsage.CollectHistory(parsed).Any(), "Literal Shortcut state must create no telemetry/history demand.");
        }
        var fresh = new C.ShortcutWidgetDefinition { Id = "new", Width = 100, Height = 300 };
        fresh.Validate(Path.GetTempPath());
        Check(fresh.Target == "" && fresh.Arguments == "" && fresh.Label == "" && fresh.Gap == 4 &&
            fresh.Activation == ShortcutContract.DoubleClick && fresh.LabelPosition == ShortcutContract.Bottom &&
            fresh.TextPresentation.OverflowMode == ValueOverflowContract.Clip && ShortcutContract.IsAuto(fresh.Asset.SourceType), "Approved new-widget defaults must remain explicit.");
        var root = JsonNode.Parse(DashboardJson.Serialize(Board(fresh), false))!.AsObject();
        foreach (var field in new[] { "target", "arguments", "label" }) root["widgets"]![0]!.AsObject().Remove(field);
        var omitted = (C.ShortcutWidgetDefinition)C.DashboardDefinition.Parse(root.ToJsonString(), Path.Combine(Path.GetTempPath(), "shortcut-omitted.json")).Widgets[0];
        Check(omitted.Target == "" && omitted.Arguments == "" && omitted.Label == "", "Missing strings must remain empty without a target or label fallback.");
        Check(!WidgetSizeContract.IsHeightDerived(fresh) && !WidgetSizeContract.IsSquareConstrained(fresh), "Shortcut must retain arbitrary independent graphic dimensions.");
    }

    public static void MigrationAndStrictness()
    {
        var original = JsonNode.Parse(DashboardJson.Serialize(Board(new C.BarWidgetDefinition
            { Id = "bar", Width = 100, Height = 20, Metric = SystemMetricContract.Uptime, Reverse = true }), false))!.AsObject();
        var old = (JsonObject)original.DeepClone(); old["schemaVersion"] = 25;
        Check(DashboardSchemaMigration.UpgradeToCurrent(old) && JsonNode.DeepEquals(old, original), "25-to-26 must preserve every existing field and change only schemaVersion.");
        Check(!DashboardSchemaMigration.UpgradeToCurrent(old), "Repeated migration must be idempotent.");
        var serialized = DashboardJson.Serialize(C.DashboardDefinition.Parse(old.ToJsonString(), Path.Combine(Path.GetTempPath(), "shortcut-migration.json")), false);
        Check(serialized == DashboardJson.Serialize(C.DashboardDefinition.Parse(serialized, Path.Combine(Path.GetTempPath(), "shortcut-migration.json")), false), "Migrate/save/load must remain idempotent.");
        for (var version = 10; version < 26; version++)
        {
            var bad = JsonNode.Parse(DashboardJson.Serialize(Board(Shortcut()), false))!.AsObject(); bad["schemaVersion"] = version;
            var before = bad.ToJsonString(); Throws(() => DashboardSchemaMigration.UpgradeToCurrent(bad));
            Check(before == bad.ToJsonString(), "An older document must reject the new discriminator before mutation.");
        }
        var unknown = JsonNode.Parse(DashboardJson.Serialize(Board(Shortcut()), false))!.AsObject();
        unknown["widgets"]![0]!["labelWidth"] = 999;
        Throws(() => C.DashboardDefinition.Parse(unknown.ToJsonString(), Path.Combine(Path.GetTempPath(), "shortcut-unknown.json")));
    }

    public static void ValidationAndSourceOwnership()
    {
        foreach (var gap in new[] { -1000f, -40, -20, -5, 0, 4, 1000 }) { var s = Shortcut(); s.Gap = gap; s.Validate(Path.GetTempPath()); }
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        { var s = Shortcut(); s.Gap = invalid; Throws(() => s.Validate(Path.GetTempPath())); }
        foreach (var invalid in new Action<C.ShortcutWidgetDefinition>[]
        {
            s => s.Width = 0, s => s.Height = -1, s => s.Opacity = 2, s => s.Activation = "Triple",
            s => s.LabelPosition = "Left", s => s.TextPresentation.OverflowMode = ValueOverflowContract.None,
            s => s.Target = null!, s => s.Arguments = null!, s => s.Label = null!, s => s.Asset = null!,
            s => s.TextPresentation = null!, s => s.Asset.Source = "not-an-auto-asset"
        }) { var s = Shortcut(); invalid(s); Throws(() => s.Validate(Path.GetTempPath())); }
        var shortcut = Shortcut(); var target = shortcut.Target; var arguments = shortcut.Arguments;
        shortcut.Asset.ChangeSourceType(C.ImageAssetSourceType.File, allowShellIcon: true);
        shortcut.Asset.Source = "temporary custom selection";
        shortcut.Asset.ChangeSourceType(ShortcutContract.AutoSource, allowShellIcon: true);
        Check(shortcut.Asset.Source is null && shortcut.Target == target && shortcut.Arguments == arguments, "Changing custom artwork must clear only Source, preserving Shell payload.");
        Throws(() => new C.ImageAssetPresentationDefinition { SourceType = ShortcutContract.AutoSource }.ValidateStructure("ordinary image"));
        shortcut.Target = @"Z:\missing\not-installed.unusual-extension";
        shortcut.Validate(Path.GetTempPath()); // Loading must never resolve or launch a Shell target.
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected canonical validation to reject the input.");
    }
}
