using System.ComponentModel;
using System.Drawing.Design;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class CommonWidgetPropertyTests
{
    public static void OrderAndBindings()
    {
        C.WidgetDefinition[] widgets = [
            new C.ValueWidgetDefinition(), new C.BinaryWidgetDefinition(), new C.GraphWidgetDefinition(),
            new C.GaugeWidgetDefinition(), new C.BarWidgetDefinition(),
            new C.ImageWidgetDefinition(), new C.PowerWidgetDefinition(), new C.MediaSystemWidgetDefinition(),
            new C.MediaPlayerWidgetDefinition()
        ];
        foreach (var widget in widgets)
        {
            var general = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
            var panel = new EditorPropertyView(widget, section: EditorPropertySection.Panel).GetProperties();
            ExpectPrefix(general, "Identity", "Data Source");
            ExpectPrefix(panel, "Geometry", "Background", "Foreground", "Border", "Shadow");
            ExpectRows(general, "Identity", "Name", "Type", "ID");
            Check(Children(general, "Data Source")["Metric"] is not null,
                "General must expose metric binding.");
            ExpectRows(panel, "Geometry", "X", "Y", "Width", "Height", "Padding", "Rotation", "Corner Radius", "Z-order");
            ExpectRows(panel, "Background", "Mode");
            ExpectRows(panel, "Foreground", "Mode");
            ExpectRows(panel, "Border", "Width", "Color", "Style", "Top", "Right", "Bottom", "Left");
            ExpectRows(panel, "Shadow", "Enabled", "Color", "Blur", "Offset X", "Offset Y");

            var border = Children(panel, "Border");
            Check(border["BorderLeft"]!.IsReadOnly, "Zero-width border sides must be disabled.");
            border["BorderWidth"]!.SetValue(null, 2f);
            border["BorderStyle"]!.SetValue(null, "Dotted");
            border["BorderLeft"]!.SetValue(null, false);
            border["BorderWidth"]!.SetValue(null, 0f);
            Check(!widget.BorderLeft && widget.BorderStyle == C.WidgetBorderStyle.Dotted, "Disabling the border lost its settings.");
        }
        var mixed = new EditorMultiPropertyView(widgets.Select(w =>
            new EditorPropertyView(w, section: EditorPropertySection.General)).ToArray()).GetProperties();
        var metric = Children(mixed, "Data Source")["Metric"]!;
        Check(metric.IsReadOnly, "A selection including derived and absent metrics cannot be rebound.");
        var media = (C.MediaSystemWidgetDefinition)widgets[7];
        var binding = Children(new EditorPropertyView(media, section: EditorPropertySection.General).GetProperties(), "Data Source")["Metric"]!;
        media.MediaSource = MediaMetricContract.InputSource;
        Check(binding.IsReadOnly && Equals(binding.GetValue(null), $"{MediaMetricContract.InputAvailable}, {MediaMetricContract.InputType}"),
            "Both derived endpoint metrics must be visible.");
    }

    public static void BorderDependencyRefresh()
    {
        Check(EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.WidgetDefinition.BorderWidth)) &&
              EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.CanvasDefinition.BorderWidth)) &&
              EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.WidgetDefinition.ShadowEnabled)) &&
              EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.CanvasDefinition.ShadowEnabled)) &&
              !EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.WidgetDefinition.BorderColor)) &&
              !EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.CalculatorWidgetDefinition.ButtonBorderWidth)),
            "Common Panel refresh policy must include its controllers and exclude independent fields.");

        object[] targets = [
            new C.ValueWidgetDefinition(), new C.BinaryWidgetDefinition(),
            new C.GraphWidgetDefinition(), new C.GaugeWidgetDefinition(),
            new C.BarWidgetDefinition(), new C.ImageWidgetDefinition(),
            new C.PowerWidgetDefinition(), new C.MediaSystemWidgetDefinition(),
            new C.MediaPlayerWidgetDefinition(), new C.CalculatorWidgetDefinition(),
            new C.CanvasDefinition()
        ];
        foreach (var target in targets)
        {
            if (target is C.WidgetDefinition widget)
                widget.BorderWidth = 0f;
            else
                ((C.CanvasDefinition)target).BorderWidth = 0f;

            PropertyDescriptorCollection Border() =>
                Children(new EditorPropertyView(target, section: EditorPropertySection.Panel).GetProperties(), "Border");

            var disabled = Border();
            Check(disabled["BorderColor"]!.IsReadOnly &&
                  disabled["BorderColor"]!.GetEditor(typeof(UITypeEditor)) is null,
                $"{target.GetType().Name}: zero width must disable the color editor.");
            foreach (var name in new[] { "BorderStyle", "BorderTop", "BorderRight", "BorderBottom", "BorderLeft" })
                Check(disabled[name]!.IsReadOnly, $"{target.GetType().Name}: zero width must disable {name}.");

            disabled["BorderWidth"]!.SetValue(null, 2f);
            var enabled = Border();
            Check(!enabled["BorderColor"]!.IsReadOnly &&
                  enabled["BorderColor"]!.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor,
                $"{target.GetType().Name}: positive width must restore the color editor.");
            foreach (var name in new[] { "BorderStyle", "BorderTop", "BorderRight", "BorderBottom", "BorderLeft" })
                Check(!enabled[name]!.IsReadOnly, $"{target.GetType().Name}: positive width must enable {name}.");

            enabled["BorderColor"]!.SetValue(null, "#FF336699");
            enabled["BorderWidth"]!.SetValue(null, 0f);
            var disabledAgain = Border();
            Check(disabledAgain["BorderColor"]!.IsReadOnly &&
                  disabledAgain["BorderColor"]!.GetEditor(typeof(UITypeEditor)) is null &&
                  Equals(disabledAgain["BorderColor"]!.GetValue(null), "#FF336699"),
                $"{target.GetType().Name}: disabling the border must retain its color.");

            disabledAgain["BorderWidth"]!.SetValue(null, 2f);
            var restored = Border();
            Check(!restored["BorderColor"]!.IsReadOnly &&
                  Equals(restored["BorderColor"]!.GetValue(null), "#FF336699"),
                $"{target.GetType().Name}: re-enabling the border must retain its color.");
        }

        var first = new C.ValueWidgetDefinition { BorderWidth = 0f };
        var second = new C.ImageWidgetDefinition { BorderWidth = 2f };
        PropertyDescriptorCollection MultiBorder() => Children(new EditorMultiPropertyView([
            new EditorPropertyView(first, section: EditorPropertySection.Panel),
            new EditorPropertyView(second, section: EditorPropertySection.Panel)
        ]).GetProperties(), "Border");

        Check(MultiBorder()["BorderColor"]!.IsReadOnly,
            "Mixed zero/nonzero widths must disable common border color.");
        MultiBorder()["BorderWidth"]!.SetValue(null, 2f);
        Check(first.BorderWidth == 2f && second.BorderWidth == 2f &&
              !MultiBorder()["BorderColor"]!.IsReadOnly,
            "Bulk positive widths must enable the shared color editor.");
        MultiBorder()["BorderWidth"]!.SetValue(null, 0f);
        Check(MultiBorder()["BorderColor"]!.IsReadOnly,
            "Bulk zero widths must disable the shared color editor.");

        var canvas = new C.CanvasDefinition { ShadowEnabled = false };
        PropertyDescriptorCollection Shadow() =>
            Children(new EditorPropertyView(canvas, section: EditorPropertySection.Panel).GetProperties(), "Shadow");
        Check(Shadow()["ShadowColor"]!.IsReadOnly, "Canvas shadow color must start disabled.");
        Shadow()["ShadowEnabled"]!.SetValue(null, true);
        Check(!Shadow()["ShadowColor"]!.IsReadOnly &&
              Shadow()["ShadowColor"]!.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor,
            "Canvas Shadow Enabled must restore its dependent color editor.");
    }


    public static void PanelLayerModeRefresh()
    {
        Check(EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.WidgetPanelLayerDefinition.Mode)) &&
              EditorForm.IsPropertyContextSwitch(nameof(C.WidgetPanelLayerDefinition.Mode)) &&
              !EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.WidgetPanelLayerDefinition.Color)) &&
              !EditorPropertyRefreshPolicy.RequiresCommonPanelRefresh(nameof(C.ImageAssetPresentationDefinition.Source)),
            "A shared Panel layer Mode edit must refresh Canvas and widget property views.");

        object[] targets = [
            new C.ValueWidgetDefinition(), new C.BinaryWidgetDefinition(),
            new C.GraphWidgetDefinition(), new C.GaugeWidgetDefinition(),
            new C.BarWidgetDefinition(), new C.ImageWidgetDefinition(),
            new C.PowerWidgetDefinition(), new C.MediaSystemWidgetDefinition(),
            new C.MediaPlayerWidgetDefinition(), new C.CalculatorWidgetDefinition(),
            new C.CanvasDefinition()
        ];

        foreach (var target in targets)
        {
            var layers = target switch
            {
                C.WidgetDefinition widget => new[] { widget.PanelBackground, widget.PanelForeground },
                C.CanvasDefinition canvas => new[] { canvas.PanelBackground, canvas.PanelForeground },
                _ => throw new InvalidOperationException("Unexpected common Panel target.")
            };

            for (var i = 0; i < layers.Length; i++)
            {
                var role = i == 0 ? "Background" : "Foreground";
                var layer = layers[i];
                layer.Mode = C.WidgetPanelLayerMode.None;
                layer.Image.Source = "saved-image.png";
                layer.Icon.Source = "lucide:check";

                PropertyDescriptorCollection PanelRows() =>
                    Children(new EditorPropertyView(target, section: EditorPropertySection.Panel).GetProperties(), role);
                PropertyDescriptorCollection ImageGroups() =>
                    new EditorPropertyView(target, section: EditorPropertySection.Image).GetProperties();

                Check(PanelRows()["Mode"] is not null && PanelRows()["Source"] is null &&
                      PanelRows()["Color"] is null && ImageGroups()[role] is null,
                    $"{target.GetType().Name}/{role}: None must hide dependent editors.");

                PanelRows()["Mode"]!.SetValue(null, "Image");
                Check(layer.Mode == C.WidgetPanelLayerMode.Image &&
                      Equals(PanelRows()["Source"]?.GetValue(null), "saved-image.png") &&
                      PanelRows()["Color"] is null &&
                      ImageGroups()[role] is not null,
                    $"{target.GetType().Name}/{role}: Image must reveal its Source and presentation group.");

                PanelRows()["Mode"]!.SetValue(null, "Icon");
                var iconGroup = ImageGroups()[role];
                Check(layer.Mode == C.WidgetPanelLayerMode.Icon &&
                      Equals(PanelRows()["Source"]?.GetValue(null), "lucide:check") &&
                      iconGroup is not null &&
                      Children(ImageGroups(), role)["IconOpacity"] is not null,
                    $"{target.GetType().Name}/{role}: Icon must reveal its Source and opacity.");

                PanelRows()["Mode"]!.SetValue(null, "Color");
                Check(layer.Mode == C.WidgetPanelLayerMode.Color &&
                      PanelRows()["Color"] is not null &&
                      PanelRows()["Source"] is null &&
                      ImageGroups()[role] is null,
                    $"{target.GetType().Name}/{role}: Color must hide image/icon controls.");

                PanelRows()["Mode"]!.SetValue(null, "None");
                Check(PanelRows()["Color"] is null && PanelRows()["Source"] is null &&
                      ImageGroups()[role] is null &&
                      layer.Image.Source == "saved-image.png" && layer.Icon.Source == "lucide:check",
                    $"{target.GetType().Name}/{role}: None must preserve dormant artwork.");

                PanelRows()["Mode"]!.SetValue(null, "Image");
                Check(Equals(PanelRows()["Source"]?.GetValue(null), "saved-image.png"),
                    $"{target.GetType().Name}/{role}: re-enabling Image must restore its Source.");
            }
        }

        var first = new C.ValueWidgetDefinition();
        var second = new C.GaugeWidgetDefinition();
        PropertyDescriptorCollection MultiPanel(string role) =>
            Children(new EditorMultiPropertyView([
                new EditorPropertyView(first, section: EditorPropertySection.Panel),
                new EditorPropertyView(second, section: EditorPropertySection.Panel)
            ]).GetProperties(), role);

        foreach (var role in new[] { "Background", "Foreground" })
        {
            Check(MultiPanel(role)["Source"] is null,
                $"Mixed selection/{role}: None must hide Source.");
            MultiPanel(role)["Mode"]!.SetValue(null, "Image");
            Check(MultiPanel(role)["Source"] is not null &&
                  (role == "Background"
                      ? first.PanelBackground.Mode == C.WidgetPanelLayerMode.Image &&
                        second.PanelBackground.Mode == C.WidgetPanelLayerMode.Image
                      : first.PanelForeground.Mode == C.WidgetPanelLayerMode.Image &&
                        second.PanelForeground.Mode == C.WidgetPanelLayerMode.Image),
                $"Mixed selection/{role}: shared Image Mode must reveal Source.");
            MultiPanel(role)["Mode"]!.SetValue(null, "None");
            Check(MultiPanel(role)["Source"] is null,
                $"Mixed selection/{role}: disabling a shared layer must hide Source.");
        }
    }

    public static void PaddingEdits()
    {
        var image = new C.ImageWidgetDefinition { Width = 100, Height = 100 };
        var gauge = new C.GaugeWidgetDefinition { Width = 100, Height = 100 };
        var views = new[] { new EditorPropertyView(image, section: EditorPropertySection.Panel),
            new EditorPropertyView(gauge, section: EditorPropertySection.Panel) };
        var field = Children(new EditorMultiPropertyView(views).GetProperties(), "Geometry")["Padding"]!;
        var converter = field.Converter;
        var uniform = converter.ConvertFromInvariantString("3.5");
        Check(Equals(uniform, converter.ConvertFromInvariantString("3.5 | 3.5 | 3.5 | 3.5")), "Equivalent insets differ.");
        var sides = converter.ConvertFromInvariantString(" 1 | 2 | 3 | 4 ");
        field.SetValue(null, sides);
        Check(image.Padding == new C.WidgetPadding(1, 2, 3, 4) && gauge.Padding == image.Padding &&
              converter.ConvertToInvariantString(field.GetValue(null)) == "1|2|3|4", "Incorrect clockwise inset order.");
        foreach (var invalid in new[] { "", "1|2", "1|2|3", "1||3|4", "-1", "NaN", "Infinity" })
        {
            try { converter.ConvertFromInvariantString(invalid); }
            catch (FormatException) { continue; }
            throw new InvalidOperationException($"Accepted malformed padding '{invalid}'.");
        }
        try { field.SetValue(null, converter.ConvertFromInvariantString("45")); }
        catch (InvalidDataException)
        {
            Check(image.Padding == new C.WidgetPadding(1, 2, 3, 4) && gauge.Padding == image.Padding,
                "A rejected Gauge inset left a partial mixed edit.");
            return;
        }
        throw new InvalidOperationException("Oversized Gauge inset was accepted.");
    }

    private static PropertyDescriptorCollection Children(PropertyDescriptorCollection rows, string group) =>
        TypeDescriptor.GetProperties(rows[group]?.GetValue(null)
            ?? throw new InvalidOperationException($"Missing property group '{group}'."));

    private static void ExpectPrefix(PropertyDescriptorCollection rows, params string[] names) =>
        Check(rows.Cast<PropertyDescriptor>().Take(names.Length).Select(p => p.DisplayName).SequenceEqual(names),
            "Unexpected common group order.");

    private static void ExpectRows(PropertyDescriptorCollection rows, string group, params string[] names) =>
        Check(Children(rows, group).Cast<PropertyDescriptor>().Select(p => p.DisplayName).SequenceEqual(names),
            $"Unexpected {group} property order.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
