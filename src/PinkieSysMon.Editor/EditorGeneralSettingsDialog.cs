namespace PinkieSysMon.Editor;

// App-wide and Editor preferences; does not own Desktop or physical Output drafts.
internal sealed class EditorGeneralSettingsDialog : PinkieEditorForm
{
    private readonly EditorSettingsHelp _help;
    private readonly CheckBox _startRuntime = new()
    {
        Text = "Start PinkieSysMon runtime with Windows",
        AutoSize = true
    };
    private readonly NumericUpDown _runtimeIdleMinutes = new()
    {
        Minimum = 0, Maximum = RuntimeIdlePolicy.MaximumTimeoutMinutes,
        Width = 130, ThousandsSeparator = true
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

    public EditorGeneralSettingsDialog(
        bool startRuntimeWithWindows,
        bool systemProviderEnabled,
        bool libreHardwareMonitorProviderEnabled,
        bool icueProviderEnabled,
        int toolbarIconSize,
        EditorThemeMode themeMode,
        AppConfig config,
        string? applicationRoot = null)
    {
        _help = new EditorSettingsHelp(applicationRoot);
        Text = "Editor Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 600);
        Padding = new Padding(12);

        _startRuntime.Checked = startRuntimeWithWindows;
        _runtimeIdleMinutes.Value = config.Runtime.IdleTimeoutMinutes;
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

        var idleGroup = new GroupBox
        {
            Text = "Runtime activity",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        var idleLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(4, 6, 4, 4)
        };
        idleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        idleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        idleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        idleLayout.Controls.Add(new Label
        {
            Text = "Idle timeout (minutes; 0 = disabled)", AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        idleLayout.Controls.Add(_runtimeIdleMinutes, 1, 0);
        idleLayout.Controls.Add(_help.Marker("Idle timeout", _runtimeIdleMinutes,
            EditorSettingsHelp.RuntimeIdleTimeout), 2, 0);
        idleGroup.Controls.Add(idleLayout);

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

        // Retain General's scrolling and top-docked group ordering at all DPI scales.
        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        content.Controls.Add(interfaceGroup);
        content.Controls.Add(providersGroup);
        content.Controls.Add(idleGroup);
        content.Controls.Add(generalGroup);
        Controls.Add(content);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;
        var applyTimingNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 54,
            Padding = new Padding(12, 4, 12, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = ApplyTimingNotice
        };
        Controls.Add(applyTimingNote);
        Controls.Add(buttons);
    }

    public bool StartRuntimeWithWindows => _startRuntime.Checked;
    public int RuntimeIdleTimeoutMinutes => Decimal.ToInt32(_runtimeIdleMinutes.Value);
    public bool SystemProviderEnabled => _systemProvider.Checked;
    public bool LibreHardwareMonitorProviderEnabled => _libreHardwareMonitorProvider.Checked;
    public bool IcueProviderEnabled => _icueProvider.Checked;
    public int ToolbarIconSize => _toolbarIconSize.SelectedIndex switch { 0 => 16, 2 => 48, _ => 32 };
    public EditorThemeMode ThemeMode => EditorThemeContract.Parse(_theme.SelectedItem as string);

    internal const string ApplyTimingNotice =
        "Changes in Settings take effect after OK (Cancel discards them). " +
        "Runtime and provider settings reload a responsive Runtime; otherwise they take effect at its next start. " +
        "Editor toolbar size updates after OK; a theme change requires restarting the Editor.";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _help.Dispose();
        base.Dispose(disposing);
    }
}
