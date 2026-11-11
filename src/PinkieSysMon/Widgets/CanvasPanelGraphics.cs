using PinkieSysMon.DashboardModel;
using SkiaSharp;

namespace PinkieSysMon.Widgets;

// Canvas and widgets use the same Panel visuals, border paths, clipping and
// alpha rules. The separate Canvas lifetime never fabricates a WidgetDefinition.
internal sealed class CanvasPanelGraphics : IDisposable
{
    private readonly CanvasDefinition _root;
    private readonly WidgetRenderContext.ContainerRenderResources _resources;
    private readonly SKRect _bounds;
    private readonly float _radius;
    private readonly bool _antialias;

    internal CanvasPanelGraphics(CanvasDefinition root, int width, int height,
        ImageAssetCache images, IconAssetCache icons, bool antialias)
    {
        _root = root;
        _bounds = new SKRect(0f, 0f, width, height);
        _radius = Math.Min(Math.Max(0f, root.CornerRadius), Math.Min(width, height) / 2f);
        _antialias = antialias;
        _resources = WidgetRenderContext.ContainerRenderResources.Create(root, antialias, images, icons);
    }

    internal void DrawBackground(SKCanvas canvas) =>
        WidgetEffects.DrawContainerBackground(canvas, _resources, _bounds, _radius, _antialias);

    // The root clips all children (including their shadows), but the border and
    // foreground remain above the children in the canonical drawing order.
    internal void ClipChildren(SKCanvas canvas)
    {
        if (_radius <= 0f)
        {
            canvas.ClipRect(_bounds, SKClipOperation.Intersect, _antialias);
            return;
        }

        _resources.ClipRoundRect.SetRect(_bounds, _radius, _radius);
        canvas.ClipRoundRect(_resources.ClipRoundRect, SKClipOperation.Intersect, _antialias);
    }

    internal void DrawOverlay(SKCanvas canvas)
    {
        WidgetEffects.DrawContainerBorder(canvas, _root, _resources, _bounds, _radius);
        if (_resources.ForegroundPaint is { } fill)
        {
            if (_radius > 0f)
                canvas.DrawRoundRect(_bounds, _radius, _radius, fill);
            else
                canvas.DrawRect(_bounds, fill);
        }
        WidgetEffects.DrawPanelVisual(canvas, _resources.ForegroundVisual, _bounds, _radius, _antialias);
    }

    public void Dispose() => _resources.Dispose();
}
