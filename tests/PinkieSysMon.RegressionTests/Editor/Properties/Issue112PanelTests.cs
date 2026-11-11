using System.ComponentModel;
using System.Text.Json.Nodes;
using PinkieSysMon.Editor;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue112PanelTests
{
    public static void CommonLayerContractAndContext()
    {
        C.WidgetDefinition[] widgets =
        [
            new C.ValueWidgetDefinition(), new C.BarWidgetDefinition(), new C.GaugeWidgetDefinition(),
            new C.GraphWidgetDefinition(), new C.ImageWidgetDefinition(),
            new C.BinaryWidgetDefinition(), new C.PowerWidgetDefinition(),
            new C.MediaSystemWidgetDefinition(), new C.MediaPlayerWidgetDefinition()
        ];

        foreach (var widget in widgets)
        {
            var panel = new EditorPropertyView(widget, section: EditorPropertySection.Panel).GetProperties();
            foreach (var group in new[] { "Geometry", "Background", "Foreground", "Border", "Shadow" })
                Check(panel[group] is not null, $"{widget.Type}: missing common Panel/{group}.");

            Check(widget.PanelBackground.Mode == C.WidgetPanelLayerMode.None &&
                  widget.PanelForeground.Mode == C.WidgetPanelLayerMode.None,
                $"{widget.Type}: both panel layers must default to None.");
            Check(new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties()["Background"] is null,
                $"{widget.Type}: disabled panel artwork must not introduce an Image/Background group.");
        }

        var image = new C.ImageWidgetDefinition { Id = "triple-image", Width = 36, Height = 32 };
        image.PanelBackground.Mode = C.WidgetPanelLayerMode.Image;
        image.PanelBackground.Image.Source = "background.png";
        image.PanelForeground.Mode = C.WidgetPanelLayerMode.Icon;
        image.PanelForeground.Icon.Source = "lucide:cpu";

        var groups = new EditorPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Check(groups["Background"] is not null && groups["Foreground"] is not null && groups["Image"] is not null,
            "Panel and image-content properties must appear as three distinct groups.");

        var file = Properties(groups, "Background");
        var icon = Properties(groups, "Foreground");
        Check(file["Fit"] is not null && file["Loop"] is not null &&
              file["IconOpacity"] is null && file["IconColor"] is null,
            "File images must have no standalone Opacity or tint.");
        Check(icon["Fit"] is not null && icon["Loop"] is not null &&
              icon["IconOpacity"] is not null,
            "Icons must retain independent Opacity.");

        image.PanelBackground.Mode = C.WidgetPanelLayerMode.None;
        groups = new EditorPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Check(groups["Background"] is null &&
              image.PanelBackground.Image.Source == "background.png",
            "Disabling a panel layer must hide presentation without losing the dormant source.");
    }

    public static void MigrationAndRoundTrip()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 28,
            ["widgets"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "value", ["id"] = "old-text", ["color"] = "#EECCBB00",
                    ["backgroundColor"] = "#99010203"
                },
                new JsonObject
                {
                    ["type"] = "image", ["id"] = "old-image", ["color"] = "#FFFFFFFF",
                    ["backgroundColor"] = "#00000000", ["opacity"] = .25,
                    ["asset"] = new JsonObject { ["sourceType"] = "file", ["source"] = "photo.png" }
                },
                new JsonObject
                {
                    ["type"] = "binary", ["id"] = "old-binary", ["color"] = "#FF123456",
                    ["backgroundColor"] = "#11000000",
                    ["profiles"] = new JsonObject
                    {
                        ["true"] = new JsonObject { ["contentType"] = "value", ["color"] = null, ["opacity"] = 1.0 },
                        ["false"] = new JsonObject
                        {
                            ["contentType"] = "image", ["color"] = null, ["opacity"] = .5,
                            ["asset"] = new JsonObject { ["sourceType"] = "icon", ["source"] = "lucide:check" }
                        }
                    }
                }
            }
        };

        Check(DashboardSchemaMigration.UpgradeToCurrent(root), "Schema 28 must be upgraded.");
        Check(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion, "Current schema not selected.");
        var w = root["widgets"]!.AsArray();
        Check(w[0]!["color"]!.GetValue<string>() == "#EECCBB00" &&
              w[0]!["panelBackground"]!["color"]!.GetValue<string>() == "#99010203" &&
              w[0]!["panelBackground"]!["mode"]!.GetValue<string>() == "Color",
            "Legacy text color and panel background must retain independent ownership.");
        Check(w[1]!["panelBackground"]!["mode"]!.GetValue<string>() == "None" &&
              w[1]!["opacity"] is null && w[1]!["iconOpacity"] is null,
            "File-image Opacity must be removed, not persisted under another name.");
        var profiles = w[2]!["profiles"]!.AsObject();
        Check(profiles["true"]!["color"] is null && profiles["true"]!["opacity"] is null,
            "Default state text color inheritance must survive migration.");
        Check(profiles["false"]!["color"] is null &&
              profiles["false"]!["iconOpacity"]!.GetValue<double>() == .5,
            "State icon opacity and inherited tint must survive migration.");
        var serialized = root.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(root) && root.ToJsonString() == serialized,
            "Current-schema migration must be idempotent.");

        var dashboard = new C.DashboardDefinition
        {
            BaseDirectory = Path.GetTempPath(),
            Canvas = new C.CanvasDefinition { Width = 32, Height = 32 },
            Widgets =
            [
                new C.ImageWidgetDefinition
                {
                    Id = "image", X = 0, Y = 0, Width = 32, Height = 32,
                    PanelBackground = new C.WidgetPanelLayerDefinition
                    {
                        Mode = C.WidgetPanelLayerMode.Color, Color = "#AABBCCDD"
                    }
                }
            ]
        };
        var json = DashboardJson.Serialize(dashboard, indented: false);
        var parsed = DashboardJson.ParseCurrent(json, Path.Combine(Path.GetTempPath(), "panel.json"));
        Check(DashboardJson.Serialize(parsed, indented: false) == json,
            "Current-schema save/parse/save must be deterministic.");
    }

    public static void ThreeLayerRenderingAndFileAlpha()
    {
        var applicationRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        var directory = Path.Combine(applicationRoot, "artifacts", "issue112-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteImage(Path.Combine(directory, "background.png"), SKColors.Red);
            WriteImage(Path.Combine(directory, "content.png"), SKColors.Lime);
            WriteImage(Path.Combine(directory, "foreground.png"), new SKColor(0, 0, 255, 128));

            var widget = new C.ImageWidgetDefinition
            {
                Id = "three-layers", X = 0, Y = 0, Width = 32, Height = 32,
                Padding = new C.WidgetPadding(8, 8, 8, 8),
                Asset = new C.ImageAssetPresentationDefinition
                {
                    SourceType = C.ImageAssetSourceType.File, Source = "content.png",
                    Fit = StateVisualProfileContract.FitStretch
                },
                PanelBackground = new C.WidgetPanelLayerDefinition
                {
                    Mode = C.WidgetPanelLayerMode.Image,
                    Image = new C.ImageAssetPresentationDefinition
                    {
                        SourceType = C.ImageAssetSourceType.File, Source = "background.png",
                        Fit = StateVisualProfileContract.FitStretch
                    }
                },
                PanelForeground = new C.WidgetPanelLayerDefinition
                {
                    Mode = C.WidgetPanelLayerMode.Image,
                    IconOpacity = 0f, // An inactive icon setting must never dim file artwork.
                    Image = new C.ImageAssetPresentationDefinition
                    {
                        SourceType = C.ImageAssetSourceType.File, Source = "foreground.png",
                        Fit = StateVisualProfileContract.FitStretch
                    }
                }
            };
            var dashboard = new C.DashboardDefinition
            {
                BaseDirectory = directory,
                Canvas = new C.CanvasDefinition { Width = 32, Height = 32, BackgroundColor = "#FF000000" },
                Widgets = [widget]
            };
            using var render = new DashboardRenderer(dashboard);
            using var bitmap = render.RenderBgra(new Dictionary<string, object?>());
            var outside = bitmap.Bitmap.GetPixel(3, 3);
            var content = bitmap.Bitmap.GetPixel(16, 16);
            Check(outside.Red > 80 && outside.Blue > 80 && outside.Green < 40,
                "Panel background/foreground must cover the padding area in file-alpha order.");
            Check(content.Green > 80 && content.Blue > 80 && content.Red < 40,
                "Content image must remain between independent panel layers.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static PropertyDescriptorCollection Properties(PropertyDescriptorCollection groups, string group) =>
        TypeDescriptor.GetProperties(groups[group]!.GetValue(null)!);

    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(path, encoded.ToArray());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
