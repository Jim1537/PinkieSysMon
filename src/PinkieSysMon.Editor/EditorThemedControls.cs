namespace PinkieSysMon.Editor;

internal sealed class EditorThemedTabControl : TabControl
{
    private ImageList? _headerImageList;
    private readonly Dictionary<TabPage, int> _headerImageIndices = new(ReferenceEqualityComparer.Instance);
    private int _headerIconSize = 32;

    public EditorThemedTabControl()
    {
        // Keep the native TabControl renderer so Application.SetColorMode owns
        // light/dark/system colors exactly as it did before icon-only headers.
        // Only the tab content changes from text to ImageList-backed icons.
        DrawMode = TabDrawMode.Normal;
        SizeMode = TabSizeMode.Fixed;
        ShowToolTips = true;
        Multiline = false;
        UpdateHeaderGeometry();
    }

    internal int HeaderIconSize => _headerIconSize;

    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }

    internal void ApplyTheme(EditorShellTheme theme)
    {
        // The native renderer owns the actual palette. The argument remains part
        // of the editor-theme contract so callers can invalidate this surface in
        // the same pass as the rest of the shell after Application.SetColorMode.
        _ = theme;
        Invalidate(true);
    }

    internal void SetIconHeaders(int iconSize, IEnumerable<(TabPage Page, Image Image)> icons)
    {
        if (iconSize is not (16 or 32 or 48))
            throw new ArgumentOutOfRangeException(nameof(iconSize));
        ArgumentNullException.ThrowIfNull(icons);

        var nextImageList = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(iconSize, iconSize),
            TransparentColor = Color.Transparent
        };

        try
        {
            var nextIndices = new Dictionary<TabPage, int>(ReferenceEqualityComparer.Instance);
            var index = 0;
            foreach (var (page, image) in icons)
            {
                ArgumentNullException.ThrowIfNull(page);
                ArgumentNullException.ThrowIfNull(image);

                nextImageList.Images.Add(image);
                nextIndices[page] = index;

                // Keep a stable numeric image index on every TabPage even while it is
                // detached. Native TabControl consumes the image index when the page is
                // inserted; binding by ImageKey only after AddRange can make the first
                // dynamic header layout use -1 and render the icon off-center until the
                // page is removed and inserted again.
                page.ImageIndex = index;
                index++;
            }

            var previousImageList = _headerImageList;
            ImageList = nextImageList;
            _headerImageList = nextImageList;
            _headerImageIndices.Clear();
            foreach (var pair in nextIndices)
                _headerImageIndices[pair.Key] = pair.Value;
            _headerIconSize = iconSize;
            UpdateHeaderGeometry();
            Invalidate(true);
            previousImageList?.Dispose();
        }
        catch
        {
            nextImageList.Dispose();
            throw;
        }
    }

    internal void RefreshIconHeaders()
    {
        if (_headerImageList is null)
            return;

        ImageList = _headerImageList;
        foreach (var pair in _headerImageIndices)
            pair.Key.ImageIndex = pair.Value;
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ImageList = null;
            _headerImageList?.Dispose();
            _headerImageList = null;
            _headerImageIndices.Clear();
        }

        base.Dispose(disposing);
    }

    private void UpdateHeaderGeometry()
    {
        // Keep icon-only tabs compact while leaving enough native-theme padding
        // around the icon at all three shared interface icon sizes.
        ItemSize = new Size(_headerIconSize + 10, _headerIconSize + 8);
    }
}
