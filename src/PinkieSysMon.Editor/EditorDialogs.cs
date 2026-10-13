namespace PinkieSysMon.Editor;

internal enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel
}

internal sealed class NewDashboardDialog : PinkieEditorForm
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    public NewDashboardDialog()
    {
        Text = "New Dashboard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 145);

        _orientation.Items.AddRange(["0°", "90°", "180°", "270°"]);
        _orientation.SelectedIndex = 0;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Orientation:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 1);
        layout.Controls.Add(_orientation, 1, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var create = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = create;
        CancelButton = cancel;
        Shown += (_, _) => _name.Focus();
    }

    public string DashboardName => _name.Text;
    public int Orientation => _orientation.SelectedIndex switch { 1 => 90, 2 => 180, 3 => 270, _ => 0 };
}

internal sealed class CloneDashboardDialog : PinkieEditorForm
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };

    public CloneDashboardDialog(string suggestedName)
    {
        Text = "Clone Dashboard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 108);

        _name.Text = suggestedName;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 0);
        layout.Controls.Add(_name, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var clone = new Button { Text = "Clone", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clone);
        layout.Controls.Add(buttons, 0, 1);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = clone;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public string DashboardName => _name.Text;
}

internal sealed class OpenDashboardDialog : PinkieEditorForm
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public OpenDashboardDialog(IEnumerable<string> dashboards, string currentDashboard)
    {
        Text = "Open Dashboard";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 420);
        MinimumSize = new Size(320, 260);

        foreach (var dashboard in dashboards)
            _list.Items.Add(dashboard);

        if (_list.Items.Count > 0)
        {
            var index = _list.Items.IndexOf(currentDashboard);
            _list.SelectedIndex = index >= 0 ? index : 0;
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var open = new Button { Text = "Open", DialogResult = DialogResult.OK, AutoSize = true, Enabled = _list.SelectedItem is not null };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);

        _list.SelectedIndexChanged += (_, _) => open.Enabled = _list.SelectedItem is not null;
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = open;
        CancelButton = cancel;
    }

    public string? SelectedDashboard => _list.SelectedItem as string;
}

