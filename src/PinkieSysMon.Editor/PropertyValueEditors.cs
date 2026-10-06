using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Drawing.Drawing2D;
using System.Globalization;
using SkiaSharp;

namespace PinkieSysMon.Editor;

internal sealed class BundledFontFamilyConverter : StringConverter
{
    private static readonly Dictionary<string, StandardValuesCollection> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly StandardValuesCollection _values;

    private BundledFontFamilyConverter(IEnumerable<string> values) =>
        _values = new StandardValuesCollection(values.ToArray());

    public static BundledFontFamilyConverter Create(string applicationRoot)
    {
        var fontsDirectory = Path.Combine(applicationRoot, "assets", "fonts");
        lock (Cache)
        {
            if (!Cache.TryGetValue(fontsDirectory, out var values))
            {
                var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(fontsDirectory))
                {
                    foreach (var path in Directory.EnumerateFiles(fontsDirectory, "*.*", SearchOption.AllDirectories)
                                 .Where(path => Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                                Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var face = SKTypeface.FromFile(path);
                        if (face is not null && !string.IsNullOrWhiteSpace(face.FamilyName))
                            families.Add(face.FamilyName);
                    }
                }

                values = new StandardValuesCollection(families.ToArray());
                Cache[fontsDirectory] = values;
            }

            return new BundledFontFamilyConverter(values.Cast<string>());
        }
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => _values;
}

internal sealed class RgbaColorConverter : StringConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? NormalizeStorage(text) : base.ConvertFrom(context, culture, value);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            try
            {
                return FormatDisplay(Parse(text));
            }
            catch (FormatException)
            {
                return text;
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public static string NormalizeStorage(string value) => ToStorage(Parse(value));

    public static string ToStorage(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string FormatDisplay(Color color) =>
        color.A == byte.MaxValue
            ? $"rgb({color.R}, {color.G}, {color.B})"
            : $"rgba({color.R}, {color.G}, {color.B}, {color.A})";

    public static Color Parse(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
            throw new FormatException(ColorFormatMessage);

        if (text.StartsWith('#'))
            return ParseHex(text[1..]);

        if (TryParseFunctional(text, "rgb", 3, out var rgb))
            return Color.FromArgb(byte.MaxValue, rgb[0], rgb[1], rgb[2]);

        if (TryParseFunctional(text, "rgba", 4, out var rgba))
            return Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);

        throw new FormatException(ColorFormatMessage);
    }

    private const string ColorFormatMessage =
        "Color must be rgb(R, G, B), rgba(R, G, B, A), #RRGGBB, or #AARRGGBB; channels use 0..255.";

    private static Color ParseHex(string text)
    {
        if (text.Length == 6)
            text = "FF" + text;

        if (text.Length != 8 ||
            !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            throw new FormatException(ColorFormatMessage);
        }

        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    private static bool TryParseFunctional(string text, string functionName, int expectedChannels, out byte[] channels)
    {
        channels = [];
        if (!text.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
            return false;

        var contents = text.AsSpan(functionName.Length + 1, text.Length - functionName.Length - 2);
        var parts = contents.ToString().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != expectedChannels)
            return false;

        channels = new byte[expectedChannels];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i]))
                return false;
        }

        return true;
    }
}

internal sealed class RgbaColorEditor : UITypeEditor
{
    public static readonly RgbaColorEditor Instance = new();

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
        UITypeEditorEditStyle.Modal;

    public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

