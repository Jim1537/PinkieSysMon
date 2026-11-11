using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void CanvasNormalizedPropertyHierarchy()
    {
        var canvas = new C.CanvasDefinition
        {
            Width = 1920, Height = 480, Orientation = 90
        };
        canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Image;
        canvas.PanelBackground.Image.Source = @"images\background.png";
        canvas.PanelForeground.Mode = C.WidgetPanelLayerMode.Image;
        canvas.PanelForeground.Image.Source = @"images\foreground.png";

        static string Semantic(PropertyDescriptor? value) =>
            (value as IPropertySemanticKeyDescriptor)?.SemanticKey ?? string.Empty;

        var general = CurrentPropertyView(canvas, section: EditorPropertySection.General).GetProperties();
        Assert(general.Count == 2 && general["Identity"] is PropertyGroupPropertyDescriptor &&
               general["Canvas"] is PropertyGroupPropertyDescriptor &&
               GetNestedProperty(general, "Canvas", nameof(C.CanvasDefinition.Orientation)) is { IsReadOnly: false },
            "Canvas General must expose root Identity and editable Orientation.");

        var panel = CurrentPropertyView(canvas, section: EditorPropertySection.Panel).GetProperties();
        Assert(panel.Count == 5 &&
               new[] { "Geometry", "Background", "Foreground", "Border", "Shadow" }
                   .All(name => panel[name] is PropertyGroupPropertyDescriptor),
            "Canvas Panel must expose the same five ordered groups as widgets.");
        var width = GetNestedProperty(panel, "Geometry", nameof(C.CanvasDefinition.Width));
        var height = GetNestedProperty(panel, "Geometry", nameof(C.CanvasDefinition.Height));
        Assert(width is { IsReadOnly: false } && height is { IsReadOnly: false } &&
               Semantic(width) == "general.geometry.width" &&
               Semantic(height) == "general.geometry.height",
            "Canvas Width/Height must be editable and retain canonical geometry identities.");
        foreach (var name in new[] { "X", "Y", "Rotation", "Padding", "Z" })
            Assert(GetNestedProperty(panel, "Geometry", name) is { IsReadOnly: true },
                $"Canvas {name} must remain visible but inapplicable.");
        foreach (var (name, role) in new[] { ("Background", "background"), ("Foreground", "foreground") })
            Assert(Semantic(GetNestedProperty(panel, name, "Mode")) == $"panel.{role}.mode" &&
                   Semantic(GetNestedProperty(panel, name, "Source")) == $"panel.{role}.source",
                $"Canvas Panel {name} must own its Mode and Source.");
        Assert(CurrentPropertyView(canvas, section: EditorPropertySection.Appearance).GetProperties().Count == 0,
            "Canvas must not duplicate its Panel background in Appearance.");

        var images = CurrentPropertyView(canvas, section: EditorPropertySection.Image).GetProperties();
        Assert(images.Count == 2 &&
               images["Background"] is PropertyGroupPropertyDescriptor &&
               images["Foreground"] is PropertyGroupPropertyDescriptor,
            "Canvas contextual Image must expose only active Panel artwork.");
        foreach (var (name, role) in new[] { ("Background", "background"), ("Foreground", "foreground") })
            Assert(Semantic(GetNestedProperty(images, name, nameof(C.ImageAssetPresentationDefinition.Fit))) == $"image.panel.{role}.fit" &&
                   Semantic(GetNestedProperty(images, name, nameof(C.ImageAssetPresentationDefinition.Loop))) == $"image.panel.{role}.loop" &&
                   GetNestedProperty(images, name, nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is null,
                $"Canvas Image {name} must own Fit/Loop without file opacity.");

        var widgetPanel = CurrentPropertyView(new C.ValueWidgetDefinition { Id = "canvas-common-parity" },
            section: EditorPropertySection.Panel).GetProperties();
        Assert(Semantic(GetNestedProperty(widgetPanel, "Geometry", nameof(C.WidgetDefinition.Width))) == Semantic(width) &&
               Semantic(GetNestedProperty(widgetPanel, "Geometry", nameof(C.WidgetDefinition.Height))) == Semantic(height),
            "Canvas and widget geometry must share common semantic identities.");

        foreach (var section in new[] { EditorPropertySection.Data, EditorPropertySection.Gauge,
                     EditorPropertySection.States, EditorPropertySection.Text, EditorPropertySection.Graph })
            Assert(CurrentPropertyView(canvas, section: section).GetProperties().Count == 0,
                $"Canvas must not expose unrelated {section} controls.");
    }

    private static void CanvasImageSourcePresentationNormalization()
    {
        var canvas = new C.CanvasDefinition();
        canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Icon;
        canvas.PanelBackground.Icon.Source = "lucide:cpu";
        canvas.PanelForeground.Mode = C.WidgetPanelLayerMode.Image;
        canvas.PanelForeground.Image.Source = @"images\foreground.png";

        var root = Path.GetTempPath();
        var general = CurrentPropertyView(canvas, section: EditorPropertySection.General).GetProperties();
        var panel = CurrentPropertyView(canvas, section: EditorPropertySection.Panel,
            applicationRoot: root, dashboardDirectory: root).GetProperties();
        var images = CurrentPropertyView(canvas, section: EditorPropertySection.Image,
            applicationRoot: root, dashboardDirectory: root).GetProperties();
        Assert(general["Background"] is null && general["Foreground"] is null,
            "Canvas artwork source selection must not leak into General.");
        foreach (var name in new[] { "Background", "Foreground" })
        {
            Assert(GetNestedProperty(panel, name, "Mode") is not null &&
                   GetNestedProperty(panel, name, "Source") is not null,
                $"Canvas Panel {name} must own active source selection.");
            Assert(GetNestedProperty(images, name, "Fit") is not null &&
                   GetNestedProperty(images, name, "Loop") is not null &&
                   GetNestedProperty(images, name, "Source") is null,
                $"Canvas Image {name} must own presentation but not Source.");
        }

        var widget = new C.ImageWidgetDefinition { Id = "shared-image-picker", Width = 50, Height = 50 };
        widget.Asset.SourceType = C.ImageAssetSourceType.Icon;
        widget.Asset.Source = "lucide:cpu";
        var widgetImage = CurrentPropertyView(widget, section: EditorPropertySection.Image,
            applicationRoot: root, dashboardDirectory: root).GetProperties();
        var panelSource = GetNestedProperty(panel, "Background", "Source");
        var widgetSource = GetNestedProperty(widgetImage, "Image", "Source");
        Assert(panelSource is not null && widgetSource is not null &&
               panelSource.ComponentType == typeof(C.WidgetPanelLayerDefinition) &&
               widgetSource.ComponentType == typeof(C.ImageAssetPresentationDefinition) &&
               panelSource.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor &&
               widgetSource.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "Canvas Panel and Image content must share the asset source picker.");

        Assert(GetNestedProperty(images, "Background", nameof(C.WidgetPanelLayerDefinition.IconColor)) is not null &&
               GetNestedProperty(images, "Background", nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is not null &&
               GetNestedProperty(images, "Foreground", nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is null,
            "Canvas icon-only settings must not be shown for a file image.");
        canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Image;
        canvas.PanelBackground.Image.Source = "photo.png";
        canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Icon;
        Assert(canvas.PanelBackground.Icon.Source == "lucide:cpu" &&
               canvas.PanelBackground.Image.Source == "photo.png",
            "Canvas layer mode changes must preserve dormant image and icon sources.");
    }

    private static void CanvasSchema16GeometryMigration()
    {
        var root = JsonNode.Parse(
            """
            {
              "schemaVersion": 16,
              "canvas": {
                "width": 777,
                "height": 888,
                "orientation": 90,
                "background": "images\\background.png",
                "backgroundColor": "#112233",
                "foreground": "images\\foreground.png"
              },
              "widgets": []
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 16 Canvas migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 16 dashboard must migrate Canvas geometry and image layers to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Canvas migration must end at current schema 21.");

        var canvas = root["canvas"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Canvas is missing.");
        Assert(canvas["width"]!.GetValue<int>() == FrameGeometry.DefaultNativeWidth &&
               canvas["height"]!.GetValue<int>() == FrameGeometry.DefaultNativeHeight,
            "Schema 16 -> 17 migration must materialize the historical 1920x480 native Canvas geometry.");
        Assert(canvas["orientation"]!.GetValue<int>() == 90 &&
               canvas["panelBackground"]!["color"]!.GetValue<string>() == "#112233" &&
               !canvas.ContainsKey("background") && !canvas.ContainsKey("foreground"),
            "Canvas migration must preserve orientation/background color and remove obsolete raw image paths.");

        AssertMigratedCanvasImageLayer(canvas, "panelBackground", @"images\background.png");
        AssertMigratedCanvasImageLayer(canvas, "panelForeground", @"images\foreground.png");
    }

    private static void CanvasSchema17ImageLayerMigration()
    {
        var root = JsonNode.Parse(
            """
            {
              "schemaVersion": 17,
              "canvas": {
                "width": 800,
                "height": 300,
                "orientation": 270,
                "background": "images\\background.png",
                "backgroundColor": "#010203",
                "foreground": null
              },
              "widgets": []
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 17 Canvas migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 17 dashboard must migrate raw Canvas paths through canonical schema 21.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 17 migration must end at current schema 21.");

        var canvas = root["canvas"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Canvas is missing.");
        Assert(canvas["width"]!.GetValue<int>() == 800 &&
               canvas["height"]!.GetValue<int>() == 300 &&
               canvas["orientation"]!.GetValue<int>() == 270 &&
               canvas["panelBackground"]!["color"]!.GetValue<string>() == "#010203",
            "Schema 17 -> current migration must preserve configurable geometry and Canvas appearance.");
        AssertMigratedCanvasImageLayer(canvas, "panelBackground", @"images\background.png");
        AssertMigratedCanvasImageLayer(canvas, "panelForeground", null);
        Assert(!canvas.ContainsKey("background") && !canvas.ContainsKey("foreground"),
            "Schema 17 migration must remove obsolete raw Canvas image-path properties.");
    }

    private static void CanvasImageLayerApplicability()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-applicability");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "static.png"), SkiaSharp.SKColors.Red, 2, 2);
            File.WriteAllBytes(
                Path.Combine(imagesRoot, "animated.gif"),
                Convert.FromBase64String("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var canvas = new C.CanvasDefinition();
            canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Image;
            canvas.PanelBackground.Image.Source = @"images\static.png";

            PropertyDescriptorCollection ImageProperties() => CurrentPropertyView(
                canvas, applicationRoot: tempRoot, dashboardDirectory: dashboardRoot,
                section: EditorPropertySection.Image).GetProperties();

            var file = ImageProperties();
            Assert(GetNestedProperty(file, "Background", "Fit") is { IsReadOnly: false } &&
                   GetNestedProperty(file, "Background", "Loop") is { IsReadOnly: true, Description: "Not applicable for selected Source." } &&
                   GetNestedProperty(file, "Background", nameof(C.WidgetPanelLayerDefinition.IconColor)) is null &&
                   GetNestedProperty(file, "Background", nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is null,
                "Static Canvas file image has no independent tint/opacity or animation Loop.");

            canvas.PanelBackground.Image.Source = @"images\animated.gif";
            Assert(GetNestedProperty(ImageProperties(), "Background", "Loop") is { IsReadOnly: false },
                "Animated Canvas file image enables Loop.");

            canvas.PanelBackground.Mode = C.WidgetPanelLayerMode.Icon;
            canvas.PanelBackground.Icon.Source = "mixed:vector";
            var vector = ImageProperties();
            Assert(GetNestedProperty(vector, "Background", nameof(C.WidgetPanelLayerDefinition.IconColor)) is { IsReadOnly: false } &&
                   GetNestedProperty(vector, "Background", nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is { IsReadOnly: false } &&
                   GetNestedProperty(vector, "Background", "Loop") is { IsReadOnly: true },
                "Tintable static Canvas icon enables tint and opacity but not Loop.");

            canvas.PanelBackground.Icon.Source = "mixed:animated.gif";
            var animated = ImageProperties();
            Assert(GetNestedProperty(animated, "Background", nameof(C.WidgetPanelLayerDefinition.IconColor)) is { IsReadOnly: true } &&
                   GetNestedProperty(animated, "Background", nameof(C.WidgetPanelLayerDefinition.IconOpacity)) is { IsReadOnly: false } &&
                   GetNestedProperty(animated, "Background", "Loop") is { IsReadOnly: false },
                "Animated raster Canvas icon preserves intrinsic color but enables Loop.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasImageLayerRenderOrder()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-render-order");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "background.png"), SkiaSharp.SKColors.Red, 4, 4);
            WriteSolidTestImage(Path.Combine(imagesRoot, "widget.png"), SkiaSharp.SKColors.Lime, 4, 4);
            WriteSolidTestImage(Path.Combine(imagesRoot, "foreground.png"), new SkiaSharp.SKColor(0, 0, 255, 128), 4, 4);

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-layers.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 40,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#FF000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\background.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": "images\\foreground.png",
                      "fit": "stretch",
                      "opacity": 0.5,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "middle",
                      "x": 0,
                      "y": 0,
                      "width": 20,
                      "height": 20,
                      "sourceType": "file",
                      "source": "images\\widget.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var overWidget = bitmap.GetPixel(10, 10);
            var overBackground = bitmap.GetPixel(30, 10);
            Assert(overWidget.Green > 110 && overWidget.Blue > 110 && overWidget.Red < 30,
                "Canvas foreground image must render above widgets with configured opacity.");
            Assert(overBackground.Red > 110 && overBackground.Blue > 110 && overBackground.Green < 30,
                "Canvas background image must render below widgets while foreground remains above all content.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasImageLayerFitModes()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-fit");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteVerticalBandTestImage(Path.Combine(imagesRoot, "bands.png"));
            var dashboardPath = Path.Combine(dashboardRoot, "canvas-fit.json");

            SkiaSharp.SKBitmap Render(string fit)
            {
                var definition = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": {
                        "width": 40,
                        "height": 20,
                        "orientation": 0,
                        "backgroundColor": "#000000",
                        "backgroundImage": {
                          "sourceType": "file",
                          "source": "images\\bands.png",
                          "fit": "{{fit}}",
                          "opacity": 1.0,
                          "loop": true,
                          "color": "#FFFFFFFF"
                        },
                        "foregroundImage": {
                          "sourceType": "file",
                          "source": null,
                          "fit": "stretch",
                          "opacity": 1.0,
                          "loop": true,
                          "color": "#FFFFFFFF"
                        }
                      },
                      "widgets": []
                    }
                    """,
                    dashboardPath);
                using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
                return SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            }

            using var contain = Render(StateVisualProfileContract.FitContain);
            using var cover = Render(StateVisualProfileContract.FitCover);
            using var stretch = Render(StateVisualProfileContract.FitStretch);

            Assert(contain.GetPixel(2, 10).Alpha == 0 &&
                   contain.GetPixel(20, 10).Green > 220,
                "Canvas contain fit must preserve aspect ratio and leave a transparent letterbox without the retired background-color underlay.");
            Assert(cover.GetPixel(2, 10).Green > 220,
                "Canvas cover fit must preserve aspect ratio while filling and clipping the Canvas.");
            Assert(stretch.GetPixel(20, 1).Red > 220 && stretch.GetPixel(20, 18).Blue > 220,
                "Canvas stretch fit must map the complete source image across the Canvas surface.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasGlobalIconImageLayerRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-icon-rendering");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var dashboardPath = Path.Combine(dashboardRoot, "canvas-icon.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "icon",
                      "source": "mixed:vector",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FF00FF00"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": null,
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var center = bitmap.GetPixel(10, 10);
            Assert(center.Green > 220 && center.Red < 40 && center.Blue < 40,
                "Canvas image layers must render tintable global icons through the shared icon asset pipeline.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasAnimatedImageLayerRefresh()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-animation");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            File.WriteAllBytes(
                Path.Combine(imagesRoot, "animated.gif"),
                Convert.FromBase64String("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-animation.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\animated.gif",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": null,
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            Assert(preview.RequiresFrequentRefresh && preview.RecommendedRefreshIntervalMs == 250,
                "Animated Canvas image layers must participate in the standard preview animation refresh cadence.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void SharedImageVisualPreservesFileFallback()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-image-visual-file-fallback");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "red.png"), SkiaSharp.SKColors.Red, 2, 2);
            var dashboardPath = Path.Combine(dashboardRoot, "image-fallback.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "legacy-file-fallback",
                      "x": 0,
                      "y": 0,
                      "width": 20,
                      "height": 20,
                      "sourceType": "",
                      "source": "images\\red.png",
                      "fit": "stretch",
                      "opacity": 1,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var center = bitmap.GetPixel(10, 10);
            Assert(center.Red > 220 && center.Green < 40 && center.Blue < 40,
                "Shared ImageVisual must preserve the Image-widget contract where blank SourceType falls back to file.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasCurrentSchemaRejectsLegacyPaths()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-current-schema-contract");
        try
        {
            var rejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": {
                        "width": 64,
                        "height": 32,
                        "orientation": 0,
                        "backgroundColor": "#000000",
                        "background": null,
                        "foreground": null
                      },
                      "widgets": []
                    }
                    """,
                    Path.Combine(tempRoot, "dashboards", "legacy-current.json"));
            }
            catch (InvalidDataException ex)
            {
                rejected = ex.Message.Contains("obsolete Background/Foreground", StringComparison.Ordinal);
            }

            Assert(rejected,
                "Current Canvas schema must reject obsolete raw Background/Foreground path properties.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasOrientationRenderMatrix()
    {
        const int nativeWidth = 40;
        const int nativeHeight = 24;

        foreach (var orientation in new[] { 0, 90, 180, 270 })
        {
            var logical = FrameGeometry.GetLogicalSize(nativeWidth, nativeHeight, orientation);
            using var logicalSurface = SkiaSharp.SKSurface.Create(
                new SkiaSharp.SKImageInfo(logical.Width, logical.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create logical Canvas orientation test surface.");
            using (var paint = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill })
            {
                var halfWidth = logical.Width / 2f;
                var halfHeight = logical.Height / 2f;

                paint.Color = SkiaSharp.SKColors.Red;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(0, 0, halfWidth, halfHeight), paint);
                paint.Color = SkiaSharp.SKColors.Lime;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(halfWidth, 0, logical.Width, halfHeight), paint);
                paint.Color = SkiaSharp.SKColors.Blue;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(0, halfHeight, halfWidth, logical.Height), paint);
                paint.Color = SkiaSharp.SKColors.Yellow;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(halfWidth, halfHeight, logical.Width, logical.Height), paint);
            }
            logicalSurface.Canvas.Flush();

            using var wireSurface = SkiaSharp.SKSurface.Create(
                new SkiaSharp.SKImageInfo(nativeWidth, nativeHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create wire Canvas orientation test surface.");
            wireSurface.Canvas.Clear(SkiaSharp.SKColors.Black);
            FrameGeometry.DrawLogicalToWire(
                wireSurface.Canvas,
                logicalSurface,
                logical.Width,
                logical.Height,
                nativeWidth,
                nativeHeight,
                orientation,
                wireRotationDegrees: 0);
            wireSurface.Canvas.Flush();

            using var wireImage = wireSurface.Snapshot();
            using var png = wireImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode Canvas orientation test frame.");
            using var bitmap = SkiaSharp.SKBitmap.Decode(png.ToArray())
                ?? throw new InvalidOperationException("Could not decode Canvas orientation test frame.");

            var cornerClasses = new HashSet<string>(StringComparer.Ordinal)
            {
                ClassifyCorner(bitmap.GetPixel(3, 3)),
                ClassifyCorner(bitmap.GetPixel(nativeWidth - 4, 3)),
                ClassifyCorner(bitmap.GetPixel(3, nativeHeight - 4)),
                ClassifyCorner(bitmap.GetPixel(nativeWidth - 4, nativeHeight - 4))
            };

            Assert(cornerClasses.SetEquals(new[] { "R", "G", "B", "Y" }),
                $"Canvas orientation {orientation} did not map the complete logical surface onto configured native geometry.");
        }

        static string ClassifyCorner(SkiaSharp.SKColor color)
        {
            if (color.Red > 200 && color.Green < 80 && color.Blue < 80)
                return "R";
            if (color.Green > 180 && color.Red < 80 && color.Blue < 80)
                return "G";
            if (color.Blue > 180 && color.Red < 80 && color.Green < 80)
                return "B";
            if (color.Red > 180 && color.Green > 180 && color.Blue < 80)
                return "Y";
            return "?";
        }
    }

    private static void CanvasRuntimeEditorImageLayerParity()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-runtime-editor-parity");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "background.png"), SkiaSharp.SKColors.Red, 8, 8);
            WriteSolidTestImage(Path.Combine(imagesRoot, "widget.png"), SkiaSharp.SKColors.Lime, 8, 8);
            WriteSolidTestImage(Path.Combine(imagesRoot, "foreground.png"), SkiaSharp.SKColors.Blue, 8, 8);

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-parity.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 80,
                    "height": 40,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\background.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": "images\\foreground.png",
                      "fit": "stretch",
                      "opacity": 0.5,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "center",
                      "x": 20,
                      "y": 10,
                      "width": 40,
                      "height": 20,
                      "sourceType": "file",
                      "source": "images\\widget.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var previewBitmap = SkiaSharp.SKBitmap.Decode(
                preview.RenderToPng(new Dictionary<string, object?>()))
                ?? throw new InvalidOperationException("Could not decode Canvas preview parity frame.");

            using var renderer = new DashboardRenderer(CurrentDashboard(definition));
            using var frame = renderer.RenderJpeg(
                new Dictionary<string, object?>(),
                wireRotationDegrees: 0,
                jpegQuality: 100);
            using var runtimeBitmap = SkiaSharp.SKBitmap.Decode(frame.EncodedData)
                ?? throw new InvalidOperationException("Could not decode Canvas runtime parity frame.");

            Assert(previewBitmap.Width == runtimeBitmap.Width && previewBitmap.Height == runtimeBitmap.Height,
                "Runtime and Editor must use the same configured Canvas dimensions at orientation 0.");

            AssertColorsNear(previewBitmap.GetPixel(5, 5), runtimeBitmap.GetPixel(5, 5), 35,
                "Runtime and Editor background/foreground Canvas composition diverged.");
            AssertColorsNear(previewBitmap.GetPixel(40, 20), runtimeBitmap.GetPixel(40, 20), 35,
                "Runtime and Editor widget/foreground Canvas composition diverged.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        static void AssertColorsNear(
            SkiaSharp.SKColor expected,
            SkiaSharp.SKColor actual,
            int tolerance,
            string message)
        {
            Assert(Math.Abs(expected.Red - actual.Red) <= tolerance &&
                   Math.Abs(expected.Green - actual.Green) <= tolerance &&
                   Math.Abs(expected.Blue - actual.Blue) <= tolerance,
                $"{message} Preview={expected}; Runtime={actual}.");
        }
    }

    private static void CanvasLargeLogicalGeometryHitTesting()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-large-logical-hit-test");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "marker.png"), SkiaSharp.SKColors.White, 4, 4);
            var dashboardPath = Path.Combine(dashboardRoot, "large-logical.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 2300,
                    "height": 640,
                    "orientation": 90,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "far-edge",
                      "x": 100,
                      "y": 2100,
                      "width": 120,
                      "height": 60,
                      "sourceType": "file",
                      "source": "images\\marker.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            var canonicalDefinition = PropertyModelNormalizationMigration.MapLegacyDefinition(definition);
            var widget = canonicalDefinition.Widgets.Single();
            using var preview = new DashboardPreviewRenderer(canonicalDefinition);
            Assert(preview.Width == 640 && preview.Height == 2300,
                "Configured 90-degree Canvas geometry must expose the complete large logical surface.");
            Assert(preview.HitTestWidget(widget, new Dictionary<string, object?>(), 110, 2110),
                "Editor hit testing must use configured logical Canvas coordinates beyond the historical 1920-pixel extent.");
            Assert(!preview.HitTestWidget(widget, new Dictionary<string, object?>(), 20, 20),
                "Editor hit testing must not alias far-edge geometry onto historical Canvas coordinates.");

            var outline = preview.GetWidgetOutline(widget, new Dictionary<string, object?>());
            Assert(outline.Max(point => point.Y) > 2100f,
                "Editor selection geometry must preserve coordinates across the complete configured logical Canvas.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void ConfigurableCanvasGeometry()
    {
        var landscape = FrameGeometry.GetLogicalSize(800, 300, 0);
        var upsideDown = FrameGeometry.GetLogicalSize(800, 300, 180);
        var clockwise = FrameGeometry.GetLogicalSize(800, 300, 90);
        var counterClockwise = FrameGeometry.GetLogicalSize(800, 300, 270);

        Assert(landscape.Width == 800 && landscape.Height == 300 &&
               upsideDown.Width == 800 && upsideDown.Height == 300,
            "Canvas 0/180 orientation must retain configured native Width x Height.");
        Assert(clockwise.Width == 300 && clockwise.Height == 800 &&
               counterClockwise.Width == 300 && counterClockwise.Height == 800,
            "Canvas 90/270 orientation must swap configured native Width/Height.");

        var invalidWidthRejected = false;
        try
        {
            _ = FrameGeometry.GetLogicalSize(0, 300, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidWidthRejected = true;
        }
        Assert(invalidWidthRejected, "Canvas Width must reject non-positive values.");

        var invalidHeightRejected = false;
        try
        {
            _ = FrameGeometry.GetLogicalSize(800, -1, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidHeightRejected = true;
        }
        Assert(invalidHeightRejected, "Canvas Height must reject non-positive values.");
    }

    private static void ConfigurableCanvasRenderSurfaces()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-geometry-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "canvas-geometry.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 320,
                    "height": 120,
                    "orientation": 90,
                    "backgroundColor": "#FFFF0000"
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using (var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition)))
            {
                Assert(preview.Width == 120 && preview.Height == 320,
                    $"Editor preview must use logical Canvas geometry; got {preview.Width}x{preview.Height}.");

                using var previewBitmap = SkiaSharp.SKBitmap.Decode(
                    preview.RenderToPng(new Dictionary<string, object?>()));
                Assert(previewBitmap.Width == 120 && previewBitmap.Height == 320,
                    "Editor snapshot dimensions must follow the configured logical Canvas size.");
                var previewCenter = previewBitmap.GetPixel(previewBitmap.Width / 2, previewBitmap.Height / 2);
                Assert(previewCenter.Red > 240 && previewCenter.Green < 16 && previewCenter.Blue < 16,
                    "Editor preview did not preserve Canvas content through configurable orientation geometry.");
            }

            using (var renderer = new DashboardRenderer(CurrentDashboard(definition)))
            {
                using var frame = renderer.RenderJpeg(new Dictionary<string, object?>(), wireRotationDegrees: 0, jpegQuality: 92);
                using var wireBitmap = SkiaSharp.SKBitmap.Decode(frame.EncodedData);
                Assert(wireBitmap.Width == 320 && wireBitmap.Height == 120,
                    $"Runtime wire frame must use configured native Canvas geometry; got {wireBitmap.Width}x{wireBitmap.Height}.");
                var wireCenter = wireBitmap.GetPixel(wireBitmap.Width / 2, wireBitmap.Height / 2);
                Assert(wireCenter.Red > 220 && wireCenter.Green < 32 && wireCenter.Blue < 32,
                    "Runtime logical-to-wire transform did not preserve Canvas content for configurable geometry.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PropertyModelSchema18To19Migration()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
            ["canvas"] = new JsonObject
            {
                ["width"] = 1920,
                ["height"] = 480,
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000",
                ["backgroundImage"] = new JsonObject
                {
                    ["sourceType"] = "file",
                    ["source"] = null,
                    ["fit"] = "stretch",
                    ["opacity"] = 1f,
                    ["loop"] = true,
                    ["color"] = "#FFFFFFFF"
                },
                ["foregroundImage"] = new JsonObject
                {
                    ["sourceType"] = "icon",
                    ["source"] = null,
                    ["fit"] = "contain",
                    ["opacity"] = 0.5f,
                    ["loop"] = false,
                    ["color"] = "#80FFFFFF"
                }
            },
            ["widgets"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Value,
                    ["id"] = "value-1",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["valueSource"] = "text",
                    ["text"] = "hello",
                    ["fontSize"] = 20,
                    ["overflowMode"] = "none",
                    ["barImageSource"] = "obsolete-stale.png"
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Image,
                    ["id"] = "image-1",
                    ["width"] = 100,
                    ["height"] = 50,
                    ["sourceType"] = "ICON",
                    ["source"] = "lucide:star",
                    ["fit"] = "COVER",
                    ["loop"] = false,
                    ["opacity"] = 0.5f
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Binary,
                    ["id"] = "binary-1",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["metric"] = "system.test.binary",
                    ["profiles"] = new JsonObject
                    {
                        [BinarySignalContract.TrueKey] = new JsonObject
                        {
                            ["sourceType"] = "value",
                            ["source"] = "stale-image-source",
                            ["fit"] = "contain",
                            ["loop"] = true,
                            ["opacity"] = 1f,
                            ["text"] = new JsonObject { ["overflowMode"] = "none" }
                        }
                    }
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Gauge,
                    ["id"] = "gauge-1",
                    ["width"] = 100,
                    ["height"] = 100,
                    ["metric"] = "system.test.gauge",
                    ["min"] = 0,
                    ["max"] = 100,
                    ["thresholdMode"] = "segmenttransition",
                    ["threshold1Enabled"] = true,
                    ["threshold1Value"] = 50,
                    ["threshold1Color"] = "#FFFF0000",
                    ["thickness"] = 12,
                    ["trackEnabled"] = true,
                    ["trackBackgroundColor"] = "#10000000",
                    ["needleEnabled"] = true,
                    ["needleThickness"] = 3,
                    ["needlePointerLength"] = 7,
                    ["needlePointerThickness"] = 2
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Bar,
                    ["id"] = "bar-1",
                    ["width"] = 200,
                    ["height"] = 20,
                    ["metric"] = "system.test.bar",
                    ["min"] = 0,
                    ["max"] = 100,
                    ["barContentMode"] = "Fill",
                    ["barImageSource"] = "images\\dormant.png",
                    ["barImageFit"] = "Cover",
                    ["barProgressMode"] = "Reveal",
                    ["loop"] = false
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Power,
                    ["id"] = "power-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = PowerMetricContract.BatterySource
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.MediaSystem,
                    ["id"] = "media.system-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = MediaMetricContract.InputSource
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.MediaPlayer,
                    ["id"] = "media.player-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = "media.playback"
                }
            }
        };

        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(changed, "Schema 18 property-model migration must report a change.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Property-model migration must produce schemaVersion 19.");

        var canvas = root["canvas"]!.AsObject();
        Assert(canvas["foregroundImage"]!["asset"]!["sourceType"]!.GetValue<string>() == "icon",
            "Canvas image-layer source identity must move under Asset.");
        Assert(!canvas["foregroundImage"]!.AsObject().ContainsKey("sourceType"),
            "Schema 19 Canvas image layer must not retain top-level SourceType.");

        var widgets = root["widgets"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        JsonObject Find(string id) => widgets.Single(widget => widget["id"]!.GetValue<string>() == id);

        var value = Find("value-1");
        Assert(value["sourceKind"]!.GetValue<string>() == "Text",
            "Value valueSource must migrate to canonical sourceKind casing.");
        Assert(value["textPresentation"]!["fontSize"]!.GetValue<float>() == 20f &&
               value["textPresentation"]!["overflowMode"]!.GetValue<string>() == ValueOverflowContract.None,
            "Value flat text properties must migrate into canonical TextPresentation.");
        Assert(!value.ContainsKey("valueSource") && !value.ContainsKey("fontSize") && !value.ContainsKey("barImageSource"),
            "Schema 19 Value output must drop old and unrelated schema-18 union fields.");

        var image = Find("image-1");
        Assert(image["asset"]!["sourceType"]!.GetValue<string>() == "icon" &&
               image["asset"]!["fit"]!.GetValue<string>() == "cover" &&
               image["asset"]!["source"]!.GetValue<string>() == "lucide:star",
            "Image source fields must migrate into canonical Asset presentation.");
        Assert(!image.ContainsKey("sourceType") && !image.ContainsKey("fit") && !image.ContainsKey("loop"),
            "Schema 19 Image output must not retain top-level asset fields.");

        var binary = Find("binary-1");
        var trueProfile = binary["profiles"]![BinarySignalContract.TrueKey]!.AsObject();
        Assert(trueProfile["contentType"]!.GetValue<string>() == "value",
            "Schema-18 state sourceType=value must migrate to ContentType=value.");
        Assert(trueProfile["asset"]!["source"]!.GetValue<string>() != "stale-image-source" &&
               trueProfile["textPresentation"]!["overflowMode"]!.GetValue<string>() == ValueOverflowContract.None,
            "Value state migration must use a valid dormant default asset and canonicalize text overflow casing.");

        var gauge = Find("gauge-1");
        Assert(gauge["thresholds"]!["mode"]!.GetValue<string>() == "SegmentTransition" &&
               gauge["thresholds"]!["items"]![0]!["value"]!.GetValue<double>() == 50d,
            "Gauge numbered threshold fields must migrate into ThresholdSetDefinition.");
        Assert(gauge["track"]!["thickness"]!.GetValue<float>() == 12f &&
               gauge["needle"]!["pointer"]!["length"]!.GetValue<float>() == 7f,
            "Gauge track and needle/pointer fields must migrate into canonical subobjects.");
        Assert(!gauge.ContainsKey("threshold1Value") && !gauge.ContainsKey("trackEnabled") && !gauge.ContainsKey("needlePointerLength"),
            "Schema 19 Gauge output must drop flat threshold/track/needle fields.");

        var bar = Find("bar-1");
        Assert(bar["contentMode"]!.GetValue<string>() == BarImageContract.ContentModeFill &&
               bar["image"]!["source"]!.GetValue<string>() == "images\\dormant.png" &&
               !bar["image"]!["loop"]!.GetValue<bool>(),
            "Bar migration must preserve dormant image configuration inside the canonical Image subobject.");

        Assert(Find("power-1")["powerSource"]!.GetValue<string>() == PowerMetricContract.BatterySource,
            "Power root Source must migrate to PowerSource.");
        Assert(Find("media.system-1")["mediaSource"]!.GetValue<string>() == MediaMetricContract.InputSource,
            "Media System root Source must migrate to MediaSource.");
        Assert(!Find("media.player-1").ContainsKey("source"),
            "Media Player inert schema-18 root Source must be removed.");
    }

    private static void PropertyModelFullMigrationChain()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 10,
            ["canvas"] = new JsonObject
            {
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000",
                ["background"] = "images\\background.png",
                ["foreground"] = null
            },
            ["widgets"] = new JsonArray()
        };

        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(changed && root["schemaVersion"]!.GetValue<int>() == 19,
            "The complete legacy migration chain must terminate at schemaVersion 19.");
        Assert(root["canvas"]!["width"]!.GetValue<int>() == FrameGeometry.DefaultNativeWidth &&
               root["canvas"]!["height"]!.GetValue<int>() == FrameGeometry.DefaultNativeHeight,
            "Full migration must preserve the schema-16 native Canvas geometry upgrade.");
        Assert(root["canvas"]!["backgroundImage"]!["asset"]!["source"]!.GetValue<string>() == "images\\background.png",
            "Full migration must carry legacy Canvas image paths through schema 18 into the schema-19 Asset contract.");
    }

    private static void CanonicalDashboardJsonStrictUnknownFieldRejection()
    {
        var definition = CreateMinimalCanonicalDashboard();
        var root = JsonNode.Parse(DashboardJson.Serialize(definition, indented: false))!.AsObject();
        root["widgets"]![0]!.AsObject()["obsoleteSyntheticProperty"] = "must fail";

        AssertThrows<InvalidDataException>(
            () => DashboardJson.ParseCurrent(root.ToJsonString(), Path.Combine(Path.GetTempPath(), "dashboard.json")),
            "Schema 21 current-schema deserialization must reject unknown widget fields.");
    }

    private static void CanonicalDashboardJsonRoundTrip()
    {
        var definition = CreateMinimalCanonicalDashboard();
        var json = DashboardJson.Serialize(definition);
        var parsed = DashboardJson.ParseCurrent(json, Path.Combine(Path.GetTempPath(), "dashboard.json"));
        var savedAgain = DashboardJson.Serialize(parsed);

        Assert(parsed.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               parsed.Widgets.Count == 1 &&
               parsed.Widgets[0] is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.SourceKind == PinkieSysMon.DashboardModel.ValueSourceKind.Text &&
               value.Text == "round-trip",
            "Canonical dashboard JSON must deserialize the polymorphic widget into its concrete schema-19 type.");

        var first = JsonNode.Parse(json)!.ToJsonString();
        var second = JsonNode.Parse(savedAgain)!.ToJsonString();
        Assert(first == second,
            "Canonical schema-21 save -> parse -> save must be structurally idempotent for an unchanged dashboard.");
    }

    private static void PropertyModelSchema19MigrationIdempotence()
    {
        var root = JsonNode.Parse(DashboardJson.Serialize(CreateMinimalCanonicalDashboard(), indented: false))!.AsObject();
        root["schemaVersion"] = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;
        var before = root.ToJsonString();
        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(!changed && root.ToJsonString() == before,
            "A schema-19 dashboard must be a no-op for the property-model migration entry point.");
    }

    private static void RuntimeDashboardLoadUsesCanonicalModel()
    {
        const string metric = "system.test.runtime-canonical";
        var root = new JsonObject
        {
            ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
            ["canvas"] = new JsonObject
            {
                ["width"] = 1920,
                ["height"] = 480,
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000"
            },
            ["widgets"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Value,
                    ["id"] = "runtime-value",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["valueSource"] = "Metric",
                    ["metric"] = metric,
                    ["overflowMode"] = ValueOverflowContract.None
                }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), "runtime-canonical-dashboard.json");
        var definition = PinkieSysMon.DashboardModel.DashboardDefinition.Parse(root.ToJsonString(), path);

        Assert(definition.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               definition.Widgets.Single() is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.Metric == metric,
            "Runtime dashboard loading must migrate schema 18 into the canonical schema-21 typed model before use.");
        Assert(DashboardMetricUsage.Collect(definition).SetEquals([metric]),
            "Runtime metric discovery must consume the canonical schema-21 widget model.");
    }

    private static void CanonicalDashboardMetricUsageSemantics()
    {
        const string valueMetric = "system.test.value";
        const string binaryMetric = "system.test.binary";
        const string gaugeMetric = "system.test.gauge";
        var dashboard = new PinkieSysMon.DashboardModel.DashboardDefinition
        {
            Widgets =
            [
                new PinkieSysMon.DashboardModel.ValueWidgetDefinition
                {
                    Id = "literal",
                    SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Text,
                    Text = valueMetric
                },
                new PinkieSysMon.DashboardModel.ValueWidgetDefinition
                {
                    Id = "metric",
                    SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Metric,
                    Metric = valueMetric
                },
                new PinkieSysMon.DashboardModel.BinaryWidgetDefinition
                {
                    Id = "binary",
                    Metric = binaryMetric
                },
                new PinkieSysMon.DashboardModel.GaugeWidgetDefinition
                {
                    Id = "gauge",
                    Metric = gaugeMetric
                },
                new PinkieSysMon.DashboardModel.PowerWidgetDefinition
                {
                    Id = "power",
                    PowerSource = PowerMetricContract.BatterySource
                },
                new PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition
                {
                    Id = "media-system",
                    MediaSource = MediaMetricContract.InputSource
                },
                new PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition
                {
                    Id = "media-player"
                }
            ]
        };

        var metrics = DashboardMetricUsage.Collect(dashboard);
        Assert(metrics.Contains(valueMetric) && metrics.Contains(binaryMetric) && metrics.Contains(gaugeMetric),
            "Canonical metric-backed Value/Binary/Gauge widgets must contribute their telemetry metrics.");
        Assert(metrics.Contains(PowerMetricContract.StateMetric(PowerMetricContract.BatterySource)),
            "Canonical PowerSource must determine the required power state metric.");
        Assert(metrics.Contains(MediaMetricContract.InputAvailable) && metrics.Contains(MediaMetricContract.InputType),
            "Canonical MediaSource=input must require the input availability/type metrics.");
        Assert(metrics.Contains(MediaMetricContract.PlaybackStatus),
            "Canonical Media Player must contribute its fixed playback-status metric without a persisted source property.");
        Assert(!metrics.Contains("system.test.not-a-metric") && metrics.Count == 7,
            "Canonical metric usage must not infer telemetry dependencies from literal text or unrelated values.");
    }

    private static void CanonicalCurrentModelOwnership()
    {
        Assert(MetricContract.DashboardSchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               MetricContract.DashboardSchemaVersion == 31,
            "The executable current dashboard contract must be schema 31.");

        AssertThrows<ArgumentException>(
            () => _ = new EditorPropertyView(new WidgetDefinition
            {
                Type = WidgetTypeContract.Value,
                Id = "legacy-editor-rejection"
            }),
            "The production Property Editor must reject the legacy schema-18 WidgetDefinition DTO.");

        var firstPropertyView = new EditorPropertyView(
            new C.ValueWidgetDefinition { Id = "property-grid-owner-first" },
            section: EditorPropertySection.General);
        var secondPropertyView = new EditorPropertyView(
            new C.ValueWidgetDefinition { Id = "property-grid-owner-second" },
            section: EditorPropertySection.General);
        Assert(ReferenceEquals(firstPropertyView.GetPropertyOwner(pd: null), firstPropertyView) &&
               ReferenceEquals(secondPropertyView.GetPropertyOwner(pd: null), secondPropertyView),
            "Canonical single-selection property views must expose a non-null PropertyGrid owner.");
        using (var propertyGrid = new PropertyGrid { PropertySort = PropertySort.NoSort })
        {
            propertyGrid.SelectedObject = firstPropertyView;
            propertyGrid.SelectedObject = secondPropertyView;
            Assert(ReferenceEquals(propertyGrid.SelectedObject, secondPropertyView),
                "PropertyGrid must replace canonical single-selection views without dereferencing a null custom-type owner.");
        }

        var legacyDashboardType = typeof(DashboardDefinition);
        var legacyWidgetType = typeof(WidgetDefinition);

        static IEnumerable<Type> SignatureTypes(System.Reflection.MethodBase member) =>
            member.GetParameters().Select(parameter => parameter.ParameterType);

        var rendererLegacySurface = typeof(DashboardRenderer)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyDashboardType);
        Assert(!rendererLegacySurface,
            "DashboardRenderer must not retain a schema-18 DashboardDefinition constructor bridge.");

        var outputLegacySurface = typeof(OutputSessionManager)
            .GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .OfType<System.Reflection.MethodBase>()
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyDashboardType);
        Assert(!outputLegacySurface,
            "OutputSessionManager must not retain schema-18 dashboard constructor/reconfigure bridges.");

        var geometryLegacySurface = typeof(PinkieSysMon.Widgets.WidgetGeometry)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Concat(typeof(PinkieSysMon.Widgets.WidgetTransform)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyWidgetType);
        Assert(!geometryLegacySurface,
            "Current widget geometry/render APIs must not expose schema-18 WidgetDefinition overloads.");
    }

    private static void CanonicalTypedWidgetSerialization()
    {
        var binary = new C.BinaryWidgetDefinition { Id = "typed-binary", Metric = "system.test.binary" };
        var power = new C.PowerWidgetDefinition { Id = "typed-power", PowerSource = PowerMetricContract.BatterySource };
        var mediaSystem = new C.MediaSystemWidgetDefinition { Id = "typed-media-system", MediaSource = MediaMetricContract.InputSource };
        var mediaPlayer = new C.MediaPlayerWidgetDefinition { Id = "typed-media-player" };

        var definition = new C.DashboardDefinition
        {
            Widgets =
            [
                new C.ValueWidgetDefinition
                {
                    Id = "typed-value",
                    SourceKind = C.ValueSourceKind.Text,
                    Text = "literal"
                },
                binary,
                new C.GaugeWidgetDefinition
                {
                    Id = "typed-gauge",
                    Metric = "system.test.gauge"
                },
                new C.BarWidgetDefinition
                {
                    Id = "typed-bar",
                    Metric = "system.test.bar",
                    ContentMode = BarImageContract.ContentModeFill,
                    Image = new C.BarImagePresentationDefinition
                    {
                        Source = "images\\dormant.png",
                        Fit = BarImageContract.ImageFitCover,
                        ProgressMode = BarImageContract.ProgressModeReveal,
                        Loop = false
                    }
                },
                new C.ImageWidgetDefinition
                {
                    Id = "typed-image",
                    Asset = new C.ImageAssetPresentationDefinition
                    {
                        SourceType = C.ImageAssetSourceType.Icon,
                        Source = "lucide:star",
                        Fit = StateVisualProfileContract.FitContain,
                        Loop = false
                    }
                },
                power,
                mediaSystem,
                mediaPlayer
            ]
        };

        var root = DashboardJson.SerializeToObjectUnchecked(definition);
        var widgets = root["widgets"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        Assert(widgets.Length == 8,
            "Canonical serialization must persist all eight concrete widget types without a flat union placeholder.");

        JsonObject Find(string type) => widgets.Single(widget => widget["type"]!.GetValue<string>() == type);
        var value = Find(WidgetTypeContract.Value);
        var gauge = Find(WidgetTypeContract.Gauge);
        var bar = Find(WidgetTypeContract.Bar);
        var image = Find(WidgetTypeContract.Image);
        var powerJson = Find(WidgetTypeContract.Power);
        var mediaSystemJson = Find(WidgetTypeContract.MediaSystem);
        var mediaPlayerJson = Find(WidgetTypeContract.MediaPlayer);

        Assert(value.ContainsKey("sourceKind") && value.ContainsKey("textPresentation") &&
               !value.ContainsKey("valueSource") && !value.ContainsKey("fontFamily"),
            "Value serialization must use SourceKind/TextPresentation and must not emit schema-18 flat aliases.");
        Assert(gauge.ContainsKey("thresholds") && gauge.ContainsKey("track") && gauge.ContainsKey("needle") &&
               !gauge.ContainsKey("threshold1Value") && !gauge.ContainsKey("trackEnabled") && !gauge.ContainsKey("needlePointerLength"),
            "Gauge serialization must own thresholds, track and needle as canonical subobjects.");
        Assert(bar.ContainsKey("contentMode") && bar.ContainsKey("image") &&
               !bar.ContainsKey("barImageSource") && !bar.ContainsKey("barImageFit"),
            "Bar serialization must own its progress image through BarImagePresentationDefinition.");
        Assert(image.ContainsKey("asset") && !image.ContainsKey("sourceType") && !image.ContainsKey("source"),
            "Image serialization must own source identity through Asset.");
        Assert(powerJson.ContainsKey("powerSource") && !powerJson.ContainsKey("source"),
            "Power serialization must persist PowerSource instead of overloaded Source.");
        Assert(mediaSystemJson.ContainsKey("mediaSource") && !mediaSystemJson.ContainsKey("source"),
            "Media System serialization must persist MediaSource instead of overloaded Source.");
        Assert(!mediaPlayerJson.ContainsKey("source") && !mediaPlayerJson.ContainsKey("metric"),
            "Media Player must persist neither an overloaded Source nor its derived playback metric.");

        var removedUnionFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "valueSource", "sourceType", "barImageSource", "barImageFit", "barProgressMode",
            "threshold1Enabled", "threshold1Value", "threshold1Color",
            "threshold2Enabled", "threshold2Value", "threshold2Color",
            "threshold3Enabled", "threshold3Value", "threshold3Color",
            "trackEnabled", "trackBackgroundColor", "trackBorderColor", "trackBorderWidth", "trackCornerRadius",
            "needleEnabled", "needleThickness", "needleColor", "needleStartOffset", "needleEndOffset",
            "needlePointerLength", "needlePointerThickness", "needlePointerColor"
        };
        Assert(widgets.All(widget => widget.Select(pair => pair.Key).All(key => !removedUnionFields.Contains(key))),
            "Canonical typed serialization must not resurrect removed schema-18 flat-union fields.");
        Assert(widgets.All(widget => !widget.ContainsKey("Type")),
            "Canonical typed serialization must persist only the lowercase polymorphic 'type' discriminator, not the computed runtime Type property.");
    }

    private static void CanonicalSharedTextPresentationParity()
    {
        static C.TextPresentationDefinition Presentation() => new()
        {
            FontFamily = "Roboto",
            FontSize = 23f,
            FontWeight = 600,
            Italic = true,
            Align = "center",
            VerticalAlign = "middle",
            OutlineWidth = 2f,
            OutlineColor = "#FF112233",
            OverflowMode = ValueOverflowContract.Bump,
            ScrollSpeed = 37f,
            BumpPauseMs = 777
        };

        var value = new C.ValueWidgetDefinition
        {
            Id = "text-parity-value",
            SourceKind = C.ValueSourceKind.Text,
            Text = "value",
            TextPresentation = Presentation()
        };
        var state = new C.StateVisualProfileDefinition
        {
            ContentType = C.StateContentType.Value,
            TextPresentation = Presentation()
        };

        var direct = value.GetTextPresentation();
        var stateRender = state.ToRenderProfile().Text;
        Assert(direct == stateRender,
            "Value and state-value rendering must consume the same canonical TextPresentationDefinition semantics.");

        var dashboard = new C.DashboardDefinition
        {
            Widgets =
            [
                value,
                new C.BinaryWidgetDefinition
                {
                    Id = "text-parity-binary",
                    Metric = FixtureOutputFps,
                    Profiles = new Dictionary<string, C.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        [BinarySignalContract.TrueKey] = state
                    }
                }
            ]
        };
        var root = DashboardJson.SerializeToObjectUnchecked(dashboard);
        var widgets = root["widgets"]!.AsArray();
        var valueText = widgets[0]!["textPresentation"]!.AsObject();
        var stateText = widgets[1]!["profiles"]![BinarySignalContract.TrueKey]!["textPresentation"]!.AsObject();
        Assert(valueText.Select(pair => pair.Key).OrderBy(key => key).SequenceEqual(
                   stateText.Select(pair => pair.Key).OrderBy(key => key), StringComparer.Ordinal),
            "Value and state-value JSON must expose the same canonical text-presentation field set.");
    }

    private static void CanonicalImageSourceKindTransitionClearsSource()
    {
        static void AssertBothDirections(C.ImageAssetPresentationDefinition asset, string context)
        {
            asset.SourceType = C.ImageAssetSourceType.File;
            asset.Source = "images\\example.png";
            asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
            Assert(C.ImageAssetSourceType.IsIcon(asset.SourceType) && asset.Source is null,
                $"{context} file -> icon transition must clear Source.");

            asset.Source = "lucide:star";
            asset.ChangeSourceType(C.ImageAssetSourceType.File);
            Assert(C.ImageAssetSourceType.IsFile(asset.SourceType) && asset.Source is null,
                $"{context} icon -> file transition must clear Source.");
        }

        var image = new C.ImageWidgetDefinition { Id = "transition-image" };
        AssertBothDirections(image.Asset, "Image widget");

        var canvas = new C.CanvasDefinition();
        AssertBothDirections(canvas.PanelBackground.Image, "Canvas Panel image asset");
        AssertBothDirections(canvas.PanelBackground.Icon, "Canvas Panel icon asset");

        var profile = new C.StateVisualProfileDefinition { ContentType = C.StateContentType.Image };
        AssertBothDirections(profile.Asset, "State image profile");
    }

}
