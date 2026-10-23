using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class ShortcutWidgetRenderer : IWidgetRenderer, IWidgetOverlayRenderer, IDisposable
{
    private readonly ImageAssetCache _images;
    private readonly IconAssetCache _icons;
    private readonly ShellIconCache _shell = new();
    private readonly TextContentRenderer _text = new();
    private readonly Dictionary<C.ShortcutWidgetDefinition, ImageVisual> _compiled = new();
    private readonly HashSet<C.ShortcutWidgetDefinition> _pending = new();

    public ShortcutWidgetRenderer(ImageAssetCache images, IconAssetCache icons) { _images = images; _icons = icons; }
    public string Type => WidgetTypeContract.Shortcut;
    // Remains true until a render consumes the result, including the final completed frame.
    internal bool HasPendingResources => _pending.Count > 0;

    public void Render(SKCanvas canvas, C.WidgetDefinition widget, WidgetRenderContext context)
    {
        var shortcut = (C.ShortcutWidgetDefinition)widget;
        if (!_compiled.TryGetValue(shortcut, out var visual))
        {
            // No custom asset also means Auto, without reinterpreting Target as a dashboard path.
            if (ShortcutContract.IsAuto(shortcut.Asset.SourceType) || string.IsNullOrWhiteSpace(shortcut.Asset.Source))
            {
                var image = _shell.Get(shortcut.Target, out var pending);
                if (pending) _pending.Add(shortcut); else _pending.Remove(shortcut);
                if (image is null) return;
                visual = ImageVisual.CreateImage(image, shortcut.Asset.Fit, shortcut.Asset.Loop, shortcut.Opacity, context.Antialias);
            }
            else
                visual = ImageVisual.Create(_images, _icons, shortcut.Asset.SourceType, shortcut.Asset.Source!,
                    shortcut.Asset.Fit, shortcut.Asset.Loop, shortcut.Opacity, shortcut.Color, context.Antialias);
            _compiled.Add(shortcut, visual);
        }
        visual.Draw(canvas, new SKRect(shortcut.X, shortcut.Y, shortcut.X + shortcut.Width, shortcut.Y + shortcut.Height), context.Antialias);
    }

    public void RenderOverlay(SKCanvas canvas, C.WidgetDefinition widget, WidgetRenderContext context)
    {
        var shortcut = (C.ShortcutWidgetDefinition)widget;
        if (ShortcutLabelLayout.GetBounds(shortcut, context) is not { } bounds) return;
        _text.Render(canvas, shortcut, context, shortcut.Label, shortcut.LabelColor,
            presentationOverride: shortcut.TextPresentation.ToRenderProfile(), boundsOverride: bounds);
    }

    public void Dispose()
    {
        foreach (var visual in _compiled.Values) visual.Dispose();
        _compiled.Clear();
        _text.Dispose();
        _shell.Dispose();
    }
}
