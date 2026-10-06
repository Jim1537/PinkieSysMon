using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

internal interface IPropertySemanticKeyDescriptor
{
    string SemanticKey { get; }
}

internal sealed class CanonicalPropertyDescriptor : PropertyDescriptor, IMultiEditContextDescriptor, IPropertyEditorSemanticDescriptor, IPropertySemanticKeyDescriptor
{
    private readonly object _target;
    private readonly Type _propertyType;
    private readonly Func<object?> _getter;
    private readonly Action<object?>? _setter;
    private readonly TypeConverter? _converter;
    private readonly UITypeEditor? _editor;
    private readonly Func<string?>? _disabledReason;
    private readonly Func<string>? _displayName;
    private readonly string _category;
    private readonly string _description;
    private readonly Action? _reset;
    private readonly Func<bool>? _canReset;

    public CanonicalPropertyDescriptor(
        string name,
        string semanticKey,
        string displayName,
        object target,
        Type propertyType,
        Func<object?> getter,
        Action<object?>? setter,
        string category = "",
        TypeConverter? converter = null,
        UITypeEditor? editor = null,
        Func<string?>? disabledReason = null,
        string description = "",
        string multiEditContextKey = "",
        string multiEditContextController = "",
        Action? reset = null,
        Func<bool>? canReset = null,
        Func<string>? displayNameAccessor = null)
        : base(name, null)
    {
        if (string.IsNullOrWhiteSpace(semanticKey))
            throw new ArgumentException("Semantic key is required.", nameof(semanticKey));
        _target = target;
        _propertyType = propertyType;
        _getter = getter;
        _setter = setter;
        _category = category;
        _converter = converter;
        _editor = editor;
        _disabledReason = disabledReason;
        _description = description;
        _reset = reset;
        _canReset = canReset;
        _displayName = displayNameAccessor;
        StaticDisplayName = displayName;
        SemanticKey = semanticKey;
        MultiEditContextKey = multiEditContextKey;
        MultiEditContextController = multiEditContextController;
    }

    private string StaticDisplayName { get; }
    public string SemanticKey { get; }
    public override string DisplayName => _displayName?.Invoke() ?? StaticDisplayName;
    public override string Category => _category;
    public override string Description => _disabledReason?.Invoke() ?? _description;
    public override Type ComponentType => _target.GetType();
    public override Type PropertyType => _propertyType;
    public override bool IsReadOnly => _setter is null || _disabledReason?.Invoke() is not null;
    public override TypeConverter Converter => _converter ?? base.Converter;
    public string MultiEditContextKey { get; }
    public string MultiEditContextController { get; }

    public override bool CanResetValue(object component) => !IsReadOnly && (_canReset?.Invoke() ?? false);
    public override object? GetValue(object? component) => _getter();
    public override void ResetValue(object component)
    {
        if (IsReadOnly || _reset is null)
            return;
        _reset();
        OnValueChanged(component, EventArgs.Empty);
    }

    public override void SetValue(object? component, object? value)
    {
        if (IsReadOnly || _setter is null)
            return;
        _setter(value);
        OnValueChanged(component, EventArgs.Empty);
    }

    public override bool ShouldSerializeValue(object component) => true;
    public override object? GetEditor(Type editorBaseType) =>
        editorBaseType == typeof(UITypeEditor) && !IsReadOnly && _editor is not null
            ? _editor
            : base.GetEditor(editorBaseType);
    public object? GetSemanticEditor(Type editorBaseType) =>
        editorBaseType == typeof(UITypeEditor) ? _editor : base.GetEditor(editorBaseType);
}

internal sealed class DynamicMetricOptionConverter : StringConverter
{
    private readonly Func<IReadOnlyList<MetricPresentationOption>> _optionsAccessor;

    public DynamicMetricOptionConverter(Func<IReadOnlyList<MetricPresentationOption>> optionsAccessor)
    {
        _optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        new(_optionsAccessor().Select(option => option.Label).ToArray());
}

internal static class CanonicalPropertyViewBuilder
{
    private static readonly TypeConverter TextConverter = new StringConverter();
    private static readonly TypeConverter SourceKindConverter = new FixedStringConverter(["Metric", "Text"]);
    private static readonly TypeConverter ImageKindConverter = new FixedStringConverter(["Image", "Icon"]);
    private static readonly TypeConverter FitConverter = new FixedStringConverter(StateVisualProfileContract.SupportedFits);
    private static readonly TypeConverter AlignConverter = new FixedStringConverter(["left", "center", "right"]);
    private static readonly TypeConverter VerticalAlignConverter = new FixedStringConverter(["baseline", "top", "middle", "bottom"]);
    private static readonly TypeConverter OverflowConverter = new FixedStringConverter(ValueOverflowContract.SupportedModes);
    private static readonly TypeConverter BarContentConverter = new FixedStringConverter(BarImageContract.SupportedContentModes);
    private static readonly TypeConverter BarFitConverter = new FixedStringConverter(BarImageContract.SupportedImageFits);
    private static readonly TypeConverter BarProgressConverter = new FixedStringConverter(BarImageContract.SupportedProgressModes);
    private static readonly TypeConverter PowerSourceConverter = new FixedStringConverter(PowerMetricContract.SupportedSources);
    private static readonly TypeConverter MediaSourceConverter = new FixedStringConverter([MediaMetricContract.OutputSource, MediaMetricContract.InputSource]);
    private static readonly TypeConverter BinaryModeConverter = new FixedStringConverter(BinarySignalContract.SupportedEvaluationModes);
    private static readonly TypeConverter BinaryTrueIfConverter = new FixedStringConverter(BinarySignalContract.SupportedTrueIfOperators);
    private static readonly TypeConverter ThresholdModeConverter = new FixedStringConverter(C.ThresholdModeContract.SupportedModes);
    private static readonly UITypeEditor MetricEditor = new MetricNameEditor();

    public static PropertyDescriptorCollection Build(
        object target,
        EditorPropertySection section,
        string? applicationRoot,
        string? dashboardDirectory,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> mediaEndpoints,
        IDictionary<string, string>? endpointTypeOverrides,
        Func<C.WidgetDefinition, (float Width, float Height)?>? effectiveSizeAccessor)
    {
        var descriptors = target switch
        {
            C.WidgetDefinition widget => BuildWidget(widget, section, applicationRoot, dashboardDirectory, mediaEndpoints, endpointTypeOverrides, effectiveSizeAccessor),
            C.CanvasDefinition canvas => BuildCanvas(canvas, section, applicationRoot, dashboardDirectory),
            _ => []
        };
        return new PropertyDescriptorCollection(descriptors.ToArray(), readOnly: true);
    }

    private static List<PropertyDescriptor> BuildWidget(
        C.WidgetDefinition widget,
        EditorPropertySection section,
        string? applicationRoot,
        string? dashboardDirectory,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> mediaEndpoints,
        IDictionary<string, string>? endpointTypeOverrides,
        Func<C.WidgetDefinition, (float Width, float Height)?>? effectiveSizeAccessor)
    {
        return section switch
        {
            EditorPropertySection.General => BuildGeneral(widget, applicationRoot, dashboardDirectory, effectiveSizeAccessor),
            EditorPropertySection.Appearance => BuildAppearance(widget, applicationRoot),
            EditorPropertySection.Data => BuildData(widget, mediaEndpoints, endpointTypeOverrides),
            EditorPropertySection.Gauge => BuildGauge(widget, dashboardDirectory),
            EditorPropertySection.States => BuildStates(widget, applicationRoot, dashboardDirectory),
            EditorPropertySection.Image => BuildImage(widget, applicationRoot, dashboardDirectory),
            EditorPropertySection.Text => BuildText(widget),
            EditorPropertySection.All => BuildAll(widget, applicationRoot, dashboardDirectory, mediaEndpoints, endpointTypeOverrides, effectiveSizeAccessor),
            _ => []
        };
    }

