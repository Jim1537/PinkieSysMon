using System.ComponentModel;
using System.Drawing;
using System.Text.Json.Nodes;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class WidgetActionTests
{
    private static C.DashboardDefinition Board(params C.WidgetDefinition[] widgets) => new()
    {
        BaseDirectory = Path.GetTempPath(),
        Canvas = new() { Width = 64, Height = 48 },
        Widgets = [.. widgets]
    };

    public static void ModelMigrationAndLegacyRejection()
    {
        var image = new C.ImageWidgetDefinition { Id = "action-image", Width = 30, Height = 25 };
        var original = DashboardJson.Serialize(Board(image), indented: false);
        var legacy = JsonNode.Parse(original)!.AsObject();
        legacy["schemaVersion"] = DashboardSchemaMigration.CanvasPanelSchemaVersion;
        legacy["widgets"]!.AsArray()[0]!.AsObject().Remove("action");
        // Recreate the actual two-factor shadow persistence from schema 30.
        // Merely relabeling a schema-32 fixture would fold its alpha twice.
        var canvas = legacy["canvas"]!.AsObject();
        canvas["shadowColor"] = "#FF000000";
        canvas["shadowOpacity"] = 0.5f;
        var legacyWidget = legacy["widgets"]!.AsArray()[0]!.AsObject();
        legacyWidget["shadowColor"] = "#FF000000";
        legacyWidget["shadowOpacity"] = 0.5f;

        Check(DashboardSchemaMigration.UpgradeToCurrent(legacy),
            "Schema 30 must migrate through Action schema 31 and Shadow schema 32.");
        Check(legacy["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Action migration must reach the current canonical schema.");
        Check(legacy["canvas"]!["shadowColor"]!.GetValue<string>() == "#80000000" &&
              legacy["widgets"]![0]!["shadowColor"]!.GetValue<string>() == "#80000000",
            "The historical shadow representation must retain its effective alpha.");
        var unchanged = legacy.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(legacy) && legacy.ToJsonString() == unchanged,
            "Repeated migration must not mutate a current-schema document.");

        var path = Path.Combine(Path.GetTempPath(), "pinkie-action-contract.json");
        var parsed = DashboardJson.DeserializeCurrent(legacy, path);
        Check(parsed.Widgets.Count == 1 && parsed.Widgets[0].Action.Activation == C.WidgetActionDefinition.None &&
              parsed.Widgets[0].Action.Target == "" && parsed.Widgets[0].Action.Arguments == "",
            "Omitted actions must acquire the canonical inactive defaults.");
        var saved = DashboardJson.Serialize(parsed, indented: false);
        Check(DashboardJson.Serialize(C.DashboardDefinition.Parse(saved, path), indented: false) == saved,
            "Current-schema parse/save and repeated migration must be deterministic.");
        var clone = DashboardJson.CloneWidget(parsed.Widgets[0]);
        Check(DashboardJson.Serialize(Board(clone), indented: false) == saved,
            "Clipboard/clone must preserve base Action values.");

        image.Action.Activation = C.WidgetActionDefinition.SingleClick;
        image.Action.Target = "  ";
        image.Action.Arguments = "--literal argument";
        Check(!image.Action.IsActive && image.Action.Activation == C.WidgetActionDefinition.SingleClick,
            "An empty Target must have None behavior without changing Activation.");
        image.Action.Target = "shell:AppsFolder";
        Check(image.Action.IsActive, "A nonblank Target must enable the selected activation mode.");
        image.Action.Activation = C.WidgetActionDefinition.None;
        Check(!image.Action.IsActive && image.Action.Target == "shell:AppsFolder" &&
              image.Action.Arguments == "--literal argument", "None must retain dormant payloads.");
        var persisted = DashboardJson.Serialize(Board(image), indented: false);
        Check(DashboardJson.Serialize(C.DashboardDefinition.Parse(persisted, path), indented: false) == persisted,
            "Dormant Target/Arguments must survive serialization.");

        image.Action.Activation = "not-a-mode";
        ExpectInvalid(() => DashboardJson.Serialize(Board(image)), "Unknown activation must not persist.");
        image.Action.Activation = C.WidgetActionDefinition.None;
        image.Action.Target = null!;
        ExpectInvalid(() => DashboardJson.Serialize(Board(image)), "Null Action payload must be rejected.");

        var unsupported = JsonNode.Parse(saved)!.AsObject();
        unsupported["widgets"]!.AsArray()[0]!.AsObject()["type"] = "shortcut";
        var incompatibleSource = unsupported.ToJsonString();
        var sourcePath = Path.Combine(Path.GetTempPath(), "pinkie-legacy-action-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(sourcePath, incompatibleSource);
        try
        {
            try { _ = C.DashboardDefinition.Load(sourcePath); }
            catch (InvalidDataException ex)
            {
                Check(ex.Message.Contains("Legacy Shortcut", StringComparison.Ordinal) &&
                      File.ReadAllText(sourcePath) == incompatibleSource,
                    "Unsupported Shortcut must fail explicitly without modifying its source file.");
                return;
            }
            throw new InvalidOperationException("Legacy Shortcut was silently accepted.");
        }
        finally { File.Delete(sourcePath); }
    }

    public static void EditorPropertiesAndMultiEdit()
    {
        C.WidgetDefinition[] all =
        [
            new C.ValueWidgetDefinition(), new C.BinaryWidgetDefinition(),
            new C.GaugeWidgetDefinition(), new C.BarWidgetDefinition(),
            new C.GraphWidgetDefinition(), new C.ImageWidgetDefinition(),
            new C.PowerWidgetDefinition(), new C.MediaSystemWidgetDefinition(),
            new C.MediaPlayerWidgetDefinition()
        ];
        foreach (var widget in all)
        {
            Check(widget.Action.Activation == C.WidgetActionDefinition.None && !widget.Action.IsActive,
                $"{widget.Type} must inherit inactive Action.");
            var row = new EditorPropertyView(widget, section: EditorPropertySection.Action).GetProperties();
            Check(row["Action"] is not null, $"{widget.Type}: missing universal Action group.");
            var cells = Children(row);
            Check(cells.Cast<PropertyDescriptor>().Select(d => d.DisplayName)
                    .SequenceEqual(new[] { "Activation", "Target", "Arguments" }),
                $"{widget.Type}: Action order must be Activation, Target, Arguments.");
            Check(cells["Target"]!.IsReadOnly && cells["Arguments"]!.IsReadOnly,
                "None must make dormant Target/Arguments read-only.");
            var merged = new EditorPropertyView(widget, section: EditorPropertySection.All).GetProperties();
            Check(merged.Cast<PropertyDescriptor>().Last().DisplayName == "Action",
                "Action must be the last shared property group.");
        }
        Check(new EditorPropertyView(new C.CanvasDefinition(), section: EditorPropertySection.Action)
                .GetProperties().Count == 0, "Canvas must not expose Action.");

        var image = (C.ImageWidgetDefinition)all[5];
        var fields = Children(new EditorPropertyView(image, section: EditorPropertySection.Action).GetProperties());
        fields["Activation"]!.SetValue(null, "Single click");
        Check(image.Action.Activation == C.WidgetActionDefinition.SingleClick &&
              !fields["Target"]!.IsReadOnly && !fields["Arguments"]!.IsReadOnly,
            "Choosing Single must enable editing without requiring Target first.");
        fields["Target"]!.SetValue(null, "  ");
        fields["Arguments"]!.SetValue(null, "--test with whitespace");
        Check(!image.Action.IsActive && image.Action.Arguments == "--test with whitespace",
            "Blank Target must remain inert while preserving literal Arguments.");
        fields["Target"]!.SetValue(null, "C:\\Some Path\\app.exe");
        Check(image.Action.IsActive && image.Action.Target == "C:\\Some Path\\app.exe",
            "Target editing must preserve the literal Shell path and enable action routing.");

        var value = (C.ValueWidgetDefinition)all[0];
        var multi = new EditorMultiPropertyView(
        [
            new EditorPropertyView(image, section: EditorPropertySection.Action),
            new EditorPropertyView(value, section: EditorPropertySection.Action)
        ]);
        var shared = Children(multi.GetProperties());
        shared["Activation"]!.SetValue(null, "Double click");
        shared["Target"]!.SetValue(null, "shell:AppsFolder");
        Check(image.Action.Activation == C.WidgetActionDefinition.DoubleClick &&
              value.Action.Activation == C.WidgetActionDefinition.DoubleClick &&
              image.Action.Target == "shell:AppsFolder" && value.Action.Target == "shell:AppsFolder",
            "Cross-type selection must edit the same Action model.");
        shared["Activation"]!.SetValue(null, "None");
        Check(shared["Target"]!.IsReadOnly && shared["Arguments"]!.IsReadOnly &&
              image.Action.Target == "shell:AppsFolder" && value.Action.Target == "shell:AppsFolder",
            "Multi-edit None must disable inputs without erasing dormant payloads.");
    }

    public static void InactiveHotspotsAndGeometry()
    {
        var active = new C.ImageWidgetDefinition
        {
            Id = "active", X = 10, Y = 10, Width = 20, Height = 20, Rotation = 25, Z = 0,
            Action = new() { Activation = C.WidgetActionDefinition.SingleClick, Target = "calc.exe" }
        };
        var none = new C.ValueWidgetDefinition
        {
            Id = "none", X = 10, Y = 10, Width = 20, Height = 20, Z = 20,
            Action = new() { Activation = C.WidgetActionDefinition.None, Target = "blocked.exe" }
        };
        var empty = new C.BarWidgetDefinition
        {
            Id = "empty", X = 10, Y = 10, Width = 20, Height = 20, Z = 30,
            Action = new() { Activation = C.WidgetActionDefinition.DoubleClick, Target = "  " }
        };
        var board = Board(active, none, empty);
        var map = new WidgetActionInteractionMap(board);
        Check(map.Hit(new Point(20, 20))?.Id == "active" &&
              map.Hit(new Point(-1, 20)) is null &&
              map.Hit(new Point(64, 20)) is null,
            "Inactive higher layers must not mask the rotated active widget or clip outside Canvas.");
        Check(map.Rasterize().Count > 0, "The geometric hotspot must not depend on visible pixels or media.");
        var frozen = map.Rasterize().ToArray();
        active.X = 100;
        active.Action.Target = "changed";
        Check(map.Rasterize().SequenceEqual(frozen) && map.Hit(new Point(20, 20))?.Target == "calc.exe",
            "A Desktop Action snapshot must not borrow mutable widget properties.");
        none.Action.Target = "";
        empty.Action.Target = "";
        var inactiveBoard = Board(none, empty);
        var inactiveMap = new WidgetActionInteractionMap(inactiveBoard);
        Check(inactiveMap.IsEmpty && inactiveMap.Rasterize().Count == 0 &&
              inactiveMap.Hit(new Point(20, 20)) is null,
            "None or blank Target must create no native input region.");
        empty.Action.Target = "notepad.exe";
        var newlyActive = new WidgetActionInteractionMap(Board(none, empty));
        Check(newlyActive.Hit(new Point(20, 20))?.Id == "empty",
            "Filling Target must activate its previously selected double-click mode.");
    }

    public static void DerivedValueGeometryAndRefresh()
    {
        var value = new C.ValueWidgetDefinition
        {
            Id = "intrinsic-text", X = 10, Y = 20, Width = 0, Height = 0,
            SourceKind = C.ValueSourceKind.Text, Text = "GO",
            TextPresentation = new() { FontFamily = "Roboto", FontSize = 22 },
            Action = new() { Activation = C.WidgetActionDefinition.SingleClick, Target = "notepad.exe" }
        };
        var board = Board(value);
        board.BaseDirectory = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        using var renderer = new DashboardRenderer(board);
        var metrics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using (var frame = renderer.RenderBgra(metrics)) { }
        var initial = renderer.CreateWidgetActionMap();
        Check(initial.HasDerivedGeometry && initial.Rasterize().Count > 0,
            "Intrinsic Text/Value layout must create a nonempty active Panel hotspot even when saved Width/Height are zero.");

        value.Text = "THIS LONG TEXT NOW OCCUPIES A LARGER PANEL";
        using (var frame = renderer.RenderBgra(metrics)) { }
        var updated = renderer.CreateWidgetActionMap();
        Check(!initial.IsEquivalent(updated) && initial.Hit(new Point(-1, -1)) is null,
            "An active derived-size widget must detect Panel geometry changes between rendered frames.");
        value.Action.Activation = C.WidgetActionDefinition.None;
        var disabled = new WidgetActionInteractionMap(board);
        Check(disabled.IsEmpty && !disabled.HasDerivedGeometry && disabled.Rasterize().Count == 0,
            "A derived-size but disabled Action must create no per-frame action geometry work.");
    }

    private static PropertyDescriptorCollection Children(PropertyDescriptorCollection rows) =>
        TypeDescriptor.GetProperties(rows["Action"]?.GetValue(null)
            ?? throw new InvalidOperationException("Missing Action property group."));

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
