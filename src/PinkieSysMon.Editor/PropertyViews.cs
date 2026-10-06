using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon.Editor;

internal enum EditorPropertySection
{
    All,
    General,
    Appearance,
    Data,
    Gauge,
    States,
    Image,
    Text
}

internal sealed class EditorPropertyView : CustomTypeDescriptor
{
    private readonly string? _applicationRoot;
    private readonly string? _dashboardDirectory;
    private readonly IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo> _mediaEndpoints;
    private readonly IDictionary<string, string>? _endpointTypeOverrides;
    private readonly Func<object, (float Width, float Height)?>? _effectiveSizeAccessor;
    private readonly EditorPropertySection _section;
    private PropertyDescriptorCollection? _cachedProperties;

    public EditorPropertyView(
        object target,
        string? applicationRoot = null,
        string? dashboardDirectory = null,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>? mediaEndpoints = null,
        IDictionary<string, string>? endpointTypeOverrides = null,
        Func<object, (float Width, float Height)?>? effectiveSizeAccessor = null,
        EditorPropertySection section = EditorPropertySection.All)
    {
        if (target is not PinkieSysMon.DashboardModel.WidgetDefinition and
            not PinkieSysMon.DashboardModel.CanvasDefinition)
        {
            throw new ArgumentException(
                "Property Editor accepts only canonical schema-19 dashboard objects.",
                nameof(target));
        }

        Target = target;
        _applicationRoot = applicationRoot;
        _dashboardDirectory = dashboardDirectory;
        _mediaEndpoints = mediaEndpoints ?? Array.Empty<WindowsMediaTelemetrySource.MediaEndpointInfo>();
        _endpointTypeOverrides = endpointTypeOverrides;
        _effectiveSizeAccessor = effectiveSizeAccessor;
        _section = section;
    }

    public object Target { get; }

    public override PropertyDescriptorCollection GetProperties() =>
        _cachedProperties ??= CanonicalPropertyViewBuilder.Build(
            Target,
            _section,
            _applicationRoot,
            _dashboardDirectory,
            _mediaEndpoints,
            _endpointTypeOverrides,
            widget => _effectiveSizeAccessor?.Invoke(widget));

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;
}

internal sealed class EditorMultiPropertyView : CustomTypeDescriptor
{
    private readonly IReadOnlyList<EditorPropertyView> _views;
    private PropertyDescriptorCollection? _cachedProperties;

    public EditorMultiPropertyView(IReadOnlyList<EditorPropertyView> views)
    {
        if (views is null || views.Count == 0)
            throw new ArgumentException("At least one property view is required.", nameof(views));
        if (views.Any(view => view.Target is not CanonicalWidgetDefinition))
            throw new ArgumentException("Multi-selection accepts only canonical schema-19 widgets.", nameof(views));
        _views = views;
    }

    public IReadOnlyList<CanonicalWidgetDefinition> Targets =>
        _views.Select(view => (CanonicalWidgetDefinition)view.Target).ToArray();

    public override PropertyDescriptorCollection GetProperties() =>
        _cachedProperties ??= BuildIntersection(
            _views.Select(view => new PropertySet((CanonicalWidgetDefinition)view.Target, view.GetProperties())).ToArray());

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;

