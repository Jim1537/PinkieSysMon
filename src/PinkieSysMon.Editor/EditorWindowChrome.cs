using System.ComponentModel;

namespace PinkieSysMon.Editor;

internal static class EditorWindowChromeContract
{
    public static Rectangle CenterVertically(Rectangle availableBounds, int preferredHeight)
    {
        if (availableBounds.Width <= 0 || availableBounds.Height <= 0)
            return Rectangle.Empty;

        var height = Math.Clamp(preferredHeight, 1, availableBounds.Height);
        var top = availableBounds.Top + Math.Max(0, (availableBounds.Height - height) / 2);
        return new Rectangle(availableBounds.Left, top, availableBounds.Width, height);
    }

    public static Rectangle[] GetMenuPassthroughRegions(Rectangle menuBounds, IEnumerable<Rectangle> itemBounds)
    {
        ArgumentNullException.ThrowIfNull(itemBounds);
        if (menuBounds.Width <= 0 || menuBounds.Height <= 0)
            return [];

        return itemBounds
            .Where(bounds => bounds.Width > 0 && bounds.Height > 0)
            .Select(bounds => new Rectangle(
                menuBounds.Left + bounds.Left,
                menuBounds.Top + bounds.Top,
                bounds.Width,
                bounds.Height))
            .ToArray();
    }
}

internal sealed class EditorTitleBarHost : Control
{
    private int _leftInset;
    private int _rightInset;
    private Icon? _windowIcon;
    private bool _showWindowIcon;
    private EditorShellTheme _theme = EditorShellTheme.CaptureCurrent();

    public EditorTitleBarHost()
    {
        Dock = DockStyle.Top;
        TabStop = false;
        BackColor = _theme.Background;
        ForeColor = _theme.Foreground;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowWindowIcon
    {
        get => _showWindowIcon;
        set
        {
            if (_showWindowIcon == value)
                return;

            _showWindowIcon = value;
            Invalidate();
        }
    }

    public Rectangle WindowIconBounds
    {
        get
        {
            if (!_showWindowIcon || _windowIcon is null || Width <= 0 || Height <= 0)
                return Rectangle.Empty;

            var padding = ScaleMetric(8);
            var iconSize = Math.Min(ScaleMetric(16), Math.Max(1, Height - (padding * 2)));
            var x = Math.Max(Math.Clamp(_leftInset, 0, Width), padding);
            var y = Math.Max(0, (Height - iconSize) / 2);
            return new Rectangle(x, y, iconSize, iconSize);
        }
    }

    public Rectangle MenuBounds
    {
        get
        {
            var left = Math.Clamp(_leftInset, 0, Width);
            var iconBounds = WindowIconBounds;
            if (!iconBounds.IsEmpty)
                left = Math.Max(left, Math.Min(Width, iconBounds.Right + ScaleMetric(6)));

            var right = Math.Clamp(Width - _rightInset, left, Width);
            return Rectangle.FromLTRB(left, 0, right, Height);
        }
    }

    public void SetWindowIcon(Icon? icon)
    {
        _windowIcon?.Dispose();
        _windowIcon = icon is null ? null : (Icon)icon.Clone();
        Invalidate();
    }

    public void UpdateNativeMetrics(int height, int leftInset, int rightInset)
    {
        Height = Math.Max(1, height);
        _leftInset = Math.Max(0, leftInset);
        _rightInset = Math.Max(0, rightInset);
        Invalidate();
    }

    public void ApplyTheme(EditorShellTheme theme)
    {
        _theme = theme;
        BackColor = theme.Background;
        ForeColor = theme.Foreground;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);

        var iconBounds = WindowIconBounds;
        if (!iconBounds.IsEmpty && _windowIcon is not null)
            e.Graphics.DrawIcon(_windowIcon, iconBounds);

        using var borderPen = new Pen(_theme.Border, 1f);
        e.Graphics.DrawLine(borderPen, 0, Math.Max(0, Height - 1), Width, Math.Max(0, Height - 1));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _windowIcon?.Dispose();
            _windowIcon = null;
        }

        base.Dispose(disposing);
    }

    private int ScaleMetric(int value) =>
        Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));
}

