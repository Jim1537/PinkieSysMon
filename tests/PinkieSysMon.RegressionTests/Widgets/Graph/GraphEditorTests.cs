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
        var graph = GraphWidgetTests.Graph();
        graph.ScaleMode = "Auto";
        var root = Path.GetTempPath();
        PropertyDescriptorCollection View(EditorPropertySection section) =>
            new EditorPropertyView(graph, root, root, section: section).GetProperties();
        static PropertyDescriptorCollection Rows(PropertyDescriptorCollection groups, string group) =>
            TypeDescriptor.GetProperties(groups[group]!.GetValue(null)!);

        var general = View(EditorPropertySection.General);
        var panel = View(EditorPropertySection.Panel);
        GraphWidgetTests.Check(Rows(general, "Data Source")["Metric"] is not null &&
                               !Rows(panel, "Geometry")["Height"]!.IsReadOnly,
            "Graph metric remains on General; geometry belongs to Panel.");
        foreach (var group in new[] { "Geometry", "Background", "Foreground", "Border", "Shadow" })
            GraphWidgetTests.Check(panel[group] is not null, $"Graph is missing Panel/{group}.");

        var appearance = View(EditorPropertySection.Appearance);
        foreach (var name in new[] { "Threshold 1", "Threshold 2", "Threshold 3" })
            GraphWidgetTests.Check(appearance[name] is not null, $"Graph is missing Appearance/{name}.");
        GraphWidgetTests.Check(appearance["Container"] is null, "Panel-owned colors must not appear on Appearance.");

        var range = Rows(View(EditorPropertySection.Data), "Range");
        GraphWidgetTests.Check(range["Min"]!.IsReadOnly && range["Max"]!.IsReadOnly,
            "Auto graph range must remain read-only.");
        graph.Min = -10;
        graph.Max = 150;
        range["GraphScaleMode"]!.SetValue(null, "Fixed");
        GraphWidgetTests.Check(!range["Min"]!.IsReadOnly, "Fixed scale must enable numeric bounds.");

        var graphRows = View(EditorPropertySection.Graph);
        GraphWidgetTests.Check(Rows(graphRows, "Plot")["Color"] is not null,
            "Plot color belongs to the Graph tab, independently from panel foreground.");
        var line = Rows(graphRows, "Line");
        GraphWidgetTests.Check(!line["Interpolation"]!.IsReadOnly && line["FillOpacity"]!.IsReadOnly,
            "Line-only settings and disabled fill must remain contextual.");
        line["FillEnabled"]!.SetValue(null, true);
        GraphWidgetTests.Check(!line["FillOpacity"]!.IsReadOnly, "Fill enables its independent opacity.");
        Rows(graphRows, "Timeline")["GraphMode"]!.SetValue(null, "Columns");
        GraphWidgetTests.Check(line["Interpolation"]!.IsReadOnly && line["FillOpacity"]!.IsReadOnly,
            "Column mode disables line presentation.");
        Rows(graphRows, "Timeline")["GraphMode"]!.SetValue(null, "Line");
        line["Interpolation"]!.SetValue(null, "Bezier");
        GraphWidgetTests.Check(graph.Interpolation == "Bezier", "Graph interpolation must survive mode changes.");

        GraphWidgetTests.Check(View(EditorPropertySection.Image).Count == 0,
            "Without a configured plot image or panel artwork Graph has no Image tab.");
        GraphWidgetTests.Check(View(EditorPropertySection.Gauge).Count == 0,
            "Graph must not expose Gauge controls.");
    }

    public static void BackgroundImageSharedPropertyBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Graph-Panel-Properties-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "icons", "test"));
        try
        {
            File.WriteAllText(Path.Combine(root, "assets", "icons", "test", "tintable.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path fill=\"currentColor\" d=\"M0 0H10V10H0Z\"/></svg>");
            File.WriteAllBytes(Path.Combine(root, "background.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJd8AAAAASUVORK5CYII="));

            var graph = GraphWidgetTests.Graph();
            PropertyDescriptorCollection Panel() => new EditorPropertyView(
                graph, root, root, section: EditorPropertySection.Panel).GetProperties();
            PropertyDescriptorCollection Image() => new EditorPropertyView(
                graph, root, root, section: EditorPropertySection.Image).GetProperties();
            static PropertyDescriptorCollection Rows(PropertyDescriptorCollection groups, string name) =>
                TypeDescriptor.GetProperties(groups[name]!.GetValue(null)!);

            var background = Rows(Panel(), "Background");
            GraphWidgetTests.Check(background["Mode"] is not null &&
                                   background["Source"] is null && Image().Count == 0,
                "Panel starts without artwork and without an Image tab.");

            background["Mode"]!.SetValue(null, "Image");
            background = Rows(Panel(), "Background");
            GraphWidgetTests.Check(background["Source"]!.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
                "File images must use the shared source editor.");
            background["Source"]!.SetValue(null, "background.png");
            var fileRows = Rows(Image(), "Background");
            GraphWidgetTests.Check(fileRows["Fit"] is not null && fileRows["Loop"] is not null &&
                                   fileRows["IconOpacity"] is null && fileRows["IconColor"] is null,
                "File-image presentation is intrinsic-alpha only.");

            background["Mode"]!.SetValue(null, "Icon");
            Rows(Panel(), "Background")["Source"]!.SetValue(null, "test:tintable");
            var iconRows = Rows(Image(), "Background");
            GraphWidgetTests.Check(iconRows["IconOpacity"] is not null && iconRows["IconColor"] is not null,
                "Icon mode exposes independent opacity and tint.");
            iconRows["IconOpacity"]!.SetValue(null, 0.4f);
            GraphWidgetTests.Check(graph.PanelBackground.IconOpacity == 0.4f &&
                                   graph.PanelBackground.Image.Source == "background.png" &&
                                   graph.Image.Asset.Source is null,
                "Panel source switching must retain dormant settings without rewriting the plot.");
            // Existing Graph plot-background settings are retained as content-specific
            // legacy artwork rather than being reinterpreted as the Panel background.
            graph.Image.Asset.SourceType = C.ImageAssetSourceType.File;
            graph.Image.Asset.Source = "background.png";
            var legacy = Image();
            GraphWidgetTests.Check(legacy["Plot Background (Legacy)"] is not null &&
                                   graph.PanelBackground.Image.Source == "background.png" &&
                                   graph.Image.Asset.Source == "background.png",
                "Panel and legacy Graph plot-background resources must remain independent.");
        }
        finally { Directory.Delete(root, recursive: true); }
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
            graph.Filter = "Median"; graph.Interpolation = "Bezier";
            var before = DashboardJson.SerializeEditorSnapshot(definition);
            graph.WindowSeconds = 30; graph.Mode = "Columns"; graph.Filter = "Mean"; graph.Interpolation = "Linear";
            var restored = DashboardJson.ParseEditorSnapshot(before, Path.Combine(target, "dashboard.json"));
            GraphWidgetTests.Check(restored.Widgets.Single() is C.GraphWidgetDefinition { WindowSeconds: 10, Mode: "Line", Filter: "Median", Interpolation: "Bezier" },
                "Canonical undo snapshots must restore Graph settings without serializing history.");
            graph.Image.Asset.Source = "background.png";
            GraphWidgetTests.Check(EditorForm.ResolveClipboardImages([graph], source, target, [],
                _ => System.Windows.Forms.DialogResult.No, _ => System.Windows.Forms.DialogResult.Cancel) && graph.Image.Asset.Source is null,
                "Declining Graph image import must clear only its source through the standard clipboard workflow.");
        }
        finally { Directory.Delete(root, true); }
    }
}
