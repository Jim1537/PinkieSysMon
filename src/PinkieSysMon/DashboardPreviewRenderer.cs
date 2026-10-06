using PinkieSysMon.Widgets;
using SkiaSharp;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal sealed class DashboardPreviewRenderer : IDisposable
{
    private readonly CanonicalDashboard.DashboardDefinition _definition;
    private readonly SKSurface _surface;
    private readonly SKImageInfo _pixelInfo;
    private readonly TypefaceCache _typefaces;
    private readonly WidgetRenderContext _renderContext;
    private readonly ImageAssetCache _images;
    private readonly IconAssetCache _icons;
    private readonly RenderEntry[] _renderEntries;
    private readonly SKColor _backgroundColor;
    private readonly ImageVisual? _backgroundImage;
    private readonly ImageVisual? _foregroundImage;

    public DashboardPreviewRenderer(CanonicalDashboard.DashboardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _backgroundColor = ColorParser.Parse(_definition.Canvas.BackgroundColor);

        var logical = FrameGeometry.GetLogicalSize(
            _definition.Canvas.Width,
            _definition.Canvas.Height,
            _definition.Canvas.Orientation);
        Width = logical.Width;
        Height = logical.Height;
        _pixelInfo = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        SKSurface? surface = null;
        TypefaceCache? typefaces = null;
        WidgetRenderContext? renderContext = null;
        ImageAssetCache? images = null;
        IconAssetCache? icons = null;
        ImageVisual? backgroundImage = null;
        ImageVisual? foregroundImage = null;
        Dictionary<string, IWidgetRenderer>? renderers = null;

        try
        {
            surface = SKSurface.Create(_pixelInfo)
                ?? throw new InvalidOperationException("Failed to create editor preview surface.");
            typefaces = new TypefaceCache(_definition.BaseDirectory, _definition.Widgets);
            renderContext = new WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                RenderQuality.Antialias,
                typefaces);
            images = new ImageAssetCache(_definition.BaseDirectory);
            icons = new IconAssetCache(_definition.BaseDirectory);
            images.Preload(_definition.Widgets);
            icons.Preload(_definition.Widgets);

            backgroundImage = CreateCanvasImageVisual(images, icons, _definition.Canvas.BackgroundImage);
            foregroundImage = CreateCanvasImageVisual(images, icons, _definition.Canvas.ForegroundImage);

            renderers = WidgetRendererRegistry.Create(images, icons);
            var renderEntries = _definition.Widgets
                .Select((widget, index) => new { widget, index })
                .OrderBy(x => x.widget.Z)
                .ThenBy(x => x.index)
                .Select(x =>
                {
                    if (!renderers.TryGetValue(x.widget.Type, out var renderer))
                        throw new InvalidDataException($"No renderer registered for widget type '{x.widget.Type}'.");
                    return new RenderEntry(x.widget, renderer);
                })
                .ToArray();

            _surface = surface;
            _typefaces = typefaces;
            _renderContext = renderContext;
            _images = images;
            _icons = icons;
            _backgroundImage = backgroundImage;
            _foregroundImage = foregroundImage;
            _renderEntries = renderEntries;

            surface = null;
            typefaces = null;
            renderContext = null;
            images = null;
            icons = null;
            backgroundImage = null;
            foregroundImage = null;
            renderers = null;
        }
        catch
        {
            if (renderers is not null)
            {
                foreach (var disposable in renderers.Values.OfType<IDisposable>().Distinct())
                    disposable.Dispose();
            }
            foregroundImage?.Dispose();
            backgroundImage?.Dispose();
            icons?.Dispose();
            images?.Dispose();
            renderContext?.Dispose();
            typefaces?.Dispose();
            surface?.Dispose();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public bool RequiresFrequentRefresh => _images.HasAnimatedAssets || _icons.HasAnimatedAssets || HasAnimatedValueOverflow();
    public int RecommendedRefreshIntervalMs => HasAnimatedValueOverflow() ? 50 : (_images.HasAnimatedAssets || _icons.HasAnimatedAssets) ? 250 : 1000;

    private bool HasAnimatedValueOverflow() =>
        _definition.Widgets.Any(widget =>
            widget.Width > 0f &&
            (widget is CanonicalDashboard.ValueWidgetDefinition value
                ? ValueOverflowContract.IsAnimated(value.TextPresentation.OverflowMode)
                : widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget &&
                  stateWidget.Profiles.Values.Any(profile =>
                      profile.UsesValueContent &&
                      ValueOverflowContract.IsAnimated(profile.TextPresentation.OverflowMode))));

    private static ImageVisual? CreateCanvasImageVisual(
        ImageAssetCache images,
        IconAssetCache icons,
        CanonicalDashboard.CanvasImageLayerDefinition layer)
    {
        if (string.IsNullOrWhiteSpace(layer.Asset.Source))
            return null;

        return ImageVisual.Create(
            images,
            icons,
            layer.Asset.SourceType,
            layer.Asset.Source,
            layer.Asset.Fit,
            layer.Asset.Loop,
            layer.Opacity,
            layer.Color,
            RenderQuality.Antialias);
    }

    private void DrawCanvasImage(SKCanvas canvas, ImageVisual? image)
    {
        image?.Draw(
            canvas,
            new SKRect(0, 0, Width, Height),
            RenderQuality.Antialias);
    }

    public void RenderToBgra(
        IReadOnlyDictionary<string, object?> metrics,
        IntPtr destination,
        int destinationRowBytes)
    {
        if (destination == IntPtr.Zero)
            throw new ArgumentException("Preview destination buffer is null.", nameof(destination));
        if (destinationRowBytes < _pixelInfo.RowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationRowBytes),
                $"Preview destination row stride {destinationRowBytes} is smaller than required {_pixelInfo.RowBytes} bytes.");
        }

        RenderFrame(metrics);
        if (!_surface.ReadPixels(_pixelInfo, destination, destinationRowBytes, 0, 0))
            throw new InvalidOperationException("Skia failed to copy the editor preview frame to the destination bitmap.");
    }

    public byte[] RenderToPng(IReadOnlyDictionary<string, object?> metrics)
    {
        RenderFrame(metrics);
        using var image = _surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia failed to encode the dashboard snapshot as PNG.");
        return data.ToArray();
    }

    private void RenderFrame(IReadOnlyDictionary<string, object?> metrics)
    {
        var canvas = _surface.Canvas;
        canvas.Clear(_backgroundColor);
        DrawCanvasImage(canvas, _backgroundImage);

        _renderContext.UpdateMetrics(metrics);
        foreach (var entry in _renderEntries)
            WidgetTransform.Render(canvas, entry.Widget, _renderContext, entry.Renderer);

        DrawCanvasImage(canvas, _foregroundImage);
        canvas.Flush();
    }

    public SKPoint[] GetWidgetOutline(CanonicalDashboard.WidgetDefinition widget, IReadOnlyDictionary<string, object?> metrics)
    {
        _renderContext.UpdateMetrics(metrics);
        return WidgetGeometry.GetRotatedVisualCorners(widget, _renderContext);
    }

    public bool HitTestWidget(CanonicalDashboard.WidgetDefinition widget, IReadOnlyDictionary<string, object?> metrics, float x, float y)
    {
        _renderContext.UpdateMetrics(metrics);
        return WidgetGeometry.HitTest(widget, _renderContext, x, y);
    }

    public SKRect GetWidgetLayoutBounds(CanonicalDashboard.WidgetDefinition widget, IReadOnlyDictionary<string, object?> metrics)
    {
        _renderContext.UpdateMetrics(metrics);
        return WidgetGeometry.GetLayoutBounds(widget, _renderContext);
    }

    public void InvalidateWidgetGeometry(CanonicalDashboard.WidgetDefinition widget)
    {
        _renderContext.InvalidateGeometry(widget);
        foreach (var entry in _renderEntries)
        {
            if (!ReferenceEquals(entry.Widget, widget))
                continue;

            entry.Renderer.InvalidateGeometry(widget);
            break;
        }
    }

    private sealed record RenderEntry(CanonicalDashboard.WidgetDefinition Widget, IWidgetRenderer Renderer);

    public void Dispose()
    {
        foreach (var disposable in _renderEntries.Select(x => x.Renderer).OfType<IDisposable>().Distinct())
            disposable.Dispose();
        _foregroundImage?.Dispose();
        _backgroundImage?.Dispose();
        _icons.Dispose();
        _images.Dispose();
        _renderContext.Dispose();
        _typefaces.Dispose();
        _surface.Dispose();
    }
}
