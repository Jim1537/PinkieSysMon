using System.ComponentModel;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class FontFamilySelectionTests
{
    public static void BundledFontOptions()
    {
        var root = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        Check(File.Exists(Path.Combine(root, "assets", "fonts", "Roboto", "Roboto-Regular.ttf")),
            "The bundled Roboto test fixture must be available.");

        var value = new C.ValueWidgetDefinition { Id = "font-value" };
        var valueRow = FindProperty(
            new EditorPropertyView(value, applicationRoot: root, section: EditorPropertySection.Text).GetProperties(),
            "Font", nameof(C.TextPresentationDefinition.FontFamily));
        CheckBundledOptions(valueRow, "Text/Value");

        valueRow.SetValue(null, "Roboto");
        Check(value.TextPresentation.FontFamily == "Roboto",
            "Selecting a bundled font must update the canonical Text/Value property.");

        var allRow = FindProperty(
            new EditorPropertyView(value, applicationRoot: root, section: EditorPropertySection.All).GetProperties(),
            "Font", nameof(C.TextPresentationDefinition.FontFamily));
        CheckBundledOptions(allRow, "All tab");

        C.StateVisualWidgetDefinition[] stateWidgets =
        [
            new C.BinaryWidgetDefinition { Id = "font-binary" },
            new C.PowerWidgetDefinition { Id = "font-power" },
            new C.MediaPlayerWidgetDefinition { Id = "font-player" },
            new C.MediaSystemWidgetDefinition { Id = "font-system" }
        ];
        foreach (var widget in stateWidgets)
        {
            TextOverflowStateContract.EnsureProfiles(widget);
            var state = StateVisualProfileContract.GetSpecs(widget.Type).First();
            widget.Profiles[state.Key].ContentType = C.StateContentType.Value;

            var row = FindProperty(
                new EditorPropertyView(widget, applicationRoot: root, section: EditorPropertySection.Text).GetProperties(),
                state.Category,
                "Font",
                StateVisualProfilePropertyDescriptor.PropertyPrefix + state.Key + ":" + StateVisualProfileField.FontFamily);
            CheckBundledOptions(row, widget.Type + " value state");
            row.SetValue(null, "Roboto");
            Check(widget.Profiles[state.Key].TextPresentation.FontFamily == "Roboto",
                "A value-state font selection must update only the canonical state text presentation.");
        }

        var second = new C.ValueWidgetDefinition { Id = "font-value-second" };
        var multiple = new EditorMultiPropertyView(
        [
            new EditorPropertyView(value, applicationRoot: root, section: EditorPropertySection.Text),
            new EditorPropertyView(second, applicationRoot: root, section: EditorPropertySection.Text)
        ]);
        var multiRow = FindProperty(multiple.GetProperties(), "Font", nameof(C.TextPresentationDefinition.FontFamily));
        Check(multiRow is MultiSelectionPropertyDescriptor,
            "Multi-selection must preserve the common canonical font-family property.");
        CheckBundledOptions(multiRow, "multi-selection");
        multiRow.SetValue(null, "Roboto");
        Check(value.TextPresentation.FontFamily == "Roboto" &&
              second.TextPresentation.FontFamily == "Roboto",
            "Choosing a font in multi-selection must update both canonical presentations.");

        var withoutRoot = FindProperty(
            new EditorPropertyView(new C.ValueWidgetDefinition { Id = "font-no-root" },
                section: EditorPropertySection.Text).GetProperties(),
            "Font", nameof(C.TextPresentationDefinition.FontFamily));
        Check(!withoutRoot.IsReadOnly,
            "The Family property must remain editable when no application root is supplied.");
    }

    private static void CheckBundledOptions(PropertyDescriptor descriptor, string context)
    {
        var converter = descriptor.Converter;
        Check(converter.GetStandardValuesSupported(null),
            context + " must expose standard font-family options.");
        Check(!converter.GetStandardValuesExclusive(null),
            context + " must preserve editable string entry.");
        var values = converter.GetStandardValues(null)?.Cast<object?>()
            .Select(value => Convert.ToString(value))
            .ToArray() ?? [];
        Check(values.Contains("Roboto", StringComparer.OrdinalIgnoreCase),
            context + " must include the bundled Roboto font family.");
    }

    private static PropertyDescriptor FindProperty(PropertyDescriptorCollection properties, params string[] names)
    {
        PropertyDescriptorCollection current = properties;
        PropertyDescriptor? row = null;
        foreach (var name in names)
        {
            row = current[name] ?? throw new InvalidOperationException("Missing font property: " + name);
            if (name != names[^1])
            {
                var group = row.GetValue(null)
                    ?? throw new InvalidOperationException("Missing font property group: " + name);
                current = TypeDescriptor.GetProperties(group);
            }
        }
        return row ?? throw new InvalidOperationException("No font property path supplied.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }
}