    public override void PaintValue(PaintValueEventArgs e)
    {
        var bounds = e.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        Color color;
        try
        {
            color = RgbaColorConverter.Parse(Convert.ToString(e.Value) ?? string.Empty);
        }
        catch (FormatException)
        {
            return;
        }

        var cell = Math.Max(2, Math.Min(bounds.Width, bounds.Height) / 4);
        using var lightBrush = new SolidBrush(Color.White);
        using var darkBrush = new SolidBrush(Color.LightGray);

        for (var y = bounds.Top; y < bounds.Bottom; y += cell)
        {
            for (var x = bounds.Left; x < bounds.Right; x += cell)
            {
                var even = (((x - bounds.Left) / cell) + ((y - bounds.Top) / cell)) % 2 == 0;
                var width = Math.Min(cell, bounds.Right - x);
                var height = Math.Min(cell, bounds.Bottom - y);
                e.Graphics.FillRectangle(even ? lightBrush : darkBrush, x, y, width, height);
            }
        }

        using var colorBrush = new SolidBrush(color);
        e.Graphics.FillRectangle(colorBrush, bounds);

        using var borderPen = new Pen(SystemColors.ControlDark);
        e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    public override object? EditValue(
        ITypeDescriptorContext? context,
        IServiceProvider provider,
        object? value)
    {
        Color initial;
        try { initial = RgbaColorConverter.Parse(Convert.ToString(value) ?? "#FFFFFFFF"); }
        catch (FormatException) { initial = Color.White; }

        using var picker = new RgbaColorPickerDialog(initial);
        var owner = Form.ActiveForm;
        var result = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        return result == DialogResult.OK
            ? RgbaColorConverter.ToStorage(picker.SelectedColor)
            : value;
    }
}

internal sealed class RgbaColorPickerDialog : PinkieEditorForm
{
    private readonly HsvColorPicker _picker = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly NumericUpDown _red = CreateChannelEditor();
    private readonly NumericUpDown _green = CreateChannelEditor();
    private readonly NumericUpDown _blue = CreateChannelEditor();
    private readonly NumericUpDown _alpha = CreateChannelEditor();
    private readonly TrackBar _alphaSlider = new()
    {
        Minimum = 0,
        Maximum = 255,
        TickFrequency = 32,
        AutoSize = false,
        Height = 34,
        Dock = DockStyle.Fill
    };
    private readonly TransparencyPreview _preview = new() { Dock = DockStyle.Fill, MinimumSize = new Size(120, 54) };
    private readonly Label _valueLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private bool _updating;

    public RgbaColorPickerDialog(Color initialColor)
    {
        Text = "Color";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(590, 345);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_picker, 0, 0);

