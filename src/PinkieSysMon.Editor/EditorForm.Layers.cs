using PinkieSysMon;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalCanvasDefinition = PinkieSysMon.DashboardModel.CanvasDefinition;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private void PopulateTree()
    {
        if (_definition is null)
            return;

        var expandedGroups = _groupTreeNodes
            .Where(pair => pair.Value.IsExpanded)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousLoading = _loading;
        _loading = true;
        _widgetTree.BeginUpdate();
        try
        {
            _widgetTree.Nodes.Clear();
            _widgetTreeNodes.Clear();
            _groupTreeNodes.Clear();

            var canvasNode = new TreeNode("Canvas") { Tag = _definition.Canvas };
            _widgetTree.Nodes.Add(canvasNode);

            var widgetsNode = new TreeNode("Widgets") { Tag = WidgetsRootTag.Instance };
            _widgetTree.Nodes.Add(widgetsNode);

            var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
            AddHierarchyTreeNodes(widgetsNode, hierarchy, null, expandedGroups);
            widgetsNode.Expand();

            var primaryNode = GetTreeNodeByKey(_treePrimaryKey);
            if (primaryNode is not null)
                _widgetTree.SelectedNode = primaryNode;
            else if (_selectedTreeKeys.Count == 0)
                _widgetTree.SelectedNode = canvasNode;
        }
        finally
        {
            _widgetTree.EndUpdate();
            _loading = previousLoading;
        }

        UpdateTreeSelectionVisuals();
    }

    private void AddHierarchyTreeNodes(
        TreeNode parentNode,
        EditorLayerHierarchy hierarchy,
        string? parentGroupId,
        HashSet<string> expandedGroups)
    {
        foreach (var item in hierarchy.GetChildren(parentGroupId))
        {
            if (item.Kind == EditorLayerNodeKind.Widget)
            {
                var widget = hierarchy.GetWidget(item);
                if (widget is null)
                    continue;
                var node = new TreeNode(WidgetCaption(widget)) { Tag = widget };
                parentNode.Nodes.Add(node);
                _widgetTreeNodes[widget] = node;
                continue;
            }

            var group = hierarchy.GetGroup(item);
            if (group is null)
                continue;
            var groupNode = new TreeNode(group.Name) { Tag = group };
            parentNode.Nodes.Add(groupNode);
            _groupTreeNodes[group.Id] = groupNode;
            AddHierarchyTreeNodes(groupNode, hierarchy, group.Id, expandedGroups);
            if (expandedGroups.Contains(group.Id))
                groupNode.Expand();
        }
    }

    private List<CanonicalWidgetDefinition> GetLayerOrder()
    {
        if (_definition is null)
            return [];

        return _definition.Widgets
            .Select((widget, index) => new { widget, index })
            .OrderBy(x => x.widget.Z)
            .ThenBy(x => x.index)
            .Select(x => x.widget)
            .ToList();
    }

    private IReadOnlyList<CanonicalWidgetDefinition> GetOrderedSelectedWidgets()
    {
        if (_definition is null || _selectedWidgets.Count == 0)
            return Array.Empty<CanonicalWidgetDefinition>();

        return _definition.Widgets
            .Where(_selectedWidgets.Contains)
            .ToArray();
    }

    private EditorMultiPropertyView CreateMultiPropertyView(
        IReadOnlyList<CanonicalWidgetDefinition> widgets,
        EditorPropertySection section) =>
        new(widgets.Select(widget => CreatePropertyView(widget, section)).ToArray());

    private EditorPropertyView CreatePropertyView(object target, EditorPropertySection section)
    {
        var dashboardDirectory = string.IsNullOrWhiteSpace(_dashboardPath)
            ? null
            : Path.GetDirectoryName(_dashboardPath);
        if (target is CanonicalWidgetDefinition widget &&
            WidgetTypeContract.Is(widget.Type, WidgetTypeContract.MediaSystem))
        {
            return new EditorPropertyView(
                target,
                _root,
                dashboardDirectory,
                _mediaTelemetry.GetActiveEndpoints(),
                _appConfig.Media.EndpointTypeOverrides,
                candidate => ResolveEffectiveWidgetSize((CanonicalWidgetDefinition)candidate),
                section);
        }

        return new EditorPropertyView(
            target,
            _root,
            dashboardDirectory,
            effectiveSizeAccessor: candidate => ResolveEffectiveWidgetSize((CanonicalWidgetDefinition)candidate),
            section: section);
    }

    private (float Width, float Height)? ResolveEffectiveWidgetSize(CanonicalWidgetDefinition widget)
    {
        if (_preview is null)
            return null;

        try
        {
            var bounds = _preview.GetWidgetLayoutBounds(widget, _lastMetrics);
            return (bounds.Width, bounds.Height);
        }
        catch
        {
            return null;
        }
    }

    private static string WidgetTreeKey(CanonicalWidgetDefinition widget) => $"w:{widget.Id}";
    private static string GroupTreeKey(string groupId) => $"g:{groupId}";

    private string? GetTreeKey(TreeNode? node)
    {
        return node?.Tag switch
        {
            CanonicalWidgetDefinition widget when !string.IsNullOrWhiteSpace(widget.Id) => WidgetTreeKey(widget),
            EditorGroupDefinition group => GroupTreeKey(group.Id),
            _ => null
        };
    }

    private TreeNode? GetTreeNodeByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        if (key.StartsWith("g:", StringComparison.OrdinalIgnoreCase))
            return _groupTreeNodes.GetValueOrDefault(key[2..]);
        if (!key.StartsWith("w:", StringComparison.OrdinalIgnoreCase) || _definition is null)
            return null;
        var id = key[2..];
        var widget = _definition.Widgets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return widget is not null && _widgetTreeNodes.TryGetValue(widget, out var node) ? node : null;
    }

    private LayerTreeViewportState CaptureLayerTreeViewport() =>
        LayerTreeViewportState.Capture(_widgetTree.TopNode, GetTreeKey);

    private void RestoreLayerTreeViewport(LayerTreeViewportState state)
    {
        var anchor = state.Resolve(GetTreeNodeByKey);
        if (anchor is not null)
            _widgetTree.TopNode = anchor;
    }

    private EditorLayerNodeRef? GetLayerNodeRef(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 3)
            return null;
        if (key.StartsWith("g:", StringComparison.OrdinalIgnoreCase))
            return EditorLayerNodeRef.Group(key[2..]);
        if (key.StartsWith("w:", StringComparison.OrdinalIgnoreCase))
            return EditorLayerNodeRef.Widget(key[2..]);
        return null;
    }

    private EditorLayerNodeRef? GetLayerNodeRef(TreeNode? node) => GetLayerNodeRef(GetTreeKey(node));

    private void SelectTreeObject(object? value)
    {
        if (value is CanonicalWidgetDefinition widget)
        {
            SetSingleSelection(widget);
            return;
        }

        if (value is EditorGroupDefinition group)
        {
            if (_groupTreeNodes.TryGetValue(group.Id, out var groupNode))
                SetSingleTreeSelection(groupNode);
            return;
        }

        if (value is CanonicalCanvasDefinition canvas)
        {
            ClearWidgetSelection();
            SetPropertyGridTarget(canvas);
            _workspaceViewport.Invalidate();
            return;
        }

        if (value is WidgetsRootTag)
        {
            ClearWidgetSelection();
            SetPropertyGridTarget(null);
            return;
        }

        ClearWidgetSelection();
        SetPropertyGridTarget(null);
    }

    private void SetSingleSelection(CanonicalWidgetDefinition widget)
    {
        if (string.IsNullOrWhiteSpace(widget.Id))
            return;
        _selectedTreeKeys.Clear();
        var key = WidgetTreeKey(widget);
        _selectedTreeKeys.Add(key);
        _treeSelectionAnchorKey = key;
        _treePrimaryKey = key;
        ApplyTreeSelectionToWidgets();
    }

    private void SetSingleTreeSelection(TreeNode node)
    {
        var key = GetTreeKey(node);
        if (key is null)
            return;
        _selectedTreeKeys.Clear();
        _selectedTreeKeys.Add(key);
        _treeSelectionAnchorKey = key;
        _treePrimaryKey = key;
        ApplyTreeSelectionToWidgets();
    }

    private void ToggleSelection(CanonicalWidgetDefinition widget)
    {
        if (string.IsNullOrWhiteSpace(widget.Id))
            return;
        var key = WidgetTreeKey(widget);
        if (!_selectedTreeKeys.Remove(key))
            _selectedTreeKeys.Add(key);
        _treePrimaryKey = _selectedTreeKeys.Contains(key) ? key : _selectedTreeKeys.LastOrDefault();
        _treeSelectionAnchorKey = key;
        ApplyTreeSelectionToWidgets();
    }

    private void ToggleTreeNodeSelection(TreeNode node)
    {
        var key = GetTreeKey(node);
        if (key is null)
            return;
        if (!_selectedTreeKeys.Remove(key))
            _selectedTreeKeys.Add(key);
        _treePrimaryKey = _selectedTreeKeys.Contains(key) ? key : _selectedTreeKeys.LastOrDefault();
        _treeSelectionAnchorKey = key;
        ApplyTreeSelectionToWidgets();
    }

    private void ClearWidgetSelection()
    {
        _selectedWidget = null;
        _selectedWidgets.Clear();
        _selectedTreeKeys.Clear();
        _treeSelectionAnchorKey = null;
        _treePrimaryKey = null;
        UpdateTreeSelectionVisuals();
        UpdateStatusBar();
        UpdateCommandStates();
    }

    private void SelectTreeRange(TreeNode targetNode, bool additive)
    {
        var targetKey = GetTreeKey(targetNode);
        if (targetKey is null)
            return;

        var visible = GetVisibleSelectableTreeNodes();
        var anchorNode = GetTreeNodeByKey(_treeSelectionAnchorKey);
        if (anchorNode is null || !visible.Contains(anchorNode))
        {
            SetSingleTreeSelection(targetNode);
            return;
        }

        var a = visible.IndexOf(anchorNode);
        var b = visible.IndexOf(targetNode);
        if (a < 0 || b < 0)
        {
            SetSingleTreeSelection(targetNode);
            return;
        }

        if (!additive)
            _selectedTreeKeys.Clear();
        var start = Math.Min(a, b);
        var end = Math.Max(a, b);
        for (var i = start; i <= end; i++)
        {
            var key = GetTreeKey(visible[i]);
            if (key is not null)
                _selectedTreeKeys.Add(key);
        }

        _treePrimaryKey = targetKey;
        ApplyTreeSelectionToWidgets();
    }

    private List<TreeNode> GetVisibleSelectableTreeNodes()
    {
        var result = new List<TreeNode>();
        var widgetsRoot = _widgetTree.Nodes.Cast<TreeNode>().FirstOrDefault(node => node.Tag is WidgetsRootTag);
        if (widgetsRoot is null)
            return result;

        void Visit(TreeNode parent)
        {
            foreach (TreeNode child in parent.Nodes)
            {
                if (GetTreeKey(child) is not null)
                    result.Add(child);
                if (child.IsExpanded)
                    Visit(child);
            }
        }

        Visit(widgetsRoot);
        return result;
    }

    private void ApplyTreeSelectionToWidgets()
    {
        _selectedWidgets.Clear();
        _selectedWidget = null;

        if (_definition is not null)
        {
            var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
            foreach (var key in _selectedTreeKeys)
            {
                var node = GetLayerNodeRef(key);
                if (node is null)
                    continue;
                if (node.Value.Kind == EditorLayerNodeKind.Widget)
                {
                    var widget = hierarchy.GetWidget(node.Value);
                    if (widget is not null)
                        _selectedWidgets.Add(widget);
                }
                else
                {
                    foreach (var widget in hierarchy.GetDescendantWidgets(node.Value.Id))
                        _selectedWidgets.Add(widget);
                }
            }

            var primaryRef = GetLayerNodeRef(_treePrimaryKey);
            if (primaryRef.HasValue && primaryRef.Value.Kind == EditorLayerNodeKind.Widget)
                _selectedWidget = hierarchy.GetWidget(primaryRef.Value);
        }

        SetPropertyGridTarget(_selectedWidget);

        var primaryNode = GetTreeNodeByKey(_treePrimaryKey);
        if (primaryNode is not null && _widgetTree.SelectedNode != primaryNode)
        {
            var previousLoading = _loading;
            _loading = true;
            try { _widgetTree.SelectedNode = primaryNode; }
            finally { _loading = previousLoading; }
        }

        UpdateTreeSelectionVisuals();
        UpdateStatusBar();
        UpdateCommandStates();
        _workspaceViewport.Invalidate();
    }

    private void UpdateTreeSelectionVisuals()
    {
        foreach (var pair in _widgetTreeNodes)
        {
            var explicitSelected = _selectedTreeKeys.Contains(WidgetTreeKey(pair.Key));
            var effectiveSelected = _selectedWidgets.Contains(pair.Key);
            pair.Value.BackColor = explicitSelected
                ? _shellTheme.SelectionBackground
                : effectiveSelected ? _shellTheme.SecondarySelectionBackground : Color.Empty;
            pair.Value.ForeColor = explicitSelected
                ? _shellTheme.SelectionForeground
                : effectiveSelected ? _shellTheme.Foreground : Color.Empty;
        }

        foreach (var pair in _groupTreeNodes)
        {
            var selected = _selectedTreeKeys.Contains(GroupTreeKey(pair.Key));
            pair.Value.BackColor = selected ? _shellTheme.SelectionBackground : Color.Empty;
            pair.Value.ForeColor = selected ? _shellTheme.SelectionForeground : Color.Empty;
        }

        // Programmatic selection changes can occur while another editor surface owns focus.
        // Force the native TreeView to repaint immediately so selection does not appear only
        // after the tree later receives focus.
        if (_widgetTree.IsHandleCreated)
            _widgetTree.Refresh();
    }

    private void DeleteSelectedTreeItems()
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var explicitWidgets = _selectedTreeKeys
            .Select(GetLayerNodeRef)
            .OfType<EditorLayerNodeRef>()
            .Where(node => node.Kind == EditorLayerNodeKind.Widget)
            .Select(node => node.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var explicitGroups = _selectedTreeKeys
            .Select(GetLayerNodeRef)
            .OfType<EditorLayerNodeRef>()
            .Where(node => node.Kind == EditorLayerNodeKind.Group)
            .Select(node => node.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (explicitWidgets.Length == 0 && explicitGroups.Length == 0)
            return;

        var parts = new List<string>();
        if (explicitWidgets.Length > 0)
            parts.Add($"{explicitWidgets.Length} widget(s)");
        if (explicitGroups.Length > 0)
            parts.Add($"{explicitGroups.Length} group(s) (their contents will be promoted)");
        if (MessageBox.Show($"Delete {string.Join(" and ", parts)}?", "PinkieSysMon Editor",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var undoBefore = CaptureSnapshot();
        var previousOrder = GetLayerOrder();
        var widgetsDeleted = false;

        foreach (var widgetId in explicitWidgets)
        {
            var widget = _definition.Widgets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, widgetId, StringComparison.OrdinalIgnoreCase));
            if (widget is null)
                continue;

            _definition.Widgets.Remove(widget);
            _dashboardMetadata.RemoveWidget(widget.Id);
            widgetsDeleted = true;
        }

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        foreach (var groupId in explicitGroups
                     .OrderByDescending(id => _dashboardMetadata.GetDepth(id)))
        {
            if (_dashboardMetadata.GetGroup(groupId) is not null)
                hierarchy.PromoteAndDeleteGroup(groupId);
        }
        hierarchy.SyncGroupDeclarationOrder();
        var flattened = hierarchy.FlattenWidgets();
        var layerOrderChanged = !previousOrder.Where(_definition.Widgets.Contains).SequenceEqual(flattened);
        NormalizeLayerOrder(flattened);

        _selectedTreeKeys.Clear();
        _treePrimaryKey = null;
        _treeSelectionAnchorKey = null;
        _selectedWidgets.Clear();
        _selectedWidget = null;
        SetPropertyGridTarget(null);
        SaveDashboardMetadata();
        PopulateTree();
        PushUndo(undoBefore);

        if (widgetsDeleted || layerOrderChanged)
        {
            MarkDirty();
            RebuildPreviewRenderer();
            RenderPreview(force: true);
        }
    }

    private void DuplicateSelectedWidgets()
    {
        if (_selectedWidgets.Count == 0 || _definition is null)
            return;

        var undoBefore = CaptureSnapshot();
        var source = GetLayerOrder().Where(_selectedWidgets.Contains).ToList();
        var duplicates = new List<CanonicalWidgetDefinition>();
        var maxZ = _definition.Widgets.Count == 0 ? -1 : _definition.Widgets.Max(w => w.Z);

        foreach (var original in source)
        {
            var clone = DashboardJson.CloneWidget(original);
            var id = CreateUniqueId(original.Type);
            clone.Id = id;
            if (!string.IsNullOrWhiteSpace(original.Name))
                clone.Name = original.Name + " Copy";
            clone.X = original.X + 10;
            clone.Y = original.Y + 10;
            clone.Z = ++maxZ;
            clone.Validate(Path.GetDirectoryName(_dashboardPath)!);

            _definition.Widgets.Add(clone);
            duplicates.Add(clone);

            var sourceGroupId = _dashboardMetadata.GetWidgetGroupId(original.Id);
            if (!string.IsNullOrWhiteSpace(sourceGroupId))
                _dashboardMetadata.SetWidgetGroup(id, sourceGroupId);
        }

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        hierarchy.SyncGroupDeclarationOrder();
        NormalizeLayerOrder(hierarchy.FlattenWidgets());

        _selectedWidgets.Clear();
        foreach (var widget in duplicates)
            _selectedWidgets.Add(widget);
        _selectedWidget = duplicates.LastOrDefault();
        _selectedTreeKeys.Clear();
        foreach (var widget in duplicates)
            if (!string.IsNullOrWhiteSpace(widget.Id))
                _selectedTreeKeys.Add(WidgetTreeKey(widget));
        _treePrimaryKey = _selectedWidget is not null ? WidgetTreeKey(_selectedWidget) : null;
        _treeSelectionAnchorKey = _treePrimaryKey;
        SaveDashboardMetadata();
        PopulateTree();
        if (_selectedWidget is not null)
            SetPropertyGridTarget(_selectedWidget);
        UpdateStatusBar();
        PushUndo(undoBefore);
        MarkDirty();
        RebuildPreviewRenderer();
        RenderPreview(force: true);
        SetStatus($"Duplicated {duplicates.Count} widget(s). Ctrl+D repeats.");
    }

    private void DuplicateSelectedGroups()
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var sourceHierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var sourceGroups = GetExplicitSelectedLayerNodes(sourceHierarchy)
            .Where(node => node.Kind == EditorLayerNodeKind.Group)
            .ToArray();
        if (sourceGroups.Length == 0)
            return;

        var undoBefore = CaptureSnapshot();
        var maxZ = _definition.Widgets.Count == 0 ? -1 : _definition.Widgets.Max(widget => widget.Z);
        var duplicatedRootIds = new List<string>(sourceGroups.Length);

        string UniqueCopyName(string sourceName, string? parentGroupId)
        {
            var baseName = sourceName + " Copy";
            if (!_dashboardMetadata.NameExistsAmongSiblings(baseName, parentGroupId))
                return baseName;

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{baseName} {suffix}";
                if (!_dashboardMetadata.NameExistsAmongSiblings(candidate, parentGroupId))
                    return candidate;
            }
        }

        void CloneWidgetIntoGroup(CanonicalWidgetDefinition original, string targetGroupId)
        {
            var clone = DashboardJson.CloneWidget(original);
            var id = CreateUniqueId(original.Type);
            clone.Id = id;
            clone.X = original.X + 10;
            clone.Y = original.Y + 10;
            clone.Z = ++maxZ;
            clone.Validate(Path.GetDirectoryName(_dashboardPath)!);

            _definition.Widgets.Add(clone);
            _dashboardMetadata.SetWidgetGroup(id, targetGroupId);
        }

        string CloneGroupRecursive(string sourceGroupId, string? targetParentGroupId, string targetName)
        {
            if (_dashboardMetadata.GetGroup(sourceGroupId) is null)
                throw new InvalidOperationException($"Unknown editor group '{sourceGroupId}'.");
            var cloneGroup = _dashboardMetadata.CreateGroup(targetName, targetParentGroupId);

            foreach (var child in sourceHierarchy.GetChildren(sourceGroupId))
            {
                if (child.Kind == EditorLayerNodeKind.Widget)
                {
                    var widget = sourceHierarchy.GetWidget(child);
                    if (widget is not null)
                        CloneWidgetIntoGroup(widget, cloneGroup.Id);
                }
                else
                {
                    var childGroup = sourceHierarchy.GetGroup(child);
                    if (childGroup is not null)
                        CloneGroupRecursive(childGroup.Id, cloneGroup.Id, childGroup.Name);
                }
            }

            return cloneGroup.Id;
        }

        foreach (var sourceNode in sourceGroups)
        {
            var sourceGroup = sourceHierarchy.GetGroup(sourceNode);
            if (sourceGroup is null)
                continue;

            var copyName = UniqueCopyName(sourceGroup.Name, sourceGroup.ParentGroupId);
            duplicatedRootIds.Add(CloneGroupRecursive(sourceGroup.Id, sourceGroup.ParentGroupId, copyName));
        }

        if (duplicatedRootIds.Count == 0)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        hierarchy.SyncGroupDeclarationOrder();
        NormalizeLayerOrder(hierarchy.FlattenWidgets());

        _selectedTreeKeys.Clear();
        foreach (var groupId in duplicatedRootIds)
            _selectedTreeKeys.Add(GroupTreeKey(groupId));
        _treePrimaryKey = GroupTreeKey(duplicatedRootIds[^1]);
        _treeSelectionAnchorKey = _treePrimaryKey;
        _selectedWidget = null;
        SetPropertyGridTarget(null);

        SaveDashboardMetadata();
        PopulateTree();
        foreach (var groupId in duplicatedRootIds)
        {
            if (_groupTreeNodes.TryGetValue(groupId, out var groupNode))
                groupNode.Expand();
        }
        ApplyTreeSelectionToWidgets();
        PushUndo(undoBefore);
        MarkDirty();
        RebuildPreviewRenderer();
        RenderPreview(force: true);
        SetStatus($"Duplicated {duplicatedRootIds.Count} group(s). Ctrl+D repeats.");
    }

    private IReadOnlyList<EditorLayerNodeRef> GetExplicitSelectedLayerNodes(EditorLayerHierarchy hierarchy)
    {
        var nodes = _selectedTreeKeys
            .Select(GetLayerNodeRef)
            .OfType<EditorLayerNodeRef>()
            .ToArray();
        return hierarchy.CanonicalizeSelection(nodes);
    }

    private readonly record struct AlignmentBounds(float Left, float Top, float Right, float Bottom)
    {
        public float MidX => (Left + Right) / 2f;
        public float MidY => (Top + Bottom) / 2f;
    }

    private bool CanAlignSelection()
    {
        if (_definition is null || _preview is null || _selectedTreeKeys.Count < 2)
            return false;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count < 2 || !ResolveAlignmentAnchor(hierarchy, selected).HasValue)
            return false;

        foreach (var node in selected)
        {
            if (node.Kind == EditorLayerNodeKind.Widget)
            {
                if (hierarchy.GetWidget(node) is null)
                    return false;
            }
            else if (hierarchy.GetDescendantWidgets(node.Id).Count == 0)
            {
                return false;
            }
        }

        return true;
    }

    private EditorLayerNodeRef? ResolveAlignmentAnchor(
        EditorLayerHierarchy hierarchy,
        IReadOnlyList<EditorLayerNodeRef> selected)
    {
        var primary = GetLayerNodeRef(_treePrimaryKey);
        if (!primary.HasValue)
            return null;

        foreach (var node in selected)
        {
            if (node.Key.Equals(primary.Value.Key, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        var parentGroupId = hierarchy.GetParentGroupId(primary.Value);
        while (!string.IsNullOrWhiteSpace(parentGroupId))
        {
            var ancestor = EditorLayerNodeRef.Group(parentGroupId);
            foreach (var node in selected)
            {
                if (node.Key.Equals(ancestor.Key, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            parentGroupId = hierarchy.GetParentGroupId(ancestor);
        }

        return null;
    }

    private IReadOnlyList<CanonicalWidgetDefinition> GetAlignmentNodeWidgets(
        EditorLayerHierarchy hierarchy,
        EditorLayerNodeRef node)
    {
        if (node.Kind == EditorLayerNodeKind.Group)
            return hierarchy.GetDescendantWidgets(node.Id);

        var widget = hierarchy.GetWidget(node);
        return widget is null ? [] : [widget];
    }

    private bool TryGetAlignmentBounds(
        EditorLayerHierarchy hierarchy,
        EditorLayerNodeRef node,
        out AlignmentBounds bounds)
    {
        bounds = default;
        if (_preview is null)
            return false;

        var widgets = GetAlignmentNodeWidgets(hierarchy, node);
        if (widgets.Count == 0)
            return false;

        var hasPoint = false;
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;

        try
        {
            foreach (var widget in widgets)
            {
                var outline = _preview.GetWidgetOutline(widget, _lastMetrics);
                foreach (var point in outline)
                {
                    if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                        return false;
                    hasPoint = true;
                    left = Math.Min(left, point.X);
                    top = Math.Min(top, point.Y);
                    right = Math.Max(right, point.X);
                    bottom = Math.Max(bottom, point.Y);
                }
            }
        }
        catch
        {
            return false;
        }

        if (!hasPoint)
            return false;

        bounds = new AlignmentBounds(left, top, right, bottom);
        return true;
    }

    private static (float Dx, float Dy) GetAlignmentOffset(
        AlignmentBounds source,
        AlignmentBounds anchor,
        AlignmentCommand alignment)
    {
        return alignment switch
        {
            AlignmentCommand.Left => (anchor.Left - source.Left, 0f),
            AlignmentCommand.HorizontalCenter => (anchor.MidX - source.MidX, 0f),
            AlignmentCommand.Right => (anchor.Right - source.Right, 0f),
            AlignmentCommand.Top => (0f, anchor.Top - source.Top),
            AlignmentCommand.VerticalCenter => (0f, anchor.MidY - source.MidY),
            AlignmentCommand.Bottom => (0f, anchor.Bottom - source.Bottom),
            _ => (0f, 0f)
        };
    }

    private void AlignSelectedLayerNodes(AlignmentCommand alignment)
    {
        if (_definition is null || _preview is null)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        var anchor = ResolveAlignmentAnchor(hierarchy, selected);
        if (selected.Count < 2 || !anchor.HasValue)
            return;

        var boundsByKey = new Dictionary<string, AlignmentBounds>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in selected)
        {
            if (!TryGetAlignmentBounds(hierarchy, node, out var nodeBounds))
            {
                SetStatus("Could not determine alignment bounds for every selected object.");
                return;
            }
            boundsByKey[node.Key] = nodeBounds;
        }

        var anchorBounds = boundsByKey[anchor.Value.Key];
        var movements = new List<(EditorLayerNodeRef Node, float Dx, float Dy)>();
        foreach (var node in selected)
        {
            if (node.Key.Equals(anchor.Value.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            var offset = GetAlignmentOffset(boundsByKey[node.Key], anchorBounds, alignment);
            if (Math.Abs(offset.Dx) > 0.0001f || Math.Abs(offset.Dy) > 0.0001f)
                movements.Add((node, offset.Dx, offset.Dy));
        }

        if (movements.Count == 0)
        {
            SetStatus("Selected objects are already aligned to the last selected object.");
            return;
        }

        var undoBefore = CaptureSnapshot();
        foreach (var movement in movements)
        {
            foreach (var widget in GetAlignmentNodeWidgets(hierarchy, movement.Node))
            {
                widget.X += movement.Dx;
                widget.Y += movement.Dy;
                SyncGeometryToJson(widget);
            }
        }

        _propertyGrid.Refresh();
        PushUndo(undoBefore);
        MarkDirty();
        RenderPreview(force: true, immediate: true);

        var label = alignment switch
        {
            AlignmentCommand.Left => "left edges",
            AlignmentCommand.HorizontalCenter => "horizontal centers",
            AlignmentCommand.Right => "right edges",
            AlignmentCommand.Top => "top edges",
            AlignmentCommand.VerticalCenter => "vertical centers",
            AlignmentCommand.Bottom => "bottom edges",
            _ => "objects"
        };
        SetStatus($"Aligned {movements.Count} object(s) by {label} to the last selected object.");
    }

    private void GroupSelectedLayerNodes()
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0)
            return;

        var parentIds = selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentIds.Length != 1)
        {
            MessageBox.Show("Selected nodes must have the same parent before they can be grouped.",
                "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = PromptForText("Group Selected", "Group name:", string.Empty);
        if (name is null)
            return;
        name = name.Trim();
        if (name.Length == 0)
            return;
        if (_dashboardMetadata.NameExistsAmongSiblings(name, parentIds[0]))
        {
            MessageBox.Show($"A group named '{name}' already exists at this level.",
                "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var undoBefore = CaptureSnapshot();
        var previousOrder = GetLayerOrder();
        var group = _dashboardMetadata.CreateGroup(name, parentIds[0]);
        hierarchy.WrapNodesInGroup(selected, group);
        hierarchy.SyncGroupDeclarationOrder();
        var flattened = hierarchy.FlattenWidgets();
        var layerOrderChanged = !previousOrder.SequenceEqual(flattened);
        NormalizeLayerOrder(flattened);

        _selectedTreeKeys.Clear();
        _selectedTreeKeys.Add(GroupTreeKey(group.Id));
        _treePrimaryKey = GroupTreeKey(group.Id);
        _treeSelectionAnchorKey = _treePrimaryKey;
        SaveDashboardMetadata();
        PopulateTree();
        if (_groupTreeNodes.TryGetValue(group.Id, out var createdGroupNode))
            createdGroupNode.Expand();
        ApplyTreeSelectionToWidgets();
        PushUndo(undoBefore);
        if (layerOrderChanged)
        {
            MarkDirty();
            RebuildPreviewRenderer();
            RenderPreview(force: true);
        }
        SetStatus($"Created group '{name}' containing {selected.Count} layer node(s).");
    }

    private void UngroupSelectedLayerNodes()
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0)
            return;

        var undoBefore = CaptureSnapshot();
        var previousOrder = GetLayerOrder();
        var changed = false;

        foreach (var node in selected.Where(node => node.Kind == EditorLayerNodeKind.Group).ToArray())
        {
            hierarchy.PromoteAndDeleteGroup(node.Id);
            changed = true;
        }

        var widgetGroups = selected
            .Where(node => node.Kind == EditorLayerNodeKind.Widget)
            .GroupBy(node => hierarchy.GetParentGroupId(node) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var widgetGroup in widgetGroups)
        {
            var parentId = string.IsNullOrWhiteSpace(widgetGroup.Key) ? null : widgetGroup.Key;
            if (parentId is null)
                continue;

            var parentGroup = _dashboardMetadata.GetGroup(parentId);
            if (parentGroup is null)
                continue;
            var grandParentId = parentGroup.ParentGroupId;
            var grandParentChildren = hierarchy.GetChildren(grandParentId).ToList();
            var parentIndex = grandParentChildren.IndexOf(EditorLayerNodeRef.Group(parentId));
            var moving = widgetGroup.ToArray();
            hierarchy.MoveNodes(moving, grandParentId, parentIndex < 0 ? grandParentChildren.Count : parentIndex + 1);
            changed = true;
        }

        if (!changed)
            return;

        hierarchy.SyncGroupDeclarationOrder();
        var flattened = hierarchy.FlattenWidgets();
        var layerOrderChanged = !previousOrder.SequenceEqual(flattened);
        NormalizeLayerOrder(flattened);
        SaveDashboardMetadata();
        _selectedTreeKeys.Clear();
        _treePrimaryKey = null;
        _treeSelectionAnchorKey = null;
        ClearWidgetSelection();
        PopulateTree();
        PushUndo(undoBefore);
        if (layerOrderChanged)
        {
            MarkDirty();
            RebuildPreviewRenderer();
            RenderPreview(force: true);
        }
        SetStatus("Ungrouped selected layer node(s); child order was preserved.");
    }

    private void TreeBeforeSelect(object? sender, TreeViewCancelEventArgs e)
    {
        if (!_loading && (ModifierKeys & (Keys.Control | Keys.Shift)) != 0 && GetTreeKey(e.Node) is not null)
            e.Cancel = true;
    }

    private void TreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        var clickedNode = e.Node;
        if (clickedNode is null)
            return;

        var key = GetTreeKey(clickedNode);
        if (e.Button == MouseButtons.Right)
        {
            if (key is not null && !_selectedTreeKeys.Contains(key))
                SetSingleTreeSelection(clickedNode);
            var previousLoading = _loading;
            _loading = true;
            try { _widgetTree.SelectedNode = clickedNode; }
            finally { _loading = previousLoading; }
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        if (key is null)
        {
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                SelectTreeObject(clickedNode.Tag);
            return;
        }

        var modifiers = ModifierKeys;
        var ctrl = (modifiers & Keys.Control) != 0;
        var shift = (modifiers & Keys.Shift) != 0;

        if (shift)
        {
            SelectTreeRange(clickedNode, additive: ctrl);
            return;
        }

        if (ctrl)
        {
            ToggleTreeNodeSelection(clickedNode);
            return;
        }

        SetSingleTreeSelection(clickedNode);
    }

    private void RenameSelectedTreeItem()
    {
        var primary = GetLayerNodeRef(_treePrimaryKey);
        if (primary.HasValue && primary.Value.Kind == EditorLayerNodeKind.Group)
        {
            var group = _dashboardMetadata.GetGroup(primary.Value.Id);
            if (group is null)
                return;

            var result = PromptForText("Rename Group", "Group name:", group.Name);
            if (result is null)
                return;
            var name = result.Trim();
            if (name.Length == 0 || string.Equals(name, group.Name, StringComparison.Ordinal))
                return;
            if (_dashboardMetadata.NameExistsAmongSiblings(name, group.ParentGroupId, group.Id))
            {
                MessageBox.Show($"A group named '{name}' already exists at this level.",
                    "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var undoBefore = CaptureSnapshot();
            group.Name = name;
            SaveDashboardMetadata();
            PopulateTree();
            PushUndo(undoBefore);
            SetStatus($"Renamed group to '{name}'.");
            return;
        }

        RenameSelectedWidget();
    }

    private void RenameSelectedWidget()
    {
        if (_selectedWidget is null)
            return;

        var current = _selectedWidget.Name ?? _selectedWidget.Id ?? _selectedWidget.Type;
        var result = PromptForText("Rename Widget", "Widget name:", current);
        if (result is null)
            return;

        var newName = result.Trim();
        var normalized = string.IsNullOrWhiteSpace(newName) ? null : newName;
        if (string.Equals(normalized, _selectedWidget.Name, StringComparison.Ordinal))
            return;

        var undoBefore = CaptureSnapshot();
        _selectedWidget.Name = normalized;
        if (_widgetTreeNodes.TryGetValue(_selectedWidget, out var treeNode))
            treeNode.Text = WidgetCaption(_selectedWidget);
        _propertyGrid.Refresh();
        PushUndo(undoBefore);
        MarkDirty();
    }

    private string? PromptForText(string title, string label, string initialValue)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(430, 125)
        };
        var prompt = new Label { Text = label, AutoSize = true, Left = 12, Top = 14 };
        var box = new TextBox { Left = 12, Top = 36, Width = 405, Text = initialValue };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 261, Top = 78, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 342, Top = 78, Width = 75 };
        dialog.Controls.AddRange([prompt, box, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { box.SelectAll(); box.Focus(); };
        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    private void TreeItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node || GetTreeKey(node) is null)
            return;

        var key = GetTreeKey(node)!;
        if (!_selectedTreeKeys.Contains(key))
            SetSingleTreeSelection(node);

        _dragTreeNode = node;
        try
        {
            _widgetTree.DoDragDrop(node, DragDropEffects.Move);
        }
        finally
        {
            _dragTreeNode = null;
            ClearTreeDropMarker();
        }
    }

    private void TreeDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void TreeDragOver(object? sender, DragEventArgs e)
    {
        if (_dragTreeNode is null || _definition is null)
        {
            e.Effect = DragDropEffects.None;
            ClearTreeDropMarker();
            return;
        }

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var moving = GetExplicitSelectedLayerNodes(hierarchy);
        if (moving.Count == 0)
        {
            e.Effect = DragDropEffects.None;
            ClearTreeDropMarker();
            return;
        }

        var client = _widgetTree.PointToClient(new Point(e.X, e.Y));
        if (client.Y < 24 && _widgetTree.TopNode?.PrevVisibleNode is TreeNode previous)
            previous.EnsureVisible();
        else if (client.Y > _widgetTree.ClientSize.Height - 24)
        {
            var edgeNode = _widgetTree.GetNodeAt(new Point(Math.Max(1, client.X), Math.Max(1, _widgetTree.ClientSize.Height - 6)));
            edgeNode?.NextVisibleNode?.EnsureVisible();
        }

        var target = _widgetTree.GetNodeAt(client);
        if (target is null)
        {
            e.Effect = DragDropEffects.None;
            ClearTreeDropMarker();
            return;
        }

        TreeDropTarget? drop = null;
        if (target.Tag is WidgetsRootTag)
        {
            drop = new TreeDropTarget(null, hierarchy.GetChildren(null).Count, IntoGroup: true);
            _widgetTree.SetInsertionMarker(null);
            _widgetTree.SetDropTarget(target);
        }
        else if (target.Tag is EditorGroupDefinition targetGroup)
        {
            var quarter = Math.Max(3, target.Bounds.Height / 4);
            if (client.Y <= target.Bounds.Top + quarter)
            {
                var parentId = targetGroup.ParentGroupId;
                var siblings = hierarchy.GetChildren(parentId).ToList();
                var index = siblings.IndexOf(EditorLayerNodeRef.Group(targetGroup.Id));
                drop = new TreeDropTarget(parentId, Math.Max(0, index), IntoGroup: false);
                _widgetTree.SetDropTarget(null);
                _widgetTree.SetInsertionMarker(target.Bounds.Top, target.Bounds.Left);
            }
            else if (client.Y >= target.Bounds.Bottom - quarter)
            {
                var parentId = targetGroup.ParentGroupId;
                var siblings = hierarchy.GetChildren(parentId).ToList();
                var index = siblings.IndexOf(EditorLayerNodeRef.Group(targetGroup.Id));
                drop = new TreeDropTarget(parentId, index < 0 ? siblings.Count : index + 1, IntoGroup: false);
                _widgetTree.SetDropTarget(null);
                _widgetTree.SetInsertionMarker(target.Bounds.Bottom, target.Bounds.Left);
            }
            else
            {
                drop = new TreeDropTarget(targetGroup.Id, hierarchy.GetChildren(targetGroup.Id).Count, IntoGroup: true);
                _widgetTree.SetInsertionMarker(null);
                _widgetTree.SetDropTarget(target);
            }
        }
        else if (target.Tag is CanonicalWidgetDefinition targetWidget && !string.IsNullOrWhiteSpace(targetWidget.Id))
        {
            var targetRef = EditorLayerNodeRef.Widget(targetWidget.Id);
            var parentId = hierarchy.GetParentGroupId(targetRef);
            var siblings = hierarchy.GetChildren(parentId).ToList();
            var index = siblings.IndexOf(targetRef);
            var after = client.Y > target.Bounds.Top + target.Bounds.Height / 2;
            drop = new TreeDropTarget(parentId, Math.Max(0, index) + (after ? 1 : 0), IntoGroup: false);
            _widgetTree.SetDropTarget(null);
            _widgetTree.SetInsertionMarker(after ? target.Bounds.Bottom : target.Bounds.Top, target.Bounds.Left);
        }

        if (drop is null || !hierarchy.CanMoveNodes(moving, drop.ParentGroupId, out _))
        {
            e.Effect = DragDropEffects.None;
            ClearTreeDropMarker();
            return;
        }

        _treeDropTarget = drop;
        e.Effect = DragDropEffects.Move;
    }

    private void TreeDragDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (_definition is null || _treeDropTarget is null)
                return;

            var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
            var moving = GetExplicitSelectedLayerNodes(hierarchy);
            if (moving.Count == 0)
                return;

            var destinationParentId = _treeDropTarget.ParentGroupId;
            var destinationIndex = _treeDropTarget.Index;
            var destinationBefore = hierarchy.GetChildren(destinationParentId).ToList();
            var removedBefore = moving.Count(node =>
            {
                if (!string.Equals(hierarchy.GetParentGroupId(node) ?? string.Empty, destinationParentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    return false;
                var index = destinationBefore.IndexOf(node);
                return index >= 0 && index < destinationIndex;
            });
            destinationIndex = Math.Max(0, destinationIndex - removedBefore);

            var viewportState = CaptureLayerTreeViewport();
            var undoBefore = CaptureSnapshot();
            var previousOrder = GetLayerOrder();
            hierarchy.MoveNodes(moving, destinationParentId, destinationIndex);
            hierarchy.SyncGroupDeclarationOrder();
            var flattened = hierarchy.FlattenWidgets();
            var layerOrderChanged = !previousOrder.SequenceEqual(flattened);
            NormalizeLayerOrder(flattened);
            SaveDashboardMetadata();
            PopulateTree();
            ApplyTreeSelectionToWidgets();
            RestoreLayerTreeViewport(viewportState);
            PushUndo(undoBefore);
            if (layerOrderChanged)
            {
                MarkDirty();
                RebuildPreviewRenderer();
                RenderPreview(force: true);
            }

            var destinationName = string.IsNullOrWhiteSpace(destinationParentId)
                ? "Widgets root"
                : _dashboardMetadata.GetGroup(destinationParentId)?.Name ?? destinationParentId;
            SetStatus($"Moved {moving.Count} layer node(s) to {destinationName}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            ClearTreeDropMarker();
        }
    }

    private void ClearTreeDropMarker()
    {
        _treeDropTarget = null;
        _widgetTree.SetInsertionMarker(null);
        _widgetTree.SetDropTarget(null);
    }

    private void MoveSelectionInLayerOrder(int direction)
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0)
            return;

        var parentIds = selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentIds.Length != 1)
        {
            SetStatus("Move Up/Down requires selected layer nodes to have the same parent.");
            return;
        }

        var siblings = hierarchy.GetChildren(parentIds[0]).ToList();
        var selectedKeys = selected.Select(node => node.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var original = siblings.ToArray();

        if (direction < 0)
        {
            for (var i = 1; i < siblings.Count; i++)
            {
                if (!selectedKeys.Contains(siblings[i].Key) || selectedKeys.Contains(siblings[i - 1].Key))
                    continue;
                (siblings[i - 1], siblings[i]) = (siblings[i], siblings[i - 1]);
            }
        }
        else
        {
            for (var i = siblings.Count - 2; i >= 0; i--)
            {
                if (!selectedKeys.Contains(siblings[i].Key) || selectedKeys.Contains(siblings[i + 1].Key))
                    continue;
                (siblings[i + 1], siblings[i]) = (siblings[i], siblings[i + 1]);
            }
        }

        if (original.SequenceEqual(siblings))
            return;

        var undoBefore = CaptureSnapshot();
        var previousOrder = GetLayerOrder();
        hierarchy.ReorderChildren(parentIds[0], siblings);
        hierarchy.SyncGroupDeclarationOrder();
        var flattened = hierarchy.FlattenWidgets();
        var layerOrderChanged = !previousOrder.SequenceEqual(flattened);
        NormalizeLayerOrder(flattened);
        SaveDashboardMetadata();
        PopulateTree();
        ApplyTreeSelectionToWidgets();
        PushUndo(undoBefore);
        if (layerOrderChanged)
        {
            MarkDirty();
            RebuildPreviewRenderer();
            RenderPreview(force: true);
        }
    }

    private void MoveSelectionToLayerEdge(bool front)
    {
        if (_definition is null || _selectedTreeKeys.Count == 0)
            return;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0)
            return;

        var parentIds = selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentIds.Length != 1)
        {
            SetStatus("Bring to Front/Send to Back requires selected layer nodes to have the same parent.");
            return;
        }

        var siblings = hierarchy.GetChildren(parentIds[0]).ToList();
        var selectedKeys = selected.Select(node => node.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moving = siblings.Where(node => selectedKeys.Contains(node.Key)).ToList();
        if (moving.Count == 0)
            return;
        siblings.RemoveAll(node => selectedKeys.Contains(node.Key));
        if (front)
            siblings.AddRange(moving);
        else
            siblings.InsertRange(0, moving);

        var undoBefore = CaptureSnapshot();
        var previousOrder = GetLayerOrder();
        hierarchy.ReorderChildren(parentIds[0], siblings);
        hierarchy.SyncGroupDeclarationOrder();
        var flattened = hierarchy.FlattenWidgets();
        var layerOrderChanged = !previousOrder.SequenceEqual(flattened);
        NormalizeLayerOrder(flattened);
        SaveDashboardMetadata();
        PopulateTree();
        ApplyTreeSelectionToWidgets();
        PushUndo(undoBefore);
        if (layerOrderChanged)
        {
            MarkDirty();
            RebuildPreviewRenderer();
            RenderPreview(force: true);
        }
    }

    private void NormalizeLayerOrder(IReadOnlyList<CanonicalWidgetDefinition> order)
    {
        if (_definition is null)
            return;

        _definition.Widgets.Clear();
        _definition.Widgets.AddRange(order);
        for (var i = 0; i < order.Count; i++)
            order[i].Z = i;
    }

}
