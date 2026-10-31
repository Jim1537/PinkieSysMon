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
    private static void QuantitativeForegroundColorSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-quantitative-foreground-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "quantitative-foreground-schema.json");

            var obsoleteFillColorRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "gauge",
                          "id": "gauge-obsolete-fill",
                          "width": 100,
                          "height": 100,
                          "metric": "{{FixtureOutputFps}}",
                          "min": 0,
                          "max": 100,
                          "fillColor": "#FF0000FF"
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteFillColorRejected = true;
            }
            Assert(obsoleteFillColorRejected,
                "Current dashboard schema must reject Gauge FillColor; Gauge base indicator color is Foreground/Color.");

            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "gauge",
                      "id": "gauge-foreground-color",
                      "width": 100,
                      "height": 100,
                      "metric": "{{FixtureOutputFps}}",
                      "min": 0,
                      "max": 100,
                      "color": "#FF123456"
                    }
                  ]
                }
                """,
                dashboardPath);
            Assert(definition.Widgets.Count == 1 &&
                   string.Equals(definition.Widgets[0].Color, "#FF123456", StringComparison.Ordinal),
                "Current Gauge schema must accept universal Color as the base indicator color.");

            var obsoleteBarFillColorRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "bar",
                          "id": "bar-obsolete-fill",
                          "width": 100,
                          "height": 20,
                          "metric": "{{FixtureOutputFps}}",
                          "min": 0,
                          "max": 100,
                          "fillColor": "#FF00FF00"
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteBarFillColorRejected = true;
            }
            Assert(obsoleteBarFillColorRejected,
                "Current dashboard schema must reject Bar FillColor; Fill-mode Bar base indicator color is Foreground/Color.");

            var barDefinition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "bar",
                      "id": "bar-foreground-color",
                      "width": 100,
                      "height": 20,
                      "metric": "{{FixtureOutputFps}}",
                      "min": 0,
                      "max": 100,
                      "color": "#FF654321"
                    }
                  ]
                }
                """,
                dashboardPath);
            Assert(barDefinition.Widgets.Count == 1 &&
                   string.Equals(barDefinition.Widgets[0].Color, "#FF654321", StringComparison.Ordinal),
                "Current Fill-mode Bar schema must accept universal Color as the base indicator color.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BarForegroundColorRendering()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-foreground-rendering-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "bar-foreground-rendering",
                Type = WidgetTypeContract.Bar,
                Width = 40,
                Height = 10,
                Metric = metric,
                Min = 0,
                Max = 100,
                BarContentMode = BarImageContract.ContentModeFill,
                Color = "#FF123456"
            };

            using var images = new ImageAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 10))
                ?? throw new InvalidOperationException("Could not create Bar foreground rendering regression surface.");
            using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect Bar foreground rendering regression pixels.");
            var sample = pixels.GetPixelColor(5, 5);
            Assert(sample.Red == 0x12 && sample.Green == 0x34 && sample.Blue == 0x56 && sample.Alpha == 0xFF,
                "Fill-mode Bar must render its base indicator from universal Color.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BarImagePropertyVisibility()
    {
        var widget = new C.BarWidgetDefinition
        {
            Id = "bar-image-properties",
            Width = 100f,
            Height = 20f,
            Metric = FixtureOutputFps,
            Min = 0,
            Max = 100,
            ContentMode = BarImageContract.ContentModeFill,
            Image = new C.BarImagePresentationDefinition
            {
                Source = "bar.png",
                Fit = BarImageContract.ImageFitContain,
                ProgressMode = BarImageContract.ProgressModeReveal,
                Loop = true
            }
        };

        var fillAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var fillImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(fillAppearance, "Foreground", nameof(C.WidgetDefinition.Color)) is not null,
            "Fill-mode Bar must expose universal Color.");
        Assert(fillAppearance["ThresholdMode"] is not null &&
               fillAppearance["Threshold 1"] is not null,
            "Fill-mode Bar must expose threshold controls.");
        Assert(fillImage.Count == 0,
            "Fill-mode Bar must hide image-specific properties while retaining dormant image configuration.");

        var modeProperty = GetNestedProperty(
            new EditorPropertyView(widget, section: EditorPropertySection.Gauge).GetProperties(),
            "Bar", "BarContentMode");
        Assert(modeProperty is { DisplayName: "Mode" } &&
               EditorForm.IsPropertyContextSwitch(modeProperty.Name),
            "Issue75: editing Gauge > Bar > Mode must rebuild the applicable property tabs.");

        modeProperty!.SetValue(null, BarImageContract.ContentModeImage);
        var imageAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var imageProperties = new EditorPropertyView(widget, dashboardDirectory: "dashboard", section: EditorPropertySection.Image).GetProperties();
        var imageSource = GetNestedProperty(imageProperties, "Image", "BarImageSource");
        Assert(imageSource?.GetEditor(typeof(UITypeEditor)) is DashboardImageSourceEditor,
            "Issue75: the newly applicable Image tab must provide the existing file picker.");
        Assert(GetNestedProperty(imageProperties, "Image", "BarImageSource") is not null,
            "Image-mode Bar must expose Image Source.");
        Assert(GetNestedProperty(imageProperties, "Image", "BarImageFit") is not null,
            "Image-mode Bar must expose Image Fit.");
        Assert(GetNestedProperty(imageProperties, "Image", "BarProgressMode") is not null,
            "Image-mode Bar must expose Progress Mode.");
        Assert(GetNestedProperty(imageProperties, "Image", "Loop") is not null,
            "Image-mode Bar must expose Loop because animated image playback uses the canonical image Loop setting.");
        Assert(imageAppearance["Foreground"] is null,
            "Image-mode Bar must hide universal foreground Color.");
        Assert(imageAppearance["ThresholdMode"] is null &&
               imageAppearance["Threshold 1"] is null &&
               imageAppearance["Threshold 2"] is null &&
               imageAppearance["Threshold 3"] is null,
            "Image-mode Bar must hide inactive threshold controls.");
        Assert(widget.Image.Source == "bar.png" &&
               widget.Image.Fit == BarImageContract.ImageFitContain &&
               widget.Image.ProgressMode == BarImageContract.ProgressModeReveal &&
               widget.Image.Loop,
            "Switching Bar content mode must preserve dormant canonical image configuration.");

        modeProperty.SetValue(null, BarImageContract.ContentModeFill);
        Assert(new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties().Count == 0 &&
               GetNestedProperty(new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties(),
                   "Foreground", nameof(C.WidgetDefinition.Color)) is not null,
            "Issue75: returning to Fill must remove the Image tab and restore Fill-only properties.");

        modeProperty.SetValue(null, BarImageContract.ContentModeImage);
        Assert(GetNestedProperty(new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties(),
                   "Image", "BarImageSource") is not null &&
               widget.Image.Source == "bar.png" && widget.Image.ProgressMode == BarImageContract.ProgressModeReveal,
            "Issue75: repeated Fill/Image switches must restore the picker without losing image settings.");
    }

    private static void BarImageProgressModes()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-progress-regression");
        try
        {
            var source = "bar-progress.png";
            WriteHorizontalQuarterTestImage(Path.Combine(tempRoot, source));

            SkiaSharp.SKColor RenderSample(string progressMode, bool reverse, int x)
            {
                var widget = new WidgetDefinition
                {
                    Id = $"bar-{progressMode}-{reverse}",
                    Type = WidgetTypeContract.Bar,
                    X = 0,
                    Y = 0,
                    Width = 40,
                    Height = 10,
                    Metric = metric,
                    Min = 0,
                    Max = 100,
                    BarContentMode = BarImageContract.ContentModeImage,
                    BarImageSource = source,
                    BarImageFit = BarImageContract.ImageFitStretch,
                    BarProgressMode = progressMode,
                    Reverse = reverse
                };

                using var images = new ImageAssetCache(tempRoot);
                images.Preload(new[] { CurrentWidget(widget) });
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 25d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 10))
                    ?? throw new InvalidOperationException("Could not create Bar image progress regression surface.");
                using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Bar image progress regression pixels.");
                return pixels.GetPixelColor(x, 5);
            }

            var reveal = RenderSample(BarImageContract.ProgressModeReveal, reverse: false, x: 5);
            Assert(reveal.Red > 240 && reveal.Green < 20 && reveal.Blue < 20,
                "Reveal mode must expose the leading quarter of the prepared image.");

            var slide = RenderSample(BarImageContract.ProgressModeSlide, reverse: false, x: 5);
            Assert(slide.Red > 240 && slide.Green > 240 && slide.Blue < 20,
                "Slide mode must bring the trailing quarter in from the start edge.");

            var scaleStart = RenderSample(BarImageContract.ProgressModeScale, reverse: false, x: 1);
            var scaleEnd = RenderSample(BarImageContract.ProgressModeScale, reverse: false, x: 8);
            Assert(scaleStart.Red > 200 && scaleStart.Green < 80 && scaleStart.Blue < 80,
                "Scale mode did not preserve the start of the full prepared image.");
            Assert(scaleEnd.Red > 180 && scaleEnd.Green > 180 && scaleEnd.Blue < 100,
                "Scale mode did not compress the end of the full prepared image into the active range.");

            var reverseReveal = RenderSample(BarImageContract.ProgressModeReveal, reverse: true, x: 35);
            Assert(reverseReveal.Red > 240 && reverseReveal.Green > 240 && reverseReveal.Blue < 20,
                "Reverse Reveal must expose the right-hand quarter of the prepared image.");

            var reverseSlide = RenderSample(BarImageContract.ProgressModeSlide, reverse: true, x: 35);
            Assert(reverseSlide.Red > 240 && reverseSlide.Green < 20 && reverseSlide.Blue < 20,
                "Reverse Slide must bring the left-hand quarter in from the right edge.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BarImageFitModes()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-fit-regression");
        try
        {
            var source = "bar-fit.png";
            WriteVerticalBandTestImage(Path.Combine(tempRoot, source));

            SkiaSharp.SKColor RenderSample(string fit, int x, int y)
            {
                var widget = new WidgetDefinition
                {
                    Id = $"bar-fit-{fit}",
                    Type = WidgetTypeContract.Bar,
                    X = 0,
                    Y = 0,
                    Width = 40,
                    Height = 20,
                    Metric = metric,
                    Min = 0,
                    Max = 100,
                    BarContentMode = BarImageContract.ContentModeImage,
                    BarImageSource = source,
                    BarImageFit = fit,
                    BarProgressMode = BarImageContract.ProgressModeReveal
                };

                using var images = new ImageAssetCache(tempRoot);
                images.Preload(new[] { CurrentWidget(widget) });
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 100d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 20))
                    ?? throw new InvalidOperationException("Could not create Bar image fit regression surface.");
                using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Bar image fit regression pixels.");
                return pixels.GetPixelColor(x, y);
            }

            var clipOutside = RenderSample(BarImageContract.ImageFitClip, 5, 10);
            var clipCenter = RenderSample(BarImageContract.ImageFitClip, 20, 10);
            Assert(clipOutside.Alpha == 0 && clipCenter.Alpha == 255,
                "Clip fit must center the natural-size image without scaling.");

            var containOutside = RenderSample(BarImageContract.ImageFitContain, 5, 10);
            var containInside = RenderSample(BarImageContract.ImageFitContain, 12, 10);
            Assert(containOutside.Alpha == 0 && containInside.Alpha == 255,
                "Contain fit must preserve aspect ratio and leave horizontal letterboxing.");

            var coverTop = RenderSample(BarImageContract.ImageFitCover, 20, 2);
            Assert(coverTop.Green > 200 && coverTop.Red < 80 && coverTop.Blue < 80,
                "Cover fit did not crop the vertically oversized scaled image as expected.");

            var stretchTop = RenderSample(BarImageContract.ImageFitStretch, 20, 2);
            Assert(stretchTop.Red > 200 && stretchTop.Green < 80 && stretchTop.Blue < 80,
                "Stretch fit did not map the full source height into the Bar height.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeUsesUniversalContainerEffects()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-container-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-container",
                Type = WidgetTypeContract.Gauge,
                X = 10,
                Y = 10,
                Width = 40,
                Height = 40,
                Metric = FixtureOutputFps,
                Min = 0,
                Max = 100,
                TrackEnabled = false,
                NeedleEnabled = false,
                BackgroundColor = "#FF102030",
                BorderColor = "#FFFFFFFF",
                BorderWidth = 2f
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(80, 80))
                ?? throw new InvalidOperationException("Could not create gauge container regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge container regression pixels.");
            var center = pixels.GetPixelColor(30, 30);
            var border = pixels.GetPixelColor(10, 30);

            Assert(center.Alpha == 255 && center.Red == 0x10 && center.Green == 0x20 && center.Blue == 0x30,
                "Gauge did not use the universal widget background.");
            Assert(border.Alpha == 255 && border.Red == 255 && border.Green == 255 && border.Blue == 255,
                "Gauge did not use the universal widget border.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeNeedleOffsetsAndPointer()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-needle-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-needle",
                Type = WidgetTypeContract.Gauge,
                X = 0,
                Y = 0,
                Width = 100,
                Height = 100,
                Metric = metric,
                Min = 0,
                Max = 100,
                StartAngle = 0,
                EndAngle = 180,
                TrackEnabled = false,
                NeedleEnabled = true,
                NeedleThickness = 4f,
                NeedleColor = "#FF00FF00",
                NeedleStartOffset = 10f,
                NeedleEndOffset = 10f,
                NeedlePointerLength = 10f,
                NeedlePointerThickness = 8f,
                NeedlePointerColor = "#FFFF0000"
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create gauge needle regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge needle regression pixels.");
            var center = pixels.GetPixelColor(50, 50);
            var body = pixels.GetPixelColor(30, 50);
            var pointer = pixels.GetPixelColor(15, 50);
            var beyondEnd = pixels.GetPixelColor(5, 50);

            Assert(center.Alpha == 0, "NeedleStartOffset did not suppress the center segment.");
            Assert(body.Alpha == 255 && body.Green == 255 && body.Red == 0,
                "Gauge needle body was not drawn on the metric axis.");
            Assert(pointer.Alpha == 255 && pointer.Red == 255 && pointer.Green == 0,
                "Gauge pointer did not replace the distal needle segment.");
            Assert(beyondEnd.Alpha == 0, "NeedleEndOffset did not suppress the radial end segment.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeNeedleRendersAboveTrack()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-layer-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-layering",
                Type = WidgetTypeContract.Gauge,
                X = 0,
                Y = 0,
                Width = 100,
                Height = 100,
                Metric = metric,
                Min = 0,
                Max = 100,
                StartAngle = 0,
                EndAngle = 180,
                TrackEnabled = true,
                Thickness = 20f,
                TrackBackgroundColor = "#00000000",
                Color = "#FF0000FF",
                ThresholdMode = "State",
                NeedleEnabled = true,
                NeedleThickness = 4f,
                NeedleColor = "#FFFFFF00",
                NeedleStartOffset = 0f,
                NeedleEndOffset = 0f,
                NeedlePointerLength = 0f
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create gauge layering regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge layering regression pixels.");
            var needle = pixels.GetPixelColor(10, 50);
            var track = pixels.GetPixelColor(10, 54);

            Assert(needle.Alpha == 255 && needle.Red == 255 && needle.Green == 255 && needle.Blue == 0,
                "Gauge needle did not render above the track layer.");
            Assert(track.Alpha == 255 && track.Blue == 255 && track.Red == 0 && track.Green == 0,
                "Gauge track was not present beneath the needle layer.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StaticStateFileAnimationClassification()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-animation-classification");
        try
        {
            const string staticSource = "static-state.png";
            const string animatedSource = "animated-state.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, staticSource),
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAk+Uzr4AAAAASUVORK5CYII="));
            File.WriteAllBytes(
                Path.Combine(tempRoot, animatedSource),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            using var images = new ImageAssetCache(tempRoot);
            using var icons = new IconAssetCache(tempRoot);
            using var visuals = new PinkieSysMon.Widgets.StateIconVisualCache(images, icons);

            var staticProfile = new StateVisualProfile(
                StateVisualProfileContract.SourceFile,
                staticSource,
                null,
                1f,
                StateVisualProfileContract.FitContain,
                true,
                null);
            var animatedProfile = staticProfile with { Source = animatedSource };

            Assert(!visuals.UsesActivationRelativePlayback(staticProfile),
                "A single-frame state image must not enter activation-relative animation playback.");
            Assert(visuals.UsesActivationRelativePlayback(animatedProfile),
                "A multi-frame state image must retain activation-relative animation playback.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StatefulWidgetShadowCacheAnimationContract()
    {
        const string metric = "test.stateful.shadow";
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-shadow-cache");
        try
        {
            const string staticSource = "static-state.png";
            const string animatedSource = "animated-state.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, staticSource),
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAk+Uzr4AAAAASUVORK5CYII="));
            File.WriteAllBytes(
                Path.Combine(tempRoot, animatedSource),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var widget = new WidgetDefinition
            {
                Id = "stateful-shadow-cache",
                Type = WidgetTypeContract.Binary,
                Metric = metric,
                X = 4,
                Y = 4,
                Width = 16,
                Height = 16,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 3f,
                ShadowOffsetX = 1f,
                ShadowOffsetY = 1f
            };
            widget.EnsureStateVisualProfiles();
            var falseProfile = widget.Profiles![BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = staticSource;
            var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = animatedSource;

            var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);
            var currentTrueProfile = currentWidget.Profiles[BinarySignalContract.TrueKey];

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { currentWidget });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { currentWidget });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = false },
                antialias: true,
                typefaces);

            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var falseKey),
                "A static state profile must be eligible for shadow-composite caching.");

            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = true });
            Assert(!context.TryGetShadowContentKey(currentWidget, renderer, out _),
                "An animated active state must remain live instead of freezing inside the shadow cache.");

            currentTrueProfile.ContentType = C.StateContentType.Image;
            currentTrueProfile.Asset.SourceType = C.ImageAssetSourceType.File;
            currentTrueProfile.Asset.Source = staticSource;
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var trueKey),
                "A static replacement state must become eligible for shadow-composite caching.");
            Assert(!falseKey.Equals(trueKey),
                "Changing Binary state must invalidate a cached static state composite even when both states reuse one asset.");

            currentTrueProfile.ContentType = C.StateContentType.Value;
            currentTrueProfile.TextPresentation ??= new C.TextPresentationDefinition();
            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 1 });
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var firstValueKey),
                "A value-backed state must be shadow-cacheable while its rendered text is stable.");
            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 2 });
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var secondValueKey) &&
                   !firstValueKey.Equals(secondValueKey),
                "A value-backed Binary shadow key must invalidate when visible metric text changes within the same state.");

            currentTrueProfile.TextPresentation.OverflowMode = ValueOverflowContract.Scroll;
            Assert(!context.TryGetShadowContentKey(currentWidget, renderer, out _),
                "A scrolling value-backed state must remain live instead of freezing inside the shadow cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void Pass9FStatefulRendererUnificationContract()
    {
        var sharedBase = typeof(PinkieSysMon.Widgets.StateProfileWidgetRenderer);
        foreach (var rendererType in new[]
                 {
                     typeof(PinkieSysMon.Widgets.BinaryWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.PowerWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.MediaSystemWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.MediaPlayerWidgetRenderer)
                 })
        {
            Assert(rendererType.BaseType == sharedBase,
                $"{rendererType.Name} must use the shared state-profile presentation core.");
        }

        var legacyCompiledVisual = typeof(PinkieSysMon.Widgets.StateIconVisualCache).GetNestedType(
            "CompiledVisual",
            System.Reflection.BindingFlags.NonPublic);
        Assert(legacyCompiledVisual is null,
            "StateIconVisualCache must not reintroduce its former duplicate image rendering implementation.");

        var createState = typeof(ImageVisual).GetMethod(
            nameof(ImageVisual.CreateState),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var drawAtElapsed = typeof(ImageVisual).GetMethod(
            nameof(ImageVisual.DrawAtElapsed),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert(createState is not null && drawAtElapsed is not null,
            "Shared ImageVisual must own state-profile compilation and activation-relative drawing.");
    }

}
