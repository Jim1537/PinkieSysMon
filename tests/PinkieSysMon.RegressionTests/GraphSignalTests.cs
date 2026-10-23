using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Widgets;
using C = PinkieSysMon.DashboardModel;
using static PinkieSysMon.RegressionTests.GraphWidgetTests;

namespace PinkieSysMon.RegressionTests;

internal static class GraphSignalTests
{
    public static void SchemaAndDefaults()
    {
        var graph = Graph(); var board = Board(graph);
        Check(graph.Interpolation == "Linear" && graph.Filter == "None", "New Graph must retain raw Linear defaults.");
        var current = JsonNode.Parse(DashboardJson.Serialize(board, false))!.AsObject();
        foreach (var reverse in new[] { false, true })
        {
            current["widgets"]![0]!["reverse"] = reverse;
            var old = (JsonObject)current.DeepClone(); old["schemaVersion"] = 24;
            old["widgets"]![0]!.AsObject().Remove("interpolation"); old["widgets"]![0]!.AsObject().Remove("filter");
            Check(DashboardSchemaMigration.UpgradeToCurrent(old) && JsonNode.DeepEquals(old, current),
                "24-to-25 migration must preserve every field, direction and raw Linear appearance.");
            Check(!DashboardSchemaMigration.UpgradeToCurrent(old), "Signal migration must be idempotent.");
        }
        var omitted = (JsonObject)current.DeepClone(); omitted["widgets"]![0]!.AsObject().Remove("interpolation"); omitted["widgets"]![0]!.AsObject().Remove("filter");
        Check(C.DashboardDefinition.Parse(omitted.ToJsonString(), Path.Combine(Path.GetTempPath(), "omitted-graph.json")).Widgets.Single()
            is C.GraphWidgetDefinition { Filter: "None", Interpolation: "Linear" }, "Missing current processing fields must use raw Linear defaults.");
        foreach (var interpolation in new[] { "Linear", "Bezier" })
        foreach (var filter in new[] { "None", "Median", "Mean", "Kalman" })
        {
            graph.Interpolation = interpolation; graph.Filter = filter;
            var json = DashboardJson.Serialize(board, false);
            var parsed = C.DashboardDefinition.Parse(json, Path.Combine(Path.GetTempPath(), "signal-graph.json"));
            Check(DashboardJson.Serialize(parsed, false) == json &&
                DashboardJson.CloneWidget(graph) is C.GraphWidgetDefinition { } clone &&
                clone.Interpolation == interpolation && clone.Filter == filter,
                "Signal choices must survive clone/parse/save without serializing working filter state.");
            foreach (var version in new[] { 23, 24 })
            {
                var invalid = JsonNode.Parse(json)!.AsObject(); invalid["schemaVersion"] = version;
                var before = invalid.ToJsonString();
                Throws<InvalidDataException>(() => DashboardSchemaMigration.UpgradeToCurrent(invalid));
                Check(invalid.ToJsonString() == before, "Previously unsupported processing fields must reject before mutation.");
            }
        }
        graph.Filter = "Average"; Throws<InvalidDataException>(() => graph.Validate(Path.GetTempPath()));
        graph.Filter = "None"; graph.Interpolation = "Spline";
        Throws<InvalidDataException>(() => graph.Validate(Path.GetTempPath()));
        var graphFree = Board(new C.BarWidgetDefinition { Id = "bar", Metric = SystemMetricContract.Uptime, Width = 20, Height = 20 });
        var graphFreeCurrent = JsonNode.Parse(DashboardJson.Serialize(graphFree, false))!.AsObject();
        var graphFreeOld = (JsonObject)graphFreeCurrent.DeepClone(); graphFreeOld["schemaVersion"] = 24;
        DashboardSchemaMigration.UpgradeToCurrent(graphFreeOld);
        Check(JsonNode.DeepEquals(graphFreeOld, graphFreeCurrent), "Graph-free migration must change only schema version.");
    }

