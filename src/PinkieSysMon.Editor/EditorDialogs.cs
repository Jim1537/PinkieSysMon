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

internal sealed class EditorSettingsDialog : PinkieEditorForm
{
    private readonly CheckBox _startRuntime = new()
    {
        Text = "Start PinkieSysMon runtime with Windows",
        AutoSize = true
    };

    private readonly CheckBox _systemProvider = new()
    {
        Text = "System",
        AutoSize = true
    };

    private readonly CheckBox _libreHardwareMonitorProvider = new()
    {
        Text = "LibreHardwareMonitor",
        AutoSize = true
    };

    private readonly CheckBox _icueProvider = new()
    {
        Text = "iCUE",
        AutoSize = true
    };

    private readonly NumericUpDown _refreshIntervalMs = new()
    {
        Minimum = 10,
        Maximum = int.MaxValue,
        DecimalPlaces = 0,
        ThousandsSeparator = true,
        Width = 130
    };

    private readonly ComboBox _outputDevice = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300
    };

    private readonly NumericUpDown _jpegQuality = new()
    {
        Minimum = 10,
        Maximum = 100,
        DecimalPlaces = 0,
        Width = 130
    };

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

    public EditorSettingsDialog(
        bool startRuntimeWithWindows,
        bool systemProviderEnabled,
        bool libreHardwareMonitorProviderEnabled,
        bool icueProviderEnabled,
        int refreshIntervalMs,
        string outputDeviceId,
        IReadOnlyList<OutputDeviceDescriptor> outputDevices,
        int jpegQuality,
        int toolbarIconSize,
        EditorThemeMode themeMode)
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 632);

        _startRuntime.Checked = startRuntimeWithWindows;
        _systemProvider.Checked = systemProviderEnabled;
        _libreHardwareMonitorProvider.Checked = libreHardwareMonitorProviderEnabled;
        _icueProvider.Checked = icueProviderEnabled;
        _refreshIntervalMs.Value = refreshIntervalMs;
        PopulateOutputDevices(outputDeviceId, outputDevices);
        _jpegQuality.Value = jpegQuality;
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
            Text = "General",
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(12)
        };
        _startRuntime.Location = new Point(16, 32);
        generalGroup.Controls.Add(_startRuntime);

        var providersGroup = new GroupBox
        {
            Text = "Metric providers",
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(12)
        };
        _systemProvider.Location = new Point(16, 30);
        _libreHardwareMonitorProvider.Location = new Point(16, 58);
        _icueProvider.Location = new Point(16, 86);
        providersGroup.Controls.Add(_systemProvider);
        providersGroup.Controls.Add(_libreHardwareMonitorProvider);
        providersGroup.Controls.Add(_icueProvider);

        var interfaceGroup = new GroupBox
        {
            Text = "Interface",
            Dock = DockStyle.Top,
            Height = 128,
            Padding = new Padding(12)
        };
        var interfaceLayout = CreateSettingsLayout(2);
        interfaceLayout.Controls.Add(new Label
        {
            Text = "Theme",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        interfaceLayout.Controls.Add(_theme, 1, 0);
        interfaceLayout.Controls.Add(new Label
        {
            Text = "Toolbar icon size",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        interfaceLayout.Controls.Add(_toolbarIconSize, 1, 1);
        interfaceGroup.Controls.Add(interfaceLayout);

        var displayGroup = new GroupBox
        {
            Text = "Display",
            Dock = DockStyle.Top,
            Height = 132,
            Padding = new Padding(12)
        };
        var displayLayout = CreateSettingsLayout(2);
        displayLayout.Controls.Add(new Label
        {
            Text = "RefreshIntervalMs",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        displayLayout.Controls.Add(_refreshIntervalMs, 1, 0);
        displayLayout.Controls.Add(new Label
        {
            Text = "Output device",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        displayLayout.Controls.Add(_outputDevice, 1, 1);
        displayGroup.Controls.Add(displayLayout);

        var jpegGroup = new GroupBox
        {
            Text = "JPEG",
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(12)
        };
        var jpegLayout = CreateSettingsLayout();
        jpegLayout.Controls.Add(new Label
        {
            Text = "JpegQuality",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        jpegLayout.Controls.Add(_jpegQuality, 1, 0);
        jpegGroup.Controls.Add(jpegLayout);

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

        Controls.Add(jpegGroup);
        Controls.Add(displayGroup);
        Controls.Add(interfaceGroup);
        Controls.Add(providersGroup);
        Controls.Add(generalGroup);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public bool StartRuntimeWithWindows => _startRuntime.Checked;
    public bool SystemProviderEnabled => _systemProvider.Checked;
    public bool LibreHardwareMonitorProviderEnabled => _libreHardwareMonitorProvider.Checked;
    public bool IcueProviderEnabled => _icueProvider.Checked;
    public int RefreshIntervalMs => Decimal.ToInt32(_refreshIntervalMs.Value);
    public string OutputDeviceId => (_outputDevice.SelectedItem as OutputDeviceOption)?.DeviceId ?? string.Empty;
    public int JpegQuality => Decimal.ToInt32(_jpegQuality.Value);
    public int ToolbarIconSize => _toolbarIconSize.SelectedIndex switch { 0 => 16, 2 => 48, _ => 32 };
    public EditorThemeMode ThemeMode => EditorThemeContract.Parse(_theme.SelectedItem as string);

    private void PopulateOutputDevices(string configuredDeviceId, IReadOnlyList<OutputDeviceDescriptor> devices)
    {
        var automatic = new OutputDeviceOption(
            string.Empty,
            "Automatic (requires exactly one compatible device)");
        _outputDevice.Items.Add(automatic);

        foreach (var device in devices)
            _outputDevice.Items.Add(new OutputDeviceOption(device.DeviceId, device.SelectionLabel));

        if (!string.IsNullOrWhiteSpace(configuredDeviceId))
        {
            var configured = _outputDevice.Items
                .Cast<OutputDeviceOption>()
                .FirstOrDefault(option => option.DeviceId.Equals(configuredDeviceId, StringComparison.OrdinalIgnoreCase));
            if (configured is null)
            {
                configured = new OutputDeviceOption(
                    configuredDeviceId,
                    $"Configured device unavailable [{ShortDeviceId(configuredDeviceId)}]");
                _outputDevice.Items.Add(configured);
            }
            _outputDevice.SelectedItem = configured;
            return;
        }

        if (devices.Count == 1)
        {
            _outputDevice.SelectedIndex = 1;
            return;
        }

        _outputDevice.SelectedItem = automatic;
    }

    private static string ShortDeviceId(string deviceId)
    {
        var separator = deviceId.IndexOf(':');
        var token = separator >= 0 ? deviceId[(separator + 1)..] : deviceId;
        return token.Length <= 8 ? token : token[..8];
    }

    private sealed record OutputDeviceOption(string DeviceId, string Label)
    {
        public override string ToString() => Label;
    }

    private static TableLayoutPanel CreateSettingsLayout(int rowCount = 1)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
            Padding = new Padding(4, 6, 4, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return layout;
    }
}

internal static class EditorDialogs
{
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
