using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue94AdaptiveDashboardTests
{
    public static void GeneratorGeometryAndSemantics()
    {
        if (NewDashboardDialog.ResolutionPresets.Length != 17 ||
            NewDashboardDialog.ResolutionPresets.Distinct().Count() != 17 ||
            !NewDashboardDialog.ResolutionPresets.Contains((640, 640)))
            throw new InvalidOperationException("New Dashboard must offer 17 unique manual resolution presets.");

        var geometries = NewDashboardDialog.ResolutionPresets
            .Concat([(1232, 432), (1440, 400), (768, 768), (2560, 640), (240, 320), (400, 1440)])
            .ToArray();
        foreach (var (width, height) in geometries)
        foreach (var orientation in new[] { 0, 90, 180, 270 })
        {
            var board = AdaptiveDashboardFactory.Create(width, height, orientation, Path.GetTempPath());
            var path = Path.Combine(Path.GetTempPath(), "issue94-dashboard.json");
            var json = DashboardJson.Serialize(board, indented: false);
            var loaded = C.DashboardDefinition.Parse(json, path);
            if (loaded.Canvas.Width != width || loaded.Canvas.Height != height ||
                loaded.Canvas.Orientation != orientation ||
                loaded.SchemaVersion != DashboardSchemaMigration.CurrentSchemaVersion ||
                board.Widgets.Count != loaded.Widgets.Count)
                throw new InvalidOperationException($"Generated board changed geometry/schema at {width}x{height}/{orientation}.");

            var values = loaded.Widgets.OfType<C.ValueWidgetDefinition>().ToArray();
            var clock = values.Single(v => v.Id == "clock");
            var computer = values.Single(v => v.Id == "computer");
            var title = values.Single(v => v.Id == "brand");
            if (clock.SourceKind != C.ValueSourceKind.Metric ||
                clock.Metric != SystemMetricContract.DateTime ||
                clock.Format != "HH:mm:ss" ||
                computer.SourceKind != C.ValueSourceKind.Metric ||
                computer.Metric != SystemMetricContract.PcName ||
                title.SourceKind != C.ValueSourceKind.Text ||
                title.Text != "Pinkie's System Monitor" ||
                title.Metric is not null)
                throw new InvalidOperationException("Generated mandatory content is not canonical.");

            var supportedExtras = new HashSet<string>(StringComparer.Ordinal)
            {
                SystemMetricContract.PcProcessor, SystemMetricContract.PcRam,
                SystemMetricContract.Edition, SystemMetricContract.Uptime
            };
            if (values.Any(v => v.Metric is { } m && m != SystemMetricContract.DateTime &&
                                m != SystemMetricContract.PcName && !supportedExtras.Contains(m)))
                throw new InvalidOperationException("Generated dashboard references an unapproved metric.");

            var logical = FrameGeometry.GetLogicalSize(width, height, orientation);
            foreach (var widget in values)
            {
                if (widget.Width <= 0 || widget.Height <= 0 || widget.X < 0 || widget.Y < 0 ||
                    widget.X + widget.Width > logical.Width + 0.01f ||
                    widget.Y + widget.Height > logical.Height + 0.01f)
                    throw new InvalidOperationException($"Generated widget '{widget.Id}' leaves the logical canvas.");

                foreach (var other in values.Where(v => v != widget))
                {
                    if (widget.X < other.X + other.Width - 0.01f &&
                        other.X < widget.X + widget.Width - 0.01f &&
                        widget.Y < other.Y + other.Height - 0.01f &&
                        other.Y < widget.Y + widget.Height - 0.01f)
                        throw new InvalidOperationException($"Generated widgets '{widget.Id}' and '{other.Id}' overlap.");
                }
            }

            if (DashboardJson.Serialize(loaded, indented: false) != json)
                throw new InvalidOperationException("Generated board changed on canonical JSON round trip.");
        }
    }

    public static void RejectInfeasibleGeometry()
    {
        foreach (var (width, height, orientation) in new[]
            { (1, 1, 0), (199, 400, 0), (320, 179, 0), (100000, 100000, 0),
              (int.MaxValue, int.MaxValue, 0), (640, 640, 45) })
        {
            try
            {
                _ = AdaptiveDashboardFactory.Create(width, height, orientation, Path.GetTempPath());
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }
            throw new InvalidOperationException($"Infeasible canvas {width}x{height}/{orientation} was accepted.");
        }
    }
}
