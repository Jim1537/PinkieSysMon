using System.Text.Json;
using System.Text.Json.Nodes;
using PinkieSysMon;
using SkiaSharp;
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
using CanonicalImageAssetPresentationDefinition = PinkieSysMon.DashboardModel.ImageAssetPresentationDefinition;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm : PinkieEditorForm
{
    private const int HistorySnapshotLimit = 128;
    private const long HistorySnapshotMemoryLimitBytes = 64L * 1024L * 1024L;

    private readonly string _root;
    private readonly FileLogger _log;
    private readonly AppConfig _appConfig;
    private readonly string _appConfigPath;
    private readonly WindowsSystemTelemetrySource _systemTelemetry;
    private readonly WindowsNetworkTelemetrySource _networkTelemetry;
    private readonly WindowsPowerTelemetrySource _powerTelemetry;
    private readonly WindowsMediaTelemetrySource _mediaTelemetry;
    private readonly LibreHardwareMonitorRawTelemetrySource _lhmTelemetry;
    private readonly IcueSensorLogTelemetrySource _icueTelemetry;

    private readonly SplitContainer _navigationSplit = new() { Dock = DockStyle.Fill, SplitterWidth = 6 };
    private readonly SplitContainer _workspaceSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6 };
    private readonly DashboardViewport _workspaceViewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private Bitmap? _canvasImage;
    private readonly LayerTreeView _widgetTree = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid _propertyGrid = new() { Dock = DockStyle.Fill, HelpVisible = false, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusDashboard = new();
    private readonly ToolStripStatusLabel _statusCanvas = new();
    private readonly ToolStripStatusLabel _statusZoom = new();
    private readonly ToolStripStatusLabel _statusSelected = new();
    private readonly ToolStripStatusLabel _statusRuntime = new();
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _runtimeStatusTimer = new() { Interval = 1000 };
    private readonly string _settingsPath;
    private readonly EditorSettings _settings;
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();

    private CanonicalDashboardDefinition? _definition;
    private DashboardPreviewRenderer? _preview;
    private readonly Dictionary<CanonicalWidgetDefinition, TreeNode> _widgetTreeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, TreeNode> _groupTreeNodes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, object?> _lastMetrics = new Dictionary<string, object?>();
    private IReadOnlySet<string> _previewRequiredMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string[] _previewSystemMetrics = [];
    private string[] _previewNetworkMetrics = [];
    private string[] _previewPowerMetrics = [];
    private string[] _previewMediaMetrics = [];
    private string[] _previewLhmMetrics = [];
    private string[] _previewIcueMetrics = [];
    private string _dashboardName = string.Empty;
    private string _dashboardPath = string.Empty;
    private float _zoom = 0.5f;
    private bool _fitZoom;
    private bool _loading;
    private bool _dirty;
    private string _savedJson = string.Empty;
    private CanonicalWidgetDefinition? _selectedWidget;
    private readonly HashSet<CanonicalWidgetDefinition> _selectedWidgets = new(ReferenceEqualityComparer.Instance);
    private bool _dragging;
    private PointF _dragStartLogical;
    private readonly Dictionary<CanonicalWidgetDefinition, (float X, float Y)> _dragOrigins = new(ReferenceEqualityComparer.Instance);
    private ResizeDragState? _resizeDrag;
    private EditorSnapshot? _dragUndoSnapshot;
    private DashboardEditorMetadata _dashboardMetadata = new();
    private string _dashboardMetadataPath = string.Empty;
    private bool _metadataSaveErrorShown;
    private TreeNode? _dragTreeNode;
    private readonly HashSet<string> _selectedTreeKeys = new(StringComparer.OrdinalIgnoreCase);
    private string? _treeSelectionAnchorKey;
    private string? _treePrimaryKey;
    private TreeDropTarget? _treeDropTarget;
    private bool _screenCommandInProgress;
    private bool _runtimeStatusProbeInProgress;
    private int _runtimeStateGeneration;
    private bool _gridEnabled;
    private int _gridStep = 25;
    private int _toolbarIconSize = 32;
    private int _scrollRestoreGeneration;
    private int _propertyViewRefreshGeneration;
    private bool _formShown;
    private DashboardWheelMessageFilter? _wheelFilter;

    private static readonly float[] ZoomLevels = [0.25f, 0.33f, 0.50f, 0.67f, 0.75f, 1.00f, 1.25f, 1.50f, 1.75f, 2.00f];

    private sealed record EditorSnapshot(
        string Json,
        int[] SelectedWidgetIndices,
        string EditorMetadataJson,
        string[] SelectedTreeKeys,
        string? TreePrimaryKey,
        string? TreeAnchorKey);
    private sealed record TreeDropTarget(string? ParentGroupId, int Index, bool IntoGroup);
    private sealed record ResizeDragState(
        CanonicalWidgetDefinition Widget,
        PointF StartLogical,
        float OriginX,
        float OriginY,
        float InitialWidth,
        float InitialHeight,
        PointF FixedCorner,
        double Rotation,
        bool WasAutoWidth);

    private sealed class WidgetsRootTag
    {
        public static readonly WidgetsRootTag Instance = new();
        private WidgetsRootTag() { }
    }

    private sealed class DashboardViewport : Panel
    {
        protected override CreateParams CreateParams
        {
            get
            {
                SetStyle(ControlStyles.ApplyThemingImplicitly, true);
                return base.CreateParams;
            }
        }

        public DashboardViewport()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }
    }

    private sealed class DashboardWheelMessageFilter(EditorForm owner) : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            // This filter is application-wide. Only hijack wheel input while the editor
            // itself is the active, enabled top-level form. Modal/modeless child windows
            // must receive their own wheel messages instead of scrolling the dashboard
            // underneath them.
            if (m.Msg != WmMouseWheel ||
                !owner.IsHandleCreated ||
                !owner.Enabled ||
                !ReferenceEquals(Form.ActiveForm, owner))
            {
                return false;
            }

            var screenRect = owner._workspaceViewport.RectangleToScreen(owner._workspaceViewport.ClientRectangle);
            if (!screenRect.Contains(Cursor.Position))
                return false;

            var delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
            owner.HandleDashboardWheel(delta);
            return true;
        }
    }

    public EditorForm(string root, string initialDashboard)
    {
        _root = root;
        _log = new FileLogger(Path.Combine(root, "logs", "PinkieSysMon.Editor.log"));
        _appConfigPath = Path.Combine(root, "config", "app.json");
        _appConfig = AppConfig.Load(_appConfigPath, _log);
        MetricSelectorEnvironment.Configure(_root, _log, () => _appConfig);
        _systemTelemetry = new WindowsSystemTelemetrySource(_log);
        _networkTelemetry = new WindowsNetworkTelemetrySource(_log);
        _powerTelemetry = new WindowsPowerTelemetrySource(_log);
        _mediaTelemetry = new WindowsMediaTelemetrySource(_log);
        _lhmTelemetry = new LibreHardwareMonitorRawTelemetrySource(_log);
        _icueTelemetry = new IcueSensorLogTelemetrySource(_root, _log);
        _mediaTelemetry.SetEndpointTypeOverrides(_appConfig.Media.EndpointTypeOverrides);
        _settingsPath = Path.Combine(root, "config", "editor.json");
        _settings = EditorSettings.Load(_settingsPath);
        _themeMode = EditorThemeContract.Parse(_settings.Theme);
        _settings.Theme = EditorThemeContract.Serialize(_themeMode);
        _gridEnabled = _settings.GridEnabled;
        _gridStep = _settings.GridStep is 10 or 25 or 50 or 100 ? _settings.GridStep : 25;
        _toolbarIconSize = _settings.ToolbarIconSize is 16 or 32 or 48 ? _settings.ToolbarIconSize : 32;

        Text = "PinkieSysMon Dashboard Editor";
        FormBorderStyle = FormBorderStyle.Sizable;
        ControlBox = true;
        MinimizeBox = true;
        MaximizeBox = true;
        Width = 1500;
        Height = 850;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        RestoreWindowSettings();
        InitializeWindowChrome();

        BuildUi();
        RefreshEditorShellTheme();
        _previewTimer.Tick += (_, _) => RenderPreview();
        _wheelFilter = new DashboardWheelMessageFilter(this);
        Application.AddMessageFilter(_wheelFilter);
        LoadInitialDashboard(initialDashboard);
        UpdatePreviewTimerState();

        _runtimeStatusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _runtimeStatusTimer.Start();

        FormClosing += OnFormClosing;
        FormClosed += (_, _) => LaunchRestartedEditor();
        Resize += (_, _) =>
        {
            if (_fitZoom)
                ApplyZoom();
            UpdatePreviewTimerState();
        };
        Shown += (_, _) =>
        {
            _formShown = true;
            RestorePanelWidths();
            RestoreDashboardViewState();
            SchedulePropertyGridPresentation();
            _ = RefreshRuntimeStatusAsync();
        };
    }

    private void BuildUi()
    {

        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("New Dashboard...", null, (_, _) => ExecuteNewDashboardCommand()) { ShortcutKeys = Keys.Control | Keys.N });
        _openDashboardMenuItem = new ToolStripMenuItem("Open Dashboard...", null, (_, _) => ExecuteOpenDashboardCommand()) { ShortcutKeys = Keys.Control | Keys.O };
        fileMenu.DropDownItems.Add(_openDashboardMenuItem);
        _cloneDashboardMenuItem = new ToolStripMenuItem("Clone Dashboard...", null, (_, _) => ExecuteCloneDashboardCommand());
        fileMenu.DropDownItems.Add(_cloneDashboardMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _saveMenuItem = new ToolStripMenuItem("Save", null, (_, _) => ExecuteSaveCommand()) { ShortcutKeys = Keys.Control | Keys.S };
        fileMenu.DropDownItems.Add(_saveMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()));
        fileMenu.DropDownOpening += (_, _) => UpdateCommandStates();

        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        _undoMenuItem = new ToolStripMenuItem("Undo", null, (_, _) => ExecuteUndoCommand()) { ShortcutKeys = Keys.Control | Keys.Z };
        _redoMenuItem = new ToolStripMenuItem("Redo", null, (_, _) => ExecuteRedoCommand()) { ShortcutKeys = Keys.Control | Keys.Y, ShortcutKeyDisplayString = "Ctrl+Y / Ctrl+Shift+Z" };
        editMenu.DropDownItems.Add(_undoMenuItem);
        editMenu.DropDownItems.Add(_redoMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());

        _reloadEditorMenuItem = new ToolStripMenuItem("Reload Editor", null, (_, _) => ExecuteReloadEditorCommand()) { ShortcutKeys = Keys.F5 };
        editMenu.DropDownItems.Add(_reloadEditorMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());

        _addWidgetMenuItem = new ToolStripMenuItem("Add Widget");
        foreach (var type in WidgetTypeContract.EditorAddableTypes)
        {
            var captured = type;
            _addWidgetMenuItem.DropDownItems.Add(new ToolStripMenuItem(WidgetTypeDisplayName(type), null, (_, _) => ExecuteAddWidgetCommand(captured)));
        }
        editMenu.DropDownItems.Add(_addWidgetMenuItem);

        _renameMenuItem = new ToolStripMenuItem("Rename", null, (_, _) => ExecuteRenameCommand()) { ShortcutKeys = Keys.F2 };
        _duplicateMenuItem = new ToolStripMenuItem("Duplicate", null, (_, _) => ExecuteDuplicateCommand()) { ShortcutKeys = Keys.Control | Keys.D };
        _deleteMenuItem = new ToolStripMenuItem("Delete", null, (_, _) => ExecuteDeleteCommand()) { ShortcutKeyDisplayString = "Del" };
        editMenu.DropDownItems.Add(_renameMenuItem);
        editMenu.DropDownItems.Add(_duplicateMenuItem);
        editMenu.DropDownItems.Add(_deleteMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());

        _groupSelectedMenuItem = new ToolStripMenuItem("Group Selected...", null, (_, _) => ExecuteGroupSelectedCommand());
        _ungroupSelectedMenuItem = new ToolStripMenuItem("Ungroup Selected", null, (_, _) => ExecuteUngroupSelectedCommand());
        editMenu.DropDownItems.Add(_groupSelectedMenuItem);
        editMenu.DropDownItems.Add(_ungroupSelectedMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());

        _moveUpMenuItem = new ToolStripMenuItem("Move Up", null, (_, _) => ExecuteMoveSelectedCommand(-1));
        _moveDownMenuItem = new ToolStripMenuItem("Move Down", null, (_, _) => ExecuteMoveSelectedCommand(1));
        _bringToFrontMenuItem = new ToolStripMenuItem("Bring to Front", null, (_, _) => ExecuteMoveSelectedToEdgeCommand(front: true));
        _sendToBackMenuItem = new ToolStripMenuItem("Send to Back", null, (_, _) => ExecuteMoveSelectedToEdgeCommand(front: false));
        editMenu.DropDownItems.Add(_moveUpMenuItem);
        editMenu.DropDownItems.Add(_moveDownMenuItem);
        editMenu.DropDownItems.Add(_bringToFrontMenuItem);
        editMenu.DropDownItems.Add(_sendToBackMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings()));
        editMenu.DropDownOpening += (_, _) => UpdateCommandStates();

        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        _gridToolsMenu = new ToolStripMenuItem("Grid");
        PopulateGridToolsMenu();
        _gridToolsMenu.DropDownOpening += (_, _) => PopulateGridToolsMenu();
        toolsMenu.DropDownItems.Add(_gridToolsMenu);

        _zoomToolsMenu = new ToolStripMenuItem("Zoom");
        _zoomInMenuItem = new ToolStripMenuItem("Zoom In", null, (_, _) => ExecuteZoomStepCommand(1));
        _zoomOutMenuItem = new ToolStripMenuItem("Zoom Out", null, (_, _) => ExecuteZoomStepCommand(-1));
        _zoomFitHeightMenuItem = new ToolStripMenuItem("Fit Height", null, (_, _) => ExecuteFitHeightCommand());
        _zoomToolsMenu.DropDownItems.Add(_zoomInMenuItem);
        _zoomToolsMenu.DropDownItems.Add(_zoomOutMenuItem);
        _zoomToolsMenu.DropDownItems.Add(new ToolStripSeparator());
        _zoomToolsMenu.DropDownItems.Add(_zoomFitHeightMenuItem);
        _zoomToolsMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var percent in new[] { 25, 33, 50, 67, 75, 100, 125, 150, 175, 200 })
        {
            var captured = percent;
            var item = new ToolStripMenuItem($"{percent}%", null, (_, _) => ExecuteSetZoomPercentCommand(captured));
            if (percent == 100)
                _zoom100MenuItem = item;
            _zoomToolsMenu.DropDownItems.Add(item);
        }
        _zoomToolsMenu.DropDownOpening += (_, _) => UpdateCommandStates();
        toolsMenu.DropDownItems.Add(_zoomToolsMenu);
        toolsMenu.DropDownItems.Add(new ToolStripSeparator());
        _dashboardSnapshotMenuItem = new ToolStripMenuItem("Dashboard Snapshot...", null, (_, _) => ExecuteDashboardSnapshotCommand());
        toolsMenu.DropDownItems.Add(_dashboardSnapshotMenuItem);
        toolsMenu.DropDownOpening += (_, _) => UpdateCommandStates();

        _screenMenu = new ToolStripMenuItem(EditorMainMenuContract.OutputMenuText) { Alignment = ToolStripItemAlignment.Right };
        RebuildScreenMenu();
        _screenMenu.DropDownOpening += (_, _) =>
        {
            RefreshScreenTargetsFromConfiguration();
            UpdateCommandStates();
            _ = RefreshRuntimeStatusAsync();
        };

        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));

        EditorMainMenuContract.Populate(_mainMenuStrip, fileMenu, editMenu, toolsMenu, helpMenu, _screenMenu);
        _mainMenuStrip.ApplyTheme(_shellTheme);
        MainMenuStrip = _mainMenuStrip;

        var tool = BuildToolbar();

        _navigationSplit.Panel1.Controls.Add(_widgetTree);
        _navigationSplit.Panel2.Controls.Add(_workspaceSplit);
        _workspaceSplit.Panel1.Controls.Add(_workspaceViewport);
        InitializePropertyPane();
        _workspaceSplit.Panel2.Controls.Add(_propertyTabs);

        _workspaceViewport.Paint += CanvasPaint;
        _workspaceViewport.Enter += (_, _) => TrackEditorInputSurface(EditorInputSurface.Canvas);
        _workspaceViewport.MouseDown += CanvasMouseDown;
        _workspaceViewport.MouseMove += CanvasMouseMove;
        _workspaceViewport.MouseUp += CanvasMouseUp;
        _workspaceViewport.MouseDoubleClick += (_, e) => SelectAt(e.Location);
        _workspaceViewport.Scroll += (_, _) => _workspaceViewport.Invalidate();

        _widgetTree.LabelEdit = false;
        _widgetTree.AllowDrop = true;
        _widgetTree.BeforeSelect += TreeBeforeSelect;
        _widgetTree.AfterSelect += (_, e) =>
        {
            if (!_loading && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0 && e.Node is { } node)
                SelectTreeObject(node.Tag);
        };
        _widgetTree.ItemDrag += TreeItemDrag;
        _widgetTree.DragEnter += TreeDragEnter;
        _widgetTree.DragOver += TreeDragOver;
        _widgetTree.DragDrop += TreeDragDrop;
        _widgetTree.DragLeave += (_, _) => ClearTreeDropMarker();

        var treeMenu = new ContextMenuStrip();
        _treeRenameMenuItem = new ToolStripMenuItem("Rename", null, (_, _) => ExecuteRenameCommand());
        _treeDuplicateMenuItem = new ToolStripMenuItem("Duplicate", null, (_, _) => ExecuteDuplicateCommand());
        _treeDeleteMenuItem = new ToolStripMenuItem("Delete", null, (_, _) => ExecuteDeleteCommand());
        _treeMoveUpMenuItem = new ToolStripMenuItem("Move Up", null, (_, _) => ExecuteMoveSelectedCommand(-1));
        _treeMoveDownMenuItem = new ToolStripMenuItem("Move Down", null, (_, _) => ExecuteMoveSelectedCommand(1));
        _treeBringToFrontMenuItem = new ToolStripMenuItem("Bring to Front", null, (_, _) => ExecuteMoveSelectedToEdgeCommand(front: true));
        _treeSendToBackMenuItem = new ToolStripMenuItem("Send to Back", null, (_, _) => ExecuteMoveSelectedToEdgeCommand(front: false));
        _treeGroupSelectedMenuItem = new ToolStripMenuItem("Group Selected...", null, (_, _) => ExecuteGroupSelectedCommand());
        _treeUngroupSelectedMenuItem = new ToolStripMenuItem("Ungroup Selected", null, (_, _) => ExecuteUngroupSelectedCommand());

        treeMenu.Items.Add(_treeRenameMenuItem);
        treeMenu.Items.Add(_treeDuplicateMenuItem);
        treeMenu.Items.Add(_treeDeleteMenuItem);
        treeMenu.Items.Add(new ToolStripSeparator());
        treeMenu.Items.Add(_treeMoveUpMenuItem);
        treeMenu.Items.Add(_treeMoveDownMenuItem);
        treeMenu.Items.Add(_treeBringToFrontMenuItem);
        treeMenu.Items.Add(_treeSendToBackMenuItem);
        treeMenu.Items.Add(new ToolStripSeparator());
        treeMenu.Items.Add(_treeGroupSelectedMenuItem);
        treeMenu.Items.Add(_treeUngroupSelectedMenuItem);
        treeMenu.Opening += (_, _) => UpdateCommandStates();
        _widgetTree.ContextMenuStrip = treeMenu;
        _widgetTree.NodeMouseClick += TreeNodeMouseClick;
        _widgetTree.PointerActivating += (_, _) => TrackEditorInputSurface(EditorInputSurface.Layers);
        _widgetTree.Enter += (_, _) => TrackEditorInputSurface(EditorInputSurface.Layers);
        _propertyGrid.Enter += (_, _) => TrackEditorInputSurface(EditorInputSurface.Properties);
        _propertyGrid.MouseDown += (_, _) => TrackEditorInputSurface(EditorInputSurface.Properties);
        _propertyGrid.PropertyValueChanged += (_, e) =>
        {
            RememberPresentedPropertyExpansionState();
            OnPropertyChanged(e.ChangedItem?.PropertyDescriptor?.Name);
            SchedulePropertyGridPresentation();
        };

        _status.Items.AddRange([
            _statusDashboard,
            new ToolStripStatusLabel("|"),
            _statusCanvas,
            new ToolStripStatusLabel("|"),
            _statusZoom,
            new ToolStripStatusLabel("|"),
            _statusSelected,
            new ToolStripStatusLabel("|"),
            _statusRuntime,
            _statusText
        ]);

        Controls.Add(_navigationSplit);
        Controls.Add(_status);
        Controls.Add(tool);
        _titleBarHost.Controls.Add(_mainMenuStrip);
        Controls.Add(_titleBarHost);
        LayoutWindowChromeMenu();
        UpdateCommandStates();
    }

    private void LoadInitialDashboard(string initial)
    {
        var dashboards = DashboardCatalog.Discover(_root);
        if (dashboards.Count == 0)
            throw new InvalidDataException("No dashboards were found.");

        var name = dashboards.FirstOrDefault(x => x.Equals(initial, StringComparison.OrdinalIgnoreCase)) ?? dashboards[0];
        LoadDashboard(name);
    }

    private void NewDashboard()
    {
        if (!ConfirmSaveBeforeDestructiveAction())
            return;

        using var dialog = new NewDashboardDialog();
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = dialog.DashboardName;
            var validationError = ValidateDashboardName(name);
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "New Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show($"Dashboard '{existing}' already exists.", "New Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialog.DialogResult = DialogResult.None;
                continue;
            }

            var directory = Path.Combine(dashboardsRoot, name);
            try
            {
                SaveCurrentDashboardViewState();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "dashboard.json");
                var definition = new CanonicalDashboardDefinition
                {
                    SchemaVersion = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
                    BaseDirectory = directory,
                    Canvas = new CanonicalCanvasDefinition
                    {
                        Width = FrameGeometry.DefaultNativeWidth,
                        Height = FrameGeometry.DefaultNativeHeight,
                        Orientation = dialog.Orientation,
                        BackgroundColor = "#000000"
                    }
                };
                File.WriteAllText(path, DashboardJson.Serialize(definition, indented: true) + Environment.NewLine);
                LoadDashboard(name);
                SetStatus($"Created dashboard '{name}'.");
                return;
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch
                {
                }

                MessageBox.Show($"Could not create dashboard.\n\n{ex.Message}", "New Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }

    private void OpenDashboard()
    {
        var dashboards = DashboardCatalog.Discover(_root);
        if (dashboards.Count == 0)
        {
            MessageBox.Show("No dashboards were found.", "Open Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenDashboardDialog(dashboards, _dashboardName);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedDashboard))
            return;

        var name = dialog.SelectedDashboard;
        if (name.Equals(_dashboardName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!ConfirmSaveBeforeDestructiveAction())
            return;

        SaveCurrentDashboardViewState();
        LoadDashboard(name);
    }

    private bool ConfirmSaveBeforeDestructiveAction()
    {
        if (!_dirty)
            return true;

        return EditorDialogs.ConfirmSaveChanges(this, _dashboardName) switch
        {
            SaveChangesChoice.Save => SaveDashboard(),
            SaveChangesChoice.DontSave => true,
            _ => false
        };
    }

    private string? ValidateDashboardName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Dashboard name must not be empty.";
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            return "Dashboard name must not start or end with spaces.";
        if (name.Length > 255)
            return "Dashboard name is too long for a Windows directory name.";

        switch (WindowsPathComponentValidator.Validate(name))
        {
            case WindowsPathComponentValidationError.DotSegment:
                return "Dashboard name is not valid.";
            case WindowsPathComponentValidationError.TrailingSpaceOrPeriod:
                return "Dashboard name must not end with a period or space.";
            case WindowsPathComponentValidationError.InvalidCharacter:
                return "Dashboard name contains characters that are not allowed in Windows file names: < > : \" / \\ | ? *";
            case WindowsPathComponentValidationError.ReservedDeviceName:
                return $"'{name}' is a reserved Windows device name.";
            case WindowsPathComponentValidationError.Empty:
                return "Dashboard name must not be empty.";
        }

        try
        {
            _ = Path.GetFullPath(Path.Combine(DashboardCatalog.GetRoot(_root), name));
        }
        catch (Exception ex)
        {
            return $"Dashboard name is not valid: {ex.Message}";
        }
        return null;
    }

    private async Task RefreshRuntimeStatusAsync()
    {
        if (_runtimeStatusProbeInProgress || _screenCommandInProgress || IsDisposed)
            return;

        var generation = _runtimeStateGeneration;
        _runtimeStatusProbeInProgress = true;
        try
        {
            var response = await RuntimeIpc.GetStatusAsync();
            if (!IsDisposed && !_screenCommandInProgress && generation == _runtimeStateGeneration)
            {
                _runtimeRunning = response.Success;
                _runtimeStatusSnapshot = response.Status;
                if (response.Success && _runtimeStatusSnapshot is { } status)
                {
                    var active = status.Outputs.Count(output => output.IsActive);
                    _statusRuntime.Text = $"Runtime: Running · Outputs {active}/{status.Outputs.Length}";
                }
                else if (IsRuntimeProcessPresent())
                {
                    _runtimeRunning = null;
                    _runtimeStatusSnapshot = null;
                    _statusRuntime.Text = "Runtime: process running · IPC unavailable";
                }
                else
                {
                    _statusRuntime.Text = "Runtime: Not running";
                }
            }
        }
        catch
        {
            if (!IsDisposed && !_screenCommandInProgress && generation == _runtimeStateGeneration)
            {
                if (IsRuntimeProcessPresent())
                {
                    _runtimeRunning = null;
                    _runtimeStatusSnapshot = null;
                    _statusRuntime.Text = "Runtime: process running · IPC unavailable";
                }
                else
                {
                    _runtimeRunning = false;
                    _runtimeStatusSnapshot = null;
                    _statusRuntime.Text = "Runtime: Not running";
                }
            }
        }
        finally
        {
            _runtimeStatusProbeInProgress = false;
            if (!IsDisposed && !_screenCommandInProgress)
                UpdateCommandStates();
        }
    }

    private void ShowSettings()
    {
        bool startupCurrent;
        AppConfig runtimeConfig;
        IReadOnlyList<OutputDeviceDescriptor> outputDevices;
        var appConfigPath = Path.Combine(_root, "config", "app.json");

        try
        {
            startupCurrent = StartupTaskManager.IsEnabled();
            runtimeConfig = AppConfig.Load(appConfigPath, _log);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read current settings.\n\n{ex.Message}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            outputDevices = DeviceDiscovery.EnumerateTrofeoDevices();
        }
        catch (Exception ex)
        {
            _log.Warn("Output-device discovery failed while opening Settings", ex);
            outputDevices = Array.Empty<OutputDeviceDescriptor>();
        }

        var outputTarget = OutputTargetContract.GetPrimary(runtimeConfig);
        using var dialog = new EditorSettingsDialog(
            startupCurrent,
            MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.System),
            MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.LibreHardwareMonitor),
            MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.Icue),
            runtimeConfig.Display.RefreshIntervalMs,
            outputTarget.DeviceId,
            outputDevices,
            runtimeConfig.Renderer.JpegQuality,
            _toolbarIconSize,
            _themeMode);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var startupChanged = dialog.StartRuntimeWithWindows != startupCurrent;
        var providersChanged =
            dialog.SystemProviderEnabled != MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.System) ||
            dialog.LibreHardwareMonitorProviderEnabled != MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.LibreHardwareMonitor) ||
            dialog.IcueProviderEnabled != MetricProviderContract.IsEnabled(runtimeConfig.MetricProviders, MetricProviderContract.Icue);
        var outputDeviceChanged =
            !dialog.OutputDeviceId.Equals(outputTarget.DeviceId, StringComparison.OrdinalIgnoreCase);
        var runtimeChanged =
            dialog.RefreshIntervalMs != runtimeConfig.Display.RefreshIntervalMs ||
            dialog.JpegQuality != runtimeConfig.Renderer.JpegQuality ||
            outputDeviceChanged ||
            providersChanged;
        var interfaceChanged = dialog.ToolbarIconSize != _toolbarIconSize;
        var themeChanged = dialog.ThemeMode != _themeMode;

        if (!startupChanged && !runtimeChanged && !interfaceChanged && !themeChanged)
            return;

        try
        {
            if (runtimeChanged)
            {
                runtimeConfig.Display.RefreshIntervalMs = dialog.RefreshIntervalMs;
                outputTarget.DeviceId = dialog.OutputDeviceId;
                runtimeConfig.Renderer.JpegQuality = dialog.JpegQuality;
                runtimeConfig.MetricProviders[MetricProviderContract.System] = dialog.SystemProviderEnabled;
                runtimeConfig.MetricProviders[MetricProviderContract.LibreHardwareMonitor] = dialog.LibreHardwareMonitorProviderEnabled;
                runtimeConfig.MetricProviders[MetricProviderContract.Icue] = dialog.IcueProviderEnabled;
                runtimeConfig.Save(appConfigPath);

                // Keep the editor-side snapshot consistent with the file it just wrote.
                _appConfig.Display.RefreshIntervalMs = runtimeConfig.Display.RefreshIntervalMs;
                OutputTargetContract.GetPrimary(_appConfig).DeviceId = outputTarget.DeviceId;
                _appConfig.Renderer.JpegQuality = runtimeConfig.Renderer.JpegQuality;
                _appConfig.MetricProviders = MetricProviderContract.Normalize(runtimeConfig.MetricProviders);
            }

            if (startupChanged)
            {
                var runtimePath = Path.Combine(_root, "bin", "PinkieSysMon.exe");
                StartupTaskManager.SetEnabled(dialog.StartRuntimeWithWindows, runtimePath);
            }

            if (interfaceChanged)
                ExecuteSetToolbarIconSizeCommand(dialog.ToolbarIconSize);

            if (themeChanged)
            {
                _themeMode = dialog.ThemeMode;
                _settings.Theme = EditorThemeContract.Serialize(dialog.ThemeMode);
            }

            if (runtimeChanged || startupChanged || themeChanged)
                SaveEditorSettings();

            if (providersChanged)
            {
                RefreshPreviewTelemetry();
                RenderPreview(force: true);
            }

            if (themeChanged)
            {
                var restart = MessageBox.Show(
                    $"Editor theme '{EditorThemeContract.Serialize(_themeMode)}' was saved.\n\n" +
                    "WinForms applies its color mode when UI controls are created. Restart the Editor now to apply the theme consistently?",
                    "Editor Theme",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (restart == DialogResult.Yes)
                {
                    RequestEditorRestartForTheme();
                    return;
                }

                SetStatus($"Editor theme: {EditorThemeContract.Serialize(_themeMode)} · restart required.");
            }
            else if (runtimeChanged)
            {
                SetStatus("Settings saved. Use the target device under Output → Start / Reload to apply runtime settings.");
            }
            else if (startupChanged)
            {
                SetStatus(dialog.StartRuntimeWithWindows
                    ? "PinkieSysMon runtime will start with Windows."
                    : "PinkieSysMon runtime startup entry removed.");
            }
            else
            {
                SetStatus($"Toolbar icon size: {_toolbarIconSize} × {_toolbarIconSize}.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowAbout()
    {
        var version = typeof(EditorForm).Assembly.GetName().Version?.ToString() ?? "unknown";
        MessageBox.Show(
            $"PinkieSysMon Dashboard Editor\nVersion {version}\n\nDirect Skia dashboard editor for the PinkieSysMon Trofeo display runtime.",
            "About PinkieSysMon Editor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateStatusBar()
    {
        _statusDashboard.Text = string.IsNullOrWhiteSpace(_dashboardName) ? "Dashboard: —" : $"Dashboard: {_dashboardName}";
        _statusCanvas.Text = _preview is null ? "Canvas: —" : $"Canvas: {_preview.Width}×{_preview.Height}";
        _statusZoom.Text = $"Zoom: {(int)Math.Round(_zoom * 100)}%";
        _statusSelected.Text = $"Selected: {_selectedWidgets.Count}";
        if (string.IsNullOrWhiteSpace(_statusRuntime.Text))
            _statusRuntime.Text = "Runtime: checking...";
        UpdateCommandStates();
    }

    private void LoadDashboard(string name)
    {
        try
        {
            var path = DashboardCatalog.GetDefinitionPath(_root, name);
            var text = File.ReadAllText(path);
            var sourceRoot = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) as JsonObject ?? throw new InvalidDataException("dashboard.json root must be a JSON object.");
            var sourceSchemaVersion = sourceRoot["schemaVersion"]?.GetValue<int>() ?? 0;
            var definition = CanonicalDashboardDefinition.Parse(text, path);
            var metadataPath = Path.Combine(Path.GetDirectoryName(path)!, ".editor.json");
            var metadata = DashboardEditorMetadata.Load(
                metadataPath,
                out var metadataLoadError,
                out var invalidMetadataBackupPath);

            var textOverflowAdjusted = NormalizePreparedTextOverflowState(definition);
            var literalNumericAdjusted = NormalizePreparedLiteralNumericState(definition);

            var hierarchy = new EditorLayerHierarchy(definition, metadata);
            var hierarchicalOrder = hierarchy.FlattenWidgets();
            var currentOrder = definition.Widgets
                .Select((widget, index) => new { widget, index })
                .OrderBy(x => x.widget.Z)
                .ThenBy(x => x.index)
                .Select(x => x.widget)
                .ToArray();
            var hierarchyAdjusted = !currentOrder.SequenceEqual(hierarchicalOrder);
            if (hierarchyAdjusted)
                NormalizePreparedLayerOrder(definition, hierarchicalOrder);

            ReplacePreviewRenderer(definition);

            _definition = definition;
            _dashboardName = name;
            _dashboardPath = path;
            _dashboardMetadataPath = metadataPath;
            _dashboardMetadata = metadata;
            _metadataSaveErrorShown = false;
            var schemaMigrated = sourceSchemaVersion != DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;
            _savedJson = schemaMigrated ? string.Empty : DashboardJson.Serialize(definition, indented: false);
            _dirty = schemaMigrated || hierarchyAdjusted || textOverflowAdjusted || literalNumericAdjusted;
            _selectedWidget = null;
            _selectedWidgets.Clear();
            _selectedTreeKeys.Clear();
            _treeSelectionAnchorKey = null;
            _treePrimaryKey = null;
            ResetEditorInputSurface();
            _undo.Clear();
            _redo.Clear();

            _widgetTreeNodes.Clear();
            _groupTreeNodes.Clear();

            PopulateTree();
            RestoreDashboardViewState();
            RefreshPreviewTelemetry();
            RenderPreview(force: true);
            UpdateTitle();
            UpdateStatusBar();
            var normalizationParts = new List<string>();
            if (schemaMigrated)
                normalizationParts.Add($"schema {sourceSchemaVersion}→{DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion}");
            if (hierarchyAdjusted)
                normalizationParts.Add("layer Z-order");
            if (textOverflowAdjusted)
                normalizationParts.Add("text overflow state");
            if (literalNumericAdjusted)
                normalizationParts.Add("literal numeric data state");
            SetStatus(normalizationParts.Count > 0
                ? $"Loaded {name}; editor-normalized {string.Join(", ", normalizationParts)}."
                : $"Loaded {name}. Ctrl/Shift select layer nodes; drag groups or widgets to reorder and reparent them.");

            if (metadataLoadError is not null)
            {
                var backupNote = invalidMetadataBackupPath is null
                    ? "The invalid metadata file could not be backed up."
                    : $"A backup was preserved as '{Path.GetFileName(invalidMetadataBackupPath)}'.";
                var warning =
                    $"Editor metadata for dashboard '{name}' could not be loaded. " +
                    $"The dashboard itself is intact; group metadata was reset in memory. {backupNote}";
                _log.Warn(warning, metadataLoadError);
                MessageBox.Show(
                    warning + $"\n\n{metadataLoadError.Message}",
                    "PinkieSysMon Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Could not load dashboard '{name}'", ex);
            MessageBox.Show($"Could not load dashboard '{name}'.\n\n{ex.Message}", "PinkieSysMon Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadDashboard()
    {
        if (string.IsNullOrWhiteSpace(_dashboardName) || !ConfirmSaveBeforeDestructiveAction())
            return;

        SaveCurrentDashboardViewState();
        LoadDashboard(_dashboardName);
    }

    private void OnPropertyChanged(string? propertyName)
    {
        if (_definition is null || string.IsNullOrWhiteSpace(propertyName))
            return;

        EditorSnapshot? rollbackSnapshot = null;
        var rollbackOnFailure = false;
        try
        {
            if (propertyName.StartsWith(EndpointOverridePropertyDescriptor.PropertyPrefix, StringComparison.Ordinal))
            {
                _appConfig.Save(_appConfigPath);
                _mediaTelemetry.SetEndpointTypeOverrides(_appConfig.Media.EndpointTypeOverrides);
                RefreshPreviewTelemetry();
                RenderPreview(force: true);
                SetStatus("Media endpoint override saved.");
                return;
            }

            var undoBefore = CaptureSnapshot();
            rollbackSnapshot = undoBefore;

            IReadOnlyList<CanonicalWidgetDefinition>? widgets = _propertyGrid.SelectedObject switch
            {
                EditorPropertyView { Target: CanonicalWidgetDefinition widget } => [widget],
                EditorMultiPropertyView multi => multi.Targets,
                _ => null
            };

            var handled = false;
            var widgetPreviewNeedsRebuild = false;
            var refreshPropertyView = false;
            var canvasPropertyViewNeedsRefresh = false;

            if (widgets is not null && widgets.Count > 0)
            {
                handled = true;
                rollbackOnFailure = widgets.Count > 1;

                foreach (var widget in widgets)
                {
                    widgetPreviewNeedsRebuild = true;
                    refreshPropertyView |= ApplyWidgetPropertyChange(widget, propertyName);

                    if (_widgetTreeNodes.TryGetValue(widget, out var treeNode))
                        treeNode.Text = WidgetCaption(widget);
                }

                if (propertyName.Equals("Z", StringComparison.OrdinalIgnoreCase))
                {
                    var hierarchy = new EditorLayerHierarchy(_definition, _dashboardMetadata);
                    NormalizeLayerOrder(hierarchy.FlattenWidgets());
                    PopulateTree();
                }

                if (refreshPropertyView)
                    RefreshWidgetPropertySelectionDeferred();
            }
            else if (_propertyGrid.SelectedObject is EditorPropertyView canvasView &&
                     canvasView.Target is CanonicalCanvasDefinition canvas)
            {
                handled = true;
                rollbackOnFailure = true;

                canvasPropertyViewNeedsRefresh =
                    propertyName is nameof(CanonicalImageAssetPresentationDefinition.SourceType)
                        or nameof(CanonicalImageAssetPresentationDefinition.Source);

                RebuildPreviewRenderer();
            }

            if (!handled)
                return;

            if (widgetPreviewNeedsRebuild)
                RebuildPreviewRenderer();

            if (canvasPropertyViewNeedsRefresh && _definition is not null)
                RefreshCanvasPropertyViewDeferred(_definition.Canvas);

            PushUndo(undoBefore);
            MarkDirty();
            RenderPreview(force: true);
        }
        catch (Exception ex)
        {
            if (rollbackOnFailure && rollbackSnapshot is not null)
            {
                try
                {
                    RestoreSnapshot(rollbackSnapshot);
                }
                catch (Exception rollbackEx)
                {
                    _log.Error("Bulk property edit rollback failed", rollbackEx);
                }
            }

            SetStatus($"Property change error: {ex.Message}");
        }
    }

    private bool ApplyWidgetPropertyChange(
        CanonicalWidgetDefinition widget,
        string propertyName)
    {
        var refreshPropertyView = false;

        if (propertyName.StartsWith(StateVisualProfilePropertyDescriptor.PropertyPrefix, StringComparison.Ordinal))
        {
            if (widget is CanonicalStateVisualWidgetDefinition stateWidget)
                TextOverflowStateContract.EnsureProfiles(stateWidget);

            if (TryParseStateProfileProperty(propertyName, out var stateKey, out var stateField))
            {
                if (stateField == StateVisualProfileField.SourceType)
                {
                    ReconcileTextOverflowContext(widget);
                    refreshPropertyView = true;
                }
                else if (stateField == StateVisualProfileField.OverflowMode &&
                         widget is CanonicalStateVisualWidgetDefinition)
                {
                    ReconcileStateTextOverflowModeEdit(widget, stateKey);
                    refreshPropertyView = true;
                }
            }
            return refreshPropertyView;
        }

        if (propertyName.Equals(nameof(CanonicalWidgetDefinition.Width), StringComparison.OrdinalIgnoreCase))
            ReconcileTextOverflowWidthEdit(widget);
        else if (propertyName.Equals("OverflowMode", StringComparison.OrdinalIgnoreCase))
            ReconcileTextOverflowModeEdit(widget);

        if (widget is CanonicalValueWidgetDefinition value &&
            propertyName is "SourceKind" or "Text" or "SourceUnit" or "Unit")
        {
            ReconcileValueDataContext(value);
        }

        if (widget is CanonicalGaugeWidgetDefinition gauge &&
            propertyName.Equals(nameof(CanonicalWidgetDefinition.Width), StringComparison.OrdinalIgnoreCase))
        {
            WidgetSizeContract.ApplyDerivedDimensions(gauge);
            refreshPropertyView = true;
        }

        if (propertyName.Equals("Metric", StringComparison.OrdinalIgnoreCase))
        {
            switch (widget)
            {
                case CanonicalValueWidgetDefinition valueMetric:
                    ReconcileValueDataContext(valueMetric);
                    break;
                case CanonicalBinaryWidgetDefinition binary:
                {
                    var usesValueVisual = binary.Profiles.Values.Any(profile => CanonicalStateContentType.IsValue(profile.ContentType));
                    var setpointMode = string.Equals(binary.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase);
                    binary.Unit = usesValueVisual && !setpointMode
                        ? MetricContract.NormalizeUnitForMetric(binary.Metric, binary.Unit)
                        : MetricContract.NormalizeNumericUnitForMetric(binary.Metric, binary.Unit);
                    binary.Format = MetricContract.NormalizeFormatForMetric(binary.Metric, binary.Format);
                    break;
                }
                case PinkieSysMon.DashboardModel.QuantitativeWidgetDefinition quantitative:
                    quantitative.Unit = MetricContract.NormalizeNumericUnitForMetric(quantitative.Metric, quantitative.Unit);
                    break;
            }
            refreshPropertyView = true;
        }
        else if (propertyName is "SourceType" or "SourceKind" or "ContentMode")
        {
            refreshPropertyView = true;
        }
        else if (propertyName.StartsWith("Threshold", StringComparison.Ordinal) ||
                 propertyName.Equals(nameof(CanonicalWidgetDefinition.ShadowEnabled), StringComparison.OrdinalIgnoreCase) ||
                 propertyName is "TrackEnabled" or "NeedleEnabled")
        {
            refreshPropertyView = true;
        }
        else if (widget is CanonicalBinaryWidgetDefinition binary &&
                 propertyName.Equals(nameof(CanonicalBinaryWidgetDefinition.EvaluationMode), StringComparison.OrdinalIgnoreCase))
        {
            var usesValueVisual = binary.Profiles.Values.Any(profile => CanonicalStateContentType.IsValue(profile.ContentType));
            var setpointMode = string.Equals(binary.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase);
            binary.Unit = usesValueVisual && !setpointMode
                ? MetricContract.NormalizeUnitForMetric(binary.Metric, binary.Unit)
                : MetricContract.NormalizeNumericUnitForMetric(binary.Metric, binary.Unit);
            refreshPropertyView = true;
        }
        else if (widget is CanonicalValueWidgetDefinition &&
                 propertyName is "SourceKind" or "Text" or "SourceUnit" or "Width" or "Unit" or "OverflowMode")
        {
            refreshPropertyView = true;
        }
        else if (widget is CanonicalStateVisualWidgetDefinition && propertyName.Equals("Width", StringComparison.OrdinalIgnoreCase))
        {
            refreshPropertyView = true;
        }

        return refreshPropertyView;
    }

    private void RefreshCanvasPropertyViewDeferred(CanonicalCanvasDefinition canvas)
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
            return;

        var generation = ++_propertyViewRefreshGeneration;
        BeginInvoke((Action)(() =>
        {
            if (generation != _propertyViewRefreshGeneration || IsDisposed || Disposing ||
                _definition is null || !ReferenceEquals(_definition.Canvas, canvas) ||
                _propertyGridTarget is not CanonicalCanvasDefinition current || !ReferenceEquals(current, canvas))
            {
                return;
            }

            PreserveEditorInputFocus(RefreshPropertyGridView);
        }));
    }

    private void RefreshWidgetPropertyViewDeferred(CanonicalWidgetDefinition widget)
    {
        if (_selectedWidgets.Contains(widget))
            RefreshWidgetPropertySelectionDeferred();
    }

    private void RefreshWidgetPropertySelectionDeferred()
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
            return;

        var expectedWidgets = GetOrderedSelectedWidgets().ToArray();
        var expectedPrimary = _selectedWidget;
        var generation = ++_propertyViewRefreshGeneration;
        BeginInvoke((Action)(() =>
        {
            if (generation != _propertyViewRefreshGeneration || IsDisposed || Disposing || _definition is null)
                return;

            var currentWidgets = GetOrderedSelectedWidgets();
            if (currentWidgets.Count != expectedWidgets.Length ||
                currentWidgets.Where((widget, index) => !ReferenceEquals(widget, expectedWidgets[index])).Any())
            {
                return;
            }

            if (expectedPrimary is null || !_selectedWidgets.Contains(expectedPrimary))
                return;

            SetPropertyGridTarget(expectedPrimary);
        }));
    }

    private static void ReconcileValueDataContext(CanonicalValueWidgetDefinition widget)
    {
        if (TextValueContract.IsMetricSource(widget.SourceKind))
        {
            widget.Unit = MetricContract.NormalizeUnitForMetric(widget.Metric, widget.Unit);
            widget.Format = MetricContract.NormalizeFormatForMetric(widget.Metric, widget.Format);
            return;
        }

        if (!LiteralNumericContract.TryParse(widget.Text, out _))
            return;

        widget.SourceUnit = LiteralNumericContract.NormalizeSourceUnit(widget.SourceUnit);
        widget.Unit = LiteralNumericContract.NormalizeUnit(widget.SourceUnit, widget.Unit);
        widget.Format = LiteralNumericContract.NormalizeFormat(widget.SourceUnit, widget.Unit, widget.Format);
    }

    private static bool NormalizePreparedLiteralNumericState(CanonicalDashboardDefinition definition)
    {
        var adjusted = false;
        foreach (var value in definition.Widgets.OfType<CanonicalValueWidgetDefinition>())
        {
            if (!TextValueContract.IsTextSource(value.SourceKind) || !LiteralNumericContract.TryParse(value.Text, out _))
                continue;

            var before = (value.SourceUnit, value.Unit, value.Format);
            ReconcileValueDataContext(value);
            adjusted |= before != (value.SourceUnit, value.Unit, value.Format);
        }
        return adjusted;
    }


    private static bool NormalizePreparedTextOverflowState(CanonicalDashboardDefinition definition)
    {
        var adjusted = false;
        foreach (var widget in definition.Widgets)
            adjusted |= TextOverflowStateContract.NormalizeStoredState(widget);
        return adjusted;
    }

    private void ReconcileTextOverflowWidthEdit(CanonicalWidgetDefinition widget)
    {
        if (!TextOverflowStateContract.UsesTextPresentation(widget))
            return;

        var previousWidth = widget.Width;
        TextOverflowStateContract.ApplyWidthChange(
            widget,
            ResolveCurrentTextConstraintWidth(widget),
            GetMinimumTextConstraintWidth(widget));
        if (widget.Width != previousWidth)
            _preview?.InvalidateWidgetGeometry(widget);
    }

    private void ReconcileTextOverflowModeEdit(CanonicalWidgetDefinition widget)
    {
        if (widget is not CanonicalValueWidgetDefinition || !TextOverflowStateContract.UsesTextPresentation(widget))
            return;

        var previousWidth = widget.Width;
        TextOverflowStateContract.ApplyOverflowModeChange(
            widget,
            ResolveCurrentTextConstraintWidth(widget),
            GetMinimumTextConstraintWidth(widget));
        if (widget.Width != previousWidth)
            _preview?.InvalidateWidgetGeometry(widget);
    }

    private void ReconcileStateTextOverflowModeEdit(
        CanonicalWidgetDefinition widget,
        string stateKey)
    {
        if (widget is not CanonicalStateVisualWidgetDefinition)
            return;

        var previousWidth = widget.Width;
        TextOverflowStateContract.ApplyOverflowModeChange(
            widget,
            stateKey,
            ResolveCurrentTextConstraintWidth(widget),
            GetMinimumTextConstraintWidth(widget));
        if (widget.Width != previousWidth)
            _preview?.InvalidateWidgetGeometry(widget);
    }

    private void ReconcileTextOverflowContext(CanonicalWidgetDefinition widget)
    {
        if (!TextOverflowStateContract.UsesTextPresentation(widget) && widget is not CanonicalStateVisualWidgetDefinition)
            return;

        var previousWidth = widget.Width;
        TextOverflowStateContract.ApplyContextChange(
            widget,
            ResolveCurrentTextConstraintWidth(widget),
            GetMinimumTextConstraintWidth(widget));
        if (widget.Width != previousWidth)
            _preview?.InvalidateWidgetGeometry(widget);
    }


    private float ResolveCurrentTextConstraintWidth(CanonicalWidgetDefinition widget)
    {
        var minimum = GetMinimumTextConstraintWidth(widget);
        if (_preview is null)
            return minimum;

        try
        {
            var measured = _preview.GetWidgetLayoutBounds(widget, _lastMetrics).Width;
            return float.IsFinite(measured) && measured > WidgetResizeGeometry.MinimumDimension
                ? measured
                : minimum;
        }
        catch
        {
            return minimum;
        }
    }

    private static float GetMinimumTextConstraintWidth(CanonicalWidgetDefinition widget)
    {
        var fontSize = widget switch
        {
            CanonicalValueWidgetDefinition value => value.TextPresentation.FontSize,
            CanonicalStateVisualWidgetDefinition stateWidget => stateWidget.Profiles.Values
                .Where(profile => CanonicalStateContentType.IsValue(profile.ContentType))
                .Select(profile => profile.TextPresentation.FontSize)
                .DefaultIfEmpty(0f)
                .Max(),
            _ => 0f
        };

        return Math.Max(WidgetResizeGeometry.MinimumDimension, fontSize);
    }

    private static bool TryParseStateProfileProperty(
        string propertyName,
        out string stateKey,
        out StateVisualProfileField field)
    {
        stateKey = string.Empty;
        field = default;

        if (!propertyName.StartsWith(
                StateVisualProfilePropertyDescriptor.PropertyPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        var payload = propertyName[StateVisualProfilePropertyDescriptor.PropertyPrefix.Length..];
        var separator = payload.LastIndexOf(':');
        if (separator <= 0 || separator >= payload.Length - 1)
            return false;

        stateKey = payload[..separator];
        return Enum.TryParse(payload[(separator + 1)..], ignoreCase: false, out field);
    }


    private void CanvasMouseDown(object? sender, MouseEventArgs e)
    {
        ClaimEditorInputSurface(EditorInputSurface.Canvas, _workspaceViewport);
        if (e.Button != MouseButtons.Left || _definition is null || _preview is null)
            return;

        var point = ScreenToLogical(e.Location);
        if (TryBeginResize(point))
            return;

        var hit = HitTest(point.X, point.Y);
        if (hit is null)
        {
            if ((ModifierKeys & Keys.Shift) == 0)
            {
                ClearWidgetSelection();
                SetPropertyGridTarget(null);
                _workspaceViewport.Invalidate();
            }
            _dragging = false;
            _resizeDrag = null;
            UpdateResizeCursor(point);
            return;
        }

        var shift = (ModifierKeys & Keys.Shift) != 0;
        if (shift)
        {
            ToggleSelection(hit);
            if (!_selectedWidgets.Contains(hit))
                return;
        }
        else if (!_selectedWidgets.Contains(hit))
        {
            SetSingleSelection(hit);
        }
        else
        {
            _selectedWidget = hit;
            _treePrimaryKey = WidgetTreeKey(hit);
            _treeSelectionAnchorKey = _treePrimaryKey;
            SetPropertyGridTarget(hit);
            UpdateTreeSelectionVisuals();
            UpdateCommandStates();
        }

        _dragUndoSnapshot = CaptureSnapshot();
        _dragging = true;
        _resizeDrag = null;
        _workspaceViewport.Capture = true;
        _previewTimer.Stop();
        _dragStartLogical = point;
        _dragOrigins.Clear();
        foreach (var widget in _selectedWidgets)
            _dragOrigins[widget] = (widget.X, widget.Y);
    }

    private bool TryBeginResize(PointF point)
    {
        if (_preview is null || _selectedWidget is null || !_selectedWidgets.Contains(_selectedWidget))
            return false;

        SKPoint[] outline;
        try
        {
            outline = _preview.GetWidgetOutline(_selectedWidget, _lastMetrics);
        }
        catch
        {
            return false;
        }

        if (outline.Length < 4 || !IsResizeHandleHit(point, outline[2]))
            return false;

        var widget = _selectedWidget;
        var layout = _preview.GetWidgetLayoutBounds(widget, _lastMetrics);
        var wasAutoWidth = IsAutoWidthTextWidget(widget);
        var initialWidth = wasAutoWidth
            ? WidgetResizeGeometry.NormalizeResizedDimension(layout.Width)
            : WidgetResizeGeometry.NormalizeLogicalDimension(widget.Width);
        var initialHeight = WidgetResizeGeometry.NormalizeLogicalDimension(widget.Height);

        _dragUndoSnapshot = CaptureSnapshot();
        _dragging = true;
        _workspaceViewport.Capture = true;
        _previewTimer.Stop();
        _dragStartLogical = point;
        _dragOrigins.Clear();
        _resizeDrag = new ResizeDragState(
            widget,
            point,
            widget.X,
            widget.Y,
            initialWidth,
            initialHeight,
            new PointF(outline[0].X, outline[0].Y),
            widget.Rotation,
            wasAutoWidth);
        _workspaceViewport.Cursor = GetResizeCursor(widget.Rotation);
        return true;
    }

    private static bool IsAutoWidthTextWidget(CanonicalWidgetDefinition widget)
    {
        if (widget.Width > 0f)
            return false;

        return IsWidthOnlyTextWidget(widget);
    }

    private static bool IsWidthOnlyTextWidget(CanonicalWidgetDefinition widget) =>
        WidgetSizeContract.IsWidthOnlyResizable(widget);

    private void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        var point = ScreenToLogical(e.Location);
        if (!_dragging || _selectedWidgets.Count == 0 || e.Button != MouseButtons.Left)
        {
            UpdateResizeCursor(point);
            return;
        }

        if (_resizeDrag is not null)
        {
            ResizePrimaryWidget(point);
            return;
        }

        var dx = (float)Math.Round(point.X - _dragStartLogical.X);
        var dy = (float)Math.Round(point.Y - _dragStartLogical.Y);

        foreach (var widget in _selectedWidgets)
        {
            if (!_dragOrigins.TryGetValue(widget, out var origin))
                continue;

            widget.X = origin.X + dx;
            widget.Y = origin.Y + dy;
            SyncGeometryToJson(widget);
        }

        _propertyGrid.Refresh();
        MarkDirty();
        RenderPreview(force: true, immediate: true);
    }

    private void ResizePrimaryWidget(PointF point)
    {
        if (_resizeDrag is null || _preview is null)
            return;

        var state = _resizeDrag;
        var widget = state.Widget;
        var canvasDelta = new PointF(point.X - state.StartLogical.X, point.Y - state.StartLogical.Y);
        var localDelta = WidgetResizeGeometry.ProjectCanvasDeltaToLocal(canvasDelta, state.Rotation);
        var localDx = (float)Math.Round(localDelta.X);
        var localDy = (float)Math.Round(localDelta.Y);

        if (IsWidthOnlyTextWidget(widget) &&
            state.WasAutoWidth &&
            localDx == 0f)
        {
            return;
        }

        widget.X = state.OriginX;
        widget.Y = state.OriginY;

        if (widget is CanonicalGaugeWidgetDefinition gaugeWidget)
        {
            gaugeWidget.Width = WidgetResizeGeometry.NormalizeResizedDimension(
                WidgetResizeGeometry.ResolveGaugeSize(
                    gaugeWidget,
                    state.InitialWidth,
                    localDx,
                    localDy),
                WidgetResizeGeometry.GetMinimumGaugeSize(gaugeWidget));
            WidgetSizeContract.ApplyDerivedDimensions(gaugeWidget);
        }
        else if (IsWidthOnlyTextWidget(widget))
        {
            widget.Width = WidgetResizeGeometry.NormalizeResizedDimension(
                state.InitialWidth + localDx);
        }
        else
        {
            widget.Width = WidgetResizeGeometry.NormalizeResizedDimension(
                state.InitialWidth + localDx);
            widget.Height = WidgetResizeGeometry.NormalizeResizedDimension(
                state.InitialHeight + localDy);
        }

        if (TextOverflowStateContract.UsesTextPresentation(widget))
        {
            TextOverflowStateContract.ApplyWidthChange(
                widget,
                ResolveCurrentTextConstraintWidth(widget),
                GetMinimumTextConstraintWidth(widget));
        }

        var resizedOutline = _preview.GetWidgetOutline(widget, _lastMetrics);
        if (resizedOutline.Length >= 4)
        {
            widget.X += state.FixedCorner.X - resizedOutline[0].X;
            widget.Y += state.FixedCorner.Y - resizedOutline[0].Y;
        }

        SyncResizeGeometryToJson(widget);
        _preview.InvalidateWidgetGeometry(widget);
        _propertyGrid.Refresh();
        MarkDirty();
        RenderPreview(force: true, immediate: true);
    }

    private void CanvasMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var resize = _resizeDrag;
        _dragging = false;
        _resizeDrag = null;
        _workspaceViewport.Capture = false;
        UpdatePreviewTimerState();
        if (_dragUndoSnapshot is not null && SnapshotDiffers(_dragUndoSnapshot))
            PushUndo(_dragUndoSnapshot);
        _dragUndoSnapshot = null;
        _dragOrigins.Clear();

        if (resize is not null &&
            resize.WasAutoWidth &&
            resize.Widget.Width > 0f)
        {
            RefreshWidgetPropertyViewDeferred(resize.Widget);
        }

        var point = ScreenToLogical(e.Location);
        UpdateResizeCursor(point);
        RenderPreview(force: true, immediate: true);
    }

    private void UpdateResizeCursor(PointF point)
    {
        if (_dragging && _resizeDrag is not null)
        {
            _workspaceViewport.Cursor = GetResizeCursor(_resizeDrag.Rotation);
            return;
        }

        if (_preview is null || _selectedWidget is null || !_selectedWidgets.Contains(_selectedWidget))
        {
            _workspaceViewport.Cursor = Cursors.Default;
            return;
        }

        try
        {
            var outline = _preview.GetWidgetOutline(_selectedWidget, _lastMetrics);
            _workspaceViewport.Cursor = outline.Length >= 4 && IsResizeHandleHit(point, outline[2])
                ? GetResizeCursor(_selectedWidget.Rotation)
                : Cursors.Default;
        }
        catch
        {
            _workspaceViewport.Cursor = Cursors.Default;
        }
    }

    private bool IsResizeHandleHit(PointF point, SKPoint corner)
    {
        const float hitHalfSizeScreenPixels = 6f;
        var hitHalfSize = hitHalfSizeScreenPixels / Math.Max(0.01f, _zoom);
        return Math.Abs(point.X - corner.X) <= hitHalfSize &&
               Math.Abs(point.Y - corner.Y) <= hitHalfSize;
    }

    private static Cursor GetResizeCursor(double rotation)
    {
        var normalized = rotation % 180d;
        if (normalized < 0d)
            normalized += 180d;

        return normalized >= 45d && normalized < 135d
            ? Cursors.SizeNESW
            : Cursors.SizeNWSE;
    }

    private void SelectAt(Point screenPoint)
    {
        var p = ScreenToLogical(screenPoint);
        var hit = HitTest(p.X, p.Y);
        if (hit is not null)
        {
            if ((ModifierKeys & Keys.Shift) != 0)
                ToggleSelection(hit);
            else
                SetSingleSelection(hit);
        }
    }

    private CanonicalWidgetDefinition? HitTest(float x, float y)
    {
        if (_definition is null || _preview is null)
            return null;

        return _definition.Widgets
            .Select((widget, index) => new { widget, index })
            .OrderByDescending(x => x.widget.Z)
            .ThenByDescending(x => x.index)
            .Select(x => x.widget)
            .FirstOrDefault(widget =>
            {
                return _preview.HitTestWidget(widget, _lastMetrics, x, y);
            });
    }

    private void SelectWidget(CanonicalWidgetDefinition widget)
    {
        SetSingleSelection(widget);
    }

    private void SyncGeometryToJson(CanonicalWidgetDefinition widget)
    {
        widget.X = WidgetResizeGeometry.NormalizeLogicalCoordinate(widget.X);
        widget.Y = WidgetResizeGeometry.NormalizeLogicalCoordinate(widget.Y);
    }

    private void SyncResizeGeometryToJson(CanonicalWidgetDefinition widget)
    {
        widget.X = WidgetResizeGeometry.NormalizeLogicalCoordinate(widget.X);
        widget.Y = WidgetResizeGeometry.NormalizeLogicalCoordinate(widget.Y);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.N)
        {
            ExecuteNewDashboardCommand();
            return true;
        }
        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.O)
        {
            ExecuteOpenDashboardCommand();
            return true;
        }
        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.S)
        {
            ExecuteSaveCommand();
            return true;
        }
        if (modifiers == Keys.None && keyCode == Keys.F5)
        {
            ExecuteReloadEditorCommand();
            return true;
        }
        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.Z)
        {
            if ((modifiers & Keys.Shift) != 0)
                ExecuteRedoCommand();
            else
                ExecuteUndoCommand();
            return true;
        }
        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.Y)
        {
            ExecuteRedoCommand();
            return true;
        }
        if ((modifiers & Keys.Control) != 0 && keyCode == Keys.D)
        {
            ExecuteDuplicateCommand();
            return true;
        }
        if (keyCode == Keys.F2)
        {
            ExecuteRenameCommand();
            return true;
        }
        if (_widgetTree.Focused && modifiers == Keys.None && keyCode == Keys.Delete && _selectedTreeKeys.Count > 0)
        {
            ExecuteDeleteCommand();
            return true;
        }

        if (IsEditorInputSurfaceActive(EditorInputSurface.Canvas) && _selectedWidgets.Count > 0)
        {
            var step = (modifiers & Keys.Shift) != 0 ? 10f : 1f;
            var dx = 0f;
            var dy = 0f;

            switch (keyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                case Keys.Delete:
                    ExecuteDeleteCommand();
                    return true;
                default:
                    return base.ProcessCmdKey(ref msg, keyData);
            }

            MoveSelectedWidgets(dx, dy);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void MoveSelectedWidgets(float dx, float dy)
    {
        if (_selectedWidgets.Count == 0)
            return;

        var undoBefore = CaptureSnapshot();
        foreach (var widget in _selectedWidgets)
        {
            widget.X += dx;
            widget.Y += dy;
            SyncGeometryToJson(widget);
        }

        _propertyGrid.Refresh();
        PushUndo(undoBefore);
        MarkDirty();
        RenderPreview(force: true, immediate: true);
    }

    private Point CaptureScrollPosition() => new(
        Math.Max(0, -_workspaceViewport.AutoScrollPosition.X),
        Math.Max(0, -_workspaceViewport.AutoScrollPosition.Y));

    private Point ClampScrollPosition(Point position)
    {
        var maxX = Math.Max(0, _workspaceViewport.AutoScrollMinSize.Width - _workspaceViewport.ClientSize.Width);
        var maxY = Math.Max(0, _workspaceViewport.AutoScrollMinSize.Height - _workspaceViewport.ClientSize.Height);
        return new Point(
            Math.Clamp(position.X, 0, maxX),
            Math.Clamp(position.Y, 0, maxY));
    }

    private void RestoreScrollPosition(Point position)
    {
        var clamped = ClampScrollPosition(position);
        _workspaceViewport.AutoScrollPosition = clamped;
    }

    private void RestoreScrollPositionDeferred(Point position)
    {
        // Cancel any older deferred restore. Reload/zoom can schedule several layout passes; a
        // stale callback must never re-apply an earlier viewport offset to a rebuilt surface.
        var generation = ++_scrollRestoreGeneration;
        if (!IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            if (generation != _scrollRestoreGeneration || IsDisposed)
                return;

            _workspaceViewport.PerformLayout();
            RestoreScrollPosition(position);
        });
    }

    private PointF ScreenToLogical(Point point)
    {
        var scroll = CaptureScrollPosition();
        return new PointF(
            (point.X + scroll.X) / _zoom,
            (point.Y + scroll.Y) / _zoom);
    }

    private void ApplyZoom(bool restoreScroll = true)
    {
        if (_preview is null)
            return;

        var desiredScroll = CaptureScrollPosition();
        if (_fitZoom)
        {
            var availableHeight = Math.Max(100, _workspaceViewport.ClientSize.Height - 24);
            _zoom = Math.Clamp(availableHeight / (float)_preview.Height, MinEditorZoom, MaxEditorZoom);
            desiredScroll = Point.Empty;
        }

        var canvasSize = new Size(
            Math.Max(1, (int)Math.Round(_preview.Width * _zoom)),
            Math.Max(1, (int)Math.Round(_preview.Height * _zoom)));

        // AutoScroll owns only the viewport offset and scrollbar range. The dashboard is
        // custom-painted and is not represented by a child control, so a scroll operation
        // can never feed a child Location back into the virtual extent during reload/layout.
        ++_scrollRestoreGeneration; // invalidate any pending restore from an older layout
        _workspaceViewport.SuspendLayout();
        try
        {
            _workspaceViewport.AutoScrollMinSize = canvasSize;
        }
        finally
        {
            _workspaceViewport.ResumeLayout(true);
        }

        _workspaceViewport.Invalidate();
        UpdateZoomToolbarText();
        UpdateStatusBar();
        if (restoreScroll)
            RestoreScrollPositionDeferred(desiredScroll);
    }

    private void AddWidget(string type)
    {
        if (_definition is null)
            return;

        try
        {
            if (WidgetTypeContract.Is(type, WidgetTypeContract.Image))
            {
                AddImageWidget();
                return;
            }

            var undoBefore = CaptureSnapshot();
            var id = CreateUniqueId(type);
            var nextZ = _definition.Widgets.Count == 0 ? 0 : _definition.Widgets.Max(w => w.Z) + 1;

            CanonicalWidgetDefinition widget = type.Trim().ToLowerInvariant() switch
            {
                WidgetTypeContract.Value => new CanonicalValueWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 0,
                    Height = 0,
                    SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Metric,
                    Metric = RuntimeMetricContract.Version,
                    TextPresentation = new PinkieSysMon.DashboardModel.TextPresentationDefinition
                    {
                        FontFamily = "Roboto",
                        FontSize = 32,
                        FontWeight = 400,
                        OverflowMode = ValueOverflowContract.None
                    }
                },
                WidgetTypeContract.Bar => new CanonicalBarWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 300,
                    Height = 20,
                    Metric = RuntimeMetricContract.Fps,
                    Min = 0,
                    Max = 100,
                    ContentMode = BarImageContract.ContentModeFill
                },
                WidgetTypeContract.Gauge => new CanonicalGaugeWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 200,
                    Height = 200,
                    Metric = RuntimeMetricContract.Fps,
                    Min = 0,
                    Max = 100,
                    StartAngle = 30,
                    EndAngle = 330,
                    Track = new PinkieSysMon.DashboardModel.GaugeTrackDefinition
                    {
                        Enabled = true,
                        Thickness = 20,
                        BackgroundColor = "#30FFFFFF"
                    }
                },
                WidgetTypeContract.Binary => new CanonicalBinaryWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 96,
                    Height = 96,
                    Metric = MediaMetricContract.InputActive,
                    EvaluationMode = BinarySignalContract.EvaluationModeAuto
                },
                WidgetTypeContract.Power => new CanonicalPowerWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 96,
                    Height = 96,
                    PowerSource = PowerMetricContract.UpsSource
                },
                WidgetTypeContract.MediaSystem => new CanonicalMediaSystemWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 96,
                    Height = 96,
                    MediaSource = MediaMetricContract.OutputSource
                },
                WidgetTypeContract.MediaPlayer => new CanonicalMediaPlayerWidgetDefinition
                {
                    Id = id,
                    Z = nextZ,
                    X = 100,
                    Y = 100,
                    Width = 96,
                    Height = 96
                },
                _ => throw new InvalidOperationException($"Unsupported widget type '{type}'.")
            };

            if (widget is CanonicalStateVisualWidgetDefinition stateWidget)
                TextOverflowStateContract.EnsureProfiles(stateWidget);

            _definition.Widgets.Add(widget);
            var parent = _widgetTree.Nodes.Cast<TreeNode>().First(n => n.Text == "Widgets");
            var treeNode = new TreeNode(WidgetCaption(widget)) { Tag = widget };
            parent.Nodes.Add(treeNode);
            _widgetTreeNodes[widget] = treeNode;
            SelectWidget(widget);
            PushUndo(undoBefore);
            MarkDirty();
            RebuildPreviewRenderer();
            RefreshPreviewTelemetry();
            RenderPreview(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Add widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddImageWidget()
    {
        if (_definition is null)
            return;

        var dashboardDirectory = Path.GetDirectoryName(_dashboardPath)!;
        using var sourceTypeDialog = new ImageSourceTypeDialog();
        if (sourceTypeDialog.ShowDialog(this) != DialogResult.OK)
            return;

        var sourceType = sourceTypeDialog.SourceType;
        string source;
        int width;
        int height;

        if (CanonicalImageAssetSourceType.IsIcon(sourceType))
        {
            using var browser = new IconBrowserDialog(_root);
            if (browser.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(browser.SelectedIcon))
                return;

            source = browser.SelectedIcon;
            width = 96;
            height = 96;
        }
        else
        {
            var selected = EditorImageSourceTools.SelectDashboardImage(this, dashboardDirectory, null);
            if (string.IsNullOrWhiteSpace(selected))
                return;

            source = selected;
            var fullPath = AssetPathResolver.ResolveExistingFile(dashboardDirectory, source, "Selected dashboard image");
            using var stream = File.OpenRead(fullPath);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Skia could not decode the selected image.");
            width = Math.Min(codec.Info.Width, 400);
            height = Math.Min(codec.Info.Height, 300);
        }

        var id = CreateUniqueId(WidgetTypeContract.Image);
        var nextZ = _definition.Widgets.Count == 0 ? 0 : _definition.Widgets.Max(w => w.Z) + 1;
        var widget = new CanonicalImageWidgetDefinition
        {
            Id = id,
            Z = nextZ,
            X = 100,
            Y = 100,
            Width = width,
            Height = height,
            Asset = new PinkieSysMon.DashboardModel.ImageAssetPresentationDefinition
            {
                SourceType = CanonicalImageAssetSourceType.Normalize(sourceType),
                Source = source,
                Fit = StateVisualProfileContract.FitStretch,
                Loop = true
            },
            Color = "#FFFFFFFF",
            Opacity = 1f
        };

        var undoBefore = CaptureSnapshot();
        _definition.Widgets.Add(widget);
        var parent = _widgetTree.Nodes.Cast<TreeNode>().First(n => n.Text == "Widgets");
        var treeNode = new TreeNode(WidgetCaption(widget)) { Tag = widget };
        parent.Nodes.Add(treeNode);
        _widgetTreeNodes[widget] = treeNode;
        SelectWidget(widget);
        PushUndo(undoBefore);
        MarkDirty();
        RebuildPreviewRenderer();
        RefreshPreviewTelemetry();
        RenderPreview(force: true);
    }

    private void HandleDashboardWheel(int delta)
    {
        var modifiers = ModifierKeys;
        if ((modifiers & Keys.Control) != 0)
        {
            ZoomByWheel(delta, _workspaceViewport.PointToClient(Cursor.Position));
            return;
        }

        var notches = delta / 120f;
        if ((modifiers & Keys.Shift) != 0)
            ScrollBy((int)Math.Round(-notches * 120), 0);
        else
            ScrollBy(0, (int)Math.Round(-notches * 120));
    }

    private void ScrollBy(int dx, int dy)
    {
        var current = CaptureScrollPosition();
        var maxX = Math.Max(0, _workspaceViewport.AutoScrollMinSize.Width - _workspaceViewport.ClientSize.Width);
        var maxY = Math.Max(0, _workspaceViewport.AutoScrollMinSize.Height - _workspaceViewport.ClientSize.Height);
        var next = new Point(
            Math.Clamp(current.X + dx, 0, maxX),
            Math.Clamp(current.Y + dy, 0, maxY));
        RestoreScrollPosition(next);
    }

    private void ZoomByWheel(int delta, Point clientPoint)
    {
        if (_preview is null || delta == 0)
            return;

        var currentZoom = _zoom;
        float nextZoom;
        if (delta > 0)
        {
            nextZoom = ZoomLevels.FirstOrDefault(z => z > currentZoom + 0.001f, MaxEditorZoom);
        }
        else
        {
            nextZoom = ZoomLevels.LastOrDefault(z => z < currentZoom - 0.001f, MinEditorZoom);
            if (currentZoom < ZoomLevels[0] - 0.001f)
                nextZoom = MinEditorZoom;
        }

        if (Math.Abs(nextZoom - currentZoom) < 0.0001f)
            return;

        var scroll = CaptureScrollPosition();
        var logicalX = (scroll.X + clientPoint.X) / currentZoom;
        var logicalY = (scroll.Y + clientPoint.Y) / currentZoom;

        _fitZoom = false;
        _zoom = nextZoom;
        ApplyZoom(restoreScroll: false);
        var desired = new Point(
            Math.Max(0, (int)Math.Round(logicalX * nextZoom - clientPoint.X)),
            Math.Max(0, (int)Math.Round(logicalY * nextZoom - clientPoint.Y)));
        RestoreScrollPositionDeferred(desired);
    }

    private void RestoreWindowSettings()
    {
        if (_settings.WindowX is int x && _settings.WindowY is int y &&
            _settings.WindowWidth is int width && _settings.WindowHeight is int height &&
            width >= MinimumSize.Width && height >= MinimumSize.Height)
        {
            var bounds = new Rectangle(x, y, width, height);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }

        if (_settings.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private void RestorePanelWidths()
    {
        if (_navigationSplit.Width <= 0)
            return;

        var totalWidth = _navigationSplit.Width;
        var leftWidth = _settings.LeftPanelWidth ?? totalWidth / 4;
        leftWidth = Math.Clamp(leftWidth, 180, Math.Max(180, totalWidth - 600));
        _navigationSplit.SplitterDistance = leftWidth;

        var availableWorkspaceWidth = _navigationSplit.Panel2.Width;
        var rightWidth = _settings.RightPanelWidth ?? totalWidth / 4;
        rightWidth = Math.Clamp(rightWidth, 240, Math.Max(240, availableWorkspaceWidth - 300));
        _workspaceSplit.SplitterDistance = Math.Max(300, availableWorkspaceWidth - rightWidth - _workspaceSplit.SplitterWidth);
    }

    private void SaveCurrentDashboardViewState()
    {
        if (string.IsNullOrWhiteSpace(_dashboardName))
            return;

        var state = _settings.GetDashboard(_dashboardName);
        state.Zoom = _fitZoom ? "Fit Height" : FormatZoomPercent(_zoom);
        var scroll = CaptureScrollPosition();
        state.ScrollX = scroll.X;
        state.ScrollY = scroll.Y;
    }

    private void RestoreDashboardViewState()
    {
        if (string.IsNullOrWhiteSpace(_dashboardName) || _preview is null)
            return;

        var state = _settings.GetDashboard(_dashboardName);
        var zoomText = state.Zoom?.Trim() ?? "50%";
        _fitZoom = zoomText.Equals("Fit Height", StringComparison.OrdinalIgnoreCase) ||
                   zoomText.Equals("Fit", StringComparison.OrdinalIgnoreCase);

        if (!_fitZoom)
        {
            var rawPercent = zoomText.TrimEnd('%').Trim();
            if (!TryParseZoomPercent(rawPercent, out var percent))
                percent = 50f;
            _zoom = Math.Clamp(percent / 100f, MinEditorZoom, MaxEditorZoom);
        }

        ApplyZoom(restoreScroll: false);

        if (_formShown)
        {
            var scroll = new Point(Math.Max(0, state.ScrollX), Math.Max(0, state.ScrollY));
            RestoreScrollPositionDeferred(scroll);
        }
    }

    private void SaveEditorSettings()
    {
        try
        {
            SaveCurrentDashboardViewState();
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.WindowX = bounds.X;
            _settings.WindowY = bounds.Y;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            _settings.LeftPanelWidth = _navigationSplit.Panel1.Width;
            _settings.RightPanelWidth = _workspaceSplit.Panel2.Width;
            _settings.GridEnabled = _gridEnabled;
            _settings.GridStep = _gridStep;
            _settings.ToolbarIconSize = _toolbarIconSize;
            _settings.Save(_settingsPath);
        }
        catch (Exception ex)
        {
            _log.Warn($"Editor settings save failed: {ex.Message}");
        }
    }

    private static string WidgetTypeDisplayName(string type) =>
        WidgetTypeContract.Is(type, WidgetTypeContract.Value) ? "Text / Value" : type;

    private static string WidgetCaption(CanonicalWidgetDefinition widget)
    {
        var label = !string.IsNullOrWhiteSpace(widget.Name)
            ? widget.Name
            : !string.IsNullOrWhiteSpace(widget.Id) ? widget.Id : widget.Type;
        return $"{label}  [{widget.Type}]";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmSaveBeforeDestructiveAction())
        {
            _restartAfterClose = false;
            e.Cancel = true;
            return;
        }

        SaveEditorSettings();
        if (_wheelFilter is not null)
            Application.RemoveMessageFilter(_wheelFilter);
        _runtimeStatusTimer.Stop();
        _previewTimer.Stop();
        _runtimeStatusTimer.Dispose();
        _previewTimer.Dispose();
        _preview?.Dispose();
        _lhmTelemetry.Dispose();
        _mediaTelemetry.Dispose();
        _canvasImage?.Dispose();
        DisposePropertyTabImages();
        DisposeToolbarImages();
    }
}