    private static PropertyDescriptorCollection BuildIntersection(IReadOnlyList<PropertySet> sets)
    {
        if (sets.Count == 0)
            return new PropertyDescriptorCollection(Array.Empty<PropertyDescriptor>(), readOnly: true);

        var result = new List<PropertyDescriptor>();
        foreach (PropertyDescriptor first in sets[0].Properties)
        {
            var key = SemanticKey(first);
            var matches = new PropertyDescriptor[sets.Count];
            matches[0] = first;
            var missing = false;
            for (var i = 1; i < sets.Count; i++)
            {
                var match = sets[i].Properties.Cast<PropertyDescriptor>()
                    .FirstOrDefault(candidate => string.Equals(SemanticKey(candidate), key, StringComparison.Ordinal));
                if (match is null)
                {
                    missing = true;
                    break;
                }
                matches[i] = match;
            }
            if (missing)
                continue;

            var groupFlags = matches.Select(item => item is PropertyGroupPropertyDescriptor).Distinct().ToArray();
            if (groupFlags.Length != 1)
                continue;

            if (groupFlags[0])
            {
                var children = new PropertySet[sets.Count];
                var invalid = false;
                for (var i = 0; i < sets.Count; i++)
                {
                    var group = matches[i].GetValue(null);
                    if (group is null)
                    {
                        invalid = true;
                        break;
                    }
                    children[i] = new PropertySet(sets[i].Target, TypeDescriptor.GetProperties(group));
                }
                if (invalid)
                    continue;

                var intersection = BuildIntersection(children);
                if (intersection.Count == 0)
                    continue;
                result.Add(new PropertyGroupPropertyDescriptor(
                    first.Name,
                    new PropertyGroupView(intersection.Cast<PropertyDescriptor>()),
                    first.Category));
                continue;
            }

            if (!AreSemanticallyCompatible(matches))
                continue;
            result.Add(new MultiSelectionPropertyDescriptor(matches));
        }

        return new PropertyDescriptorCollection(result.ToArray(), readOnly: true);
    }

    private static bool AreSemanticallyCompatible(IReadOnlyList<PropertyDescriptor> descriptors)
    {
        var first = descriptors[0];
        if (descriptors.Any(item => item.PropertyType != first.PropertyType))
            return false;
        if (descriptors.Select(SemanticKey).Distinct(StringComparer.Ordinal).Count() != 1)
            return false;

        var editorTypes = descriptors
            .Select(item =>
                (item is IPropertyEditorSemanticDescriptor semantic
                    ? semantic.GetSemanticEditor(typeof(UITypeEditor))
                    : item.GetEditor(typeof(UITypeEditor)))?.GetType())
            .Distinct()
            .ToArray();
        return editorTypes.Length == 1;
    }

    private static string SemanticKey(PropertyDescriptor descriptor) =>
        descriptor is IPropertySemanticKeyDescriptor semantic ? semantic.SemanticKey : descriptor.Name;

    private sealed record PropertySet(CanonicalWidgetDefinition Target, PropertyDescriptorCollection Properties);
}

internal sealed class MultiSelectionPropertyDescriptor : PropertyDescriptor
{
    private readonly IReadOnlyList<PropertyDescriptor> _descriptors;
    private readonly MultiSelectionTypeConverter _converter;
    private readonly string? _contextMismatchReason;

    public MultiSelectionPropertyDescriptor(IEnumerable<PropertyDescriptor> descriptors)
        : this(Validate(descriptors))
    {
    }

    private MultiSelectionPropertyDescriptor(PropertyDescriptor[] descriptors)
        : base(descriptors[0].Name, null)
    {
        _descriptors = descriptors;
        _contextMismatchReason = ResolveContextMismatchReason(_descriptors);
        _converter = new MultiSelectionTypeConverter(_descriptors, IsMixedValue);
    }

