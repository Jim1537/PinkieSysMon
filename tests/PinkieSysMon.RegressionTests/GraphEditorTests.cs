using System.ComponentModel;
using System.Drawing.Design;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class GraphEditorTests
{
    public static void CommonPropertiesAndGraphApplicability()
    {
        var graph = GraphWidgetTests.Graph(); graph.ScaleMode = "Auto";
        var root = Path.GetTempPath();
        PropertyDescriptorCollection View(EditorPropertySection section) => new EditorPropertyView(graph, root, root, section: section).GetProperties();
        static PropertyDescriptorCollection Rows(PropertyDescriptorCollection groups, string group) =>
            TypeDescriptor.GetProperties(groups[group]!.GetValue(null)!);
        var general = View(EditorPropertySection.General);
        GraphWidgetTests.Check(Rows(general, "Source")["Metric"] is not null && !Rows(general, "Geometry")["Height"]!.IsReadOnly,
            "Graph must expose its metric and independent rectangular geometry.");
        var appearance = View(EditorPropertySection.Appearance);
        foreach (var name in new[] { "Foreground", "Background", "Border", "Shadow", "Threshold 1", "Threshold 2", "Threshold 3" })
            GraphWidgetTests.Check(appearance[name] is not null, $"Graph is missing common Appearance/{name}.");
        var range = Rows(View(EditorPropertySection.Data), "Range");
        GraphWidgetTests.Check(range["Min"]!.IsReadOnly && range["Max"]!.IsReadOnly, "Auto bounds must remain visible but disabled.");
        graph.Min = -10; graph.Max = 150; range["GraphScaleMode"]!.SetValue(null, "Fixed");
        GraphWidgetTests.Check(!range["Min"]!.IsReadOnly && graph.Min == -10 && graph.Max == 150,
            "Fixed must restore editing without replacing retained bounds.");
        var graphRows = View(EditorPropertySection.Graph);
        var line = Rows(graphRows, "Line");
        GraphWidgetTests.Check(!line["StrokeWidth"]!.IsReadOnly && line["FillOpacity"]!.IsReadOnly, "Line-only and disabled-fill applicability is incorrect.");
        line["FillEnabled"]!.SetValue(null, true);
        GraphWidgetTests.Check(!line["FillOpacity"]!.IsReadOnly, "Enabling fill must enable its opacity.");
        Rows(graphRows, "Timeline")["GraphMode"]!.SetValue(null, "Columns");
        GraphWidgetTests.Check(line["StrokeWidth"]!.IsReadOnly && line["FillEnabled"]!.IsReadOnly && line["FillOpacity"]!.IsReadOnly,
            "Columns must disable Line-only settings.");
        var image = Rows(graphRows, "Background Image");
        GraphWidgetTests.Check(image["Fit"]!.IsReadOnly && image["Opacity"]!.IsReadOnly &&
              image["Source"]!.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "Graph background must reuse the normal asset picker and absent-source applicability.");
        GraphWidgetTests.Check(View(EditorPropertySection.Gauge).Count == 0 &&
              EditorForm.IsPropertyContextSwitch("GraphMode") && EditorForm.IsPropertyContextSwitch("GraphScaleMode"),
            "Graph needs its own tab and normal context refresh, without Bar/Gauge controls.");
        graph.Metric = SystemMetricContract.DateTime;
        var value = Rows(View(EditorPropertySection.Data), "Value");
        GraphWidgetTests.Check(value["Component"] is not null && value["ComponentFormat"] is not null,
            "DateTime Graph must reuse the numeric component and precision controls.");
    }

    public static void ClipboardImageImportAndCanonicalUndoSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Graph-Clipboard-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        try
        {
            File.WriteAllBytes(Path.Combine(source, "background.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJd8AAAAASUVORK5CYII="));
            var graph = GraphWidgetTests.Graph(); graph.Image.Asset.Source = "background.png";
            var files = new List<string>();
            GraphWidgetTests.Check(EditorForm.ResolveClipboardImages([graph], source, target, files,
                _ => System.Windows.Forms.DialogResult.Yes, _ => System.Windows.Forms.DialogResult.Yes) &&
                File.Exists(Path.Combine(target, graph.Image.Asset.Source!)), "Graph clipboard import must copy its dashboard-local image.");
            var definition = GraphWidgetTests.Board(graph); definition.BaseDirectory = target;
            var before = DashboardJson.SerializeEditorSnapshot(definition);
            graph.WindowSeconds = 30; graph.Mode = "Columns";
            var restored = DashboardJson.ParseEditorSnapshot(before, Path.Combine(target, "dashboard.json"));
            GraphWidgetTests.Check(restored.Widgets.Single() is C.GraphWidgetDefinition { WindowSeconds: 10, Mode: "Line" },
                "Canonical undo snapshots must restore Graph settings without serializing history.");
            graph.Image.Asset.Source = "background.png";
            GraphWidgetTests.Check(EditorForm.ResolveClipboardImages([graph], source, target, [],
                _ => System.Windows.Forms.DialogResult.No, _ => System.Windows.Forms.DialogResult.Cancel) && graph.Image.Asset.Source is null,
                "Declining Graph image import must clear only its source through the standard clipboard workflow.");
        }
        finally { Directory.Delete(root, true); }
    }
}
