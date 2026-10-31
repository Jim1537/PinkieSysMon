using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void TextValueSourceSemantics()
    {
        const string metric = RuntimeMetricContract.Version;
        var metricWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "metric-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = metric
        };
        var textWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "static-text",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Metric = metric,
            Prefix = "<",
            Suffix = ">"
        };
        var numericTextWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "numeric-text",
            ValueSource = TextValueContract.SourceText,
            Text = "125",
            SourceUnit = "MB/s",
            Unit = "Mbit/s",
            Format = "0.0",
            Prefix = "[",
            Suffix = "]"
        };

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-text-value-regression");
        try
        {
            var currentMetricWidget = (C.ValueWidgetDefinition)CurrentWidget(metricWidget);
            var currentTextWidget = (C.ValueWidgetDefinition)CurrentWidget(textWidget);
            var currentNumericTextWidget = (C.ValueWidgetDefinition)CurrentWidget(numericTextWidget);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new C.WidgetDefinition[] { currentMetricWidget, currentTextWidget, currentNumericTextWidget });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = "1.2.3" },
                antialias: false,
                typefaces);

            Assert(context.ResolveValueText(currentMetricWidget) == "1.2.3",
                "Metric-source value did not resolve formatted metric content.");
            Assert(context.ResolveValueText(currentTextWidget) == "<STATIC>",
                "Text-source value must apply Prefix/Suffix while preserving non-numeric literal text.");
            var expectedNumericText = $"[{1000d.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)}]";
            Assert(context.ResolveValueText(currentNumericTextWidget) == expectedNumericText,
                "Numeric literal Text must apply Source Unit conversion and Format before text presentation.");

            var dashboard = new DashboardDefinition();
            dashboard.Widgets.Add(textWidget);
            Assert(!DashboardMetricUsage.Collect(CurrentDashboard(dashboard)).Contains(metric),
                "Text-source value must not retain a telemetry dependency from a stale Metric field.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextValuePropertyVisibility()
    {
        var metricWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "metric-value-properties",
            ValueSource = TextValueContract.SourceMetric,
            Metric = RuntimeMetricContract.Version,
            Width = 100
        };
        var metricProperties = CurrentPropertyView(metricWidget, section: EditorPropertySection.General).GetProperties();
        var metricSourceType = GetNestedProperty(metricProperties, "Source", nameof(C.ValueWidgetDefinition.SourceKind));
        var metricSourceMetric = GetNestedProperty(metricProperties, "Source", nameof(WidgetDefinition.Metric));
        var metricSourceText = GetNestedProperty(metricProperties, "Source", nameof(WidgetDefinition.Text));
        Assert(metricSourceType is not null && metricSourceType.DisplayName == "Type" &&
               string.Equals(Convert.ToString(metricSourceType.GetValue(null)), TextValueContract.SourceMetric, StringComparison.Ordinal),
            "Metric-source value must expose Source/Type=Metric.");
        Assert(metricSourceMetric is { IsReadOnly: false },
            "Metric-source value must expose editable Source/Metric.");
        Assert(metricSourceText is { IsReadOnly: true } &&
               metricSourceText.Description == "Not applicable for selected Type.",
            "Metric-source value must keep Source/Text visible but disabled.");

        var textWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "text-value-properties",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Width = 100
        };
        var textProperties = CurrentPropertyView(textWidget, section: EditorPropertySection.General).GetProperties();
        var textSourceType = GetNestedProperty(textProperties, "Source", nameof(C.ValueWidgetDefinition.SourceKind));
        var textSourceText = GetNestedProperty(textProperties, "Source", nameof(WidgetDefinition.Text));
        var textSourceMetric = GetNestedProperty(textProperties, "Source", nameof(WidgetDefinition.Metric));
        Assert(textSourceType is not null && textSourceType.DisplayName == "Type" &&
               string.Equals(Convert.ToString(textSourceType.GetValue(null)), TextValueContract.SourceText, StringComparison.Ordinal),
            "Text-source value must expose Source/Type=Text.");
        Assert(textSourceText is { IsReadOnly: false },
            "Text-source value must expose editable Source/Text.");
        Assert(textSourceMetric is { IsReadOnly: true } &&
               textSourceMetric.Description == "Not applicable for selected Type.",
            "Text-source value must keep Source/Metric visible but disabled.");
        Assert(textProperties[nameof(WidgetDefinition.SourceUnit)] is null &&
               textProperties[nameof(WidgetDefinition.Prefix)] is null &&
               textProperties[nameof(WidgetDefinition.Suffix)] is null &&
               textProperties[nameof(WidgetDefinition.Unit)] is null &&
               textProperties[nameof(WidgetDefinition.Format)] is null &&
               textProperties[nameof(WidgetDefinition.Fallback)] is null,
            "Literal Text data properties must live on Data rather than Widget.");

        var metricAppearance = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Appearance).GetProperties();
        var textAppearance = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(metricAppearance["Alignment"] is null &&
               textAppearance["Alignment"] is null &&
               metricAppearance["Font"] is null && metricAppearance["Outline"] is null && metricAppearance["Overflow"] is null &&
               textAppearance["Font"] is null && textAppearance["Outline"] is null && textAppearance["Overflow"] is null,
            "Text presentation groups must move off Appearance.");

        var metricText = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Text).GetProperties();
        var literalText = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Text).GetProperties();
        Assert(metricText["Font"] is not null &&
               metricText["Alignment"] is not null &&
               metricText["Outline"] is not null &&
               metricText["Overflow"] is not null,
            "Metric-source Text/Value must expose Font, Alignment, Outline, and Overflow together on Text.");
        Assert(literalText["Font"] is not null &&
               literalText["Alignment"] is not null &&
               literalText["Outline"] is not null &&
               literalText["Overflow"] is not null,
            "Text-source Text/Value must expose Font, Alignment, Outline, and Overflow together on Text.");
        Assert(GetNestedProperty(literalText, "Overflow", nameof(WidgetDefinition.OverflowMode)) is { DisplayName: "Mode" },
            "Text-source Text/Value Overflow group must expose Mode.");

        var metricData = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Fallback)) is not null,
            "Metric-source Text/Value presentation fields must live under Data/Display.");
        var literalData = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Prefix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Suffix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Fallback)) is { IsReadOnly: true },
            "Non-numeric literal Text must keep Data discoverable while disabling numeric-only and unavailable-source fields.");

    }

    private static void LiteralNumericDataSemantics()
    {
        Assert(LiteralNumericContract.TryParse("125", out var integerValue) && Math.Abs(integerValue - 125d) < 0.000001,
            "Plain invariant numeric Text must be recognized as numeric.");
        Assert(LiteralNumericContract.TryParse("1.25e3", out var exponentValue) && Math.Abs(exponentValue - 1250d) < 0.000001,
            "Invariant exponent notation must be recognized as numeric Text.");
        Assert(!LiteralNumericContract.TryParse("1,234", out _),
            "Ambiguous thousands/decimal punctuation must not be guessed as numeric Text.");
        Assert(!LiteralNumericContract.TryParse("125 MB", out _),
            "Text carrying embedded unit characters must remain ordinary text.");

        var sourceDescriptor = LiteralNumericContract.GetDescriptor("MB/s");
        Assert(MetricValueConverter.TryConvertNumeric(125d, sourceDescriptor, "Mbit/s", out var converted) &&
               Math.Abs(converted - 1000d) < 0.000001,
            "Manual Source Unit=MB/s must convert through the same numeric pipeline to Mbit/s.");

        var numeric = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "literal-numeric-data",
            ValueSource = TextValueContract.SourceText,
            Text = "125",
            SourceUnit = "MB/s",
            Unit = "Mbit/s",
            Format = "0.0",
            Width = 100f
        };
        var data = CurrentPropertyView(numeric, section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(data, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: false } &&
               GetNestedProperty(data, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: false } &&
               GetNestedProperty(data, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: false },
            "Numeric literal Text must enable Source Unit, Unit, and Format under Data/Value.");
    }

    private static void LiteralNumericUnitOptionsFollowSourceUnit()
    {
        var widget = new C.ValueWidgetDefinition
        {
            Id = "literal-unit-refresh",
            SourceKind = C.ValueSourceKind.Text,
            Text = "125",
            SourceUnit = "B",
            Width = 100f
        };

        var properties = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var unit = GetNestedProperty(properties, "Value", nameof(C.ValueWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Literal numeric Data/Value/Unit property is missing.");

        var initialValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(initialValues.Contains("MB", StringComparer.Ordinal) &&
               !initialValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Source Unit=B must expose data-size target units, not data-rate units.");

        widget.SourceUnit = "B/s";
        var updatedValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(updatedValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)) &&
               updatedValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)) &&
               !updatedValues.Contains("MB", StringComparer.Ordinal),
            "The live literal Unit selector must follow Source Unit changes without a stale converter snapshot.");
    }

    private static void LiteralNumericDashboardNormalization()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-literal-normalization-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "literal-normalization.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "literal",
                      "width": 100,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "125",
                      "sourceUnit": "MB/s",
                      "unit": "GB",
                      "format": "0.0",
                      "overflowMode": "Clip"
                    }
                  ]
                }
                """,
                dashboardPath);

            var widget = definition.Widgets.Single();
            Assert(string.Equals(widget.SourceUnit, "MB/s", StringComparison.Ordinal),
                "Valid manual Source Unit must survive dashboard normalization.");
            Assert(widget.Unit is null,
                "A stale target Unit incompatible with the manual Source Unit must normalize to raw.");
            Assert(string.Equals(widget.Format, "0.0", StringComparison.Ordinal),
                "A valid numeric Format must survive literal numeric normalization.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextOverflowStateTransitions()
    {
        var widget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "overflow-transitions",
            Width = 0f,
            FontSize = 32f,
            OverflowMode = ValueOverflowContract.Clip
        };

        Assert(TextOverflowStateContract.NormalizeStoredState(widget) &&
               widget.Width == 0f &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Width=0 text must normalize to Overflow=None.");

        widget.OverflowMode = ValueOverflowContract.Scroll;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 137f, 32f) &&
               Math.Abs(widget.Width - 137f) < 0.001f &&
               widget.OverflowMode == ValueOverflowContract.Scroll,
            "Selecting a concrete Overflow mode from auto width must materialize the current intrinsic width.");

        widget.OverflowMode = ValueOverflowContract.None;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 137f, 32f) &&
               widget.Width == 0f &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Selecting Overflow=None must restore auto width.");

        widget.Width = 90f;
        Assert(TextOverflowStateContract.ApplyWidthChange(widget, 90f, 32f) &&
               widget.Width == 90f &&
               widget.OverflowMode == ValueOverflowContract.Clip,
            "Changing Width from auto to fixed while Overflow=None must select neutral Clip mode.");

        widget.Width = 0f;
        Assert(TextOverflowStateContract.ApplyWidthChange(widget, 1f, 32f) &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Changing Width back to zero must restore Overflow=None.");

        widget.OverflowMode = ValueOverflowContract.Wrap;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 1f, 32f) &&
               Math.Abs(widget.Width - 32f) < 0.001f,
            "Empty auto-width text must use the minimum default width when Overflow is enabled.");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "overflow-binary",
            Width = 120f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();
        binary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        binary.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        var trueText = GetBinaryText(binary, BinarySignalContract.TrueKey);
        var falseText = GetBinaryText(binary, BinarySignalContract.FalseKey);
        trueText.OverflowMode = ValueOverflowContract.Clip;
        falseText.OverflowMode = ValueOverflowContract.Ellipsis;

        Assert(!TextOverflowStateContract.NormalizeStoredState(binary) &&
               trueText.OverflowMode == ValueOverflowContract.Clip &&
               falseText.OverflowMode == ValueOverflowContract.Ellipsis,
            "Fixed-width value-only Binary must preserve independent concrete Overflow modes.");

        trueText.OverflowMode = ValueOverflowContract.None;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(
                   binary, BinarySignalContract.TrueKey, 137f, 32f) &&
               binary.Width == 0f &&
               trueText.OverflowMode == ValueOverflowContract.None &&
               falseText.OverflowMode == ValueOverflowContract.None,
            "Selecting None in one value-only Binary state must switch shared Width to auto and normalize both states to None.");

        trueText.OverflowMode = ValueOverflowContract.Scroll;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(
                   binary, BinarySignalContract.TrueKey, 137f, 32f) &&
               Math.Abs(binary.Width - 137f) < 0.001f &&
               trueText.OverflowMode == ValueOverflowContract.Scroll &&
               falseText.OverflowMode == ValueOverflowContract.Clip,
            "Selecting a concrete Binary Overflow mode from auto width must materialize shared Width and normalize sibling None to Clip.");

        binary.Width = 0f;
        Assert(TextOverflowStateContract.ApplyWidthChange(binary, 1f, 32f) &&
               trueText.OverflowMode == ValueOverflowContract.None &&
               falseText.OverflowMode == ValueOverflowContract.None,
            "Changing shared Binary Width to zero must normalize every value state to None.");

        binary.Profiles[BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
        Assert(TextOverflowStateContract.ApplyContextChange(binary, 80f, 32f) &&
               Math.Abs(binary.Width - 80f) < 0.001f &&
               trueText.OverflowMode == ValueOverflowContract.Clip,
            "Switching auto-width Binary to mixed value/image must materialize Width and normalize the active value state to Clip.");
        Assert(falseText.OverflowMode == ValueOverflowContract.None,
            "Hidden text presentation of a non-value Binary state must remain latent during mixed-state reconciliation.");
        Assert(!TextOverflowStateContract.GetSupportedModes(binary).Contains(ValueOverflowContract.None),
            "Mixed Binary must not offer Overflow=None because its image state requires a fixed container.");
    }

    private static void TextOverflowDashboardNormalization()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-overflow-normalization-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "overflow-normalization.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "auto",
                      "width": 0,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "AUTO",
                      "overflowMode": "Clip"
                    },
                    {
                      "type": "value",
                      "id": "fixed",
                      "width": 120,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "FIXED",
                      "overflowMode": "None"
                    }
                  ]
                }
                """,
                dashboardPath);

            var auto = definition.Widgets.Single(widget => widget.Id == "auto");
            var fixedWidth = definition.Widgets.Single(widget => widget.Id == "fixed");
            Assert(auto.OverflowMode == ValueOverflowContract.None,
                "Legacy Width=0 text must migrate to Overflow=None during load.");
            Assert(fixedWidth.OverflowMode == ValueOverflowContract.Clip,
                "Legacy fixed-width text with Overflow=None must migrate to Clip during load.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
