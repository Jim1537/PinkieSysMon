using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70ConcurrentEditorSettingsTests
{
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
