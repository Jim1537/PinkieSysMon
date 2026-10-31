using System.ComponentModel;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue80PropertyUndoTests
{
    public static void SingleWidgetAndCanvasCapture()
    {
        var widget = new C.ValueWidgetDefinition { Id = "issue80-value", Name = "Before" };
        string? before = null;
        var widgetView = new EditorPropertyView(
            widget,
            section: EditorPropertySection.General,
            beforeValueChange: () => before ??= widget.Name);
        var name = FindNestedProperty(widgetView.GetProperties(), "Identity", nameof(C.WidgetDefinition.Name));

        name.SetValue(null, "After");
        Check(before == "Before" && widget.Name == "After",
            "A widget property must capture the unmodified value before its setter runs.");

        before = null; // Simulate consuming the checkpoint in PropertyValueChanged.
        name.SetValue(null, "Again");
        Check(before == "After" && widget.Name == "Again",
            "A second committed edit must capture the most recently committed value.");

        var canvas = new C.CanvasDefinition { Width = 320, Height = 240 };
        int? canvasBefore = null;
        var canvasView = new EditorPropertyView(
            canvas,
            section: EditorPropertySection.General,
            beforeValueChange: () => canvasBefore ??= canvas.Width);
        var width = FindNestedProperty(canvasView.GetProperties(), "Geometry", nameof(C.CanvasDefinition.Width));
        width.SetValue(null, 640);

        Check(canvasBefore == 320 && canvas.Width == 640,
            "A canvas property must capture the prior width before the mutation.");
    }

    public static void MultiWidgetCapture()
    {
        var first = new C.ValueWidgetDefinition { Id = "issue80-first", X = 10f };
        var second = new C.ValueWidgetDefinition { Id = "issue80-second", X = 20f };
        (float First, float Second)? before = null;
        var callbackCount = 0;

        void Capture()
        {
            callbackCount++;
            before ??= (first.X, second.X);
        }

        var view = new EditorMultiPropertyView(
        [
            new EditorPropertyView(first, section: EditorPropertySection.General, beforeValueChange: Capture),
            new EditorPropertyView(second, section: EditorPropertySection.General, beforeValueChange: Capture)
        ]);

        var x = FindNestedProperty(view.GetProperties(), "Geometry", nameof(C.WidgetDefinition.X));
        Check(x is MultiSelectionPropertyDescriptor,
            "Multi-selection must retain the established atomic property descriptor.");

        x.SetValue(null, 70f);
        Check(callbackCount == 2 && before == (10f, 20f) && first.X == 70f && second.X == 70f,
            "The first pre-mutation checkpoint must be retained across both setters.");

        before = null; // Simulate consuming the checkpoint after the group edit.
        x.SetValue(null, 80f);
        Check(before == (70f, 70f) && first.X == 80f && second.X == 80f,
            "The next group edit must checkpoint both newly committed values.");
    }

    private static PropertyDescriptor FindNestedProperty(
        PropertyDescriptorCollection properties,
        string groupName,
        string propertyName)
    {
        var group = properties[groupName]
            ?? throw new InvalidOperationException($"Missing property group: {groupName}");
        var target = group.GetValue(null)
            ?? throw new InvalidOperationException($"Missing property group target: {groupName}");
        return TypeDescriptor.GetProperties(target)[propertyName]
            ?? throw new InvalidOperationException($"Missing property: {groupName}.{propertyName}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
