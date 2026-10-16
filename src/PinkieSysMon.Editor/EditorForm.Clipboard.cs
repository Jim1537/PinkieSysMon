using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

// The clipboard wire format is independent of Editor process lifetime.
internal sealed class WidgetTransferPackage
{
    public int Version { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public List<WidgetTransferGroup> Groups { get; set; } = [];
    public List<string> RootGroupIds { get; set; } = [];
    public Dictionary<string, string> WidgetGroupIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class WidgetTransferGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentGroupId { get; set; }
}

internal static class EditorWidgetClipboard
{
    public const string Format = "PinkieSysMon.Widgets.v1";
    private const int MaximumTransferBytes = 4 * 1024 * 1024;

    public static string Serialize(WidgetTransferPackage package) => JsonSerializer.Serialize(package);

    public static WidgetTransferPackage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumTransferBytes)
            throw new InvalidDataException("Widget clipboard payload is empty or too large.");

        var package = JsonSerializer.Deserialize<WidgetTransferPackage>(json)
            ?? throw new InvalidDataException("Widget clipboard payload is empty.");
        if (package.Version != 1)
            throw new InvalidDataException($"Unsupported widget clipboard version {package.Version}.");
        if (string.IsNullOrWhiteSpace(package.SnapshotJson) || package.Groups is null ||
            package.RootGroupIds is null || package.WidgetGroupIds is null)
            throw new InvalidDataException("Widget clipboard payload is incomplete.");
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in package.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id) ||
                string.IsNullOrWhiteSpace(group.Name))
                throw new InvalidDataException("Clipboard group definitions are invalid.");
        }
        if (package.RootGroupIds.Any(id => !groupIds.Contains(id)) ||
            package.WidgetGroupIds.Values.Any(id => !groupIds.Contains(id)))
            throw new InvalidDataException("Clipboard group relationships are invalid.");
        return package;
    }

    public static void Write(WidgetTransferPackage package)
    {
        var bytes = Encoding.UTF8.GetBytes(Serialize(package));
        if (bytes.Length > MaximumTransferBytes)
            throw new InvalidDataException("Selected widgets exceed the clipboard transfer size limit.");

        var data = new DataObject();
        data.SetDataAsJson(Format, Serialize(package));
        // Flush clipboard ownership so a second Editor can paste after this process exits.
        Clipboard.SetDataObject(data, copy: true);
    }

    public static WidgetTransferPackage? Read()
    {
        if (!Clipboard.ContainsData(Format))
            return null;
        if (!Clipboard.TryGetData<string>(Format, out var json) || json is null)
            throw new InvalidDataException("Windows clipboard data could not be deserialized.");
        return Parse(json);
    }
}

internal sealed partial class EditorForm
{
    private sealed record ImageSourceSlot(string Source, Action<string?> Replace);

