using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class ImageWidgetRenderer : IWidgetRenderer, IDisposable
{
    private readonly ImageAssetCache _images;
    private readonly IconAssetCache _icons;
    private readonly Dictionary<CanonicalWidgetDefinition, ImageVisual> _compiled = new();

    public ImageWidgetRenderer(ImageAssetCache images, IconAssetCache icons)
    {
        _images = images;
        _icons = icons;
    }

    public string Type => WidgetTypeContract.Image;

    public void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var image = widget as CanonicalDashboard.ImageWidgetDefinition
            ?? throw new InvalidDataException($"Image renderer received widget type '{widget.Type}'.");
        var compiled = GetCompiled(image, context.Antialias);
        var bounds = new SKRect(image.X, image.Y, image.X + image.Width, image.Y + image.Height);
        compiled.Draw(canvas, bounds, context.Antialias);
    }

    private ImageVisual GetCompiled(CanonicalDashboard.ImageWidgetDefinition widget, bool antialias)
    {
        if (_compiled.TryGetValue(widget, out var compiled))
            return compiled;

        compiled = ImageVisual.Create(
            _images,
            _icons,
            widget.Asset.SourceType,
            widget.Asset.Source!,
            widget.Asset.Fit,
            widget.Asset.Loop,
            widget.Opacity,
            widget.Color,
            antialias);
        _compiled.Add(widget, compiled);
        return compiled;
    }

    public void Dispose()
    {
        foreach (var compiled in _compiled.Values)
            compiled.Dispose();
        _compiled.Clear();
    }
}
