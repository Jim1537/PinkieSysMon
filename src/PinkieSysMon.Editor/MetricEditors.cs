using System.ComponentModel;
using System.Drawing.Design;

namespace PinkieSysMon.Editor;

internal static class MetricSelectorEnvironment
{
    private static string? _applicationRoot;
    private static FileLogger? _log;
    private static Func<AppConfig>? _configAccessor;
    private static readonly object CacheSync = new();
    private static readonly Dictionary<string, LibreHardwareMonitorMetricEntry> LastKnownLhmEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IcueSensorLogMetricEntry> LastKnownIcueEntries =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Configure(string applicationRoot, FileLogger log, Func<AppConfig> configAccessor)
    {
        _applicationRoot = applicationRoot ?? throw new ArgumentNullException(nameof(applicationRoot));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configAccessor = configAccessor ?? throw new ArgumentNullException(nameof(configAccessor));
    }

    public static MetricSelectorCatalogSnapshot BuildSnapshot(string? currentMetric)
    {
        var log = _log;
        var configAccessor = _configAccessor;
        if (log is null || configAccessor is null || string.IsNullOrWhiteSpace(_applicationRoot))
            return new MetricSelectorCatalogSnapshot([], null, null);

        var config = configAccessor();
        var systemEnabled = MetricProviderContract.IsEnabled(config.MetricProviders, MetricProviderContract.System);
        var lhmEnabled = MetricProviderContract.IsEnabled(config.MetricProviders, MetricProviderContract.LibreHardwareMonitor);
        var icueEnabled = MetricProviderContract.IsEnabled(config.MetricProviders, MetricProviderContract.Icue);

        var metrics = new List<MetricSelectorMetric>();

        if (systemEnabled)
        {
            metrics.AddRange(MetricContract.Descriptors
                .Where(descriptor => descriptor.Id.StartsWith(MetricProviderContract.System + ".", StringComparison.OrdinalIgnoreCase))
                .Select(descriptor => CreateRawMetric(descriptor.Id, available: true)));
        }

        var lhmDiscoverySucceeded = false;
        if (lhmEnabled)
        {
            try
            {
                using var client = new LibreHardwareMonitorHttpClient(log);
                if (client.TryGetTree(out var tree) && tree is not null)
                {
                    var catalog = LibreHardwareMonitorMetricCatalog.Build(tree, log);
                    var descriptors = catalog.Metrics.Values
                        .Select(entry => LibreHardwareMonitorMetricContract.CreateDescriptor(entry.MetricId, entry.SensorType, entry.SensorName))
                        .ToArray();

                    MetricContract.ReplaceProviderDescriptors(MetricProviderContract.LibreHardwareMonitor, descriptors);
                    lhmDiscoverySucceeded = true;

                    foreach (var entry in catalog.Metrics.Values)
                        metrics.Add(CreateLhmMetric(entry, available: true));

                    lock (CacheSync)
                    {
                        foreach (var entry in catalog.Metrics.Values)
                            LastKnownLhmEntries[entry.MetricId] = entry;
                    }
                }
            }
            catch (Exception ex)
            {
                // Metric selection must remain usable even when the optional external provider is unavailable.
                log.WarnThrottled(
                    "editor.metric-selector.lhm-discovery",
                    TimeSpan.FromSeconds(30),
                    "Libre Hardware Monitor discovery failed while opening the Metric selector.",
                    ex);
            }
        }

        var icueDiscoverySucceeded = false;
        if (icueEnabled)
        {
            try
            {
                var source = new IcueSensorLogTelemetrySource(_applicationRoot!, log);
                if (source.RefreshCatalog())
                {
                    var entries = source.MetricEntries;
                    var descriptors = entries
                        .Select(IcueSensorLogMetricContract.CreateDescriptor)
                        .ToArray();

                    MetricContract.ReplaceProviderDescriptors(MetricProviderContract.Icue, descriptors);
                    icueDiscoverySucceeded = true;

                    foreach (var entry in entries)
                        metrics.Add(CreateIcueMetric(entry, available: true));

                    lock (CacheSync)
                    {
                        foreach (var entry in entries)
                            LastKnownIcueEntries[entry.MetricId] = entry;
                    }
                }
            }
            catch (Exception ex)
            {
                log.WarnThrottled(
                    "editor.metric-selector.icue-discovery",
                    TimeSpan.FromSeconds(30),
                    "iCUE Sensor Logging discovery failed while opening the Metric selector.",
                    ex);
            }
        }

        MetricSelectorMetric? ghostMetric = null;
        var currentLhmUnavailable =
            lhmEnabled &&
            LibreHardwareMonitorMetricContract.IsMetricId(currentMetric) &&
            !metrics.Any(metric => metric.Id.Equals(currentMetric, StringComparison.OrdinalIgnoreCase));

        if (currentLhmUnavailable)
        {
            LibreHardwareMonitorMetricEntry? ghostMetadata;
            lock (CacheSync)
                LastKnownLhmEntries.TryGetValue(currentMetric!, out ghostMetadata);

            ghostMetric = ghostMetadata is not null
                ? CreateLhmMetric(ghostMetadata, available: false)
                : CreateRawMetric(currentMetric!, available: false);
        }

        var currentIcueUnavailable =
            icueEnabled &&
            IcueSensorLogMetricContract.IsMetricId(currentMetric) &&
            !metrics.Any(metric => metric.Id.Equals(currentMetric, StringComparison.OrdinalIgnoreCase));

        if (currentIcueUnavailable)
        {
            IcueSensorLogMetricEntry? ghostMetadata;
            lock (CacheSync)
                LastKnownIcueEntries.TryGetValue(currentMetric!, out ghostMetadata);

            ghostMetric = ghostMetadata is not null
                ? CreateIcueMetric(ghostMetadata, available: false)
                : CreateRawMetric(currentMetric!, available: false);
        }

        var statusMessages = new List<string>();
        if (lhmEnabled && !lhmDiscoverySucceeded)
            statusMessages.Add("LibreHardwareMonitor is unavailable. Existing LHM metrics remain unchanged and are shown as unavailable.");
        else if (currentLhmUnavailable)
            statusMessages.Add("The current LHM metric is not present in the discovered catalog.");

        if (icueEnabled && !icueDiscoverySucceeded)
            statusMessages.Add("iCUE Sensor Logging is unavailable. Existing iCUE metrics remain unchanged and are shown as unavailable.");
        else if (currentIcueUnavailable)
            statusMessages.Add("The current iCUE metric is not present in the discovered catalog.");

        var status = statusMessages.Count == 0 ? null : string.Join(" ", statusMessages);
        return new MetricSelectorCatalogSnapshot(metrics, ghostMetric, status);
    }

