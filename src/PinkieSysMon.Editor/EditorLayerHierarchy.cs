using PinkieSysMon;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon.Editor;

internal enum EditorLayerNodeKind
{
    Widget,
    Group
}

internal readonly record struct EditorLayerNodeRef(EditorLayerNodeKind Kind, string Id)
{
    public string Key => Kind == EditorLayerNodeKind.Group ? $"g:{Id}" : $"w:{Id}";

    public static EditorLayerNodeRef Widget(string id) => new(EditorLayerNodeKind.Widget, id);
    public static EditorLayerNodeRef Group(string id) => new(EditorLayerNodeKind.Group, id);
}

internal static class EditorLayerSelection
{
    public static EditorLayerNodeKind? GetHomogeneousKind(IReadOnlyList<EditorLayerNodeRef> selected)
    {
        if (selected.Count == 0)
            return null;

        var kind = selected[0].Kind;
        return selected.All(node => node.Kind == kind) ? kind : null;
    }
}

internal sealed class EditorLayerHierarchy
{
    private const string RootKey = "";

    private readonly CanonicalDashboardDefinition _definition;
    private readonly DashboardEditorMetadata _metadata;
    private readonly Dictionary<string, CanonicalWidgetDefinition> _widgetsById;
    private readonly Dictionary<string, List<EditorLayerNodeRef>> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _groupDeclarationOrder;

    public EditorLayerHierarchy(CanonicalDashboardDefinition definition, DashboardEditorMetadata metadata)
    {
        _definition = definition;
        _metadata = metadata;
        _widgetsById = definition.Widgets
            .Where(widget => !string.IsNullOrWhiteSpace(widget.Id))
            .ToDictionary(widget => widget.Id!, StringComparer.OrdinalIgnoreCase);
        _groupDeclarationOrder = metadata.GroupDefinitions
            .Select((group, index) => new { group.Id, index })
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);

