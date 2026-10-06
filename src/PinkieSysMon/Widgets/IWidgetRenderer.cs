using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon.Widgets;

internal interface IWidgetRenderer
{
    string Type { get; }
    void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context);

    // Editor geometry edits can change compiled size-dependent resources without rebuilding
    // the entire preview renderer. Stateless renderers need no action.
    void InvalidateGeometry(CanonicalWidgetDefinition widget)
    {
    }
}

// Renderers whose visible output can be reduced to a stable content key may opt in to the
// widget-shadow composite cache. Returning false means the visual is time-dependent (for
// example an animation) and must remain live.
internal interface IWidgetShadowCacheKeyProvider
{
    bool TryGetShadowContentKey(
        global::PinkieSysMon.DashboardModel.WidgetDefinition widget,
        WidgetRenderContext context,
        out WidgetRenderContext.ShadowContentKey key);
}
