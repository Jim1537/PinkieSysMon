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
    private static void PropertyApplicabilityContract()
    {
        const string overflowReason = "Not applicable for selected Overflow Mode.";
        const string enabledReason = "Not applicable for selected Enabled.";
        const string typeReason = "Not applicable for selected Type.";
        const string modeReason = "Not applicable for selected Mode.";

        static string[] ChildDisplayNames(PropertyDescriptorCollection properties, string groupName)
        {
            var group = properties[groupName]
                ?? throw new InvalidOperationException($"Property group '{groupName}' is missing.");
            var value = group.GetValue(null)
                ?? throw new InvalidOperationException($"Property group '{groupName}' has no value.");
            return TypeDescriptor.GetProperties(value)
                .Cast<PropertyDescriptor>()
                .Select(property => property.DisplayName)
                .ToArray();
        }

        static void AssertDisabled(PropertyDescriptor? property, string reason, string context)
        {
            Assert(property is not null, $"{context}: property is missing.");
            Assert(property!.IsReadOnly, $"{context}: property must be disabled/read-only.");
            Assert(string.Equals(property.Description, reason, StringComparison.Ordinal),
                $"{context}: disabled reason is '{property.Description}', expected '{reason}'.");
        }

        static void AssertEnabled(PropertyDescriptor? property, string context)
        {
            Assert(property is not null, $"{context}: property is missing.");
            Assert(!property!.IsReadOnly, $"{context}: property must be editable.");
        }

        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "applicability-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = RuntimeMetricContract.Version,
            Width = 100f,
            OverflowMode = ValueOverflowContract.Clip,
            ShadowEnabled = false
        };

        var sourceMetric = CurrentPropertyView(value, section: EditorPropertySection.General).GetProperties();
        var metricSourceOrder = ChildDisplayNames(sourceMetric, "Source");
        AssertEnabled(GetNestedProperty(sourceMetric, "Source", nameof(WidgetDefinition.Metric)),
            "Metric-source Value/Metric");
        AssertDisabled(GetNestedProperty(sourceMetric, "Source", nameof(WidgetDefinition.Text)), typeReason,
            "Metric-source Value/Text");

        value.ValueSource = TextValueContract.SourceText;
        value.Text = "STATIC";
        var sourceText = CurrentPropertyView(value, section: EditorPropertySection.General).GetProperties();
        Assert(metricSourceOrder.SequenceEqual(ChildDisplayNames(sourceText, "Source")),
            "Value Source row order must remain stable when Type changes.");
        AssertDisabled(GetNestedProperty(sourceText, "Source", nameof(WidgetDefinition.Metric)), typeReason,
            "Text-source Value/Metric");
        AssertEnabled(GetNestedProperty(sourceText, "Source", nameof(WidgetDefinition.Text)),
            "Text-source Value/Text");

        var textClip = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        var overflowOrder = ChildDisplayNames(textClip, "Overflow");
        AssertDisabled(GetNestedProperty(textClip, "Overflow", nameof(WidgetDefinition.ScrollSpeed)), overflowReason,
            "Clip overflow/Scroll Speed");
        AssertDisabled(GetNestedProperty(textClip, "Overflow", nameof(WidgetDefinition.BumpPauseMs)), overflowReason,
            "Clip overflow/Bump Pause");

        value.OverflowMode = ValueOverflowContract.Scroll;
        var textScroll = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(overflowOrder.SequenceEqual(ChildDisplayNames(textScroll, "Overflow")),
            "Overflow property order must remain stable when Mode changes.");
        AssertEnabled(GetNestedProperty(textScroll, "Overflow", nameof(WidgetDefinition.ScrollSpeed)),
            "Scroll overflow/Scroll Speed");
        AssertDisabled(GetNestedProperty(textScroll, "Overflow", nameof(WidgetDefinition.BumpPauseMs)), overflowReason,
            "Scroll overflow/Bump Pause");

        value.OverflowMode = ValueOverflowContract.Bump;
        var textBump = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(overflowOrder.SequenceEqual(ChildDisplayNames(textBump, "Overflow")),
            "Overflow property order must remain stable for Bump mode.");
        AssertEnabled(GetNestedProperty(textBump, "Overflow", nameof(WidgetDefinition.ScrollSpeed)),
            "Bump overflow/Scroll Speed");
        AssertEnabled(GetNestedProperty(textBump, "Overflow", nameof(WidgetDefinition.BumpPauseMs)),
            "Bump overflow/Bump Pause");

        var shadowOff = CurrentPropertyView(value, section: EditorPropertySection.Appearance).GetProperties();
        var shadowOrder = ChildDisplayNames(shadowOff, "Shadow");
        AssertDisabled(GetNestedProperty(shadowOff, "Shadow", nameof(WidgetDefinition.ShadowOffsetX)), enabledReason,
            "Disabled shadow/Offset X");
        AssertDisabled(GetNestedProperty(shadowOff, "Shadow", nameof(WidgetDefinition.ShadowColor)), enabledReason,
            "Disabled shadow/Color");

        value.ShadowEnabled = true;
        var shadowOn = CurrentPropertyView(value, section: EditorPropertySection.Appearance).GetProperties();
        Assert(shadowOrder.SequenceEqual(ChildDisplayNames(shadowOn, "Shadow")),
            "Shadow property order must remain stable when Enabled changes.");
        AssertEnabled(GetNestedProperty(shadowOn, "Shadow", nameof(WidgetDefinition.ShadowOffsetX)),
            "Enabled shadow/Offset X");
        AssertEnabled(GetNestedProperty(shadowOn, "Shadow", nameof(WidgetDefinition.ShadowColor)),
            "Enabled shadow/Color");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "applicability-gauge",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 100f,
            TrackEnabled = false,
            NeedleEnabled = false,
            Threshold1Enabled = false
        };
        var gaugeAppearanceOff = CurrentPropertyView(gauge, section: EditorPropertySection.Appearance).GetProperties();
        var thresholdOrder = ChildDisplayNames(gaugeAppearanceOff, "Threshold 1");
        AssertDisabled(GetNestedProperty(gaugeAppearanceOff, "Threshold 1", nameof(WidgetDefinition.Threshold1Value)), enabledReason,
            "Disabled threshold/Value");
        AssertDisabled(GetNestedProperty(gaugeAppearanceOff, "Threshold 1", nameof(WidgetDefinition.Threshold1Color)), enabledReason,
            "Disabled threshold/Color");

        gauge.Threshold1Enabled = true;
        var gaugeAppearanceOn = CurrentPropertyView(gauge, section: EditorPropertySection.Appearance).GetProperties();
        Assert(thresholdOrder.SequenceEqual(ChildDisplayNames(gaugeAppearanceOn, "Threshold 1")),
            "Threshold property order must remain stable when Enabled changes.");
        AssertEnabled(GetNestedProperty(gaugeAppearanceOn, "Threshold 1", nameof(WidgetDefinition.Threshold1Value)),
            "Enabled threshold/Value");
        AssertEnabled(GetNestedProperty(gaugeAppearanceOn, "Threshold 1", nameof(WidgetDefinition.Threshold1Color)),
            "Enabled threshold/Color");

        var gaugeOff = CurrentPropertyView(gauge, section: EditorPropertySection.Gauge).GetProperties();
        var horseshoeOrder = ChildDisplayNames(gaugeOff, "Horseshoe");
        var needleOrder = ChildDisplayNames(gaugeOff, "Needle");
        AssertEnabled(GetNestedProperty(gaugeOff, "Horseshoe", nameof(WidgetDefinition.Reverse)),
            "Disabled Gauge track/Reverse");
        AssertDisabled(GetNestedProperty(gaugeOff, "Horseshoe", nameof(WidgetDefinition.Thickness)), enabledReason,
            "Disabled Gauge track/Thickness");
        AssertDisabled(GetNestedProperty(gaugeOff, "Needle", nameof(WidgetDefinition.NeedleThickness)), enabledReason,
            "Disabled Gauge needle/Thickness");

        gauge.TrackEnabled = true;
        gauge.NeedleEnabled = true;
        var gaugeOn = CurrentPropertyView(gauge, section: EditorPropertySection.Gauge).GetProperties();
        Assert(horseshoeOrder.SequenceEqual(ChildDisplayNames(gaugeOn, "Horseshoe")) &&
               needleOrder.SequenceEqual(ChildDisplayNames(gaugeOn, "Needle")),
            "Gauge property order must remain stable when track/needle Enabled changes.");
        AssertEnabled(GetNestedProperty(gaugeOn, "Horseshoe", nameof(WidgetDefinition.Thickness)),
            "Enabled Gauge track/Thickness");
        AssertEnabled(GetNestedProperty(gaugeOn, "Needle", nameof(WidgetDefinition.NeedleThickness)),
            "Enabled Gauge needle/Thickness");

        var image = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "applicability-image",
            SourceType = "icon",
            Source = "lucide:activity",
            Width = 100f,
            Height = 100f
        };
        var iconImage = CurrentPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        var imageOrder = ChildDisplayNames(iconImage, "Image");
        AssertDisabled(GetNestedProperty(iconImage, "Image", nameof(WidgetDefinition.Loop)), "Not applicable for selected Source.",
            "Icon image/Loop");

        image.SourceType = "file";
        image.Source = "example.gif";
        var fileImage = CurrentPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Assert(imageOrder.SequenceEqual(ChildDisplayNames(fileImage, "Image")),
            "Image property order must remain stable when Source Type changes.");
        AssertEnabled(GetNestedProperty(fileImage, "Image", nameof(WidgetDefinition.Loop)),
            "File image/Loop");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "applicability-binary",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        binary.EnsureStateVisualProfiles();
        var binaryAuto = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        var evaluationOrder = ChildDisplayNames(binaryAuto, "Evaluation");
        AssertDisabled(GetNestedProperty(binaryAuto, "Evaluation", nameof(WidgetDefinition.TrueIf)), modeReason,
            "Binary Auto/True If");
        AssertDisabled(GetNestedProperty(binaryAuto, "Evaluation", nameof(WidgetDefinition.Setpoint)), modeReason,
            "Binary Auto/Setpoint");

        binary.EvaluationMode = BinarySignalContract.EvaluationModeSetpoint;
        var binarySetpoint = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        Assert(evaluationOrder.SequenceEqual(ChildDisplayNames(binarySetpoint, "Evaluation")),
            "Binary Evaluation property order must remain stable when Mode changes.");
        AssertEnabled(GetNestedProperty(binarySetpoint, "Evaluation", nameof(WidgetDefinition.TrueIf)),
            "Binary Setpoint/True If");
        AssertEnabled(GetNestedProperty(binarySetpoint, "Evaluation", nameof(WidgetDefinition.Setpoint)),
            "Binary Setpoint/Setpoint");
    }

    private static void ContextDisabledCustomEditors()
    {
        var disabledGauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-disabled-gauge",
            Metric = FixtureOutputFps,
            TrackEnabled = true,
            NeedleEnabled = false
        };

        var disabledProperties = CurrentPropertyView(
            disabledGauge, section: EditorPropertySection.Gauge).GetProperties();
        var needleColor = GetNestedProperty(
            disabledProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        var pointerColor = GetNestedProperty(
            disabledProperties, "Needle", nameof(WidgetDefinition.NeedlePointerColor));
        Assert(needleColor is { IsReadOnly: true } &&
               needleColor.GetEditor(typeof(UITypeEditor)) is null &&
               pointerColor is { IsReadOnly: true } &&
               pointerColor.GetEditor(typeof(UITypeEditor)) is null,
            "Context-disabled Gauge color properties must suppress their custom UITypeEditor instead of looking editable.");

        disabledGauge.NeedleEnabled = true;
        var enabledProperties = CurrentPropertyView(
            disabledGauge, section: EditorPropertySection.Gauge).GetProperties();
        needleColor = GetNestedProperty(
            enabledProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        pointerColor = GetNestedProperty(
            enabledProperties, "Needle", nameof(WidgetDefinition.NeedlePointerColor));
        Assert(needleColor is { IsReadOnly: false } &&
               needleColor.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor &&
               pointerColor is { IsReadOnly: false } &&
               pointerColor.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor,
            "Re-enabling a contextual Gauge property must restore its custom editor immediately.");

        var mixedDisabled = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-mixed-disabled",
            Metric = FixtureOutputFps,
            NeedleEnabled = false
        };
        var mixedEnabled = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-mixed-enabled",
            Metric = FixtureOutputFps,
            NeedleEnabled = true
        };
        var mixedProperties = new EditorMultiPropertyView([
            CurrentPropertyView(mixedDisabled, section: EditorPropertySection.Gauge),
            CurrentPropertyView(mixedEnabled, section: EditorPropertySection.Gauge)
        ]).GetProperties();
        var mixedNeedleColor = GetNestedProperty(
            mixedProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        Assert(mixedNeedleColor is { IsReadOnly: true } &&
               mixedNeedleColor.GetEditor(typeof(UITypeEditor)) is null &&
               string.Equals(
                   mixedNeedleColor.Description,
                   "Not applicable for selected Enabled.",
                   StringComparison.Ordinal),
            "A custom-editor property disabled for one selected widget must remain in the multi-selection intersection but expose no editor.");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "context-editor-state-profile",
            Metric = "test.context.editor"
        };
        binary.EnsureStateVisualProfiles();
        var trueProfile = binary.Profiles![BinarySignalContract.TrueKey];
        trueProfile.SourceType = StateVisualProfileContract.SourceFile;
        trueProfile.Source = "state.png";

        var stateImage = CurrentPropertyView(
            binary, section: EditorPropertySection.Image).GetProperties();
        var stateColor = GetNestedProperty(
            stateImage,
            "State: True",
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.Color}");
        Assert(stateColor is { IsReadOnly: true } &&
               stateColor.GetEditor(typeof(UITypeEditor)) is null,
            "Context-disabled state-profile color properties must suppress their custom editor too.");
    }

    private static void DateTimeFormatEditorContract()
    {
        var widget = new C.ValueWidgetDefinition
        {
            Id = "datetime-format-editor",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = SystemMetricContract.DateTime,
            Width = 180f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };

        var data = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var format = GetNestedProperty(data, "Value", nameof(C.ValueWidgetDefinition.Format));
        Assert(format is not null && !format.IsReadOnly,
            "DateTime Data/Value must expose an editable Format property.");
        Assert(string.Equals(
                Convert.ToString(format!.GetValue(data)),
                MetricValueFormatter.DefaultDateTimeFormat,
                StringComparison.Ordinal),
            "An empty DateTime Format must display the runtime default mask.");

        var editor = format.GetEditor(typeof(UITypeEditor)) as UITypeEditor;
        Assert(editor is DateTimeFormatEditor,
            "DateTime Format must expose the dedicated format selector/editor.");
        Assert(editor!.GetEditStyle(null) == UITypeEditorEditStyle.DropDown,
            "DateTime Format selector must use a PropertyGrid drop-down editor.");

        var expectedPresets = new[]
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyy", "MMMM", "MMM", "MM", "dd", "dddd", "ddd",
            "HH:mm", "HH:mm:ss", "hh:mm tt", "hh:mm:ss tt", "MMMM d", "MMMM d, yyyy",
            "dddd, MMMM d", "dddd, MMMM d, yyyy"
        };
        Assert(expectedPresets.SequenceEqual(DateTimeFormatCatalog.Formats),
            "DateTime Format selector preset order/content does not match the PASS 4 contract.");

        var sample = new DateTime(2026, 9, 29, 17, 8, 9, DateTimeKind.Local);
        var enUs = CultureInfo.GetCultureInfo("en-US");
        var presets = DateTimeFormatCatalog.CreatePresets(sample, enUs);
        Assert(presets.Count == expectedPresets.Length &&
               presets.All(preset => !string.IsNullOrWhiteSpace(preset.Preview)),
            "Every DateTime preset must expose a live example.");
        Assert(string.Equals(
                presets.Single(preset => preset.Format == "MMMM").Preview,
                "September",
                StringComparison.Ordinal),
            "MMMM preset preview must render the full month name for the selected culture.");

        const string custom = "dddd, MMMM d, yyyy 'at' HH:mm:ss";
        format.SetValue(data, custom);
        Assert(string.Equals(widget.Format, custom, StringComparison.Ordinal),
            "A valid custom DateTime format must be stored without rewriting it.");
        Assert(string.Equals(Convert.ToString(format.GetValue(data)), custom, StringComparison.Ordinal),
            "A valid custom DateTime format must remain editable even when it is not a preset.");

        var reloaded = (C.ValueWidgetDefinition)DashboardJson.CloneWidget(widget);
        Assert(string.Equals(reloaded.Format, custom, StringComparison.Ordinal),
            "A valid custom DateTime format must round-trip through canonical schema-19 widget JSON unchanged.");

        var descriptor = MetricContract.GetDescriptor(SystemMetricContract.DateTime)
            ?? throw new InvalidOperationException("DateTime metric descriptor is missing.");
        Assert(string.Equals(
                MetricValueFormatter.Format(sample, descriptor, null, custom, enUs),
                "Tuesday, September 29, 2026 at 17:08:09",
                StringComparison.Ordinal),
            "Custom DateTime format must continue to use the runtime formatter with current-culture semantics.");

        var previous = widget.Format;
        var rejected = false;
        try
        {
            format.SetValue(data, "yyyy-MM-dd '");
        }
        catch (FormatException)
        {
            rejected = true;
        }
        Assert(rejected, "An invalid custom DateTime format must be rejected by the editor descriptor.");
        Assert(string.Equals(widget.Format, previous, StringComparison.Ordinal),
            "Rejecting an invalid DateTime format must not mutate the persisted widget value.");

        Assert(DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "yyyy") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "MMMM") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "dddd") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "HH") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "mm") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "ss") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "tt") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "%/") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "%:"),
            "Custom DateTime builder must expose year/month/day/day-of-week/time/AM-PM/separator token choices.");
        Assert(string.Equals(DateTimeFormatCatalog.QuoteLiteral("at"), "'at'", StringComparison.Ordinal),
            "Custom DateTime builder must support safe literal-text insertion.");
    }

    private static void MultiSelectionCommonPropertyEditing()
    {
        var first = new C.ValueWidgetDefinition
        {
            Id = "multi-value-a",
            Name = "A",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = FixtureOutputFps,
            X = 10f,
            Y = 20f,
            Width = 110f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };
        var second = new C.ValueWidgetDefinition
        {
            Id = "multi-value-b",
            Name = "B",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = FixtureOutputFps,
            X = 30f,
            Y = 20f,
            Width = 140f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };

        var view = new EditorMultiPropertyView([
            new EditorPropertyView(first, section: EditorPropertySection.General),
            new EditorPropertyView(second, section: EditorPropertySection.General)
        ]);
        var properties = view.GetProperties();

        var x = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.X));
        var y = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Y));
        var name = GetNestedProperty(properties, "Identity", nameof(C.WidgetDefinition.Name));
        Assert(x is MultiSelectionPropertyDescriptor && y is MultiSelectionPropertyDescriptor &&
               name is MultiSelectionPropertyDescriptor,
            "Canonical multi-selection common properties must use the multi-selection descriptor.");
        Assert(x!.GetValue(null) is null &&
               string.Equals(x.Converter.ConvertToInvariantString(x.GetValue(null)), "...", StringComparison.Ordinal),
            "Different canonical X values must be represented as a mixed value.");
        Assert(Convert.ToSingle(y!.GetValue(null), CultureInfo.InvariantCulture) == 20f,
            "Equal canonical common values must be shown normally.");

        x.SetValue(view, 64f);
        Assert(first.X == 64f && second.X == 64f,
            "Editing a canonical common mixed property must apply to every selected widget.");
        name!.SetValue(view, "Shared name");
        Assert(first.Name == "Shared name" && second.Name == "Shared name",
            "Editing a canonical common string property must apply to every selected widget.");

        first.Width = 111f;
        second.Width = 222f;
        _ = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Width))!.GetValue(null);
        Assert(first.Width == 111f && second.Width == 222f,
            "Reading an untouched canonical mixed property must preserve individual values.");

        var reversed = new EditorMultiPropertyView([
            new EditorPropertyView(second, section: EditorPropertySection.General),
            new EditorPropertyView(first, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(properties.Cast<PropertyDescriptor>().Select(property => property.Name)
                .SequenceEqual(reversed.Cast<PropertyDescriptor>().Select(property => property.Name)),
            "Selection order must not change canonical common top-level property order.");
        Assert(string.Equals(
                GetNestedProperty(reversed, "Geometry", nameof(C.WidgetDefinition.Width))!.Converter.ConvertToInvariantString(
                    GetNestedProperty(reversed, "Geometry", nameof(C.WidgetDefinition.Width))!.GetValue(null)),
                "...", StringComparison.Ordinal),
            "Selection order must not change canonical mixed-value semantics.");

        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "multi-gauge",
            Metric = FixtureOutputFps,
            X = 5f,
            Y = 6f,
            Width = 100f,
            Height = 100f
        };
        var heterogeneous = new EditorMultiPropertyView([
            new EditorPropertyView(first, section: EditorPropertySection.General),
            new EditorPropertyView(gauge, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(GetNestedProperty(heterogeneous, "Geometry", nameof(C.WidgetDefinition.X)) is not null,
            "Different canonical widget types must retain semantically common Geometry properties.");
        Assert(GetNestedProperty(heterogeneous, "Source", nameof(C.ValueWidgetDefinition.Metric)) is not null,
            "Different metric-backed canonical widgets must intersect by the shared metric semantic key.");
        Assert(GetNestedProperty(heterogeneous, "Source", nameof(C.ValueWidgetDefinition.SourceKind)) is null,
            "Value-only SourceKind must not leak into heterogeneous canonical multi-selection.");
    }

    private static void MultiSelectionApplicabilityAndContext()
    {
        const string overflowReason = "Not applicable for selected Overflow Mode.";
        const string sourceTypeReason = "Not applicable for selected Type.";

        var scroll = new C.ValueWidgetDefinition
        {
            Id = "multi-scroll",
            SourceKind = C.ValueSourceKind.Text,
            Text = "A",
            Width = 100f,
            TextPresentation = new C.TextPresentationDefinition
            {
                OverflowMode = ValueOverflowContract.Scroll,
                ScrollSpeed = 20f
            }
        };
        var clip = new C.ValueWidgetDefinition
        {
            Id = "multi-clip",
            SourceKind = C.ValueSourceKind.Text,
            Text = "B",
            Width = 100f,
            TextPresentation = new C.TextPresentationDefinition
            {
                OverflowMode = ValueOverflowContract.Clip,
                ScrollSpeed = 30f
            }
        };

        var mixedOverflow = new EditorMultiPropertyView([
            new EditorPropertyView(scroll, section: EditorPropertySection.Text),
            new EditorPropertyView(clip, section: EditorPropertySection.Text)
        ]);
        var scrollSpeed = GetNestedProperty(mixedOverflow.GetProperties(), "Overflow", nameof(C.TextPresentationDefinition.ScrollSpeed));
        Assert(scrollSpeed is not null && scrollSpeed.IsReadOnly &&
               string.Equals(scrollSpeed.Description, overflowReason, StringComparison.Ordinal),
            "A canonical property disabled for any selected widget must remain visible with its contextual reason.");
        scrollSpeed!.SetValue(mixedOverflow, 99f);
        Assert(scroll.TextPresentation.ScrollSpeed == 20f && clip.TextPresentation.ScrollSpeed == 30f,
            "Disabled canonical multi-selection edits must not partially apply.");

        clip.TextPresentation.OverflowMode = ValueOverflowContract.Scroll;
        var enabledOverflow = new EditorMultiPropertyView([
            new EditorPropertyView(scroll, section: EditorPropertySection.Text),
            new EditorPropertyView(clip, section: EditorPropertySection.Text)
        ]);
        scrollSpeed = GetNestedProperty(enabledOverflow.GetProperties(), "Overflow", nameof(C.TextPresentationDefinition.ScrollSpeed));
        Assert(scrollSpeed is not null && !scrollSpeed.IsReadOnly,
            "A canonical common property must become editable when applicable to every selected widget.");
        scrollSpeed!.SetValue(enabledOverflow, 55f);
        Assert(scroll.TextPresentation.ScrollSpeed == 55f && clip.TextPresentation.ScrollSpeed == 55f,
            "An enabled canonical multi-selection edit must apply to all selected widgets.");

        var fileImage = new C.ImageWidgetDefinition
        {
            Id = "multi-image-file",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.File, Source = "images/a.png" }
        };
        var iconImage = new C.ImageWidgetDefinition
        {
            Id = "multi-image-icon",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.Icon, Source = "activity" }
        };
        var imageView = new EditorMultiPropertyView([
            new EditorPropertyView(fileImage, "root", "dashboard", section: EditorPropertySection.General),
            new EditorPropertyView(iconImage, "root", "dashboard", section: EditorPropertySection.General)
        ]);
        var imageSource = GetNestedProperty(imageView.GetProperties(), "Source", "Source");
        Assert(imageSource is not null && imageSource.IsReadOnly &&
               string.Equals(imageSource.Description, sourceTypeReason, StringComparison.Ordinal),
            "A canonical image source editor with mixed Source Type context must remain visible but disabled.");

        var power = new C.PowerWidgetDefinition
        {
            Id = "multi-power",
            PowerSource = PowerMetricContract.UpsSource
        };
        var media = new C.MediaSystemWidgetDefinition
        {
            Id = "multi-media",
            MediaSource = MediaMetricContract.OutputSource
        };
        var differentSourceSemantics = new EditorMultiPropertyView([
            new EditorPropertyView(power, section: EditorPropertySection.General),
            new EditorPropertyView(media, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(differentSourceSemantics["Source"] is null,
            "Same display label with incompatible semantic keys must not create a false common Source group.");

        var firstBox = new MultiEditTestBox { Value = 1 };
        var secondBox = new MultiEditTestBox { Value = 2 };
        var atomic = new MultiSelectionPropertyDescriptor([
            new MultiEditTestPropertyDescriptor(firstBox, throwsOnSet: false),
            new MultiEditTestPropertyDescriptor(secondBox, throwsOnSet: true)
        ]);
        var threw = false;
        try
        {
            atomic.SetValue(null, 9);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert(threw, "Synthetic multi-edit failure fixture must throw.");
        Assert(firstBox.Value == 1 && secondBox.Value == 2,
            "A failed multi-selection write must roll back earlier targets instead of partially applying.");
    }

    private static void GlobalIconFormatDiscoveryAndCapabilities()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-formats");
        try
        {
            var libraryRoot = WriteGlobalIconTestAssets(tempRoot, includeBroken: true);
            File.WriteAllText(Path.Combine(libraryRoot, "ignored.tiff"), "unsupported");

            var entries = GlobalAssetResolver.EnumerateIconsFromRoot(tempRoot)
                .Where(entry => string.Equals(entry.Library, "Mixed", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var logicalNames = entries.Select(entry => entry.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var expected in new[]
                     {
                         "mixed:vector",
                         "mixed:red.png",
                         "mixed:red.jpg",
                         "mixed:red.jpeg",
                         "mixed:red.bmp",
                         "mixed:red.gif",
                         "mixed:red.ico",
                         "mixed:red.webp",
                         "mixed:animated.gif",
                         "mixed:broken.png"
                     })
            {
                Assert(logicalNames.Contains(expected), $"Global icon library did not enumerate '{expected}'.");
            }

            Assert(!entries.Any(entry => entry.Path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)),
                "Unsupported icon extensions must not enter the global icon library.");
            Assert(entries.Single(entry => entry.LogicalName == "mixed:vector").Format == "SVG" &&
                   entries.Single(entry => entry.LogicalName == "mixed:red.webp").Format == "WEBP",
                "Global icon enumeration must report each asset format.");

            Assert(GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:vector")
                       .EndsWith("vector.svg", StringComparison.OrdinalIgnoreCase),
                "Extensionless SVG logical identity must remain backward compatible.");
            Assert(GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:red.png")
                       .EndsWith("red.png", StringComparison.OrdinalIgnoreCase),
                "Raster icon logical identity must resolve the explicit extension.");

            var ambiguous = false;
            try
            {
                _ = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:red");
            }
            catch (InvalidDataException ex)
            {
                ambiguous = ex.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase);
            }
            Assert(ambiguous,
                "Extensionless raster identity must fail clearly when multiple formats share one stem.");

            var svgCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "vector.svg"));
            Assert(svgCapabilities.Tintable && !svgCapabilities.UsesIntrinsicColor &&
                   !svgCapabilities.IsAnimated && svgCapabilities.SupportsAlpha,
                "SVG global icons must advertise tintable static alpha-capable semantics.");

            var pngCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.png"));
            Assert(!pngCapabilities.Tintable && pngCapabilities.UsesIntrinsicColor && !pngCapabilities.IsAnimated,
                "Raster global icons must advertise intrinsic-color semantics.");

            var jpegCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.jpg"));
            Assert(!jpegCapabilities.SupportsAlpha,
                "JPEG global icons must report opaque/no-alpha semantics.");

            var animatedCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "animated.gif"));
            Assert(animatedCapabilities.IsAnimated && animatedCapabilities.HasFrameTiming,
                "Animated global icon assets must expose animation and frame-timing capabilities.");

            var corruptReported = false;
            try
            {
                _ = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "broken.png"));
            }
            catch (InvalidDataException ex)
            {
                corruptReported = ex.Message.Contains("decode", StringComparison.OrdinalIgnoreCase) ||
                                  ex.Message.Contains("icon", StringComparison.OrdinalIgnoreCase);
            }
            Assert(corruptReported,
                "Corrupt supported icon files must fail with a clear asset/decode error.");
            Assert(IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.webp")).Format == "WebP",
                "A corrupt sibling icon must not poison the library capability scan/cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GlobalIconWindowsSafeLogicalNames()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-logical-names");
        try
        {
            var libraryName = "Pinkie Pie! (custom)+&";
            var fileName = "pinkie neutral! [01]+&.png";
            var libraryRoot = Path.Combine(tempRoot, "assets", "icons", libraryName);
            Directory.CreateDirectory(libraryRoot);
            var assetPath = Path.Combine(libraryRoot, fileName);
            WriteSolidTestImage(assetPath, SkiaSharp.SKColors.Magenta, width: 2, height: 2);

            var expectedLogicalName = $"{libraryName.ToLowerInvariant()}:{fileName}";
            var entry = GlobalAssetResolver.EnumerateIconsFromRoot(tempRoot)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.LogicalName, expectedLogicalName, StringComparison.Ordinal));
            Assert(entry is not null,
                "Global icon discovery must preserve ordinary Windows-valid punctuation and spaces in library/file names.");

            var resolved = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, expectedLogicalName);
            Assert(string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(assetPath), StringComparison.OrdinalIgnoreCase),
                "A discovered icon with Windows-valid punctuation must resolve through its logical library:name identity.");

            foreach (var invalidLogicalName in new[]
                     {
                         "bad/library:icon.png",
                         "bad\\library:icon.png",
                         "bad|library:icon.png",
                         "CON:icon.png",
                         "COM¹:icon.png",
                         "valid:NUL.png",
                         "valid:LPT².png",
                         "valid:icon?.png",
                         "valid:.."
                     })
            {
                var rejected = false;
                try
                {
                    _ = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, invalidLogicalName);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                Assert(rejected,
                    $"Unsafe or Win32-invalid logical icon name '{invalidLogicalName}' must be rejected before path resolution.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void WindowsPathComponentValidationContract()
    {
        foreach (var valid in new[]
                 {
                     "Pinkie Pie! (custom)+&",
                     ".hidden",
                     "name.with.multiple.extensions.png"
                 })
        {
            Assert(WindowsPathComponentValidator.IsSafe(valid),
                $"Windows-valid path component '{valid}' must remain accepted.");
        }

        var invalid = new Dictionary<string, WindowsPathComponentValidationError>(StringComparer.Ordinal)
        {
            ["."] = WindowsPathComponentValidationError.DotSegment,
            [".."] = WindowsPathComponentValidationError.DotSegment,
            ["bad/name"] = WindowsPathComponentValidationError.InvalidCharacter,
            ["bad\\name"] = WindowsPathComponentValidationError.InvalidCharacter,
            ["bad."] = WindowsPathComponentValidationError.TrailingSpaceOrPeriod,
            ["bad "] = WindowsPathComponentValidationError.TrailingSpaceOrPeriod,
            ["NUL.txt"] = WindowsPathComponentValidationError.ReservedDeviceName,
            ["COM¹.log"] = WindowsPathComponentValidationError.ReservedDeviceName,
            ["LPT³"] = WindowsPathComponentValidationError.ReservedDeviceName
        };

        foreach (var pair in invalid)
        {
            var actual = WindowsPathComponentValidator.Validate(pair.Key);
            Assert(actual == pair.Value,
                $"Windows path component '{pair.Key}' must be rejected as {pair.Value}; got {actual}.");
        }
    }

    private static void GlobalIconRenderingAndApplicability()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-rendering");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            using var icons = new IconAssetCache(dashboardRoot);

            SkiaSharp.SKColor RenderCenter(string logicalName, SkiaSharp.SKColor requestedColor)
            {
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
                    16, 16, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                    ?? throw new InvalidOperationException("Could not create global icon regression surface.");
                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                var icon = icons.Get(logicalName);
                icon.Draw(
                    surface.Canvas,
                    new SkiaSharp.SKRect(0, 0, 16, 16),
                    requestedColor,
                    1f,
                    "stretch",
                    loop: false,
                    antialias: false,
                    elapsedMs: 0);
                surface.Canvas.Flush();
                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect global icon regression pixels.");
                return pixels.GetPixelColor(8, 8);
            }

            var svg = RenderCenter("mixed:vector", SkiaSharp.SKColors.Lime);
            Assert(svg.Green > 220 && svg.Red < 40 && svg.Blue < 40,
                "Tintable SVG global icons must use the configured icon Color.");

            foreach (var logicalName in new[]
                     {
                         "mixed:red.png",
                         "mixed:red.jpg",
                         "mixed:red.jpeg",
                         "mixed:red.bmp",
                         "mixed:red.gif",
                         "mixed:red.ico",
                         "mixed:red.webp"
                     })
            {
                var rendered = RenderCenter(logicalName, SkiaSharp.SKColors.Lime);
                Assert(rendered.Red > 160 && rendered.Green < 100 && rendered.Blue < 100 && rendered.Alpha > 200,
                    $"Raster global icon '{logicalName}' did not render with its intrinsic red color.");
            }

            var image = new C.ImageWidgetDefinition
            {
                Id = "global-icon-image",
                Width = 32f,
                Height = 32f,
                Asset = new C.ImageAssetPresentationDefinition
                {
                    SourceType = C.ImageAssetSourceType.Icon,
                    Source = "mixed:vector",
                    Loop = true
                }
            };

            var svgAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var svgColor = GetNestedProperty(svgAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var svgImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var svgLoop = GetNestedProperty(svgImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(svgColor is { IsReadOnly: false } &&
                   svgLoop is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Static tintable SVG icons must enable Color and disable Loop by asset capability.");

            image.Asset.Source = "mixed:red.png";
            var pngAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var pngColor = GetNestedProperty(pngAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var pngImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var pngLoop = GetNestedProperty(pngImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(pngColor is { IsReadOnly: true, Description: "Not applicable for selected Source." } &&
                   pngLoop is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Static intrinsic-color raster icons must keep Color/Loop visible but disabled.");

            image.Asset.Source = "mixed:animated.gif";
            var gifAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var gifColor = GetNestedProperty(gifAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var gifImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var gifLoop = GetNestedProperty(gifImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(gifColor is { IsReadOnly: true } && gifLoop is { IsReadOnly: false },
                "Animated raster icons must keep intrinsic Color disabled while enabling Loop.");

            var binary = new C.BinaryWidgetDefinition
            {
                Id = "animated-global-icon-state",
                Metric = "test.global-icon.animation",
                X = 0,
                Y = 0,
                Width = 8,
                Height = 8
            };
            TextOverflowStateContract.EnsureProfiles(binary);
            var trueProfile = binary.Profiles[BinarySignalContract.TrueKey];
            trueProfile.ContentType = C.StateContentType.Image;
            trueProfile.Asset.SourceType = C.ImageAssetSourceType.Icon;
            trueProfile.Asset.Source = "mixed:animated.gif";
            trueProfile.Asset.Fit = StateVisualProfileContract.FitStretch;
            trueProfile.Asset.Loop = false;
            trueProfile.Opacity = 1f;

            var stateImage = new EditorPropertyView(
                binary, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            static string StateProperty(string key, StateVisualProfileField field) =>
                $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";
            var trueLoop = GetNestedProperty(
                stateImage,
                "State: True",
                StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop));
            var trueColor = GetNestedProperty(
                stateImage,
                "State: True",
                StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color));
            Assert(trueLoop is { IsReadOnly: false } &&
                   trueColor is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Stateful animated raster icons must enable Loop and disable tint Color.");

            long nowMs = 10_000;
            using var images = new ImageAssetCache(dashboardRoot);
            icons.Preload(new C.WidgetDefinition[] { binary });
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new C.WidgetDefinition[] { binary });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons, () => nowMs);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [binary.Metric!] = true
                },
                antialias: false,
                typefaces);
            using var stateSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(8, 8))
                ?? throw new InvalidOperationException("Could not create animated icon state surface.");

            SkiaSharp.SKColor RenderStateCenter()
            {
                stateSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                renderer.Render(stateSurface.Canvas, binary, context);
                stateSurface.Canvas.Flush();
                using var pixels = stateSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect animated icon state pixels.");
                return pixels.GetPixelColor(4, 4);
            }

            var first = RenderStateCenter();
            nowMs = 10_150;
            var second = RenderStateCenter();
            Assert(first.Red > 240 && first.Blue < 20 && second.Blue > 240 && second.Red < 20,
                "Stateful animated global icons must use activation-relative frame timing.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void AllWidgetPropertiesAssignedToExplicitTabs()
    {
        var endpoints = new[]
        {
            new WindowsMediaTelemetrySource.MediaEndpointInfo("coverage-endpoint", "Coverage Endpoint")
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        void Check(
            C.WidgetDefinition widget,
            IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>? mediaEndpoints = null,
            IDictionary<string, string>? endpointTypeOverrides = null)
        {
            if (widget is C.StateVisualWidgetDefinition stateWidget)
                TextOverflowStateContract.EnsureProfiles(stateWidget);

            var all = new EditorPropertyView(
                widget,
                mediaEndpoints: mediaEndpoints,
                endpointTypeOverrides: endpointTypeOverrides,
                section: EditorPropertySection.All).GetProperties();

            var expected = new HashSet<string>(StringComparer.Ordinal);
            CollectLeafSemanticKeys(all, expected);

            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in new[]
                     {
                         EditorPropertySection.General,
                         EditorPropertySection.Appearance,
                         EditorPropertySection.Data,
                         EditorPropertySection.Gauge,
                         EditorPropertySection.States,
                         EditorPropertySection.Image,
                         EditorPropertySection.Text
                     })
            {
                var properties = new EditorPropertyView(
                    widget,
                    mediaEndpoints: mediaEndpoints,
                    endpointTypeOverrides: endpointTypeOverrides,
                    section: section).GetProperties();
                CollectLeafSemanticKeys(properties, assigned);
            }

            var missing = expected.Except(assigned, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Assert(missing.Length == 0,
                $"{widget.Type} has canonical property semantics not assigned to an explicit tab: {string.Join(", ", missing)}.");
        }

        Check(new C.ValueWidgetDefinition
        {
            Id = "coverage-value-metric",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = SystemMetricContract.Uptime,
            Width = 120f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Bump }
        });

        Check(new C.ValueWidgetDefinition
        {
            Id = "coverage-value-text",
            SourceKind = C.ValueSourceKind.Text,
            Text = "123",
            SourceUnit = "seconds",
            Width = 120f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Scroll }
        });

        var barFill = new C.BarWidgetDefinition
        {
            Id = "coverage-bar-fill",
            Metric = FixtureOutputFps,
            ContentMode = BarImageContract.ContentModeFill,
            Width = 160f,
            Height = 24f
        };
        foreach (var threshold in barFill.Thresholds.Items)
            threshold.Enabled = true;
        Check(barFill);

        Check(new C.BarWidgetDefinition
        {
            Id = "coverage-bar-image",
            Metric = FixtureOutputFps,
            ContentMode = BarImageContract.ContentModeImage,
            Width = 160f,
            Height = 24f,
            Image = new C.BarImagePresentationDefinition { Source = "coverage.png" }
        });

        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "coverage-gauge",
            Metric = FixtureOutputFps,
            Width = 120f,
            Height = 120f
        };
        foreach (var threshold in gauge.Thresholds.Items)
            threshold.Enabled = true;
        Check(gauge);

        Check(new C.ImageWidgetDefinition
        {
            Id = "coverage-image-file",
            Width = 120f,
            Height = 120f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.File, Source = "coverage.png" }
        });
        Check(new C.ImageWidgetDefinition
        {
            Id = "coverage-image-icon",
            Width = 120f,
            Height = 120f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.Icon, Source = "activity" }
        });

        var binary = new C.BinaryWidgetDefinition
        {
            Id = "coverage-binary",
            Metric = SystemMetricContract.Uptime,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            Width = 140f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binary);
        binary.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        binary.Profiles[BinarySignalContract.TrueKey].TextPresentation.OverflowMode = ValueOverflowContract.Bump;
        binary.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Image;
        binary.Profiles[BinarySignalContract.FalseKey].Asset.SourceType = C.ImageAssetSourceType.File;
        binary.Profiles[BinarySignalContract.FalseKey].Asset.Source = "coverage.gif";
        Check(binary);

        var binaryIcon = new C.BinaryWidgetDefinition
        {
            Id = "coverage-binary-icon",
            Metric = FixtureOutputFps,
            Width = 140f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binaryIcon);
        binaryIcon.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Image;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].Asset.SourceType = C.ImageAssetSourceType.Icon;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].Asset.Source = "activity";
        Check(binaryIcon);

        var power = new C.PowerWidgetDefinition
        {
            Id = "coverage-power",
            PowerSource = PowerMetricContract.UpsSource,
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(power);
        Check(power);

        var mediaPlayer = new C.MediaPlayerWidgetDefinition
        {
            Id = "coverage-media-player",
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(mediaPlayer);
        Check(mediaPlayer);

        var mediaSystem = new C.MediaSystemWidgetDefinition
        {
            Id = "coverage-media-system",
            MediaSource = MediaMetricContract.OutputSource,
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(mediaSystem);
        Check(mediaSystem, endpoints, overrides);

        var canvas = new C.CanvasDefinition();
        var canvasProperties = new EditorPropertyView(canvas, section: EditorPropertySection.All).GetProperties();
        var canvasKeys = new HashSet<string>(StringComparer.Ordinal);
        CollectLeafSemanticKeys(canvasProperties, canvasKeys);
        foreach (var required in new[]
                 {
                     "general.geometry.width", "general.geometry.height", "general.geometry.orientation", "appearance.background.color",
                     "general.background_image.asset.kind", "general.background_image.asset.source",
                     "general.foreground_image.asset.kind", "general.foreground_image.asset.source",
                     "image.background_image.asset.fit", "image.background_image.opacity",
                     "image.background_image.asset.loop", "image.background_image.color",
                     "image.foreground_image.asset.fit", "image.foreground_image.opacity",
                     "image.foreground_image.asset.loop", "image.foreground_image.color"
                 })
        {
            Assert(canvasKeys.Contains(required), $"Canvas canonical property view is missing semantic key '{required}'.");
        }

        static void ConfigureCoverageStateProfiles(C.StateVisualWidgetDefinition widget)
        {
            TextOverflowStateContract.EnsureProfiles(widget);
            var specs = StateVisualProfileContract.GetSpecs(widget.Type);
            Assert(specs.Count >= 3, $"{widget.Type} must provide at least three states for coverage.");

            var value = widget.Profiles[specs[0].Key];
            value.ContentType = C.StateContentType.Value;
            value.TextPresentation.OverflowMode = ValueOverflowContract.Bump;

            var file = widget.Profiles[specs[1].Key];
            file.ContentType = C.StateContentType.Image;
            file.Asset.SourceType = C.ImageAssetSourceType.File;
            file.Asset.Source = "coverage.gif";

            var icon = widget.Profiles[specs[2].Key];
            icon.ContentType = C.StateContentType.Image;
            icon.Asset.SourceType = C.ImageAssetSourceType.Icon;
            icon.Asset.Source = "activity";
        }
    }

    private static void PropertyExpansionStatePersistence()
    {
        var state = new EditorPropertyExpansionState();

        var general = state.Get(EditorPropertySection.General);
        Assert(general.TryGetValue("Identity", out var identityExpanded) && !identityExpanded &&
               general.TryGetValue("Source", out var sourceExpanded) && sourceExpanded &&
               general.TryGetValue("Background Image", out var backgroundImageSourceExpanded) && backgroundImageSourceExpanded &&
               general.TryGetValue("Foreground Image", out var foregroundImageSourceExpanded) && foregroundImageSourceExpanded &&
               general.TryGetValue("Geometry", out var geometryExpanded) && geometryExpanded,
            "General expansion state must initialize widget and Canvas source groups consistently.");

        var appearance = state.Get(EditorPropertySection.Appearance);
        Assert(!appearance.ContainsKey("Alignment") &&
               appearance.TryGetValue("Foreground", out var foregroundExpanded) && foregroundExpanded &&
               appearance.TryGetValue("Background", out var backgroundExpanded) && backgroundExpanded &&
               appearance.TryGetValue("Border", out var borderExpanded) && borderExpanded &&
               appearance.TryGetValue("Shadow", out var shadowExpanded) && !shadowExpanded &&
               appearance.TryGetValue("Threshold 1", out var threshold1Expanded) && !threshold1Expanded &&
               appearance.TryGetValue("Threshold 2", out var threshold2Expanded) && !threshold2Expanded &&
               appearance.TryGetValue("Threshold 3", out var threshold3Expanded) && !threshold3Expanded,
            "Appearance expansion state must start from the UI defaults with contextual groups initialized.");

        state.Remember(EditorPropertySection.Appearance, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Shadow"] = true,
            ["Threshold 1"] = true
        });
        state.Remember(EditorPropertySection.Appearance, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Background"] = false
        });

        appearance = state.Get(EditorPropertySection.Appearance);
        Assert(appearance["Shadow"] && !appearance["Background"] && appearance["Border"] &&
               appearance["Foreground"] && appearance["Threshold 1"] &&
               !appearance["Threshold 2"] && !appearance["Threshold 3"],
            "Remembering one visible Appearance group must preserve the last state of the other groups.");

        var data = state.Get(EditorPropertySection.Data);
        Assert(data["Value"] && data["Display"] && data["Range"] && data["Evaluation"],
            "Data expansion state must start with all structural data groups open.");

        var gauge = state.Get(EditorPropertySection.Gauge);
        Assert(gauge["Horseshoe"] && gauge["Needle"] && gauge["Bar"],
            "Gauge expansion state must start with all structural groups open.");
        state.Remember(EditorPropertySection.Gauge, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Needle"] = false
        });
        Assert(state.Get(EditorPropertySection.Gauge)["Horseshoe"] &&
               !state.Get(EditorPropertySection.Gauge)["Needle"],
            "Gauge group expansion state must use the centralized persistence model.");


        var states = state.Get(EditorPropertySection.States);
        Assert(states.Count == 0,
            "States expansion defaults must remain unspecified until a default is explicitly chosen.");
        state.Remember(EditorPropertySection.States, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["State: True"] = true,
            ["State: False"] = false
        });
        Assert(state.Get(EditorPropertySection.States)["State: True"] &&
               !state.Get(EditorPropertySection.States)["State: False"],
            "States group expansion state must use the centralized persistence model.");

        var image = state.Get(EditorPropertySection.Image);
        Assert(image.TryGetValue("Image", out var imageExpanded) && imageExpanded &&
               image.TryGetValue("Background Image", out var backgroundImageExpanded) && backgroundImageExpanded &&
               image.TryGetValue("Foreground Image", out var foregroundImageExpanded) && foregroundImageExpanded,
            "Image expansion state must start with widget and Canvas image groups open.");

        var text = state.Get(EditorPropertySection.Text);
        Assert(text["Font"] && text["Alignment"] && !text["Outline"] && !text["Overflow"],
            "Text expansion state must start from the UI defaults.");
        state.Remember(EditorPropertySection.Text, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Overflow"] = true
        });
        Assert(state.Get(EditorPropertySection.Text)["Overflow"],
            "Overflow expansion state must survive a PropertyGrid rebuild.");
        Assert(state.Get(EditorPropertySection.Appearance)["Shadow"],
            "Expansion state must be isolated by property tab.");
    }

    private static void EditorInputFocusOwnership()
    {
        var state = new EditorInputFocusState();
        state.Claim(EditorInputSurface.Canvas);
        var canvasSnapshot = state.Capture();
        Assert(state.CanRestore(canvasSnapshot),
            "The current canvas focus snapshot must be restorable after a programmatic PropertyGrid refresh.");

        state.Claim(EditorInputSurface.Properties);
        Assert(!state.CanRestore(canvasSnapshot),
            "A later explicit Properties focus claim must invalidate a pending canvas focus restore.");

        var propertySnapshot = state.Capture();
        Assert(state.CanRestore(propertySnapshot),
            "The current Properties focus snapshot must be restorable.");

        state.Claim(EditorInputSurface.Layers);
        Assert(!state.CanRestore(propertySnapshot) && state.Surface == EditorInputSurface.Layers,
            "A later Layers focus claim must own keyboard input and invalidate older focus restores.");

        var transitionSnapshot = state.CaptureForPreservation(EditorInputSurface.Properties);
        Assert(transitionSnapshot.Surface == EditorInputSurface.Layers && state.Surface == EditorInputSurface.Layers,
            "Lagging native Properties focus must not overwrite an explicit Layers pointer-activation claim.");

        var unclaimed = new EditorInputFocusState();
        var seededSnapshot = unclaimed.CaptureForPreservation(EditorInputSurface.Properties);
        Assert(seededSnapshot.Surface == EditorInputSurface.Properties && unclaimed.Surface == EditorInputSurface.Properties,
            "Actual focus may seed input ownership only when no editor surface has claimed ownership yet.");
    }

    private static void LayerTreePointerFocusAndInactiveSelectionContract()
    {
        using var tree = new LayerTreeView();
        Assert(tree.HideSelection,
            "LayerTreeView must hide the native inactive-selection overlay so logical TreeNode selection colors remain visible outside Layers focus.");
        Assert(tree.FullRowSelect,
            "LayerTreeView must preserve full-row native selection while Layers owns focus.");
        Assert(LayerTreeView.IsPointerActivationMessage(0x0201) &&
               LayerTreeView.IsPointerActivationMessage(0x0204) &&
               !LayerTreeView.IsPointerActivationMessage(0x000F),
            "LayerTreeView must recognize left/right pointer activation before native selection processing without confusing paint messages for input.");
    }

    private static void EditorGeometryWholePixelNormalization()
    {
        var bar = new C.BarWidgetDefinition
        {
            Id = "whole-pixel-canonical-bar",
            X = 10.25f,
            Y = -20.6f,
            Width = 100.25f,
            Height = 38.40625f
        };
        var properties = new EditorPropertyView(bar, section: EditorPropertySection.General).GetProperties();
        var x = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.X))
            ?? throw new InvalidOperationException("Bar X property is missing.");
        var y = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Y))
            ?? throw new InvalidOperationException("Bar Y property is missing.");
        var width = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Width))
            ?? throw new InvalidOperationException("Bar Width property is missing.");
        var height = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Height))
            ?? throw new InvalidOperationException("Bar Height property is missing.");

        Assert(Convert.ToSingle(x.GetValue(null)) == 10f &&
               Convert.ToSingle(y.GetValue(null)) == -21f &&
               Convert.ToSingle(width.GetValue(null)) == 100f &&
               Convert.ToSingle(height.GetValue(null)) == 38f,
            "Canonical Property Editor geometry did not preserve the whole-logical-pixel display contract.");

        x.SetValue(null, 11.5f);
        y.SetValue(null, -12.5f);
        width.SetValue(null, 121.6f);
        height.SetValue(null, 44.5f);
        Assert(bar.X == 12f && bar.Y == -13f && bar.Width == 122f && bar.Height == 45f,
            $"Canonical manual geometry edit was not normalized to whole logical pixels: X={bar.X}, Y={bar.Y}, {bar.Width} x {bar.Height}.");

        var value = new C.ValueWidgetDefinition
        {
            Id = "whole-pixel-canonical-value",
            SourceKind = C.ValueSourceKind.Text,
            Text = "TEST",
            Width = 100f,
            Height = 0f
        };
        var valueProperties = new EditorPropertyView(
            value,
            effectiveSizeAccessor: _ => (100.25f, 167.57812f),
            section: EditorPropertySection.General).GetProperties();
        var derivedHeight = GetNestedProperty(valueProperties, "Geometry", nameof(C.WidgetDefinition.Height))
            ?? throw new InvalidOperationException("Canonical Value Height property is missing.");
        Assert(derivedHeight.IsReadOnly && Convert.ToSingle(derivedHeight.GetValue(null)) == 168f,
            "Canonical derived text/value Height must remain read-only and display a whole logical pixel value.");

        Assert(WidgetResizeGeometry.NormalizeLogicalCoordinate(18.6f) == 19f &&
               WidgetResizeGeometry.NormalizeLogicalCoordinate(-18.6f) == -19f,
            "Logical coordinate normalization is inconsistent with the editor whole-pixel contract.");
        Assert(WidgetResizeGeometry.NormalizeResizedDimension(38.40625f) == 38f &&
               WidgetResizeGeometry.NormalizeResizedDimension(0.4f) == WidgetResizeGeometry.MinimumDimension,
            "Manual resize dimension normalization is inconsistent with the editor whole-pixel contract.");
    }

    private static void LayerTreeViewportAnchorPersistence()
    {
        var originalTop = new TreeNode("Original top");
        var state = LayerTreeViewportState.Capture(originalTop, _ => "w:viewport-anchor");
        Assert(state.TopNodeKey == "w:viewport-anchor",
            "Layer tree viewport capture did not retain the top visible node key.");

        var rebuiltTop = new TreeNode("Rebuilt top");
        var resolved = state.Resolve(key => key == "w:viewport-anchor" ? rebuiltTop : null);
        Assert(ReferenceEquals(resolved, rebuiltTop),
            "Layer tree viewport anchor did not resolve to the rebuilt node instance.");

        var missing = state.Resolve(_ => null);
        Assert(missing is null,
            "Layer tree viewport restore must tolerate an anchor that no longer exists.");
    }

    private static void GridSplitButtonBehavior()
    {
        using var button = new CheckableToolStripSplitButton();
        button.DropDownItems.Add(new ToolStripMenuItem("10 x 10") { Tag = 10 });
        button.DropDownItems.Add(new ToolStripMenuItem("25 x 25") { Tag = 25 });

        var mainButtonClicks = 0;
        button.ButtonClick += (_, _) => mainButtonClicks++;

        button.DropDownItems[1].PerformClick();
        Assert(mainButtonClicks == 0,
            "Grid spacing selection unexpectedly triggered the grid main-button action.");

        button.PerformButtonClick();
        Assert(mainButtonClicks == 1,
            "ToolStripSplitButton main area did not raise ButtonClick exactly once.");
        Assert(button.DropDownItems.Count == 2,
            "Grid split-button lost its independent spacing drop-down items.");

        button.Checked = true;
        Assert(button.Checked,
            "Grid split-button did not retain its persistent enabled-state presentation flag.");
        button.Checked = false;
        Assert(!button.Checked,
            "Grid split-button did not clear its enabled-state presentation flag.");
    }

}
