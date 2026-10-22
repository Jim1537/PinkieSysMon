using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.Editor;

// Presentation only: descriptions never read configuration or touch a live Output.
internal sealed class EditorSettingsHelp : IDisposable
{
    private IIconAsset? _icon;
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 450,
        ReshowDelay = 150,
        AutoPopDelay = 15000,
        ShowAlways = true
    };

    public EditorSettingsHelp(string? applicationRoot = null) => ApplicationRoot = applicationRoot;

    internal string? ApplicationRoot { get; }

    public Label Marker(string caption, Control setting, string description)
    {
        var marker = new HelpMarker(CreateMarkerImage)
        {
            AutoSize = true, UseMnemonic = false,
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
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = rows.Length,
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

    private Image CreateMarkerImage(int pixelSize, Color foreground)
    {
        // Reuse the same Lucide resolver/renderer as the Editor toolbar. Load only
        // on paint so configuration-only fixtures do not acquire asset dependencies.
        _icon ??= IconAssetLoader.Load(GlobalAssetResolver.ResolveIconFromRoot(
            ApplicationRoot ?? ApplicationRootLocator.Find(AppContext.BaseDirectory), "lucide:circle-help"));
        using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        _icon.Draw(canvas, new SKRect(0, 0, pixelSize, pixelSize),
            new SKColor(foreground.R, foreground.G, foreground.B), 1f, "contain",
            loop: false, antialias: true);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap)
            ?? throw new InvalidOperationException("Could not create Settings help icon image.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode Settings help icon image.");
        using var stream = new MemoryStream(data.ToArray());
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    public void Dispose()
    {
        _toolTip.Dispose();
        _icon?.Dispose();
        _icon = null;
    }

    private sealed class HelpMarker(Func<int, Color, Image> createImage) : Label
    {
        private Image? _image;
        private int _pixelSize;
        private Color _foreground;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var side = LogicalToDeviceUnits(20);
            return new Size(side, side);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var theme = EditorShellTheme.CaptureCurrent();
            var foreground = Enabled ? theme.Foreground : theme.InactiveForeground;
            var pixelSize = LogicalToDeviceUnits(16);
            if (_image is null || _pixelSize != pixelSize || _foreground != foreground)
            {
                var next = createImage(pixelSize, foreground);
                _image?.Dispose();
                _image = next;
                _pixelSize = pixelSize;
                _foreground = foreground;
            }
            e.Graphics.DrawImageUnscaled(_image,
                (ClientSize.Width - pixelSize) / 2, (ClientSize.Height - pixelSize) / 2);
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }
    }

    public const string Startup = "Start Runtime when you sign in to Windows.\nThe Dashboard Editor is not started automatically.";
    public const string RuntimeIdleTimeout = "Pause Runtime polling, Desktop panels, and USB frame transfers after this many minutes without Windows keyboard/mouse input.\n0 disables the feature. Windows' active display-off timeout may shorten the effective interval; the saved value stays unchanged.\nThe Runtime remains running and resumes on user input; USB wake must be verified on hardware.";
    public const string SystemProvider = "Make built-in Windows, network, power and media metrics available.\nRuntime captures only readings requested by active dashboards.";
    public const string LhmProvider = "Read hardware sensors from Libre Hardware Monitor's local HTTP server.\nRun LHM separately with its Remote Web Server enabled.";
    public const string IcueProvider = "Read iCUE Sensor Logging CSV files from this application's logs folder.\nKeep iCUE running with Sensor Logging enabled.";
    public const string Theme = "Choose the Editor's interface colors. System follows Windows;\nLight and Dark override the Windows preference.";
    public const string ToolbarSize = "Set the Editor toolbar icon size in pixels.\nThis does not resize dashboard widgets.";
    public const string DisplayName = "Name shown in Settings and Output menus.\nThis does not rename the dashboard or change the stable Output ID.";
    public const string Dashboard = "Select this Output's saved dashboard.\nOther Outputs keep their own assignments; changes apply after OK.";
    public const string OutputEnabledCaption = "Enabled";
    public const string BoardEnabled = "Allow Runtime to show this Desktop panel and render its dashboard after OK.\nOff unloads the panel and removes its dashboard demand; Provider Enabled is independent.";
    public const string OutputProvider = "Make this Output's status and performance metrics available to dashboards.\nThis does not enable the panel or USB output; live values require an active Output.";
    public const string Lock = "Pass all mouse clicks through the Desktop panel to windows underneath.\nTurn Lock off to drag the panel; its appearance is unchanged.";
    public const string DesktopRefresh = "Requested interval between panel redraws, in milliseconds.\nLower values use more CPU; sensor polling has its own schedule.";
    public const string DeviceRefresh = "Requested interval between display frames, in milliseconds.\nActual FPS also depends on rendering and USB speed; sensor polling is separate.";
    public const string PositionX = "Horizontal position in physical desktop pixels; negative values are allowed.\nDragging an unlocked panel also updates its saved position.";
    public const string PositionY = "Vertical position in physical desktop pixels; negative values are allowed.\nDragging an unlocked panel also updates its saved position.";
    public const string Stacking = "Select a panel to edit only its settings. Drag list entries to reorder panels.\nEarlier entries are behind later entries; dashboard widget layers are separate.";
    public const string CreateDesktop = "Add a new virtual Desktop Output to this Settings draft.\nIt takes effect after OK; Cancel discards it.";
    public const string RemoveDesktop = "Remove the selected Desktop Output after OK, keeping its dashboard files.\nAt least one Output must remain.";
    public const string DeviceType = "The generic physical Output and its explicitly selected protocol.\nRenaming the Output does not change its hardware binding.";
    public const string DeviceEnabled = "Allow Runtime to open this USB display and send dashboard frames after OK.\nClose other display-control software first; enabling requires confirmation.\nOff unloads this Output; Provider Enabled is independent.";
    public const string JpegQuality = "JPEG quality for this USB display, from 10 to 100.\nHigher values usually create larger frames and use more USB bandwidth.";
    public const string UsbRetry = "Wait this many milliseconds before another connection attempt after\na missing device or failed USB connection/transfer. It is not a transfer timeout.";
    public const string UsbTimeout = "Time limit requested for each USB read/write operation, in milliseconds.\nHigher values allow slower transfers but delay error recovery.";
    public const string UsbJournal = "Write a rotating USB event journal in the application's logs folder.\nUse for troubleshooting: it adds disk I/O and does not fix USB faults.";
    public const string AddDevice = "After a clean Runtime Test and positive visual confirmation, save this candidate immediately.\nIts own editable working board is created or reused; the new Output remains disabled.";
    public const string RemoveDevice = "Remove this saved physical Output after OK, keeping its dashboard files.\nAt least one Output must remain; attached hardware is not automatically re-added.";
}
