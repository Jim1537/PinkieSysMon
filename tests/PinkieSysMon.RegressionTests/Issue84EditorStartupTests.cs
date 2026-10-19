using System.Reflection;
using System.Windows.Forms;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue84EditorStartupTests
{
    public static void SelectionAndRestartArguments()
    {
        var discoveries = 0;
        IReadOnlyList<string> Discover()
        {
            discoveries++;
            return ["Example", "Other"];
        }

        if (EditorStartupContract.ResolveInitialDashboard(null, Discover) is not null ||
            EditorStartupContract.ResolveInitialDashboard("", Discover) is not null ||
            discoveries != 0)
            throw new InvalidOperationException("Ordinary Editor startup must not select or discover a dashboard.");

        if (EditorStartupContract.ResolveInitialDashboard("other", Discover) != "Other" ||
            EditorStartupContract.ResolveInitialDashboard("Missing", Discover) != "Example" ||
            discoveries != 2)
            throw new InvalidOperationException("Explicit Editor startup lost its existing selection or fallback.");

        if (EditorStartupContract.ResolveInitialDashboard(null, () => Array.Empty<string>()) is not null)
            throw new InvalidOperationException("A fresh installation should launch document-free.");

        try
        {
            EditorStartupContract.ResolveInitialDashboard("Example", () => Array.Empty<string>());
            throw new InvalidOperationException("An explicit name unexpectedly succeeded without saved dashboards.");
        }
        catch (InvalidDataException ex) when (ex.Message == "No dashboards were found.")
        {
            // Explicit startup retains the historical missing-catalog error.
        }

        var noDocument = EditorStartupContract.GetRestartArguments("test-root", null);
        var withDocument = EditorStartupContract.GetRestartArguments("test-root", "Example");
        if (!noDocument.SequenceEqual(["--root", "test-root"]) ||
            !withDocument.SequenceEqual(["--root", "test-root", "--dashboard", "Example"]))
            throw new InvalidOperationException("Editor theme restart did not preserve document-free/named state.");
    }

    public static void EmptyWorkspaceControls()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue84-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CreateToolbarIconFixtures(root);
            using var editor = new EditorForm(root, initialDashboard: null);
            if (editor.Text != "PinkieSysMon Dashboard Editor" ||
                GetField<object?>(editor, "_definition") is not null ||
                GetField<object?>(editor, "_preview") is not null)
                throw new InvalidOperationException("Ordinary Editor launch must open with no dashboard document or preview.");

            var open = GetField<ToolStripMenuItem>(editor, "_openDashboardMenuItem");
            var save = GetField<ToolStripMenuItem>(editor, "_saveMenuItem");
            var reload = GetField<ToolStripMenuItem>(editor, "_reloadEditorMenuItem");
            var snapshot = GetField<ToolStripMenuItem>(editor, "_dashboardSnapshotMenuItem");
            var newButton = GetField<ToolStripButton>(editor, "_toolbarNewButton");
            var tree = GetField<TreeView>(editor, "_widgetTree");
            var properties = GetField<PropertyGrid>(editor, "_propertyGrid");
            var statusDashboard = GetField<ToolStripStatusLabel>(editor, "_statusDashboard");
            var statusCanvas = GetField<ToolStripStatusLabel>(editor, "_statusCanvas");
            var statusZoom = GetField<ToolStripStatusLabel>(editor, "_statusZoom");

            if (!open.Enabled || !newButton.Enabled || save.Enabled || reload.Enabled || snapshot.Enabled ||
                tree.Nodes.Count != 0 || properties.SelectedObject is not null ||
                statusDashboard.Text != "Dashboard: —" || statusCanvas.Text != "Canvas: —" ||
                statusZoom.Text != "Zoom: —")
                throw new InvalidOperationException("The document-free Editor has stale data or incorrect command states.");

            // Empty-state startup must also work when a saved dashboard exists.
            var dashboardDir = Path.Combine(root, "dashboards", "Example");
            Directory.CreateDirectory(dashboardDir);
            File.WriteAllText(Path.Combine(dashboardDir, "dashboard.json"), "{}");
            using var second = new EditorForm(root, initialDashboard: null);
            if (GetField<object?>(second, "_definition") is not null ||
                second.Text != "PinkieSysMon Dashboard Editor")
                throw new InvalidOperationException("An existing dashboard was selected without --dashboard.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CreateToolbarIconFixtures(string root)
    {
        // EditorForm loads the real Lucide toolbar while constructing the UI.
        // Isolate document-startup behavior with minimal valid SVG assets rather
        // than requiring a developer installation or bundling an icon collection.
        var iconDirectory = Path.Combine(root, "assets", "icons", "Lucide");
        Directory.CreateDirectory(iconDirectory);
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M4 4h16v16H4z\"/></svg>";
        foreach (var icon in new[]
        {
            "file-plus", "folder-open", "save", "refresh-cw", "plus",
            "copy", "clipboard-paste", "copy-plus", "undo-2", "redo-2",
            "group", "ungroup", "zoom-in", "zoom-out", "focus",
            "move-vertical", "grid-3x3", "monitor"
        })
        {
            File.WriteAllText(Path.Combine(iconDirectory, icon + ".svg"), svg);
        }
    }

    private static T GetField<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Editor state field: " + name);
        return (T)field.GetValue(owner)!;
    }
}
