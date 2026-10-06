using System.Globalization;
using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private const float MinEditorZoom = 0.05f;
    private const float MaxEditorZoom = 2f;

    private static readonly string[] ToolbarNewIcons = ["file-plus", "file-plus-2", "file"];
    private static readonly string[] ToolbarOpenIcons = ["folder-open", "folder"];
    private static readonly string[] ToolbarSaveIcons = ["save"];
    private static readonly string[] ToolbarReloadIcons = ["refresh-cw", "rotate-cw"];
    private static readonly string[] ToolbarAddIcons = ["plus"];
    private static readonly string[] ToolbarDuplicateIcons = ["copy", "copy-plus", "files"];
    private static readonly string[] ToolbarUndoIcons = ["undo-2", "undo", "rotate-ccw"];
    private static readonly string[] ToolbarRedoIcons = ["redo-2", "redo", "rotate-cw"];
    private static readonly string[] ToolbarGroupIcons = ["group", "combine", "layers"];
    private static readonly string[] ToolbarUngroupIcons = ["ungroup", "split", "unlink"];
    private static readonly string[] ToolbarZoomInIcons = ["zoom-in"];
    private static readonly string[] ToolbarZoomOutIcons = ["zoom-out"];
    private static readonly string[] ToolbarZoom100Icons = ["focus", "crosshair", "scan"];
    private static readonly string[] ToolbarFitHeightIcons = ["move-vertical", "maximize-2", "maximize"];
    private static readonly string[] ToolbarGridIcons = ["grid-3x3", "grid-2x2", "grid"];

    private readonly List<Image> _toolbarImages = [];
    private ToolStrip _toolbar = null!;

    private ToolStrip BuildToolbar()
    {
        _toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            ShowItemToolTips = true
        };

        _toolbarNewButton = CreateToolbarButton("New dashboard (Ctrl+N)", ToolbarNewIcons, (_, _) => ExecuteNewDashboardCommand());
        _toolbarOpenButton = CreateToolbarButton("Open dashboard (Ctrl+O)", ToolbarOpenIcons, (_, _) => ExecuteOpenDashboardCommand());
        _toolbarSaveButton = CreateToolbarButton("Save (Ctrl+S)", ToolbarSaveIcons, (_, _) => ExecuteSaveCommand());
        _toolbarReloadEditorButton = CreateToolbarButton("Reload Editor (F5)", ToolbarReloadIcons, (_, _) => ExecuteReloadEditorCommand());
        _toolbar.Items.Add(_toolbarNewButton);
        _toolbar.Items.Add(_toolbarOpenButton);
        _toolbar.Items.Add(_toolbarSaveButton);
        _toolbar.Items.Add(_toolbarReloadEditorButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarAddButton = CreateToolbarDropDownButton("Add widget", ToolbarAddIcons);
        foreach (var type in WidgetTypeContract.EditorAddableTypes)
        {
            var captured = type;
            _toolbarAddButton.DropDownItems.Add(new ToolStripMenuItem(WidgetTypeDisplayName(type), null, (_, _) => ExecuteAddWidgetCommand(captured)));
        }
        _toolbar.Items.Add(_toolbarAddButton);
        _toolbarDuplicateButton = CreateToolbarButton("Duplicate selected items (Ctrl+D)", ToolbarDuplicateIcons, (_, _) => ExecuteDuplicateCommand());
        _toolbar.Items.Add(_toolbarDuplicateButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarUndoButton = CreateToolbarButton("Undo (Ctrl+Z)", ToolbarUndoIcons, (_, _) => ExecuteUndoCommand());
        _toolbarRedoButton = CreateToolbarButton("Redo (Ctrl+Y)", ToolbarRedoIcons, (_, _) => ExecuteRedoCommand());
        _toolbar.Items.Add(_toolbarUndoButton);
        _toolbar.Items.Add(_toolbarRedoButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarGroupButton = CreateToolbarButton("Group selected items", ToolbarGroupIcons, (_, _) => ExecuteGroupSelectedCommand());
        _toolbarUngroupButton = CreateToolbarButton("Ungroup selected items", ToolbarUngroupIcons, (_, _) => ExecuteUngroupSelectedCommand());
        _toolbar.Items.Add(_toolbarGroupButton);
        _toolbar.Items.Add(_toolbarUngroupButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarAlignLeftButton = CreateAlignmentToolbarButton(
            "Align left to last selected object", AlignmentCommand.Left);
        _toolbarAlignHorizontalCenterButton = CreateAlignmentToolbarButton(
            "Align horizontal centers to last selected object", AlignmentCommand.HorizontalCenter);
        _toolbarAlignRightButton = CreateAlignmentToolbarButton(
            "Align right to last selected object", AlignmentCommand.Right);
        _toolbarAlignTopButton = CreateAlignmentToolbarButton(
            "Align top to last selected object", AlignmentCommand.Top);
        _toolbarAlignVerticalCenterButton = CreateAlignmentToolbarButton(
            "Align vertical centers to last selected object", AlignmentCommand.VerticalCenter);
        _toolbarAlignBottomButton = CreateAlignmentToolbarButton(
            "Align bottom to last selected object", AlignmentCommand.Bottom);
        _toolbar.Items.Add(_toolbarAlignLeftButton);
        _toolbar.Items.Add(_toolbarAlignHorizontalCenterButton);
        _toolbar.Items.Add(_toolbarAlignRightButton);
        _toolbar.Items.Add(_toolbarAlignTopButton);
        _toolbar.Items.Add(_toolbarAlignVerticalCenterButton);
        _toolbar.Items.Add(_toolbarAlignBottomButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarZoomInButton = CreateToolbarButton("Zoom in", ToolbarZoomInIcons, (_, _) => ExecuteZoomStepCommand(1));
        _toolbar.Items.Add(_toolbarZoomInButton);

        _toolbarZoomTextBox = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 64,
            Text = FormatZoomPercent(_zoom),
            ToolTipText = "Zoom percentage. Press Enter to apply."
        };
        _toolbarZoomTextBox.TextBoxTextAlign = HorizontalAlignment.Center;
        _toolbarZoomTextBox.KeyDown += ToolbarZoomTextBoxKeyDown;
        _toolbarZoomTextBox.Leave += (_, _) => UpdateZoomToolbarText();
        _toolbar.Items.Add(_toolbarZoomTextBox);

        _toolbarZoomOutButton = CreateToolbarButton("Zoom out", ToolbarZoomOutIcons, (_, _) => ExecuteZoomStepCommand(-1));
        _toolbarZoom100Button = CreateToolbarButton("Zoom 100%", ToolbarZoom100Icons, (_, _) => ExecuteSetZoomPercentCommand(100));
        _toolbarFitHeightButton = CreateToolbarButton("Fit dashboard to visible height", ToolbarFitHeightIcons, (_, _) => ExecuteFitHeightCommand());
        _toolbar.Items.Add(_toolbarZoomOutButton);
        _toolbar.Items.Add(_toolbarZoom100Button);
        _toolbar.Items.Add(_toolbarFitHeightButton);
        _toolbar.Items.Add(CreateToolbarSeparator());

        _toolbarGridButton = CreateCheckableToolbarSplitButton("Show or hide grid; use the arrow to select grid spacing", ToolbarGridIcons);
        _toolbarGridButton.ButtonClick += (_, _) => ExecuteToggleGridCommand();
        PopulateGridSizeMenu(_toolbarGridButton.DropDownItems);
        _toolbarGridButton.DropDownOpening += (_, _) => UpdateGridSizeMenuChecks(_toolbarGridButton.DropDownItems);
        _toolbar.Items.Add(_toolbarGridButton);

        _toolbar.ContextMenuStrip = BuildToolbarContextMenu();
        return _toolbar;
    }

    private ContextMenuStrip BuildToolbarContextMenu()
    {
        var context = new ContextMenuStrip();
        var iconSizeMenu = new ToolStripMenuItem("Icon Size");
        foreach (var size in new[] { 16, 32, 48 })
        {
            var captured = size;
            iconSizeMenu.DropDownItems.Add(new ToolStripMenuItem($"{size} × {size}", null, (_, _) => ExecuteSetToolbarIconSizeCommand(captured))
            {
                Tag = size,
                Checked = size == _toolbarIconSize
            });
        }
        iconSizeMenu.DropDownOpening += (_, _) => UpdateToolbarIconSizeMenuChecks(iconSizeMenu.DropDownItems);
        context.Items.Add(iconSizeMenu);
        return context;
    }

    private void UpdateToolbarIconSizeMenuChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menu && menu.Tag is int size)
                menu.Checked = size == _toolbarIconSize;
        }
    }

    private ToolStripButton CreateToolbarButton(string toolTip, string[] iconCandidates, EventHandler click)
    {
        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = TrackToolbarImage(CreateLucideToolbarImage(_toolbarIconSize, iconCandidates)),
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = toolTip,
            AutoToolTip = false
        };
        button.Click += click;
        return button;
    }

    private ToolStripDropDownButton CreateToolbarDropDownButton(string toolTip, string[] iconCandidates)
    {
        return new ToolStripDropDownButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = TrackToolbarImage(CreateLucideToolbarImage(_toolbarIconSize, iconCandidates)),
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = toolTip,
            AutoToolTip = false,
            ShowDropDownArrow = true
        };
    }

    private CheckableToolStripSplitButton CreateCheckableToolbarSplitButton(string toolTip, string[] iconCandidates)
    {
        return new CheckableToolStripSplitButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = TrackToolbarImage(CreateLucideToolbarImage(_toolbarIconSize, iconCandidates)),
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = toolTip,
            AutoToolTip = false
        };
    }

    private ToolStripButton CreateAlignmentToolbarButton(string toolTip, AlignmentCommand alignment)
    {
        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = TrackToolbarImage(CreateAlignmentToolbarImage(_toolbarIconSize, alignment)),
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = toolTip,
            AutoToolTip = false
        };
        button.Click += (_, _) => ExecuteAlignCommand(alignment);
        return button;
    }

    private Image CreateAlignmentToolbarImage(int pixelSize, AlignmentCommand alignment)
    {
        using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var scale = pixelSize / 32f;
        var objectStroke = Math.Max(1f, 1.5f * scale);
        var guideStroke = Math.Max(1f, 1.25f * scale);
        using var objectPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = objectStroke,
            Color = new SKColor(_shellTheme.Foreground.R, _shellTheme.Foreground.G, _shellTheme.Foreground.B),
            IsAntialias = true
        };
        using var guidePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = guideStroke,
            Color = new SKColor(_shellTheme.SelectionBackground.R, _shellTheme.SelectionBackground.G, _shellTheme.SelectionBackground.B),
            IsAntialias = true
        };

        float S(float value) => value * scale;
        var shortExtent = S(9f);
        var longExtent = S(15f);
        var thickness = S(6f);
        var first = S(7f);
        var second = S(19f);
        var near = S(5f);
        var middle = S(16f);
        var far = S(27f);

        if (alignment is AlignmentCommand.Left or AlignmentCommand.HorizontalCenter or AlignmentCommand.Right)
        {
            var edge = alignment switch
            {
                AlignmentCommand.Left => near,
                AlignmentCommand.HorizontalCenter => middle,
                _ => far
            };
            canvas.DrawLine(edge, S(3f), edge, S(29f), guidePaint);

            static SKRect HorizontalRect(float edge, float y, float width, float height, AlignmentCommand mode) => mode switch
            {
                AlignmentCommand.Left => new SKRect(edge, y, edge + width, y + height),
                AlignmentCommand.HorizontalCenter => new SKRect(edge - width / 2f, y, edge + width / 2f, y + height),
                _ => new SKRect(edge - width, y, edge, y + height)
            };

            canvas.DrawRect(HorizontalRect(edge, first, longExtent, thickness, alignment), objectPaint);
            canvas.DrawRect(HorizontalRect(edge, second, shortExtent, thickness, alignment), objectPaint);
        }
        else
        {
            var edge = alignment switch
            {
                AlignmentCommand.Top => near,
                AlignmentCommand.VerticalCenter => middle,
                _ => far
            };
            canvas.DrawLine(S(3f), edge, S(29f), edge, guidePaint);

            static SKRect VerticalRect(float x, float edge, float width, float height, AlignmentCommand mode) => mode switch
            {
                AlignmentCommand.Top => new SKRect(x, edge, x + width, edge + height),
                AlignmentCommand.VerticalCenter => new SKRect(x, edge - height / 2f, x + width, edge + height / 2f),
                _ => new SKRect(x, edge - height, x + width, edge)
            };

            canvas.DrawRect(VerticalRect(first, edge, thickness, longExtent, alignment), objectPaint);
            canvas.DrawRect(VerticalRect(second, edge, thickness, shortExtent, alignment), objectPaint);
        }

        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap)
            ?? throw new InvalidOperationException("Could not create alignment toolbar icon image.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode alignment toolbar icon image.");
        using var stream = new MemoryStream(data.ToArray());
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private sealed class ToolbarSeparatorItem : ToolStripLabel
    {
        public ToolbarSeparatorItem(int slotWidth, int lineHeight)
        {
            AutoSize = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Text = string.Empty;
            UpdateGeometry(slotWidth, lineHeight);
        }

        public void UpdateGeometry(int slotWidth, int lineHeight)
        {
            Size = new Size(Math.Max(1, slotWidth), Math.Max(1, lineHeight));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0)
                return;

            var x = (Width - 1) / 2;
            var inset = Math.Max(2, Height / 6);
            var bottom = Math.Max(inset, Height - inset - 1);
            using var pen = new Pen(SystemColors.ControlDarkDark, 1f);
            e.Graphics.DrawLine(pen, x, inset, x, bottom);
        }
    }

    private ToolbarSeparatorItem CreateToolbarSeparator() =>
        new(
            GetToolbarSeparatorWidth(_toolbarIconSize),
            GetToolbarSeparatorHeight(_toolbarIconSize));

    private static int GetToolbarSeparatorWidth(int iconSize) => Math.Max(10, (int)MathF.Ceiling(iconSize * 0.75f));

    private static int GetToolbarSeparatorHeight(int iconSize) => Math.Max(16, iconSize);

    private void UpdateToolbarSeparators(int iconSize)
    {
        if (_toolbar is null || _toolbar.IsDisposed)
            return;

        var width = GetToolbarSeparatorWidth(iconSize);
        var height = GetToolbarSeparatorHeight(iconSize);
        foreach (var separator in _toolbar.Items.OfType<ToolbarSeparatorItem>())
            separator.UpdateGeometry(width, height);
    }

    private Image TrackToolbarImage(Image image)
    {
        _toolbarImages.Add(image);
        return image;
    }

    private Image CreateLucideToolbarImage(int pixelSize, params string[] iconCandidates)
    {
        Exception? lastError = null;
        foreach (var iconName in iconCandidates)
        {
            try
            {
                var path = GlobalAssetResolver.ResolveIconFromRoot(_root, $"lucide:{iconName}");
                using var icon = IconAssetLoader.Load(path);
                using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                var margin = Math.Max(1f, pixelSize * 0.05f);
                icon.Draw(
                    canvas,
                    new SKRect(margin, margin, pixelSize - margin, pixelSize - margin),
                    new SKColor(_shellTheme.Foreground.R, _shellTheme.Foreground.G, _shellTheme.Foreground.B),
                    1f,
                    "contain",
                    loop: false,
                    antialias: true);
                canvas.Flush();

                using var image = SKImage.FromBitmap(bitmap)
                    ?? throw new InvalidOperationException("Could not create toolbar icon image.");
                using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("Could not encode toolbar icon image.");
                using var stream = new MemoryStream(data.ToArray());
                using var decoded = new Bitmap(stream);
                return new Bitmap(decoded);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"No Lucide toolbar icon could be loaded from candidates: {string.Join(", ", iconCandidates)}.",
            lastError);
    }

    private void ExecuteSetToolbarIconSizeCommand(int size)
    {
        if (size is not (16 or 32 or 48) || size == _toolbarIconSize)
            return;

        try
        {
            ApplyToolbarIconSize(size);
            SaveEditorSettings();
            SetStatus($"Toolbar icon size: {size} × {size}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not change toolbar icon size.\n\n{ex.Message}", "Toolbar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyToolbarIconSize(int size)
    {
        if (size is not (16 or 32 or 48))
            throw new ArgumentOutOfRangeException(nameof(size));

        if (_toolbar is null || _toolbar.IsDisposed)
        {
            _toolbarIconSize = size;
            _settings.ToolbarIconSize = size;
            return;
        }

        var replacements = new List<(ToolStripItem Item, Image Image)>();
        List<(TabPage Page, Image Image)>? propertyTabReplacements = null;
        try
        {
            replacements.Add((_toolbarNewButton, CreateLucideToolbarImage(size, ToolbarNewIcons)));
            replacements.Add((_toolbarOpenButton, CreateLucideToolbarImage(size, ToolbarOpenIcons)));
            replacements.Add((_toolbarSaveButton, CreateLucideToolbarImage(size, ToolbarSaveIcons)));
            replacements.Add((_toolbarReloadEditorButton, CreateLucideToolbarImage(size, ToolbarReloadIcons)));
            replacements.Add((_toolbarAddButton, CreateLucideToolbarImage(size, ToolbarAddIcons)));
            replacements.Add((_toolbarDuplicateButton, CreateLucideToolbarImage(size, ToolbarDuplicateIcons)));
            replacements.Add((_toolbarUndoButton, CreateLucideToolbarImage(size, ToolbarUndoIcons)));
            replacements.Add((_toolbarRedoButton, CreateLucideToolbarImage(size, ToolbarRedoIcons)));
            replacements.Add((_toolbarGroupButton, CreateLucideToolbarImage(size, ToolbarGroupIcons)));
            replacements.Add((_toolbarUngroupButton, CreateLucideToolbarImage(size, ToolbarUngroupIcons)));
            replacements.Add((_toolbarAlignLeftButton, CreateAlignmentToolbarImage(size, AlignmentCommand.Left)));
            replacements.Add((_toolbarAlignHorizontalCenterButton, CreateAlignmentToolbarImage(size, AlignmentCommand.HorizontalCenter)));
            replacements.Add((_toolbarAlignRightButton, CreateAlignmentToolbarImage(size, AlignmentCommand.Right)));
            replacements.Add((_toolbarAlignTopButton, CreateAlignmentToolbarImage(size, AlignmentCommand.Top)));
            replacements.Add((_toolbarAlignVerticalCenterButton, CreateAlignmentToolbarImage(size, AlignmentCommand.VerticalCenter)));
            replacements.Add((_toolbarAlignBottomButton, CreateAlignmentToolbarImage(size, AlignmentCommand.Bottom)));
            replacements.Add((_toolbarZoomInButton, CreateLucideToolbarImage(size, ToolbarZoomInIcons)));
            replacements.Add((_toolbarZoomOutButton, CreateLucideToolbarImage(size, ToolbarZoomOutIcons)));
            replacements.Add((_toolbarZoom100Button, CreateLucideToolbarImage(size, ToolbarZoom100Icons)));
            replacements.Add((_toolbarFitHeightButton, CreateLucideToolbarImage(size, ToolbarFitHeightIcons)));
            replacements.Add((_toolbarGridButton, CreateLucideToolbarImage(size, ToolbarGridIcons)));
            propertyTabReplacements = CreatePropertyTabIconReplacements(size);
        }
        catch
        {
            foreach (var replacement in replacements)
                replacement.Image.Dispose();
            if (propertyTabReplacements is not null)
            {
                foreach (var replacement in propertyTabReplacements)
                    replacement.Image.Dispose();
            }
            throw;
        }

        var oldImages = _toolbarImages.ToArray();
        _toolbarImages.Clear();
        foreach (var replacement in replacements)
        {
            replacement.Item.Image = replacement.Image;
            _toolbarImages.Add(replacement.Image);
        }

        _toolbarIconSize = size;
        _settings.ToolbarIconSize = size;
        UpdateToolbarSeparators(size);
        _toolbar.PerformLayout();
        _toolbar.Invalidate();

        CommitPropertyTabIconReplacements(size, propertyTabReplacements!);

        foreach (var image in oldImages)
            image.Dispose();
    }

    private void DisposeToolbarImages()
    {
        foreach (var image in _toolbarImages)
            image.Dispose();
        _toolbarImages.Clear();
    }

    private void ToolbarZoomTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            ExecuteToolbarZoomTextCommand();
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            UpdateZoomToolbarText();
            _workspaceViewport.Focus();
        }
    }

    private void ExecuteToolbarZoomTextCommand()
    {
        if (_preview is null)
            return;

        var text = _toolbarZoomTextBox.Text.Trim().TrimEnd('%').Trim();
        if (!TryParseZoomPercent(text, out var percent) || percent < MinEditorZoom * 100f || percent > MaxEditorZoom * 100f)
        {
            SetStatus($"Zoom must be between {(int)(MinEditorZoom * 100)}% and {(int)(MaxEditorZoom * 100)}%.");
            UpdateZoomToolbarText();
            return;
        }

        ExecuteSetZoomPercentCommand(percent);
    }

    private static bool TryParseZoomPercent(string text, out float percent)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out percent))
            return float.IsFinite(percent);

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) && float.IsFinite(percent);
    }

    private void ExecuteZoomStepCommand(int direction)
    {
        if (_preview is null || direction == 0)
            return;

        var current = _zoom;
        float next;
        if (direction > 0)
        {
            next = ZoomLevels.FirstOrDefault(level => level > current + 0.001f, MaxEditorZoom);
        }
        else
        {
            next = ZoomLevels.LastOrDefault(level => level < current - 0.001f, MinEditorZoom);
            if (current < ZoomLevels[0] - 0.001f)
                next = MinEditorZoom;
        }

        ExecuteSetZoomPercentCommand(next * 100f);
    }

    private void ExecuteSetZoomPercentCommand(float percent)
    {
        if (_preview is null || !float.IsFinite(percent))
            return;

        _fitZoom = false;
        _zoom = Math.Clamp(percent / 100f, MinEditorZoom, MaxEditorZoom);
        ApplyZoom();
        UpdateZoomToolbarText();
        UpdateCommandStates();
    }

    private void ExecuteFitHeightCommand()
    {
        if (_preview is null)
            return;

        _fitZoom = true;
        ApplyZoom();
        UpdateZoomToolbarText();
        UpdateCommandStates();
    }

    private void UpdateZoomToolbarText()
    {
        if (_toolbarZoomTextBox is null || _toolbarZoomTextBox.IsDisposed)
            return;

        _toolbarZoomTextBox.Text = FormatZoomPercent(_zoom);
    }

    private static string FormatZoomPercent(float zoom)
    {
        var percent = zoom * 100f;
        return Math.Abs(percent - MathF.Round(percent)) < 0.05f
            ? $"{MathF.Round(percent):0}%"
            : $"{percent:0.0}%";
    }

    private void ExecuteToggleGridCommand()
    {
        if (_definition is null)
            return;

        _gridEnabled = !_gridEnabled;
        UpdateGridControls();
        _workspaceViewport.Invalidate();
        UpdateCommandStates();
    }

    private void ExecuteSetGridStepCommand(int step)
    {
        if (_definition is null || step is not (10 or 25 or 50 or 100))
            return;

        _gridStep = step;
        UpdateGridControls();
        _workspaceViewport.Invalidate();
        UpdateCommandStates();
    }

    private void PopulateGridToolsMenu()
    {
        if (_gridToolsMenu.DropDownItems.Count == 0)
        {
            _gridToggleMenuItem = new ToolStripMenuItem("Show Grid", null, (_, _) => ExecuteToggleGridCommand());
            _gridToolsMenu.DropDownItems.Add(_gridToggleMenuItem);
            _gridToolsMenu.DropDownItems.Add(new ToolStripSeparator());
            PopulateGridSizeMenu(_gridToolsMenu.DropDownItems);
        }

        UpdateGridControls();
        UpdateGridSizeMenuChecks(_gridToolsMenu.DropDownItems);
    }

    private void PopulateGridSizeMenu(ToolStripItemCollection items)
    {
        foreach (var step in new[] { 10, 25, 50, 100 })
        {
            var captured = step;
            var item = new ToolStripMenuItem($"{step} × {step}", null, (_, _) => ExecuteSetGridStepCommand(captured))
            {
                Tag = step,
                Checked = _gridStep == step
            };
            items.Add(item);
        }
    }

    private void UpdateGridSizeMenuChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menu && menu.Tag is int step)
                menu.Checked = step == _gridStep;
        }
    }

    private void UpdateGridControls()
    {
        if (_toolbarGridButton is not null)
        {
            _toolbarGridButton.Checked = _gridEnabled;
            _toolbarGridButton.ToolTipText = _gridEnabled
                ? "Hide grid; use the arrow to select grid spacing"
                : "Show grid; use the arrow to select grid spacing";
            UpdateGridSizeMenuChecks(_toolbarGridButton.DropDownItems);
        }
        if (_gridToggleMenuItem is not null)
            _gridToggleMenuItem.Checked = _gridEnabled;
        if (_gridToolsMenu is not null)
            UpdateGridSizeMenuChecks(_gridToolsMenu.DropDownItems);
    }
}

internal sealed class CheckableToolStripSplitButton : ToolStripSplitButton
{
    private bool _checked;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!_checked || !Enabled || ButtonBounds.Width <= 0 || ButtonBounds.Height <= 0)
            return;

        var bounds = ButtonBounds;
        bounds.Inflate(-1, -1);
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        using var pen = new Pen(SystemColors.Highlight, 2f);
        e.Graphics.DrawRectangle(pen, bounds);
    }
}
