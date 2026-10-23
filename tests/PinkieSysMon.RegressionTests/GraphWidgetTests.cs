using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Widgets;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class GraphWidgetTests
{
    public static void SchemaAndLegacyCompatibility()
    {
        var graph = Graph();
        graph.Name = "History"; graph.Z = 7; graph.Rotation = 17; graph.BorderWidth = 2; graph.CornerRadius = 5;
        graph.ShadowEnabled = true; graph.ShadowColor = "#80010203";
        graph.Unit = null; graph.FillEnabled = true; graph.FillOpacity = .4f;
        var definition = Board(graph);
        var json = DashboardJson.Serialize(definition, indented: false);
        var parsed = C.DashboardDefinition.Parse(json, Path.Combine(Path.GetTempPath(), "graph.json"));
        Check(parsed.Widgets.Single() is C.GraphWidgetDefinition, "Graph discriminator must round-trip.");
        Check(DashboardJson.Serialize(parsed, indented: false) == json, "Repeated parse/save must be idempotent.");
        var widget = JsonNode.Parse(json)!["widgets"]![0]!.AsObject();
        Check(!widget.ContainsKey("gap") && !widget.ContainsKey("reverse") && !widget.ContainsKey("history") &&
              !widget.ContainsKey("sampleIntervalMs"), "Graph must not persist Bar knobs, samples or an independent polling interval.");
        var clone = (C.GraphWidgetDefinition)DashboardJson.CloneWidget(graph);
        Check(clone.Name == graph.Name && clone.Rotation == 17 && clone.ShadowEnabled && clone.Thresholds.Items.Count == 3 &&
              clone.FillOpacity == .4f, "Canonical cloning must preserve common fields and Graph settings.");
        for (var version = 10; version < DashboardSchemaMigration.GraphSchemaVersion; version++)
        {
            var root = JsonNode.Parse(json)!.AsObject(); root["schemaVersion"] = version;
            var before = root.ToJsonString();
            Throws<InvalidDataException>(() => DashboardSchemaMigration.UpgradeToCurrent(root));
            Check(root.ToJsonString() == before, "Legacy Graph rejection must precede mutation, for every earlier schema.");
        }
        definition.Widgets = [new C.BarWidgetDefinition { Id = "bar", Metric = SystemMetricContract.Uptime, Width = 50, Height = 10 }];
        var current = DashboardJson.Serialize(definition, indented: false);
        var old = JsonNode.Parse(current)!.AsObject(); old["schemaVersion"] = 22;
        Check(DashboardSchemaMigration.UpgradeToCurrent(old), "Schema 22 requires the named Graph introduction migration.");
        Check(JsonNode.DeepEquals(old, JsonNode.Parse(current)), "Graph-free migration must preserve every content field.");
        Check(!DashboardSchemaMigration.UpgradeToCurrent(old), "The introduction migration must be idempotent.");
        Check(DashboardMetricUsage.Collect(Board(graph)).Contains(graph.Metric!) &&
              DashboardMetricUsage.CollectHistory(Board(graph)).Single().WindowSeconds == graph.WindowSeconds,
            "Ordinary acquisition and history demand must both discover Graph.");
        graph.Thresholds.Items[0].Enabled = true; graph.Thresholds.Items[0].Value = 150;
        graph.ScaleMode = "Auto"; graph.Min = 10; graph.Max = 5; graph.Validate(Path.GetTempPath());
        graph.ScaleMode = "Fixed"; Throws<InvalidDataException>(() => graph.Validate(Path.GetTempPath()));
        graph.Min = 0; graph.Max = 100; Throws<InvalidDataException>(() => graph.Validate(Path.GetTempPath()));
        graph.ScaleMode = "Auto"; graph.WindowSeconds = 601; Throws<InvalidDataException>(() => graph.Validate(Path.GetTempPath()));
    }

    public static void RangesAndAbsoluteThresholdColors()
    {
        foreach (var value in new[] { 0d, 50, -50, double.MaxValue, -double.MaxValue, double.Epsilon })
        {
            var range = GraphRange.Auto(value, value);
            Check(double.IsFinite(range.Min) && double.IsFinite(range.Max) && range.Min < range.Max &&
                  double.IsFinite(range.Normalize(value)), "Every finite constant must yield an ordered finite rendering range.");
        }
        var flat = GraphRange.Auto(50, 50);
        Check(Math.Abs(flat.Min - 49.99995) < 1e-10 && Math.Abs(flat.Max - 50.00005) < 1e-10,
            "Constant padding must follow the documented selected-unit convention.");
        Check(new GraphRange(-double.MaxValue, double.MaxValue).Normalize(0) == .5 &&
              new GraphRange(0, double.Epsilon).Normalize(double.Epsilon) == 1,
            "Normalization must handle opposite extremes and adjacent subnormals.");
        var graph = Graph(); graph.Color = "#000000";
        SetThresholds(graph);
        graph.Thresholds.Mode = C.ThresholdModeContract.State;
        var colors = new QuantitativeColorScale(graph);
        Check(colors.At(19, 0) == SKColors.Black && colors.At(20, 0) == SKColors.Green && colors.At(80, 0) == SKColors.Red,
            "State equality must select the last threshold reached.");
        graph.Thresholds.Items[1].Value = 20;
        colors = new(graph);
        Check(colors.At(20, 0) == SKColors.Blue, "The last enabled duplicate threshold must win exact/outgoing state.");
        graph.Thresholds.Mode = C.ThresholdModeContract.SegmentTransition;
        colors = new(graph);
        var incoming = colors.At(19.999, 0);
        Check(incoming.Green > 120 && incoming.Blue < 2 && colors.At(20, 0) == SKColors.Blue,
            "Duplicate transition stops must preserve the incoming color and the zero-width jump.");
        graph.Thresholds.Items[1].Enabled = false;
        colors = new(graph);
        var midpoint = colors.At(50, 20);
        Check(Math.Abs(midpoint.Red - 128) <= 1 && Math.Abs(midpoint.Green - 64) <= 1,
            "Auto must interpolate absolute out-of-view thresholds instead of clamping them onto viewport endpoints.");
        var firstRamp = colors.At(10, 0);
        var secondRamp = colors.At(10, 10);
        Check(firstRamp.Green == 64 && secondRamp == SKColors.Black,
            "The below-first-threshold Auto ramp must use the visible lower anchor.");
    }

    public static void RenderingModeScaleThresholdMatrix()
    {
        foreach (var mode in new[] { "Line", "Columns" })
        foreach (var scale in new[] { "Auto", "Fixed" })
        foreach (var threshold in C.ThresholdModeContract.SupportedModes)
        foreach (var descending in new[] { false, true })
        {
            var graph = Graph(); graph.Mode = mode; graph.ScaleMode = scale; SetThresholds(graph); graph.Thresholds.Mode = threshold;
            using var f = new Fixture(graph);
            f.Publish(1, descending ? 90 : 10); f.Publish(5, 50); f.Publish(9, descending ? 10 : 90); f.Now = T(10);
            using var bitmap = f.Render();
            var range = scale == "Auto" ? new GraphRange(10, 90) : new GraphRange(0, 100);
            var sampleValue = mode == "Line" ? descending ? 40 : 60 : 30;
            var x = mode == "Line" ? 60 : 50;
            var y = (int)Math.Round(100 * (1 - range.Normalize(sampleValue)));
            var stateValue = mode == "Line" ? descending ? 10 : 90 : 50;
            var colorScale = new QuantitativeColorScale(graph);
            var expected = threshold == C.ThresholdModeContract.State
                ? QuantitativeColorScale.StateColor(colorScale.BaseColor, colorScale.Thresholds, stateValue)
                : colorScale.At(sampleValue, scale == "Auto" ? 10 : 0);
            Check(NearPixel(bitmap, x, y, expected), $"Incorrect {mode}/{scale}/{threshold} value-based color.");
        }
    }

    public static void GapsExpiryClippingAndDenseSpikes()
    {
        var graph = Graph(); graph.FillEnabled = true; graph.Color = "#FFFFFF";
        using (var f = new Fixture(graph))
        {
            using (var empty = f.Render()) Check(!AnyPixel(empty), "Startup must not fabricate a history series.");
            f.Publish(1, 20); f.Publish(2, null); f.Publish(3, 80); f.Publish(4, 70); f.Now = T(10);
            using var bitmap = f.Render();
            Check(bitmap.GetPixel(20, 90).Alpha == 0 && bitmap.GetPixel(20, 50).Alpha == 0,
                "Neither line nor fill may bridge an explicit unavailable publication.");
            var before = f.Count();
            using var repeat = f.Render();
            Check(f.Count() == before, "Rendering must not append history.");
            f.Now = T(30); using var expired = f.Render();
            Check(!AnyPixel(expired), "Silence must expire geometry without extending the last observation.");
        }
        graph = Graph(); graph.ScaleMode = "Auto"; graph.FillEnabled = true;
        using (var f = new Fixture(graph))
        {
            f.Publish(2, 0); f.Now = T(10); using var bitmap = f.Render();
            Check(NearPixel(bitmap, 20, 50, SKColors.White) && bitmap.GetPixel(50, 50).Alpha == 0,
                "A single zero observation must be a point with constant Auto padding, never an invented segment or fill.");
        }
        graph = Graph(); graph.WindowSeconds = 2; graph.Color = "#FFFFFF";
        using (var f = new Fixture(graph))
        {
            f.Publish(0, -50); f.Publish(2, 150);
            using var bitmap = f.Render();
            Check(NearPixel(bitmap, 50, 50, SKColors.White) && bitmap.GetPixel(10, 90).Alpha == 0 &&
                  NearPixel(bitmap, 25, 98, SKColors.White), "Fixed clipping must use original values and true crossing times.");
        }
        using (var f = new Fixture(graph))
        {
            f.Publish(0, 150); f.Publish(2, -50); using var bitmap = f.Render();
            Check(NearPixel(bitmap, 50, 50, SKColors.White) && bitmap.GetPixel(10, 10).Alpha == 0 &&
                  NearPixel(bitmap, 25, 1, SKColors.White), "Descending Fixed clipping must preserve its true crossing times.");
        }
        graph = Graph(); graph.ScaleMode = "Auto";
        using (var f = new Fixture(graph))
        {
            f.Publish(1, 100); f.Publish(3, 20); f.Publish(4, 30); f.Now = T(12);
            using var bitmap = f.Render();
            Check(NearPixel(bitmap, 20, 1, SKColors.White), "An expired predecessor must not set Auto's maximum.");
        }
        graph = Graph(); graph.Width = 20; graph.ScaleMode = "Auto";
        using (var f = new Fixture(graph))
        {
            f.Publish(4.91, 0); f.Publish(4.92, 100); f.Publish(4.93, -20); f.Publish(4.94, 0); f.Now = T(10);
            using var bitmap = f.Render();
            Check(NearPixel(bitmap, 10, 1, SKColors.White) && NearPixel(bitmap, 10, 98, SKColors.White),
                "A dense time bin must preserve both extrema and chronological geometry.");
        }
    }

    public static void FillAlphaAndCommonImageComposition()
    {
        foreach (var mode in C.ThresholdModeContract.SupportedModes)
        {
            var graph = Graph(); SetThresholds(graph); graph.Thresholds.Mode = mode;
            graph.Color = "#80000000";
            foreach (var t in graph.Thresholds.Items) t.Color = "#80" + t.Color.TrimStart('#');
            graph.FillEnabled = true; graph.FillOpacity = .4f;
            using var f = new Fixture(graph);
            f.Publish(1, 40); f.Publish(5, 50); f.Publish(9, 90); f.Now = T(10);
            using var bitmap = f.Render();
            Check(Math.Abs(bitmap.GetPixel(50, 80).Alpha - 51) <= 2 && Math.Abs(bitmap.GetPixel(60, 80).Alpha - 51) <= 2,
                "Fill must multiply source alpha once, including at a joined segment boundary.");
        }
        var imageGraph = Graph(); imageGraph.X = 10; imageGraph.Y = 10; imageGraph.Width = 80; imageGraph.Height = 80;
        imageGraph.BorderWidth = 4; imageGraph.BorderColor = "#FF0000"; imageGraph.CornerRadius = 15;
        using var fixture = new Fixture(imageGraph);
        using (var source = new SKBitmap(2, 2))
        {
            source.Erase(SKColors.Blue); using var data = source.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(fixture.Root, "background.png"), data.ToArray());
        }
        imageGraph.Image.Asset.Source = "background.png"; imageGraph.Image.Opacity = .5f;
        using var rendered = fixture.Render();
        Check(rendered.GetPixel(10, 10).Alpha == 0 && NearPixel(rendered, 50, 11, SKColors.Red) &&
              rendered.GetPixel(50, 60).Blue > 240 && Math.Abs(rendered.GetPixel(50, 60).Alpha - 128) <= 1,
            "Graph must compose its rounded common container, border and independently faded image.");
    }

    public static void GeometryReuseAndRuntimeEditorParity()
    {
        var graph = Graph(); using var f = new Fixture(graph);
        f.Publish(1, 10); f.Publish(9, 90); f.Now = T(10);
        using (var initial = f.Render()) Check(NearPixel(initial, 90, 10, SKColors.White), "Initial Graph geometry is incorrect.");
        graph.Width = 50; graph.Height = 50; f.InvalidateGeometry();
        using (var resized = f.Render())
            Check(NearPixel(resized, 45, 5, SKColors.White) && resized.GetPixel(90, 10).Alpha == 0,
                "Geometry invalidation must update Graph clipping and reuse its history scratch.");
        using (var bitmap = new SKBitmap(100, 100))
        using (var canvas = new SKCanvas(bitmap))
        {
            for (var i = 0; i < 100; i++) f.Draw(canvas);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) f.Draw(canvas);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated <= 4096, $"A warmed fixed numeric Line must avoid per-sample/frame allocation; observed {allocated} bytes / 1000 frames.");
        }
        var definition = Board(graph); definition.BaseDirectory = f.Root; definition.Canvas.BackgroundColor = "#00000000";
        using var runtime = new DashboardRenderer(definition, history: f.History);
        using var preview = new DashboardPreviewRenderer(definition, f.History);
        Check(preview.RequiresFrequentRefresh && preview.RecommendedRefreshIntervalMs == 100, "Graph preview must scroll between publications.");
        using var actual = runtime.RenderBgra(f.Metrics);
        using var expected = SKBitmap.Decode(preview.RenderToPng(f.Metrics));
        for (var y = 0; y < 100; y++) for (var x = 0; x < 100; x++)
            Check(actual.Bitmap.GetPixel(x, y) == expected.GetPixel(x, y), "Runtime and Editor must share identical logical Graph rasterization.");
        definition.Widgets.Add(new C.ValueWidgetDefinition { Id = "scrolling-caption", Width = 100,
            TextPresentation = new() { OverflowMode = ValueOverflowContract.Scroll } });
        Check(preview.RecommendedRefreshIntervalMs == 50, "A Graph must preserve the faster existing scrolling-text cadence.");
    }

    internal static C.GraphWidgetDefinition Graph() => new()
    {
        Id = "graph", Metric = SystemMetricContract.IntegratedGpuLoad,
        Width = 100, Height = 100, WindowSeconds = 10, ScaleMode = "Fixed", StrokeWidth = 3
    };
    internal static C.DashboardDefinition Board(C.WidgetDefinition graph) => new()
    { BaseDirectory = Path.GetTempPath(), Canvas = new() { Width = 100, Height = 100 }, Widgets = [graph] };
    private static void SetThresholds(C.GraphWidgetDefinition graph)
    {
        graph.Color = "#000000";
        graph.Thresholds.Items = [new() { Enabled = true, Value = 20, Color = "#008000" },
            new() { Enabled = true, Value = 50, Color = "#0000FF" }, new() { Enabled = true, Value = 80, Color = "#FF0000" }];
    }
    private static long T(double seconds) => (long)(seconds * TimeSpan.TicksPerSecond);
    private static bool AnyPixel(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Alpha > 0) return true;
        return false;
    }
    private static bool NearPixel(SKBitmap bitmap, int x, int y, SKColor expected)
    {
        for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++)
        {
            var px = x + dx; var py = y + dy;
            if (px < 0 || py < 0 || px >= bitmap.Width || py >= bitmap.Height) continue;
            var actual = bitmap.GetPixel(px, py);
            if (actual.Alpha > 150 && Math.Abs(actual.Red - expected.Red) < 20 &&
                Math.Abs(actual.Green - expected.Green) < 20 && Math.Abs(actual.Blue - expected.Blue) < 20) return true;
        }
        return false;
    }
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "Pinkie-Graph-" + Guid.NewGuid().ToString("N"));
        public long Now;
        private readonly C.GraphWidgetDefinition _graph;
        private readonly MetricHistoryStore _history;
        private readonly MetricStore _store;
        private readonly ImageAssetCache _images;
        private readonly IconAssetCache _icons;
        private readonly TypefaceCache _typefaces;
        private readonly GraphWidgetRenderer _renderer;
        private readonly WidgetRenderContext _context;
        public Fixture(C.GraphWidgetDefinition graph)
        {
            _graph = graph; Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "config"));
            File.WriteAllText(Path.Combine(Root, "config", "app.json"), "{}");
            _history = new(() => Now);
            _history.SetDemand([new(graph.Metric!, graph.WindowSeconds, 10, MetricContract.GetDescriptor(graph.Metric))]);
            _store = new(_history); _images = new(Root); _icons = new(Root); _typefaces = new(Root, []);
            _renderer = new(_images, _icons); _context = new(_store.Snapshot(), false, _typefaces, _history);
        }
        public void Publish(double seconds, object? value) { Now = T(seconds); _store.Publish(_graph.Metric!, value); }
        public int Count() => _history.CopyTo(_graph.Metric!, _graph.WindowSeconds, Now, new MetricHistorySample[8192]).Count;
        public MetricHistoryStore History => _history;
        public IReadOnlyDictionary<string, object?> Metrics => _store.Snapshot();
        public void InvalidateGeometry() { _context.InvalidateGeometry(_graph); _renderer.InvalidateGeometry(_graph); }
        public void Draw(SKCanvas canvas)
        { _context.BeginFrame(_store.Snapshot()); WidgetTransform.Render(canvas, _graph, _context, _renderer); }
        public SKBitmap Render()
        {
            var bitmap = new SKBitmap(100, 100); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
            _context.BeginFrame(_store.Snapshot()); WidgetTransform.Render(canvas, _graph, _context, _renderer); canvas.Flush(); return bitmap;
        }
        public void Dispose()
        {
            _renderer.Dispose(); _context.Dispose(); _typefaces.Dispose(); _icons.Dispose(); _images.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
