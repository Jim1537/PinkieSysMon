using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class ShortcutWidgetDefinition : WidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Shortcut;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("activation")]
    public string Activation { get; set; } = ShortcutContract.DoubleClick;

    [JsonPropertyName("asset")]
    public ImageAssetPresentationDefinition Asset { get; set; } = new()
    {
        SourceType = ShortcutContract.AutoSource,
        Fit = StateVisualProfileContract.FitContain
    };

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("labelPosition")]
    public string LabelPosition { get; set; } = ShortcutContract.Bottom;

    [JsonPropertyName("gap")]
    public float Gap { get; set; } = 4f;

    [JsonPropertyName("labelColor")]
    public string LabelColor { get; set; } = "#FFFFFFFF";

    [JsonPropertyName("textPresentation")]
    public TextPresentationDefinition TextPresentation { get; set; } = new()
    {
        OverflowMode = ValueOverflowContract.Clip
    };

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidatePositiveGeometry();
        if (Target is null || Arguments is null || Label is null)
            throw new InvalidDataException($"Shortcut widget '{Id}' Target, Arguments and Label must not be null.");
        if (!ShortcutContract.Activations.Contains(Activation, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Shortcut widget '{Id}' has invalid Activation '{Activation}'.");
        if (!ShortcutContract.LabelPositions.Contains(LabelPosition, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Shortcut widget '{Id}' has invalid LabelPosition '{LabelPosition}'.");
        RequireFinite(Gap, nameof(Gap));
        if (!float.IsFinite(Opacity) || Opacity is < 0f or > 1f)
            throw new InvalidDataException($"Shortcut widget '{Id}' Opacity must be in the range 0..1.");
        _ = ColorParser.Parse(LabelColor);
        if (Asset is null || TextPresentation is null)
            throw new InvalidDataException($"Shortcut widget '{Id}' requires Asset and TextPresentation.");
        // Only this widget admits Auto. Other image consumers keep file/icon validation.
        Asset.ValidateActiveSource(dashboardBaseDirectory, $"Shortcut widget '{Id}' Asset", allowShellIcon: true);
        TextPresentation.Validate($"Shortcut widget '{Id}' TextPresentation");
        if (ValueOverflowContract.Normalize(TextPresentation.OverflowMode) == ValueOverflowContract.None)
            throw new InvalidDataException($"Shortcut widget '{Id}' label requires fixed-width overflow.");
    }
}
