using CanonicalImageAssetPresentationDefinition = PinkieSysMon.DashboardModel.ImageAssetPresentationDefinition;
using CanonicalImageAssetSourceType = PinkieSysMon.DashboardModel.ImageAssetSourceType;
using System.ComponentModel;
using System.Drawing.Design;
using SkiaSharp;

namespace PinkieSysMon.Editor;

internal sealed class ImageAssetSourceEditor : UITypeEditor
{
    private readonly CanonicalImageAssetPresentationDefinition _asset;
    private readonly string _applicationRoot;
    private readonly string _dashboardDirectory;

    public ImageAssetSourceEditor(
        CanonicalImageAssetPresentationDefinition asset,
        string applicationRoot,
        string dashboardDirectory)
    {
        _asset = asset;
        _applicationRoot = applicationRoot;
        _dashboardDirectory = dashboardDirectory;
    }

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (CanonicalImageAssetSourceType.IsIcon(_asset.SourceType))
        {
            using var browser = new IconBrowserDialog(_applicationRoot, Convert.ToString(value));
            return browser.ShowDialog(Form.ActiveForm) == DialogResult.OK
                ? browser.SelectedIcon ?? value
                : value;
        }

        return EditorImageSourceTools.SelectDashboardImage(
            Form.ActiveForm,
            _dashboardDirectory,
            Convert.ToString(value)) ?? value;
    }
}

internal sealed class DashboardImageSourceEditor : UITypeEditor
{
    private readonly string _dashboardDirectory;

    public DashboardImageSourceEditor(string dashboardDirectory)
    {
        _dashboardDirectory = dashboardDirectory;
    }

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value) =>
        EditorImageSourceTools.SelectDashboardImage(
            Form.ActiveForm,
            _dashboardDirectory,
            Convert.ToString(value)) ?? value;
}

