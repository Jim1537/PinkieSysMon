using CanonicalBinaryWidgetDefinition = PinkieSysMon.DashboardModel.BinaryWidgetDefinition;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private readonly TableLayoutPanel _binarySamplePanel = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
        RowCount = 4,
        Padding = new Padding(8, 5, 8, 7)
    };
    private readonly CheckBox _binarySampleEnabled = new()
    {
        Text = "Use test value (not live telemetry)",
        AutoSize = true
    };
    private readonly TextBox _binarySampleText = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Binary Editor preview test metric value"
    };
    private readonly CheckBox _binarySampleUnavailable = new()
    {
        Text = "Unavailable (no reading)",
        AutoSize = true
    };
    private CanonicalBinaryWidgetDefinition? _binarySampleWidget;
    private string? _binarySampleMetric;
    private bool _binarySampleUpdating;

    private void InitializeBinarySamplePanel()
    {
        _binarySamplePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _binarySamplePanel.Controls.Add(new Label
        {
            Text = "Binary preview sample — Editor only",
            AutoSize = true
        }, 0, 0);
        _binarySamplePanel.Controls.Add(_binarySampleEnabled, 0, 1);
        _binarySamplePanel.Controls.Add(_binarySampleText, 0, 2);
        _binarySamplePanel.Controls.Add(_binarySampleUnavailable, 0, 3);
        _dataPropertyTab.Controls.Add(_binarySamplePanel);
        _binarySamplePanel.Visible = false;
        _binarySampleText.Enabled = false;
        _binarySampleUnavailable.Enabled = false;

        _binarySampleEnabled.CheckedChanged += (_, _) => OnBinarySampleChanged();
        _binarySampleText.TextChanged += (_, _) => OnBinarySampleChanged();
        _binarySampleUnavailable.CheckedChanged += (_, _) => OnBinarySampleChanged();
    }

    private void OnBinarySampleChanged()
    {
        _binarySampleUnavailable.Enabled = _binarySampleEnabled.Checked;
        _binarySampleText.Enabled = _binarySampleEnabled.Checked && !_binarySampleUnavailable.Checked;
        if (!_binarySampleUpdating)
            RenderPreview(force: true, immediate: true);
    }

    private void ClearBinarySample()
    {
        _binarySampleUpdating = true;
        try
        {
            _binarySampleWidget = null;
            _binarySampleMetric = null;
            _binarySampleEnabled.Checked = false;
            _binarySampleUnavailable.Checked = false;
            _binarySampleText.Clear();
            _binarySamplePanel.Visible = false;
        }
        finally
        {
            _binarySampleUpdating = false;
        }
    }

    private void UpdateBinarySamplePanel()
    {
        var widget = _propertyGridTarget as CanonicalBinaryWidgetDefinition;
        var eligible = _selectedWidgets.Count == 1 &&
                       ReferenceEquals(_selectedWidget, widget) &&
                       EditorBinaryPreviewSample.Supports(widget);
        if (!eligible ||
            !ReferenceEquals(_binarySampleWidget, widget) ||
            !string.Equals(_binarySampleMetric, widget?.Metric, StringComparison.OrdinalIgnoreCase))
        {
            ClearBinarySample();
            if (eligible)
            {
                _binarySampleWidget = widget;
                _binarySampleMetric = widget!.Metric;
            }
        }

        _binarySamplePanel.Visible = eligible;
        if (eligible && ReferenceEquals(_propertyTabs.SelectedTab, _dataPropertyTab))
            _binarySamplePanel.BringToFront();
    }

    private IReadOnlyDictionary<string, object?> ApplyBinarySample(IReadOnlyDictionary<string, object?> metrics)
    {
        var widget = _binarySampleWidget;
        if (widget is null ||
            !_binarySampleEnabled.Checked ||
            !ReferenceEquals(widget, _selectedWidget) ||
            _selectedWidgets.Count != 1 ||
            !string.Equals(widget.Metric, _binarySampleMetric, StringComparison.OrdinalIgnoreCase) ||
            !EditorBinaryPreviewSample.Supports(widget))
        {
            return metrics;
        }

        return EditorBinaryPreviewSample.Apply(
            metrics, widget.Metric,
            _binarySampleUnavailable.Checked ? BinaryPreviewSampleMode.Unavailable : BinaryPreviewSampleMode.Text,
            _binarySampleText.Text);
    }
}
