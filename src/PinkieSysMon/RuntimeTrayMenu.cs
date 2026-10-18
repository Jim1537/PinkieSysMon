namespace PinkieSysMon;

// Owns native menu items only. Snapshot projection never captures telemetry or USB.
internal sealed class RuntimeTrayMenu : IDisposable
{
    private readonly ToolStripMenuItem _desktopHeading = Information("Desktop");
    private readonly ToolStripMenuItem _physicalHeading = Information("Physical Devices");
    private readonly Action<RuntimeIpcCommand> _execute;
    private readonly Action<string, bool> _setPhysicalEnabled;
    private readonly List<ToolStripMenuItem> _outputs = [];

    public ContextMenuStrip Menu { get; } = new();
    public ToolStripMenuItem Status { get; } = Information("Starting...");
    public ToolStripMenuItem ControlCenter { get; }

    public RuntimeTrayMenu(Action openEditor, Action<RuntimeIpcCommand> execute,
        Action openLogs, Action<string, bool> setPhysicalEnabled)
    {
        _execute = execute;
        _setPhysicalEnabled = setPhysicalEnabled;
        ControlCenter = ActionItem("Control Center", openEditor);
        Menu.Items.Add(ControlCenter);
        Menu.Items.Add(new ToolStripSeparator());
        Menu.Items.Add(_desktopHeading);
        Menu.Items.Add(new ToolStripSeparator());
        Menu.Items.Add(_physicalHeading);
        Menu.Items.Add(new ToolStripSeparator());
        Menu.Items.Add(ActionItem("Reload All", () => _execute(new(RuntimeIpcCommandKind.StartOrReloadAll))));
        Menu.Items.Add(new ToolStripSeparator());
        Menu.Items.Add(Status);
        Menu.Items.Add(ActionItem("Open Logs Folder", openLogs));
        Menu.Items.Add(new ToolStripSeparator());
        Menu.Items.Add(ActionItem("Exit", () => _execute(new(RuntimeIpcCommandKind.Exit))));
    }

    public void RefreshOutputs(IReadOnlyList<OutputSessionStatus> outputs,
        IReadOnlyList<string> dashboards, IReadOnlySet<string> enableablePhysicalIds,
        IReadOnlyDictionary<string, int> jpegQuality)
    {
        ObjectDisposedException.ThrowIf(Menu.IsDisposed, this);
        var desktops = new List<ToolStripMenuItem>();
        var physical = new List<ToolStripMenuItem>();
        try
        {
            foreach (var output in outputs)
            {
                var group = output.Kind switch
                {
                    OutputTargetContract.DesktopKind => desktops,
                    OutputTargetContract.TrofeoKind or OutputTargetContract.TurzxKind => physical,
                    _ => throw new InvalidOperationException("Unsupported Output kind in tray snapshot.")
                };
                group.Add(BuildOutput(output, dashboards, enableablePhysicalIds.Contains(output.TargetId),
                    jpegQuality.TryGetValue(output.TargetId, out var quality) ? quality : null));
            }
        }
        catch
        {
            foreach (var item in desktops.Concat(physical)) item.Dispose();
            throw; // Keep the previous native menu if snapshot preparation fails.
        }

        var prepared = desktops.Concat(physical).ToArray();
        Menu.SuspendLayout();
        try
        {
            try
            {
                InsertAfter(_desktopHeading, desktops);
                InsertAfter(_physicalHeading, physical);
            }
            catch
            {
                foreach (var item in prepared)
                {
                    item.Dispose();
                }
                throw;
            }
            // WinForms removes owned items during disposal and records their disposed state.
            foreach (var item in _outputs) item.Dispose();
            _outputs.Clear();
            _outputs.AddRange(prepared);
        }
        finally { Menu.ResumeLayout(); }
    }

    private void InsertAfter(ToolStripMenuItem heading, List<ToolStripMenuItem> items)
    {
        var index = Menu.Items.IndexOf(heading) + 1;
        foreach (var item in items) Menu.Items.Insert(index++, item);
    }

    private ToolStripMenuItem BuildOutput(OutputSessionStatus output,
        IReadOnlyList<string> dashboards, bool canEnablePhysical, int? jpegQuality)
    {
        var item = new ToolStripMenuItem(Caption(output.TargetName)) { Tag = output.TargetId };
        try
        {
            var desktop = output.Kind == OutputTargetContract.DesktopKind;
            var canAct = RuntimeTrayOutputContract.CanUseActions(output.OutputEnabled);
            var enabled = ActionItem("Enabled", () =>
            {
                if (desktop)
                    _execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, output.TargetId, Value: !output.OutputEnabled));
                else
                    _setPhysicalEnabled(output.TargetId, !output.OutputEnabled);
            });
            enabled.Checked = output.OutputEnabled;
            enabled.Enabled = desktop || output.OutputEnabled || canEnablePhysical;
            enabled.ToolTipText = desktop ? "Show or unload this Desktop Output" :
                enabled.Enabled ? "Device use requires explicit consent before enabling" :
                "Enabling requires fresh, unambiguous connected device identity";
            item.DropDownItems.Add(enabled);

            var assignment = new ToolStripMenuItem("Dashboard")
            {
                Enabled = canAct && dashboards.Count > 0,
                ToolTipText = "Assigned dashboard: " + output.Dashboard
            };
            item.DropDownItems.Add(assignment);
            foreach (var dashboard in dashboards)
            {
                var choice = ActionItem(Caption(dashboard), () =>
                    _execute(new(RuntimeIpcCommandKind.AssignDashboard, output.TargetId, Dashboard: dashboard)));
                choice.Enabled = canAct;
                choice.Checked = dashboard.Equals(output.Dashboard, StringComparison.OrdinalIgnoreCase);
                assignment.DropDownItems.Add(choice);
            }
            var reload = ActionItem("Reload", () =>
                _execute(new(RuntimeIpcCommandKind.StartOrReloadTarget, output.TargetId)));
            reload.Enabled = canAct;
            item.DropDownItems.Add(reload);
            if (desktop)
            {
                var locked = ActionItem("Lock", () =>
                    _execute(new(RuntimeIpcCommandKind.SetDesktopLock, output.TargetId, Value: output.Lock != true)));
                locked.Checked = output.Lock == true;
                locked.Enabled = canAct;
                item.DropDownItems.Add(locked);
            }
            else
                item.DropDownItems.Add(Information($"Connected: {output.IsConnected}"));

            var frame = RuntimeTrayOutputContract.FrameValues(output);
            item.DropDownItems.Add(Information($"Provider Enabled: {output.ProviderEnabled}"));
            item.DropDownItems.Add(Information($"Refresh interval: {output.RefreshIntervalMs} ms"));
            item.DropDownItems.Add(Information($"Frame count: {frame.Count}"));
            item.DropDownItems.Add(Information($"Frame time: {frame.Time} ms"));
            if (!desktop)
            {
                item.DropDownItems.Add(Information($"JPEG Size: {frame.JpegKiB} KiB"));
                item.DropDownItems.Add(Information($"JPEG Quality: {(jpegQuality is >= 10 and <= 100 ? jpegQuality.ToString() : "N/A")}%"));
            }
            return item;
        }
        catch { item.Dispose(); throw; }
    }

    private static string Caption(string value) => value.Replace("&", "&&");
    private static ToolStripMenuItem Information(string text) => new(text) { Enabled = false };
    private static ToolStripMenuItem ActionItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => { if (!item.IsDisposed && item.Enabled) action(); };
        return item;
    }

    public void Dispose() => Menu.Dispose();
}
