using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue102EditorDirtyTests
{
    public static void CleanLoadAndSwitch() => WithEditor((root, editor) =>
    {
        CreateDashboard(root, "First");
        CreateDashboard(root, "Second");

        Open(editor, "First");
        AssertClean(editor, "First");
        Check((bool)Invoke(editor, "ConfirmSaveBeforeDestructiveAction")!,
            "An unchanged dashboard must allow navigation without a Save prompt.");

        Open(editor, "Second");
        AssertClean(editor, "Second");
        Open(editor, "First");
        AssertClean(editor, "First");
    });


    public static void PropertyPaneRebindsOnDashboardSwitch() => WithEditor((root, editor) =>
    {
        CreateDashboard(root, "First");
        CreateDashboard(root, "Second");

        var grid = GetField<PropertyGrid>(editor, "_propertyGrid");
        var tree = GetField<TreeView>(editor, "_widgetTree");

        void AssertCanvasBound(C.DashboardDefinition document, string context)
        {
            Check(ReferenceEquals(GetField<object>(editor, "_propertyGridTarget"), document.Canvas) &&
                  grid.SelectedObject is EditorPropertyView view &&
                  ReferenceEquals(view.Target, document.Canvas) &&
                  ReferenceEquals(tree.SelectedNode?.Tag, document.Canvas),
                context + ": the PropertyGrid must show the newly opened Canvas, not a previous document.");
            Check(GetField<C.WidgetDefinition?>(editor, "_selectedWidget") is null &&
                  GetField<HashSet<C.WidgetDefinition>>(editor, "_selectedWidgets").Count == 0,
                context + ": no widget from another dashboard may remain selected.");
        }

        Open(editor, "First");
        var first = GetField<C.DashboardDefinition>(editor, "_definition");
        AssertCanvasBound(first, "First initial load");
        Invoke(editor, "SetSingleSelection", first.Widgets[0]);
        Check(grid.SelectedObject is EditorPropertyView firstView &&
              ReferenceEquals(firstView.Target, first.Widgets[0]),
            "Selecting a widget must bind its own properties.");

        Check((bool)Invoke(editor, "SaveDashboard")!, "Fixture dashboard Save failed.");
        Open(editor, "Second");
        var second = GetField<C.DashboardDefinition>(editor, "_definition");
        AssertCanvasBound(second, "First to Second");
        AssertClean(editor, "Second");

        // Simulate a delayed property callback from an obsolete document.
        // A stale view must not dirty the active document or create undo history.
        grid.SelectedObject = new EditorPropertyView(first.Widgets[0]);
        Invoke(editor, "OnPropertyChanged", "Name", null);
        AssertClean(editor, "Second");
        Check(GetField<object>(editor, "_undo") is System.Collections.ICollection { Count: 0 },
            "A stale single-widget event created a new-document undo checkpoint.");

        grid.SelectedObject = new EditorPropertyView(first.Canvas);
        Invoke(editor, "OnPropertyChanged", "Width", null);
        AssertClean(editor, "Second");

        grid.SelectedObject = new EditorMultiPropertyView(
            first.Widgets.Take(2).Select(widget => new EditorPropertyView(widget)).ToArray());
        Invoke(editor, "OnPropertyChanged", "Name", null);
        AssertClean(editor, "Second");

        Invoke(editor, "SetPropertyGridTarget", second.Canvas);
        AssertCanvasBound(second, "After obsolete callbacks");
        Invoke(editor, "SetSingleSelection", second.Widgets[0]);
        Check(grid.SelectedObject is EditorPropertyView secondView &&
              ReferenceEquals(secondView.Target, second.Widgets[0]),
            "The new document's widget selection must remain editable.");

        var before = Invoke(editor, "CaptureSnapshot");
        second.Widgets[0].Name = "Second edited";
        Invoke(editor, "OnPropertyChanged", "Name", before);
        AssertDirty(editor, "Current-document widget property changes must still work.");
        Check((bool)Invoke(editor, "SaveDashboard")!, "Saving a valid Second edit failed.");
        AssertClean(editor, "Second");

        tree.SelectedNode = tree.Nodes[0];
        AssertCanvasBound(second, "Canvas selection after widget edit");
        Open(editor, "First");
        var reopened = GetField<C.DashboardDefinition>(editor, "_definition");
        Check(!ReferenceEquals(reopened, first), "Reopening must create a fresh document instance.");
        AssertCanvasBound(reopened, "Second to First");
        Invoke(editor, "ReloadDashboard");
        var reloaded = GetField<C.DashboardDefinition>(editor, "_definition");
        Check(!ReferenceEquals(reloaded, reopened), "Reload must publish a fresh document instance.");
        AssertCanvasBound(reloaded, "Same-document reload");
    });

    public static void MigrationAndSave() => WithEditor((root, editor) =>
    {
        var path = CreateDashboard(root, "Legacy");
        var source = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        source["schemaVersion"] = DashboardSchemaMigration.GraphSignalSchemaVersion;
        File.WriteAllText(path, source.ToJsonString());
        var persistedBefore = File.ReadAllText(path);

        Open(editor, "Legacy");
        AssertDirty(editor, "A real schema-25-to-current migration must require Save.");
        Check(GetField<bool>(editor, "_loadRequiresSave"), "Migration persistence obligation was lost.");
        Check(GetField<string>(editor, "_savedJson").Length != 0,
            "Migration must retain a usable canonical undo baseline.");
        Check(File.ReadAllText(path) == persistedBefore,
            "Loading a legacy dashboard silently rewrote the persisted JSON.");

        EditCanvasAndCheckpoint(editor);
        Invoke(editor, "Undo");
        AssertDirty(editor, "Undo must not discard a pending migration.");

        Check((bool)Invoke(editor, "SaveDashboard")!, "Saving the migrated dashboard failed.");
        AssertClean(editor, "Legacy");
        Check(JsonNode.Parse(File.ReadAllText(path))!["schemaVersion"]!.GetValue<int>() ==
              DashboardSchemaMigration.CurrentSchemaVersion, "Saved migration did not persist the current schema.");
        Open(editor, "Legacy");
        AssertClean(editor, "Legacy");
    });

    public static void NormalizationAndUndo() => WithEditor((root, editor) =>
    {
        var path = CreateDashboard(root, "Grouped");
        var definition = C.DashboardDefinition.Load(path);
        Check(definition.Widgets.Count >= 3, "The normalization fixture requires three widgets.");

        // Group the first and last widget. Their contiguous hierarchy traversal
        // must change the source's originally interleaved canonical Z-order.
        var metadata = new DashboardEditorMetadata();
        var group = metadata.CreateGroup("Noncontiguous", parentGroupId: null);
        metadata.SetWidgetGroup(definition.Widgets[0].Id, group.Id);
        metadata.SetWidgetGroup(definition.Widgets[^1].Id, group.Id);
        metadata.Save(Path.Combine(Path.GetDirectoryName(path)!, ".editor.json"));
        var persistedBefore = File.ReadAllText(path);

        Open(editor, "Grouped");
        AssertDirty(editor, "Real hierarchy Z-order normalization must require Save.");
        Check(GetField<bool>(editor, "_loadRequiresSave"), "Normalization persistence obligation was lost.");
        Check(File.ReadAllText(path) == persistedBefore,
            "Loading a normalized dashboard silently changed dashboard.json.");

        EditCanvasAndCheckpoint(editor);
        Invoke(editor, "Undo");
        AssertDirty(editor, "Undo must preserve pending hierarchy normalization.");
        Check((bool)Invoke(editor, "SaveDashboard")!, "Saving the normalized dashboard failed.");
        AssertClean(editor, "Grouped");
        Open(editor, "Grouped");
        AssertClean(editor, "Grouped");
    });

    public static void EditUndoRedoAndSave() => WithEditor((root, editor) =>
    {
        CreateDashboard(root, "Editable");
        Open(editor, "Editable");
        AssertClean(editor, "Editable");

        EditCanvasAndCheckpoint(editor);
        AssertDirty(editor, "A real user edit did not enable Save.");
        Invoke(editor, "Undo");
        AssertClean(editor, "Editable");
        Invoke(editor, "Redo");
        AssertDirty(editor, "Redo did not restore dirty state.");

        Check((bool)Invoke(editor, "SaveDashboard")!, "Save returned failure.");
        AssertClean(editor, "Editable");

        EditCanvasAndCheckpoint(editor);
        Invoke(editor, "Undo");
        AssertClean(editor, "Editable");
        Check((bool)Invoke(editor, "ConfirmSaveBeforeDestructiveAction")!,
            "Undo to the last Save must not prompt on switching.");
    });

    private static void WithEditor(Action<string, EditorForm> test)
    {
        // Borrow the application's bundled fonts without copying/deleting native font files.
        // Only the uniquely named scratch directory belongs to this regression.
        var applicationRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        var root = Path.Combine(applicationRoot, "artifacts", "issue102-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? scenarioFailure = null;
        EditorForm? editor = null;
        try
        {
            Issue84EditorStartupTests.CreateEditorIconFixtures(root);
            using (editor = new EditorForm(root, initialDashboard: null))
                test(root, editor);
        }
        catch (Exception error) { scenarioFailure = error; throw; }
        finally
        {
            if (Directory.Exists(root))
                RegressionFixtureCleanup.Delete(root, scenarioFailure, editor?.IsDisposed);
        }
    }

    private static string CreateDashboard(string root, string name)
    {
        var directory = Path.Combine(DashboardCatalog.GetRoot(root), name);
        EditorForm.WriteNewDashboardFile(directory, 640, 640, orientation: 0);
        var path = DashboardCatalog.GetDefinitionPath(root, name);
        var definition = C.DashboardDefinition.Load(path);
        // Equal Z values must retain source order, not be sorted by widget ID.
        foreach (var widget in definition.Widgets)
            widget.Z = 0;
        File.WriteAllText(path, DashboardJson.Serialize(definition, indented: true) + Environment.NewLine);
        return path;
    }

    private static void Open(EditorForm editor, string name)
    {
        Invoke(editor, "LoadDashboard", name);
        Check(GetField<string>(editor, "_dashboardName") == name,
            "The Editor did not open dashboard '" + name + "'.");
    }

    private static void EditCanvasAndCheckpoint(EditorForm editor)
    {
        var prior = Invoke(editor, "CaptureSnapshot")
            ?? throw new InvalidOperationException("Failed to capture the Editor undo checkpoint.");
        GetField<C.DashboardDefinition>(editor, "_definition").Canvas.Width += 10;
        Invoke(editor, "PushUndo", prior);
        Invoke(editor, "MarkDirty");
    }

    private static void AssertClean(EditorForm editor, string dashboard)
    {
        Check(!GetField<bool>(editor, "_dirty"), dashboard + " was marked modified without an unsaved change.");
        Check(!GetField<bool>(editor, "_loadRequiresSave"), dashboard + " has a stale load-normalization flag.");
        Check(!GetField<ToolStripMenuItem>(editor, "_saveMenuItem").Enabled, dashboard + " has Save enabled.");
        Check(!editor.Text.EndsWith(" *", StringComparison.Ordinal), dashboard + " has a dirty title indicator.");
        var expected = DashboardJson.SerializeEditorSnapshot(GetField<C.DashboardDefinition>(editor, "_definition"));
        Check(GetField<string>(editor, "_savedJson") == expected,
            dashboard + " did not preserve the current canonical Save baseline.");
    }

    private static void AssertDirty(EditorForm editor, string message)
    {
        Check(GetField<bool>(editor, "_dirty"), message);
        Check(GetField<ToolStripMenuItem>(editor, "_saveMenuItem").Enabled, message + " (Save disabled)");
        Check(editor.Text.EndsWith(" *", StringComparison.Ordinal), message + " (title indicator missing)");
    }

    private static object? Invoke(EditorForm editor, string method, params object?[] arguments) =>
        (typeof(EditorForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
         ?? throw new InvalidOperationException("Missing Editor method: " + method)).Invoke(editor, arguments);

    private static T GetField<T>(EditorForm editor, string field) =>
        (T)(typeof(EditorForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Editor field: " + field)).GetValue(editor)!;

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
