using System.Diagnostics;
using PinkieSysMon;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalCanvasDefinition = PinkieSysMon.DashboardModel.CanvasDefinition;
using CanonicalStateVisualWidgetDefinition = PinkieSysMon.DashboardModel.StateVisualWidgetDefinition;
using CanonicalValueWidgetDefinition = PinkieSysMon.DashboardModel.ValueWidgetDefinition;
using CanonicalBinaryWidgetDefinition = PinkieSysMon.DashboardModel.BinaryWidgetDefinition;
using CanonicalGaugeWidgetDefinition = PinkieSysMon.DashboardModel.GaugeWidgetDefinition;
using CanonicalBarWidgetDefinition = PinkieSysMon.DashboardModel.BarWidgetDefinition;
using CanonicalImageWidgetDefinition = PinkieSysMon.DashboardModel.ImageWidgetDefinition;
using CanonicalPowerWidgetDefinition = PinkieSysMon.DashboardModel.PowerWidgetDefinition;
using CanonicalMediaSystemWidgetDefinition = PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition;
using CanonicalMediaPlayerWidgetDefinition = PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition;
using CanonicalStateContentType = PinkieSysMon.DashboardModel.StateContentType;
using CanonicalImageAssetSourceType = PinkieSysMon.DashboardModel.ImageAssetSourceType;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private ToolStripMenuItem _openDashboardMenuItem = null!;
    private ToolStripMenuItem _saveMenuItem = null!;
    private ToolStripMenuItem _cloneDashboardMenuItem = null!;
    private ToolStripMenuItem _reloadEditorMenuItem = null!;
    private ToolStripMenuItem _undoMenuItem = null!;
    private ToolStripMenuItem _redoMenuItem = null!;
    private ToolStripMenuItem _addWidgetMenuItem = null!;
    private ToolStripMenuItem _renameMenuItem = null!;
    private ToolStripMenuItem _duplicateMenuItem = null!;
    private ToolStripMenuItem _deleteMenuItem = null!;
    private ToolStripMenuItem _groupSelectedMenuItem = null!;
    private ToolStripMenuItem _ungroupSelectedMenuItem = null!;
    private ToolStripMenuItem _moveUpMenuItem = null!;
    private ToolStripMenuItem _moveDownMenuItem = null!;
    private ToolStripMenuItem _bringToFrontMenuItem = null!;
    private ToolStripMenuItem _sendToBackMenuItem = null!;

    private ToolStripMenuItem _treeRenameMenuItem = null!;
    private ToolStripMenuItem _treeDuplicateMenuItem = null!;
    private ToolStripMenuItem _treeDeleteMenuItem = null!;
    private ToolStripMenuItem _treeMoveUpMenuItem = null!;
    private ToolStripMenuItem _treeMoveDownMenuItem = null!;
    private ToolStripMenuItem _treeBringToFrontMenuItem = null!;
    private ToolStripMenuItem _treeSendToBackMenuItem = null!;
    private ToolStripMenuItem _treeGroupSelectedMenuItem = null!;
    private ToolStripMenuItem _treeUngroupSelectedMenuItem = null!;

    private ToolStripMenuItem _screenMenu = null!;
    private ToolStripMenuItem _screenStartReloadAllMenuItem = null!;
    private ToolStripMenuItem _screenStopAllMenuItem = null!;
    private readonly Dictionary<string, ScreenTargetMenuItems> _screenTargetMenuItems = new(StringComparer.OrdinalIgnoreCase);

    private ToolStripMenuItem _gridToolsMenu = null!;
    private ToolStripMenuItem _gridToggleMenuItem = null!;
    private ToolStripMenuItem _zoomToolsMenu = null!;
    private ToolStripMenuItem _zoomInMenuItem = null!;
    private ToolStripMenuItem _zoomOutMenuItem = null!;
    private ToolStripMenuItem _zoom100MenuItem = null!;
    private ToolStripMenuItem _zoomFitHeightMenuItem = null!;
    private ToolStripMenuItem _dashboardSnapshotMenuItem = null!;

    private ToolStripButton _toolbarNewButton = null!;
    private ToolStripButton _toolbarOpenButton = null!;
    private ToolStripButton _toolbarSaveButton = null!;
    private ToolStripButton _toolbarReloadEditorButton = null!;
    private ToolStripDropDownButton _toolbarAddButton = null!;
    private ToolStripButton _toolbarDuplicateButton = null!;
    private ToolStripButton _toolbarUndoButton = null!;
    private ToolStripButton _toolbarRedoButton = null!;
    private ToolStripButton _toolbarGroupButton = null!;
    private ToolStripButton _toolbarUngroupButton = null!;
    private ToolStripButton _toolbarAlignLeftButton = null!;
    private ToolStripButton _toolbarAlignHorizontalCenterButton = null!;
    private ToolStripButton _toolbarAlignRightButton = null!;
    private ToolStripButton _toolbarAlignTopButton = null!;
    private ToolStripButton _toolbarAlignVerticalCenterButton = null!;
    private ToolStripButton _toolbarAlignBottomButton = null!;
    private ToolStripButton _toolbarZoomInButton = null!;
    private ToolStripTextBox _toolbarZoomTextBox = null!;
    private ToolStripButton _toolbarZoomOutButton = null!;
    private ToolStripButton _toolbarZoom100Button = null!;
    private ToolStripButton _toolbarFitHeightButton = null!;
    private CheckableToolStripSplitButton _toolbarGridButton = null!;

    private bool? _runtimeRunning;
    private RuntimeStatusSnapshot? _runtimeStatusSnapshot;

    private sealed record ScreenTargetMenuItems(
        ToolStripMenuItem Root,
        ToolStripMenuItem Status,
        ToolStripMenuItem StartReload,
        ToolStripMenuItem Stop);

    private bool CanOpenDashboardCommand() =>
        _definition is not null || DashboardCatalog.Discover(_root).Count > 0;

    private bool CanSaveCommand() =>
        _dirty && _definition is not null && !string.IsNullOrWhiteSpace(_dashboardPath);

    private bool CanReloadEditorCommand() =>
        _definition is not null && !string.IsNullOrWhiteSpace(_dashboardName);

    private bool CanCloneDashboardCommand() =>
        _definition is not null && !string.IsNullOrWhiteSpace(_dashboardPath);

    private bool CanDashboardSnapshotCommand() =>
        _definition is not null && _preview is not null;

    private enum AlignmentCommand
    {
        Left,
        HorizontalCenter,
        Right,
        Top,
        VerticalCenter,
        Bottom
    }

    private bool CanUndoCommand() => _undo.Count > 0;
    private bool CanRedoCommand() => _redo.Count > 0;
    private bool CanAddWidgetCommand() => _definition is not null;
    private bool CanRenameCommand() => GetDirectSelectedLayerNodes().Length == 1;
    private bool CanDeleteCommand() => GetDirectSelectedLayerNodes().Length > 0;

    private EditorLayerNodeRef[] GetDirectSelectedLayerNodes()
    {
        if (_selectedTreeKeys.Count == 0)
            return [];

        var result = new List<EditorLayerNodeRef>(_selectedTreeKeys.Count);
        foreach (var key in _selectedTreeKeys)
        {
            var node = GetLayerNodeRef(key);
            if (!node.HasValue)
                return [];
            result.Add(node.Value);
        }
        return result.ToArray();
    }

    private EditorLayerNodeKind? GetHomogeneousSelectedLayerKind() =>
        EditorLayerSelection.GetHomogeneousKind(GetDirectSelectedLayerNodes());

    private bool HasOnlySelectedLayerKind(EditorLayerNodeKind kind) =>
        GetHomogeneousSelectedLayerKind() == kind;

    private bool CanDuplicateSelectedCommand() =>
        GetHomogeneousSelectedLayerKind().HasValue;

    private bool CanGroupSelectedCommand()
    {
        var kind = GetHomogeneousSelectedLayerKind();
        return kind.HasValue && CanGroupSelection(kind.Value);
    }

    private bool CanUngroupSelectedCommand()
    {
        var kind = GetHomogeneousSelectedLayerKind();
        return kind.HasValue && CanUngroupSelection(kind.Value);
    }

    private bool CanMoveSelectedCommand(int direction)
    {
        var kind = GetHomogeneousSelectedLayerKind();
        return kind.HasValue && CanMoveSelection(kind.Value, direction);
    }

    private bool CanMoveSelectedToEdgeCommand(bool front)
    {
        var kind = GetHomogeneousSelectedLayerKind();
        return kind.HasValue && CanMoveSelectionToEdge(kind.Value, front);
    }

    private bool CanGroupSelection(EditorLayerNodeKind kind)
    {
        if (_definition is null || !HasOnlySelectedLayerKind(kind))
            return false;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        return selected.Count > 0 &&
               selected.All(node => node.Kind == kind) &&
               selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 1;
    }

    private bool CanUngroupSelection(EditorLayerNodeKind kind)
    {
        if (_definition is null || !HasOnlySelectedLayerKind(kind))
            return false;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0 || selected.Any(node => node.Kind != kind))
            return false;

        return kind == EditorLayerNodeKind.Group ||
               selected.Any(node => !string.IsNullOrWhiteSpace(hierarchy.GetParentGroupId(node)));
    }

    private bool CanMoveSelection(EditorLayerNodeKind kind, int direction)
    {
        if (_definition is null || direction == 0 || !HasOnlySelectedLayerKind(kind))
            return false;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0 || selected.Any(node => node.Kind != kind))
            return false;

        var parentIds = selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentIds.Length != 1)
            return false;

        var siblings = hierarchy.GetChildren(parentIds[0]).ToList();
        var selectedKeys = selected.Select(node => node.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (direction < 0)
        {
            for (var i = 1; i < siblings.Count; i++)
            {
                if (selectedKeys.Contains(siblings[i].Key) && !selectedKeys.Contains(siblings[i - 1].Key))
                    return true;
            }
        }
        else
        {
            for (var i = siblings.Count - 2; i >= 0; i--)
            {
                if (selectedKeys.Contains(siblings[i].Key) && !selectedKeys.Contains(siblings[i + 1].Key))
                    return true;
            }
        }

        return false;
    }

    private bool CanMoveSelectionToEdge(EditorLayerNodeKind kind, bool front)
    {
        if (_definition is null || !HasOnlySelectedLayerKind(kind))
            return false;

        var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
        var selected = GetExplicitSelectedLayerNodes(hierarchy);
        if (selected.Count == 0 || selected.Any(node => node.Kind != kind))
            return false;

        var parentIds = selected.Select(hierarchy.GetParentGroupId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parentIds.Length != 1)
            return false;

        var siblings = hierarchy.GetChildren(parentIds[0]).ToList();
        var selectedKeys = selected.Select(node => node.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moving = siblings.Where(node => selectedKeys.Contains(node.Key)).ToList();
        if (moving.Count == 0 || moving.Count == siblings.Count)
            return false;

        var reordered = siblings.Where(node => !selectedKeys.Contains(node.Key)).ToList();
        if (front)
            reordered.AddRange(moving);
        else
            reordered.InsertRange(0, moving);

        return !siblings.SequenceEqual(reordered);
    }

    private void UpdateCommandStates()
    {
        var canOpenDashboard = CanOpenDashboardCommand();
        var canSave = CanSaveCommand();
        var canReloadEditor = CanReloadEditorCommand();
        var canUndo = CanUndoCommand();
        var canRedo = CanRedoCommand();
        var canAddWidget = CanAddWidgetCommand();
        var canRename = CanRenameCommand();
        var canDuplicate = CanDuplicateSelectedCommand();
        var canDelete = CanDeleteCommand();
        var canGroupSelected = CanGroupSelectedCommand();
        var canUngroupSelected = CanUngroupSelectedCommand();
        var canMoveUp = CanMoveSelectedCommand(-1);
        var canMoveDown = CanMoveSelectedCommand(1);
        var canBringToFront = CanMoveSelectedToEdgeCommand(front: true);
        var canSendToBack = CanMoveSelectedToEdgeCommand(front: false);
        var canAlign = CanAlignSelection();
        var canZoom = _preview is not null;
        var canZoomIn = canZoom && _zoom < MaxEditorZoom - 0.0001f;
        var canZoomOut = canZoom && _zoom > MinEditorZoom + 0.0001f;
        var canGrid = _definition is not null;

        _openDashboardMenuItem.Enabled = canOpenDashboard;
        _saveMenuItem.Enabled = canSave;
        _cloneDashboardMenuItem.Enabled = CanCloneDashboardCommand();
        _reloadEditorMenuItem.Enabled = canReloadEditor;
        _undoMenuItem.Enabled = canUndo;
        _redoMenuItem.Enabled = canRedo;
        _addWidgetMenuItem.Enabled = canAddWidget;
        _renameMenuItem.Enabled = canRename;
        _duplicateMenuItem.Enabled = canDuplicate;
        _deleteMenuItem.Enabled = canDelete;
        _groupSelectedMenuItem.Enabled = canGroupSelected;
        _ungroupSelectedMenuItem.Enabled = canUngroupSelected;
        _moveUpMenuItem.Enabled = canMoveUp;
        _moveDownMenuItem.Enabled = canMoveDown;
        _bringToFrontMenuItem.Enabled = canBringToFront;
        _sendToBackMenuItem.Enabled = canSendToBack;

        _treeRenameMenuItem.Enabled = canRename;
        _treeDuplicateMenuItem.Enabled = canDuplicate;
        _treeDeleteMenuItem.Enabled = canDelete;
        _treeGroupSelectedMenuItem.Enabled = canGroupSelected;
        _treeUngroupSelectedMenuItem.Enabled = canUngroupSelected;
        _treeMoveUpMenuItem.Enabled = canMoveUp;
        _treeMoveDownMenuItem.Enabled = canMoveDown;
        _treeBringToFrontMenuItem.Enabled = canBringToFront;
        _treeSendToBackMenuItem.Enabled = canSendToBack;

        _gridToolsMenu.Enabled = canGrid;
        _gridToggleMenuItem.Enabled = canGrid;
        _gridToggleMenuItem.Checked = _gridEnabled;
        _zoomToolsMenu.Enabled = canZoom;
        _zoomInMenuItem.Enabled = canZoomIn;
        _zoomOutMenuItem.Enabled = canZoomOut;
        _zoom100MenuItem.Enabled = canZoom && (_fitZoom || Math.Abs(_zoom - 1f) > 0.0001f);
        _zoomFitHeightMenuItem.Enabled = canZoom && !_fitZoom;
        _dashboardSnapshotMenuItem.Enabled = CanDashboardSnapshotCommand();

        UpdateScreenMenuStates();

        _toolbarNewButton.Enabled = true;
        _toolbarOpenButton.Enabled = canOpenDashboard;
        _toolbarSaveButton.Enabled = canSave;
        _toolbarReloadEditorButton.Enabled = canReloadEditor;
        _toolbarAddButton.Enabled = canAddWidget;
        _toolbarDuplicateButton.Enabled = canDuplicate;
        _toolbarUndoButton.Enabled = canUndo;
        _toolbarRedoButton.Enabled = canRedo;
        _toolbarGroupButton.Enabled = canGroupSelected;
        _toolbarUngroupButton.Enabled = canUngroupSelected;
        _toolbarAlignLeftButton.Enabled = canAlign;
        _toolbarAlignHorizontalCenterButton.Enabled = canAlign;
        _toolbarAlignRightButton.Enabled = canAlign;
        _toolbarAlignTopButton.Enabled = canAlign;
        _toolbarAlignVerticalCenterButton.Enabled = canAlign;
        _toolbarAlignBottomButton.Enabled = canAlign;
        _toolbarZoomInButton.Enabled = canZoomIn;
        _toolbarZoomTextBox.Enabled = canZoom;
        _toolbarZoomOutButton.Enabled = canZoomOut;
        _toolbarZoom100Button.Enabled = canZoom && (_fitZoom || Math.Abs(_zoom - 1f) > 0.0001f);
        _toolbarFitHeightButton.Enabled = canZoom && !_fitZoom;
        _toolbarGridButton.Enabled = canGrid;
        _toolbarGridButton.Checked = _gridEnabled;
    }

    private void ExecuteNewDashboardCommand()
    {
        NewDashboard();
        UpdateCommandStates();
    }

    private void ExecuteOpenDashboardCommand()
    {
        if (CanOpenDashboardCommand())
            OpenDashboard();
        UpdateCommandStates();
    }

    private void ExecuteSaveCommand()
    {
        if (CanSaveCommand())
            SaveDashboard();
        UpdateCommandStates();
    }

    private void ExecuteCloneDashboardCommand()
    {
        if (CanCloneDashboardCommand())
            CloneDashboard();
        UpdateCommandStates();
    }

    private void ExecuteDashboardSnapshotCommand()
    {
        if (CanDashboardSnapshotCommand())
            SaveDashboardSnapshot();
        UpdateCommandStates();
    }

    private void ExecuteReloadEditorCommand()
    {
        if (CanReloadEditorCommand())
            ReloadDashboard();
        UpdateCommandStates();
    }

    private void ExecuteUndoCommand()
    {
        if (CanUndoCommand())
            Undo();
        UpdateCommandStates();
    }

    private void ExecuteRedoCommand()
    {
        if (CanRedoCommand())
            Redo();
        UpdateCommandStates();
    }

    private void ExecuteAddWidgetCommand(string type)
    {
        if (CanAddWidgetCommand())
            AddWidget(type);
        UpdateCommandStates();
    }


    private void ExecuteRenameCommand()
    {
        if (CanRenameCommand())
            RenameSelectedTreeItem();
        UpdateCommandStates();
    }

    private void ExecuteDuplicateCommand()
    {
        var kind = GetHomogeneousSelectedLayerKind();
        if (kind == EditorLayerNodeKind.Widget)
            DuplicateSelectedWidgets();
        else if (kind == EditorLayerNodeKind.Group)
            DuplicateSelectedGroups();
        UpdateCommandStates();
    }

    private void ExecuteDeleteCommand()
    {
        if (CanDeleteCommand())
            DeleteSelectedTreeItems();
        UpdateCommandStates();
    }

    private void ExecuteGroupSelectedCommand()
    {
        if (CanGroupSelectedCommand())
            GroupSelectedLayerNodes();
        UpdateCommandStates();
    }

    private void ExecuteUngroupSelectedCommand()
    {
        if (CanUngroupSelectedCommand())
            UngroupSelectedLayerNodes();
        UpdateCommandStates();
    }

    private void ExecuteAlignCommand(AlignmentCommand alignment)
    {
        if (CanAlignSelection())
            AlignSelectedLayerNodes(alignment);
        UpdateCommandStates();
    }

    private void ExecuteMoveSelectedCommand(int direction)
    {
        if (CanMoveSelectedCommand(direction))
            MoveSelectionInLayerOrder(direction);
        UpdateCommandStates();
    }

    private void ExecuteMoveSelectedToEdgeCommand(bool front)
    {
        if (CanMoveSelectedToEdgeCommand(front))
            MoveSelectionToLayerEdge(front);
        UpdateCommandStates();
    }

    private void RebuildScreenMenu()
    {
        _screenMenu.DropDownItems.Clear();
        _screenTargetMenuItems.Clear();

        foreach (var target in _appConfig.Outputs.Targets)
        {
            var targetId = target.Id;
            var targetItem = new ToolStripMenuItem(target.Name);
            var statusItem = new ToolStripMenuItem("Status: checking...") { Enabled = false };
            var startReloadItem = new ToolStripMenuItem(
                ScreenMenuContract.StartReloadText,
                null,
                async (_, _) => await ExecuteStartOrReloadTargetAsync(targetId));
            var stopItem = new ToolStripMenuItem(
                ScreenMenuContract.StopText,
                null,
                async (_, _) => await ExecuteStopTargetAsync(targetId));

            targetItem.DropDownItems.Add(statusItem);
            targetItem.DropDownItems.Add(new ToolStripSeparator());
            targetItem.DropDownItems.Add(startReloadItem);
            targetItem.DropDownItems.Add(stopItem);
            _screenMenu.DropDownItems.Add(targetItem);

            _screenTargetMenuItems[targetId] = new ScreenTargetMenuItems(
                targetItem,
                statusItem,
                startReloadItem,
                stopItem);
        }

        _screenMenu.DropDownItems.Add(new ToolStripSeparator());
        _screenStartReloadAllMenuItem = new ToolStripMenuItem(
            ScreenMenuContract.StartReloadAllText,
            null,
            async (_, _) => await ExecuteStartOrReloadAllAsync());
        _screenStopAllMenuItem = new ToolStripMenuItem(
            ScreenMenuContract.StopAllText,
            null,
            async (_, _) => await ExecuteStopAllOutputsAsync());
        _screenMenu.DropDownItems.Add(_screenStartReloadAllMenuItem);
        _screenMenu.DropDownItems.Add(_screenStopAllMenuItem);

        UpdateScreenMenuStates();
    }

    private void RefreshScreenTargetsFromConfiguration()
    {
        try
        {
            var config = AppConfig.Load(_appConfigPath, _log);
            var oldIdentity = _appConfig.Outputs.Targets
                .Select(target => (target.Id, target.Name))
                .ToArray();
            var newIdentity = config.Outputs.Targets
                .Select(target => (target.Id, target.Name))
                .ToArray();

            _appConfig.Outputs = config.Outputs;
            if (!oldIdentity.SequenceEqual(newIdentity))
                RebuildScreenMenu();
        }
        catch (Exception ex)
        {
            _log.WarnThrottled(
                "editor.screen.config",
                TimeSpan.FromSeconds(30),
                "Output menu could not refresh output targets from app.json",
                ex);
        }
    }

    private void UpdateScreenMenuStates()
    {
        if (_screenMenu is null || _screenStartReloadAllMenuItem is null || _screenStopAllMenuItem is null)
            return;

        var state = ScreenMenuContract.Build(
            _appConfig.Outputs.Targets,
            _runtimeRunning,
            _runtimeStatusSnapshot,
            _screenCommandInProgress);

        _screenMenu.Enabled = state.Targets.Length > 0;
        foreach (var targetState in state.Targets)
        {
            if (!_screenTargetMenuItems.TryGetValue(targetState.TargetId, out var items))
                continue;

            items.Root.Text = targetState.TargetName;
            items.Root.ToolTipText = targetState.StatusText;
            items.Status.Text = targetState.StatusText;
            items.StartReload.Enabled = targetState.CanStartOrReload;
            items.Stop.Enabled = targetState.CanStop;
        }

        _screenStartReloadAllMenuItem.Enabled = state.CanStartOrReloadAll;
        _screenStopAllMenuItem.Enabled = state.CanStopAll;
    }

    private async Task ExecuteStartOrReloadTargetAsync(string targetId)
    {
        var name = _appConfig.Outputs.Targets
            .FirstOrDefault(target => target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? targetId;

        await ExecuteOutputCommandAsync(
            async () =>
            {
                var wasActive = ScreenMenuContract.IsTargetActive(_runtimeStatusSnapshot, targetId);

                // Dashboard/configuration are process-global today, so refresh the shared runtime
                // state first. Output activation remains target-scoped: a stopped sibling stays
                // stopped because OutputSessionManager preserves explicit stop state on reload.
                var reload = await RuntimeIpc.ReloadAsync();
                if (!reload.Success || wasActive)
                    return reload;

                return await RuntimeIpc.StartOrReloadTargetAsync(targetId);
            },
            startRuntimeIfNeeded: true,
            dialogTitle: "Start / Reload Output",
            fallbackSuccessMessage: $"Output '{name}' started/reloaded.");
    }

    private async Task ExecuteStopTargetAsync(string targetId)
    {
        var name = _appConfig.Outputs.Targets
            .FirstOrDefault(target => target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? targetId;

        await ExecuteOutputCommandAsync(
            () => RuntimeIpc.StopTargetAsync(targetId),
            startRuntimeIfNeeded: false,
            dialogTitle: "Stop Output",
            fallbackSuccessMessage: $"Output '{name}' stopped and unloaded.",
            synchronizeRuntimeShutdown: true);
    }

    private async Task ExecuteStartOrReloadAllAsync()
    {
        await ExecuteOutputCommandAsync(
            async () =>
            {
                var allWereActive = ScreenMenuContract.AreAllTargetsActive(
                    _appConfig.Outputs.Targets,
                    _runtimeStatusSnapshot);

                var reload = await RuntimeIpc.ReloadAsync();
                if (!reload.Success || allWereActive)
                    return reload;

                return await RuntimeIpc.StartOrReloadAllAsync();
            },
            startRuntimeIfNeeded: true,
            dialogTitle: "Start / Reload All Outputs",
            fallbackSuccessMessage: "All configured output targets started/reloaded.");
    }

    private async Task ExecuteStopAllOutputsAsync()
    {
        await ExecuteOutputCommandAsync(
            () => RuntimeIpc.StopAllOutputsAsync(),
            startRuntimeIfNeeded: false,
            dialogTitle: "Stop All Outputs",
            fallbackSuccessMessage: "All output targets stopped and unloaded.",
            synchronizeRuntimeShutdown: true);
    }

    private async Task ExecuteOutputCommandAsync(
        Func<Task<RuntimeIpcResponse>> command,
        bool startRuntimeIfNeeded,
        string dialogTitle,
        string fallbackSuccessMessage,
        bool synchronizeRuntimeShutdown = false)
    {
        if (_screenCommandInProgress)
            return;

        _screenCommandInProgress = true;
        ++_runtimeStateGeneration;
        UpdateCommandStates();
        try
        {
            var canExecute = !startRuntimeIfNeeded || await EnsureRuntimeReadyForOutputCommandAsync();
            if (canExecute)
            {
                var response = await command();
                if (response.Success)
                {
                    var successMessage = string.IsNullOrWhiteSpace(response.Message)
                        ? fallbackSuccessMessage
                        : response.Message;
                    SetStatus(successMessage);

                    if (synchronizeRuntimeShutdown && RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(response))
                        await WaitForRuntimeProcessExitAsync();
                    else
                        _runtimeRunning = true;
                }
                else
                {
                    SetStatus(response.Message);
                    MessageBox.Show(response.Message, dialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(ex.Message, dialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _screenCommandInProgress = false;
            UpdateCommandStates();
        }

        await RefreshRuntimeStatusAsync();
    }

    private async Task WaitForRuntimeProcessExitAsync()
    {
        _runtimeRunning = null;
        _runtimeStatusSnapshot = null;
        _statusRuntime.Text = "Runtime: stopping...";

        const int attempts = 100;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!IsRuntimeProcessPresent())
            {
                _runtimeRunning = false;
                _runtimeStatusSnapshot = null;
                _statusRuntime.Text = "Runtime: Not running";
                return;
            }

            await Task.Delay(50);
        }

        _runtimeRunning = null;
        _runtimeStatusSnapshot = null;
        _statusRuntime.Text = "Runtime: stopping...";
        SetStatus("Outputs are stopped, but the PinkieSysMon runtime process has not exited yet.");
    }

    private async Task<bool> EnsureRuntimeReadyForOutputCommandAsync()
    {
        var existing = await RuntimeIpc.GetStatusAsync();
        if (existing.Success)
        {
            _runtimeRunning = true;
            _runtimeStatusSnapshot = existing.Status;
            return true;
        }

        if (IsRuntimeProcessPresent())
        {
            _runtimeRunning = null;
            _runtimeStatusSnapshot = null;
            _statusRuntime.Text = "Runtime: checking...";
            const string message = "A PinkieSysMon runtime process already exists, but its IPC endpoint is not responding. A duplicate runtime will not be started.";
            SetStatus(message);
            MessageBox.Show(message, "Start Output", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var runtimePath = Path.Combine(_root, "bin", "PinkieSysMon.exe");
        if (!File.Exists(runtimePath))
            throw new FileNotFoundException("PinkieSysMon runtime executable was not found.", runtimePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = runtimePath,
            WorkingDirectory = Path.GetDirectoryName(runtimePath)!,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(RuntimeStartupOptions.OutputsStoppedArgument);
        Process.Start(startInfo);

        for (var attempt = 0; attempt < 15; attempt++)
        {
            await Task.Delay(200);
            var response = await RuntimeIpc.GetStatusAsync();
            if (!response.Success)
                continue;

            _runtimeRunning = true;
            _runtimeStatusSnapshot = response.Status;
            _statusRuntime.Text = response.Status is { } status
                ? $"Runtime: Running · Outputs {status.Outputs.Count(output => output.IsActive)}/{status.Outputs.Length}"
                : "Runtime: Running";
            return true;
        }

        _runtimeRunning = null;
        _runtimeStatusSnapshot = null;
        _statusRuntime.Text = "Runtime: checking...";
        SetStatus("Runtime was launched, but IPC did not become ready in time.");
        return false;
    }

    private static bool IsRuntimeProcessPresent()
    {
        var processes = Process.GetProcessesByName("PinkieSysMon");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}

internal sealed record ScreenTargetCommandState(
    string TargetId,
    string TargetName,
    string StatusText,
    bool CanStartOrReload,
    bool CanStop);

internal sealed record ScreenMenuCommandState(
    ScreenTargetCommandState[] Targets,
    bool CanStartOrReloadAll,
    bool CanStopAll);

internal static class ScreenMenuContract
{
    public const string StartReloadText = "Start / Reload";
    public const string StopText = "Stop / Unload Output";
    public const string StartReloadAllText = "Start / Reload All";
    public const string StopAllText = "Stop / Unload All Outputs";

    public static bool IsTargetActive(RuntimeStatusSnapshot? runtimeStatus, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        return runtimeStatus?.Outputs.Any(output =>
            output.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase) && output.IsActive) == true;
    }

    public static bool AreAllTargetsActive(
        IReadOnlyList<OutputTargetConfig> targets,
        RuntimeStatusSnapshot? runtimeStatus)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            return false;

        var activeIds = runtimeStatus?.Outputs
            .Where(output => output.IsActive)
            .Select(output => output.TargetId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return targets.All(target => activeIds.Contains(target.Id));
    }

    public static ScreenMenuCommandState Build(
        IReadOnlyList<OutputTargetConfig> targets,
        bool? runtimeRunning,
        RuntimeStatusSnapshot? runtimeStatus,
        bool commandInProgress)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var statusByTarget = runtimeStatus?.Outputs.ToDictionary(
            output => output.TargetId,
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, RuntimeOutputStatus>(StringComparer.OrdinalIgnoreCase);

        var targetStates = targets.Select(target =>
        {
            statusByTarget.TryGetValue(target.Id, out var outputStatus);
            return new ScreenTargetCommandState(
                target.Id,
                target.Name,
                DescribeStatus(runtimeRunning, outputStatus),
                !commandInProgress,
                !commandInProgress && outputStatus?.IsActive == true);
        }).ToArray();

        return new ScreenMenuCommandState(
            targetStates,
            !commandInProgress && targetStates.Length > 0,
            !commandInProgress && targetStates.Any(target => target.CanStop));
    }

    private static string DescribeStatus(bool? runtimeRunning, RuntimeOutputStatus? status)
    {
        if (runtimeRunning == false)
            return "Status: Runtime not running";
        if (runtimeRunning is null)
            return "Status: Checking...";
        if (status is null)
            return "Status: Not loaded";
        if (!status.IsActive)
            return "Status: Stopped";
        if (status.IsConnected)
            return "Status: Connected";

        return status.UsbState.ToUpperInvariant() switch
        {
            "WAITING" => "Status: Waiting for device",
            "AMBIGUOUS" => "Status: Device selection required",
            "ERROR" => "Status: Output error",
            "SUSPENDED" => "Status: Suspended",
            "STOPPED" => "Status: Stopped",
            var state when state.Length > 0 => $"Status: {state}",
            _ => "Status: Unavailable"
        };
    }
}
