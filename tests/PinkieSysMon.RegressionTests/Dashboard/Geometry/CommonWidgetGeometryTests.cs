using System.Text.Json.Nodes;
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
