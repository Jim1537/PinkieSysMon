using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal static class WidgetPanelLayerMode
{
    public const string None = "None";
    public const string Color = "Color";
    public const string Image = "Image";
    public const string Icon = "Icon";

    public static bool IsSupported(string? mode) =>
        mode is None or Color or Image or Icon;
}

/// <summary>
/// An independent, full-panel visual layer. Inactive sources remain dormant so that
/// switching modes does not discard a previously selected file or library icon.
/// </summary>
internal sealed class WidgetPanelLayerDefinition
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = WidgetPanelLayerMode.None;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#00000000";

    [JsonPropertyName("image")]
    public ImageAssetPresentationDefinition Image { get; set; } = new()
    {
        SourceType = ImageAssetSourceType.File
    };

    [JsonPropertyName("icon")]
    public ImageAssetPresentationDefinition Icon { get; set; } = new()
    {
        SourceType = ImageAssetSourceType.Icon
    };

    [JsonPropertyName("iconColor")]
    public string IconColor { get; set; } = "#FFFFFFFF";

    [JsonPropertyName("iconOpacity")]
    public float IconOpacity { get; set; } = 1f;

    [JsonIgnore]
    internal ImageAssetPresentationDefinition? ActiveAsset => Mode switch
    {
        WidgetPanelLayerMode.Image => Image,
        WidgetPanelLayerMode.Icon => Icon,
        _ => null
    };

    internal void Validate(string dashboardDirectory, string context, bool allowUnavailableArtwork)
    {
        if (!WidgetPanelLayerMode.IsSupported(Mode))
            throw new InvalidDataException($"{context} mode must be None, Color, Image, or Icon.");

        _ = global::PinkieSysMon.ColorParser.Parse(Color);
        _ = global::PinkieSysMon.ColorParser.Parse(IconColor);
        if (!float.IsFinite(IconOpacity) || IconOpacity is < 0f or > 1f)
            throw new InvalidDataException($"{context} iconOpacity must be in the range 0..1.");

        if (Image is null || Icon is null)
            throw new InvalidDataException($"{context} image and icon configuration must not be null.");
        Image.ValidateStructure($"{context} image");
        Icon.ValidateStructure($"{context} icon");
        if (!ImageAssetSourceType.IsFile(Image.SourceType) || !ImageAssetSourceType.IsIcon(Icon.SourceType))
            throw new InvalidDataException($"{context} has mismatched image/icon source types.");

        // Disabled resources are intentionally not resolved: a missing dormant file
        // must not prevent a dashboard from opening or saving.
        ActiveAsset?.ValidateActiveSource(dashboardDirectory, context,
            allowUnavailableArtwork: allowUnavailableArtwork);
    }
}
