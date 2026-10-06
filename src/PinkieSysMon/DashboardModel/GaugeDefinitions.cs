using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class GaugeTrackDefinition
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("thickness")]
    public float Thickness { get; set; } = 20f;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#00000000";

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; set; } = "#00000000";

    [JsonPropertyName("borderWidth")]
    public float BorderWidth { get; set; }

    [JsonPropertyName("cornerRadius")]
    public float CornerRadius { get; set; }

    internal void Validate(string context)
    {
        if (!float.IsFinite(Thickness) || Thickness < 0f)
            throw new InvalidDataException($"{context} Thickness must be finite and >= 0.");
        if (!float.IsFinite(BorderWidth) || BorderWidth < 0f)
            throw new InvalidDataException($"{context} BorderWidth must be finite and >= 0.");
        if (!float.IsFinite(CornerRadius) || CornerRadius < 0f)
            throw new InvalidDataException($"{context} CornerRadius must be finite and >= 0.");
        _ = global::PinkieSysMon.ColorParser.Parse(BackgroundColor);
        _ = global::PinkieSysMon.ColorParser.Parse(BorderColor);
        if (Enabled && Thickness <= 0f)
            throw new InvalidDataException($"{context} Thickness must be greater than zero when Enabled is true.");
    }
}

internal sealed class GaugePointerDefinition
{
    [JsonPropertyName("length")]
    public float Length { get; set; }

    [JsonPropertyName("thickness")]
    public float Thickness { get; set; } = 2f;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    internal void Validate(string context)
    {
        if (!float.IsFinite(Length) || Length < 0f)
            throw new InvalidDataException($"{context} Length must be finite and >= 0.");
        if (!float.IsFinite(Thickness) || Thickness < 0f)
            throw new InvalidDataException($"{context} Thickness must be finite and >= 0.");
        if (Length > 0f && Thickness <= 0f)
            throw new InvalidDataException($"{context} Thickness must be greater than zero when Length is positive.");
        _ = global::PinkieSysMon.ColorParser.Parse(Color);
    }
}

internal sealed class GaugeNeedleDefinition
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("thickness")]
    public float Thickness { get; set; } = 2f;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    [JsonPropertyName("startOffset")]
    public float StartOffset { get; set; }

    [JsonPropertyName("endOffset")]
    public float EndOffset { get; set; }

    [JsonPropertyName("pointer")]
    public GaugePointerDefinition Pointer { get; set; } = new();

    internal void Validate(float availableRadius, string context)
    {
        if (!float.IsFinite(Thickness) || Thickness < 0f)
            throw new InvalidDataException($"{context} Thickness must be finite and >= 0.");
        if (!float.IsFinite(StartOffset) || StartOffset < 0f)
            throw new InvalidDataException($"{context} StartOffset must be finite and >= 0.");
        if (!float.IsFinite(EndOffset) || EndOffset < 0f)
            throw new InvalidDataException($"{context} EndOffset must be finite and >= 0.");
        _ = global::PinkieSysMon.ColorParser.Parse(Color);

        if (Pointer is null)
            throw new InvalidDataException($"{context} Pointer must not be null.");
        Pointer.Validate($"{context} Pointer");

        if (Enabled)
        {
            if (Thickness <= 0f)
                throw new InvalidDataException($"{context} Thickness must be greater than zero when Enabled is true.");
            if (StartOffset + EndOffset >= availableRadius)
                throw new InvalidDataException($"{context} offsets must leave a positive visible needle length.");
        }
    }
}
