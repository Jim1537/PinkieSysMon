using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

// A deterministic, detached paste plan: no clipboard reads, dialogs or filesystem writes.
internal sealed record WidgetPastePlan(
    C.DashboardDefinition Definition,
    DashboardEditorMetadata Metadata,
    IReadOnlyList<string> SelectionKeys);

internal static class WidgetPastePlanner
{
    public static WidgetPastePlan Prepare(
        C.DashboardDefinition destination,
        DashboardEditorMetadata destinationMetadata,
        IReadOnlyList<C.WidgetDefinition> transferredWidgets,
        WidgetTransferPackage package,
        string destinationPath)
    {
        var staged = DashboardJson.ParseEditorSnapshot(
            DashboardJson.SerializeEditorSnapshot(destination), destinationPath);
        var metadata = DashboardEditorMetadata.FromJson(destinationMetadata.ToCompactJson());
        if (transferredWidgets.Count == 0 && package.Groups.Count == 0)
            throw new InvalidDataException("Clipboard does not contain widgets or groups.");

        var usedIds = new HashSet<string>(
            staged.Widgets.Select(widget => widget.Id
                ?? throw new InvalidDataException("Destination widget ID is missing.")),
            StringComparer.OrdinalIgnoreCase);
        var newIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clones = new List<C.WidgetDefinition>(transferredWidgets.Count);
        var nextZ = staged.Widgets.Count == 0 ? -1 : staged.Widgets.Max(widget => widget.Z);
        foreach (var original in transferredWidgets)
        {
            var clone = DashboardJson.CloneWidget(original);
            var oldId = clone.Id;
            if (string.IsNullOrWhiteSpace(oldId) || !newIds.TryAdd(oldId, string.Empty))
                throw new InvalidDataException("Clipboard widget IDs must be present and unique.");
            var suffix = 1;
            string id;
            do { id = $"{clone.Type}-{suffix++}"; } while (!usedIds.Add(id));
            newIds[oldId] = id;
            clone.Id = id;
            clone.X += 16;
            clone.Y += 16;
            clone.Z = ++nextZ;
            staged.Widgets.Add(clone);
            clones.Add(clone);
        }

        var rootIds = new HashSet<string>(package.RootGroupIds, StringComparer.OrdinalIgnoreCase);
        if (rootIds.Count != package.RootGroupIds.Count)
            throw new InvalidDataException("Clipboard root group IDs are duplicated.");
        var newGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in package.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name) ||
                newGroups.ContainsKey(group.Id))
                throw new InvalidDataException("Clipboard group definitions are invalid.");
            string? parent = null;
            if (!rootIds.Contains(group.Id) && !string.IsNullOrWhiteSpace(group.ParentGroupId) &&
                !newGroups.TryGetValue(group.ParentGroupId, out parent))
                throw new InvalidDataException("Clipboard group parent is missing or out of order.");

            var name = group.Name.Trim();
            if (metadata.NameExistsAmongSiblings(name, parent))
            {
                for (var number = 2; ; number++)
                {
                    var candidate = $"{name} {number}";
                    if (metadata.NameExistsAmongSiblings(candidate, parent))
                        continue;
                    name = candidate;
                    break;
                }
            }
            newGroups.Add(group.Id, metadata.CreateGroup(name, parent).Id);
        }
        if (rootIds.Any(id => !newGroups.ContainsKey(id)))
            throw new InvalidDataException("Clipboard selected group is missing.");
        foreach (var pair in package.WidgetGroupIds)
        {
            if (!newIds.TryGetValue(pair.Key, out var widgetId) ||
                !newGroups.TryGetValue(pair.Value, out var groupId))
                throw new InvalidDataException("Clipboard widget group mapping is invalid.");
            metadata.SetWidgetGroup(widgetId, groupId);
        }

        var hierarchy = new EditorLayerHierarchy(staged, metadata);
        hierarchy.SyncGroupDeclarationOrder();
        var ordered = hierarchy.FlattenWidgets();
        if (ordered.Count != staged.Widgets.Count)
            throw new InvalidDataException("Clipboard layer hierarchy did not preserve all widgets.");
        staged.Widgets.Clear();
        staged.Widgets.AddRange(ordered);
        for (var i = 0; i < ordered.Count; i++)
            staged.Widgets[i].Z = i;
        staged.Validate();

        var selected = rootIds.Count > 0
            ? package.RootGroupIds.Select(id => $"g:{newGroups[id]}").ToArray()
            : clones.Select(widget => $"w:{widget.Id}").ToArray();
        return new WidgetPastePlan(staged, metadata, selected);
    }
}
