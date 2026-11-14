using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue126FallbackTests
{
    public static void FixedContractAndMigration()
    {
        Check(SystemFallback.Text == "--" && SystemFallback.Icon == "lucide:image-off",
            "System fallback content must remain fixed.");
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        var icons = Path.Combine(root, "assets", "icons", "lucide");
        Directory.CreateDirectory(icons);
        File.Copy(GlobalAssetResolver.ResolveIconFromRoot(
            ApplicationRootLocator.Find(AppContext.BaseDirectory), "lucide:check"),
            Path.Combine(icons, "check.svg"));
        try
        {
            var board = new C.DashboardDefinition { BaseDirectory = root };
            var value = new C.ValueWidgetDefinition { Id = "value", Metric = SystemMetricContract.Uptime };
            var binary = new C.BinaryWidgetDefinition
            {
                Id = "binary", Metric = SystemMetricContract.Uptime, Width = 96, Height = 96
            };
            StateVisualProfileContract.EnsureProfiles(binary);
            board.Widgets.Add(value);
            board.Widgets.Add(binary);
            var json = DashboardJson.Serialize(board, indented: false);
            Check(!json.Contains("\"fallback\"", StringComparison.OrdinalIgnoreCase),
                "Current schema must not serialize editable fallback.");
            var legacy = JsonNode.Parse(json)!.AsObject();
            legacy["schemaVersion"] = DashboardSchemaMigration.CalculatorAppearanceSchemaVersion;
            foreach (var item in legacy["widgets"]!.AsArray().OfType<JsonObject>())
                item["fallback"] = "CUSTOM";
            var parsed = C.DashboardDefinition.Parse(legacy.ToJsonString(), Path.Combine(root, "legacy.json"));
            Check(parsed.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion,
                "Schema 35 must migrate to current version.");
            var normalized = DashboardJson.Serialize(parsed, indented: false);
            Check(!normalized.Contains("\"fallback\"", StringComparison.OrdinalIgnoreCase),
                "Migrated definitions must retire custom fallback fields.");
            var again = DashboardJson.Serialize(
                DashboardJson.ParseCurrent(normalized, Path.Combine(root, "current.json")),
                indented: false);
            Check(normalized == again, "Migrated parse/save must be idempotent.");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    public static void RenderStateSemantics()
    {
        using var typefaces = new TypefaceCache(
            ApplicationRootLocator.Find(AppContext.BaseDirectory), []);
        var value = new C.ValueWidgetDefinition
        {
            Id = "metric-fallback", Metric = SystemMetricContract.Uptime,
            Prefix = "prefix", Suffix = "suffix"
        };
        using var missing = new WidgetRenderContext(new Dictionary<string, object?>(), false, typefaces);
        Check(missing.ResolveValueText(value) == SystemFallback.Text,
            "Missing metric must use exact -- without prefix/suffix.");
        using var valid = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.Uptime] = 0d }, false, typefaces);
        Check(valid.ResolveValueText(value) != SystemFallback.Text,
            "Valid zero must not become unavailable.");
        var binary = new C.BinaryWidgetDefinition
        {
            Id = "binary", Metric = SystemMetricContract.Uptime, Width = 96, Height = 96
        };
        StateVisualProfileContract.EnsureProfiles(binary);
        Check(!missing.TryResolveBinaryVisualProfile(binary, out _),
            "Missing Binary input must not select False.");
        Check(valid.TryResolveBinaryVisualProfile(binary, out _) &&
              !binary.UsesOnlyValueSources(),
            "Valid zero must select the graphical False state.");
        foreach (var profile in binary.Profiles.Values)
            profile.ContentType = C.StateContentType.Value;
        Check(binary.UsesOnlyValueSources(),
            "Both textual Binary states must select text fallback.");
        Check(StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power)
                  .Any(profile => profile.Key == PowerMetricContract.StateUnavailable) &&
              StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem)
                  .Any(profile => profile.Key == StateVisualProfileContract.UnavailableKey) &&
              StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaPlayer)
                  .Any(profile => profile.Key == StateVisualProfileContract.UnavailableKey),
            "Power/Media must retain domain-specific Unavailable profiles.");
    }

    public static void BundledGraphicRendering()
    {
        var root = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        using var icons = new IconAssetCache(root);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(80, 80))
            ?? throw new InvalidOperationException("Could not create fallback test surface.");
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        SystemFallback.DrawGraphic(surface.Canvas, icons, new SkiaSharp.SKRect(0, 0, 80, 80),
            "#FFFFFFFF", antialias: true);
        surface.Canvas.Flush();
        using var pixels = surface.PeekPixels()
            ?? throw new InvalidOperationException("Could not inspect Lucide fallback pixels.");

        var active = 0;
        for (var y = 23; y < 57; y++)
        for (var x = 23; x < 57; x++)
            if (pixels.GetPixelColor(x, y).Alpha > 0) active++;

        Check(active > 8, "Bundled Lucide image-off marker must rasterize as visible SVG geometry.");
        Check(pixels.GetPixelColor(4, 4).Alpha == 0 && pixels.GetPixelColor(75, 75).Alpha == 0,
            "System fallback icon must stay centered within content bounds.");

        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        SystemFallback.DrawGraphic(surface.Canvas, icons, new SkiaSharp.SKRect(10, 10, 10, 20),
            "#FFFFFFFF", antialias: true);
        surface.Canvas.Flush();
        using var empty = surface.PeekPixels()
            ?? throw new InvalidOperationException("Could not inspect zero-size fallback.");
        Check(empty.GetPixelColor(40, 40).Alpha == 0,
            "Zero-size content must not draw an icon outside widget geometry.");
    }

    public static void GraphicWidgetAvailabilityRasters()
    {
        var root = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        using var images = new ImageAssetCache(root);
        using var icons = new IconAssetCache(root);
        using var typefaces = new TypefaceCache(root, []);
        using var missing = new WidgetRenderContext(new Dictionary<string, object?>(), false, typefaces);
        using var validZero = new WidgetRenderContext(
            new Dictionary<string, object?> { [SystemMetricContract.Uptime] = 0d },
            false, typefaces);

        var bar = new C.BarWidgetDefinition
        {
            Id = "fallback-bar", Metric = SystemMetricContract.Uptime, Width = 96,
            Height = 96, Min = 0, Max = 100
        };
        bar.Track.BackgroundColor = "#FF101010";
        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "fallback-gauge", Metric = SystemMetricContract.Uptime, Width = 96,
            Height = 96, Min = 0, Max = 100
        };
        gauge.Track.BackgroundColor = "#FF101010";
        var binary = new C.BinaryWidgetDefinition
        {
            Id = "fallback-binary", Metric = SystemMetricContract.Uptime, Width = 96, Height = 96
        };
        StateVisualProfileContract.EnsureProfiles(binary);

        using var barRenderer = new BarWidgetRenderer(images, icons);
        using var gaugeRenderer = new GaugeWidgetRenderer(icons);
        using var binaryRenderer = new BinaryWidgetRenderer(images, icons);

        foreach (var (widget, renderer) in new (C.WidgetDefinition Widget, IWidgetRenderer Renderer)[]
        {
            (bar, barRenderer),
            (gauge, gaugeRenderer),
            (binary, binaryRenderer)
        })
        {
            var absent = Raster(widget, renderer, missing);
            var realZero = Raster(widget, renderer, validZero);
            Check(!absent.AsSpan().SequenceEqual(realZero),
                widget.Type + " must draw a distinct unavailable graphic, not present valid zero/False as unavailable.");
        }

        static SkiaSharp.SKColor[] Raster(C.WidgetDefinition widget, IWidgetRenderer renderer, WidgetRenderContext context)
        {
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(96, 96))
                ?? throw new InvalidOperationException("Could not create availability regression surface.");
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            WidgetTransform.Render(surface.Canvas, widget, context, renderer);
            surface.Canvas.Flush();
            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect availability regression raster.");
            var result = new SkiaSharp.SKColor[96 * 96];
            for (var y = 0; y < 96; y++)
            for (var x = 0; x < 96; x++)
                result[y * 96 + x] = pixels.GetPixelColor(x, y);
            return result;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