    private static MetricSelectorMetric CreateRawMetric(string metricId, bool available)
    {
        var parts = metricId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var displayPath = new List<MetricSelectorPathSegment>(parts.Length);
        var rawPath = string.Empty;

        foreach (var part in parts)
        {
            rawPath = rawPath.Length == 0 ? part : rawPath + "." + part;
            displayPath.Add(new MetricSelectorPathSegment("raw:" + rawPath, part));
        }

        return new MetricSelectorMetric(metricId, available, displayPath);
    }

    private static MetricSelectorMetric CreateLhmMetric(LibreHardwareMonitorMetricEntry entry, bool available)
    {
        var displayPath = new List<MetricSelectorPathSegment>
        {
            new("provider:lhm", MetricProviderContract.LibreHardwareMonitor)
        };

        foreach (var hardware in entry.HardwarePath)
        {
            var name = string.IsNullOrWhiteSpace(hardware.HardwareName)
                ? hardware.HardwareId
                : hardware.HardwareName.Trim();
            displayPath.Add(new MetricSelectorPathSegment("hardware:" + hardware.HardwareId, name));
        }

        // Normal discovery carries LHM's own TypeNode text ("Temperatures", "Loads", ...).
        // The fallback is only for incomplete/old cached metadata.
        var sensorGroup = string.IsNullOrWhiteSpace(entry.SensorGroupName)
            ? HumanizeSensorType(entry.SensorType)
            : entry.SensorGroupName!.Trim();
        var nearestHardwareId = entry.HardwarePath.Count > 0
            ? entry.HardwarePath[^1].HardwareId
            : entry.HardwareId ?? string.Empty;
        displayPath.Add(new MetricSelectorPathSegment(
            $"sensor-group:{nearestHardwareId}:{entry.SensorType}",
            sensorGroup));

        var sensorName = string.IsNullOrWhiteSpace(entry.SensorName)
            ? entry.SensorId
            : entry.SensorName!.Trim();
        displayPath.Add(new MetricSelectorPathSegment("metric:" + entry.MetricId, sensorName));

        return new MetricSelectorMetric(entry.MetricId, available, displayPath);
    }