    public override string DisplayName
    {
        get
        {
            var names = _descriptors
                .Select(descriptor => descriptor.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (names.Length == 1)
                return names[0];
            if (Name.Equals("Source", StringComparison.Ordinal))
                return "Source";
            return names.OrderBy(name => name, StringComparer.Ordinal).First();
        }
    }
    public override string Category => _descriptors[0].Category;
    public override string Description => DisabledReason ?? _descriptors[0].Description;
    public override Type ComponentType => typeof(EditorMultiPropertyView);
    public override Type PropertyType => _descriptors[0].PropertyType;
    public override bool IsReadOnly => DisabledReason is not null || _descriptors.Any(descriptor => descriptor.IsReadOnly);
    public override TypeConverter Converter => _converter;

    private string? DisabledReason
    {
        get
        {
            if (_contextMismatchReason is not null)
                return _contextMismatchReason;

            var reasons = _descriptors
                .Where(descriptor => descriptor.IsReadOnly)
                .Select(descriptor => descriptor.Description?.Trim())
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return reasons.Length == 1 ? reasons[0] : null;
        }
    }

    public override bool CanResetValue(object component) =>
        !IsReadOnly && _descriptors.Any(descriptor => descriptor.CanResetValue(component));

    public override object? GetValue(object? component)
    {
        var first = _descriptors[0].GetValue(component);
        for (var i = 1; i < _descriptors.Count; i++)
        {
            if (!ValuesEqual(first, _descriptors[i].GetValue(component)))
                return null;
        }
        return first;
    }

    public override void ResetValue(object component)
    {
        if (IsReadOnly)
            return;

        ApplyAtomically(
            descriptor => descriptor.CanResetValue(component)
                ? ResetAndRead(descriptor, component)
                : descriptor.GetValue(component),
            component);
    }

    public override void SetValue(object? component, object? value)
    {
        if (IsReadOnly)
            return;

        var oldValues = _descriptors.Select(descriptor => descriptor.GetValue(component)).ToArray();
        try
        {
            foreach (var descriptor in _descriptors)
                descriptor.SetValue(component, value);
        }
        catch
        {
            // A setter is allowed to mutate before it reports failure. Restore every target,
            // including the failing target, so a bulk edit never leaves a partial result.
            for (var i = _descriptors.Count - 1; i >= 0; i--)
            {
                try { _descriptors[i].SetValue(component, oldValues[i]); }
                catch { }
            }
            throw;
        }

        OnValueChanged(component, EventArgs.Empty);
    }

    public override bool ShouldSerializeValue(object component) =>
        _descriptors.Any(descriptor => descriptor.ShouldSerializeValue(component));

    public override object? GetEditor(Type editorBaseType)
    {
        if (IsReadOnly && editorBaseType == typeof(UITypeEditor))
            return null;

        var editors = _descriptors
            .Select(descriptor => descriptor.GetEditor(editorBaseType))
            .ToArray();
        if (editors.All(editor => editor is null))
            return null;
        if (editors.Any(editor => editor is null))
            return null;
        var type = editors[0]!.GetType();
        return editors.All(editor => editor!.GetType() == type) ? editors[0] : null;
    }

    private bool IsMixedValue()
    {
        var first = _descriptors[0].GetValue(null);
        for (var i = 1; i < _descriptors.Count; i++)
        {
            if (!ValuesEqual(first, _descriptors[i].GetValue(null)))
                return true;
        }
        return false;
    }

    private static bool ValuesEqual(object? left, object? right) => Equals(left, right);

    private static PropertyDescriptor[] Validate(IEnumerable<PropertyDescriptor> descriptors)
    {
        var materialized = descriptors?.ToArray() ?? throw new ArgumentNullException(nameof(descriptors));
        if (materialized.Length < 2)
            throw new ArgumentException("Multi-selection property requires at least two descriptors.", nameof(descriptors));
        var name = materialized[0].Name;
        if (materialized.Any(descriptor => !string.Equals(descriptor.Name, name, StringComparison.Ordinal)))
            throw new ArgumentException("Multi-selection property descriptors must have the same name.", nameof(descriptors));
        return materialized;
    }

    private static string? ResolveContextMismatchReason(IReadOnlyList<PropertyDescriptor> descriptors)
    {
        var contexts = descriptors
            .OfType<IMultiEditContextDescriptor>()
            .ToArray();
        if (contexts.Length != descriptors.Count || contexts.Length == 0)
            return null;

        var keys = contexts.Select(context => context.MultiEditContextKey).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length <= 1)
            return null;

        var controllers = contexts
            .Select(context => context.MultiEditContextController)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return controllers.Length == 1
            ? $"Not applicable for selected {controllers[0]}."
            : "Not applicable for selected properties.";
    }