    private static List<PropertyDescriptor> BuildAll(
        C.WidgetDefinition widget,
        string? applicationRoot,
        string? dashboardDirectory,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> mediaEndpoints,
        IDictionary<string, string>? endpointTypeOverrides,
        Func<C.WidgetDefinition, (float Width, float Height)?>? effectiveSizeAccessor)
    {
        var result = new List<PropertyDescriptor>();
        foreach (var section in new[]
                 {
                     EditorPropertySection.General, EditorPropertySection.Appearance, EditorPropertySection.Data,
                     EditorPropertySection.Gauge, EditorPropertySection.States, EditorPropertySection.Image,
                     EditorPropertySection.Text
                 })
        {
            result.AddRange(BuildWidget(widget, section, applicationRoot, dashboardDirectory, mediaEndpoints, endpointTypeOverrides, effectiveSizeAccessor));
        }
        return result;
    }

    private static List<PropertyDescriptor> BuildGeneral(
        C.WidgetDefinition widget,
        string? applicationRoot,
        string? dashboardDirectory,
        Func<C.WidgetDefinition, (float Width, float Height)?>? effectiveSizeAccessor)
    {
        var result = new List<PropertyDescriptor>();
        result.Add(Group("Identity", [
            String(widget, nameof(C.WidgetDefinition.Name), "general.identity.name", "Name", () => widget.Name ?? string.Empty, value => widget.Name = EmptyToNull(value))
        ]));

        var source = new List<PropertyDescriptor>();
        switch (widget)
        {
            case C.ValueWidgetDefinition value:
                source.Add(new CanonicalPropertyDescriptor(
                    nameof(C.ValueWidgetDefinition.SourceKind), "general.source.value.kind", "Type", value, typeof(string),
                    () => C.ValueSourceKind.IsText(value.SourceKind) ? "Text" : "Metric",
                    v => value.SourceKind = string.Equals(Convert.ToString(v), "Text", StringComparison.OrdinalIgnoreCase)
                        ? C.ValueSourceKind.Text : C.ValueSourceKind.Metric,
                    converter: SourceKindConverter));
                source.Add(Metric(value, nameof(C.ValueWidgetDefinition.Metric), "general.source.metric", "Metric", () => value.Metric,
                    v => value.Metric = EmptyToNull(v),
                    () => C.ValueSourceKind.IsMetric(value.SourceKind) ? null : NotApplicable("Type")));
                source.Add(String(value, nameof(C.ValueWidgetDefinition.Text), "general.source.value.text", "Text", () => value.Text,
                    v => value.Text = Convert.ToString(v) ?? string.Empty,
                    disabled: () => C.ValueSourceKind.IsText(value.SourceKind) ? null : NotApplicable("Type")));
                break;
            case C.ImageWidgetDefinition image:
            {
                var editor = !string.IsNullOrWhiteSpace(applicationRoot) && !string.IsNullOrWhiteSpace(dashboardDirectory)
                    ? new ImageAssetSourceEditor(image.Asset, applicationRoot!, dashboardDirectory!) : null;
                source.AddRange(ImageAssetSourceProperties(
                    image.Asset,
                    "general.source.image.kind",
                    "general.source.image.source",
                    editor,
                    () => C.ImageAssetSourceType.IsIcon(image.Asset.SourceType) ? "Icon" : "Image"));
                break;
            }
            case C.BinaryWidgetDefinition binary:
                source.Add(Metric(binary, nameof(C.BinaryWidgetDefinition.Metric), "general.source.metric", "Metric", () => binary.Metric, v => binary.Metric = EmptyToNull(v)));
                break;
            case C.GaugeWidgetDefinition gauge:
                source.Add(Metric(gauge, nameof(C.GaugeWidgetDefinition.Metric), "general.source.metric", "Metric", () => gauge.Metric, v => gauge.Metric = EmptyToNull(v)));
                break;
            case C.BarWidgetDefinition bar:
                source.Add(Metric(bar, nameof(C.BarWidgetDefinition.Metric), "general.source.metric", "Metric", () => bar.Metric, v => bar.Metric = EmptyToNull(v)));
                break;
            case C.PowerWidgetDefinition power:
                source.Add(String(power, nameof(C.PowerWidgetDefinition.PowerSource), "general.source.power", "Source", () => power.PowerSource,
                    v => power.PowerSource = Convert.ToString(v) ?? PowerMetricContract.UpsSource, converter: PowerSourceConverter));
                break;
            case C.MediaSystemWidgetDefinition media:
                source.Add(String(media, nameof(C.MediaSystemWidgetDefinition.MediaSource), "general.source.media", "Source", () => media.MediaSource,
                    v => media.MediaSource = Convert.ToString(v) ?? MediaMetricContract.OutputSource, converter: MediaSourceConverter));
                break;
            case C.MediaPlayerWidgetDefinition player:
                source.Add(new CanonicalPropertyDescriptor(
                    "PlaybackMetric", "general.source.media_player.metric", "Metric", player, typeof(string),
                    () => MediaMetricContract.PlaybackStatus, null));
                break;
        }
        if (source.Count > 0)
            result.Add(Group("Source", source));

        var geometry = new List<PropertyDescriptor>
        {
            WholePixelCoordinate(widget, nameof(C.WidgetDefinition.X), "general.geometry.x", "X", () => widget.X, v => widget.X = v),
            WholePixelCoordinate(widget, nameof(C.WidgetDefinition.Y), "general.geometry.y", "Y", () => widget.Y, v => widget.Y = v),
            WholePixelDimension(widget, nameof(C.WidgetDefinition.Width), "general.geometry.width", "Width", () => widget.Width, v => widget.Width = v)
        };
        geometry.Add(new CanonicalPropertyDescriptor(
            nameof(C.WidgetDefinition.Height), "general.geometry.height", "Height", widget, typeof(float),
            () => WidgetResizeGeometry.NormalizeLogicalDimension(
                WidgetSizeContract.IsHeightDerived(widget)
                    ? effectiveSizeAccessor?.Invoke(widget)?.Height ?? widget.Height
                    : widget.Height),
            WidgetSizeContract.IsHeightDerived(widget)
                ? null
                : v => widget.Height = Math.Max(0f, WidgetResizeGeometry.NormalizeLogicalDimension(Convert.ToSingle(v, CultureInfo.InvariantCulture))),
            description: WidgetSizeContract.IsHeightDerived(widget) ? "Derived from the active widget layout." : string.Empty));
        geometry.Add(Double(widget, nameof(C.WidgetDefinition.Rotation), "general.geometry.rotation", "Rotation", () => widget.Rotation, v => widget.Rotation = v));
        result.Add(Group("Geometry", geometry));
        return result;
    }

    private static List<PropertyDescriptor> BuildAppearance(C.WidgetDefinition widget, string? applicationRoot)
    {
        var result = new List<PropertyDescriptor>();
        var foregroundApplicable = widget is C.ValueWidgetDefinition or C.GaugeWidgetDefinition or C.StateVisualWidgetDefinition ||
                                   widget is C.BarWidgetDefinition bar && !BarImageContract.IsImageMode(bar.ContentMode) ||
                                   widget is C.ImageWidgetDefinition image && C.ImageAssetSourceType.IsIcon(image.Asset.SourceType);
        if (foregroundApplicable)
        {
            Func<string?>? disabled = null;
            if (widget is C.ImageWidgetDefinition iconImage)
            {
                disabled = () =>
                {
                    if (!C.ImageAssetSourceType.IsIcon(iconImage.Asset.SourceType))
                        return NotApplicable("Type");
                    if (string.IsNullOrWhiteSpace(iconImage.Asset.Source))
                        return NotApplicable("Source");
                    return !IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, iconImage.Asset.Source, out var caps) || !caps.Tintable
                        ? NotApplicable("Source") : null;
                };
            }
            result.Add(Group("Foreground", [Color(widget, nameof(C.WidgetDefinition.Color), "appearance.foreground.color", "Color", () => widget.Color, v => widget.Color = v, disabled)]));
        }

