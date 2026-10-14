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
        ToolStripMenuItem StartReload);

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
        foreach (var item in _screenMenu.DropDownItems.Cast<ToolStripItem>().ToArray())
            item.Dispose();
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
            targetItem.DropDownItems.Add(statusItem);
            targetItem.DropDownItems.Add(new ToolStripSeparator());
            targetItem.DropDownItems.Add(startReloadItem);
            _screenMenu.DropDownItems.Add(targetItem);

            _screenTargetMenuItems[targetId] = new ScreenTargetMenuItems(
                targetItem,
                statusItem,
                startReloadItem);
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
        }

        _screenStartReloadAllMenuItem.Enabled = state.CanStartOrReloadAll;
        _screenStopAllMenuItem.Enabled = state.CanStopAll;
    }

    private Task ExecuteStartOrReloadTargetAsync(string targetId) => ExecuteEditorOutputStartAsync(targetId);

    private async Task ExecuteEditorOutputStartAsync(string? targetId)
    {
        if (_screenCommandInProgress || !ConfirmSaveBeforeDestructiveAction())
            return;
        var dashboard = _dashboardName;
        var actions = new EditorOutputStartActions(
            () => RuntimeIpc.GetStatusAsync(),
            IsRuntimeProcessPresent,
            () => EditorOutputStartWorkflow.FromConfiguration(AppConfig.Load(_appConfigPath, _log)),
            LaunchRuntimeForOutputCommandAsync,
            output =>
            {
                if (!dashboard.Equals(_dashboardName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The Editor dashboard changed while preparing Start / Reload. Retry the action.");
                return EditorDialogs.ChooseOutputDashboard(this, output, dashboard);
            },
            (id, board) => RuntimeIpc.AssignDashboardAsync(id, board),
            id => RuntimeIpc.StartOrReloadTargetAsync(id),
            () => RuntimeIpc.StartOrReloadAllAsync());
        await ExecuteOutputCommandAsync(
            () => EditorOutputStartWorkflow.RunAsync(dashboard, targetId, actions),
            dialogTitle: targetId is null ? "Reload All Outputs" : "Start / Reload Output",
            fallbackSuccessMessage: targetId is null ? "Common settings and enabled Outputs reloaded." : "Selected Output started/reloaded.");
    }

    private Task ExecuteStartOrReloadAllAsync() => ExecuteEditorOutputStartAsync(null);

    private async Task ExecuteStopAllOutputsAsync()
    {
        await ExecuteOutputCommandAsync(
            async () => await RuntimeIpc.StopAllOutputsAsync(),
            dialogTitle: "Stop All Outputs",
            fallbackSuccessMessage: "All output targets stopped and unloaded.",
            synchronizeRuntimeShutdown: true);
    }

    private async Task ExecuteOutputCommandAsync(
        Func<Task<RuntimeIpcResponse?>> command,
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
            var response = await command();
            if (response is null)
            {
                SetStatus("Start / Reload canceled.");
            }
            else if (response.Success)
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

    private async Task<RuntimeIpcStatusResponse> LaunchRuntimeForOutputCommandAsync()
    {
        var existing = await RuntimeIpc.GetStatusAsync();
        if (existing.Success)
        {
            _runtimeRunning = true;
            _runtimeStatusSnapshot = existing.Status;
            return existing;
        }

        if (IsRuntimeProcessPresent())
        {
            _runtimeRunning = null;
            _runtimeStatusSnapshot = null;
            _statusRuntime.Text = "Runtime: checking...";
            const string message = "A PinkieSysMon runtime process already exists, but its IPC endpoint is not responding. A duplicate runtime will not be started.";
            SetStatus(message);
            return new RuntimeIpcStatusResponse(false, message, null);
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
        using var launched = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Runtime could not be launched.");

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
            return response;
        }

        _runtimeRunning = null;
        _runtimeStatusSnapshot = null;
        _statusRuntime.Text = "Runtime: checking...";
        SetStatus("Runtime was launched, but IPC did not become ready in time.");
        return new RuntimeIpcStatusResponse(false, "Runtime was launched, but IPC did not become ready in time. Check Runtime logs.", null);
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
    bool CanStartOrReload);

internal sealed record ScreenMenuCommandState(
    ScreenTargetCommandState[] Targets,
    bool CanStartOrReloadAll,
    bool CanStopAll);

internal static class ScreenMenuContract
{
    public const string StartReloadText = "Start / Reload";
    public const string StartReloadAllText = "Reload All";
    public const string StopAllText = "Stop / Unload All Outputs";

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
            var enabled = outputStatus?.OutputEnabled ?? OutputTargetContract.IsEnabled(target);
            var description = DescribeStatus(runtimeRunning, outputStatus);
            if (!string.IsNullOrWhiteSpace(outputStatus?.Dashboard))
                description += $" · Dashboard: {outputStatus.Dashboard}";
            return new ScreenTargetCommandState(
                target.Id,
                target.Name,
                description,
                !commandInProgress && enabled);
        }).ToArray();

        return new ScreenMenuCommandState(
            targetStates,
            !commandInProgress && targetStates.Length > 0,
            !commandInProgress && runtimeStatus?.Outputs.Any(output => output.IsActive) == true);
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
            return status.OutputEnabled == false ? "Status: Disabled" : "Status: Not loaded";
        if (status.IsConnected)
            return status.Kind == OutputTargetContract.DesktopKind ? "Status: Displaying" : "Status: Connected";

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