internal sealed class EditorTitleBarMenuStrip : MenuStrip
{
    private int _titlePadding = 8;
    private readonly EditorTitleBarMenuRenderer _titleRenderer = new();
    private EditorShellTheme _theme = EditorShellTheme.CaptureCurrent();

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowWindowTitle { get; set; } = true;

    public EditorTitleBarMenuStrip()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        AutoSize = false;
        Dock = DockStyle.None;
        GripStyle = ToolStripGripStyle.Hidden;
        LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        CanOverflow = false;
        TabStop = false;
        Renderer = _titleRenderer;
        Padding = Padding.Empty;
        ApplyTheme(_theme);
    }

    public void ApplyTheme(EditorShellTheme theme)
    {
        _theme = theme;
        _titleRenderer.ApplyTheme(theme);
        BackColor = Color.Transparent;
        ForeColor = theme.Foreground;
        foreach (ToolStripItem item in Items)
            item.ForeColor = item.Enabled ? theme.Foreground : theme.InactiveForeground;
        Invalidate();
    }

    public Rectangle[] GetInteractiveItemBounds() => Items
        .Cast<ToolStripItem>()
        .Where(item => item.Available && item.Enabled && !item.Bounds.IsEmpty)
        .Select(item => item.Bounds)
        .ToArray();

    public void UpdateTitleBarMetrics(int titlePadding)
    {
        _titlePadding = Math.Max(0, titlePadding);
        Invalidate();
    }

    public int GetTitleBarPreferredHeight(int verticalPadding) =>
        Math.Max(1, Font.Height + Math.Max(0, verticalPadding) * 2);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // WinForms transparency means inheriting the parent background paint.
        // ToolStrip/MenuStrip can otherwise substitute its themed background even
        // when BackColor is Transparent, which produces a visible title-bar band.
        // Paint exactly the parent's current background rather than maintaining a
        // second guessed title-bar color.
        if (Parent is not null)
        {
            e.Graphics.Clear(Parent.BackColor);
            return;
        }

        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!ShowWindowTitle)
            return;

        var form = FindForm();
        if (form is null || string.IsNullOrWhiteSpace(form.Text))
            return;

        var leftGroupRight = 0;
        var rightGroupLeft = Width;
        foreach (ToolStripItem item in Items)
        {
            if (!item.Available || item.Bounds.IsEmpty)
                continue;

            if (item.Alignment == ToolStripItemAlignment.Right)
                rightGroupLeft = Math.Min(rightGroupLeft, item.Bounds.Left);
            else
                leftGroupRight = Math.Max(leftGroupRight, item.Bounds.Right);
        }

        var textLeft = Math.Min(Width, leftGroupRight + _titlePadding);
        var textRight = Math.Max(textLeft, rightGroupLeft - _titlePadding);
        if (textRight - textLeft < 40)
            return;

        var textBounds = Rectangle.FromLTRB(textLeft, 0, textRight, Height);
        TextRenderer.DrawText(
            e.Graphics,
            form.Text,
            form.Font,
            textBounds,
            form.ContainsFocus ? _theme.Foreground : _theme.InactiveForeground,
            TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }
}

internal sealed class EditorTitleBarMenuRenderer : ToolStripSystemRenderer
{
    private EditorShellTheme _theme = EditorShellTheme.CaptureCurrent();

    public void ApplyTheme(EditorShellTheme theme) => _theme = theme;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is EditorTitleBarMenuStrip)
        {
            // The MenuStrip is a child of EditorTitleBarHost. Leave its background
            // transparent so there is one title-bar background owner instead of a
            // second approximated color surface behind the menus/window title.
            return;
        }

        base.OnRenderToolStripBackground(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is EditorTitleBarMenuStrip)
            return;

        base.OnRenderToolStripBorder(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.ToolStrip is EditorTitleBarMenuStrip && ReferenceEquals(e.Item.Owner, e.ToolStrip))
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using var brush = new SolidBrush(_theme.SelectionBackground);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
            return;
        }

        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.ToolStrip is EditorTitleBarMenuStrip && ReferenceEquals(e.Item.Owner, e.ToolStrip))
        {
            var foreground = !e.Item.Enabled
                ? _theme.InactiveForeground
                : e.Item.Selected || e.Item.Pressed
                    ? _theme.SelectionForeground
                    : _theme.Foreground;

            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                foreground,
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.NoPrefix);
            return;
        }

        base.OnRenderItemText(e);
    }
}
