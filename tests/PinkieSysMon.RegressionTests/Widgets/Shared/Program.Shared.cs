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
    private static void WidgetSourcePropertyHierarchy()
    {
        static PropertyDescriptor? SourceProperty(PropertyDescriptorCollection properties, string propertyName) =>
            GetNestedProperty(properties, "Data Source", propertyName);

        static PropertyDescriptor RequireSourceType(PropertyDescriptorCollection properties, string expectedValue)
        {
            var group = properties["Data Source"]
                ?? throw new InvalidOperationException("Widget Source group is missing.");
            var groupValue = group.GetValue(null)
                ?? throw new InvalidOperationException("Widget Source group value is missing.");
            var type = TypeDescriptor.GetProperties(groupValue)
                .Cast<PropertyDescriptor>()
                .FirstOrDefault(property => property.DisplayName == "Type")
                ?? throw new InvalidOperationException("Widget Source/Type property is missing.");
            Assert(string.Equals(Convert.ToString(type.GetValue(groupValue)), expectedValue, StringComparison.Ordinal),
                $"Widget Source/Type expected '{expectedValue}'.");
            return type;
        }

        static void AssertNoSourceType(PropertyDescriptorCollection properties, string widgetKind)
        {
            var group = properties["Source"];
            if (group is null) return;
            var groupValue = group.GetValue(null)
                ?? throw new InvalidOperationException($"{widgetKind} Source group value is missing.");
            Assert(TypeDescriptor.GetProperties(groupValue)
                       .Cast<PropertyDescriptor>()
                       .All(property => property.DisplayName != "Type"),
                $"{widgetKind} must not expose a fake fixed Source/Type row in schema 19.");
        }

        var value = new C.ValueWidgetDefinition
        {
            Id = "source-value-metric",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = RuntimeMetricContract.Version,
            Width = 100f
        };
        var valueProperties = new EditorPropertyView(value, section: EditorPropertySection.General).GetProperties();
        var valueType = RequireSourceType(valueProperties, "Metric");
        Assert(!valueType.IsReadOnly,
            "Text / Value Source/Type must remain user-selectable.");
        var valueTypeOptions = valueType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(option => option is not null)
            .ToArray() ?? [];
        Assert(valueTypeOptions.SequenceEqual(new[] { "Metric", "Text" }),
            "Text / Value Source/Type must offer Metric and Text.");
        Assert(SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Metric)) is { IsReadOnly: false } &&
               SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Text)) is { IsReadOnly: true },
            "Metric Text / Value source must keep Text visible but disabled while Metric is selected.");

        valueType.SetValue(null, "Text");
        Assert(C.ValueSourceKind.IsText(value.SourceKind),
            "Text / Value Source/Type UI mapping must update the canonical SourceKind property.");
        valueProperties = new EditorPropertyView(value, section: EditorPropertySection.General).GetProperties();
        Assert(SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Metric)) is { IsReadOnly: true } &&
               SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Text)) is { IsReadOnly: false },
            "Text Text / Value source must disable Metric and enable Text.");

        C.WidgetDefinition[] metricOnly =
        [
            new C.BinaryWidgetDefinition { Id = "source-binary", Metric = FixtureOutputFps, Width = 100f, Height = 40f },
            new C.GaugeWidgetDefinition { Id = "source-gauge", Metric = FixtureOutputFps, Width = 100f, Height = 100f },
            new C.BarWidgetDefinition { Id = "source-bar", Metric = FixtureOutputFps, Width = 100f, Height = 20f }
        ];
        foreach (var widget in metricOnly)
        {
            var properties = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
            AssertNoSourceType(properties, widget.Type);
            Assert(SourceProperty(properties, "Metric") is { IsReadOnly: false },
                $"{widget.Type} must expose its real Metric property in Source.");
        }

        var image = new C.ImageWidgetDefinition
        {
            Id = "source-image",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File,
                Source = "example.png"
            }
        };
        // Issue #117 moved content source selection to General, leaving Image for presentation.
        var imageProperties = new EditorPropertyView(image, section: EditorPropertySection.General).GetProperties();
        var imageType = TypeDescriptor.GetProperties(imageProperties["Image Source"]!.GetValue(null)!)["SourceType"]!
            ?? throw new InvalidOperationException("Image content source type is missing.");
        var imageTypeOptions = imageType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(option => option is not null)
            .ToArray() ?? [];
        Assert(imageTypeOptions.SequenceEqual(new[] { "Image", "Icon" }),
            "Image Source/Type must offer Image and Icon.");
        var imageSource = GetNestedProperty(imageProperties, "Image Source", "Source");
        Assert(imageSource is not null && imageSource.DisplayName == "Image",
            "File-backed Image widget must label its source row as Image.");
        imageType.SetValue(null, "Icon");
        Assert(C.ImageAssetSourceType.IsIcon(image.Asset.SourceType) && image.Asset.Source is null,
            "Image Source/Type transition to Icon must update canonical Asset.SourceType and clear Source atomically.");
        imageProperties = new EditorPropertyView(image, section: EditorPropertySection.General).GetProperties();
        imageSource = GetNestedProperty(imageProperties, "Image Source", "Source");
        Assert(imageSource is not null && imageSource.DisplayName == "Icon",
            "Icon-backed Image widget must label its source row as Icon.");

        var power = new C.PowerWidgetDefinition
        {
            Id = "source-power",
            PowerSource = PowerMetricContract.UpsSource,
            Width = 100f,
            Height = 100f
        };
        var powerProperties = new EditorPropertyView(power, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(powerProperties, "Power");
        Assert(SourceProperty(powerProperties, nameof(C.PowerWidgetDefinition.PowerSource)) is { IsReadOnly: false, DisplayName: "Source" },
            "Power must expose canonical PowerSource as Source without a fake Type row.");

        var mediaSystem = new C.MediaSystemWidgetDefinition
        {
            Id = "source-media-system",
            MediaSource = MediaMetricContract.OutputSource,
            Width = 100f,
            Height = 100f
        };
        var mediaSystemProperties = new EditorPropertyView(mediaSystem, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(mediaSystemProperties, "Media System");
        Assert(SourceProperty(mediaSystemProperties, nameof(C.MediaSystemWidgetDefinition.MediaSource)) is { IsReadOnly: false, DisplayName: "Source" },
            "Media System must expose canonical MediaSource as Source without a fake Type row.");

        var mediaPlayer = new C.MediaPlayerWidgetDefinition
        {
            Id = "source-media-player",
            Width = 100f,
            Height = 100f
        };
        var mediaPlayerProperties = new EditorPropertyView(mediaPlayer, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(mediaPlayerProperties, "Media Player");
        var playbackMetric = SourceProperty(mediaPlayerProperties, "Metric");
        Assert(playbackMetric is not null && playbackMetric.DisplayName == "Metric" && playbackMetric.IsReadOnly &&
               string.Equals(Convert.ToString(playbackMetric.GetValue(null)), MediaMetricContract.PlaybackStatus, StringComparison.Ordinal),
            "Media Player must expose its fixed playback-status metric only as derived read-only information.");

        var canonicalProperties = new[] { valueProperties, imageProperties, powerProperties, mediaSystemProperties, mediaPlayerProperties }
            .SelectMany(collection => collection.Cast<PropertyDescriptor>())
            .SelectMany(FlattenProperties)
            .ToArray();
        Assert(canonicalProperties.All(property => property.Name != "ContentSourceType" && property.Name != "ContentMetric"),
            "Canonical Property Editor must not expose removed synthetic ContentSourceType/ContentMetric identities.");
    }

    private static void UniversalWidgetPropertyHierarchy()
    {
        var types = new[]
        {
            WidgetTypeContract.Value,
            WidgetTypeContract.Bar,
            WidgetTypeContract.Gauge,
            WidgetTypeContract.Image,
            WidgetTypeContract.Binary,
            WidgetTypeContract.Power,
            WidgetTypeContract.MediaSystem,
            WidgetTypeContract.MediaPlayer
        };

        foreach (var type in types)
        {
            var widget = CreateCanonicalPropertyTestWidget(
                type,
                $"hierarchy-{type}",
                type == WidgetTypeContract.Value ? 0f : 100f,
                type == WidgetTypeContract.Value ? 0f : 100f);

            var properties = CurrentPropertyView(
                widget,
                section: EditorPropertySection.General).GetProperties();
            foreach (var group in new[] { "Identity", "Data Source" })
            {
                var descriptor = properties[group]
                    ?? throw new InvalidOperationException(
                        $"{type} must expose the Widget-tab {group} property group.");
                Assert(descriptor is PropertyGroupPropertyDescriptor,
                    $"{type} Widget-tab {group} must be a first-level editor group.");
            }

            Assert(properties["Foreground"] is null &&
                   properties["Alignment"] is null &&
                   properties["Background"] is null &&
                   properties["Border"] is null &&
                   properties["Shadow"] is null &&
                   properties["Threshold 1"] is null &&
                   properties["Threshold 2"] is null &&
                   properties["Threshold 3"] is null &&
                   properties["Font"] is null &&
                   properties["Outline"] is null,
                $"{type} General tab must not contain Appearance groups.");

            Assert(properties[nameof(WidgetDefinition.Type)] is null &&
                   properties[nameof(WidgetDefinition.Id)] is null &&
                   properties[nameof(WidgetDefinition.Z)] is null &&
                   properties[nameof(WidgetDefinition.ValueSource)] is null &&
                   properties[nameof(WidgetDefinition.Metric)] is null &&
                   properties[nameof(WidgetDefinition.Text)] is null &&
                   properties[nameof(WidgetDefinition.SourceType)] is null &&
                   properties[nameof(WidgetDefinition.X)] is null &&
                   properties[nameof(WidgetDefinition.Width)] is null &&
                   properties[nameof(WidgetDefinition.BackgroundColor)] is null &&
                   properties[nameof(WidgetDefinition.BorderColor)] is null &&
                   properties[nameof(WidgetDefinition.ShadowEnabled)] is null,
                $"{type} must not duplicate hidden Widget/Appearance properties at the top level.");

            Assert(GetNestedProperty(properties, "Identity", "Name") is not null &&
                   GetNestedProperty(properties, "Identity", "Type") is { IsReadOnly: true } &&
                   GetNestedProperty(properties, "Identity", "Id") is { IsReadOnly: true },
                $"{type} Identity group must expose Name, read-only Type and ID.");
            var panel = CurrentPropertyView(widget, section: EditorPropertySection.Panel).GetProperties();
            Assert(GetNestedProperty(panel, "Geometry", "X") is not null &&
                   GetNestedProperty(panel, "Geometry", "Y") is not null &&
                   GetNestedProperty(panel, "Geometry", "Width") is not null &&
                   GetNestedProperty(panel, "Geometry", "Height") is not null &&
                   GetNestedProperty(panel, "Geometry", "Rotation") is not null &&
                   GetNestedProperty(panel, "Geometry", "Padding") is { IsReadOnly: false } &&
                   GetNestedProperty(panel, "Geometry", "Z") is { IsReadOnly: true } &&
                   GetNestedProperty(panel, "Geometry", "Position") is null &&
                   GetNestedProperty(panel, "Geometry", "Size") is null,
                $"{type} Geometry group must be a flat layout with Padding and read-only Z-order.");

        }

        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "hierarchy-value-height",
            Width = 120f,
            Height = 999f
        };
        var valueProperties = CurrentPropertyView(
            value,
            effectiveSizeAccessor: _ => (120f, 37f),
            section: EditorPropertySection.Panel).GetProperties();
        var valueHeight = GetNestedProperty(valueProperties, "Geometry", "Height")
            ?? throw new InvalidOperationException("Value Height property is missing.");
        Assert(valueHeight.IsReadOnly,
            "Text/Value Height must be disabled because vertical size is derived.");
        Assert(Math.Abs(Convert.ToSingle(valueHeight.GetValue(null)) - 37f) < 0.001f,
            "Text/Value disabled Height must report effective layout height, not dormant storage.");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "hierarchy-gauge-size",
            Width = 180f,
            Height = 180f
        };
        var gaugeProperties = CurrentPropertyView(
            gauge,
            section: EditorPropertySection.Panel).GetProperties();
        var gaugeWidth = GetNestedProperty(gaugeProperties, "Geometry", "Width")
            ?? throw new InvalidOperationException("Gauge Width property is missing.");
        var gaugeHeight = GetNestedProperty(gaugeProperties, "Geometry", "Height")
            ?? throw new InvalidOperationException("Gauge Height property is missing.");
        Assert(!gaugeWidth.IsReadOnly && gaugeHeight.IsReadOnly,
            "Gauge Width must be editable and Height must be disabled/derived.");
        Assert(Math.Abs(Convert.ToSingle(gaugeHeight.GetValue(null)) - 180f) < 0.001f,
            "Gauge derived Height must equal Width.");
    }

    private static void AppearancePropertyHierarchy()
    {
        foreach (var type in new[]
                 {
                     WidgetTypeContract.Value, WidgetTypeContract.Bar, WidgetTypeContract.Gauge,
                     WidgetTypeContract.Image, WidgetTypeContract.Binary, WidgetTypeContract.Power,
                     WidgetTypeContract.MediaSystem, WidgetTypeContract.MediaPlayer
                 })
        {
            var widget = CreateCanonicalPropertyTestWidget(type, $"panel-{type}", 100f, 100f);
            var panel = CurrentPropertyView(widget, section: EditorPropertySection.Panel).GetProperties();
            foreach (var group in new[] { "Geometry", "Background", "Foreground", "Border", "Shadow" })
                Assert(panel[group] is PropertyGroupPropertyDescriptor,
                    $"{type} must expose Panel/{group}.");
            Assert(GetNestedProperty(panel, "Geometry", nameof(C.WidgetDefinition.CornerRadius)) is not null &&
                   GetNestedProperty(panel, "Background", "Mode") is { DisplayName: "Mode" } &&
                   GetNestedProperty(panel, "Foreground", "Mode") is { DisplayName: "Mode" } &&
                   GetNestedProperty(panel, "Border", nameof(C.WidgetDefinition.BorderColor)) is { DisplayName: "Color" } &&
                   GetNestedProperty(panel, "Shadow", nameof(C.WidgetDefinition.ShadowOpacity)) is { DisplayName: "Opacity" },
                $"{type} Panel geometry and effects are incomplete.");

            var appearance = CurrentPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
            Assert(appearance["Border"] is null && appearance["Shadow"] is null &&
                   appearance["Container"] is null,
                "Panel-owned properties must not leak onto Appearance.");

            var stateWidget = type is WidgetTypeContract.Binary or WidgetTypeContract.Power or
                WidgetTypeContract.MediaSystem or WidgetTypeContract.MediaPlayer;
            Assert((GetNestedProperty(appearance, "Content", nameof(C.WidgetDefinition.Color)) is not null) == stateWidget,
                "Default Color must exist only where state content can inherit it.");

            var quantitative = type is WidgetTypeContract.Bar or WidgetTypeContract.Gauge;
            Assert((appearance["Threshold 1"] is not null) == quantitative,
                $"{type} threshold applicability changed unintentionally.");
        }

        var gauge = new C.GaugeWidgetDefinition { Id = "gauge-indicator", Width = 100, Height = 100 };
        var gaugeView = CurrentPropertyView(gauge, section: EditorPropertySection.Gauge).GetProperties();
        Assert(GetNestedProperty(gaugeView, "Horseshoe", nameof(C.WidgetDefinition.Color)) is { DisplayName: "Indicator Color" },
            "Gauge indicator styling belongs on Gauge.");

        var bar = new C.BarWidgetDefinition { Id = "bar-image", ContentMode = BarImageContract.ContentModeImage };
        var barView = CurrentPropertyView(bar, section: EditorPropertySection.Gauge).GetProperties();
        Assert(GetNestedProperty(barView, "Bar", nameof(C.WidgetDefinition.Color)) is { IsReadOnly: true } &&
               CurrentPropertyView(bar, section: EditorPropertySection.Appearance).GetProperties()["Threshold 1"] is null,
            "Image-mode Bar must disable fill color and hide thresholds.");
    }

    private static void DataPropertyHierarchy()
    {
        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "data-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = SystemMetricContract.Uptime,
            Width = 100f
        };
        var valueData = CurrentPropertyView(value, section: EditorPropertySection.Data).GetProperties();
        Assert(valueData.Count == 2 &&
               valueData["Value"] is PropertyGroupPropertyDescriptor &&
               valueData["Display"] is PropertyGroupPropertyDescriptor,
            "Metric Text/Value Data must be grouped as Value and Display.");
        Assert(GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } sourceUnit &&
               string.Equals(Convert.ToString(sourceUnit.GetValue(null)), "seconds", StringComparison.Ordinal) &&
               GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.Unit)) is not null &&
               GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.Format)) is not null,
            "Data/Value must expose Source Unit, Unit, and Format for a numeric metric.");
        Assert(GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Fallback)) is not null,
            "Data/Display must expose Prefix, Suffix, and Fallback for Text/Value.");


        foreach (var type in new[] { WidgetTypeContract.Bar, WidgetTypeContract.Gauge })
        {
            var widget = new WidgetDefinition
            {
                Type = type,
                Id = $"data-{type}",
                Metric = SystemMetricContract.Uptime,
                Width = 100f,
                Height = 100f,
                Min = 0,
                Max = 100
            };
            var data = CurrentPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
            Assert(data.Count == 2 &&
                   data["Value"] is PropertyGroupPropertyDescriptor &&
                   data["Range"] is PropertyGroupPropertyDescriptor,
                $"{type} Data must be grouped as Value and Range.");
            Assert(GetNestedProperty(data, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } &&
                   GetNestedProperty(data, "Value", nameof(WidgetDefinition.Unit)) is not null &&
                   GetNestedProperty(data, "Value", nameof(WidgetDefinition.Format)) is null,
                $"{type} Data/Value must expose provider Source Unit and converted Unit without text Format.");
            Assert(GetNestedProperty(data, "Range", nameof(WidgetDefinition.Min)) is { DisplayName: "Min" } &&
                   GetNestedProperty(data, "Range", nameof(WidgetDefinition.Max)) is { DisplayName: "Max" },
                $"{type} Data/Range must expose Min and Max.");
            Assert(data["Display"] is null && data["Evaluation"] is null,
                $"{type} must not expose text Display or Binary Evaluation groups.");

        }

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "data-binary-evaluation",
            Metric = SystemMetricContract.Uptime,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            Width = 100f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();
        var binaryData = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        Assert(binaryData["Evaluation"] is PropertyGroupPropertyDescriptor,
            "Binary Data must expose Evaluation as a first-level group.");
        var evaluationOwner = binaryData["Evaluation"]!.GetValue(null)
            ?? throw new InvalidOperationException("Binary Evaluation group has no value.");
        var evaluationProperties = TypeDescriptor.GetProperties(evaluationOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(evaluationProperties.Select(property => property.DisplayName)
                   .SequenceEqual(new[] { "Mode", "True If", "Setpoint" }, StringComparer.Ordinal),
            "Binary Evaluation properties must be ordered Mode, True If, Setpoint.");
        Assert(evaluationProperties[0].Name == nameof(WidgetDefinition.EvaluationMode) &&
               evaluationProperties[1].Name == nameof(WidgetDefinition.TrueIf) &&
               evaluationProperties[2].Name == nameof(WidgetDefinition.Setpoint),
            "Binary Evaluation property identity/order is incorrect.");

        var literalText = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "data-literal-numeric",
            ValueSource = TextValueContract.SourceText,
            Text = "123",
            Width = 100f
        };
        var literalData = CurrentPropertyView(literalText, section: EditorPropertySection.Data).GetProperties();
        Assert(literalData.Count == 2 &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Prefix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Suffix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Fallback)) is { IsReadOnly: true },
            "Numeric literal Text must expose grouped Value/Display data while keeping Fallback unavailable.");

        var image = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "data-image",
            Width = 100f,
            Height = 100f
        };
        var imageData = CurrentPropertyView(image, section: EditorPropertySection.Data).GetProperties();
        Assert(imageData.Count == 0,
            "Image must not expose an empty Data surface.");
    }

    private static void GaugePropertyHierarchy()
    {
        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "gauge-tab-hierarchy",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 100f
        };

        var gaugeProperties = CurrentPropertyView(
            gauge,
            section: EditorPropertySection.Gauge).GetProperties();

        Assert(gaugeProperties.Count == 2 &&
               gaugeProperties["Horseshoe"] is PropertyGroupPropertyDescriptor &&
               gaugeProperties["Needle"] is PropertyGroupPropertyDescriptor,
            "Gauge tab must expose exactly Horseshoe and Needle as first-level groups.");

        var horseshoeOwner = gaugeProperties["Horseshoe"]!.GetValue(null)
            ?? throw new InvalidOperationException("Gauge Horseshoe group has no value.");
        var horseshoe = TypeDescriptor.GetProperties(horseshoeOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(horseshoe.Select(property => property.DisplayName).SequenceEqual(
            new[]
            {
                "Indicator Color", "Enabled", "Reverse", "Start Angle", "End Angle", "Thickness",
                "Background Color", "Border Width", "Border Color", "Corner Radius", "Gap"
            },
            StringComparer.Ordinal),
            "Gauge Horseshoe properties are incomplete or out of order.");
        Assert(horseshoe.Select(property => property.Name).SequenceEqual(
            new[]
            {
                nameof(WidgetDefinition.Color),
                nameof(WidgetDefinition.TrackEnabled),
                nameof(WidgetDefinition.Reverse),
                nameof(WidgetDefinition.StartAngle),
                nameof(WidgetDefinition.EndAngle),
                nameof(WidgetDefinition.Thickness),
                nameof(WidgetDefinition.TrackBackgroundColor),
                nameof(WidgetDefinition.TrackBorderWidth),
                nameof(WidgetDefinition.TrackBorderColor),
                nameof(WidgetDefinition.TrackCornerRadius),
                nameof(WidgetDefinition.Gap)
            },
            StringComparer.Ordinal),
            "Gauge Horseshoe property identity/order is incorrect.");

        var needleOwner = gaugeProperties["Needle"]!.GetValue(null)
            ?? throw new InvalidOperationException("Gauge Needle group has no value.");
        var needle = TypeDescriptor.GetProperties(needleOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(needle.Select(property => property.DisplayName).SequenceEqual(
            new[]
            {
                "Enabled", "Thickness", "Color", "Start Offset", "End Offset",
                "Pointer Length", "Pointer Thickness", "Pointer Color"
            },
            StringComparer.Ordinal),
            "Gauge Needle properties are incomplete or out of order.");
        Assert(needle.Select(property => property.Name).SequenceEqual(
            new[]
            {
                nameof(WidgetDefinition.NeedleEnabled),
                nameof(WidgetDefinition.NeedleThickness),
                nameof(WidgetDefinition.NeedleColor),
                nameof(WidgetDefinition.NeedleStartOffset),
                nameof(WidgetDefinition.NeedleEndOffset),
                nameof(WidgetDefinition.NeedlePointerLength),
                nameof(WidgetDefinition.NeedlePointerThickness),
                nameof(WidgetDefinition.NeedlePointerColor)
            },
            StringComparer.Ordinal),
            "Gauge Needle property identity/order is incorrect.");


        var bar = new WidgetDefinition
        {
            Type = WidgetTypeContract.Bar,
            Id = "gauge-tab-bar",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 20f
        };
        var barGauge = CurrentPropertyView(
            bar,
            section: EditorPropertySection.Gauge).GetProperties();
        Assert(barGauge.Count == 1 &&
               barGauge["Bar"] is PropertyGroupPropertyDescriptor &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.BarContentMode)) is { DisplayName: "Mode" } &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.Reverse)) is { DisplayName: "Reverse" } &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.Gap)) is { DisplayName: "Gap" },
            "Bar must expose Gauge > Bar > Mode, Reverse, and Gap.");

    }

    private static void StatesPropertyHierarchy()
    {
        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "states-tab-binary",
            Metric = FixtureOutputFps,
            Fit = "contain",
            Loop = false,
            Width = 100f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueSourceType = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.SourceType);
        var trueSource = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Source);
        var trueColor = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var trueOpacity = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Opacity);
        var falseSourceType = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.SourceType);
        var falseSource = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Source);
        var falseColor = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);
        var falseOpacity = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Opacity);

        var states = CurrentPropertyView(
            binary,
            section: EditorPropertySection.States).GetProperties();

        Assert(states.Count == 2 &&
               states["State: True"] is PropertyGroupPropertyDescriptor &&
               states["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary States tab must expose exactly State: True and State: False as first-level groups.");

        Assert(GetNestedProperty(states, "State: True", trueSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(states, "State: True", trueSource) is { DisplayName: "Source" } &&
               GetNestedProperty(states, "State: False", falseSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(states, "State: False", falseSource) is { DisplayName: "Source" },
            "Icon-backed Binary states must expose Source Type and Source in their corresponding States groups.");

        binary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        states = CurrentPropertyView(
            binary,
            section: EditorPropertySection.States).GetProperties();

        var trueValueSource = GetNestedProperty(states, "State: True", trueSource);
        Assert(GetNestedProperty(states, "State: True", trueSourceType) is not null &&
               trueValueSource is { IsReadOnly: true } &&
               trueValueSource.Description == "Not applicable for selected Source Type." &&
               GetNestedProperty(states, "State: False", falseSourceType) is not null &&
               GetNestedProperty(states, "State: False", falseSource) is { IsReadOnly: false },
            "Value-backed Binary state must keep Source visible but disabled without affecting the sibling state.");

        binary.Profiles[BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceFile;
        binary.Profiles[BinarySignalContract.TrueKey].Source = "state.gif";

        var power = new WidgetDefinition
        {
            Type = WidgetTypeContract.Power,
            Id = "states-tab-power",
            Source = PowerMetricContract.UpsSource,
            Width = 40f,
            Height = 40f
        };
        power.EnsureStateVisualProfiles();
        var powerStates = CurrentPropertyView(
            power,
            section: EditorPropertySection.States).GetProperties();
        Assert(powerStates.Count == PowerMetricContract.States.Length &&
               powerStates["State: Online"] is PropertyGroupPropertyDescriptor &&
               powerStates["State: Unavailable"] is PropertyGroupPropertyDescriptor,
            "Power States tab must expose all Power state profiles.");

        var powerSpec = StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power)[0];
        var powerSourceType = StateProperty(powerSpec.Key, StateVisualProfileField.SourceType);
        var powerSource = StateProperty(powerSpec.Key, StateVisualProfileField.Source);
        Assert(GetNestedProperty(powerStates, powerSpec.Category, powerSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(powerStates, powerSpec.Category, powerSource) is { DisplayName: "Source" },
            "Icon-backed Power states must expose Source Type and Source in States.");

    }

    private static void ImagePropertyHierarchy()
    {
        var imageWidget = new C.ImageWidgetDefinition
        {
            Id = "image-tab-file",
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File,
                Source = "example.png",
                Fit = StateVisualProfileContract.FitContain,
                Loop = true
            },
            Opacity = 0.75f,
            Width = 100f,
            Height = 100f
        };

        var imageProperties = new EditorPropertyView(imageWidget, section: EditorPropertySection.Image).GetProperties();
        Assert(imageProperties["Image"] is PropertyGroupPropertyDescriptor,
            "Image widget must expose Image as a first-level Image-tab group.");
        Assert(GetNestedProperty(imageProperties, "Image", "Fit") is { DisplayName: "Fit" } &&
               GetNestedProperty(imageProperties, "Image", "Loop") is { DisplayName: "Loop" } &&
               GetNestedProperty(imageProperties, "Image", nameof(C.ImageWidgetDefinition.IconOpacity)) is null,
            "File-backed Image widget must expose Fit/Loop without icon-only Opacity.");

        imageWidget.Asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
        imageWidget.Asset.Source = "activity";
        var iconProperties = new EditorPropertyView(imageWidget, section: EditorPropertySection.Image).GetProperties();
        var iconLoop = GetNestedProperty(iconProperties, "Image", "Loop");
        Assert(GetNestedProperty(iconProperties, "Image", "Fit") is not null &&
               GetNestedProperty(iconProperties, "Image", nameof(C.ImageWidgetDefinition.IconOpacity)) is not null &&
               iconLoop is { IsReadOnly: true } &&
               iconLoop.Description == "Not applicable for selected Source.",
            "Icon-backed Image widget must keep Loop visible but disabled.");

        var bar = new C.BarWidgetDefinition
        {
            Id = "image-tab-bar",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 20f,
            Min = 0,
            Max = 100,
            ContentMode = BarImageContract.ContentModeImage,
            Image = new C.BarImagePresentationDefinition
            {
                Source = "bar.png",
                Fit = BarImageContract.ImageFitContain,
                ProgressMode = BarImageContract.ProgressModeReveal,
                Loop = true
            }
        };

        var barImageProperties = new EditorPropertyView(bar, section: EditorPropertySection.Image).GetProperties();
        Assert(barImageProperties["Image"] is PropertyGroupPropertyDescriptor,
            "Image-mode Bar must expose Image as a first-level Image-tab group.");
        Assert(GetNestedProperty(barImageProperties, "Image", "BarImageSource") is { DisplayName: "Source" } &&
               GetNestedProperty(barImageProperties, "Image", "BarImageFit") is { DisplayName: "Fit" } &&
               GetNestedProperty(barImageProperties, "Image", "BarProgressMode") is { DisplayName: "Progress Mode" } &&
               GetNestedProperty(barImageProperties, "Image", "Loop") is { DisplayName: "Loop" },
            "Image-mode Bar Image group must expose Source, Fit, Progress Mode, and Loop.");

        var barGauge = new EditorPropertyView(bar, section: EditorPropertySection.Gauge).GetProperties();
        Assert(GetNestedProperty(barGauge, "Bar", "BarContentMode") is { DisplayName: "Mode" },
            "Bar Gauge tab must retain Mode.");

        bar.ContentMode = BarImageContract.ContentModeFill;
        var fillImageProperties = new EditorPropertyView(bar, section: EditorPropertySection.Image).GetProperties();
        Assert(fillImageProperties.Count == 0,
            "Fill-mode Bar must have no Image section, allowing the Image tab to be omitted while dormant image settings remain in the model.");
        Assert(bar.Image.Source == "bar.png" && bar.Image.Fit == BarImageContract.ImageFitContain,
            "Switching Bar to Fill must preserve dormant canonical Bar image configuration.");

        var binary = new C.BinaryWidgetDefinition
        {
            Id = "image-tab-binary-stage3",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binary);
        var trueProfile = binary.Profiles[BinarySignalContract.TrueKey];
        trueProfile.ContentType = C.StateContentType.Image;
        trueProfile.Asset.SourceType = C.ImageAssetSourceType.File;
        trueProfile.Asset.Source = "binary-state.gif";
        trueProfile.Asset.Fit = StateVisualProfileContract.FitCover;
        trueProfile.Asset.Loop = false;
        trueProfile.Opacity = 0.75f;

        var falseProfile = binary.Profiles[BinarySignalContract.FalseKey];
        falseProfile.ContentType = C.StateContentType.Image;
        falseProfile.Asset.SourceType = C.ImageAssetSourceType.Icon;
        falseProfile.Asset.Source = "lucide:check";
        falseProfile.Asset.Fit = StateVisualProfileContract.FitContain;
        falseProfile.Asset.Loop = false;
        falseProfile.Color = "#FF00FF00";
        falseProfile.Opacity = 0.5f;

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueFit = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Fit);
        var trueLoop = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop);
        var trueOpacity = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Opacity);
        var trueColor = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var falseFit = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Fit);
        var falseLoop = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Loop);
        var falseOpacity = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Opacity);
        var falseColor = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);

        var binaryImage = new EditorPropertyView(binary, section: EditorPropertySection.Image).GetProperties();
        Assert(binaryImage.Count == 2 &&
               binaryImage["State: True"] is PropertyGroupPropertyDescriptor &&
               binaryImage["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary Image tab must expose state-specific first-level groups for active image/icon states.");
        var trueFileColor = GetNestedProperty(binaryImage, "State: True", trueColor);
        Assert(GetNestedProperty(binaryImage, "State: True", trueFit) is { DisplayName: "Fit" } &&
               GetNestedProperty(binaryImage, "State: True", trueLoop) is { DisplayName: "Loop", IsReadOnly: true } &&
               GetNestedProperty(binaryImage, "State: True", trueOpacity) is null &&
               trueFileColor is null,
            "File-backed Binary state Image group must have neither tint nor independent Opacity.");
        var falseIconLoop = GetNestedProperty(binaryImage, "State: False", falseLoop);
        Assert(GetNestedProperty(binaryImage, "State: False", falseFit) is { DisplayName: "Fit" } &&
               falseIconLoop is { DisplayName: "Loop", IsReadOnly: true } &&
               falseIconLoop.Description == "Not applicable for selected Source." &&
               GetNestedProperty(binaryImage, "State: False", falseColor) is { DisplayName: "Color" } &&
               GetNestedProperty(binaryImage, "State: False", falseOpacity) is { DisplayName: "Opacity" },
            "Icon-backed Binary state Image group must keep Loop visible but disabled.");

        var trueFitDescriptor = GetNestedProperty(binaryImage, "State: True", trueFit)
            ?? throw new InvalidOperationException("Binary True Fit property is missing.");
        var falseFitDescriptor = GetNestedProperty(binaryImage, "State: False", falseFit)
            ?? throw new InvalidOperationException("Binary False Fit property is missing.");
        trueFitDescriptor.SetValue(null, StateVisualProfileContract.FitStretch);
        falseFitDescriptor.SetValue(null, StateVisualProfileContract.FitCover);
        Assert(trueProfile.Asset.Fit == StateVisualProfileContract.FitStretch &&
               falseProfile.Asset.Fit == StateVisualProfileContract.FitCover,
            "Binary state Fit values must be independently editable.");

        var directory = Directory.CreateTempSubdirectory("binary-loop-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "binary-state.gif"), Convert.FromBase64String(
                "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));
            var resolved = new EditorPropertyView(binary, dashboardDirectory: directory.FullName, section: EditorPropertySection.Image).GetProperties();
            var loop = GetNestedProperty(resolved, "State: True", trueLoop)!;
            Assert(!loop.IsReadOnly, "A decoded animated file must enable Loop.");
            loop.SetValue(null, true);
            Assert(trueProfile.Asset.Loop && !falseProfile.Asset.Loop,
                "Editing Binary True Loop must not overwrite the sibling state Loop value.");
        }
        finally { directory.Delete(true); }

        trueProfile.ContentType = C.StateContentType.Value;
        var mixedBinaryImage = new EditorPropertyView(binary, section: EditorPropertySection.Image).GetProperties();
        Assert(mixedBinaryImage.Count == 1 &&
               mixedBinaryImage["State: True"] is null &&
               mixedBinaryImage["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary Image tab must omit value-backed states while preserving image/icon-backed sibling states.");
    }

    private static void RemovedLegacyWidgetTypes()
    {
        Assert(!WidgetTypeContract.KnownTypes.Contains("grid", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.KnownTypes.Contains("rect", StringComparer.OrdinalIgnoreCase),
            "Removed Grid/Rect widget types must not remain in the runtime type contract.");
        Assert(!WidgetTypeContract.EditorAddableTypes.Contains("grid", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.EditorAddableTypes.Contains("rect", StringComparer.OrdinalIgnoreCase),
            "Removed Grid/Rect widget types must not remain addable in the editor.");

        var widgetProperties = TypeDescriptor.GetProperties(typeof(C.WidgetDefinition));
        Assert(widgetProperties["Spacing"] is null &&
               widgetProperties["FillColor"] is null &&
               widgetProperties["History"] is null &&
               widgetProperties["SampleIntervalMs"] is null &&
               widgetProperties["StrokeWidth"] is null,
            "Removed legacy widget-only properties must not remain in the canonical model.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-removed-widget-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "removed-widgets.json");
            foreach (var type in new[] { "grid", "rect" })
            {
                var rejected = false;
                try
                {
                    _ = DashboardJson.ParseCurrent(
                        $$"""
                        {
                          "schemaVersion": {{DashboardSchemaMigration.CurrentSchemaVersion}},
                          "canvas": {
                            "width": 1920,
                            "height": 480,
                            "orientation": 0,
                            "backgroundColor": "#000000",
                            "backgroundImage": { "asset": { "sourceType": "file", "source": null, "fit": "stretch", "loop": true }, "opacity": 1, "color": "#FFFFFFFF" },
                            "foregroundImage": { "asset": { "sourceType": "file", "source": null, "fit": "stretch", "loop": true }, "opacity": 1, "color": "#FFFFFFFF" }
                          },
                          "widgets": [
                            { "type": "{{type}}", "id": "removed-{{type}}", "width": 20, "height": 20 }
                          ]
                        }
                        """,
                        dashboardPath);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                Assert(rejected, $"Removed widget type '{type}' must be rejected by the canonical current dashboard reader.");
            }

            var legacy = JsonNode.Parse(
                """
                {
                  "schemaVersion": 11,
                  "canvas": { "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "legacy-inert-fields",
                      "width": 100,
                      "height": 20,
                      "valueSource": "text",
                      "text": "OK",
                      "spacing": 48,
                      "fillColor": "#FFFFFFFF",
                      "history": 120,
                      "sampleIntervalMs": 500,
                      "strokeWidth": 2
                    }
                  ]
                }
                """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 11 legacy-property migration document.");

            Assert(DashboardSchemaMigration.UpgradeToCurrent(legacy),
                "Schema 11 dashboard must migrate to the canonical current schema after legacy primitive removal.");
            Assert(legacy["schemaVersion"]?.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
                "Legacy primitive removal chain must terminate at schema 21.");
            var migratedWidget = ((legacy["widgets"] as JsonArray)?[0] as JsonObject)
                ?? throw new InvalidOperationException("Migrated legacy-property widget is missing.");
            Assert(!migratedWidget.ContainsKey("spacing") &&
                   !migratedWidget.ContainsKey("fillColor") &&
                   !migratedWidget.ContainsKey("history") &&
                   !migratedWidget.ContainsKey("sampleIntervalMs") &&
                   !migratedWidget.ContainsKey("strokeWidth"),
                "Schema 11 migration must remove inert properties owned only by removed legacy widgets.");
            _ = DashboardJson.ParseCurrent(legacy.ToJsonString(), dashboardPath);

            foreach (var type in new[] { "grid", "rect", "graph" })
            {
                var obsolete = JsonNode.Parse(
                    $$"""
                    {
                      "schemaVersion": 11,
                      "canvas": { "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        { "type": "{{type}}", "id": "legacy-{{type}}", "width": 20, "height": 20 }
                      ]
                    }
                    """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 11 removed-widget migration document.");

                var migrationRejected = false;
                try
                {
                    _ = DashboardSchemaMigration.UpgradeToCurrent(obsolete);
                }
                catch (InvalidDataException)
                {
                    migrationRejected = true;
                }

                Assert(migrationRejected,
                    $"Schema 11 migration must reject removed widget type '{type}' instead of carrying it into schema 19.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextPropertyHierarchy()
    {
        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "text-tab-value",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Width = 100f,
            OverflowMode = ValueOverflowContract.Clip
        };
        var valueText = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(valueText["Font"] is PropertyGroupPropertyDescriptor &&
               valueText["Alignment"] is PropertyGroupPropertyDescriptor &&
               valueText["Outline"] is PropertyGroupPropertyDescriptor &&
               valueText["Overflow"] is PropertyGroupPropertyDescriptor,
            "Text/Value must expose Font, Alignment, Outline, and Overflow as first-level Text groups.");
        Assert(GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontFamily)) is { DisplayName: "Family" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontSize)) is { DisplayName: "Size" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontWeight)) is { DisplayName: "Weight" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.Italic)) is { DisplayName: "Italic" },
            "Text/Value Font group is incomplete.");
        Assert(GetNestedProperty(valueText, "Alignment", nameof(WidgetDefinition.Align)) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(valueText, "Alignment", nameof(WidgetDefinition.VerticalAlign)) is { DisplayName: "Vertical" },
            "Text/Value Alignment group is incomplete.");
        Assert(GetNestedProperty(valueText, "Outline", nameof(WidgetDefinition.OutlineColor)) is { DisplayName: "Color" } &&
               GetNestedProperty(valueText, "Outline", nameof(WidgetDefinition.OutlineWidth)) is { DisplayName: "Width" },
            "Text/Value Outline group is incomplete.");
        var valueScrollSpeed = GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.ScrollSpeed));
        var valueBumpPause = GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.BumpPauseMs));
        Assert(GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.OverflowMode)) is { DisplayName: "Mode" } &&
               valueScrollSpeed is { DisplayName: "Scroll Speed", IsReadOnly: true } &&
               valueBumpPause is { DisplayName: "Bump Pause", IsReadOnly: true } &&
               valueScrollSpeed.Description == "Not applicable for selected Overflow Mode." &&
               valueBumpPause.Description == "Not applicable for selected Overflow Mode.",
            "Text/Value Overflow group must keep Scroll Speed and Bump Pause visible but disabled for Clip mode.");

        var iconBinary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "text-tab-binary-icon",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 40f
        };
        iconBinary.EnsureStateVisualProfiles();
        var iconBinaryText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(iconBinaryText.Count == 0,
            "Icon-only Binary must have no Text section, allowing the Text tab to be omitted.");

        iconBinary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(iconBinary, 100f, 32f);
        iconBinary.Profiles[BinarySignalContract.TrueKey].Text!.OverflowMode = ValueOverflowContract.Clip;
        var valueBinaryText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(valueBinaryText["State: True"] is PropertyGroupPropertyDescriptor &&
               valueBinaryText["State: False"] is null,
            "Binary with one value-backed state must expose only that state as a first-level Text group.");
        var trueFontName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.FontFamily}";
        var trueAlignName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.Align}";
        var trueOutlineName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OutlineColor}";
        var trueOverflowName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OverflowMode}";
        var trueScrollSpeedName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.ScrollSpeed}";
        var trueBumpPauseName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.BumpPauseMs}";
        var trueScrollSpeed = GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueScrollSpeedName);
        var trueBumpPause = GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueBumpPauseName);
        Assert(GetNestedProperty(valueBinaryText, "State: True", "Font", trueFontName) is { DisplayName: "Family" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Alignment", trueAlignName) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Outline", trueOutlineName) is { DisplayName: "Color" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueOverflowName) is { DisplayName: "Mode" } &&
               trueScrollSpeed is { DisplayName: "Scroll Speed", IsReadOnly: true } &&
               trueBumpPause is { DisplayName: "Bump Pause", IsReadOnly: true } &&
               trueScrollSpeed.Description == "Not applicable for selected Overflow Mode." &&
               trueBumpPause.Description == "Not applicable for selected Overflow Mode.",
            "Binary state Text group must keep contextual Overflow rows visible but disabled for Clip mode.");

        iconBinary.Profiles[BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(iconBinary, 100f, 32f);
        var bothValueText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(bothValueText["State: True"] is not null && bothValueText["State: False"] is not null,
            "Value-only Binary must expose independent first-level Text groups for True and False.");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "text-tab-gauge",
            Metric = FixtureOutputFps,
            Width = 100f,
            Height = 100f
        };
        var gaugeText = CurrentPropertyView(gauge, section: EditorPropertySection.Text).GetProperties();
        Assert(gaugeText.Count == 0,
            "Gauge must not expose the contextual Text section without text presentation.");
    }

}
