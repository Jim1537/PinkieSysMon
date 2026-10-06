using PinkieSysMon.Widgets;
using SkiaSharp;
using System.Diagnostics;
using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalCanvasImageLayerDefinition = global::PinkieSysMon.DashboardModel.CanvasImageLayerDefinition;

namespace PinkieSysMon;

internal sealed class DashboardRenderer : IDisposable
{
    private readonly CanonicalDashboardDefinition _definition;
    private readonly int _nativeWidth;
    private readonly int _nativeHeight;
    private readonly int _orientation;
    private readonly SKSurface _logicalSurface;
    private readonly SKSurface _wireSurface;
    private readonly TypefaceCache _typefaceCache;
    private readonly WidgetRenderContext _renderContext;
    private readonly ImageAssetCache _imageAssets;
    private readonly IconAssetCache _iconAssets;
    private readonly RenderEntry[] _renderEntries;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private readonly SKColor _backgroundColor;
    private readonly ImageVisual? _backgroundImage;
    private readonly ImageVisual? _foregroundImage;


    public DashboardRenderer(CanonicalDashboardDefinition definition)
    {
        _definition = definition;
        _nativeWidth = definition.Canvas.Width;
        _nativeHeight = definition.Canvas.Height;
        _orientation = definition.Canvas.Orientation;
        _backgroundColor = ColorParser.Parse(definition.Canvas.BackgroundColor);

        var widgets = definition.Widgets
            .Select((widget, index) => new { widget, index })
            .OrderBy(x => x.widget.Z)
            .ThenBy(x => x.index)
            .Select(x => x.widget)
            .ToArray();

        var logical = FrameGeometry.GetLogicalSize(_nativeWidth, _nativeHeight, _orientation);
        _logicalWidth = logical.Width;
        _logicalHeight = logical.Height;

        TypefaceCache? typefaceCache = null;
        WidgetRenderContext? renderContext = null;
        SKSurface? logicalSurface = null;
        SKSurface? wireSurface = null;
        ImageAssetCache? imageAssets = null;
        IconAssetCache? iconAssets = null;
        ImageVisual? backgroundImage = null;
        ImageVisual? foregroundImage = null;
        Dictionary<string, IWidgetRenderer>? renderers = null;

        try
        {
            typefaceCache = new TypefaceCache(definition.BaseDirectory, widgets);
            renderContext = new WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                RenderQuality.Antialias,
                typefaceCache);

            logicalSurface = SKSurface.Create(new SKImageInfo(logical.Width, logical.Height, SKColorType.Bgra8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Failed to create logical Skia surface.");
            wireSurface = SKSurface.Create(new SKImageInfo(_nativeWidth, _nativeHeight, SKColorType.Bgra8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Failed to create wire Skia surface.");

            imageAssets = new ImageAssetCache(definition.BaseDirectory);
            iconAssets = new IconAssetCache(definition.BaseDirectory);
            imageAssets.Preload(widgets);
            iconAssets.Preload(widgets);
            backgroundImage = CreateCanvasImageVisual(
                imageAssets, iconAssets, definition.Canvas.BackgroundImage);
            foregroundImage = CreateCanvasImageVisual(
                imageAssets, iconAssets, definition.Canvas.ForegroundImage);

            renderers = WidgetRendererRegistry.Create(imageAssets, iconAssets);
            var renderEntries = widgets.Select(widget =>
            {
                if (!renderers.TryGetValue(widget.Type, out var renderer))
                    throw new InvalidDataException($"No renderer registered for widget type '{widget.Type}'.");
                return new RenderEntry(widget, renderer);
            }).ToArray();

            _typefaceCache = typefaceCache;
            _renderContext = renderContext;
            _logicalSurface = logicalSurface;
            _wireSurface = wireSurface;
            _imageAssets = imageAssets;
            _iconAssets = iconAssets;
            _backgroundImage = backgroundImage;
            _foregroundImage = foregroundImage;
            _renderEntries = renderEntries;

            typefaceCache = null;
            renderContext = null;
            logicalSurface = null;
            wireSurface = null;
            imageAssets = null;
            iconAssets = null;
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
            iconAssets?.Dispose();
            imageAssets?.Dispose();
            wireSurface?.Dispose();
            logicalSurface?.Dispose();
            renderContext?.Dispose();
            typefaceCache?.Dispose();
            throw;
        }
    }

    private static ImageVisual? CreateCanvasImageVisual(
        ImageAssetCache images,
        IconAssetCache icons,
        CanonicalCanvasImageLayerDefinition layer)
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
            new SKRect(0, 0, _logicalWidth, _logicalHeight),
            RenderQuality.Antialias);
    }

    public RenderedJpeg RenderJpeg(IReadOnlyDictionary<string, object?> metrics, int wireRotationDegrees, int jpegQuality)
    {
        var renderStartedAt = Stopwatch.GetTimestamp();
        var logicalCanvas = _logicalSurface.Canvas;
        logicalCanvas.Clear(_backgroundColor);
        DrawCanvasImage(logicalCanvas, _backgroundImage);

        _renderContext.UpdateMetrics(metrics);
        foreach (var entry in _renderEntries)
            WidgetTransform.Render(logicalCanvas, entry.Widget, _renderContext, entry.Renderer);

        DrawCanvasImage(logicalCanvas, _foregroundImage);
        logicalCanvas.Flush();

        var wireCanvas = _wireSurface.Canvas;
        wireCanvas.Clear(SKColors.Black);
        FrameGeometry.DrawLogicalToWire(
            wireCanvas,
            _logicalSurface,
            _logicalWidth,
            _logicalHeight,
            _nativeWidth,
            _nativeHeight,
            _orientation,
            wireRotationDegrees);
        wireCanvas.Flush();
        var renderCompletedAt = Stopwatch.GetTimestamp();

        var encodeStartedAt = renderCompletedAt;
        using var wirePixels = _wireSurface.PeekPixels()
            ?? throw new InvalidOperationException("Skia wire surface did not expose raster pixels.");
        var encoded = wirePixels.Encode(SKEncodedImageFormat.Jpeg, jpegQuality)
            ?? throw new InvalidOperationException("Skia JPEG encoder returned no data.");

        var encodeCompletedAt = Stopwatch.GetTimestamp();
        return new RenderedJpeg(
            encoded,
            Stopwatch.GetElapsedTime(renderStartedAt, renderCompletedAt).TotalMilliseconds,
            Stopwatch.GetElapsedTime(encodeStartedAt, encodeCompletedAt).TotalMilliseconds);
    }

    internal sealed class RenderedJpeg : IDisposable
    {
        private SKData? _data;

        public RenderedJpeg(SKData data, double renderMs, double encodeMs)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            RenderMs = renderMs;
            EncodeMs = encodeMs;
        }

        public SKData EncodedData => _data ?? throw new ObjectDisposedException(nameof(RenderedJpeg));
        public ReadOnlySpan<byte> Bytes => EncodedData.AsSpan();
        public int Length => checked((int)EncodedData.Size);
        public double RenderMs { get; }
        public double EncodeMs { get; }

        public void Dispose()
        {
            var data = Interlocked.Exchange(ref _data, null);
            data?.Dispose();
        }
    }

    private sealed record RenderEntry(CanonicalWidgetDefinition Widget, IWidgetRenderer Renderer);

    public void Dispose()
    {
        foreach (var disposable in _renderEntries.Select(x => x.Renderer).OfType<IDisposable>().Distinct())
            disposable.Dispose();
        _foregroundImage?.Dispose();
        _backgroundImage?.Dispose();
        _iconAssets.Dispose();
        _imageAssets.Dispose();
        _renderContext.Dispose();
        _typefaceCache.Dispose();
        _wireSurface.Dispose();
        _logicalSurface.Dispose();
    }
}
