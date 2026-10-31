using System.ComponentModel;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class MultiSelectionDropdownTests
{
    public static void MixedValueOptionLabels()
    {
        var first = new C.ValueWidgetDefinition
        {
            Id = "dropdown-first",
            TextPresentation = new C.TextPresentationDefinition { Align = "left", VerticalAlign = "top" }
        };
        var second = new C.ValueWidgetDefinition
        {
            Id = "dropdown-second",
            TextPresentation = new C.TextPresentationDefinition { Align = "right", VerticalAlign = "bottom" }
        };

        var multiple = new EditorMultiPropertyView(
        [
            new EditorPropertyView(first, section: EditorPropertySection.Text),
            new EditorPropertyView(second, section: EditorPropertySection.Text)
        ]);
        var properties = multiple.GetProperties();

        var horizontal = FindProperty(properties, "Alignment", nameof(C.TextPresentationDefinition.Align));
        Check(horizontal is MultiSelectionPropertyDescriptor,
            "Mixed Horizontal alignment must use the shared multi-selection descriptor.");
        CheckMixedAndOptions(horizontal, ["left", "center", "right"], exclusive: false);

        horizontal.SetValue(null, "center");
        Check(first.TextPresentation.Align == "center" && second.TextPresentation.Align == "center",
            "Selecting Horizontal alignment must update both selected widgets.");
        Check(Equals(horizontal.GetValue(null), "center") &&
              horizontal.Converter.ConvertToInvariantString(horizontal.GetValue(null)) == "center",
            "Equalized Horizontal alignment must show its actual value instead of ellipsis.");

        var vertical = FindProperty(properties, "Alignment", nameof(C.TextPresentationDefinition.VerticalAlign));
        CheckMixedAndOptions(vertical, ["baseline", "top", "middle", "bottom"], exclusive: false);
        vertical.SetValue(null, "middle");
        Check(first.TextPresentation.VerticalAlign == "middle" &&
              second.TextPresentation.VerticalAlign == "middle",
            "Selecting Vertical alignment must update both selected widgets.");

        var single = FindProperty(
            new EditorPropertyView(first, section: EditorPropertySection.Text).GetProperties(),
            "Alignment", nameof(C.TextPresentationDefinition.Align));
        Check(Equals(single.GetValue(null), "center") &&
              single.Converter.ConvertToInvariantString("left") == "left",
            "Single-widget property conversion must retain its original behavior.");

        var options = DateTimeComponentContract.Options.Select(option => option.Label).ToArray();
        Check(options.Length > 1, "The exclusive component converter must expose multiple values.");
        var left = options[0];
        var right = options[1];
        var exclusive = new MultiSelectionPropertyDescriptor(
        [
            new CanonicalPropertyDescriptor(
                "Option", "regression.component.option", "Option", first, typeof(string),
                () => left, value => left = Convert.ToString(value) ?? string.Empty,
                converter: new DateTimeComponentTypeConverter()),
            new CanonicalPropertyDescriptor(
                "Option", "regression.component.option", "Option", second, typeof(string),
                () => right, value => right = Convert.ToString(value) ?? string.Empty,
                converter: new DateTimeComponentTypeConverter())
        ]);
        CheckMixedAndOptions(exclusive, options, exclusive: true);
        exclusive.SetValue(null, options[0]);
        Check(left == options[0] && right == options[0],
            "Exclusive predefined options must remain editable across compatible selected properties.");
    }

    private static void CheckMixedAndOptions(
        PropertyDescriptor descriptor,
        string[] expected,
        bool exclusive)
    {
        Check(descriptor.GetValue(null) is null &&
              descriptor.Converter.ConvertToInvariantString(descriptor.GetValue(null)) == "...",
            "Only the mixed current value should display the ellipsis.");
        var converter = descriptor.Converter;
        Check(converter.GetStandardValuesSupported(null) &&
              converter.GetStandardValuesExclusive(null) == exclusive,
            "The multi-selection converter must preserve the option-list contract.");
        var actual = converter.GetStandardValues(null)?
            .Cast<object?>()
            .Select(option => converter.ConvertToInvariantString(option) ?? string.Empty)
            .ToArray() ?? [];
        Check(actual.SequenceEqual(expected, StringComparer.Ordinal),
            "Every dropdown option must retain its actual label and order for mixed selections.");
    }

    private static PropertyDescriptor FindProperty(PropertyDescriptorCollection properties, params string[] names)
    {
        PropertyDescriptorCollection current = properties;
        PropertyDescriptor? row = null;
        foreach (var name in names)
        {
            row = current[name] ?? throw new InvalidOperationException("Missing property: " + name);
            if (name != names[^1])
            {
                var group = row.GetValue(null)
                    ?? throw new InvalidOperationException("Missing property group: " + name);
                current = TypeDescriptor.GetProperties(group);
            }
        }
        return row ?? throw new InvalidOperationException("No property path supplied.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }
}