internal sealed partial class EditorSettingsDialog : PinkieEditorForm
{
    private readonly EditorSettingsHelp _help;
    private readonly CheckBox _startRuntime = new()
    {
        Text = "Start PinkieSysMon runtime with Windows",
        AutoSize = true
    };
    private readonly CheckBox _systemProvider = new() { Text = "System", AutoSize = true };
    private readonly CheckBox _libreHardwareMonitorProvider = new()
    {
        Text = "LibreHardwareMonitor",
        AutoSize = true
    };
    private readonly CheckBox _icueProvider = new() { Text = "iCUE", AutoSize = true };
    private readonly ComboBox _toolbarIconSize = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 130
    };
    private readonly ComboBox _theme = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 130
    };

    private readonly DesktopSettingsModel _desktops;
    private readonly IReadOnlyList<string> _dashboardChoices;
    private readonly ListBox _desktopList = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, AllowDrop = true
    };
    private readonly Button _createDesktop = new() { Text = "Create New", AutoSize = true };
    private readonly Button _removeDesktop = new() { Text = "Remove", AutoSize = true };
    private readonly TextBox _desktopName = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _desktopDashboard = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly CheckBox _boardEnabled = new() { Text = "Board Enabled", AutoSize = true };
    private readonly CheckBox _providerEnabled = new() { Text = "Provider Enabled", AutoSize = true };
    private readonly CheckBox _desktopLock = new() { Text = "Lock (click-through)", AutoSize = true };
    private readonly NumericUpDown _desktopInterval = new()
    {
        Minimum = 10, Maximum = int.MaxValue, Width = 160, ThousandsSeparator = true
    };
    private readonly NumericUpDown _desktopX = new()
    {
        Minimum = int.MinValue, Maximum = int.MaxValue, Width = 160
    };
    private readonly NumericUpDown _desktopY = new()
    {
        Minimum = int.MinValue, Maximum = int.MaxValue, Width = 160
    };
    private readonly Panel _desktopDetails = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private OutputTargetConfig? _selectedDesktop;
    private bool _bindingDesktop;
    private int _desktopDragIndex = -1;

    // Stage21 owns app-wide preferences; Stage22 owns virtual Output controls.
    // The physical Device pages are added separately in Stage23.
    public EditorSettingsDialog(
        bool startRuntimeWithWindows,
        bool systemProviderEnabled,
        bool libreHardwareMonitorProviderEnabled,
        bool icueProviderEnabled,
        int toolbarIconSize,
        EditorThemeMode themeMode,
        AppConfig config,
        IReadOnlyList<string> dashboards,
        Func<IReadOnlyList<OutputDeviceDescriptor>>? discoverDevices = null,
        string? applicationRoot = null)
    {
        _help = new EditorSettingsHelp(applicationRoot);
        _dashboardChoices = dashboards;
        _devices = new DeviceSettingsModel(config, dashboards.FirstOrDefault() ?? config.Dashboard.Active);
        _desktops = new DesktopSettingsModel(config, () => _devices.ConfiguredCount);
        _discoverDevices = discoverDevices ?? DeviceDiscovery.EnumerateTrofeoDevices;
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 600);

        _startRuntime.Checked = startRuntimeWithWindows;
        _systemProvider.Checked = systemProviderEnabled;
        _libreHardwareMonitorProvider.Checked = libreHardwareMonitorProviderEnabled;
        _icueProvider.Checked = icueProviderEnabled;
        _toolbarIconSize.Items.AddRange(["16 × 16", "32 × 32", "48 × 48"]);
        _toolbarIconSize.SelectedIndex = toolbarIconSize switch { 16 => 0, 48 => 2, _ => 1 };
        _theme.Items.AddRange([
            EditorThemeContract.SystemSetting,
            EditorThemeContract.LightSetting,
            EditorThemeContract.DarkSetting
        ]);
        _theme.SelectedItem = EditorThemeContract.Serialize(themeMode);

        var generalGroup = new GroupBox
        {
            Text = "Startup",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        generalGroup.Controls.Add(_help.ToggleRows((_startRuntime, EditorSettingsHelp.Startup)));

        var providersGroup = new GroupBox
        {
            Text = "Metric providers",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        providersGroup.Controls.Add(_help.ToggleRows(
            (_systemProvider, EditorSettingsHelp.SystemProvider),
            (_libreHardwareMonitorProvider, EditorSettingsHelp.LhmProvider),
            (_icueProvider, EditorSettingsHelp.IcueProvider)));

        var interfaceGroup = new GroupBox
        {
            Text = "Interface",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        var interfaceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(4, 6, 4, 4)
        };
        interfaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        interfaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        interfaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        interfaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        interfaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        interfaceLayout.Controls.Add(new Label
        {
            Text = "Theme", AutoSize = true, Anchor = AnchorStyles.Left
        }, 0, 0);
        interfaceLayout.Controls.Add(_theme, 1, 0);
        interfaceLayout.Controls.Add(_help.Marker("Theme", _theme, EditorSettingsHelp.Theme), 2, 0);
        interfaceLayout.Controls.Add(new Label
        {
            Text = "Toolbar icon size", AutoSize = true, Anchor = AnchorStyles.Left
        }, 0, 1);
        interfaceLayout.Controls.Add(_toolbarIconSize, 1, 1);
        interfaceLayout.Controls.Add(_help.Marker("Toolbar icon size", _toolbarIconSize, EditorSettingsHelp.ToolbarSize), 2, 1);
        interfaceGroup.Controls.Add(interfaceLayout);

        var general = new TabPage("General") { AutoScroll = true };
        // WinForms Top docking lays out in reverse z-order.
        general.Controls.Add(interfaceGroup);
        general.Controls.Add(providersGroup);
        general.Controls.Add(generalGroup);
        var tabs = _settingsTabs;
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(BuildDesktopPage());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(tabs);
        Controls.Add(_discoveryNotice);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        RefreshDeviceDiscovery();
    }

    public bool StartRuntimeWithWindows => _startRuntime.Checked;
    public bool SystemProviderEnabled => _systemProvider.Checked;
    public bool LibreHardwareMonitorProviderEnabled => _libreHardwareMonitorProvider.Checked;
    public bool IcueProviderEnabled => _icueProvider.Checked;
    public int ToolbarIconSize => _toolbarIconSize.SelectedIndex switch { 0 => 16, 2 => 48, _ => 32 };
    public EditorThemeMode ThemeMode => EditorThemeContract.Parse(_theme.SelectedItem as string);

    public bool DesktopChanged => _desktops.IsDirty;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _help.Dispose();
        base.Dispose(disposing);
    }

    private TabPage BuildDesktopPage()
    {
        var page = new TabPage("Desktop");
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false
        };
        leftButtons.Controls.Add(_createDesktop);
        leftButtons.Controls.Add(_removeDesktop);
        _help.Bind(_createDesktop, EditorSettingsHelp.CreateDesktop);
        _help.Bind(_removeDesktop, EditorSettingsHelp.RemoveDesktop);
        _help.Bind(_desktopList, EditorSettingsHelp.Stacking);
        left.Controls.Add(_desktopList, 0, 0);
        left.Controls.Add(leftButtons, 0, 1);
        columns.Controls.Add(left, 0, 0);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3,
            Padding = new Padding(12)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void AddRow(string caption, Control control, string description)
        {
            var row = details.RowCount++;
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 9, 3, 9)
            }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            details.Controls.Add(control, 1, row);
            details.Controls.Add(_help.Marker(caption, control, description), 2, row);
        }

        AddRow("Display name", _desktopName, EditorSettingsHelp.DisplayName);
        AddRow("Dashboard", _desktopDashboard, EditorSettingsHelp.Dashboard);
        AddRow("Display", _boardEnabled, EditorSettingsHelp.BoardEnabled);
        AddRow("Diagnostics", _providerEnabled, EditorSettingsHelp.OutputProvider);
        AddRow("Mouse input", _desktopLock, EditorSettingsHelp.Lock);
        AddRow("Refresh interval (ms)", _desktopInterval, EditorSettingsHelp.DesktopRefresh);
        AddRow("Position X", _desktopX, EditorSettingsHelp.PositionX);
        AddRow("Position Y", _desktopY, EditorSettingsHelp.PositionY);
        var orderHint = new Label
        {
            Text = "Drag the Desktop entries on the left to change their stacking order.",
            AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(4, 12, 4, 4)
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(orderHint, 0, details.RowCount);
        details.SetColumnSpan(orderHint, 3);
        _help.Bind(orderHint, EditorSettingsHelp.Stacking);
        details.RowCount++;

        _desktopDashboard.Items.AddRange(_dashboardChoices.Cast<object>().ToArray());
        _desktopDetails.Controls.Add(details);
        columns.Controls.Add(_desktopDetails, 1, 0);
        page.Controls.Add(columns);

        _desktopList.SelectedIndexChanged += (_, _) =>
        {
            if (!_bindingDesktop)
                BindSelectedDesktop();
        };
        _desktopList.MouseDown += (_, e) =>
            _desktopDragIndex = e.Button == MouseButtons.Left
                ? _desktopList.IndexFromPoint(e.Location) : -1;
        _desktopList.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _desktopDragIndex >= 0)
            {
                var source = _desktopDragIndex;
                _desktopDragIndex = -1;
                _desktopList.DoDragDrop(source, DragDropEffects.Move);
            }
        };
        _desktopList.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
        };
        _desktopList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(int)) is not int source)
                return;
            var point = _desktopList.PointToClient(new Point(e.X, e.Y));
            var dest = _desktopList.IndexFromPoint(point);
            if (dest < 0)
                dest = _desktops.Desktops.Count - 1;
            if (source < 0 || dest < 0 || source == dest)
                return;
            var selection = _desktops.Desktops[source];
            _desktops.Move(source, dest);
            ReloadDesktopList(selection);
        };

        _createDesktop.Click += (_, _) =>
        {
            var dashboard = _selectedDesktop?.Dashboard ?? _dashboardChoices.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(dashboard))
            {
                MessageBox.Show("Create a dashboard first.", "Desktop Output",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var created = _desktops.Create(dashboard);
            ReloadDesktopList(created);
            UpdateDevicePages();
        };
        _removeDesktop.Click += (_, _) =>
        {
            var selected = _selectedDesktop;
            if (selected is null || !CanRemoveDesktop(selected))
                return;
            var decision = MessageBox.Show(
                $"Remove Desktop Output '{selected.Name}'? Its dashboard files and other Outputs will remain.",
                "Remove Desktop Output", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (decision != DialogResult.Yes)
                return;
            _desktops.Remove(selected);
            ReloadDesktopList(_desktops.Desktops.FirstOrDefault());
            UpdateDevicePages();
        };

        _desktopName.TextChanged += (_, _) => ChangeSelectedDesktop(t => {
            t.Name = _desktopName.Text;
            RefreshDesktopListLabel();
        });
        _desktopDashboard.SelectedIndexChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Dashboard = _desktopDashboard.SelectedItem as string);
        _boardEnabled.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.BoardEnabled = _boardEnabled.Checked);
        _providerEnabled.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.ProviderEnabled = _providerEnabled.Checked);
        _desktopLock.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.Lock = _desktopLock.Checked);
        _desktopInterval.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.RefreshIntervalMs = Decimal.ToInt32(_desktopInterval.Value));
        _desktopX.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.X = Decimal.ToInt32(_desktopX.Value));
        _desktopY.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.Y = Decimal.ToInt32(_desktopY.Value));

        ReloadDesktopList(_desktops.Desktops.FirstOrDefault());
        return page;
    }

    private void ChangeSelectedDesktop(Action<OutputTargetConfig> change)
    {
        if (_bindingDesktop || _selectedDesktop is null)
            return;
        change(_selectedDesktop);
        _desktops.MarkChanged();
    }

    private void RefreshDesktopListLabel()
    {
        var index = _desktopList.SelectedIndex;
        if (index < 0 || _selectedDesktop is null)
            return;
        _bindingDesktop = true;
        try
        {
            _desktopList.Items[index] = new DesktopListItem(_selectedDesktop);
            _desktopList.SelectedIndex = index;
        }
        finally { _bindingDesktop = false; }
    }

    private void ReloadDesktopList(OutputTargetConfig? select)
    {
        _bindingDesktop = true;
        try
        {
            _desktopList.Items.Clear();
            foreach (var desktop in _desktops.Desktops)
                _desktopList.Items.Add(new DesktopListItem(desktop));
            _desktopList.SelectedIndex = select is null ? -1 :
                _desktops.Desktops.ToList().IndexOf(select);
        }
        finally { _bindingDesktop = false; }
        BindSelectedDesktop();
    }

    private void BindSelectedDesktop()
    {
        _bindingDesktop = true;
        try
        {
            _selectedDesktop = (_desktopList.SelectedItem as DesktopListItem)?.Target;
            var target = _selectedDesktop;
            _desktopDetails.Enabled = target is not null;
            _removeDesktop.Enabled = target is not null && CanRemoveDesktop(target);
            _desktopName.Text = target?.Name ?? string.Empty;
            var dashboard = target?.Dashboard;
            if (!string.IsNullOrWhiteSpace(dashboard) &&
                !_desktopDashboard.Items.Cast<string>().Contains(dashboard, StringComparer.OrdinalIgnoreCase))
                _desktopDashboard.Items.Add(dashboard);
            _desktopDashboard.SelectedItem = dashboard;
            _boardEnabled.Checked = target?.Desktop?.BoardEnabled ?? false;
            _providerEnabled.Checked = target?.ProviderEnabled ?? false;
            _desktopLock.Checked = target?.Desktop?.Lock ?? false;
            _desktopInterval.Value = target?.RefreshIntervalMs ?? 1000;
            _desktopX.Value = target?.Desktop?.X ?? 0;
            _desktopY.Value = target?.Desktop?.Y ?? 0;
        }
        finally { _bindingDesktop = false; }
    }

    private sealed record DesktopListItem(OutputTargetConfig Target)
    {
        public override string ToString() => Target.Name;
    }
}