    private static MetricHistorySample[] Samples(params double?[] values) => values.Select((value, i) =>
        MetricHistorySample.Create(i * TimeSpan.TicksPerSecond, i + 1, value, i > 0)).ToArray();
    private static double[] Project(GraphFilterProjection filter, MetricHistorySample[] samples, long epoch = 1)
    {
        filter.Begin(epoch, samples); var graph = Graph();
        return samples.Select(sample => filter.TryResolve(sample, null, graph, out var value) ? value : double.NaN).ToArray();
    }
    private static void Near(double actual, double expected) =>
        Check(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Abs(expected)), $"Expected {expected}, observed {actual}.");

    public static void FiltersAndFiniteArithmetic()
    {
        var samples = Samples(0, 10, 0, 100, 0, 10);
        var expected = new[] { new[] { 0d, 5, 0, 5, 0, 10 }, new[] { 0d, 5, 10d / 3, 27.5, 22, 24 } };
        var modes = new[] { "Median", "Mean", "Kalman" };
        for (var m = 0; m < 2; m++)
        {
            var projected = Project(new(modes[m], 16), samples);
            for (var i = 0; i < samples.Length; i++) Near(projected[i], expected[m][i]);
        }
        var kalman = Project(new("Kalman", 16), samples);
        Near(kalman[0], 0); Near(kalman[1], 10 * 11d / 21); Near(kalman[2], kalman[1] * 210d / 331);
        foreach (var mode in modes)
        {
            var values = Project(new(mode, 16), Samples(-20, 50, 10, -100, 30));
            var transformed = Project(new(mode, 16), Samples(-20 * 1.8 + 32, 50 * 1.8 + 32, 10 * 1.8 + 32, -100 * 1.8 + 32, 30 * 1.8 + 32));
            for (var i = 0; i < values.Length; i++) Near(transformed[i], values[i] * 1.8 + 32);
            foreach (var constant in new[] { double.MaxValue, -double.MaxValue, double.Epsilon, 0d })
                foreach (var value in Project(new(mode, 16), Samples(Enumerable.Repeat<double?>(constant, 12).ToArray())))
                    Check(value == constant, "Every filter must retain constant finite extremes, including subnormals.");
            foreach (var value in Project(new(mode, 16), Samples(double.MaxValue, -double.MaxValue, double.MaxValue, -double.MaxValue)))
                Check(double.IsFinite(value), "Filter arithmetic must remain finite for opposite extremes.");
            var gaps = Project(new(mode, 16), Samples(1, 100, null, 30, 40));
            Check(double.IsNaN(gaps[2]) && gaps[3] == 30, "Unavailable samples must reset filter state and remain gaps.");
            var disconnected = Samples(1, 100, 30); disconnected[2] = disconnected[2] with { ConnectsToPrevious = false };
            Check(Project(new(mode, 16), disconnected)[2] == 30, "Lifecycle/cadence discontinuity must restart at the actual observation.");
        }
        var temperature = Graph(); temperature.Unit = "F";
        var converted = new GraphFilterProjection("Mean", 16); var readings = Samples(0, 10); converted.Begin(1, readings);
        var descriptor = new MetricDescriptor("temperature", MetricValueKind.Number, MetricUnit.Celsius);
        Check(converted.TryResolve(readings[0], descriptor, temperature, out var first), "Temperature projection must be available."); Near(first, 32);
        Check(converted.TryResolve(readings[1], descriptor, temperature, out var second), "Converted mean must be available."); Near(second, 41);
        var temporal = Graph(); temporal.Component = "second";
        var dates = new[] { MetricHistorySample.Create(0, 1, new DateTime(2026, 1, 1, 0, 0, 10), false),
            MetricHistorySample.Create(1, 2, new DateTime(2026, 1, 1, 0, 0, 20), true) };
        var projectedDates = new GraphFilterProjection("Mean", 16); projectedDates.Begin(1, dates);
        var dateDescriptor = new MetricDescriptor("date", MetricValueKind.DateTime, MetricUnit.None);
        Check(projectedDates.TryResolve(dates[0], dateDescriptor, temporal, out first), "Historical DateTime projection must be available."); Near(first, 10);
        Check(projectedDates.TryResolve(dates[1], dateDescriptor, temporal, out second), "Historical components must filter after projection."); Near(second, 15);
        Check(samples.Select(s => s.Number).SequenceEqual(new[] { 0d, 10, 0, 100, 0, 10 }), "Projection must not mutate shared raw samples.");
    }

    public static void PublicationCacheLifecycle()
    {
        foreach (var mode in new[] { "Median", "Mean", "Kalman" })
        {
            var filter = new GraphFilterProjection(mode, 16); var all = Samples(0, 100, 10, 50, 20, 70);
            var first = Project(filter, all[..4]); var complete = Project(filter, all);
            Check(first.SequenceEqual(complete[..4]), "New publication must not rewrite cached older filtered values.");
            Check(Project(filter, all).SequenceEqual(complete) && Project(filter, all[3..]).SequenceEqual(complete[3..]),
                "Repeated/scrolling frames must not re-filter retained publications.");
            var reset = Project(filter, all[3..], epoch: 2);
            Check(reset[0] == 50, "Descriptor/owner epoch must clear the per-consumer filter cache.");
            var newer = Samples(200, 300).Select(s => s with { Sequence = s.Sequence + 100 }).ToArray();
            Check(Project(filter, newer, epoch: 2)[0] == 200, "Losing retained overlap must prime from available history, without imaginary predecessors.");
            var small = new GraphFilterProjection(mode, 4); var reference = Project(new(mode, 16), all);
            Project(small, all[..4]); var rolling = Project(small, all[2..]);
            Check(rolling.SequenceEqual(reference[2..]), "Fixed-capacity cache eviction must retain filter state across overlapping queries.");
            filter.Begin(2, newer); var graph = Graph();
            for (var i = 0; i < 1000; i++) foreach (var sample in newer) filter.TryResolve(sample, null, graph, out _);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) foreach (var sample in newer) filter.TryResolve(sample, null, graph, out _);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, $"Cached projection must allocate zero bytes per rendered frame; observed {allocated}.");
            var scalar = new MetricHistorySample(0, 1000, 42, 0, 0, MetricHistoryValueKind.Number, true);
            for (var i = 1000; i < 2000; i++) filter.TryResolve(scalar with { Sequence = i }, null, graph, out _);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 2000; i < 3000; i++) filter.TryResolve(scalar with { Sequence = i }, null, graph, out _);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, $"Sustained publication filtering/cache eviction must not allocate; observed {allocated}.");
        }
        Check(System.Runtime.CompilerServices.Unsafe.SizeOf<GraphFilterProjection.Entry>() == 24,
            "The render scratch reservation must charge the actual 24-byte cache element.");
    }

    public static void BezierGeometry()
    {
        foreach (var values in new[] { new[] { 0d, 10, 30, 100 }, new[] { 100d, 30, 10, 0 },
            new[] { 0d, 100, 0, 50 }, new[] { -double.MaxValue, -1, 1, double.MaxValue }, new[] { 0d, 0, 10, 10 } })
        {
            var points = values.Select((v, i) => new GraphPoint(new[] { 0d, .1, .7, 1 }[i], v, i > 0)).ToArray();
            for (var i = 1; i < points.Length; i++)
            {
                var c = GraphBezierSegment.Create(points[i - 1], points[i], i > 1 ? points[i - 2] : null,
                    i + 1 < points.Length ? points[i + 1] : null);
                Check(c.Value(0) == values[i - 1] && c.Value(1) == values[i], "Bezier must interpolate the original endpoint values.");
                for (var j = 0; j <= 100; j++)
                {
                    var v = c.Value(j / 100d);
                    Check(double.IsFinite(v) && v >= Math.Min(c.V0, c.V1) && v <= Math.Max(c.V0, c.V1), "Bezier must not overshoot finite data, including extrema.");
                }
                var sliced = c.Slice(.2, .8);
                for (var j = 0; j <= 10; j++) Near(sliced.Value(j / 10d), c.Value(.2 + .6 * j / 10));
            }
        }
        var knots = new[] { new GraphPoint(0, 0, false), new GraphPoint(.1, 10, true), new GraphPoint(.7, 30, true), new GraphPoint(1, 100, true) };
        for (var i = 1; i < knots.Length - 1; i++)
        {
            var left = GraphBezierSegment.Create(knots[i - 1], knots[i], i > 1 ? knots[i - 2] : null, knots[i + 1]);
            var right = GraphBezierSegment.Create(knots[i], knots[i + 1], knots[i - 1], i + 2 < knots.Length ? knots[i + 2] : null);
            Near(3 * (left.V1 - left.C2) / (left.X1 - left.X0), 3 * (right.C1 - right.V0) / (right.X1 - right.X0));
        }
        var curve = GraphBezierSegment.Create(new(0, 0, false), new(1, 100, true), null, new(2, 0, true));
        Check(Math.Abs(curve.Value(.5) - 50) > 5, "Bezier shape must visibly differ from a straight segment near a turning point.");
        Near(curve.Value(curve.Crossing(30)), 30);
        var descending = GraphBezierSegment.Create(new(0, 100, false), new(1, 0, true), null, new(2, 100, true));
        Near(descending.Value(descending.Crossing(30)), 30);
    }
}