        var values = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 0, 0),
            ColumnCount = 2,
            RowCount = 8
        };
        values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        values.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        for (var i = 0; i < 4; i++)
            values.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        values.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        values.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        values.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        values.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        AddChannelRow(values, 0, "Red", _red);
        AddChannelRow(values, 1, "Green", _green);
        AddChannelRow(values, 2, "Blue", _blue);
        AddChannelRow(values, 3, "Alpha", _alpha);

        values.Controls.Add(new Label
        {
            Text = "Alpha",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 10, 4)
        }, 0, 4);
        values.Controls.Add(_alphaSlider, 0, 5);
        values.SetColumnSpan(_alphaSlider, 2);
        values.Controls.Add(_preview, 0, 6);
        values.SetColumnSpan(_preview, 2);
        values.Controls.Add(_valueLabel, 0, 7);
        values.SetColumnSpan(_valueLabel, 2);
        root.Controls.Add(values, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        _picker.SelectedColorChanged += (_, _) => PickerColorChanged();
        _red.ValueChanged += (_, _) => RgbValueChanged();
        _green.ValueChanged += (_, _) => RgbValueChanged();
        _blue.ValueChanged += (_, _) => RgbValueChanged();
        _alpha.ValueChanged += (_, _) => AlphaValueChanged();
        _alphaSlider.ValueChanged += (_, _) => AlphaSliderChanged();

        SetSelectedColor(initialColor);
    }

    public Color SelectedColor => Color.FromArgb(
        Decimal.ToByte(_alpha.Value),
        Decimal.ToByte(_red.Value),
        Decimal.ToByte(_green.Value),
        Decimal.ToByte(_blue.Value));

    private static NumericUpDown CreateChannelEditor() => new()
    {
        Minimum = 0,
        Maximum = 255,
        DecimalPlaces = 0,
        Width = 72,
        Anchor = AnchorStyles.Right
    };

    private static void AddChannelRow(TableLayoutPanel layout, int row, string label, Control editor)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 10, 5)
        }, 0, row);
        layout.Controls.Add(editor, 1, row);
    }

    private void SetSelectedColor(Color color)
    {
        _updating = true;
        try
        {
            _red.Value = color.R;
            _green.Value = color.G;
            _blue.Value = color.B;
            _alpha.Value = color.A;
            _alphaSlider.Value = color.A;
            _picker.SetColor(Color.FromArgb(color.R, color.G, color.B));
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    private void PickerColorChanged()
    {
        if (_updating)
            return;

        var color = _picker.SelectedColor;
        _updating = true;
        try
        {
            _red.Value = color.R;
            _green.Value = color.G;
            _blue.Value = color.B;
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RgbValueChanged()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            _picker.SetColor(Color.FromArgb(
                Decimal.ToByte(_red.Value),
                Decimal.ToByte(_green.Value),
                Decimal.ToByte(_blue.Value)));
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    private void AlphaValueChanged()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            _alphaSlider.Value = Decimal.ToInt32(_alpha.Value);
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    private void AlphaSliderChanged()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            _alpha.Value = _alphaSlider.Value;
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdatePreview()
    {
        var color = SelectedColor;
        _preview.PreviewColor = color;
        _valueLabel.Text = RgbaColorConverter.FormatDisplay(color);
    }
}

internal sealed class HsvColorPicker : Control
{
    private const int HueStripWidth = 26;
    private const int RegionGap = 12;
    private const int MarkerRadius = 5;

    private float _hue;
    private float _saturation;
    private float _value = 1f;
    private DragRegion _dragRegion;

    public HsvColorPicker()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        MinimumSize = new Size(220, 220);
        Cursor = Cursors.Cross;
    }

    public event EventHandler? SelectedColorChanged;

    public Color SelectedColor => FromHsv(_hue, _saturation, _value);

    public void SetColor(Color color)
    {
        ToHsv(color, out _hue, out _saturation, out _value);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var (svRect, hueRect) = GetRegions();
        if (svRect.Width <= 0 || svRect.Height <= 0 || hueRect.Width <= 0 || hueRect.Height <= 0)
            return;

        using (var hueBrush = new SolidBrush(FromHsv(_hue, 1f, 1f)))
            e.Graphics.FillRectangle(hueBrush, svRect);

        using (var whiteOverlay = new LinearGradientBrush(
                   svRect,
                   Color.White,
                   Color.FromArgb(0, Color.White),
                   LinearGradientMode.Horizontal))
        {
            e.Graphics.FillRectangle(whiteOverlay, svRect);
        }

        using (var blackOverlay = new LinearGradientBrush(
                   svRect,
                   Color.FromArgb(0, Color.Black),
                   Color.Black,
                   LinearGradientMode.Vertical))
        {
            e.Graphics.FillRectangle(blackOverlay, svRect);
        }

        DrawHueStrip(e.Graphics, hueRect);
        DrawMarkers(e.Graphics, svRect, hueRect);

        using var borderPen = new Pen(SystemColors.ControlDark);
        e.Graphics.DrawRectangle(borderPen, svRect.X, svRect.Y, svRect.Width - 1, svRect.Height - 1);
        e.Graphics.DrawRectangle(borderPen, hueRect.X, hueRect.Y, hueRect.Width - 1, hueRect.Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        var (svRect, hueRect) = GetRegions();
        if (svRect.Contains(e.Location))
            _dragRegion = DragRegion.SaturationValue;
        else if (hueRect.Contains(e.Location))
            _dragRegion = DragRegion.Hue;
        else
            return;

        Capture = true;
        UpdateFromPointer(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragRegion != DragRegion.None && Capture)
            UpdateFromPointer(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;

        if (_dragRegion != DragRegion.None)
            UpdateFromPointer(e.Location);
        _dragRegion = DragRegion.None;
        Capture = false;
    }

    private (Rectangle SvRect, Rectangle HueRect) GetRegions()
    {
        var content = Rectangle.Inflate(ClientRectangle, -2, -2);
        var hueWidth = Math.Min(HueStripWidth, Math.Max(16, content.Width / 8));
        var svWidth = Math.Max(1, content.Width - hueWidth - RegionGap);
        var svRect = new Rectangle(content.Left, content.Top, svWidth, content.Height);
        var hueRect = new Rectangle(svRect.Right + RegionGap, content.Top, hueWidth, content.Height);
        return (svRect, hueRect);
    }

    private void UpdateFromPointer(Point point)
    {
        var (svRect, hueRect) = GetRegions();
        switch (_dragRegion)
        {
            case DragRegion.SaturationValue:
                _saturation = Normalize(point.X, svRect.Left, svRect.Right - 1);
                _value = 1f - Normalize(point.Y, svRect.Top, svRect.Bottom - 1);
                break;
            case DragRegion.Hue:
                _hue = Normalize(point.Y, hueRect.Top, hueRect.Bottom - 1) * 360f;
                if (_hue >= 360f)
                    _hue = 0f;
                break;
            default:
                return;
        }

        Invalidate();
        SelectedColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private static float Normalize(int coordinate, int minimum, int maximum)
    {
        if (maximum <= minimum)
            return 0f;
        return Math.Clamp((coordinate - minimum) / (float)(maximum - minimum), 0f, 1f);
    }

    private static void DrawHueStrip(Graphics graphics, Rectangle rectangle)
    {
        const int hueStops = 7;
        var blend = new ColorBlend(hueStops)
        {
            Colors =
            [
                FromHsv(0f, 1f, 1f),
                FromHsv(60f, 1f, 1f),
                FromHsv(120f, 1f, 1f),
                FromHsv(180f, 1f, 1f),
                FromHsv(240f, 1f, 1f),
                FromHsv(300f, 1f, 1f),
                FromHsv(360f, 1f, 1f)
            ],
            Positions = [0f, 1f / 6f, 2f / 6f, 3f / 6f, 4f / 6f, 5f / 6f, 1f]
        };

        using var brush = new LinearGradientBrush(
            rectangle,
            blend.Colors[0],
            blend.Colors[^1],
            LinearGradientMode.Vertical)
        {
            InterpolationColors = blend
        };
        graphics.FillRectangle(brush, rectangle);
    }

    private void DrawMarkers(Graphics graphics, Rectangle svRect, Rectangle hueRect)
    {
        var x = svRect.Left + (int)MathF.Round(_saturation * Math.Max(0, svRect.Width - 1));
        var y = svRect.Top + (int)MathF.Round((1f - _value) * Math.Max(0, svRect.Height - 1));
        var marker = new Rectangle(x - MarkerRadius, y - MarkerRadius, MarkerRadius * 2, MarkerRadius * 2);

        using (var blackPen = new Pen(Color.Black, 3f))
            graphics.DrawEllipse(blackPen, marker);
        using (var whitePen = new Pen(Color.White, 1f))
            graphics.DrawEllipse(whitePen, marker);

        var hueY = hueRect.Top + (int)MathF.Round((_hue / 360f) * Math.Max(0, hueRect.Height - 1));
        using var huePen = new Pen(Color.Black, 2f);
        graphics.DrawLine(huePen, hueRect.Left - 3, hueY, hueRect.Right + 2, hueY);
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        hue %= 360f;
        if (hue < 0f)
            hue += 360f;

        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        var chroma = value * saturation;
        var sector = hue / 60f;
        var x = chroma * (1f - MathF.Abs(sector % 2f - 1f));
        var m = value - chroma;

        var (r, g, b) = sector switch
        {
            < 1f => (chroma, x, 0f),
            < 2f => (x, chroma, 0f),
            < 3f => (0f, chroma, x),
            < 4f => (0f, x, chroma),
            < 5f => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };

        return Color.FromArgb(
            ToByte(r + m),
            ToByte(g + m),
            ToByte(b + m));
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static void ToHsv(Color color, out float hue, out float saturation, out float value)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        if (delta <= float.Epsilon)
        {
            hue = 0f;
        }
        else if (Math.Abs(max - r) <= float.Epsilon)
        {
            hue = 60f * (((g - b) / delta) % 6f);
        }
        else if (Math.Abs(max - g) <= float.Epsilon)
        {
            hue = 60f * (((b - r) / delta) + 2f);
        }
        else
        {
            hue = 60f * (((r - g) / delta) + 4f);
        }

        if (hue < 0f)
            hue += 360f;

        saturation = max <= float.Epsilon ? 0f : delta / max;
        value = max;
    }

    private enum DragRegion
    {
        None,
        SaturationValue,
        Hue
    }
}

internal sealed class TransparencyPreview : Control
{
    private Color _color = Color.White;

    public TransparencyPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PreviewColor
    {
        get => _color;
        set
        {
            if (_color.ToArgb() == value.ToArgb())
                return;
            _color = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const int cell = 8;
        using var lightBrush = new SolidBrush(Color.White);
        using var darkBrush = new SolidBrush(Color.LightGray);
        for (var y = 0; y < ClientSize.Height; y += cell)
        {
            for (var x = 0; x < ClientSize.Width; x += cell)
            {
                var light = ((x / cell) + (y / cell)) % 2 == 0;
                e.Graphics.FillRectangle(light ? lightBrush : darkBrush, x, y, cell, cell);
            }
        }

        using (var colorBrush = new SolidBrush(_color))
            e.Graphics.FillRectangle(colorBrush, ClientRectangle);

        using var borderPen = new Pen(SystemColors.ControlDark);
        if (ClientSize.Width > 0 && ClientSize.Height > 0)
            e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}
