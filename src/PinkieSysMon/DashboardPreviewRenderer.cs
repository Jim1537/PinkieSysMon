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
    private readonly CanvasPanelGraphics _canvasPanel;

    public DashboardPreviewRenderer(CanonicalDashboard.DashboardDefinition definition, MetricHistoryStore? history = null, FileLogger? log = null, EditorArtworkRecovery? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;

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
        CanvasPanelGraphics? canvasPanel = null;
        Dictionary<string, IWidgetRenderer>? renderers = null;

        var phase = "surface";
        try
        {
            surface = SKSurface.Create(_pixelInfo)
                ?? throw new InvalidOperationException("Failed to create editor preview surface.");
            phase = "fonts";
            typefaces = new TypefaceCache(_definition.BaseDirectory, _definition.Widgets);
            renderContext = new WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                RenderQuality.Antialias,
                typefaces, history);
            images = new ImageAssetCache(_definition.BaseDirectory, recovery);
            icons = new IconAssetCache(_definition.BaseDirectory, recovery);
            phase = "images.preload";
            images.Preload(_definition.Widgets);
            phase = "icons.preload";
            icons.Preload(_definition.Widgets);

            renderContext.InitializePanelAssets(images, icons, _definition.Widgets);
            canvasPanel = new CanvasPanelGraphics(_definition.Canvas, Width, Height,
                images, icons, RenderQuality.Antialias);

            phase = "widgets.prepare";
            renderers = WidgetRendererRegistry.Create(images, icons, log);
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
            _canvasPanel = canvasPanel;
            _renderEntries = renderEntries;

            surface = null;
            typefaces = null;
            renderContext = null;
            images = null;
            icons = null;
            canvasPanel = null;
            renderers = null;
        }
        catch (Exception error)
        {
            RenderFailureContext.Add(error, "preview.prepare." + phase);
            if (renderers is not null)
            {
                foreach (var disposable in renderers.Values.OfType<IDisposable>().Distinct())
                    disposable.Dispose();
            }
            canvasPanel?.Dispose();
            icons?.Dispose();
            images?.Dispose();
            renderContext?.Dispose();
            typefaces?.Dispose();
            surface?.Dispose();
            throw;
        }
    }

    internal string ResourceSummary => $"images={_images.ResourceCount}; imageBytes={_images.DecodedBytes}; icons={_icons.ResourceCount}; iconBytes={_icons.DecodedBytes}; fonts={_typefaces.LoadedFaces}";

    public int Width { get; }
    public int Height { get; }
    public bool RequiresFrequentRefresh => _renderEntries.Any(entry => entry.Renderer is ShortcutWidgetRenderer { HasPendingResources: true }) || _definition.Widgets.Any(w => w is CanonicalDashboard.GraphWidgetDefinition) || _images.HasAnimatedAssets || _icons.HasAnimatedAssets || HasAnimatedValueOverflow();
    public int RecommendedRefreshIntervalMs => HasAnimatedValueOverflow() ? 50 : _definition.Widgets.Any(w => w is CanonicalDashboard.GraphWidgetDefinition) ? 100 : (_images.HasAnimatedAssets || _icons.HasAnimatedAssets) ? 250 : 1000;

    private bool HasAnimatedValueOverflow() =>
        _definition.Widgets.Any(widget =>
            widget.Width > 0f &&
            (widget is CanonicalDashboard.ValueWidgetDefinition value
                ? ValueOverflowContract.IsAnimated(value.TextPresentation.OverflowMode)
                : widget is CanonicalDashboard.ShortcutWidgetDefinition shortcut
                    ? shortcut.Label.Length > 0 && ValueOverflowContract.IsAnimated(shortcut.TextPresentation.OverflowMode)
                : widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget &&
                  stateWidget.Profiles.Values.Any(profile =>
                      profile.UsesValueContent &&
                      ValueOverflowContract.IsAnimated(profile.TextPresentation.OverflowMode))));

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
        canvas.Clear(SKColors.Transparent);
        _canvasPanel.DrawBackground(canvas);

        _renderContext.BeginFrame(metrics);
        canvas.Save();
        try
        {
            _canvasPanel.ClipChildren(canvas);
            foreach (var entry in _renderEntries)
            {
                try { WidgetTransform.Render(canvas, entry.Widget, _renderContext, entry.Renderer); }
                catch (Exception error) { RenderFailureContext.Add(error, "widget.render", entry.Widget.Id); throw; }
            }
        }
        finally { canvas.Restore(); }

        _canvasPanel.DrawOverlay(canvas);
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
        _canvasPanel.Dispose();
        _icons.Dispose();
        _images.Dispose();
        _renderContext.Dispose();
        _typefaces.Dispose();
        _surface.Dispose();
    }
}