        _children[RootKey] = [];
        foreach (var group in metadata.GroupDefinitions)
            _children.TryAdd(ParentKey(group.Id), []);
        foreach (var group in metadata.GroupDefinitions)
            GetMutableChildren(group.ParentGroupId).Add(EditorLayerNodeRef.Group(group.Id));
        foreach (var widget in definition.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id))
                continue;
            GetMutableChildren(metadata.GetWidgetGroupId(widget.Id)).Add(EditorLayerNodeRef.Widget(widget.Id));
        }

        SortAllChildren();
    }

    public IReadOnlyList<EditorLayerNodeRef> GetChildren(string? parentGroupId) =>
        GetExistingChildren(parentGroupId);

    public string? GetParentGroupId(EditorLayerNodeRef node)
    {
        if (node.Kind == EditorLayerNodeKind.Group)
            return _metadata.GetGroup(node.Id)?.ParentGroupId;
        return _metadata.GetWidgetGroupId(node.Id);
    }

    public CanonicalWidgetDefinition? GetWidget(EditorLayerNodeRef node) =>
        node.Kind == EditorLayerNodeKind.Widget && _widgetsById.TryGetValue(node.Id, out var widget)
            ? widget
            : null;

    public EditorGroupDefinition? GetGroup(EditorLayerNodeRef node) =>
        node.Kind == EditorLayerNodeKind.Group ? _metadata.GetGroup(node.Id) : null;

    public IReadOnlyList<CanonicalWidgetDefinition> GetDescendantWidgets(string groupId)
    {
        var result = new List<CanonicalWidgetDefinition>();
        CollectDescendantWidgets(groupId, result);
        return result;
    }

    public IReadOnlyList<EditorLayerNodeRef> GetPreOrderNodes()
    {
        var result = new List<EditorLayerNodeRef>();
        CollectPreOrder(null, result);
        return result;
    }

    public IReadOnlyList<EditorLayerNodeRef> CanonicalizeSelection(IEnumerable<EditorLayerNodeRef> selected)
    {
        var selectedKeys = new HashSet<string>(selected.Select(node => node.Key), StringComparer.OrdinalIgnoreCase);
        var ordered = GetPreOrderNodes().Where(node => selectedKeys.Contains(node.Key)).ToList();
        return ordered.Where(node => !HasSelectedAncestor(node, selectedKeys)).ToArray();
    }

    public bool CanReparent(EditorLayerNodeRef node, string? newParentGroupId)
    {
        if (!string.IsNullOrWhiteSpace(newParentGroupId) && _metadata.GetGroup(newParentGroupId) is null)
            return false;
        if (node.Kind != EditorLayerNodeKind.Group)
            return true;
        if (string.Equals(node.Id, newParentGroupId, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.IsNullOrWhiteSpace(newParentGroupId) || !_metadata.IsDescendantGroup(newParentGroupId, node.Id);
    }

    public bool CanMoveNodes(IReadOnlyList<EditorLayerNodeRef> nodes, string? newParentGroupId, out string? error)
    {
        foreach (var node in nodes)
        {
            if (!CanReparent(node, newParentGroupId))
            {
                error = "A group cannot be moved into itself or one of its descendants.";
                return false;
            }
        }

        var movingGroupIds = nodes
            .Where(node => node.Kind == EditorLayerNodeKind.Group)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Where(node => node.Kind == EditorLayerNodeKind.Group))
        {
            var group = _metadata.GetGroup(node.Id);
            if (group is null)
                continue;
            if (!names.Add(group.Name))
            {
                error = $"Two selected groups are both named '{group.Name}' and cannot become siblings.";
                return false;
            }
        }

        foreach (var sibling in GetMutableChildren(newParentGroupId))
        {
            if (sibling.Kind != EditorLayerNodeKind.Group || movingGroupIds.Contains(sibling.Id))
                continue;
            var siblingGroup = _metadata.GetGroup(sibling.Id);
            if (siblingGroup is not null && names.Contains(siblingGroup.Name))
            {
                error = $"A group named '{siblingGroup.Name}' already exists at the destination level.";
                return false;
            }
        }

        error = null;
        return true;
    }


    public void ReorderChildren(string? parentGroupId, IReadOnlyList<EditorLayerNodeRef> orderedChildren)
    {
        var current = GetMutableChildren(parentGroupId);
        if (current.Count != orderedChildren.Count ||
            current.Select(node => node.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(orderedChildren.Select(node => node.Key)) == false)
        {
            throw new InvalidOperationException("Reordered children must contain exactly the existing sibling nodes.");
        }

        current.Clear();
        current.AddRange(orderedChildren);
    }

    public void MoveNodes(IReadOnlyList<EditorLayerNodeRef> nodes, string? destinationParentGroupId, int destinationIndex)
    {
        if (nodes.Count == 0)
            return;
        if (!CanMoveNodes(nodes, destinationParentGroupId, out var error))
            throw new InvalidOperationException(error ?? "The selected layer nodes cannot be moved to that destination.");

        foreach (var node in nodes)
            RemoveNode(node);

        var destination = GetMutableChildren(destinationParentGroupId);
        destinationIndex = Math.Clamp(destinationIndex, 0, destination.Count);
        destination.InsertRange(destinationIndex, nodes);

        foreach (var node in nodes)
        {
            if (node.Kind == EditorLayerNodeKind.Group)
            {
                var group = _metadata.GetGroup(node.Id)
                    ?? throw new InvalidOperationException($"Unknown editor group '{node.Id}'.");
                group.ParentGroupId = string.IsNullOrWhiteSpace(destinationParentGroupId) ? null : destinationParentGroupId;
            }
            else
            {
                _metadata.SetWidgetGroup(node.Id, destinationParentGroupId);
            }
        }
    }

    public void WrapNodesInGroup(IReadOnlyList<EditorLayerNodeRef> nodes, EditorGroupDefinition group)
    {
        if (nodes.Count == 0)
            return;

        var parentId = nodes.Select(GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentId.Length != 1)
            throw new InvalidOperationException("Selected nodes must have the same parent to create a group.");

        var siblings = GetMutableChildren(parentId[0]);
        var indices = nodes.Select(node => siblings.IndexOf(node)).Where(index => index >= 0).OrderBy(index => index).ToArray();
        if (indices.Length != nodes.Count)
            throw new InvalidOperationException("Could not locate every selected node in its parent group.");

        var insertionIndex = indices[0];
        foreach (var node in nodes)
            RemoveNode(node);

        siblings = GetMutableChildren(parentId[0]);
        insertionIndex = Math.Clamp(insertionIndex, 0, siblings.Count);
        siblings.Insert(insertionIndex, EditorLayerNodeRef.Group(group.Id));
        _children[ParentKey(group.Id)] = nodes.ToList();

        foreach (var node in nodes)
        {
            if (node.Kind == EditorLayerNodeKind.Group)
            {
                var childGroup = _metadata.GetGroup(node.Id)!;
                childGroup.ParentGroupId = group.Id;
            }
            else
            {
                _metadata.SetWidgetGroup(node.Id, group.Id);
            }
        }
    }

    public void PromoteAndDeleteGroup(string groupId)
    {
        var groupNode = EditorLayerNodeRef.Group(groupId);
        var parentId = GetParentGroupId(groupNode);
        var siblings = GetMutableChildren(parentId);
        var index = siblings.IndexOf(groupNode);
        if (index < 0)
        {
            _metadata.DeleteGroupPromoteContents(groupId);
            return;
        }

        var children = GetMutableChildren(groupId).ToArray();
        siblings.RemoveAt(index);
        siblings.InsertRange(index, children);
        foreach (var child in children)
        {
            if (child.Kind == EditorLayerNodeKind.Group)
                _metadata.GetGroup(child.Id)!.ParentGroupId = parentId;
            else
                _metadata.SetWidgetGroup(child.Id, parentId);
        }
        _children.Remove(ParentKey(groupId));
        var group = _metadata.GetGroup(groupId);
        if (group is not null)
            _metadata.GroupDefinitions.Remove(group);
    }

    public List<CanonicalWidgetDefinition> FlattenWidgets()
    {
        var result = new List<CanonicalWidgetDefinition>(_definition.Widgets.Count);
        FlattenInto(null, result);
        return result;
    }

    public void SyncGroupDeclarationOrder()
    {
        var orderedGroups = new List<EditorGroupDefinition>();
        CollectGroupsInPreOrder(null, orderedGroups);
        _metadata.GroupDefinitions.Clear();
        _metadata.GroupDefinitions.AddRange(orderedGroups);
    }

    private void SortAllChildren()
    {
        foreach (var pair in _children)
        {
            pair.Value.Sort((left, right) =>
            {
                var leftZ = GetSortZ(left);
                var rightZ = GetSortZ(right);
                var compare = leftZ.CompareTo(rightZ);
                if (compare != 0)
                    return compare;

                if (left.Kind != right.Kind)
                    return left.Kind == EditorLayerNodeKind.Widget ? -1 : 1;

                if (left.Kind == EditorLayerNodeKind.Widget)
                    return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

                var leftOrder = _groupDeclarationOrder.GetValueOrDefault(left.Id, int.MaxValue);
                var rightOrder = _groupDeclarationOrder.GetValueOrDefault(right.Id, int.MaxValue);
                compare = leftOrder.CompareTo(rightOrder);
                return compare != 0
                    ? compare
                    : string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    private int GetSortZ(EditorLayerNodeRef node)
    {
        if (node.Kind == EditorLayerNodeKind.Widget)
            return _widgetsById.TryGetValue(node.Id, out var widget) ? widget.Z : int.MaxValue;

        var descendants = GetDescendantWidgets(node.Id);
        return descendants.Count == 0 ? int.MaxValue : descendants.Min(widget => widget.Z);
    }

    private void RemoveNode(EditorLayerNodeRef node)
    {
        foreach (var children in _children.Values)
        {
            var index = children.IndexOf(node);
            if (index >= 0)
            {
                children.RemoveAt(index);
                return;
            }
        }
    }

    private bool HasSelectedAncestor(EditorLayerNodeRef node, HashSet<string> selectedKeys)
    {
        var parentId = GetParentGroupId(node);
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (selectedKeys.Contains(EditorLayerNodeRef.Group(parentId).Key))
                return true;
            parentId = _metadata.GetGroup(parentId)?.ParentGroupId;
        }
        return false;
    }

    private void CollectDescendantWidgets(string groupId, List<CanonicalWidgetDefinition> result)
    {
        foreach (var child in GetExistingChildren(groupId))
        {
            if (child.Kind == EditorLayerNodeKind.Widget)
            {
                if (_widgetsById.TryGetValue(child.Id, out var widget))
                    result.Add(widget);
            }
            else
            {
                CollectDescendantWidgets(child.Id, result);
            }
        }
    }

    private void CollectPreOrder(string? parentGroupId, List<EditorLayerNodeRef> result)
    {
        foreach (var child in GetExistingChildren(parentGroupId))
        {
            result.Add(child);
            if (child.Kind == EditorLayerNodeKind.Group)
                CollectPreOrder(child.Id, result);
        }
    }

    private void CollectGroupsInPreOrder(string? parentGroupId, List<EditorGroupDefinition> result)
    {
        foreach (var child in GetExistingChildren(parentGroupId))
        {
            if (child.Kind != EditorLayerNodeKind.Group)
                continue;
            var group = _metadata.GetGroup(child.Id);
            if (group is null)
                continue;
            result.Add(group);
            CollectGroupsInPreOrder(group.Id, result);
        }
    }

    private void FlattenInto(string? parentGroupId, List<CanonicalWidgetDefinition> result)
    {
        foreach (var child in GetExistingChildren(parentGroupId))
        {
            if (child.Kind == EditorLayerNodeKind.Widget)
            {
                if (_widgetsById.TryGetValue(child.Id, out var widget))
                    result.Add(widget);
            }
            else
            {
                FlattenInto(child.Id, result);
            }
        }
    }


    private IReadOnlyList<EditorLayerNodeRef> GetExistingChildren(string? parentGroupId)
    {
        var key = ParentKey(parentGroupId);
        return _children.TryGetValue(key, out var children) ? children : Array.Empty<EditorLayerNodeRef>();
    }

    private List<EditorLayerNodeRef> GetMutableChildren(string? parentGroupId)
    {
        var key = ParentKey(parentGroupId);
        if (!_children.TryGetValue(key, out var children))
        {
            children = [];
            _children[key] = children;
        }
        return children;
    }

    private static string ParentKey(string? parentGroupId) => parentGroupId ?? RootKey;
}