internal static class EditorDialogs
{
    public static OutputDashboardChoice ChooseOutputDashboard(
        IWin32Window owner, RuntimeOutputStatus output, string editorDashboard)
    {
        using var dialog = new Form
        {
            Text = "Start / Reload Output",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            ClientSize = new Size(620, 230)
        };
        var label = new Label
        {
            Text = $"Output: {output.TargetName} ({output.TargetId})\n" +
                $"Assigned dashboard: {output.Dashboard}\nEditor dashboard: {editorDashboard}\n\n" +
                "Use the editor dashboard on this Output, or reload its assigned dashboard. Runtime uses the last saved version.",
            Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16)
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var keep = new Button { Text = "Keep assigned dashboard", DialogResult = DialogResult.No, AutoSize = true };
        var use = new Button { Text = "Use editor dashboard", DialogResult = DialogResult.Yes, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(keep);
        buttons.Controls.Add(use);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = cancel;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) switch
        {
            DialogResult.Yes => OutputDashboardChoice.UseEditorDashboard,
            DialogResult.No => OutputDashboardChoice.KeepAssignedDashboard,
            _ => OutputDashboardChoice.Cancel
        };
    }

    public static SaveChangesChoice ConfirmSaveChanges(IWin32Window owner, string dashboardName)
    {
        using var dialog = new Form
        {
            Text = "PinkieSysMon Editor",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(500, 145)
        };

        var label = new Label
        {
            Text = $"Save changes to \"{dashboardName}\"?",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 75,
            Padding = new Padding(16, 12, 16, 0)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var dontSave = new Button { Text = "Don't Save", DialogResult = DialogResult.No, AutoSize = true };
        var save = new Button { Text = "Save", DialogResult = DialogResult.Yes, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(dontSave);
        buttons.Controls.Add(save);

        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(owner) switch
        {
            DialogResult.Yes => SaveChangesChoice.Save,
            DialogResult.No => SaveChangesChoice.DontSave,
            _ => SaveChangesChoice.Cancel
        };
    }
}