    private void ExecuteCopyWidgetsCommand()
    {
        if (!CanCopyWidgetsCommand() || _definition is null)
            return;
        try
        {
            var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
            var selected = GetExplicitSelectedLayerNodes(hierarchy);
            var kind = EditorLayerSelection.GetHomogeneousKind(selected);
            if (kind is null)
                return;

            var groups = new List<WidgetTransferGroup>();
            var widgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<string>();

            void CollectGroup(string groupId)
            {
                if (!includedGroups.Add(groupId))
                    return;
                var group = _dashboardMetadata.GetGroup(groupId)
                    ?? throw new InvalidDataException($"Selected group '{groupId}' was not found.");
                groups.Add(new WidgetTransferGroup
                {
                    Id = group.Id, Name = group.Name, ParentGroupId = group.ParentGroupId
                });
                foreach (var child in hierarchy.GetChildren(groupId))
                {
                    if (child.Kind == EditorLayerNodeKind.Group)
                        CollectGroup(child.Id);
                    else
                        widgetIds.Add(child.Id);
                }
            }

            foreach (var node in selected)
            {
                if (node.Kind == EditorLayerNodeKind.Group)
                {
                    roots.Add(node.Id);
                    CollectGroup(node.Id);
                }
                else
                {
                    widgetIds.Add(node.Id);
                }
            }

            var widgets = GetLayerOrder()
                .Where(widget => !string.IsNullOrWhiteSpace(widget.Id) && widgetIds.Contains(widget.Id))
                .Select(DashboardJson.CloneWidget)
                .ToList();
            if (widgets.Count == 0 && groups.Count == 0)
                throw new InvalidOperationException("The selection contains no transferable widgets or groups.");

            var transfer = new C.DashboardDefinition
            {
                SchemaVersion = _definition.SchemaVersion,
                // Transfer widgets only; unrelated canvas artwork must not enter the clipboard.
                Canvas = new C.CanvasDefinition(),
                Widgets = widgets,
                BaseDirectory = _definition.BaseDirectory
            };
            var package = new WidgetTransferPackage
            {
                Version = 1,
                SourceDirectory = Path.GetDirectoryName(_dashboardPath)!,
                SnapshotJson = DashboardJson.SerializeEditorSnapshot(transfer),
                Groups = groups,
                RootGroupIds = roots
            };
            foreach (var widget in widgets)
            {
                var widgetId = widget.Id;
                var groupId = _dashboardMetadata.GetWidgetGroupId(widgetId);
                if (!string.IsNullOrWhiteSpace(widgetId) &&
                    groupId is not null && includedGroups.Contains(groupId))
                {
                    package.WidgetGroupIds[widgetId] = groupId;
                }
            }

            EditorWidgetClipboard.Write(package);
            SetStatus($"Copied {widgets.Count} widget(s) to the Windows clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy widgets", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateCommandStates();
    }

    private void ExecutePasteWidgetsCommand()
    {
        if (!CanPasteWidgetsCommand() || _definition is null)
            return;
        try
        {
            var package = EditorWidgetClipboard.Read();
            if (package is null)
            {
                SetStatus("The Windows clipboard contains no PinkieSysMon widgets.");
                return;
            }
            PasteTransferredWidgets(package);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Paste widgets", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateCommandStates();
    }

    private void PasteTransferredWidgets(WidgetTransferPackage package)
    {
        var undoBefore = CaptureSnapshot();
        var staged = DashboardJson.ParseEditorSnapshot(
            DashboardJson.SerializeEditorSnapshot(_definition!), _dashboardPath);
        var metadata = DashboardEditorMetadata.FromJson(_dashboardMetadata.ToCompactJson());
        var incoming = DashboardJson.ParseEditorSnapshot(package.SnapshotJson, _dashboardPath);
        if (incoming.Widgets.Count == 0 && package.Groups.Count == 0)
            throw new InvalidDataException("The clipboard does not contain widgets or groups.");

        var clones = incoming.Widgets.Select(DashboardJson.CloneWidget).ToList();
        var newlyCreatedAssets = new List<string>();
        var committed = false;
        try
        {
            if (!ResolveClipboardImages(clones, package.SourceDirectory,
                Path.GetDirectoryName(_dashboardPath)!, newlyCreatedAssets))
                return;

            var knownIds = new HashSet<string>(
                staged.Widgets.Select(widget => widget.Id
                    ?? throw new InvalidDataException("Destination widget ID is missing.")),
                StringComparer.OrdinalIgnoreCase);
            var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nextZ = staged.Widgets.Count == 0 ? -1 : staged.Widgets.Max(widget => widget.Z);
            foreach (var widget in clones)
            {
                if (string.IsNullOrWhiteSpace(widget.Id) || !idMap.TryAdd(widget.Id, string.Empty))
                    throw new InvalidDataException("Clipboard widget IDs must be present and distinct.");
                var suffix = 1;
                string id;
                do { id = $"{widget.Type}-{suffix++}"; } while (!knownIds.Add(id));
                idMap[widget.Id] = id;
                widget.Id = id;
                widget.X += 16;
                widget.Y += 16;
                widget.Z = ++nextZ;
                staged.Widgets.Add(widget);
            }

            var newGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in package.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name) ||
                    newGroups.ContainsKey(group.Id))
                    throw new InvalidDataException("Clipboard group definitions are invalid.");
                var parent = group.ParentGroupId is not null &&
                             newGroups.TryGetValue(group.ParentGroupId, out var mappedParent)
                    ? mappedParent : null;
                var name = group.Name;
                if (metadata.NameExistsAmongSiblings(name, parent))
                {
                    var suffix = 2;
                    while (metadata.NameExistsAmongSiblings($"{group.Name} {suffix}", parent))
                        suffix++;
                    name = $"{group.Name} {suffix}";
                }
                var targetGroup = metadata.CreateGroup(name, parent);
                newGroups.Add(group.Id, targetGroup.Id);
            }

            foreach (var pair in package.WidgetGroupIds)
            {
                if (idMap.TryGetValue(pair.Key, out var widgetId) &&
                    newGroups.TryGetValue(pair.Value, out var groupId))
                    metadata.SetWidgetGroup(widgetId, groupId);
            }

            var hierarchy = new EditorLayerHierarchy(staged, metadata);
            hierarchy.SyncGroupDeclarationOrder();
            NormalizePreparedLayerOrder(staged, hierarchy.FlattenWidgets());
            staged.Validate();
            ReplacePreviewRenderer(staged);
            _definition = staged;
            _dashboardMetadata = metadata;
            committed = true;

            _selectedTreeKeys.Clear();
            foreach (var rootId in package.RootGroupIds)
                if (newGroups.TryGetValue(rootId, out var pastedGroup))
                    _selectedTreeKeys.Add(GroupTreeKey(pastedGroup));
            if (_selectedTreeKeys.Count == 0)
                foreach (var widget in clones)
                    _selectedTreeKeys.Add(WidgetTreeKey(widget));
            _treePrimaryKey = _selectedTreeKeys.LastOrDefault();
            _treeSelectionAnchorKey = _treePrimaryKey;
            _selectedWidget = null;
            _selectedWidgets.Clear();
            PopulateTree();
            ApplyTreeSelectionToWidgets();
            SaveDashboardMetadata();
            PushUndo(undoBefore);
            MarkDirty();
            UpdateStatusBar();
            RefreshPreviewTelemetry();
            RenderPreview(force: true);
            SetStatus($"Pasted {clones.Count} widget(s).");
        }
        finally
        {
            if (!committed)
            {
                foreach (var path in newlyCreatedAssets)
                {
                    try { File.Delete(path); }
                    catch (IOException) { /* Keep any file whose removal cannot be verified. */ }
                }
            }
        }
    }

