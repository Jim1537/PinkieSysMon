namespace PinkieSysMon.Editor;

// Presentation only: descriptions never read configuration or touch a live Output.
internal sealed class EditorSettingsHelp : IDisposable
{
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 450,
        ReshowDelay = 150,
        AutoPopDelay = 15000,
        ShowAlways = true
    };

    public Label Marker(string caption, Control setting, string description)
    {
        var marker = new Label
        {
            Text = "(?)", AutoSize = true, UseMnemonic = false,
            Anchor = AnchorStyles.None, Margin = new Padding(6, 4, 3, 4),
            Cursor = Cursors.Help, TabStop = false,
            AccessibleName = $"Help for {caption}",
            AccessibleRole = AccessibleRole.StaticText
        };
        Bind(marker, description);
        Bind(setting, description);
        return marker;
    }

    public void Bind(Control control, string description)
    {
        control.AccessibleDescription = description;
        _toolTip.SetToolTip(control, description.Replace("\n", Environment.NewLine));
    }

    public TableLayoutPanel ToggleRows(params (CheckBox Control, string Description)[] rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows.Length,
            Padding = new Padding(4, 2, 4, 2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < rows.Length; row++)
        {
            var (control, description) = rows[row];
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 4, 3, 4);
            layout.Controls.Add(control, 0, row);
            layout.Controls.Add(Marker(control.Text, control, description), 1, row);
        }
        return layout;
    }

    public void Dispose() => _toolTip.Dispose();

    public const string Startup = "Start Runtime when you sign in to Windows.\nThe Dashboard Editor is not started automatically.";
    public const string SystemProvider = "Make built-in Windows, network, power and media metrics available.\nRuntime captures only readings requested by active dashboards.";
    public const string LhmProvider = "Read hardware sensors from Libre Hardware Monitor's local HTTP server.\nRun LHM separately with its Remote Web Server enabled.";
    public const string IcueProvider = "Read iCUE Sensor Logging CSV files from this application's logs folder.\nKeep iCUE running with Sensor Logging enabled.";
    public const string Theme = "Choose the Editor's interface colors. System follows Windows;\nLight and Dark override the Windows preference.";
    public const string ToolbarSize = "Set the Editor toolbar icon size in pixels.\nThis does not resize dashboard widgets.";
    public const string DisplayName = "Name shown in Settings and Output menus.\nThis does not rename the dashboard or change the stable Output ID.";
    public const string Dashboard = "Select this Output's saved dashboard.\nOther Outputs keep their own assignments; changes apply after OK.";
    public const string BoardEnabled = "Show this Desktop panel and render its dashboard.\nOff unloads the panel and removes its dashboard demand; Provider Enabled is independent.";
    public const string OutputProvider = "Make this Output's status and performance metrics available to dashboards.\nThis does not enable the panel or USB output; live values require an active Output.";
    public const string Lock = "Pass all mouse clicks through the Desktop panel to windows underneath.\nTurn Lock off to drag the panel; its appearance is unchanged.";
    public const string DesktopRefresh = "Requested interval between panel redraws, in milliseconds.\nLower values use more CPU; sensor polling has its own schedule.";
    public const string DeviceRefresh = "Requested interval between display frames, in milliseconds.\nActual FPS also depends on rendering and USB speed; sensor polling is separate.";
    public const string PositionX = "Horizontal position in physical desktop pixels; negative values are allowed.\nDragging an unlocked panel also updates its saved position.";
    public const string PositionY = "Vertical position in physical desktop pixels; negative values are allowed.\nDragging an unlocked panel also updates its saved position.";
    public const string Stacking = "Select a panel to edit only its settings. Drag list entries to reorder panels.\nEarlier entries are behind later entries; dashboard widget layers are separate.";
    public const string CreateDesktop = "Add a new virtual Desktop Output to this Settings draft.\nIt takes effect after OK; Cancel discards it.";
    public const string RemoveDesktop = "Remove the selected Desktop Output after OK, keeping its dashboard files.\nAt least one Output must remain.";
    public const string DeviceType = "The detected physical display model.\nRenaming the Output does not change its hardware binding.";
    public const string DeviceEnabled = "Allow Runtime to open this USB display and send dashboard frames after OK.\nClose other display-control software first; enabling requires confirmation.";
    public const string JpegQuality = "JPEG quality for this USB display, from 10 to 100.\nHigher values usually create larger frames and use more USB bandwidth.";
    public const string UsbRetry = "Wait this many milliseconds before another connection attempt after\na missing device or failed USB connection/transfer. It is not a transfer timeout.";
    public const string UsbTimeout = "Time limit requested for each USB read/write operation, in milliseconds.\nHigher values allow slower transfers but delay error recovery.";
    public const string UsbJournal = "Write a rotating USB event journal in the application's logs folder.\nUse for troubleshooting: it adds disk I/O and does not fix USB faults.";
    public const string AddDevice = "Include this detected device in the Settings draft without enabling USB.\nIt is saved after OK; discovery alone does not save it.";
    public const string RemoveDevice = "Remove this saved physical Output after OK, keeping its dashboard files.\nAt least one Output must remain; attached hardware is not automatically re-added.";
}