    private static object? ResetAndRead(PropertyDescriptor descriptor, object component)
    {
        descriptor.ResetValue(component);
        return descriptor.GetValue(component);
    }

    private void ApplyAtomically(Func<PropertyDescriptor, object?> operation, object component)
    {
        var oldValues = _descriptors.Select(descriptor => descriptor.GetValue(component)).ToArray();
        try
        {
            foreach (var descriptor in _descriptors)
                operation(descriptor);
        }
        catch
        {
            for (var i = _descriptors.Count - 1; i >= 0; i--)
            {
                try { _descriptors[i].SetValue(component, oldValues[i]); }
                catch { }
            }
            throw;
        }
        OnValueChanged(component, EventArgs.Empty);
    }
}

internal interface IMultiEditContextDescriptor
{
    string MultiEditContextKey { get; }
    string MultiEditContextController { get; }
}

internal interface IPropertyEditorSemanticDescriptor
{
    object? GetSemanticEditor(Type editorBaseType);
}

internal sealed class MultiSelectionTypeConverter : TypeConverter
{
    private readonly IReadOnlyList<PropertyDescriptor> _descriptors;
    private readonly Func<bool> _isMixed;
    private TypeConverter Primary => _descriptors[0].Converter;

    public MultiSelectionTypeConverter(
        IReadOnlyList<PropertyDescriptor> descriptors,
        Func<bool> isMixed)
    {
        _descriptors = descriptors;
        _isMixed = isMixed;
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        Primary.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || Primary.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) =>
        Primary.ConvertFrom(context, culture, value);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && _isMixed())
            return "...";
        return Primary.ConvertTo(context, culture, value, destinationType);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) =>
        _descriptors.All(descriptor => descriptor.Converter.GetStandardValuesSupported(context));

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) =>
        GetStandardValuesSupported(context) &&
        _descriptors.All(descriptor => descriptor.Converter.GetStandardValuesExclusive(context));

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        if (!GetStandardValuesSupported(context))
            return new StandardValuesCollection(Array.Empty<object>());

        var firstValues = _descriptors[0].Converter.GetStandardValues(context)
            ?.Cast<object?>()
            .Where(value => value is not null)
            .ToArray() ?? Array.Empty<object?>();

        var common = new List<object>();
        foreach (var candidate in firstValues)
        {
            var key = ConvertValueToKey(_descriptors[0].Converter, context, candidate);
            var presentEverywhere = true;
            for (var i = 1; i < _descriptors.Count; i++)
            {
                var values = _descriptors[i].Converter.GetStandardValues(context);
                if (values is null || !values.Cast<object?>().Any(value =>
                        string.Equals(
                            ConvertValueToKey(_descriptors[i].Converter, context, value),
                            key,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    presentEverywhere = false;
                    break;
                }
            }

            if (presentEverywhere && candidate is not null)
                common.Add(candidate);
        }

        return new StandardValuesCollection(common);
    }

    public override bool IsValid(ITypeDescriptorContext? context, object? value) =>
        _descriptors.All(descriptor => descriptor.Converter.IsValid(context, value));

    private static string ConvertValueToKey(
        TypeConverter converter,
        ITypeDescriptorContext? context,
        object? value)
    {
        try
        {
            return converter.ConvertTo(context, CultureInfo.InvariantCulture, value, typeof(string)) as string
                   ?? Convert.ToString(value, CultureInfo.InvariantCulture)
                   ?? string.Empty;
        }
        catch
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}

internal sealed class PropertyGroupView : CustomTypeDescriptor
{
    private readonly PropertyDescriptorCollection _properties;

    public PropertyGroupView(IEnumerable<PropertyDescriptor> properties)
    {
        _properties = new PropertyDescriptorCollection(properties.ToArray(), readOnly: true);
    }

    public override PropertyDescriptorCollection GetProperties() => _properties;
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => _properties;
    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;
    public override string ToString() => string.Empty;
}

internal sealed class PropertyGroupPropertyDescriptor : PropertyDescriptor
{
    private static readonly TypeConverter GroupConverter = new PropertyGroupTypeConverter();
    private readonly PropertyGroupView _group;
    private readonly string _category;
    public PropertyGroupPropertyDescriptor(string name, PropertyGroupView group, string category)
        : base(name, null)
    {
        _group = group;
        _category = category;
    }

    public override string Category => _category;
    public override string Description => string.Empty;
    public override Type ComponentType => typeof(EditorPropertyView);
    public override Type PropertyType => typeof(PropertyGroupView);
    public override bool IsReadOnly => true;
    public override TypeConverter Converter => GroupConverter;
    public override bool CanResetValue(object component) => false;
    public override object GetValue(object? component) => _group;
    public override void ResetValue(object component) { }
    public override void SetValue(object? component, object? value) { }
    // Synthetic editor group; it never represents a persisted dashboard value.
    // First-level label emphasis is applied by the PropertyGrid presentation layer.
    public override bool ShouldSerializeValue(object component) => false;
}

internal sealed class PropertyGroupTypeConverter : ExpandableObjectConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        System.Globalization.CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string))
            return string.Empty;

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

