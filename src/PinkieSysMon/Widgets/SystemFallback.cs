using SkiaSharp;

namespace PinkieSysMon.Widgets;

// System-wide presentation of unavailable telemetry. Never publish or persist this marker.
internal static class SystemFallback
{
    public const string Text = "--";
    public const string Icon = "lucide:image-off";

    public static void DrawGraphic(
        SKCanvas canvas,
        IconAssetCache icons,
        SKRect content,
        string color,
        bool antialias)
    {
        if (content.Width <= 0f || content.Height <= 0f)
            return;

        // Derived from the content area; no user-facing fallback size or style property.
        var side = Math.Min(64f, Math.Min(content.Width, content.Height) * 0.6f);
        if (side < 2f)
            return;

        var left = content.MidX - side / 2f;
        var top = content.MidY - side / 2f;
        icons.Get(Icon).Draw(canvas, new SKRect(left, top, left + side, top + side),
            ColorParser.Parse(color), 1f, StateVisualProfileContract.FitContain,
            loop: true, antialias: antialias);
    }
}