internal static class EditorImageSourceTools
{
    public static string? SelectDashboardImage(IWin32Window? owner, string dashboardDirectory, string? currentSource)
    {
        var imagesDirectory = Path.Combine(dashboardDirectory, "images");
        Directory.CreateDirectory(imagesDirectory);

        var initialDirectory = imagesDirectory;
        if (!string.IsNullOrWhiteSpace(currentSource))
        {
            try
            {
                var current = Path.GetFullPath(Path.Combine(dashboardDirectory, currentSource));
                var parent = Path.GetDirectoryName(current);
                if (parent is not null && Directory.Exists(parent))
                    initialDirectory = parent;
            }
            catch
            {
            }
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Select dashboard image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
            InitialDirectory = initialDirectory
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        var selected = Path.GetFullPath(dialog.FileName);
        var dashboardRoot = Path.GetFullPath(dashboardDirectory);
        var rootPrefix = dashboardRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (selected.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(dashboardRoot, selected);

        var target = Path.Combine(imagesDirectory, Path.GetFileName(selected));
        if (!FilesEqual(target, selected))
        {
            var stem = Path.GetFileNameWithoutExtension(selected);
            var extension = Path.GetExtension(selected);
            var number = 1;
            while (File.Exists(target))
                target = Path.Combine(imagesDirectory, $"{stem}-{number++}{extension}");
            File.Copy(selected, target);
        }

        return Path.GetRelativePath(dashboardRoot, target);
    }

    private static bool FilesEqual(string first, string second)
    {
        if (!File.Exists(first))
            return false;

        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        const int bufferSize = 81920;
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        var bufferA = new byte[bufferSize];
        var bufferB = new byte[bufferSize];
        while (true)
        {
            var readA = a.Read(bufferA, 0, bufferA.Length);
            var readB = b.Read(bufferB, 0, bufferB.Length);
            if (readA != readB)
                return false;
            if (readA == 0)
                return true;
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
        }
    }
}

internal sealed class ImageSourceTypeDialog : PinkieEditorForm
{
    private readonly ComboBox _sourceType = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 210
    };

    public ImageSourceTypeDialog()
    {
        Text = "Add Image Widget";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(390, 135);

        _sourceType.Items.AddRange(["File", "Icon"]);
        _sourceType.SelectedIndex = 0;

        var label = new Label { Text = "Source type:", AutoSize = true, Location = new Point(16, 24) };
        _sourceType.Location = new Point(130, 20);

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

        Controls.Add(label);
        Controls.Add(_sourceType);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string SourceType => _sourceType.SelectedIndex == 1 ? "icon" : "file";
}

internal sealed class IconBrowserDialog : PinkieEditorForm
{
    private const string AllLibraries = "All libraries";

    private readonly ComboBox _library = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Search icons..."
    };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        MultiSelect = false,
        HideSelection = false,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable
    };
    private readonly PictureBox _preview = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(32, 32, 32),
        SizeMode = PictureBoxSizeMode.CenterImage
    };
    private readonly Label _logicalName = new()
    {
        Dock = DockStyle.Bottom,
        Height = 34,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoEllipsis = true
    };
    private readonly IReadOnlyList<GlobalAssetResolver.IconEntry> _entries;
    private readonly Button _ok = new() { Text = "Select", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
    private readonly string? _initial;

    public IconBrowserDialog(string applicationRoot, string? currentSource = null)
    {
        _entries = GlobalAssetResolver.EnumerateIconsFromRoot(applicationRoot);
        _initial = currentSource;

        Text = "Select Global Icon";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 650;
        MinimumSize = new Size(650, 450);
        ShowInTaskbar = false;

        _list.Columns.Add("Icon", 330);
        _list.Columns.Add("Format", 80);

        _library.Items.Add(AllLibraries);
        foreach (var library in _entries
                     .Select(x => x.Library)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _library.Items.Add(library);
        _library.SelectedIndex = 0;

        _search.TextChanged += (_, _) => PopulateList();
        _library.SelectedIndexChanged += (_, _) => PopulateList();
        _list.SelectedIndexChanged += (_, _) => UpdatePreview();
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItems.Count == 1 && _ok.Enabled)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 8, 8, 4),
            ColumnCount = 4,
            RowCount = 1
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.Controls.Add(new Label
        {
            Text = "Library:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 6, 0)
        }, 0, 0);
        filters.Controls.Add(_library, 1, 0);
        filters.Controls.Add(new Label
        {
            Text = "Search:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 4, 6, 0)
        }, 2, 0);
        filters.Controls.Add(_search, 3, 0);

        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        previewHost.Controls.Add(_preview);
        previewHost.Controls.Add(_logicalName);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 430,
            SplitterWidth = 6
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(previewHost);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);

        Controls.Add(split);
        Controls.Add(filters);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_initial))
            {
                var initial = _entries.FirstOrDefault(x =>
                    string.Equals(x.LogicalName, _initial, StringComparison.OrdinalIgnoreCase));
                if (initial is not null)
                {
                    var libraryIndex = _library.Items.Cast<object>()
                        .Select((item, index) => (Item: Convert.ToString(item), Index: index))
                        .FirstOrDefault(x => string.Equals(x.Item, initial.Library, StringComparison.OrdinalIgnoreCase))
                        .Index;
                    if (libraryIndex > 0)
                        _library.SelectedIndex = libraryIndex;
                }
            }

            PopulateList();
            SelectInitialIfVisible();
            _search.Focus();
        };
    }

    public string? SelectedIcon => _list.SelectedItems.Count == 1 &&
                                   _list.SelectedItems[0].Tag is GlobalAssetResolver.IconEntry entry
        ? entry.LogicalName
        : null;

    private void PopulateList()
    {
        var terms = _search.Text
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectedLibrary = Convert.ToString(_library.SelectedItem);
        var filterLibrary = !string.IsNullOrWhiteSpace(selectedLibrary) &&
                            !string.Equals(selectedLibrary, AllLibraries, StringComparison.OrdinalIgnoreCase)
            ? selectedLibrary
            : null;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var entry in _entries)
            {
                if (filterLibrary is not null &&
                    !string.Equals(entry.Library, filterLibrary, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (terms.Length > 0 && terms.Any(term =>
                        !entry.LogicalName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                        !entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                        !entry.Library.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                        !entry.Format.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var item = new ListViewItem(entry.LogicalName) { Tag = entry };
                item.SubItems.Add(entry.Format);
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _ok.Enabled = false;
        SetPreview(null);
    }

    private void SelectInitialIfVisible()
    {
        if (string.IsNullOrWhiteSpace(_initial))
            return;

        var item = _list.Items.Cast<ListViewItem>()
            .FirstOrDefault(x => x.Tag is GlobalAssetResolver.IconEntry entry &&
                                 string.Equals(entry.LogicalName, _initial, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        item.Selected = true;
        item.EnsureVisible();
    }

    private void UpdatePreview()
    {
        if (_list.SelectedItems.Count != 1 ||
            _list.SelectedItems[0].Tag is not GlobalAssetResolver.IconEntry entry)
        {
            _ok.Enabled = false;
            SetPreview(null);
            return;
        }

        _ok.Enabled = false;
        _logicalName.Text = $"{entry.LogicalName}   [{entry.Library}]";
        try
        {
            using var icon = IconAssetLoader.Load(entry.Path);
            using var surface = SKSurface.Create(new SKImageInfo(220, 220, SKColorType.Bgra8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create icon preview surface.");
            surface.Canvas.Clear(new SKColor(32, 32, 32));
            icon.Draw(
                surface.Canvas,
                new SKRect(30, 30, 190, 190),
                SKColors.White,
                1f,
                "contain",
                loop: true,
                antialias: true,
                elapsedMs: 0);
            surface.Canvas.Flush();
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode icon preview.");
            using var stream = new MemoryStream(data.ToArray());
            using var loaded = Image.FromStream(stream);
            SetPreview(new Bitmap(loaded));

            var capabilities = icon.Capabilities;
            var colorMode = capabilities.Tintable ? "tintable" : "intrinsic color";
            var animation = capabilities.IsAnimated ? "animated" : "static";
            var alpha = capabilities.SupportsAlpha ? "alpha" : "opaque";
            _logicalName.Text =
                $"{entry.LogicalName}   [{capabilities.Format}; {colorMode}; {animation}; {alpha}]";
            _ok.Enabled = true;
        }
        catch (Exception ex)
        {
            SetPreview(null);
            _logicalName.Text = $"{entry.LogicalName} — {ex.Message}";
        }
    }

    private void SetPreview(Image? image)
    {
        var old = _preview.Image;
        _preview.Image = image;
        old?.Dispose();
        if (image is null && _list.SelectedItems.Count == 0)
            _logicalName.Text = _entries.Count == 0 ? "No global icon assets found." : string.Empty;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
        }
        base.Dispose(disposing);
    }
}
