using System.ComponentModel;
using System.Drawing.Design;
using System.Text.Json;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue117ImageSourcePropertyTests
{
    public static void PlacementAndSwitching()
    {
        var image = new C.ImageWidgetDefinition { Id = "issue117-image", Width = 64, Height = 64 };
        image.Asset.Source = "images/logo.png";
        image.PanelBackground.Mode = C.WidgetPanelLayerMode.Image;
        image.PanelBackground.Image.Source = "images/background.png";
        image.PanelForeground.Mode = C.WidgetPanelLayerMode.Icon;
        image.PanelForeground.Icon.Source = "lucide:check";

        var originalBackground = JsonSerializer.Serialize(image.PanelBackground);
        var originalForeground = JsonSerializer.Serialize(image.PanelForeground);
        var originalAsset = JsonSerializer.Serialize(image.Asset);
        var beforeChanges = new List<string>();
        var general = new EditorPropertyView(image, "application", "dashboard",
            section: EditorPropertySection.General,
            beforeValueChange: () => beforeChanges.Add(JsonSerializer.Serialize(image.Asset))).GetProperties();

        Check(general.Cast<PropertyDescriptor>().Select(p => p.DisplayName)
                .SequenceEqual(["Identity", "Data Source", "Image Source"]),
            "Image General groups must follow the universal groups in fixed order.");
        var identityType = Children(general, "Identity")["Type"];
        Check(identityType is { IsReadOnly: true } && Equals(identityType.GetValue(null), "image"),
            "Widget identity Type must remain distinct and read-only.");

        var metric = Children(general, "Data Source");
        Check(metric.Count == 1 && metric["Metric"] is { IsReadOnly: true } &&
              Equals(metric["Metric"]!.GetValue(null), "Not applicable"),
            "The universal Image Metric must remain visible, disabled, and unchanged.");

        var sources = Children(general, "Image Source");
        Check(sources.Cast<PropertyDescriptor>().Select(p => p.DisplayName).SequenceEqual(["Type", "Image"]),
            "The Image Source group must expose Type and a contextually labeled picker.");
        var kind = sources["SourceType"]!;
        var source = sources["Source"]!;
        Check(!kind.IsReadOnly && !source.IsReadOnly &&
              source.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "The existing contextual file/icon selector must be available on General.");
        Check(kind.Converter.GetStandardValues(null)!.Cast<string>().SequenceEqual(["Image", "Icon"]),
            "Image source kinds must keep the existing Image/Icon choices.");

        var presentation = new EditorPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Check(Children(presentation, "Image").Cast<PropertyDescriptor>()
                .Select(p => p.DisplayName).SequenceEqual(["Fit", "Loop"]),
            "File Image presentation must not duplicate Type or Source.");
        Check(presentation["Background"] is not null && presentation["Foreground"] is not null,
            "Independent Panel image presentation groups must remain available.");
        Check(originalAsset == JsonSerializer.Serialize(image.Asset) && beforeChanges.Count == 0,
            "Opening property views must not mutate the dashboard or mark it dirty.");

        kind.SetValue(null, "Icon");
        Check(C.ImageAssetSourceType.IsIcon(image.Asset.SourceType) &&
              image.Asset.Source is null && source.DisplayName == "Icon",
            "Switching to Icon must clear the old file path and update the contextual label.");
        Check(beforeChanges.Count == 1 && beforeChanges[0] == originalAsset,
            "The type change must record the pre-edit value for Undo.");
        source.SetValue(null, "lucide:cpu");
        Check(image.Asset.Source == "lucide:cpu" && beforeChanges.Count == 2,
            "The icon selector must write the existing asset.source with Undo support.");
        kind.SetValue(null, "Icon");
        Check(image.Asset.Source == "lucide:cpu",
            "Reselecting the current source kind must preserve the selected source.");

        var iconPresentation = new EditorPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Check(Children(iconPresentation, "Image").Cast<PropertyDescriptor>()
                .Select(p => p.DisplayName).SequenceEqual(["Fit", "Loop", "Color", "Opacity"]),
            "Icon presentation must retain Fit, Loop, Color and Opacity only.");
        kind.SetValue(null, "Image");
        Check(C.ImageAssetSourceType.IsFile(image.Asset.SourceType) &&
              image.Asset.Source is null && source.DisplayName == "Image",
            "Switching back to Image must clear the stale icon and restore the file picker label.");

        Check(JsonSerializer.Serialize(image.PanelBackground) == originalBackground &&
              JsonSerializer.Serialize(image.PanelForeground) == originalForeground,
            "Switching content sources must never alter Panel Background/Foreground.");

        var nonImage = new C.GraphWidgetDefinition();
        Check(new EditorPropertyView(nonImage, section: EditorPropertySection.General)
                .GetProperties()["Image Source"] is null,
            "The Image-specific group must not appear on other widget types.");
    }

    public static void MultiSelectionAndSerialization()
    {
        var file = new C.ImageWidgetDefinition
        {
            Id = "issue117-file", Width = 64, Height = 64,
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File, Source = "images/a.png"
            }
        };
        var icon = new C.ImageWidgetDefinition
        {
            Id = "issue117-icon", Width = 64, Height = 64,
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.Icon, Source = "lucide:check"
            }
        };
        EditorMultiPropertyView Multi() => new([
            new EditorPropertyView(file, "application", "dashboard", section: EditorPropertySection.General),
            new EditorPropertyView(icon, "application", "dashboard", section: EditorPropertySection.General)
        ]);

        var mixed = Children(Multi().GetProperties(), "Image Source");
        var mixedSource = mixed["Source"]!;
        Check(mixedSource.IsReadOnly &&
              mixedSource.Description == "Not applicable for selected Type.",
            "Mixed file/icon selections must disable their incompatible picker.");
        mixedSource.SetValue(null, "ignored");
        Check(file.Asset.Source == "images/a.png" && icon.Asset.Source == "lucide:check",
            "Disabled mixed source edits must not alter either image.");
        var mixedKind = mixed["SourceType"]!;
        Check(!mixedKind.IsReadOnly, "Mixed Image Source Type must remain editable.");
        mixedKind.SetValue(null, "Image");
        Check(C.ImageAssetSourceType.IsFile(icon.Asset.SourceType) && icon.Asset.Source is null &&
              file.Asset.Source == "images/a.png",
            "Bulk source kind switching must clear only incompatible prior sources.");

        var sameKind = Children(Multi().GetProperties(), "Image Source");
        var sameSource = sameKind["Source"]!;
        Check(!sameSource.IsReadOnly && sameSource.DisplayName == "Image" &&
              sameSource.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "Compatible multi-selection must expose one editable file picker.");

        sameSource.SetValue(null, "images/shared.png");
        Check(file.Asset.Source == "images/shared.png" && icon.Asset.Source == "images/shared.png",
            "Compatible multi-edit must update both canonical asset sources.");

        var serialized = JsonSerializer.Serialize(file.Asset);
        using var json = JsonDocument.Parse(serialized);
        Check(json.RootElement.GetProperty("sourceType").GetString() == "file" &&
              json.RootElement.GetProperty("source").GetString() == "images/shared.png" &&
              json.RootElement.EnumerateObject().Select(x => x.Name)
                  .OrderBy(x => x, StringComparer.Ordinal)
                  .SequenceEqual(["fit", "loop", "source", "sourceType"]),
            "Moving the picker must not introduce new persisted source fields.");
        var restored = JsonSerializer.Deserialize<C.ImageAssetPresentationDefinition>(serialized)!;
        Check(restored.SourceType == file.Asset.SourceType && restored.Source == file.Asset.Source &&
              restored.Fit == file.Asset.Fit && restored.Loop == file.Asset.Loop,
            "Canonical image asset source must survive JSON serialization and parsing.");
    }

    private static PropertyDescriptorCollection Children(PropertyDescriptorCollection properties, string group) =>
        TypeDescriptor.GetProperties(properties[group]?.GetValue(null) ??
            throw new InvalidOperationException($"Missing property group '{group}'."));

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
