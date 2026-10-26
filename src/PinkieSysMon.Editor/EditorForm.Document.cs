using PinkieSysMon;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private bool SaveDashboardMetadata(LogOperation? operation = null)
    {
        if (string.IsNullOrWhiteSpace(_dashboardMetadataPath))
            return true;

        try
        {
            _dashboardMetadata.Save(_dashboardMetadataPath);
            _metadataSaveErrorShown = false;
            return true;
        }
        catch (Exception ex)
        {
            if (operation is not null)
                operation.RecordFailure(ex, "metadata", "Editor group metadata was not saved.");
            else
                _log.ErrorThrottled("editor.metadata.save", TimeSpan.FromSeconds(30),
                    $"Editor metadata save failed; dashboard={_dashboardName}.", ex);
            SetStatus($"Editor metadata was not saved: {ex.Message}");

            if (!_metadataSaveErrorShown)
            {
                _metadataSaveErrorShown = true;
                ShowDocumentMessage(
                    $"Editor group metadata could not be saved. The dashboard itself remains open and editable.\n\n{ex.Message}",
                    "PinkieSysMon Editor", MessageBoxIcon.Warning);
            }

            return false;
        }
    }

    private string CreateUniqueId(string type)
    {
        var used = new HashSet<string>(_definition!.Widgets.Where(w => !string.IsNullOrWhiteSpace(w.Id)).Select(w => w.Id!), StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var candidate = $"{type}-{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private bool SaveDashboard()
    {
        if (_definition is null || string.IsNullOrWhiteSpace(_dashboardPath))
            return false;

        using var operation = new LogOperation(_log, "editor.dashboard.save", _dashboardName);
        var jsonCommitted = false;
        try
        {
            operation.Phase("serialize");
            var text = DashboardJson.Serialize(_definition, indented: true) + Environment.NewLine;
            var temp = _dashboardPath + ".tmp";
            operation.Phase("json.write");
            File.WriteAllText(temp, text);
            operation.Phase("json.commit");
            File.Move(temp, _dashboardPath, true);
            jsonCommitted = true;
            _savedJson = DashboardJson.Serialize(_definition, indented: false);
            operation.Phase("metadata");
            if (!SaveDashboardMetadata(operation))
            {
                // JSON was written, but the associated Editor-only group tree was
                // not. Keep Save available, and do not allow a destructive close
                // to treat this as a successful save.
                _loadRequiresSave = true;
                _dirty = true;
                UpdateTitle();
                SetStatus("Dashboard JSON saved, but Editor group metadata was NOT saved. Correct the error and retry Save.");
                UpdateCommandStates();
                operation.Partial("json=committed; metadata=failed; editorRemainsDirty=true");
                return false;
            }
            _loadRequiresSave = false;
            _dirty = false;
            UpdateTitle();
            SetStatus($"Saved {_dashboardPath}. Use the selected Output → Reload to apply runtime changes.");
            UpdateCommandStates();
            operation.Complete($"json=committed; metadata=saved; widgets={_definition.Widgets.Count}");
            return true;
        }
        catch (Exception ex)
        {
            operation.Fail(ex, $"jsonCommitted={jsonCommitted}");
            ShowDocumentMessage($"Dashboard was not saved.\n\n{ex.Message}", "PinkieSysMon Editor", MessageBoxIcon.Error);
            return false;
        }
    }

    private void CloneDashboard()
    {
        if (_definition is null || string.IsNullOrWhiteSpace(_dashboardName) || string.IsNullOrWhiteSpace(_dashboardPath))
            return;

        using var dialog = new CloneDashboardDialog(SuggestCloneDashboardName());
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = dialog.DashboardName;
            using var operation = new LogOperation(_log, "editor.dashboard.clone", name);
            var validationError = ValidateDashboardName(name);
            if (validationError is not null)
            {
                operation.Cancel("Name validation rejected the request.");
                MessageBox.Show(validationError, "Clone Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialog.DialogResult = DialogResult.None;
                continue;
            }

            string? destinationDirectory = null;
            var copyStarted = false;
            try
            {
                operation.Phase("directory.check");
                var dashboardsRoot = DashboardCatalog.GetRoot(_root);
                Directory.CreateDirectory(dashboardsRoot);
                var existing = Directory.EnumerateDirectories(dashboardsRoot)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    operation.Cancel("A dashboard with this name already exists.");
                    MessageBox.Show($"Dashboard '{existing}' already exists.", "Clone Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    dialog.DialogResult = DialogResult.None;
                    continue;
                }

                var sourceDirectory = Path.GetDirectoryName(_dashboardPath)!;
                var destinationPath = DashboardCatalog.GetDefinitionPath(_root, name);
                destinationDirectory = Path.GetDirectoryName(destinationPath)!;
                operation.Phase("serialize");
                var serialized = DashboardJson.Serialize(_definition, indented: true) + Environment.NewLine;
                operation.Phase("directory.copy");
                copyStarted = true;
                CopyDashboardDirectory(sourceDirectory, destinationDirectory);
                operation.Phase("json.write");
                File.WriteAllText(destinationPath, serialized);
                operation.Phase("metadata.save");
                _dashboardMetadata.Save(Path.Combine(destinationDirectory, ".editor.json"));

                operation.Phase("view.save");
                SaveCurrentDashboardViewState();
                var sourceView = _settings.GetDashboard(_dashboardName);
                _settings.Dashboards[name] = new DashboardViewSettings
                {
                    Zoom = sourceView.Zoom,
                    ScrollX = sourceView.ScrollX,
                    ScrollY = sourceView.ScrollY
                };
                _settings.Save(_settingsPath);

                operation.Phase("document.open");
                if (!LoadDashboardCore(name, operation.Id))
                {
                    operation.Partial("copyCreated=true; editorLoadFailed=true; copiedFilesPreserved=true");
                    return;
                }
                SetStatus($"Cloned dashboard '{_dashboardName}'.");
                operation.Complete("copyCreated=true; documentLoaded=true");
                return;
            }
            catch (Exception ex)
            {
                operation.Fail(ex);
                try
                {
                    if (copyStarted && destinationDirectory is not null && Directory.Exists(destinationDirectory))
                        Directory.Delete(destinationDirectory, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    operation.RecordFailure(cleanupEx, "cleanup", "Could not remove the incomplete cloned dashboard.");
                }

                ShowDocumentMessage($"Could not clone dashboard.\n\n{ex.Message}", "Clone Dashboard", MessageBoxIcon.Error);
                return;
            }
        }
    }

    private string SuggestCloneDashboardName()
    {
        var existing = DashboardCatalog.Discover(_root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = $"{_dashboardName} Copy";
        if (!existing.Contains(baseName))
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    private static void CopyDashboardDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((sourceDirectory, destinationDirectory));

        while (pending.Count > 0)
        {
            var (source, destination) = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(source))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var childDestination = Path.Combine(destination, Path.GetFileName(directory));
                Directory.CreateDirectory(childDestination);
                pending.Push((directory, childDestination));
            }
        }
    }

    private void MarkDirty()
    {
        if (_dirty)
            return;

        _dirty = true;
        UpdateTitle();
        UpdateCommandStates();
    }

    private void UpdateTitle()
    {
        Text = string.IsNullOrWhiteSpace(_dashboardName)
            ? "PinkieSysMon Dashboard Editor"
            : $"PinkieSysMon Dashboard Editor — {_dashboardName}{(_dirty ? " *" : string.Empty)}";
    }

    private void SetStatus(string text) => _statusText.Text = text;

    private EditorSnapshot? CaptureSnapshot()
    {
        if (_definition is null)
            return null;

        var indices = _selectedWidgets
            .Select(widget => _definition.Widgets.IndexOf(widget))
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        return new EditorSnapshot(
            DashboardJson.SerializeEditorSnapshot(_definition),
            indices,
            _dashboardMetadata.ToCompactJson(),
            _selectedTreeKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray(),
            _treePrimaryKey,
            _treeSelectionAnchorKey);
    }

    private bool SnapshotDiffers(EditorSnapshot snapshot) =>
        _definition is not null &&
        (!string.Equals(snapshot.Json, DashboardJson.SerializeEditorSnapshot(_definition), StringComparison.Ordinal) ||
         !string.Equals(snapshot.EditorMetadataJson, _dashboardMetadata.ToCompactJson(), StringComparison.Ordinal));

    private void PushUndo(EditorSnapshot? snapshot)
    {
        // A subsequent non-property edit invalidates any incomplete PropertyGrid transaction.
        _propertyEditUndoSnapshot = null;
        if (snapshot is null || !SnapshotDiffers(snapshot))
            return;

        PushHistorySnapshot(_undo, snapshot);
        _redo.Clear();
        LogEditCommitted();
        UpdateCommandStates();
    }

    private static void PushHistorySnapshot(Stack<EditorSnapshot> history, EditorSnapshot snapshot)
    {
        history.Push(snapshot);

        // Stack enumerates newest -> oldest. Keep the newest bounded window while
        // preserving normal push/pop order. The byte estimate is intentionally
        // conservative: snapshots are mostly UTF-16 strings plus small selection arrays.
        var retainedNewestFirst = new List<EditorSnapshot>(Math.Min(history.Count, HistorySnapshotLimit));
        long retainedBytes = 0;
        foreach (var item in history)
        {
            if (retainedNewestFirst.Count >= HistorySnapshotLimit)
                break;

            var itemBytes = EstimateSnapshotBytes(item);
            if (retainedNewestFirst.Count > 0 && retainedBytes + itemBytes > HistorySnapshotMemoryLimitBytes)
                break;

            retainedNewestFirst.Add(item);
            retainedBytes += itemBytes;
        }

        if (retainedNewestFirst.Count == history.Count)
            return;

        history.Clear();
        for (var i = retainedNewestFirst.Count - 1; i >= 0; i--)
            history.Push(retainedNewestFirst[i]);
    }

    private static long EstimateSnapshotBytes(EditorSnapshot snapshot)
    {
        long chars = snapshot.Json.Length + snapshot.EditorMetadataJson.Length;
        chars += snapshot.TreePrimaryKey?.Length ?? 0;
        chars += snapshot.TreeAnchorKey?.Length ?? 0;
        foreach (var key in snapshot.SelectedTreeKeys)
            chars += key.Length;

        return chars * sizeof(char) +
               (long)snapshot.SelectedWidgetIndices.Length * sizeof(int) +
               (long)snapshot.SelectedTreeKeys.Length * IntPtr.Size;
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;

        using var operation = new LogOperation(_log, "editor.history.undo", _dashboardName);
        try
        {
            operation.Phase("snapshot.capture");
            var current = CaptureSnapshot();
            var target = _undo.Peek();
            operation.Phase("snapshot.restore");
            var metadataSaved = RestoreSnapshotCore(target, operation);
            _undo.Pop();
            if (current is not null)
                PushHistorySnapshot(_redo, current);
            SetStatus("Undo");
            if (metadataSaved) operation.Complete("documentRestored=true; metadata=saved");
            else operation.Partial("documentRestored=true; metadata=failed");
        }
        catch (Exception ex)
        {
            operation.Fail(ex);
            ShowDocumentMessage(
                $"Undo could not be completed. The current dashboard remains open.\n\n{ex.Message}",
                "PinkieSysMon Editor", MessageBoxIcon.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;

        using var operation = new LogOperation(_log, "editor.history.redo", _dashboardName);
        try
        {
            operation.Phase("snapshot.capture");
            var current = CaptureSnapshot();
            var target = _redo.Peek();
            operation.Phase("snapshot.restore");
            var metadataSaved = RestoreSnapshotCore(target, operation);
            _redo.Pop();
            if (current is not null)
                PushHistorySnapshot(_undo, current);
            SetStatus("Redo");
            if (metadataSaved) operation.Complete("documentRestored=true; metadata=saved");
            else operation.Partial("documentRestored=true; metadata=failed");
        }
        catch (Exception ex)
        {
            operation.Fail(ex);
            ShowDocumentMessage(
                $"Redo could not be completed. The current dashboard remains open.\n\n{ex.Message}",
                "PinkieSysMon Editor", MessageBoxIcon.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private void RestoreSnapshot(EditorSnapshot snapshot) => RestoreSnapshotCore(snapshot, operation: null);

    private bool RestoreSnapshotCore(EditorSnapshot snapshot, LogOperation? operation)
    {
        if (string.IsNullOrWhiteSpace(_dashboardPath))
            return false;

        _propertyEditUndoSnapshot = null;
        var scroll = CaptureScrollPosition();
        var definition = DashboardJson.ParseEditorSnapshot(snapshot.Json, _dashboardPath);
        var metadata = DashboardEditorMetadata.FromJson(snapshot.EditorMetadataJson);

        // Validate the complete hierarchy before replacing any live editor document state.
        _ = new EditorLayerHierarchy(definition, metadata).FlattenWidgets();
        ReplacePreviewRenderer(definition);

        _definition = definition;
        _dashboardMetadata = metadata;
        _widgetTreeNodes.Clear();
        _groupTreeNodes.Clear();

        _selectedWidgets.Clear();
        _selectedTreeKeys.Clear();
        foreach (var key in snapshot.SelectedTreeKeys)
            _selectedTreeKeys.Add(key);
        _treePrimaryKey = snapshot.TreePrimaryKey;
        _treeSelectionAnchorKey = snapshot.TreeAnchorKey;

        if (_selectedTreeKeys.Count == 0)
        {
            foreach (var index in snapshot.SelectedWidgetIndices)
            {
                if (index >= 0 && index < definition.Widgets.Count && !string.IsNullOrWhiteSpace(definition.Widgets[index].Id))
                    _selectedTreeKeys.Add(WidgetTreeKey(definition.Widgets[index]));
            }
            var fallbackPrimary = snapshot.SelectedWidgetIndices
                .Where(index => index >= 0 && index < definition.Widgets.Count)
                .Select(index => definition.Widgets[index])
                .LastOrDefault();
            _treePrimaryKey = fallbackPrimary is not null ? WidgetTreeKey(fallbackPrimary) : null;
            _treeSelectionAnchorKey = _treePrimaryKey;
        }

        PopulateTree();
        ApplyTreeSelectionToWidgets();
        ApplyZoom(restoreScroll: false);
        UpdateStatusBar();
        RestoreScrollPositionDeferred(scroll);

        _dirty = _loadRequiresSave ||
            !string.Equals(_savedJson, DashboardJson.SerializeEditorSnapshot(_definition), StringComparison.Ordinal);
        UpdateTitle();
        UpdateCommandStates();
        RenderPreview(force: true, immediate: true);
        return SaveDashboardMetadata(operation);
    }



    private static void NormalizePreparedLayerOrder(
        CanonicalDashboardDefinition definition,
        IReadOnlyList<CanonicalWidgetDefinition> order)
    {
        if (order.Count != definition.Widgets.Count)
            throw new InvalidDataException("Layer hierarchy does not contain every dashboard widget exactly once.");

        definition.Widgets.Clear();
        definition.Widgets.AddRange(order);
        for (var i = 0; i < order.Count; i++)
            order[i].Z = i;
    }

}