        result.Add(Group("Background", [Color(widget, nameof(C.WidgetDefinition.BackgroundColor), "appearance.background.color", "Color", () => widget.BackgroundColor, v => widget.BackgroundColor = v)]));
        result.Add(Group("Border", [
            Color(widget, nameof(C.WidgetDefinition.BorderColor), "appearance.border.color", "Color", () => widget.BorderColor, v => widget.BorderColor = v),
            Float(widget, nameof(C.WidgetDefinition.BorderWidth), "appearance.border.width", "Width", () => widget.BorderWidth, v => widget.BorderWidth = Math.Max(0f, v)),
            Float(widget, nameof(C.WidgetDefinition.CornerRadius), "appearance.border.corner_radius", "Corner Radius", () => widget.CornerRadius, v => widget.CornerRadius = Math.Max(0f, v))
        ]));
        result.Add(Group("Shadow", [
            Bool(widget, nameof(C.WidgetDefinition.ShadowEnabled), "appearance.shadow.enabled", "Enabled", () => widget.ShadowEnabled, v => widget.ShadowEnabled = v),
            Float(widget, nameof(C.WidgetDefinition.ShadowOffsetX), "appearance.shadow.offset_x", "Offset X", () => widget.ShadowOffsetX, v => widget.ShadowOffsetX = v, () => widget.ShadowEnabled ? null : NotApplicable("Enabled")),
            Float(widget, nameof(C.WidgetDefinition.ShadowOffsetY), "appearance.shadow.offset_y", "Offset Y", () => widget.ShadowOffsetY, v => widget.ShadowOffsetY = v, () => widget.ShadowEnabled ? null : NotApplicable("Enabled")),
            Float(widget, nameof(C.WidgetDefinition.ShadowBlur), "appearance.shadow.blur", "Blur", () => widget.ShadowBlur, v => widget.ShadowBlur = Math.Max(0f, v), () => widget.ShadowEnabled ? null : NotApplicable("Enabled")),
            Float(widget, nameof(C.WidgetDefinition.ShadowOpacity), "appearance.shadow.opacity", "Opacity", () => widget.ShadowOpacity, v => widget.ShadowOpacity = Math.Clamp(v, 0f, 1f), () => widget.ShadowEnabled ? null : NotApplicable("Enabled")),
            Color(widget, nameof(C.WidgetDefinition.ShadowColor), "appearance.shadow.color", "Color", () => widget.ShadowColor, v => widget.ShadowColor = v, () => widget.ShadowEnabled ? null : NotApplicable("Enabled"))
        ]));

        if (widget is C.QuantitativeWidgetDefinition quantitative &&
            (widget is not C.BarWidgetDefinition thresholdBar || !BarImageContract.IsImageMode(thresholdBar.ContentMode)))
        {
            result.Add(String(quantitative, "ThresholdMode", "appearance.thresholds.mode", "Threshold Mode", () => quantitative.Thresholds.Mode,
                v => quantitative.Thresholds.Mode = Convert.ToString(v) ?? C.ThresholdModeContract.SegmentSolid,
                converter: ThresholdModeConverter));
            for (var i = 0; i < quantitative.Thresholds.Items.Count; i++)
            {
                var index = i;
                var threshold = quantitative.Thresholds.Items[index];
                var n = index + 1;
                result.Add(Group($"Threshold {n}", [
                    Bool(threshold, $"Threshold{n}Enabled", $"appearance.thresholds.{n}.enabled", "Enabled", () => threshold.Enabled, v => threshold.Enabled = v),
                    Double(threshold, $"Threshold{n}Value", $"appearance.thresholds.{n}.value", "Value", () => threshold.Value, v => threshold.Value = v, () => threshold.Enabled ? null : NotApplicable("Enabled")),
                    Color(threshold, $"Threshold{n}Color", $"appearance.thresholds.{n}.color", "Color", () => threshold.Color, v => threshold.Color = v, () => threshold.Enabled ? null : NotApplicable("Enabled"))
                ]));
            }
        }
        return result;
    }

