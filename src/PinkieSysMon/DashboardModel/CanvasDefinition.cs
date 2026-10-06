using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class CanvasDefinition
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = global::PinkieSysMon.FrameGeometry.DefaultNativeWidth;

    [JsonPropertyName("height")]
    public int Height { get; set; } = global::PinkieSysMon.FrameGeometry.DefaultNativeHeight;

    [JsonPropertyName("orientation")]
    public int Orientation { get; set; }

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#000000";

    [JsonPropertyName("backgroundImage")]
    public CanvasImageLayerDefinition BackgroundImage { get; set; } = new();

    [JsonPropertyName("foregroundImage")]
    public CanvasImageLayerDefinition ForegroundImage { get; set; } = new();

    internal void Validate(string dashboardBaseDirectory)
    {
        _ = global::PinkieSysMon.FrameGeometry.GetLogicalSize(Width, Height, Orientation);
        _ = global::PinkieSysMon.ColorParser.Parse(BackgroundColor);
        if (BackgroundImage is null || ForegroundImage is null)
            throw new InvalidDataException("Canvas BackgroundImage and ForegroundImage definitions must not be null.");
        BackgroundImage.Validate(dashboardBaseDirectory, "background");
        ForegroundImage.Validate(dashboardBaseDirectory, "foreground");
    }
}
