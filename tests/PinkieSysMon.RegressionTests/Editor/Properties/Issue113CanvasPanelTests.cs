using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon.Editor;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue113CanvasPanelTests
{
    public static void SchemaMigrationAndIdempotence()
    {
        var legacy = JsonNode.Parse(
            """
            {
              "schemaVersion": 29,
              "canvas": {
                "width": 64,
                "height": 36,
                "orientation": 270,
                "backgroundColor": "#A0123456",
                "backgroundImage": {
                  "asset": { "sourceType": "file", "source": "background.png", "fit": "cover", "loop": false },
                  "color": "#FF112233", "iconOpacity": 0.4
                },
                "foregroundImage": {
                  "asset": { "sourceType": "icon", "source": "lucide:cpu", "fit": "contain", "loop": true },
                  "color": "#FFABCDEF", "iconOpacity": 0.6
                }
              },
              "widgets": []
            }
            """)!.AsObject();

        Check(DashboardSchemaMigration.UpgradeToCurrent(legacy), "Schema 29 must migrate through schema 30 to current.");
        Check(legacy["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion, "Canvas migration must reach current schema.");
        var canvas = legacy["canvas"]!.AsObject();
        Check(canvas["width"]!.GetValue<int>() == 64 &&
              canvas["height"]!.GetValue<int>() == 36 &&
              canvas["orientation"]!.GetValue<int>() == 270,
            "Canvas migration changed its logical dimensions or orientation.");
        Check(canvas["panelBackground"]!["mode"]!.GetValue<string>() == "Image" &&
              canvas["panelBackground"]!["image"]!["source"]!.GetValue<string>() == "background.png" &&
              canvas["panelBackground"]!["image"]!["fit"]!.GetValue<string>() == "cover" &&
              canvas["panelBackground"]!["image"]!["loop"]!.GetValue<bool>() == false &&
              canvas["panelBackground"]!["color"]!.GetValue<string>() == "#A0123456",
            "Active background image must outrank the old color underlay and preserve presentation.");
        Check(canvas["panelForeground"]!["mode"]!.GetValue<string>() == "Icon" &&
              canvas["panelForeground"]!["iconColor"]!.GetValue<string>() == "#FFABCDEF" &&
              Math.Abs(canvas["panelForeground"]!["iconOpacity"]!.GetValue<double>() - 0.6) < 0.0001,
            "Foreground icon source, tint and opacity must survive migration.");
        Check(canvas["backgroundImage"] is null && canvas["foregroundImage"] is null &&
              canvas["backgroundColor"] is null,
            "Retired Canvas image/color keys must not remain in schema 30.");
        var snapshot = legacy.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(legacy) && legacy.ToJsonString() == snapshot,
            "Repeated schema-30 migration must be strictly idempotent.");

        var plain = JsonNode.Parse(
            """{"schemaVersion":29,"canvas":{"width":32,"height":24,"orientation":0,"backgroundColor":"#80445566","backgroundImage":{"asset":{"sourceType":"file","source":null}},"foregroundImage":{"asset":{"sourceType":"file","source":null}}},"widgets":[]}"""
            )!.AsObject();
        Check(DashboardSchemaMigration.UpgradeToCurrent(plain), "Color-only Canvas migration failed.");
        Check(plain["canvas"]!["panelBackground"]!["mode"]!.GetValue<string>() == "Color" &&
              plain["canvas"]!["panelForeground"]!["mode"]!.GetValue<string>() == "None",
            "Inactive legacy image sources must not override the background color.");
        var dashboard = DashboardJson.ParseCurrent(plain.ToJsonString(),
            Path.Combine(Path.GetTempPath(), "issue113-canvas.json"));
        var saved = DashboardJson.Serialize(dashboard, indented: false);
        var restored = DashboardJson.ParseCurrent(saved,
            Path.Combine(Path.GetTempPath(), "issue113-canvas.json"));
        Check(saved == DashboardJson.Serialize(restored, indented: false),
            "Schema 30 canonical parse/save must be stable.");
        Check(!saved.Contains("\"backgroundImage\"", StringComparison.Ordinal) &&
              !saved.Contains("\"backgroundColor\"", StringComparison.Ordinal),
            "Schema-30 save must not resurrect legacy Canvas keys.");
    }

    public static void EditorPanelProperties()
    {
        var root = new C.CanvasDefinition { Width = 96, Height = 64, CornerRadius = 12f };
        var general = new EditorPropertyView(root, section: EditorPropertySection.General).GetProperties();
        Check(general["Identity"] is not null && general["Canvas"] is not null,
            "Canvas General must preserve root identity and Orientation.");
        var panel = new EditorPropertyView(root, section: EditorPropertySection.Panel).GetProperties();
        foreach (var group in new[] { "Geometry", "Background", "Foreground", "Border", "Shadow" })
            Check(panel[group] is not null, $"Canvas Panel must contain {group}.");
        var geometry = TypeDescriptor.GetProperties(panel["Geometry"]!.GetValue(null)!);
        foreach (var disabled in new[] { "X", "Y", "Rotation", "Padding", "Z" })
            Check(geometry[disabled] is { IsReadOnly: true },
                $"Canvas {disabled} must remain visible but inapplicable.");
        Check(geometry[nameof(C.CanvasDefinition.Width)] is { IsReadOnly: false } &&
              geometry[nameof(C.CanvasDefinition.Height)] is { IsReadOnly: false },
            "Canvas native raster dimensions must remain editable.");
        Check(new EditorPropertyView(root, section: EditorPropertySection.Image).GetProperties().Count == 0,
            "Inactive Canvas layers must not show contextual Image properties.");

        root.PanelForeground.Mode = C.WidgetPanelLayerMode.Image;
        root.PanelForeground.Image.Source = "saved.png";
        var image = new EditorPropertyView(root, section: EditorPropertySection.Image).GetProperties();
        Check(image["Foreground"] is not null,
            "An active Canvas image mode must expose the shared image presentation group.");
        root.PanelForeground.Mode = C.WidgetPanelLayerMode.None;
        Check(root.PanelForeground.Image.Source == "saved.png" &&
              new EditorPropertyView(root, section: EditorPropertySection.Image).GetProperties().Count == 0,
            "Changing modes must preserve inactive Canvas sources without showing stale groups.");
    }

    public static void DrawOrderAlphaAndPhysicalIsolation()
    {
        // Renderers resolve fonts/icons through the owning application root even
        // when this fixture contains no text widgets. Keep the test self-contained.
        var applicationRoot = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-issue113-" + Guid.NewGuid().ToString("N"));
        var dashboardDirectory = Path.Combine(applicationRoot, "dashboards", "issue113");
        try
        {
            Directory.CreateDirectory(Path.Combine(applicationRoot, "config"));
            Directory.CreateDirectory(dashboardDirectory);
            File.WriteAllText(Path.Combine(applicationRoot, "config", "app.json"), "{}");
            var root = new C.CanvasDefinition { Width = 32, Height = 24 };
            root.PanelBackground = new C.WidgetPanelLayerDefinition
            {
                Mode = C.WidgetPanelLayerMode.Color, Color = "#FFFF0000"
            };
            root.PanelForeground = new C.WidgetPanelLayerDefinition
            {
                Mode = C.WidgetPanelLayerMode.Color, Color = "#800000FF"
            };
            root.BorderColor = "#FF00FF00";
            root.BorderWidth = 4f;
    
            var definition = new C.DashboardDefinition
            {
                BaseDirectory = dashboardDirectory,
                Canvas = root
            };
            using (var renderer = new DashboardRenderer(definition))
            using (var frame = renderer.RenderBgra(new Dictionary<string, object?>()))
            {
                var inside = frame.Bitmap.GetPixel(16, 12);
                var edge = frame.Bitmap.GetPixel(16, 1);
                Check(inside.Red > 70 && inside.Blue > 70 && inside.Green < 35,
                    "Canvas Foreground must blend over Background in the interior.");
                Check(edge.Blue > 65 && edge.Green > 70 && edge.Red < 60,
                    "Canvas Border must render over Background, then under Foreground.");
            }
    
            root.CornerRadius = 12f;
            using (var renderer = new DashboardRenderer(definition))
            using (var frame = renderer.RenderBgra(new Dictionary<string, object?>()))
            {
                var corner = frame.Bitmap.GetPixel(0, 0);
                Check(corner.Alpha == 0, "Rounded Canvas corners must be transparent.");
            }
    
            root.ShadowEnabled = false;
            byte[] withoutShadow;
            using (var renderer = new DashboardRenderer(definition))
            using (var jpeg = renderer.RenderJpeg(new Dictionary<string, object?>(), 0, 90))
                withoutShadow = jpeg.Bytes.ToArray();
    
            root.ShadowEnabled = true;
            byte[] withShadow;
            using (var renderer = new DashboardRenderer(definition))
            using (var jpeg = renderer.RenderJpeg(new Dictionary<string, object?>(), 0, 90))
                withShadow = jpeg.Bytes.ToArray();
            Check(withoutShadow.SequenceEqual(withShadow),
                "Canvas Shadow must not affect the physical JPEG wire frame.");
        }
        finally
        {
            if (Directory.Exists(applicationRoot))
                Directory.Delete(applicationRoot, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