    private static MetricSelectorMetric CreateIcueMetric(IcueSensorLogMetricEntry entry, bool available)
    {
        var displayPath = new List<MetricSelectorPathSegment>
        {
            new("provider:icue", "iCUE"),
            new("icue-device:" + entry.DeviceName, entry.DeviceName),
            new($"icue-group:{entry.DeviceName}:{entry.SensorGroupName}", entry.SensorGroupName),
            new("metric:" + entry.MetricId, entry.SensorName)
        };

        return new MetricSelectorMetric(entry.MetricId, available, displayPath);
    }

    private static string HumanizeSensorType(string sensorType)
    {
        var value = sensorType?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return "Sensors";

        return value.ToLowerInvariant() switch
        {
            "voltage" => "Voltages",
            "current" => "Currents",
            "power" => "Powers",
            "clock" => "Clocks",
            "temperature" => "Temperatures",
            "load" => "Loads",
            "frequency" => "Frequencies",
            "fan" => "Fans",
            "flow" => "Flows",
            "control" => "Controls",
            "level" => "Levels",
            "factor" => "Factors",
            "data" => "Data",
            "smalldata" => "Small Data",
            "throughput" => "Throughput",
            "timespan" => "Time",
            "timing" => "Timings",
            "energy" => "Energy",
            "noise" => "Noise",
            "conductivity" => "Conductivity",
            "humidity" => "Humidity",
            _ => value
        };
    }
}

internal sealed record MetricSelectorPathSegment(
    string Key,
    string Text);

internal sealed record MetricSelectorMetric(
    string Id,
    bool Available,
    IReadOnlyList<MetricSelectorPathSegment> DisplayPath);

internal sealed record MetricSelectorCatalogSnapshot(
    IReadOnlyList<MetricSelectorMetric> Metrics,
    MetricSelectorMetric? GhostMetric,
    string? StatusText);

internal sealed class MetricNameEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
        UITypeEditorEditStyle.Modal;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        var currentMetric = Convert.ToString(value);
        var snapshot = MetricSelectorEnvironment.BuildSnapshot(currentMetric);
        using var picker = new MetricPickerDialog(snapshot, currentMetric);
        var owner = Form.ActiveForm;
        var result = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        return result == DialogResult.OK
            ? picker.SelectedMetric ?? value
            : value;
    }
}

