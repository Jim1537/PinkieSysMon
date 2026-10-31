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
    private static void RuntimeJpegZeroCopyTransportContract()
    {
        var sendJpeg = typeof(TrofeoTransport).GetMethod(
            nameof(TrofeoTransport.SendJpeg),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert(sendJpeg is not null,
            "Trofeo transport must expose the JPEG send entry point.");

        var parameters = sendJpeg!.GetParameters();
        Assert(parameters.Length == 1 && parameters[0].ParameterType == typeof(ReadOnlySpan<byte>),
            "Trofeo JPEG transport must consume a ReadOnlySpan<byte> so encoded SKData does not require a per-frame managed byte[] copy.");

        var renderedType = typeof(DashboardRenderer.RenderedJpeg);
        Assert(typeof(IDisposable).IsAssignableFrom(renderedType),
            "Rendered JPEG ownership must be explicit so the native SKData buffer is disposed after USB transfer.");
        Assert(renderedType.GetProperty(nameof(DashboardRenderer.RenderedJpeg.EncodedData))?.PropertyType == typeof(SkiaSharp.SKData),
            "Rendered JPEG must retain the encoder-owned SKData buffer instead of materializing a managed byte array.");
        Assert(renderedType.GetProperty(nameof(DashboardRenderer.RenderedJpeg.Bytes))?.PropertyType == typeof(ReadOnlySpan<byte>),
            "Rendered JPEG must expose encoded bytes as a zero-copy span over SKData.");
    }

    private static void StableWidgetShadowIsReused()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-shadow",
                Type = WidgetTypeContract.Gauge,
                Metric = FixtureOutputFps,
                X = 10,
                Y = 10,
                Width = 30,
                Height = 30,
                Min = 0,
                Max = 100,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 4f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            var renderer = new CountingWidgetRenderer(WidgetTypeContract.Gauge);

            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);

            Assert(renderer.RenderCount == 1,
                $"Stable shadowed widget rendered {renderer.RenderCount} times; expected one cached render.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void NumericWidgetShadowInvalidatesOnValueChange()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "bar-shadow",
                Type = WidgetTypeContract.Bar,
                Metric = metric,
                X = 10,
                Y = 10,
                Width = 50,
                Height = 10,
                Min = 0,
                Max = 100,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 4f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 25d },
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            var renderer = new CountingWidgetRenderer(WidgetTypeContract.Bar);

            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Unchanged numeric shadow state was not reused.");

            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d });
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 2, "Changed numeric shadow state did not invalidate the cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }


    private static void CachedWidgetShadowFollowsTranslation()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "translated-shadow",
                Type = WidgetTypeContract.Gauge,
                Metric = FixtureOutputFps,
                Min = 0,
                Max = 100,
                X = 10,
                Y = 10,
                Width = 20,
                Height = 12,
                ShadowEnabled = true,
                ShadowOpacity = 1f,
                ShadowBlur = 2f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
                120, 80, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            using var renderer = new SolidBoxCountingWidgetRenderer(WidgetTypeContract.Gauge);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Initial translated-shadow render did not populate the cache.");

            var oldCenterX = (int)(currentWidget.X + currentWidget.Width / 2f);
            var oldCenterY = (int)(currentWidget.Y + currentWidget.Height / 2f);
            currentWidget.X = 70;
            currentWidget.Y = 45;

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Pure widget translation rebuilt a reusable shadow cache entry.");
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect translated shadow regression surface pixels.");
            var newCenterX = (int)(currentWidget.X + currentWidget.Width / 2f);
            var newCenterY = (int)(currentWidget.Y + currentWidget.Height / 2f);
            Assert(pixels.GetPixelColor(newCenterX, newCenterY).Alpha > 0,
                "Cached shadow composite did not move with the widget.");
            Assert(pixels.GetPixelColor(oldCenterX, oldCenterY).Alpha == 0,
                "Cached shadow composite remained at the widget's previous position.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void ResizeLocalAxisProjection()
    {
        var unrotated = WidgetResizeGeometry.ProjectCanvasDeltaToLocal(new System.Drawing.PointF(12f, -7f), 0d);
        Assert(Math.Abs(unrotated.X - 12f) < 0.001f && Math.Abs(unrotated.Y + 7f) < 0.001f,
            "Unrotated resize delta was not preserved.");

        var quarterTurn = WidgetResizeGeometry.ProjectCanvasDeltaToLocal(new System.Drawing.PointF(0f, 10f), 90d);
        Assert(Math.Abs(quarterTurn.X - 10f) < 0.001f && Math.Abs(quarterTurn.Y) < 0.001f,
            "Rotated resize did not follow the widget local X axis.");
    }

    private static void GaugeResizeGeometryClamp()
    {
        var widget = new PinkieSysMon.DashboardModel.GaugeWidgetDefinition
        {
            Width = 200f,
            Height = 200f,
            Gap = 3f,
            Track = new PinkieSysMon.DashboardModel.GaugeTrackDefinition
            {
                Enabled = true,
                Thickness = 20f,
                BorderWidth = 2f
            },
            Needle = new PinkieSysMon.DashboardModel.GaugeNeedleDefinition
            {
                Enabled = true,
                StartOffset = 30f,
                EndOffset = 10f
            }
        };

        var trackMinimum = 2f * (2f * widget.Track.BorderWidth + 2f * widget.Gap + widget.Track.Thickness);
        var needleMinimum = MathF.BitIncrement(2f * (widget.Needle.StartOffset + widget.Needle.EndOffset));
        var expectedMinimum = Math.Max(WidgetResizeGeometry.MinimumDimension, Math.Max(trackMinimum, needleMinimum));
        var actualMinimum = WidgetResizeGeometry.GetMinimumGaugeSize(widget);
        Assert(actualMinimum == expectedMinimum, "Gauge minimum size does not preserve valid track/needle geometry.");

        var expanded = WidgetResizeGeometry.ResolveGaugeSize(widget, 200f, 20f, 20f);
        Assert(Math.Abs(expanded - 220f) < 0.001f, "Gauge diagonal resize did not preserve 1:1 geometry.");

        var clamped = WidgetResizeGeometry.ResolveGaugeSize(widget, 200f, -1000f, -1000f);
        Assert(clamped == expectedMinimum, "Gauge resize did not clamp to its valid minimum size.");
    }

    private static void PreviewResizeGeometryInvalidation()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-preview-resize-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "resize-preview.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 1920,
                    "height": 480,
                    "orientation": 0,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "bar",
                      "id": "resize-bar",
                      "x": 10,
                      "y": 10,
                      "width": 20,
                      "height": 20,
                      "metric": "system.output.desktop-1.fps",
                      "min": 0,
                      "max": 100,
                      "color": "#FFFFFFFF"
                    }
                  ]
                }
                """,
                dashboardPath);
            var canonicalDefinition = PropertyModelNormalizationMigration.MapLegacyDefinition(definition);
            var widget = canonicalDefinition.Widgets.Single();

            using var preview = new DashboardPreviewRenderer(canonicalDefinition);
            var metrics = new Dictionary<string, object?> { [FixtureOutputFps] = 100d };
            using var before = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(metrics));
            Assert(before.GetPixel(35, 15) == SkiaSharp.SKColors.Black,
                "Bar unexpectedly occupied the post-resize area before geometry changed.");

            widget.Width = 40f;
            preview.InvalidateWidgetGeometry(widget);
            using var after = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(metrics));
            Assert(after.GetPixel(35, 15) == SkiaSharp.SKColors.White,
                "Preview renderer kept stale compiled widget geometry after resize invalidation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
