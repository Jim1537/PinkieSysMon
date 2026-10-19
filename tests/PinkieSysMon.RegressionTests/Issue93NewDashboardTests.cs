using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue93NewDashboardTests
{
    public static void CreateSaveAndReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "psm-issue93-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var orientation in new[] { 0, 90, 180, 270 })
            {
                var name = $"New-{orientation}";
                var directory = Path.Combine(DashboardCatalog.GetRoot(root), name);

                // Exercise the exact file-creation method called by EditorForm.NewDashboard.
                EditorForm.WriteNewDashboardFile(directory, orientation);
                var path = DashboardCatalog.GetDefinitionPath(root, name);
                if (!File.Exists(path))
                    throw new InvalidOperationException("New Dashboard did not write dashboard.json.");

                var json = File.ReadAllText(path);
                var persisted = JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidOperationException("New Dashboard wrote invalid JSON.");
                if (persisted["schemaVersion"]?.GetValue<int>() != DashboardSchemaMigration.CurrentSchemaVersion)
                    throw new InvalidOperationException("New Dashboard wrote a stale schema version.");

                var loaded = C.DashboardDefinition.Load(path);
                if (loaded.SchemaVersion != DashboardSchemaMigration.CurrentSchemaVersion ||
                    loaded.Canvas.Width != FrameGeometry.DefaultNativeWidth ||
                    loaded.Canvas.Height != FrameGeometry.DefaultNativeHeight ||
                    loaded.Canvas.Orientation != orientation ||
                    loaded.Canvas.BackgroundColor != "#000000" ||
                    loaded.Widgets.Count != 0)
                    throw new InvalidOperationException("New Dashboard lost its canonical canvas or empty-widget defaults.");

                // Exercise the Editor's strict serializer and an actual second disk load.
                var saved = DashboardJson.Serialize(loaded, indented: true) + Environment.NewLine;
                File.WriteAllText(path, saved);
                var reopened = C.DashboardDefinition.Load(path);
                if (DashboardJson.Serialize(reopened, indented: false) !=
                    DashboardJson.Serialize(loaded, indented: false))
                    throw new InvalidOperationException("New Dashboard changed after Save/reopen.");
            }

            if (DashboardCatalog.Discover(root).Count != 4)
                throw new InvalidOperationException("New dashboards were not discoverable.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
