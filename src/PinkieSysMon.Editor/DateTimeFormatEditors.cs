using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace PinkieSysMon.Editor;

internal sealed record DateTimeFormatPreset(string Format, string Preview)
{
    public string DisplayText => $"{Format}    →    {Preview}";
}

internal sealed record DateTimeFormatToken(string Pattern, string Description)
{
    public override string ToString() => $"{Pattern}    —    {Description}";
}

internal static class DateTimeFormatCatalog
{
    public const string CustomFormatLabel = "Custom format...";

    private static readonly string[] PresetFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy",
        "MMMM",
        "MMM",
        "MM",
        "dd",
        "dddd",
        "ddd",
        "HH:mm",
        "HH:mm:ss",
        "hh:mm tt",
        "hh:mm:ss tt",
        "MMMM d",
        "MMMM d, yyyy",
        "dddd, MMMM d",
        "dddd, MMMM d, yyyy"
    ];

    private static readonly DateTimeFormatToken[] BuilderTokens =
    [
        new("yyyy", "4-digit year"),
        new("yy", "2-digit year"),
        new("MMMM", "full month name"),
        new("MMM", "abbreviated month name"),
        new("MM", "2-digit month"),
        new("%M", "month number"),
        new("dddd", "full day-of-week name"),
        new("ddd", "abbreviated day-of-week name"),
        new("dd", "2-digit day of month"),
        new("%d", "day of month"),
        new("HH", "2-digit hour, 24-hour clock"),
        new("%H", "hour, 24-hour clock"),
        new("hh", "2-digit hour, 12-hour clock"),
        new("%h", "hour, 12-hour clock"),
        new("mm", "2-digit minute"),
        new("%m", "minute"),
        new("ss", "2-digit second"),
        new("%s", "second"),
        new("tt", "AM/PM designator"),
        new("%t", "first character of AM/PM designator"),
        new("%/", "culture-specific date separator"),
        new("%:", "culture-specific time separator"),
        new("zzz", "UTC offset with hours and minutes"),
        new("%K", "time-zone information")
    ];

    public static IReadOnlyList<string> Formats => PresetFormats;
    public static IReadOnlyList<DateTimeFormatToken> Tokens => BuilderTokens;

    public static IReadOnlyList<DateTimeFormatPreset> CreatePresets(DateTime sample, CultureInfo culture) =>
        PresetFormats
            .Select(format => new DateTimeFormatPreset(format, sample.ToString(format, culture)))
            .ToArray();

    public static string GetEffectiveFormat(string? format) =>
        string.IsNullOrWhiteSpace(format)
            ? MetricValueFormatter.DefaultDateTimeFormat
            : format;

    public static bool TryCreatePreview(
        string? format,
        DateTime sample,
        CultureInfo culture,
        out string preview,
        out string? error)
    {
        try
        {
            preview = sample.ToString(GetEffectiveFormat(format), culture);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            preview = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    public static void Validate(string? format)
    {
        if (TryCreatePreview(format, DateTime.Now, CultureInfo.CurrentCulture, out _, out var error))
            return;

        throw new FormatException($"Invalid date/time format: {error}");
    }

    public static string QuoteLiteral(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        var escaped = literal
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"'{escaped}'";
    }
}

internal sealed class DateTimeFormatEditor : UITypeEditor
{
    public static readonly DateTimeFormatEditor Instance = new();

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
        UITypeEditorEditStyle.DropDown;

    public override object? EditValue(
        ITypeDescriptorContext? context,
        IServiceProvider provider,
        object? value)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService editorService)
            return value;

        var currentFormat = Convert.ToString(value) ?? MetricValueFormatter.DefaultDateTimeFormat;
        using var selector = new DateTimeFormatDropDown(currentFormat, editorService);
        editorService.DropDownControl(selector);

        if (selector.RequestCustomFormat)
        {
            using var builder = new DateTimeFormatBuilderDialog(currentFormat);
            return editorService.ShowDialog(builder) == DialogResult.OK
                ? builder.SelectedFormat
                : value;
        }

        return selector.SelectedFormat ?? value;
    }
}

