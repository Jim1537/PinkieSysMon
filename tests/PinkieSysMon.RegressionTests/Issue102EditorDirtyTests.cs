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
        var root = Path.Combine(Path.GetTempPath(), "psm-issue102-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Issue84EditorStartupTests.CreateEditorIconFixtures(root);
            using var editor = new EditorForm(root, initialDashboard: null);
            test(root, editor);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateDashboard(string root, string name)
    {
        var directory = Path.Combine(DashboardCatalog.GetRoot(root), name);
        EditorForm.WriteNewDashboardFile(directory, 640, 640, orientation: 0);
        return DashboardCatalog.GetDefinitionPath(root, name);
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
