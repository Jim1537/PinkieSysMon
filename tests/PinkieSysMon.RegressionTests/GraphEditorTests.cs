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
        GraphWidgetTests.Check(graphRows["Background Image"] is null, "Graph must not duplicate image controls in its plot tab.");
        var source = Rows(general, "Background Image");
        var image = Rows(View(EditorPropertySection.Image), "Background Image");
        GraphWidgetTests.Check(image["Fit"]!.IsReadOnly && image["Opacity"]!.IsReadOnly &&
              source["Source"]!.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "Graph background must reuse the normal asset picker and absent-source applicability.");
        GraphWidgetTests.Check(View(EditorPropertySection.Gauge).Count == 0 &&
              EditorForm.IsPropertyContextSwitch("GraphMode") && EditorForm.IsPropertyContextSwitch("GraphScaleMode") &&
              EditorForm.IsPropertyContextSwitch("Source") && EditorForm.IsPropertyContextSwitch("SourceType"),
            "Graph needs its own tab and normal context refresh, without Bar/Gauge controls.");
        Rows(graphRows, "Timeline")["Reverse"]!.SetValue(null, false);
        GraphWidgetTests.Check(!graph.Reverse &&
              ((IPropertySemanticKeyDescriptor)source["Source"]!).SemanticKey == "general.background_image.asset.source" &&
              ((IPropertySemanticKeyDescriptor)image["Opacity"]!).SemanticKey == "image.background_image.opacity",
            "Graph must use the shared image-layer property keys and expose Reverse in Timeline.");
        graph.Metric = SystemMetricContract.DateTime;
        var value = Rows(View(EditorPropertySection.Data), "Value");
        GraphWidgetTests.Check(value["Component"] is not null && value["ComponentFormat"] is not null,
            "DateTime Graph must reuse the numeric component and precision controls.");
    }

    public static void BackgroundImageSharedPropertyBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Graph-Image-Properties-" + Guid.NewGuid().ToString("N"));
        var icons = Path.Combine(root, "assets", "icons", "test"); Directory.CreateDirectory(icons);
        try
        {
            File.WriteAllText(Path.Combine(icons, "tintable.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path fill=\"currentColor\" d=\"M0 0H10V10H0Z\"/></svg>");
            File.WriteAllBytes(Path.Combine(root, "background.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJd8AAAAASUVORK5CYII="));
            var graph = GraphWidgetTests.Graph();
            PropertyDescriptorCollection Layer(EditorPropertySection section) => TypeDescriptor.GetProperties(
                new EditorPropertyView(graph, root, root, section: section).GetProperties()["Background Image"]!.GetValue(null)!);
            var source = Layer(EditorPropertySection.General); var image = Layer(EditorPropertySection.Image);
            GraphWidgetTests.Check(source.Count == 2 && image.Count == 4 && image["Color"]!.IsReadOnly,
                "Graph must split source/presentation through the shared image-layer groups.");
            source["SourceType"]!.SetValue(null, "Icon"); source["Source"]!.SetValue(null, "test:tintable");
            GraphWidgetTests.Check(!image["Opacity"]!.IsReadOnly && !image["Color"]!.IsReadOnly && image["Loop"]!.IsReadOnly,
                "Selecting a static tintable icon must enable opacity/color and keep Loop disabled.");
            image["Opacity"]!.SetValue(null, .4f); image["Color"]!.SetValue(null, "#80123456");
            GraphWidgetTests.Check(graph.Image.Opacity == .4f && graph.Image.Color == "#80123456" && graph.Color != graph.Image.Color,
                "Image edits must write the graph-local layer rather than the plot foreground or another layer.");
            source["SourceType"]!.SetValue(null, "Image");
            GraphWidgetTests.Check(graph.Image.Asset.Source is null && image["Opacity"]!.IsReadOnly && image["Color"]!.IsReadOnly,
                "Switching image kind must clear the source and reapply shared applicability.");
            source["Source"]!.SetValue(null, "background.png"); image["Opacity"]!.SetValue(null, .2f);
            GraphWidgetTests.Check(!image["Fit"]!.IsReadOnly && graph.Image.Opacity == .2f && image["Color"]!.IsReadOnly,
                "A file background must expose fit/opacity without offering unsupported tint.");
            var canvas = new C.CanvasDefinition();
            var canvasSource = TypeDescriptor.GetProperties(new EditorPropertyView(canvas, root, root,
                section: EditorPropertySection.General).GetProperties()["Background Image"]!.GetValue(null)!);
            var canvasImage = TypeDescriptor.GetProperties(new EditorPropertyView(canvas, root, root,
                section: EditorPropertySection.Image).GetProperties()["Background Image"]!.GetValue(null)!);
            foreach (PropertyDescriptor row in source)
                GraphWidgetTests.Check(((IPropertySemanticKeyDescriptor)row).SemanticKey == ((IPropertySemanticKeyDescriptor)canvasSource[row.Name]!).SemanticKey,
                    "Graph and Canvas source controls must share canonical image-layer semantics.");
            foreach (PropertyDescriptor row in image)
                GraphWidgetTests.Check(((IPropertySemanticKeyDescriptor)row).SemanticKey == ((IPropertySemanticKeyDescriptor)canvasImage[row.Name]!).SemanticKey,
                    "Graph and Canvas presentation controls must share canonical image-layer semantics.");
        }
        finally { Directory.Delete(root, true); }
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
