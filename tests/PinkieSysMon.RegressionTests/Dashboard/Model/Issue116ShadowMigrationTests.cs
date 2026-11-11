using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon.Editor;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue116ShadowMigrationTests
{
    public static void SchemaMigrationAndRoundTrip()
    {
        var legacy = JsonNode.Parse(
            """
            {
              "schemaVersion": 31,
              "canvas": {
                "width": 40, "height": 24, "shadowEnabled": false,
                "shadowColor": "#FF000000", "shadowOpacity": 0.5
              },
              "widgets": [
                { "type": "image", "id": "fractional", "shadowEnabled": false,
                  "shadowColor": "#80445566", "shadowOpacity": 0.375,
                  "iconOpacity": 0.7 },
                { "type": "image", "id": "transparent",
                  "shadowColor": "#FF102030", "shadowOpacity": 0.0 },
                { "type": "image", "id": "opaque",
                  "shadowColor": "#80112233", "shadowOpacity": 1.0 },
                { "type": "graph", "id": "defaults", "fillOpacity": 0.4 }
              ]
            }
            """)!.AsObject();

        Check(DashboardSchemaMigration.UpgradeToCurrent(legacy), "Schema 31 must migrate to 32.");
        Check(legacy["schemaVersion"]!.GetValue<int>() == 32, "Shadow migration must own schema 32.");
        var canvas = legacy["canvas"]!.AsObject();
        var widgets = legacy["widgets"]!.AsArray();
        Check(canvas["shadowColor"]!.GetValue<string>() == "#80000000" &&
              canvas["shadowEnabled"]!.GetValue<bool>() == false &&
              canvas["shadowOpacity"] is null,
            "Disabled Canvas shadow must retain the old effective alpha without the redundant field.");
        Check(widgets[0]!["shadowColor"]!.GetValue<string>() == "#30445566" &&
              widgets[0]!["shadowEnabled"]!.GetValue<bool>() == false &&
              widgets[0]!["shadowOpacity"] is null,
            "Nonopaque widget alpha must round 128 * .375 to 48 even while disabled.");
        Check(widgets[1]!["shadowColor"]!.GetValue<string>() == "#00102030" &&
              widgets[2]!["shadowColor"]!.GetValue<string>() == "#80112233" &&
              widgets[3]!["shadowColor"]!.GetValue<string>() == "#80000000",
            "Zero, one, and omitted legacy opacity must preserve old effective alpha.");
        Check(widgets[0]!["iconOpacity"]!.GetValue<double>() == 0.7 &&
              widgets[3]!["fillOpacity"]!.GetValue<double>() == 0.4,
            "Shadow migration must not alter icon or Graph Fill opacity.");
        var migratedJson = legacy.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(legacy) &&
              legacy.ToJsonString() == migratedJson,
            "A second migration must be an exact no-op.");

        var old18 = JsonNode.Parse(
            """
            { "schemaVersion": 18,
              "canvas": { "width": 40, "height": 24 },
              "widgets": [{ "type": "image", "id": "old-image",
                            "shadowColor": "#80445566", "shadowOpacity": 0.25 }]
            }
            """)!.AsObject();
        Check(DashboardSchemaMigration.UpgradeToCurrent(old18) &&
              old18["widgets"]![0]!["shadowColor"]!.GetValue<string>() == "#20445566" &&
              old18["widgets"]![0]!["shadowOpacity"] is null,
            "Schema-18 normalization must preserve historical two-factor shadow alpha exactly once.");

        var invalid = JsonNode.Parse(
            """{"schemaVersion":31,"canvas":{"shadowOpacity":1.25},"widgets":[]}"""
            )!.AsObject();
        Check(Throws<InvalidDataException>(() => DashboardSchemaMigration.UpgradeToCurrent(invalid)),
            "Out-of-range legacy shadow alpha must fail rather than be silently clamped.");

        var fresh = new C.DashboardDefinition
        {
            BaseDirectory = Path.GetTempPath(),
            Canvas = new C.CanvasDefinition { Width = 40, Height = 24 }
        };
        Check(fresh.Canvas.ShadowColor == "#80000000" &&
              new C.ValueWidgetDefinition().ShadowColor == "#80000000",
            "New Canvas and widgets must keep the former opaque-black × .5 default.");
        var saved = DashboardJson.Serialize(fresh, indented: false);
        Check(!saved.Contains("shadowOpacity", StringComparison.Ordinal),
            "New documents must not persist shadowOpacity.");
        var path = Path.Combine(Path.GetTempPath(), "issue116-shadow.json");
        var parsed = DashboardJson.ParseCurrent(saved, path);
        Check(DashboardJson.Serialize(parsed, indented: false) == saved,
            "Canonical schema-32 parse/save must be deterministic.");
    }

    public static void PropertyContractAndDesktopAlpha()
    {
        C.WidgetDefinition[] widgets =
        [
            new C.ValueWidgetDefinition(), new C.BarWidgetDefinition(), new C.GaugeWidgetDefinition(),
            new C.GraphWidgetDefinition(), new C.ImageWidgetDefinition(), new C.BinaryWidgetDefinition(),
            new C.PowerWidgetDefinition(), new C.MediaSystemWidgetDefinition(), new C.MediaPlayerWidgetDefinition()
        ];
        foreach (var widget in widgets)
            CheckShadowGroup(widget, widget.Type);
        CheckShadowGroup(new C.CanvasDefinition(), "Canvas");

        var root = new C.CanvasDefinition
        {
            Width = 40, Height = 24, ShadowEnabled = true,
            ShadowBlur = 0, ShadowOffsetX = 8, ShadowOffsetY = 3,
            ShadowColor = "#40000000"
        };
        var viewport = DesktopCanvasViewport.Create(root);
        Check(viewport.HasShadow && viewport.Surface.Width == 49 &&
              viewport.Surface.Height == 28,
            "RGBA-only Canvas shadows must retain their native margin geometry.");
        using var compositor = new DesktopCanvasShadowCompositor(viewport, root);
        using var blank = new SKBitmap(new SKImageInfo(40, 24,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        blank.Erase(SKColors.Transparent);
        var frame = compositor.Compose(blank);
        Check(Math.Abs(frame.GetPixel(44, 12).Alpha - 64) <= 1,
            "Desktop Canvas contour must use the stored Color alpha directly.");
        root.ShadowColor = "#00000000";
        Check(!DesktopCanvasViewport.Create(root).HasShadow,
            "A transparent RGBA shadow must not allocate a native shadow margin.");

        var widget = new C.ImageWidgetDefinition
        {
            Id = "rgba-widget", Width = 16, Height = 16,
            ShadowEnabled = true, ShadowBlur = 0,
            ShadowOffsetX = 22, ShadowOffsetY = 0,
            ShadowColor = "#30445566",
            PanelBackground = new C.WidgetPanelLayerDefinition
            {
                Mode = C.WidgetPanelLayerMode.Color, Color = "#FFFFFFFF"
            }
        };
        var dashboard = new C.DashboardDefinition
        {
            BaseDirectory = Path.GetTempPath(),
            Canvas = new C.CanvasDefinition
            {
                Width = 64, Height = 32,
                PanelBackground = new C.WidgetPanelLayerDefinition
                {
                    Mode = C.WidgetPanelLayerMode.None
                }
            },
            Widgets = [widget]
        };
        using var renderer = new DashboardRenderer(dashboard);
        using var rendered = renderer.RenderBgra(new Dictionary<string, object?>());
        Check(Math.Abs(rendered.Bitmap.GetPixel(30, 8).Alpha - 48) <= 1,
            "Widget contour shadow must use the migrated RGBA alpha without a second multiplier.");
    }

    private static void CheckShadowGroup(object owner, string description)
    {
        var view = new EditorPropertyView(owner, section: EditorPropertySection.Panel);
        var groups = view.GetProperties();
        Check(groups["Shadow"] is not null, description + " is missing Panel/Shadow.");
        var rows = TypeDescriptor.GetProperties(groups["Shadow"]!.GetValue(null)!);
        var names = rows.Cast<PropertyDescriptor>().Select(row => row.DisplayName);
        Check(names.SequenceEqual(new[] { "Enabled", "Color", "Blur", "Offset X", "Offset Y" }),
            description + " must show five common Shadow properties in canonical order.");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