    private static List<PropertyDescriptor> BuildData(
        C.WidgetDefinition widget,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> mediaEndpoints,
        IDictionary<string, string>? endpointTypeOverrides)
    {
        var result = new List<PropertyDescriptor>();
        switch (widget)
        {
            case C.ValueWidgetDefinition value:
            {
                var literal = C.ValueSourceKind.IsText(value.SourceKind);
                var numericLiteral = literal && LiteralNumericContract.TryParse(value.Text, out _);
                MetricDescriptor? CurrentMetric() => MetricContract.GetDescriptor(value.Metric);
                var descriptor = CurrentMetric();
                var valueRows = new List<PropertyDescriptor>();
                if (literal)
                {
                    valueRows.Add(MetricOption(
                        value,
                        nameof(C.ValueWidgetDefinition.SourceUnit),
                        "data.value.literal_source_unit",
                        "Source Unit",
                        () => value.SourceUnit,
                        v => value.SourceUnit = v,
                        () => LiteralNumericContract.SourceUnitOptions,
                        () => numericLiteral ? null : NotApplicable("Text")));
                }
                else
                {
                    valueRows.Add(new CanonicalPropertyDescriptor(
                        nameof(C.ValueWidgetDefinition.SourceUnit),
                        "data.value.metric_source_unit",
                        "Source Unit",
                        value,
                        typeof(string),
                        () => CurrentMetric() is { } metric ? MetricContract.GetSourceUnitLabel(metric) : string.Empty,
                        null));
                }

                valueRows.Add(MetricOption(
                    value,
                    nameof(C.ValueWidgetDefinition.Unit),
                    "data.value.unit",
                    "Unit",
                    () => value.Unit,
                    v => value.Unit = v,
                    () => literal
                        ? LiteralNumericContract.GetUnitOptions(value.SourceUnit)
                        : CurrentMetric() is { } metric ? MetricContract.GetUnits(metric) : Array.Empty<MetricPresentationOption>(),
                    () =>
                    {
                        if (literal)
                            return !numericLiteral ? NotApplicable("Text") : null;
                        var metric = CurrentMetric();
                        return metric is null || MetricContract.GetUnits(metric).Count == 0
                            ? NotApplicable("Metric")
                            : null;
                    }));

                if (!literal && descriptor?.ValueKind == MetricValueKind.DateTime)
                {
                    valueRows.Add(DateTimeFormat(
                        value,
                        nameof(C.ValueWidgetDefinition.Format),
                        "data.value.format",
                        "Format",
                        () => value.Format,
                        v => value.Format = v));
                }
                else
                {
                    valueRows.Add(MetricOption(
                        value,
                        nameof(C.ValueWidgetDefinition.Format),
                        "data.value.format",
                        "Format",
                        () => value.Format,
                        v => value.Format = v,
                        () => literal
                            ? LiteralNumericContract.GetFormatOptions(value.SourceUnit)
                            : CurrentMetric() is { } metric ? MetricContract.GetFormats(metric) : Array.Empty<MetricPresentationOption>(),
                        () =>
                        {
                            if (literal)
                            {
                                if (!numericLiteral)
                                    return NotApplicable("Text");
                                return LiteralNumericContract.SupportsFormat(value.SourceUnit, value.Unit)
                                    ? null
                                    : NotApplicable("Unit");
                            }

                            var metric = CurrentMetric();
                            if (metric is null || metric.ValueKind is MetricValueKind.Text or MetricValueKind.Boolean)
                                return NotApplicable("Metric");
                            if (metric.ValueKind == MetricValueKind.Duration &&
                                string.Equals(value.Unit, "auto", StringComparison.OrdinalIgnoreCase))
                                return NotApplicable("Unit");
                            return null;
                        }));
                }

                result.Add(Group("Value", valueRows));
                result.Add(Group("Display", [
                    String(value, nameof(C.ValueWidgetDefinition.Prefix), "data.display.prefix", "Prefix", () => value.Prefix, v => value.Prefix = Convert.ToString(v) ?? string.Empty),
                    String(value, nameof(C.ValueWidgetDefinition.Suffix), "data.display.suffix", "Suffix", () => value.Suffix, v => value.Suffix = Convert.ToString(v) ?? string.Empty),
                    String(value, nameof(C.ValueWidgetDefinition.Fallback), "data.display.fallback", "Fallback", () => value.Fallback, v => value.Fallback = Convert.ToString(v) ?? "--", disabled: () => literal ? NotApplicable("Type") : null)
                ]));
                break;
            }
            case C.BinaryWidgetDefinition binary:
            {
                var usesValue = BinaryUsesValueVisual(binary);
                var setpoint = string.Equals(binary.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase);
                MetricDescriptor? CurrentMetric() => MetricContract.GetDescriptor(binary.Metric);
                var metric = CurrentMetric();
                if (usesValue || setpoint)
                {
                    var valueRows = new List<PropertyDescriptor>
                    {
                        new CanonicalPropertyDescriptor(
                            "SourceUnit",
                            "data.binary.metric_source_unit",
                            "Source Unit",
                            binary,
                            typeof(string),
                            () => CurrentMetric() is { } current ? MetricContract.GetSourceUnitLabel(current) : string.Empty,
                            null),
                        MetricOption(
                            binary,
                            nameof(C.BinaryWidgetDefinition.Unit),
                            "data.binary.unit",
                            "Unit",
                            () => binary.Unit,
                            v => binary.Unit = v,
                            () => CurrentMetric() is { } current ? GetApplicableUnitOptions(binary, current) : Array.Empty<MetricPresentationOption>(),
                            () => CurrentMetric() is { } current && GetApplicableUnitOptions(binary, current).Count > 0
                                ? null
                                : NotApplicable("Metric"))
                    };

                    if (metric?.ValueKind == MetricValueKind.DateTime && usesValue)
                    {
                        valueRows.Add(DateTimeFormat(
                            binary,
                            nameof(C.BinaryWidgetDefinition.Format),
                            "data.binary.format",
                            "Format",
                            () => binary.Format,
                            v => binary.Format = v));
                    }
                    else
                    {
                        valueRows.Add(MetricOption(
                            binary,
                            nameof(C.BinaryWidgetDefinition.Format),
                            "data.binary.format",
                            "Format",
                            () => binary.Format,
                            v => binary.Format = v,
                            () => CurrentMetric() is { } current ? MetricContract.GetFormats(current) : Array.Empty<MetricPresentationOption>(),
                            () =>
                            {
                                if (!usesValue)
                                    return NotApplicable("Source Type");
                                var current = CurrentMetric();
                                if (current is null || current.ValueKind is MetricValueKind.Text or MetricValueKind.Boolean)
                                    return NotApplicable("Metric");
                                if (current.ValueKind == MetricValueKind.Duration &&
                                    string.Equals(binary.Unit, "auto", StringComparison.OrdinalIgnoreCase))
                                    return NotApplicable("Unit");
                                return null;
                            }));
                    }

                    result.Add(Group("Value", valueRows));
                    if (usesValue)
                    {
                        result.Add(Group("Display", [
                            String(binary, nameof(C.BinaryWidgetDefinition.Prefix), "data.display.prefix", "Prefix", () => binary.Prefix, v => binary.Prefix = Convert.ToString(v) ?? string.Empty),
                            String(binary, nameof(C.BinaryWidgetDefinition.Suffix), "data.display.suffix", "Suffix", () => binary.Suffix, v => binary.Suffix = Convert.ToString(v) ?? string.Empty),
                            String(binary, nameof(C.BinaryWidgetDefinition.Fallback), "data.display.fallback", "Fallback", () => binary.Fallback, v => binary.Fallback = Convert.ToString(v) ?? "--")
                        ]));
                    }
                }

                result.Add(Group("Evaluation", [
                    String(binary, nameof(C.BinaryWidgetDefinition.EvaluationMode), "data.binary.evaluation.mode", "Mode", () => binary.EvaluationMode, v => binary.EvaluationMode = Convert.ToString(v) ?? BinarySignalContract.EvaluationModeAuto, converter: BinaryModeConverter),
                    String(binary, nameof(C.BinaryWidgetDefinition.TrueIf), "data.binary.evaluation.true_if", "True If", () => binary.TrueIf, v => binary.TrueIf = Convert.ToString(v) ?? BinarySignalContract.TrueIfGreaterThan, converter: BinaryTrueIfConverter, disabled: () => string.Equals(binary.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase) ? null : NotApplicable("Mode")),
                    String(binary, nameof(C.BinaryWidgetDefinition.Setpoint), "data.binary.evaluation.setpoint", "Setpoint", () => binary.Setpoint, v => binary.Setpoint = Convert.ToString(v) ?? string.Empty, disabled: () => string.Equals(binary.EvaluationMode, BinarySignalContract.EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase) ? null : NotApplicable("Mode"))
                ]));
                break;
            }
            case C.QuantitativeWidgetDefinition quantitative:
            {
                MetricDescriptor? CurrentMetric() => MetricContract.GetDescriptor(quantitative.Metric);
                result.Add(Group("Value", [
                    new CanonicalPropertyDescriptor(
                        "SourceUnit",
                        "data.quantitative.metric_source_unit",
                        "Source Unit",
                        quantitative,
                        typeof(string),
                        () => CurrentMetric() is { } metric ? MetricContract.GetSourceUnitLabel(metric) : string.Empty,
                        null),
                    MetricOption(
                        quantitative,
                        nameof(C.QuantitativeWidgetDefinition.Unit),
                        "data.quantitative.unit",
                        "Unit",
                        () => quantitative.Unit,
                        v => quantitative.Unit = v,
                        () => CurrentMetric() is { } metric ? MetricContract.GetNumericUnits(metric) : Array.Empty<MetricPresentationOption>(),
                        () => CurrentMetric() is { } metric && MetricContract.GetNumericUnits(metric).Count > 0
                            ? null
                            : NotApplicable("Metric"))
                ]));
                result.Add(Group("Range", [
                    Double(quantitative, nameof(C.QuantitativeWidgetDefinition.Min), "data.range.min", "Min", () => quantitative.Min, v => quantitative.Min = v),
                    Double(quantitative, nameof(C.QuantitativeWidgetDefinition.Max), "data.range.max", "Max", () => quantitative.Max, v => quantitative.Max = v)
                ]));
                break;
            }
        }

        if (widget is C.MediaSystemWidgetDefinition && endpointTypeOverrides is not null && mediaEndpoints.Count > 0)
        {
            result.Add(Group("Endpoint Overrides", mediaEndpoints
                .Select(endpoint => (PropertyDescriptor)new EndpointOverridePropertyDescriptor(endpoint, endpointTypeOverrides))
                .ToList()));
        }
        return result;
    }

