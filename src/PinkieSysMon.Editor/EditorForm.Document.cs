using PinkieSysMon;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private bool SaveDashboardMetadata()
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
            _log.ErrorThrottled(
                "editor.metadata.save",
                TimeSpan.FromSeconds(30),
                $"Editor metadata could not be saved to '{_dashboardMetadataPath}'",
                ex);
            SetStatus($"Editor metadata was not saved: {ex.Message}");

            if (!_metadataSaveErrorShown)
            {
                _metadataSaveErrorShown = true;
                MessageBox.Show(
                    $"Editor group metadata could not be saved. The dashboard itself remains open and editable.\n\n{ex.Message}",
                    "PinkieSysMon Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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

        try
        {
            var text = DashboardJson.Serialize(_definition, indented: true) + Environment.NewLine;
            var temp = _dashboardPath + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, _dashboardPath, true);
            _savedJson = DashboardJson.Serialize(_definition, indented: false);
            _dirty = false;
            UpdateTitle();
            SetStatus($"Saved {_dashboardPath}. Use the target device under Output → Start / Reload to apply runtime changes.");
            UpdateCommandStates();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard was not saved.\n\n{ex.Message}", "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var validationError = ValidateDashboardName(name);
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "Clone Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialog.DialogResult = DialogResult.None;
                continue;
            }

            var dashboardsRoot = DashboardCatalog.GetRoot(_root);
            Directory.CreateDirectory(dashboardsRoot);
            var existing = Directory.EnumerateDirectories(dashboardsRoot)
                .Select(Path.GetFileName)
                .FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                MessageBox.Show($"Dashboard '{existing}' already exists.", "Clone Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialog.DialogResult = DialogResult.None;
                continue;
            }

            var sourceDirectory = Path.GetDirectoryName(_dashboardPath)!;
            var destinationPath = DashboardCatalog.GetDefinitionPath(_root, name);
            var destinationDirectory = Path.GetDirectoryName(destinationPath)!;

            try
            {
                var serialized = DashboardJson.Serialize(_definition, indented: true) + Environment.NewLine;
                CopyDashboardDirectory(sourceDirectory, destinationDirectory);
                File.WriteAllText(destinationPath, serialized);
                _dashboardMetadata.Save(Path.Combine(destinationDirectory, ".editor.json"));

                SaveCurrentDashboardViewState();
                var sourceView = _settings.GetDashboard(_dashboardName);
                _settings.Dashboards[name] = new DashboardViewSettings
                {
                    Zoom = sourceView.Zoom,
                    ScrollX = sourceView.ScrollX,
                    ScrollY = sourceView.ScrollY
                };
                _settings.Save(_settingsPath);

                LoadDashboard(name);
                SetStatus($"Cloned dashboard '{_dashboardName}'.");
                return;
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(destinationDirectory))
                        Directory.Delete(destinationDirectory, recursive: true);
                }
                catch
                {
                }

                MessageBox.Show($"Could not clone dashboard.\n\n{ex.Message}", "Clone Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        Text = $"PinkieSysMon Dashboard Editor — {_dashboardName}{(_dirty ? " *" : string.Empty)}";
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
        if (snapshot is null || !SnapshotDiffers(snapshot))
            return;

        PushHistorySnapshot(_undo, snapshot);
        _redo.Clear();
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

        var current = CaptureSnapshot();
        var target = _undo.Peek();
        try
        {
            RestoreSnapshot(target);
            _undo.Pop();
            if (current is not null)
                PushHistorySnapshot(_redo, current);
            SetStatus("Undo");
        }
        catch (Exception ex)
        {
            _log.Error("Undo restore failed", ex);
            MessageBox.Show(
                $"Undo could not be completed. The current dashboard remains open.\n\n{ex.Message}",
                "PinkieSysMon Editor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

        var current = CaptureSnapshot();
        var target = _redo.Peek();
        try
        {
            RestoreSnapshot(target);
            _redo.Pop();
            if (current is not null)
                PushHistorySnapshot(_undo, current);
            SetStatus("Redo");
        }
        catch (Exception ex)
        {
            _log.Error("Redo restore failed", ex);
            MessageBox.Show(
                $"Redo could not be completed. The current dashboard remains open.\n\n{ex.Message}",
                "PinkieSysMon Editor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(_dashboardPath))
            return;

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

        _dirty = !string.Equals(_savedJson, DashboardJson.SerializeEditorSnapshot(_definition), StringComparison.Ordinal);
        UpdateTitle();
        UpdateCommandStates();
        RenderPreview(force: true, immediate: true);
        SaveDashboardMetadata();
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
