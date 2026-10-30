using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70ConcurrentEditorSettingsTests
{
    public static void SaveWithOpenSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-editor-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "editor.json");
            // Retain Load's BOM detection and permissive JSON compatibility.
            File.WriteAllText(path, "{ /* existing preferences */ \"theme\": \"Dark\", \"gridStep\": 40, }", System.Text.Encoding.Unicode);
            var initial = EditorSettings.Load(path);
            if (initial.Theme != "Dark" || initial.GridStep != 40)
                throw new InvalidOperationException("Editor settings must retain BOM, case, comment and trailing-comma support.");
            initial.GetDashboard("Source").Zoom = "75%";
            initial.Save(path, "Source");
            var previousJson = File.ReadAllText(path);

            // Hold the exact reader used by Load open without consuming its data.
            // On Windows this deterministically exercises replacement sharing;
            // other platforms check snapshot and merge semantics only.
            using (var snapshot = EditorSettings.OpenSnapshotReader(path))
            {
                var next = EditorSettings.Load(path);
                next.Theme = "Light";
                next.GetDashboard("Destination").Zoom = "125%";
                next.Save(path, "Destination");
                var current = EditorSettings.Load(path);
                if (snapshot.ReadToEnd() != previousJson || current.Theme != "Light" ||
                    current.GridStep != 40 || current.GetDashboard("Source").Zoom != "75%" ||
                    current.GetDashboard("Destination").Zoom != "125%")
                    throw new InvalidOperationException("An open reader must retain the old snapshot while a new save is visible and merged.");
            }
            if (Directory.GetFiles(root, "*.tmp-*").Length != 0)
                throw new InvalidOperationException("Saving with an open reader must leave no temporary files.");

            File.WriteAllText(path, "{ malformed");
            if (EditorSettings.Load(path).GridStep != 25)
                throw new InvalidOperationException("Malformed Editor settings must still fall back to defaults.");
            File.Delete(path);
            if (EditorSettings.Load(path).GridStep != 25)
                throw new InvalidOperationException("Missing Editor settings must still fall back to defaults.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void MergesDashboardViews()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-editor-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "editor.json");
            var first = EditorSettings.Load(path);
            var second = EditorSettings.Load(path);
            first.GetDashboard("Source").Zoom = "75%";
            second.GetDashboard("Destination").Zoom = "125%";

            first.Save(path, "Source");
            second.Save(path, "Destination");

            var saved = EditorSettings.Load(path);
            if (saved.GetDashboard("Source").Zoom != "75%" ||
                saved.GetDashboard("Destination").Zoom != "125%")
                throw new InvalidOperationException("Saving a second Editor must not discard another dashboard's view.");
            var simultaneous = Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            {
                var local = EditorSettings.Load(path);
                var board = $"Concurrent-{index}";
                local.GetDashboard(board).Zoom = $"{index + 30}%";
                local.Save(path, board);
            })).ToArray();
            Task.WaitAll(simultaneous);
            var merged = EditorSettings.Load(path);
            for (var index = 0; index < 8; index++)
            {
                if (merged.GetDashboard($"Concurrent-{index}").Zoom != $"{index + 30}%")
                    throw new InvalidOperationException("Concurrent Editors lost a dashboard view update.");
            }

            if (Directory.GetFiles(root, "*.tmp-*").Length != 0)
                throw new InvalidOperationException("Editor settings temporary files must be cleaned up.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