internal sealed class MetricPickerDialog : PinkieEditorForm
{
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        FullRowSelect = true,
        HideSelection = false,
        ShowLines = true,
        ShowPlusMinus = true,
        ShowRootLines = true,
        ShowNodeToolTips = true
    };

    private readonly Button _ok = new()
    {
        Text = "OK",
        DialogResult = DialogResult.OK,
        AutoSize = true,
        Enabled = false
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private TreeNode? _currentNode;

    public MetricPickerDialog(MetricSelectorCatalogSnapshot snapshot, string? currentMetric)
    {
        Text = "Select Metric";
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 650;
        MinimumSize = new Size(460, 420);
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_tree, 0, 0);
        root.Controls.Add(_status, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = _ok;
        CancelButton = cancel;

        PopulateTree(snapshot, currentMetric);
        _status.Text = snapshot.StatusText ?? string.Empty;

        _tree.AfterSelect += (_, _) => UpdateSelectionState();
        _tree.NodeMouseDoubleClick += (_, e) => CommitNode(e.Node);
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || _tree.SelectedNode is null)
                return;

            if (IsSelectable(_tree.SelectedNode))
                CommitNode(_tree.SelectedNode);
            else if (_tree.SelectedNode.Nodes.Count > 0)
                _tree.SelectedNode.Toggle();

            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        Shown += (_, _) =>
        {
            if (_currentNode is null)
                return;

            ExpandAncestors(_currentNode);
            _tree.SelectedNode = _currentNode;
            _currentNode.EnsureVisible();
            UpdateSelectionState();
        };
    }

    public string? SelectedMetric { get; private set; }

    private void PopulateTree(MetricSelectorCatalogSnapshot snapshot, string? currentMetric)
    {
        var branchIndex = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        var metricNodeIndex = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var metric in snapshot.Metrics
                     .OrderBy(BuildDisplaySortKey, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase))
        {
            AddMetric(metric, isGhost: false, branchIndex, metricNodeIndex);
        }

        if (snapshot.GhostMetric is not null)
            AddMetric(snapshot.GhostMetric, isGhost: true, branchIndex, metricNodeIndex);

        if (!string.IsNullOrWhiteSpace(currentMetric) &&
            metricNodeIndex.TryGetValue(currentMetric, out var currentNode) &&
            currentNode.Tag is MetricTreeNodeState state &&
            state.IsMetric)
        {
            _currentNode = currentNode;
        }
    }

    private void AddMetric(
        MetricSelectorMetric metric,
        bool isGhost,
        IDictionary<string, TreeNode> branchIndex,
        IDictionary<string, TreeNode> metricNodeIndex)
    {
        if (metric.DisplayPath.Count == 0)
            return;

        TreeNodeCollection nodes = _tree.Nodes;
        var identityPath = string.Empty;
        TreeNode? node = null;

        for (var i = 0; i < metric.DisplayPath.Count; i++)
        {
            var segment = metric.DisplayPath[i];
            identityPath = identityPath.Length == 0
                ? segment.Key
                : identityPath + "\u001F" + segment.Key;

            if (!branchIndex.TryGetValue(identityPath, out node))
            {
                var state = new MetricTreeNodeState(identityPath, segment.Text);
                node = new TreeNode(segment.Text) { Tag = state };
                nodes.Add(node);
                branchIndex.Add(identityPath, node);
            }

            nodes = node.Nodes;
        }

        if (node is null || node.Tag is not MetricTreeNodeState leafState)
            return;

        leafState.MetricId = metric.Id;
        leafState.Available = metric.Available;
        leafState.IsGhost = isGhost;
        node.ToolTipText = metric.Id;
        if (isGhost)
        {
            node.Text = leafState.Text + "  [unavailable]";
            node.ForeColor = SystemColors.GrayText;
        }

        metricNodeIndex[metric.Id] = node;
    }

    private static string BuildDisplaySortKey(MetricSelectorMetric metric) =>
        string.Join('\u001F', metric.DisplayPath.Select(segment => segment.Text));

    private void UpdateSelectionState()
    {
        var selectable = IsSelectable(_tree.SelectedNode);
        _ok.Enabled = selectable;
        SelectedMetric = selectable && _tree.SelectedNode?.Tag is MetricTreeNodeState state
            ? state.MetricId
            : null;
    }

    private void CommitNode(TreeNode? node)
    {
        if (!IsSelectable(node) || node?.Tag is not MetricTreeNodeState state)
            return;

        SelectedMetric = state.MetricId;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool IsSelectable(TreeNode? node) =>
        node?.Tag is MetricTreeNodeState state &&
        state.IsMetric &&
        state.Available &&
        !state.IsGhost;

    private static void ExpandAncestors(TreeNode node)
    {
        var ancestors = new Stack<TreeNode>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            ancestors.Push(parent);

        while (ancestors.Count > 0)
            ancestors.Pop().Expand();
    }

    private sealed class MetricTreeNodeState(string identityPath, string text)
    {
        public string IdentityPath { get; } = identityPath;
        public string Text { get; } = text;
        public string? MetricId { get; set; }
        public bool IsMetric => !string.IsNullOrWhiteSpace(MetricId);
        public bool Available { get; set; }
        public bool IsGhost { get; set; }
    }
}