    private bool ResolveClipboardImages(
        IEnumerable<C.WidgetDefinition> widgets, string sourceRoot,
        string destinationRoot, List<string> createdFiles)
    {
        var sources = new List<ImageSourceSlot>();
        foreach (var widget in widgets)
        {
            switch (widget)
            {
                case C.ImageWidgetDefinition image
                    when C.ImageAssetSourceType.IsFile(image.Asset.SourceType) &&
                         !string.IsNullOrWhiteSpace(image.Asset.Source):
                    sources.Add(new ImageSourceSlot(image.Asset.Source!,
                        value => image.Asset.Source = value));
                    break;
                case C.BarWidgetDefinition bar when !string.IsNullOrWhiteSpace(bar.Image.Source):
                    sources.Add(new ImageSourceSlot(bar.Image.Source!,
                        value => bar.Image.Source = value));
                    break;
                case C.StateVisualWidgetDefinition stateWidget:
                    foreach (var profile in stateWidget.Profiles.Values)
                    {
                        if (C.ImageAssetSourceType.IsFile(profile.Asset.SourceType) &&
                            !string.IsNullOrWhiteSpace(profile.Asset.Source))
                            sources.Add(new ImageSourceSlot(profile.Asset.Source!,
                                value => profile.Asset.Source = value));
                    }
                    break;
            }
        }

        if (sources.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new InvalidDataException("Clipboard image references have no source dashboard directory.");
        if (string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(destinationRoot),
            StringComparison.OrdinalIgnoreCase))
            return true;

        var choice = MessageBox.Show(
            $"Paste references {sources.Select(source => source.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()} image file(s).\n\nImport them into the destination dashboard?\n\nNo keeps the widgets but clears those image sources.",
            "Import widget images", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel)
            return false;
        if (choice == DialogResult.No)
        {
            foreach (var source in sources)
                source.Replace(null);
            return true;
        }

        var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var imagesDirectory = Path.Combine(destinationRoot, "images");
        foreach (var slot in sources)
        {
            if (imported.TryGetValue(slot.Source, out var existing))
            {
                slot.Replace(existing);
                continue;
            }

            var originalPath = AssetPathResolver.ResolveExistingFile(sourceRoot, slot.Source, "Clipboard image");
            RejectLinkedImagePath(sourceRoot, originalPath);
            Directory.CreateDirectory(imagesDirectory);
            RejectLinkedImagePath(destinationRoot, imagesDirectory);
            var proposed = Path.Combine(imagesDirectory, Path.GetFileName(originalPath));
            RejectLinkedImagePath(destinationRoot, proposed);
            if (File.Exists(proposed) && !SameContents(proposed, originalPath))
            {
                var collision = MessageBox.Show(
                    $"An image named '{Path.GetFileName(originalPath)}' already exists and differs.\n\nYes: import under a new unique name\nNo: use the existing destination image\nCancel: cancel paste",
                    "Image filename conflict", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (collision == DialogResult.Cancel)
                    return false;
                if (collision == DialogResult.Yes)
                {
                    var stem = Path.GetFileNameWithoutExtension(originalPath);
                    var extension = Path.GetExtension(originalPath);
                    for (var n = 1; ; n++)
                    {
                        proposed = Path.Combine(imagesDirectory, $"{stem}-{n}{extension}");
                        if (!File.Exists(proposed))
                            break;
                    }
                }
            }

            if (!File.Exists(proposed))
            {
                using var src = File.OpenRead(originalPath);
                using var dst = new FileStream(proposed, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                createdFiles.Add(proposed);
                src.CopyTo(dst);
            }

            var destinationSource = Path.GetRelativePath(destinationRoot, proposed);
            imported.Add(slot.Source, destinationSource);
            slot.Replace(destinationSource);
        }

        return true;
    }

    private static void RejectLinkedImagePath(string dashboardDirectory, string requestedPath)
    {
        var root = Path.GetFullPath(dashboardDirectory).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(requestedPath);
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The image path leaves the dashboard directory.");

        for (var current = candidate;
             !string.Equals(current, root, StringComparison.OrdinalIgnoreCase);
             current = Path.GetDirectoryName(current)
                 ?? throw new InvalidDataException("Could not verify image path containment."))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Image import through a symbolic link or junction is not supported.");
        }
    }

    private static bool SameContents(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length)
            return false;
        using var streamA = File.OpenRead(a);
        using var streamB = File.OpenRead(b);
        return System.Security.Cryptography.SHA256.HashData(streamA)
            .AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(streamB));
    }
}
