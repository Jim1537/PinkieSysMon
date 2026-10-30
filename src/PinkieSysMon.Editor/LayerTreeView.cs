namespace PinkieSysMon.Editor;

internal readonly record struct LayerTreeViewportState(string? TopNodeKey)
{
    public static LayerTreeViewportState Capture(
        TreeNode? topNode,
        Func<TreeNode, string?> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new LayerTreeViewportState(topNode is null ? null : keySelector(topNode));
    }

    public TreeNode? Resolve(Func<string, TreeNode?> nodeResolver)
    {
        ArgumentNullException.ThrowIfNull(nodeResolver);
        return string.IsNullOrWhiteSpace(TopNodeKey) ? null : nodeResolver(TopNodeKey);
    }
}

internal sealed class LayerTreeView : TreeView
{
    private const int WmPaint = 0x000F;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;

    public event EventHandler? PointerActivating;

    public LayerTreeView()
    {
        // Logical multi-selection is painted through TreeNode colors. Suppress the
        // native inactive-selection overlay so it cannot hide that logical selection
        // while Canvas or Properties owns keyboard focus.
        HideSelection = true;
        FullRowSelect = true;
    }

    internal static bool IsPointerActivationMessage(int message) =>
        message is WmLButtonDown or WmRButtonDown;

    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
    private int? _insertionY;
    private int _insertionLeft = 18;
    private Rectangle? _dropTargetBounds;

    public void SetInsertionMarker(int? y, int left = 18)
    {
        if (_insertionY == y && _insertionLeft == left)
            return;
        _insertionY = y;
        _insertionLeft = left;
        Invalidate();
    }

    public void SetDropTarget(TreeNode? node)
    {
        var bounds = node is null
            ? (Rectangle?)null
            : new Rectangle(Math.Max(2, node.Bounds.Left - 3), node.Bounds.Top, Math.Max(8, ClientSize.Width - node.Bounds.Left), node.Bounds.Height);
        if (_dropTargetBounds == bounds)
            return;
        _dropTargetBounds = bounds;
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        var pointerActivation = IsPointerActivationMessage(m.Msg);
        if (pointerActivation)
        {
            // TreeView raises native selection notifications while processing the mouse
            // message. Claim/select Layers before that work starts so a PropertyGrid
            // refresh cannot restore focus back to Properties during the same click.
            PointerActivating?.Invoke(this, EventArgs.Empty);
            if (CanSelect && !ContainsFocus)
                Select();
        }

        base.WndProc(ref m);

        // Select() above is deliberately repeated after native processing only when
        // focus still did not transfer. This covers an in-place PropertyGrid editor
        // completing its edit during the same mouse activation without using a timer
        // or BeginInvoke focus race.
        if (pointerActivation && CanSelect && !ContainsFocus)
            Select();

        if (m.Msg != WmPaint || !IsHandleCreated)
            return;

        using var graphics = CreateGraphics();
        if (_dropTargetBounds is Rectangle target)
        {
            using var targetPen = new Pen(Color.DeepSkyBlue, 2f);
            graphics.DrawRectangle(targetPen, target);
        }

        if (_insertionY is not int y)
            return;

        using var pen = new Pen(Color.DeepSkyBlue, 2f);
        var left = Math.Max(4, _insertionLeft);
        var right = Math.Max(left + 8, ClientSize.Width - 5);
        graphics.DrawLine(pen, left, y, right, y);

        using var brush = new SolidBrush(Color.DeepSkyBlue);
        graphics.FillPolygon(brush,
        [
            new Point(left - 1, y),
            new Point(left + 6, y - 4),
            new Point(left + 6, y + 4)
        ]);
    }
}
