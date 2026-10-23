using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class GraphWidgetDefinition : NumericWidgetDefinition
{
    [JsonIgnore] public override string Type => global::PinkieSysMon.WidgetTypeContract.Graph;
    [JsonPropertyName("mode")] public string Mode { get; set; } = "Line";
    [JsonPropertyName("windowSeconds")] public double WindowSeconds { get; set; } = 60;
    [JsonPropertyName("scaleMode")] public string ScaleMode { get; set; } = "Auto";
    [JsonPropertyName("strokeWidth")] public float StrokeWidth { get; set; } = 2;
    [JsonPropertyName("fillEnabled")] public bool FillEnabled { get; set; }
    [JsonPropertyName("fillOpacity")] public float FillOpacity { get; set; } = .25f;
    [JsonPropertyName("image")] public CanvasImageLayerDefinition Image { get; set; } = new();

    [JsonIgnore] public bool AutoScale => ScaleMode == "Auto";

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidatePositiveGeometry();
        ValidateNumeric(validateThresholds: true, autoScale: AutoScale);
        if (Mode is not ("Line" or "Columns") || ScaleMode is not ("Auto" or "Fixed"))
            throw new InvalidDataException($"Graph '{Id}' requires Line/Columns and Auto/Fixed modes.");
        RequireFinite(WindowSeconds, nameof(WindowSeconds));
        RequireFinite(StrokeWidth, nameof(StrokeWidth));
        RequireFinite(FillOpacity, nameof(FillOpacity));
        if (WindowSeconds is < 1 or > global::PinkieSysMon.MetricHistoryStore.MaximumWindowSeconds)
            throw new InvalidDataException($"Graph '{Id}' History Window must be between 1 and 600 seconds.");
        if (StrokeWidth <= 0 || FillOpacity is < 0 or > 1)
            throw new InvalidDataException($"Graph '{Id}' requires positive Stroke Width and Fill Opacity in 0..1.");
        if (Image is null) throw new InvalidDataException($"Graph '{Id}' Image must not be null.");
        Image.Validate(dashboardBaseDirectory, "Graph background");
    }
}
