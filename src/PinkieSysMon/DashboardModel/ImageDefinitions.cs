using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class ImageAssetPresentationDefinition
{
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = ImageAssetSourceType.File;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = global::PinkieSysMon.StateVisualProfileContract.FitStretch;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    internal bool ChangeSourceType(string sourceType)
    {
        if (!ImageAssetSourceType.IsSupported(sourceType))
            throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Image source type must be file or icon.");

        var normalized = ImageAssetSourceType.Normalize(sourceType);
        if (string.Equals(SourceType, normalized, StringComparison.OrdinalIgnoreCase))
        {
            SourceType = normalized;
            return false;
        }

        SourceType = normalized;
        Source = null;
        return true;
    }

    internal void ValidateStructure(string context)
    {
        if (!ImageAssetSourceType.IsSupported(SourceType))
            throw new InvalidDataException($"{context} SourceType must be file or icon.");
        if (!global::PinkieSysMon.StateVisualProfileContract.SupportedFits.Contains(Fit, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} Fit must be contain, cover, or stretch.");
    }

    internal void ValidateActiveSource(string dashboardBaseDirectory, string context, bool allowUnavailableArtwork = false)
    {
        ValidateStructure(context);
        if (string.IsNullOrWhiteSpace(Source))
            return;

        ResolveReference(dashboardBaseDirectory, SourceType, Source, context, allowUnavailableArtwork);
    }

    internal static string? ResolveReference(string directory, string type, string source, string context, bool allowUnavailableArtwork)
    {
        try
        {
            return ImageAssetSourceType.IsFile(type)
                ? AssetPathResolver.ResolveExistingFile(directory, source, context)
                : GlobalAssetResolver.ResolveIcon(directory, source);
        }
        catch (Exception ex) when (allowUnavailableArtwork && (ex is IOException or UnauthorizedAccessException))
        {
            return null;
        }
    }
}

internal sealed class CanvasImageLayerDefinition
{
    [JsonPropertyName("asset")]
    public ImageAssetPresentationDefinition Asset { get; set; } = new();

    [JsonPropertyName("iconOpacity")]
    public float IconOpacity { get; set; } = 1f;

    [JsonIgnore]
    public float Opacity { get => IconOpacity; set => IconOpacity = value; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    internal void Validate(string dashboardBaseDirectory, string role, bool allowUnavailableArtwork = false)
    {
        if (Asset is null)
            throw new InvalidDataException($"Canvas {role} image Asset must not be null.");

        Asset.ValidateStructure($"Canvas {role} image");
        if (!float.IsFinite(IconOpacity) || IconOpacity is < 0f or > 1f)
            throw new InvalidDataException($"Canvas {role} image Opacity must be in the range 0..1.");
        _ = global::PinkieSysMon.ColorParser.Parse(Color);

        if (!string.IsNullOrWhiteSpace(Asset.Source))
            Asset.ValidateActiveSource(dashboardBaseDirectory, $"Canvas {role} image", allowUnavailableArtwork: allowUnavailableArtwork);
    }
}

internal sealed class BarImagePresentationDefinition
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = global::PinkieSysMon.BarImageContract.ImageFitStretch;

    [JsonPropertyName("progressMode")]
    public string ProgressMode { get; set; } = global::PinkieSysMon.BarImageContract.ProgressModeScale;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    internal void ValidateStructure(string context)
    {
        if (!global::PinkieSysMon.BarImageContract.SupportedImageFits.Contains(Fit, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} has invalid Fit '{Fit}'.");
        if (!global::PinkieSysMon.BarImageContract.SupportedProgressModes.Contains(ProgressMode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} has invalid ProgressMode '{ProgressMode}'.");
    }

    internal void ValidateActive(string dashboardBaseDirectory, string context, bool allowUnavailableArtwork = false)
    {
        ValidateStructure(context);
        if (string.IsNullOrWhiteSpace(Source))
            return;
        ImageAssetPresentationDefinition.ResolveReference(dashboardBaseDirectory, ImageAssetSourceType.File, Source, context, allowUnavailableArtwork);
    }
}
