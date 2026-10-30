using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

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
    public string OverflowMode { get; set; } = global::PinkieSysMon.ValueOverflowContract.None;

    [JsonPropertyName("scrollSpeed")]
    public float ScrollSpeed { get; set; }

    [JsonPropertyName("bumpPauseMs")]
    public int BumpPauseMs { get; set; } = 500;

    internal void Validate(string context)
    {
        if (string.IsNullOrWhiteSpace(FontFamily))
            throw new InvalidDataException($"{context} FontFamily is required.");
        if (!float.IsFinite(FontSize) || FontSize <= 0f)
            throw new InvalidDataException($"{context} FontSize must be finite and greater than zero.");
        if (FontWeight is < 1 or > 1000)
            throw new InvalidDataException($"{context} FontWeight must be in the range 1..1000.");
        if (!float.IsFinite(OutlineWidth) || OutlineWidth < 0f)
            throw new InvalidDataException($"{context} OutlineWidth must be finite and >= 0.");
        _ = global::PinkieSysMon.ColorParser.Parse(OutlineColor);

        if (!global::PinkieSysMon.ValueOverflowContract.SupportedModes.Contains(OverflowMode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} has invalid OverflowMode '{OverflowMode}'.");
        if (!float.IsFinite(ScrollSpeed) || ScrollSpeed < 0f)
            throw new InvalidDataException($"{context} ScrollSpeed must be finite and >= 0.");
        if (BumpPauseMs < 0)
            throw new InvalidDataException($"{context} BumpPauseMs cannot be negative.");
    }
}
