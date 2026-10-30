using SkiaSharp;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class StateIconVisualCache : IDisposable
{
    private readonly ImageAssetCache _images;
    private readonly IconAssetCache _icons;
    private readonly Dictionary<VisualKey, ImageVisual> _compiled = new();

    public StateIconVisualCache(ImageAssetCache images, IconAssetCache icons)
    {
        _images = images;
        _icons = icons;
    }

    public bool UsesActivationRelativePlayback(StateVisualProfile profile) => IsAnimated(profile);

    public bool IsAnimated(StateVisualProfile profile) =>
        ImageVisual.IsStateSourceAnimated(_images, _icons, profile);

    public void Draw(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        StateVisualProfile profile,
        string fit = "contain",
        bool loop = true)
    {
        if (string.IsNullOrWhiteSpace(profile.Source))
            return;

        var bounds = GetBounds(widget);
        GetOrCreate(widget, context, profile, fit, loop).Draw(canvas, bounds, context.Antialias);
    }

    public void DrawAtElapsed(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        StateVisualProfile profile,
        long elapsedMs,
        string fit = "contain",
        bool loop = true)
    {
        if (string.IsNullOrWhiteSpace(profile.Source))
            return;

        var bounds = GetBounds(widget);
        GetOrCreate(widget, context, profile, fit, loop)
            .DrawAtElapsed(canvas, bounds, context.Antialias, elapsedMs);
    }

    private ImageVisual GetOrCreate(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        StateVisualProfile profile,
        string fit,
        bool loop)
    {
        var key = new VisualKey(
            widget,
            widget.Color,
            profile,
            fit ?? string.Empty,
            loop,
            context.Antialias);
        if (_compiled.TryGetValue(key, out var visual))
            return visual;

        visual = ImageVisual.CreateState(
            _images,
            _icons,
            widget,
            profile,
            fit,
            loop,
            context.Antialias);
        _compiled.Add(key, visual);
        return visual;
    }

    private static SKRect GetBounds(CanonicalWidgetDefinition widget) =>
        new(widget.X, widget.Y, widget.X + widget.Width, widget.Y + widget.Height);

    public void Dispose()
    {
        foreach (var visual in _compiled.Values)
            visual.Dispose();
        _compiled.Clear();
    }

    private readonly record struct VisualKey(
        CanonicalWidgetDefinition Widget,
        string FallbackColor,
        StateVisualProfile Profile,
        string Fit,
        bool Loop,
        bool Antialias);
}