    private static List<PropertyDescriptor> BuildGauge(C.WidgetDefinition widget, string? dashboardDirectory)
    {
        var result = new List<PropertyDescriptor>();
        if (widget is C.BarWidgetDefinition bar)
        {
            result.Add(Group("Bar", [
                String(bar, "BarContentMode", "gauge.bar.content_mode", "Mode", () => bar.ContentMode, v => bar.ContentMode = Convert.ToString(v) ?? BarImageContract.ContentModeFill, converter: BarContentConverter),
                Bool(bar, nameof(C.QuantitativeWidgetDefinition.Reverse), "gauge.bar.reverse", "Reverse", () => bar.Reverse, v => bar.Reverse = v),
                Float(bar, nameof(C.QuantitativeWidgetDefinition.Gap), "gauge.bar.gap", "Gap", () => bar.Gap, v => bar.Gap = Math.Max(0f, v))
            ]));
            return result;
        }
        if (widget is not C.GaugeWidgetDefinition gauge)
            return result;

        result.Add(Group("Horseshoe", [
            Bool(gauge.Track, "TrackEnabled", "gauge.track.enabled", "Enabled", () => gauge.Track.Enabled, v => gauge.Track.Enabled = v),
            Bool(gauge, nameof(C.QuantitativeWidgetDefinition.Reverse), "gauge.reverse", "Reverse", () => gauge.Reverse, v => gauge.Reverse = v),
            Float(gauge, nameof(C.GaugeWidgetDefinition.StartAngle), "gauge.arc.start", "Start Angle", () => gauge.StartAngle, v => gauge.StartAngle = v),
            Float(gauge, nameof(C.GaugeWidgetDefinition.EndAngle), "gauge.arc.end", "End Angle", () => gauge.EndAngle, v => gauge.EndAngle = v),
            Float(gauge.Track, "Thickness", "gauge.track.thickness", "Thickness", () => gauge.Track.Thickness, v => gauge.Track.Thickness = Math.Max(0f, v), () => gauge.Track.Enabled ? null : NotApplicable("Enabled")),
            Color(gauge.Track, "TrackBackgroundColor", "gauge.track.background", "Background Color", () => gauge.Track.BackgroundColor, v => gauge.Track.BackgroundColor = v, () => gauge.Track.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Track, "TrackBorderWidth", "gauge.track.border.width", "Border Width", () => gauge.Track.BorderWidth, v => gauge.Track.BorderWidth = Math.Max(0f, v), () => gauge.Track.Enabled ? null : NotApplicable("Enabled")),
            Color(gauge.Track, "TrackBorderColor", "gauge.track.border.color", "Border Color", () => gauge.Track.BorderColor, v => gauge.Track.BorderColor = v, () => gauge.Track.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Track, "TrackCornerRadius", "gauge.track.corner_radius", "Corner Radius", () => gauge.Track.CornerRadius, v => gauge.Track.CornerRadius = Math.Max(0f, v), () => gauge.Track.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge, nameof(C.QuantitativeWidgetDefinition.Gap), "gauge.track.gap", "Gap", () => gauge.Gap, v => gauge.Gap = Math.Max(0f, v))
        ]));
        result.Add(Group("Needle", [
            Bool(gauge.Needle, "NeedleEnabled", "gauge.needle.enabled", "Enabled", () => gauge.Needle.Enabled, v => gauge.Needle.Enabled = v),
            Float(gauge.Needle, "NeedleThickness", "gauge.needle.thickness", "Thickness", () => gauge.Needle.Thickness, v => gauge.Needle.Thickness = Math.Max(0f, v), () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Color(gauge.Needle, "NeedleColor", "gauge.needle.color", "Color", () => gauge.Needle.Color, v => gauge.Needle.Color = v, () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Needle, "NeedleStartOffset", "gauge.needle.start_offset", "Start Offset", () => gauge.Needle.StartOffset, v => gauge.Needle.StartOffset = Math.Max(0f, v), () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Needle, "NeedleEndOffset", "gauge.needle.end_offset", "End Offset", () => gauge.Needle.EndOffset, v => gauge.Needle.EndOffset = Math.Max(0f, v), () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Needle.Pointer, "NeedlePointerLength", "gauge.needle.pointer.length", "Pointer Length", () => gauge.Needle.Pointer.Length, v => gauge.Needle.Pointer.Length = Math.Max(0f, v), () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Float(gauge.Needle.Pointer, "NeedlePointerThickness", "gauge.needle.pointer.thickness", "Pointer Thickness", () => gauge.Needle.Pointer.Thickness, v => gauge.Needle.Pointer.Thickness = Math.Max(0f, v), () => gauge.Needle.Enabled ? null : NotApplicable("Enabled")),
            Color(gauge.Needle.Pointer, "NeedlePointerColor", "gauge.needle.pointer.color", "Pointer Color", () => gauge.Needle.Pointer.Color, v => gauge.Needle.Pointer.Color = v, () => gauge.Needle.Enabled ? null : NotApplicable("Enabled"))
        ]));
        return result;
    }

    private static List<PropertyDescriptor> BuildStates(C.WidgetDefinition widget, string? applicationRoot, string? dashboardDirectory)
    {
        if (widget is not C.StateVisualWidgetDefinition stateWidget ||
            !StateVisualProfileContract.SupportsValueSource(widget.Type))
            return [];

        TextOverflowStateContract.EnsureProfiles(stateWidget);
        var result = new List<PropertyDescriptor>();
        foreach (var spec in StateVisualProfileContract.GetSpecs(widget.Type).OrderBy(spec => spec.Order))
        {
            if (!stateWidget.Profiles.TryGetValue(spec.Key, out var profile))
                continue;
            result.Add(Group(spec.Category, [
                StateSourceType(stateWidget, spec, profile),
                StateSource(stateWidget, spec, profile, applicationRoot, dashboardDirectory)
            ]));
        }
        return result;
    }

    private static List<PropertyDescriptor> BuildImage(C.WidgetDefinition widget, string? applicationRoot, string? dashboardDirectory)
    {
        var result = new List<PropertyDescriptor>();
        if (widget is C.ImageWidgetDefinition image)
        {
            var loopDisabled = () =>
            {
                if (!C.ImageAssetSourceType.IsIcon(image.Asset.SourceType))
                    return null;
                return IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, image.Asset.Source, out var capabilities) && capabilities.IsAnimated
                    ? null
                    : NotApplicable("Source");
            };
            result.Add(Group("Image", [
                String(image.Asset, "Fit", "image.asset.fit", "Fit", () => image.Asset.Fit, v => image.Asset.Fit = Convert.ToString(v) ?? StateVisualProfileContract.FitStretch, converter: FitConverter),
                Bool(image.Asset, "Loop", "image.asset.loop", "Loop", () => image.Asset.Loop, v => image.Asset.Loop = v, loopDisabled),
                Float(image, nameof(C.ImageWidgetDefinition.Opacity), "image.opacity", "Opacity", () => image.Opacity, v => image.Opacity = Math.Clamp(v, 0f, 1f))
            ]));
            return result;
        }

        if (widget is C.BarWidgetDefinition bar)
        {
            if (!BarImageContract.IsImageMode(bar.ContentMode))
                return [];
            var editor = !string.IsNullOrWhiteSpace(dashboardDirectory) ? new DashboardImageSourceEditor(dashboardDirectory!) : null;
            result.Add(Group("Image", [
                new CanonicalPropertyDescriptor("BarImageSource", "bar.image.source", "Source", bar.Image, typeof(string), () => bar.Image.Source ?? string.Empty, v => bar.Image.Source = EmptyToNull(v), editor: editor),
                String(bar.Image, "BarImageFit", "bar.image.fit", "Fit", () => bar.Image.Fit, v => bar.Image.Fit = Convert.ToString(v) ?? BarImageContract.ImageFitStretch, converter: BarFitConverter),
                String(bar.Image, "BarProgressMode", "bar.image.progress_mode", "Progress Mode", () => bar.Image.ProgressMode, v => bar.Image.ProgressMode = Convert.ToString(v) ?? BarImageContract.ProgressModeScale, converter: BarProgressConverter),
                Bool(bar.Image, "Loop", "bar.image.loop", "Loop", () => bar.Image.Loop, v => bar.Image.Loop = v)
            ]));
            return result;
        }

        if (widget is C.StateVisualWidgetDefinition stateWidget)
        {
            TextOverflowStateContract.EnsureProfiles(stateWidget);
            foreach (var spec in StateVisualProfileContract.GetSpecs(widget.Type).OrderBy(spec => spec.Order))
            {
                var profile = stateWidget.Profiles[spec.Key];
                if (!C.StateContentType.IsImage(profile.ContentType))
                    continue;
                result.Add(Group(spec.Category, [
                    String(profile.Asset, StateName(spec.Key, StateVisualProfileField.Fit), $"image.state.{spec.Key}.fit", "Fit", () => profile.Asset.Fit, v => profile.Asset.Fit = Convert.ToString(v) ?? StateVisualProfileContract.FitContain, converter: FitConverter),
                    Bool(profile.Asset, StateName(spec.Key, StateVisualProfileField.Loop), $"image.state.{spec.Key}.loop", "Loop", () => profile.Asset.Loop, v => profile.Asset.Loop = v,
                        () => C.ImageAssetSourceType.IsIcon(profile.Asset.SourceType) &&
                              (!IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, profile.Asset.Source, out var loopCapabilities) || !loopCapabilities.IsAnimated)
                            ? NotApplicable("Source")
                            : null),
                    Color(profile, StateName(spec.Key, StateVisualProfileField.Color), $"image.state.{spec.Key}.color", "Color", () => profile.Color ?? "#FFFFFFFF", v => profile.Color = v,
                        () => C.ImageAssetSourceType.IsFile(profile.Asset.SourceType)
                            ? NotApplicable("Source Type")
                            : (!IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, profile.Asset.Source, out var colorCapabilities) || !colorCapabilities.Tintable)
                                ? NotApplicable("Source")
                                : null),
                    Float(profile, StateName(spec.Key, StateVisualProfileField.Opacity), $"image.state.{spec.Key}.opacity", "Opacity", () => profile.Opacity, v => profile.Opacity = Math.Clamp(v, 0f, 1f))
                ]));
            }
        }
        return result;
    }

    private static List<PropertyDescriptor> BuildText(C.WidgetDefinition widget)
    {
        var result = new List<PropertyDescriptor>();
        if (widget is C.ValueWidgetDefinition value)
        {
            AddTextGroups(result, value.TextPresentation, "text.value", null);
            return result;
        }
        if (widget is C.StateVisualWidgetDefinition stateWidget)
        {
            TextOverflowStateContract.EnsureProfiles(stateWidget);
            foreach (var spec in StateVisualProfileContract.GetSpecs(widget.Type).OrderBy(spec => spec.Order))
            {
                var profile = stateWidget.Profiles[spec.Key];
                if (!C.StateContentType.IsValue(profile.ContentType))
                    continue;
                var nested = new List<PropertyDescriptor>();
                nested.Add(Group("Foreground", [
                    Color(profile, StateName(spec.Key, StateVisualProfileField.Color), $"text.state.{spec.Key}.color", "Color", () => profile.Color ?? "#FFFFFFFF", v => profile.Color = v),
                    Float(profile, StateName(spec.Key, StateVisualProfileField.Opacity), $"text.state.{spec.Key}.opacity", "Opacity", () => profile.Opacity, v => profile.Opacity = Math.Clamp(v, 0f, 1f))
                ]));
                AddTextGroups(nested, profile.TextPresentation, $"text.state.{spec.Key}", null, spec.Key);
                result.Add(Group(spec.Category, nested));
            }
        }
        return result;
    }

    private static void AddTextGroups(List<PropertyDescriptor> result, C.TextPresentationDefinition text, string semanticPrefix, Func<string?>? disabled, string? stateKey = null)
    {
        string Name(StateVisualProfileField field, string plain) => stateKey is null ? plain : StateName(stateKey, field);
        result.Add(Group("Font", [
            String(text, Name(StateVisualProfileField.FontFamily, "FontFamily"), $"{semanticPrefix}.font.family", "Family", () => text.FontFamily, v => text.FontFamily = Convert.ToString(v)?.Trim() ?? "Roboto", disabled: disabled),
            Float(text, Name(StateVisualProfileField.FontSize, "FontSize"), $"{semanticPrefix}.font.size", "Size", () => text.FontSize, v => text.FontSize = Math.Max(1f, v), disabled),
            Int(text, Name(StateVisualProfileField.FontWeight, "FontWeight"), $"{semanticPrefix}.font.weight", "Weight", () => text.FontWeight, v => text.FontWeight = Math.Clamp(v, 1, 1000), disabled),
            Bool(text, Name(StateVisualProfileField.Italic, "Italic"), $"{semanticPrefix}.font.italic", "Italic", () => text.Italic, v => text.Italic = v, disabled)
        ]));
        result.Add(Group("Alignment", [
            String(text, Name(StateVisualProfileField.Align, "Align"), $"{semanticPrefix}.align.horizontal", "Horizontal", () => text.Align, v => text.Align = Convert.ToString(v) ?? "left", converter: AlignConverter, disabled: disabled),
            String(text, Name(StateVisualProfileField.VerticalAlign, "VerticalAlign"), $"{semanticPrefix}.align.vertical", "Vertical", () => text.VerticalAlign, v => text.VerticalAlign = Convert.ToString(v) ?? "baseline", converter: VerticalAlignConverter, disabled: disabled)
        ]));
        result.Add(Group("Outline", [
            Color(text, Name(StateVisualProfileField.OutlineColor, "OutlineColor"), $"{semanticPrefix}.outline.color", "Color", () => text.OutlineColor, v => text.OutlineColor = v, disabled),
            Float(text, Name(StateVisualProfileField.OutlineWidth, "OutlineWidth"), $"{semanticPrefix}.outline.width", "Width", () => text.OutlineWidth, v => text.OutlineWidth = Math.Max(0f, v), disabled)
        ]));
        result.Add(Group("Overflow", [
            String(text, Name(StateVisualProfileField.OverflowMode, "OverflowMode"), $"{semanticPrefix}.overflow.mode", "Mode", () => text.OverflowMode, v => text.OverflowMode = ValueOverflowContract.Normalize(Convert.ToString(v)), converter: OverflowConverter, disabled: disabled),
            Float(text, Name(StateVisualProfileField.ScrollSpeed, "ScrollSpeed"), $"{semanticPrefix}.overflow.scroll_speed", "Scroll Speed", () => text.ScrollSpeed, v => text.ScrollSpeed = Math.Max(0f, v), () => disabled?.Invoke() ?? (ValueOverflowContract.Normalize(text.OverflowMode) is ValueOverflowContract.Scroll or ValueOverflowContract.Bump ? null : NotApplicable("Overflow Mode"))),
            Int(text, Name(StateVisualProfileField.BumpPauseMs, "BumpPauseMs"), $"{semanticPrefix}.overflow.bump_pause", "Bump Pause", () => text.BumpPauseMs, v => text.BumpPauseMs = Math.Max(0, v), () => disabled?.Invoke() ?? (ValueOverflowContract.Normalize(text.OverflowMode) == ValueOverflowContract.Bump ? null : NotApplicable("Overflow Mode")))
        ]));
    }

    private static List<PropertyDescriptor> BuildCanvas(C.CanvasDefinition canvas, EditorPropertySection section, string? applicationRoot, string? dashboardDirectory)
    {
        if (section == EditorPropertySection.General)
        {
            return [
                Group("Background Image", BuildCanvasLayer("background_image", canvas.BackgroundImage, applicationRoot, dashboardDirectory, sourceOnly: true)),
                Group("Foreground Image", BuildCanvasLayer("foreground_image", canvas.ForegroundImage, applicationRoot, dashboardDirectory, sourceOnly: true)),
                Group("Geometry", [
                    Int(canvas, nameof(C.CanvasDefinition.Width), "general.geometry.width", "Width", () => canvas.Width, v => canvas.Width = Math.Max(1, v)),
                    Int(canvas, nameof(C.CanvasDefinition.Height), "general.geometry.height", "Height", () => canvas.Height, v => canvas.Height = Math.Max(1, v)),
                    Int(canvas, nameof(C.CanvasDefinition.Orientation), "general.geometry.orientation", "Orientation", () => canvas.Orientation, v => canvas.Orientation = v)
                ])
            ];
        }
        if (section == EditorPropertySection.Appearance)
            return [Group("Background", [Color(canvas, nameof(C.CanvasDefinition.BackgroundColor), "appearance.background.color", "Color", () => canvas.BackgroundColor, v => canvas.BackgroundColor = v)])];
        if (section == EditorPropertySection.Image)
        {
            return [
                Group("Background Image", BuildCanvasLayer("background_image", canvas.BackgroundImage, applicationRoot, dashboardDirectory, sourceOnly: false)),
                Group("Foreground Image", BuildCanvasLayer("foreground_image", canvas.ForegroundImage, applicationRoot, dashboardDirectory, sourceOnly: false))
            ];
        }
        if (section == EditorPropertySection.All)
        {
            var result = BuildCanvas(canvas, EditorPropertySection.General, applicationRoot, dashboardDirectory);
            result.AddRange(BuildCanvas(canvas, EditorPropertySection.Appearance, applicationRoot, dashboardDirectory));
            result.AddRange(BuildCanvas(canvas, EditorPropertySection.Image, applicationRoot, dashboardDirectory));
            return result;
        }
        return [];
    }

    private static List<PropertyDescriptor> BuildCanvasLayer(
        string role,
        C.CanvasImageLayerDefinition layer,
        string? applicationRoot,
        string? dashboardDirectory,
        bool sourceOnly)
    {
        var editor = !string.IsNullOrWhiteSpace(applicationRoot) && !string.IsNullOrWhiteSpace(dashboardDirectory)
            ? new ImageAssetSourceEditor(layer.Asset, applicationRoot!, dashboardDirectory!) : null;

        if (sourceOnly)
        {
            return ImageAssetSourceProperties(
                layer.Asset,
                $"general.{role}.asset.kind",
                $"general.{role}.asset.source",
                editor);
        }

        string? LoopDisabledReason()
        {
            if (string.IsNullOrWhiteSpace(layer.Asset.Source))
                return NotApplicable("Source");
            if (C.ImageAssetSourceType.IsIcon(layer.Asset.SourceType))
                return IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, layer.Asset.Source, out var iconCapabilities) && iconCapabilities.IsAnimated
                    ? null
                    : NotApplicable("Source");
            return ImageAssetCapabilityProbe.TryReadCapabilities(dashboardDirectory, layer.Asset.Source, out var imageCapabilities) && imageCapabilities.IsAnimated
                ? null
                : NotApplicable("Source");
        }

        string? ColorDisabledReason()
        {
            if (string.IsNullOrWhiteSpace(layer.Asset.Source))
                return NotApplicable("Source");
            if (C.ImageAssetSourceType.IsFile(layer.Asset.SourceType))
                return NotApplicable("Type");
            return IconAssetLoader.TryReadCapabilitiesFromRoot(applicationRoot, layer.Asset.Source, out var capabilities) && capabilities.Tintable
                ? null
                : NotApplicable("Source");
        }

        var prefix = $"image.{role}";
        return [
            String(layer.Asset, nameof(C.ImageAssetPresentationDefinition.Fit), $"{prefix}.asset.fit", "Fit", () => layer.Asset.Fit, v => layer.Asset.Fit = Convert.ToString(v) ?? StateVisualProfileContract.FitStretch, converter: FitConverter, disabled: () => string.IsNullOrWhiteSpace(layer.Asset.Source) ? NotApplicable("Source") : null),
            Float(layer, nameof(C.CanvasImageLayerDefinition.Opacity), $"{prefix}.opacity", "Opacity", () => layer.Opacity, v => layer.Opacity = Math.Clamp(v, 0f, 1f), () => string.IsNullOrWhiteSpace(layer.Asset.Source) ? NotApplicable("Source") : null),
            Bool(layer.Asset, nameof(C.ImageAssetPresentationDefinition.Loop), $"{prefix}.asset.loop", "Loop", () => layer.Asset.Loop, v => layer.Asset.Loop = v, LoopDisabledReason),
            Color(layer, nameof(C.CanvasImageLayerDefinition.Color), $"{prefix}.color", "Color", () => layer.Color, v => layer.Color = v, ColorDisabledReason)
        ];
    }

    private static List<PropertyDescriptor> ImageAssetSourceProperties(
        C.ImageAssetPresentationDefinition asset,
        string kindSemantic,
        string sourceSemantic,
        UITypeEditor? editor,
        Func<string>? sourceDisplayName = null)
    {
        return [
            new CanonicalPropertyDescriptor(
                nameof(C.ImageAssetPresentationDefinition.SourceType), kindSemantic, "Type", asset, typeof(string),
                () => C.ImageAssetSourceType.IsIcon(asset.SourceType) ? "Icon" : "Image",
                v => asset.ChangeSourceType(string.Equals(Convert.ToString(v), "Icon", StringComparison.OrdinalIgnoreCase)
                    ? C.ImageAssetSourceType.Icon
                    : C.ImageAssetSourceType.File),
                converter: ImageKindConverter),
            new CanonicalPropertyDescriptor(
                nameof(C.ImageAssetPresentationDefinition.Source), sourceSemantic, "Source", asset, typeof(string),
                () => asset.Source ?? string.Empty,
                v => asset.Source = EmptyToNull(v),
                editor: editor,
                multiEditContextKey: asset.SourceType,
                multiEditContextController: "Type",
                displayNameAccessor: sourceDisplayName)
        ];
    }

    private static PropertyDescriptor StateSourceType(C.StateVisualWidgetDefinition widget, StateVisualProfileSpec spec, C.StateVisualProfileDefinition profile)
    {
        var supported = StateVisualProfileContract.SupportsValueSource(widget.Type) ? new[] { "Image", "Icon", "Value" } : new[] { "Image", "Icon" };
        return new CanonicalPropertyDescriptor(
            StateName(spec.Key, StateVisualProfileField.SourceType), $"states.{spec.Key}.source.kind", "Source Type", profile, typeof(string),
            () => C.StateContentType.IsValue(profile.ContentType) ? "Value" : C.ImageAssetSourceType.IsIcon(profile.Asset.SourceType) ? "Icon" : "Image",
            v =>
            {
                var selected = Convert.ToString(v)?.Trim();
                if (string.Equals(selected, "Value", StringComparison.OrdinalIgnoreCase) && StateVisualProfileContract.SupportsValueSource(widget.Type))
                {
                    profile.ContentType = C.StateContentType.Value;
                    return;
                }
                profile.ContentType = C.StateContentType.Image;
                profile.Asset.ChangeSourceType(string.Equals(selected, "Icon", StringComparison.OrdinalIgnoreCase)
                    ? C.ImageAssetSourceType.Icon : C.ImageAssetSourceType.File);
            },
            converter: new FixedStringConverter(supported));
    }

    private static PropertyDescriptor StateSource(C.StateVisualWidgetDefinition widget, StateVisualProfileSpec spec, C.StateVisualProfileDefinition profile, string? applicationRoot, string? dashboardDirectory)
    {
        var editor = !string.IsNullOrWhiteSpace(applicationRoot) && !string.IsNullOrWhiteSpace(dashboardDirectory)
            ? new ImageAssetSourceEditor(profile.Asset, applicationRoot!, dashboardDirectory!) : null;
        return new CanonicalPropertyDescriptor(
            StateName(spec.Key, StateVisualProfileField.Source), $"states.{spec.Key}.asset.source", "Source", profile, typeof(string),
            () => profile.Asset.Source ?? string.Empty, v => profile.Asset.Source = EmptyToNull(v), editor: editor,
            disabledReason: () => C.StateContentType.IsValue(profile.ContentType) ? NotApplicable("Source Type") : null,
            multiEditContextKey: C.StateContentType.IsValue(profile.ContentType) ? "value" : profile.Asset.SourceType,
            multiEditContextController: "Source Type");
    }

    private static bool BinaryUsesValueVisual(C.BinaryWidgetDefinition widget) =>
        widget.Profiles.Values.Any(profile => C.StateContentType.IsValue(profile.ContentType));

    private static IReadOnlyList<MetricPresentationOption> GetApplicableUnitOptions(
        C.BinaryWidgetDefinition widget,
        MetricDescriptor metric)
    {
        var setpoint = string.Equals(
            widget.EvaluationMode,
            BinarySignalContract.EvaluationModeSetpoint,
            StringComparison.OrdinalIgnoreCase);
        if (setpoint && MetricContract.IsNumericValueKind(metric.ValueKind))
            return MetricContract.GetNumericUnits(metric);
        return BinaryUsesValueVisual(widget)
            ? MetricContract.GetUnits(metric)
            : Array.Empty<MetricPresentationOption>();
    }

    private static CanonicalPropertyDescriptor MetricOption(
        object target,
        string name,
        string semantic,
        string display,
        Func<string?> tokenGetter,
        Action<string?> tokenSetter,
        Func<IReadOnlyList<MetricPresentationOption>> optionsAccessor,
        Func<string?>? disabled = null) =>
        new(
            name,
            semantic,
            display,
            target,
            typeof(string),
            () => MetricOptionLabel(tokenGetter(), optionsAccessor()),
            value => tokenSetter(MetricOptionToken(Convert.ToString(value), optionsAccessor())),
            converter: new DynamicMetricOptionConverter(optionsAccessor),
            disabledReason: disabled);

    private static CanonicalPropertyDescriptor DateTimeFormat(
        object target,
        string name,
        string semantic,
        string display,
        Func<string?> getter,
        Action<string?> setter,
        Func<string?>? disabled = null) =>
        new(
            name,
            semantic,
            display,
            target,
            typeof(string),
            () => string.IsNullOrWhiteSpace(getter()) ? MetricValueFormatter.DefaultDateTimeFormat : getter(),
            value =>
            {
                var format = Convert.ToString(value) ?? string.Empty;
                DateTimeFormatCatalog.Validate(format);
                setter(format);
            },
            converter: TextConverter,
            editor: DateTimeFormatEditor.Instance,
            disabledReason: disabled);

    private static string MetricOptionLabel(string? token, IReadOnlyList<MetricPresentationOption> options)
    {
        var normalized = NormalizeMetricOptionToken(token);
        var option = options.FirstOrDefault(candidate => MetricOptionTokenEquals(candidate.Token, normalized));
        return option?.Label ?? normalized ?? "raw";
    }

    private static string? MetricOptionToken(string? value, IReadOnlyList<MetricPresentationOption> options)
    {
        var selected = value?.Trim() ?? string.Empty;
        var option = options.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, selected, StringComparison.OrdinalIgnoreCase));
        return NormalizeMetricOptionToken(option?.Token ?? selected);
    }

    private static bool MetricOptionTokenEquals(string? left, string? right) =>
        string.Equals(NormalizeMetricOptionToken(left), NormalizeMetricOptionToken(right), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeMetricOptionToken(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static string StateName(string stateKey, StateVisualProfileField field) => $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{stateKey}:{field}";
    private static PropertyGroupPropertyDescriptor Group(string name, IEnumerable<PropertyDescriptor> properties) => new(name, new PropertyGroupView(properties), string.Empty);
    private static string NotApplicable(string controllingProperty) => $"Not applicable for selected {controllingProperty}.";
    private static string? EmptyToNull(object? value)
    {
        var text = Convert.ToString(value)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static CanonicalPropertyDescriptor Metric(object target, string name, string semantic, string display, Func<string?> getter, Action<object?> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(string), () => getter() ?? string.Empty, setter, converter: TextConverter, editor: MetricEditor, disabledReason: disabled);

    private static CanonicalPropertyDescriptor String(object target, string name, string semantic, string display, Func<string> getter, Action<object?> setter, TypeConverter? converter = null, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(string), getter, setter, converter: converter ?? TextConverter, disabledReason: disabled);

    private static CanonicalPropertyDescriptor Bool(object target, string name, string semantic, string display, Func<bool> getter, Action<bool> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(bool), () => getter(), v => setter(Convert.ToBoolean(v, CultureInfo.InvariantCulture)), disabledReason: disabled);

    private static CanonicalPropertyDescriptor WholePixelCoordinate(object target, string name, string semantic, string display, Func<float> getter, Action<float> setter) =>
        new(name, semantic, display, target, typeof(float),
            () => WidgetResizeGeometry.NormalizeLogicalCoordinate(getter()),
            v => setter(WidgetResizeGeometry.NormalizeLogicalCoordinate(Convert.ToSingle(v, CultureInfo.InvariantCulture))));

    private static CanonicalPropertyDescriptor WholePixelDimension(object target, string name, string semantic, string display, Func<float> getter, Action<float> setter) =>
        new(name, semantic, display, target, typeof(float),
            () => WidgetResizeGeometry.NormalizeLogicalDimension(getter()),
            v => setter(Math.Max(0f, WidgetResizeGeometry.NormalizeLogicalDimension(Convert.ToSingle(v, CultureInfo.InvariantCulture)))));

    private static CanonicalPropertyDescriptor Float(object target, string name, string semantic, string display, Func<float> getter, Action<float> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(float), () => getter(), v => setter(Convert.ToSingle(v, CultureInfo.InvariantCulture)), disabledReason: disabled);

    private static CanonicalPropertyDescriptor Double(object target, string name, string semantic, string display, Func<double> getter, Action<double> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(double), () => getter(), v => setter(Convert.ToDouble(v, CultureInfo.InvariantCulture)), disabledReason: disabled);

    private static CanonicalPropertyDescriptor Int(object target, string name, string semantic, string display, Func<int> getter, Action<int> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(int), () => getter(), v => setter(Convert.ToInt32(v, CultureInfo.InvariantCulture)), disabledReason: disabled);

    private static CanonicalPropertyDescriptor Color(object target, string name, string semantic, string display, Func<string> getter, Action<string> setter, Func<string?>? disabled = null) =>
        new(name, semantic, display, target, typeof(string), getter,
            v => setter(RgbaColorConverter.NormalizeStorage(Convert.ToString(v) ?? "#FFFFFFFF")),
            converter: new RgbaColorConverter(), editor: RgbaColorEditor.Instance, disabledReason: disabled);
}
