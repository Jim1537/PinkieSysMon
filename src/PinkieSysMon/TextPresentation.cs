using System.Text.Json.Serialization;

namespace PinkieSysMon;

internal readonly record struct TextPresentation(
    string FontFamily,
    float FontSize,
    int FontWeight,
    bool Italic,
    string Align,
    string VerticalAlign,
    float OutlineWidth,
    string OutlineColor,
    string OverflowMode,
    float ScrollSpeed,
    int BumpPauseMs);

internal sealed class TextPresentationDefinition
{
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Roboto";

    [JsonPropertyName("fontSize")]
    public float FontSize { get; set; } = 32f;

    [JsonPropertyName("fontWeight")]
    public int FontWeight { get; set; } = 400;

    [JsonPropertyName("italic")]
    public bool Italic { get; set; }

    [JsonPropertyName("align")]
    public string Align { get; set; } = "left";

    [JsonPropertyName("verticalAlign")]
    public string VerticalAlign { get; set; } = "baseline";

    [JsonPropertyName("outlineWidth")]
    public float OutlineWidth { get; set; }

    [JsonPropertyName("outlineColor")]
    public string OutlineColor { get; set; } = "#FF000000";

    [JsonPropertyName("overflowMode")]
    public string OverflowMode { get; set; } = ValueOverflowContract.None;

    [JsonPropertyName("scrollSpeed")]
    public float ScrollSpeed { get; set; }

    [JsonPropertyName("bumpPauseMs")]
    public int BumpPauseMs { get; set; } = 500;

    internal TextPresentation ToRenderProfile() => new(
        FontFamily,
        FontSize,
        FontWeight,
        Italic,
        Align,
        VerticalAlign,
        OutlineWidth,
        OutlineColor,
        OverflowMode,
        ScrollSpeed,
        BumpPauseMs);

    internal static TextPresentationDefinition FromWidget(WidgetDefinition widget) => new()
    {
        FontFamily = widget.FontFamily,
        FontSize = widget.FontSize,
        FontWeight = widget.FontWeight,
        Italic = widget.Italic,
        Align = widget.Align,
        VerticalAlign = widget.VerticalAlign,
        OutlineWidth = widget.OutlineWidth,
        OutlineColor = widget.OutlineColor,
        OverflowMode = widget.OverflowMode,
        ScrollSpeed = widget.ScrollSpeed,
        BumpPauseMs = widget.BumpPauseMs
    };
}

internal static class TextPresentationContract
{
    public static TextPresentation FromWidget(WidgetDefinition widget) => new(
        widget.FontFamily,
        widget.FontSize,
        widget.FontWeight,
        widget.Italic,
        widget.Align,
        widget.VerticalAlign,
        widget.OutlineWidth,
        widget.OutlineColor,
        widget.OverflowMode,
        widget.ScrollSpeed,
        widget.BumpPauseMs);

    public static TextPresentation GetBinaryState(WidgetDefinition widget, string stateKey)
    {
        widget.EnsureStateVisualProfiles();
        if (widget.Profiles is null ||
            !widget.Profiles.TryGetValue(stateKey, out var profile) ||
            profile?.Text is null)
        {
            throw new InvalidDataException(
                $"Binary widget '{widget.Id}' state '{stateKey}' requires a text presentation profile.");
        }

        return profile.Text.ToRenderProfile();
    }

    public static TextPresentation GetBinaryState(WidgetDefinition widget, StateVisualProfileDefinition profile)
    {
        if (profile.Text is null)
            throw new InvalidDataException($"Binary widget '{widget.Id}' requires a text presentation profile for every state.");

        return profile.Text.ToRenderProfile();
    }
}