internal sealed class DateTimeFormatDropDown : UserControl
{
    private readonly IWindowsFormsEditorService _editorService;
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        HorizontalScrollbar = true
    };

    public DateTimeFormatDropDown(string currentFormat, IWindowsFormsEditorService editorService)
    {
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        Size = new Size(540, 360);
        Controls.Add(_list);

        var presets = DateTimeFormatCatalog.CreatePresets(DateTime.Now, CultureInfo.CurrentCulture);
        foreach (var preset in presets)
            _list.Items.Add(new DateTimeFormatDropDownItem(preset.Format, preset.DisplayText, IsCustom: false));
        _list.Items.Add(new DateTimeFormatDropDownItem(null, DateTimeFormatCatalog.CustomFormatLabel, IsCustom: true));

        var currentIndex = presets
            .Select((preset, index) => (preset, index))
            .FirstOrDefault(item => string.Equals(item.preset.Format, currentFormat, StringComparison.Ordinal))
            .index;
        if (presets.Count > 0 &&
            currentIndex >= 0 &&
            currentIndex < presets.Count &&
            string.Equals(presets[currentIndex].Format, currentFormat, StringComparison.Ordinal))
        {
            _list.SelectedIndex = currentIndex;
        }

        _list.MouseClick += (_, e) =>
        {
            var index = _list.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                _list.SelectedIndex = index;
                CommitSelection();
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            CommitSelection();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }

    internal string? SelectedFormat { get; private set; }
    internal bool RequestCustomFormat { get; private set; }

    private void CommitSelection()
    {
        if (_list.SelectedItem is not DateTimeFormatDropDownItem item)
            return;

        RequestCustomFormat = item.IsCustom;
        SelectedFormat = item.IsCustom ? null : item.Format;
        _editorService.CloseDropDown();
    }

    private sealed record DateTimeFormatDropDownItem(string? Format, string Text, bool IsCustom)
    {
        public override string ToString() => Text;
    }
}

internal sealed class DateTimeFormatBuilderDialog : PinkieEditorForm
{
    private readonly TextBox _format = new() { Dock = DockStyle.Fill };
    private readonly Label _preview = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(6),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _validation = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ListBox _tokens = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        HorizontalScrollbar = true
    };
    private readonly TextBox _literal = new() { Dock = DockStyle.Fill };
    private readonly Button _ok = new()
    {
        Text = "OK",
        DialogResult = DialogResult.OK,
        AutoSize = true
    };

    public DateTimeFormatBuilderDialog(string? initialFormat)
    {
        Text = "Custom Date/Time Format";
        StartPosition = FormStartPosition.CenterParent;
        Width = 680;
        Height = 620;
        MinimumSize = new Size(560, 480);
        ShowInTaskbar = false;

        foreach (var token in DateTimeFormatCatalog.Tokens)
            _tokens.Items.Add(token);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 8
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = "Format", AutoSize = true }, 0, 0);
        root.Controls.Add(_format, 0, 1);

        var cultureName = CultureInfo.CurrentCulture.DisplayName;
        root.Controls.Add(new Label
        {
            Text = $"Live preview ({cultureName})",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        }, 0, 2);
        root.Controls.Add(_preview, 0, 3);

        root.Controls.Add(new Label
        {
            Text = "Tokens and patterns (double-click to insert)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        }, 0, 4);
        root.Controls.Add(_tokens, 0, 5);

        var literalPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        literalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        literalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        literalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        literalPanel.Controls.Add(new Label
        {
            Text = "Literal text",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);
        literalPanel.Controls.Add(_literal, 1, 0);
        var insertLiteral = new Button { Text = "Insert Literal", AutoSize = true };
        literalPanel.Controls.Add(insertLiteral, 2, 0);
        root.Controls.Add(literalPanel, 0, 6);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_validation, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 7);

        Controls.Add(root);
        AcceptButton = _ok;
        CancelButton = cancel;

        _format.Text = initialFormat ?? string.Empty;
        _format.SelectionStart = _format.TextLength;
        _format.TextChanged += (_, _) => UpdatePreview();
        _tokens.DoubleClick += (_, _) => InsertSelectedToken();
        _tokens.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            InsertSelectedToken();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        insertLiteral.Click += (_, _) => InsertLiteral();
        _literal.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            InsertLiteral();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        UpdatePreview();
        Shown += (_, _) => _format.Focus();
    }

    internal string SelectedFormat => _format.Text;

    private void InsertSelectedToken()
    {
        if (_tokens.SelectedItem is DateTimeFormatToken token)
            InsertAtCaret(token.Pattern);
    }

    private void InsertLiteral()
    {
        if (_literal.Text.Length == 0)
            return;

        InsertAtCaret(DateTimeFormatCatalog.QuoteLiteral(_literal.Text));
        _literal.Clear();
    }

    private void InsertAtCaret(string text)
    {
        var start = _format.SelectionStart;
        var length = _format.SelectionLength;
        _format.Text = _format.Text.Remove(start, length).Insert(start, text);
        _format.SelectionStart = start + text.Length;
        _format.SelectionLength = 0;
        _format.Focus();
    }

    private void UpdatePreview()
    {
        if (DateTimeFormatCatalog.TryCreatePreview(
                _format.Text,
                DateTime.Now,
                CultureInfo.CurrentCulture,
                out var preview,
                out var error))
        {
            _preview.Text = preview;
            _validation.Text = string.IsNullOrWhiteSpace(_format.Text)
                ? $"Empty format uses the default: {MetricValueFormatter.DefaultDateTimeFormat}"
                : "Valid format";
            _ok.Enabled = true;
        }
        else
        {
            _preview.Text = "Invalid format";
            _validation.Text = error ?? "Invalid date/time format.";
            _ok.Enabled = false;
        }
    }
}
