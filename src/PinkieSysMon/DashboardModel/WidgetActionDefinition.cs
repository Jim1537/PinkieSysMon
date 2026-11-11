using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

// Widget-local data; the Runtime Desktop session owns live input and dispatch.
internal sealed class WidgetActionDefinition
{
    public const string None = "None";
    public const string DoubleClick = "Double";
    public const string SingleClick = "Single";

    [JsonPropertyName("activation")]
    public string Activation { get; set; } = None;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsActive => Activation != None && !string.IsNullOrWhiteSpace(Target);

    public void Validate(string? widgetId)
    {
        if (Activation is not (None or SingleClick or DoubleClick))
            throw new InvalidDataException($"Widget '{widgetId}' has invalid Action Activation '{Activation}'.");
        if (Target is null || Arguments is null)
            throw new InvalidDataException($"Widget '{widgetId}' Action Target and Arguments must not be null.");
    }
}
