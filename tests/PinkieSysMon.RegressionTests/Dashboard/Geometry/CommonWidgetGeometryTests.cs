using System.Text.Json.Nodes;
using PinkieSysMon.Widgets;
using SkiaSharp;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class CommonWidgetGeometryTests
{
    public static void MigrationAndHistory()
    {
        const string legacy = """
            {"schemaVersion":26,"canvas":{"width":200,"height":100},"widgets":[
              {"type":"image","id":"picture","width":32,"height":32,"borderWidth":3,"cornerRadius":4}]}
            """;
        var path = Path.GetFullPath("common-geometry-fixture.json");
        var board = C.DashboardDefinition.Parse(legacy, path);
        var widget = board.Widgets.Single();
        Check(widget.Padding == default && widget.BorderStyle == C.WidgetBorderStyle.Solid &&
              widget.BorderTop && widget.BorderRight && widget.BorderBottom && widget.BorderLeft &&
              widget.BorderWidth == 3 && widget.CornerRadius == 4, "Legacy geometry changed.");

        var before = DashboardJson.SerializeEditorSnapshot(board);
        widget.Padding = new C.WidgetPadding(1, 2, 3, 4);
        widget.BorderStyle = C.WidgetBorderStyle.Dotted;
        for (var mask = 0; mask < 16; mask++)
        {
            widget.BorderTop = (mask & 1) != 0;
            widget.BorderRight = (mask & 2) != 0;
            widget.BorderBottom = (mask & 4) != 0;
            widget.BorderLeft = (mask & 8) != 0;
            var json = DashboardJson.Serialize(board, false);
            var saved = JsonNode.Parse(json)!["widgets"]![0]!;
            Check(saved["padding"]!["left"]!.GetValue<float>() == 4 &&
                  saved["borderRight"]!.GetValue<bool>() == widget.BorderRight, "Wrong serialized sides.");
            var restored = C.DashboardDefinition.Parse(json, path);
            Check(DashboardJson.Serialize(restored, false) == json, "Round trip was not idempotent.");
            restored.Widgets[0] = DashboardJson.CloneWidget(widget);
            Check(DashboardJson.Serialize(restored, false) == json, "Clone lost common geometry.");
            var snapshot = DashboardJson.SerializeEditorSnapshot(board);
            Check(DashboardJson.Serialize(DashboardJson.ParseEditorSnapshot(snapshot, path), false) == json,
                "History restore lost common geometry.");
        }
        var original = DashboardJson.ParseEditorSnapshot(before, path);
        Check(original.Widgets[0].Padding == default && original.Widgets[0].BorderLeft,
            "Restoring the prior snapshot did not restore the prior geometry.");
        widget.BorderStyle = "invalid";
        ExpectInvalid(() => DashboardJson.Serialize(board));
        widget.BorderStyle = C.WidgetBorderStyle.Solid;
        widget.Padding = new C.WidgetPadding(-1, 0, 0, 0);
        ExpectInvalid(() => DashboardJson.Serialize(board));
    }

    public static void RenderBordersAndPadding()
    {
        var widget = new C.ImageWidgetDefinition
        {
            Width = 80, Height = 80, X = 4, Y = 4, CornerRadius = 12,
            BorderWidth = 3.5f, BorderColor = "#80FFFFFF", BackgroundColor = "#00000000"
        };
        var value = new C.ValueWidgetDefinition { X = 20, Y = 100, Width = 80 };
        var root = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        using var fonts = new TypefaceCache(root, [widget, value]);
        using var context = new WidgetRenderContext(new Dictionary<string, object?>(), false, fonts);
        using var images = new ImageAssetCache(root);
        using var icons = new IconAssetCache(root);
        using var renderer = new ImageWidgetRenderer(images, icons);
        using var bitmap = new SKBitmap(88, 88);
        using var canvas = new SKCanvas(bitmap);
        foreach (var style in new[] { C.WidgetBorderStyle.Solid, C.WidgetBorderStyle.Dotted })
        for (int mask = 0; mask < 16; mask++)
        {
            widget.BorderStyle = style;
            widget.BorderTop = (mask & 1) != 0;
            widget.BorderRight = (mask & 2) != 0;
            widget.BorderBottom = (mask & 4) != 0;
            widget.BorderLeft = (mask & 8) != 0;
            context.InvalidateGeometry(widget);
            canvas.Clear(SKColors.Transparent);
            WidgetTransform.Render(canvas, widget, context, renderer);
            canvas.Flush();
            if (style == C.WidgetBorderStyle.Solid)
            {
                // Pixel centers near -110 and -160 degrees on the upper-left corner.
                Check((bitmap.GetPixel(12, 6).Alpha > 0) == widget.BorderTop &&
                      (bitmap.GetPixel(6, 12).Alpha > 0) == widget.BorderLeft,
                    $"Rounded half-corner ownership changed for mask {mask}.");
                if (mask == 15)
                {
                    using var reference = new SKBitmap(88, 88);
                    using var referenceCanvas = new SKCanvas(reference);
                    using var paint = new SKPaint { Color = new SKColor(255, 255, 255, 128),
                        Style = SKPaintStyle.Stroke, StrokeWidth = 3.5f, IsAntialias = false };
                    referenceCanvas.Clear(SKColors.Transparent);
                    referenceCanvas.DrawRoundRect(new SKRect(5.75f, 5.75f, 82.25f, 82.25f), 10.25f, 10.25f, paint);
                    referenceCanvas.Flush();
                    Check(bitmap.Pixels.SequenceEqual(reference.Pixels), "Full Solid must preserve the original outline.");
                }
            }
            for (int side = 0; side < 4; side++)
            {
                var samples = Enumerable.Range(22, 40).Select(i => side switch
                {
                    0 => bitmap.GetPixel(i, 5).Alpha,
                    1 => bitmap.GetPixel(82, i).Alpha,
                    2 => bitmap.GetPixel(i, 82).Alpha,
                    _ => bitmap.GetPixel(5, i).Alpha
                }).ToArray();
                bool enabled = (mask & (1 << side)) != 0;
                Check(samples.Any(alpha => alpha > 0) == enabled, $"Incorrect {style} side {side} for mask {mask}.");
                if (enabled)
                    Check(samples.Contains((byte)0) == (style == C.WidgetBorderStyle.Dotted), "Unexpected border gaps.");
            }
        }
        var before = ValueTextLayout.CreateFixedLayout(value, context, "text");
        value.Padding = new(8, 12, 4, 16);
        var after = ValueTextLayout.CreateFixedLayout(value, context, "text");
        Check(after.FirstBaseline == 108 && after.Bounds.Left == 36 && after.Bounds.Width == 52 &&
              Math.Abs(after.Bounds.Top - 8 - before.Bounds.Top) < .001,
            "Fixed text must retain its outer anchor and measure inside asymmetric padding.");
    }

    public static void GaugeFit()
    {
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "dial", Width = 100, Height = 100, Metric = SystemMetricContract.Uptime,
            Track = new C.GaugeTrackDefinition { Thickness = 45 }
        };
        gauge.Validate(string.Empty);
        gauge.Padding = new(0, 0, 0, 20);
        ExpectInvalid(() => gauge.Validate(string.Empty));
        Check(WidgetResizeGeometry.GetMinimumGaugeSize(gauge) == 110, "Drag minimum omitted padding.");
        gauge.Track.Thickness = 40;
        gauge.Validate(string.Empty);
        gauge.Needle.Enabled = true;
        gauge.Needle.EndOffset = 40;
        ExpectInvalid(() => gauge.Validate(string.Empty));
        var minimum = WidgetResizeGeometry.GetMinimumGaugeSize(gauge);
        Check(minimum > 100, "Needle must have positive length at the drag minimum.");
        gauge.Width = gauge.Height = minimum;
        gauge.Validate(string.Empty);
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected invalid widget geometry to be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
