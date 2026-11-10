using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class StateVisualProfileDefinition
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = StateContentType.Image;

    [JsonPropertyName("asset")]
    public ImageAssetPresentationDefinition Asset { get; set; } = new()
    {
        SourceType = ImageAssetSourceType.Icon,
        Fit = global::PinkieSysMon.StateVisualProfileContract.FitContain
    };

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("iconOpacity")]
    public float IconOpacity { get; set; } = 1f;

    // Source compatibility for existing profile factories; Value/File never use it.
    [JsonIgnore]
    public float Opacity { get => IconOpacity; set => IconOpacity = value; }

    [JsonPropertyName("textPresentation")]
    public TextPresentationDefinition TextPresentation { get; set; } = new();

    internal bool UsesValueContent => StateContentType.IsValue(ContentType);

    internal void Validate(string dashboardBaseDirectory, string context, bool allowUnavailableArtwork = false)
    {
        if (!StateContentType.IsSupported(ContentType))
            throw new InvalidDataException($"{context} ContentType must be image or value.");
        if (Asset is null)
            throw new InvalidDataException($"{context} Asset must not be null.");
        if (TextPresentation is null)
            throw new InvalidDataException($"{context} TextPresentation must not be null.");
        if (!float.IsFinite(IconOpacity) || IconOpacity is < 0f or > 1f)
            throw new InvalidDataException($"{context} Opacity must be a finite value in the range 0..1.");
        if (!string.IsNullOrWhiteSpace(Color))
            _ = global::PinkieSysMon.ColorParser.Parse(Color);

        Asset.ValidateStructure($"{context} Asset");
        TextPresentation.Validate($"{context} TextPresentation");

        if (StateContentType.IsImage(ContentType))
            Asset.ValidateActiveSource(dashboardBaseDirectory, $"{context} Asset", allowUnavailableArtwork: allowUnavailableArtwork);
    }
}