internal enum StateVisualProfileField
{
    SourceType,
    Source,
    Color,
    Opacity,
    Fit,
    Loop,
    FontFamily,
    FontSize,
    FontWeight,
    Italic,
    Align,
    VerticalAlign,
    OutlineColor,
    OutlineWidth,
    OverflowMode,
    ScrollSpeed,
    BumpPauseMs
}

internal static class StateVisualProfilePropertyDescriptor
{
    public const string PropertyPrefix = "StateProfile:";
}

internal sealed class EndpointOverridePropertyDescriptor : PropertyDescriptor
{
    public const string PropertyPrefix = "EndpointOverride:";
    private static readonly FixedStringConverter OverrideConverter =
        new(new[] { "Auto" }.Concat(MediaMetricContract.EndpointTypes));

    private readonly WindowsMediaTelemetrySource.MediaEndpointInfo _endpoint;
    private readonly IDictionary<string, string> _overrides;

    public EndpointOverridePropertyDescriptor(
        WindowsMediaTelemetrySource.MediaEndpointInfo endpoint,
        IDictionary<string, string> overrides)
        : base(PropertyPrefix + endpoint.EndpointId, null)
    {
        _endpoint = endpoint;
        _overrides = overrides;
    }

    public override string DisplayName => _endpoint.FriendlyName;
    public override string Category => "Endpoint Overrides";
    public override string Description => string.Empty;
    public override Type ComponentType => typeof(CanonicalWidgetDefinition);
    public override Type PropertyType => typeof(string);
    public override bool IsReadOnly => false;
    public override TypeConverter Converter => OverrideConverter;
    public override bool CanResetValue(object component) =>
        _overrides.ContainsKey(_endpoint.EndpointId);

    public override object? GetValue(object? component) =>
        _overrides.TryGetValue(_endpoint.EndpointId, out var value)
            ? MediaMetricContract.NormalizeEndpointType(value)
            : "Auto";

    public override void SetValue(object? component, object? value)
    {
        var text = Convert.ToString(value)?.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _overrides.Remove(_endpoint.EndpointId);
        }
        else
        {
            var normalized = MediaMetricContract.NormalizeEndpointType(text);
            _overrides[_endpoint.EndpointId] = normalized;
        }

        OnValueChanged(component, EventArgs.Empty);
    }

    public override void ResetValue(object component)
    {
        _overrides.Remove(_endpoint.EndpointId);
        OnValueChanged(component, EventArgs.Empty);
    }

    public override bool ShouldSerializeValue(object component) =>
        _overrides.ContainsKey(_endpoint.EndpointId);
}

internal sealed class FixedStringConverter : StringConverter
{
    private readonly StandardValuesCollection _values;

    public FixedStringConverter(IEnumerable<string> values)
    {
        _values = new StandardValuesCollection(values.ToArray());
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => _values;
}
