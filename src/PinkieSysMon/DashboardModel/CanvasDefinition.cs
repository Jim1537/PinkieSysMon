using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

// The Canvas is the dashboard root, not an ordinary widget. It shares the
// graphical Panel contract without inventing widget identity or transforms.
internal sealed class CanvasDefinition
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = global::PinkieSysMon.FrameGeometry.DefaultNativeWidth;

    [JsonPropertyName("height")]
    public int Height { get; set; } = global::PinkieSysMon.FrameGeometry.DefaultNativeHeight;

    [JsonPropertyName("orientation")]
    public int Orientation { get; set; }

    [JsonPropertyName("panelBackground")]
    public WidgetPanelLayerDefinition PanelBackground { get; set; } = new()
    {
        Mode = WidgetPanelLayerMode.Color,
        Color = "#FF000000"
    };

    [JsonPropertyName("panelForeground")]
    public WidgetPanelLayerDefinition PanelForeground { get; set; } = new();

    // Source compatibility only: schema 30 persists the Panel layer, not a
    // second Canvas background-color owner.
    [JsonIgnore]
    public string BackgroundColor
    {
        get => PanelBackground.Color;
        set
        {
            PanelBackground.Color = value;
            PanelBackground.Mode = WidgetPanelLayerMode.Color;
        }
    }

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; set; } = "#00000000";

    [JsonPropertyName("borderWidth")]
    public float BorderWidth { get; set; }

    [JsonPropertyName("borderStyle")]
    public string BorderStyle { get; set; } = WidgetBorderStyle.Solid;

    [JsonPropertyName("borderTop")]
    public bool BorderTop { get; set; } = true;

    [JsonPropertyName("borderRight")]
    public bool BorderRight { get; set; } = true;

    [JsonPropertyName("borderBottom")]
    public bool BorderBottom { get; set; } = true;

    [JsonPropertyName("borderLeft")]
    public bool BorderLeft { get; set; } = true;

    [JsonPropertyName("cornerRadius")]
    public float CornerRadius { get; set; }

    [JsonPropertyName("shadowEnabled")]
    public bool ShadowEnabled { get; set; }

    [JsonPropertyName("shadowOffsetX")]
    public float ShadowOffsetX { get; set; } = 2f;

    [JsonPropertyName("shadowOffsetY")]
    public float ShadowOffsetY { get; set; } = 2f;

    [JsonPropertyName("shadowBlur")]
    public float ShadowBlur { get; set; } = 4f;

    [JsonPropertyName("shadowOpacity")]
    public float ShadowOpacity { get; set; } = 0.5f;

    [JsonPropertyName("shadowColor")]
    public string ShadowColor { get; set; } = "#FF000000";

    internal void Validate(string dashboardBaseDirectory, bool allowUnavailableArtwork = false)
    {
        _ = global::PinkieSysMon.FrameGeometry.GetLogicalSize(Width, Height, Orientation);
        if (PanelBackground is null || PanelForeground is null)
            throw new InvalidDataException("Canvas panel layers must not be null.");
        PanelBackground.Validate(dashboardBaseDirectory, "Canvas Panel Background", allowUnavailableArtwork);
        PanelForeground.Validate(dashboardBaseDirectory, "Canvas Panel Foreground", allowUnavailableArtwork);

        if (BorderStyle is not (WidgetBorderStyle.Solid or WidgetBorderStyle.Dotted))
            throw new InvalidDataException("Canvas Border Style must be solid or dotted.");
        if (!float.IsFinite(BorderWidth) || BorderWidth < 0f)
            throw new InvalidDataException("Canvas Border Width must be finite and nonnegative.");
        if (!float.IsFinite(CornerRadius) || CornerRadius < 0f)
            throw new InvalidDataException("Canvas Corner Radius must be finite and nonnegative.");
        if (!float.IsFinite(ShadowBlur) || ShadowBlur < 0f ||
            !float.IsFinite(ShadowOffsetX) || !float.IsFinite(ShadowOffsetY) ||
            !float.IsFinite(ShadowOpacity) || ShadowOpacity is < 0f or > 1f)
            throw new InvalidDataException("Canvas Shadow configuration is invalid.");
        _ = global::PinkieSysMon.ColorParser.Parse(BorderColor);
        _ = global::PinkieSysMon.ColorParser.Parse(ShadowColor);
    }
}
