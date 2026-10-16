using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70PastePlannerTests
{
    public static void TransfersNestedGroups()
    {
        var root = Path.Combine(Path.GetTempPath(), "psm-paste-plan-" + Guid.NewGuid().ToString("N"));
        var destinationDirectory = Path.Combine(root, "dashboards", "target");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(destinationDirectory);
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        var destinationPath = Path.Combine(destinationDirectory, "dashboard.json");
        try
        {
            var existing = NewImage("image-1", "Existing", 0, 0);
            var original = NewImage("image-1", "Copied", 21, 33);
            original.Opacity = 0.4f;
            original.Asset.Fit = StateVisualProfileContract.FitContain;
            var child = NewImage("image-2", "Nested", 90, 100);
            var destination = new C.DashboardDefinition
            {
                BaseDirectory = destinationDirectory,
                Canvas = new C.CanvasDefinition { Width = 200, Height = 200 },
                Widgets = [existing]
            };
            var metadata = new DashboardEditorMetadata();
            metadata.CreateGroup("Indicators", null);

            var package = new WidgetTransferPackage
            {
                Version = 1,
                SourceDirectory = destinationDirectory,
                Groups =
                [
                    new WidgetTransferGroup { Id = "foreign-top", Name = "Indicators" },
                    new WidgetTransferGroup { Id = "foreign-child", Name = "Nested", ParentGroupId = "foreign-top" }
                ],
                RootGroupIds = ["foreign-top"],
                WidgetGroupIds = new Dictionary<string, string>
                {
                    ["image-1"] = "foreign-top", ["image-2"] = "foreign-child"
                }
            };

            var staged = WidgetPastePlanner.Prepare(
                destination, metadata, [original, child], package, destinationPath);
            if (destination.Widgets.Count != 1 || destination.Widgets[0].Id != "image-1" ||
                metadata.GroupDefinitions.Count != 1)
                throw new InvalidOperationException("Paste planner mutated the source/destination document.");
            if (staged.Definition.Widgets.Count != 3 ||
                staged.Definition.Widgets.Select(w => w.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
                throw new InvalidOperationException("Pasted widgets must have distinct destination IDs.");

            var copied = staged.Definition.Widgets.Single(w => w.Name == "Copied");
            var nested = staged.Definition.Widgets.Single(w => w.Name == "Nested");
            if (copied.X != 37 || copied.Y != 49 || nested.X != 106 || nested.Y != 116 ||
                ((C.ImageWidgetDefinition)copied).Opacity != 0.4f ||
                ((C.ImageWidgetDefinition)copied).Asset.Fit != StateVisualProfileContract.FitContain)
                throw new InvalidOperationException("Widget geometry and configuration were not preserved.");

            var rootGroup = staged.Metadata.GroupDefinitions.Single(g => g.Name == "Indicators 2");
            var nestedGroup = staged.Metadata.GroupDefinitions.Single(g => g.Name == "Nested");
            if (nestedGroup.ParentGroupId != rootGroup.Id ||
                staged.Metadata.GetWidgetGroupId(copied.Id) != rootGroup.Id ||
                staged.Metadata.GetWidgetGroupId(nested.Id) != nestedGroup.Id ||
                staged.SelectionKeys.Count != 1 ||
                staged.SelectionKeys[0] != $"g:{rootGroup.Id}")
                throw new InvalidOperationException("Nested group hierarchy or selection was not preserved.");

            var saved = DashboardJson.Serialize(staged.Definition);
            var restored = C.DashboardDefinition.Parse(saved, destinationPath);
            var metadataRestored = DashboardEditorMetadata.FromJson(staged.Metadata.ToCompactJson());
            if (restored.Widgets.Count != 3 ||
                metadataRestored.GetWidgetGroupId(restored.Widgets.Single(w => w.Name == "Nested").Id) != nestedGroup.Id)
                throw new InvalidOperationException("Pasted hierarchy did not survive Save/reopen.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void RejectsMalformedMappingsWithoutMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "psm-paste-reject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "dashboard.json");
            var destination = new C.DashboardDefinition
            {
                BaseDirectory = directory,
                Canvas = new C.CanvasDefinition { Width = 100, Height = 100 },
                Widgets = [NewImage("existing", "Existing", 0, 0)]
            };
            var metadata = new DashboardEditorMetadata();
            var source = NewImage("copy", "Copied", 4, 9);
            var bad = new WidgetTransferPackage
            {
                Version = 1,
                Groups = [new WidgetTransferGroup { Id = "child", Name = "Child", ParentGroupId = "absent" }],
                RootGroupIds = [],
                WidgetGroupIds = new Dictionary<string, string> { ["copy"] = "child" }
            };
            var rejected = false;
            try { _ = WidgetPastePlanner.Prepare(destination, metadata, [source], bad, path); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected || destination.Widgets.Count != 1 || metadata.GroupDefinitions.Count != 0 ||
                destination.Widgets[0].Id != "existing")
                throw new InvalidOperationException("Malformed group payload must not mutate the destination.");

            var standalone = new WidgetTransferPackage { Version = 1 };
            var accepted = WidgetPastePlanner.Prepare(destination, metadata, [source], standalone, path);
            if (accepted.Metadata.GroupDefinitions.Count != 0 ||
                accepted.SelectionKeys.Count != 1 ||
                !accepted.SelectionKeys[0].StartsWith("w:", StringComparison.Ordinal) ||
                accepted.Definition.Widgets.Count != 2)
                throw new InvalidOperationException("Standalone pasted widgets must remain top-level.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static C.ImageWidgetDefinition NewImage(string id, string name, float x, float y) => new()
    {
        Id = id, Name = name, X = x, Y = y, Width = 24, Height = 18,
        Asset = new C.ImageAssetPresentationDefinition { Source = null }
    };
}
