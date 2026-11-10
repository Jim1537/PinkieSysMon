using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal static class WidgetBorderStyle
{
    public const string Solid = "solid";
    public const string Dotted = "dotted";
}

internal readonly record struct WidgetPadding(
    [property: JsonPropertyName("top")] float Top,
    [property: JsonPropertyName("right")] float Right,
    [property: JsonPropertyName("bottom")] float Bottom,
    [property: JsonPropertyName("left")] float Left)
{
    internal double GetInscribedRadius(float width, float height)
    {
        var innerWidth = (double)width - Left - Right;
        var innerHeight = (double)height - Top - Bottom;
        return Math.Max(0d, Math.Min(innerWidth, innerHeight)) / 2d;
    }

    internal void Validate(string? widgetId)
    {
        if (!IsValid(Top) || !IsValid(Right) || !IsValid(Bottom) || !IsValid(Left))
            throw new InvalidDataException($"Widget '{widgetId}' Padding must contain four finite, nonnegative distances.");
    }

    private static bool IsValid(float value) => float.IsFinite(value) && value >= 0f;
}
