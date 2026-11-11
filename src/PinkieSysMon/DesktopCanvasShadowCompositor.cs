using System.Drawing;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

// Desktop only. A cached silhouette shadow is composited beneath each logical
// frame; physical JPEGs never pass through this compositor.
internal sealed class DesktopCanvasShadowCompositor : IDisposable
{
    private readonly DesktopCanvasViewport _viewport;
    private readonly SKBitmap _shadow;
    private readonly SKBitmap _composed;
    private readonly SKCanvas _canvas;
    private bool _disposed;

    internal DesktopCanvasShadowCompositor(DesktopCanvasViewport viewport, C.CanvasDefinition root)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(root);
        if (!viewport.HasShadow)
            throw new ArgumentException("An unshadowed viewport needs no compositor.", nameof(viewport));

        _viewport = viewport;
        var info = new SKImageInfo(viewport.Surface.Width, viewport.Surface.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul);
        _shadow = new SKBitmap(info);
        _composed = new SKBitmap(info);
        _canvas = new SKCanvas(_composed);
        try
        {
            using var shadowCanvas = new SKCanvas(_shadow);
            shadowCanvas.Clear(SKColors.Transparent);
            shadowCanvas.Translate(viewport.CanvasOffset.X + root.ShadowOffsetX,
                viewport.CanvasOffset.Y + root.ShadowOffsetY);

            var tint = ColorParser.Parse(root.ShadowColor);
            using var filter = root.ShadowBlur > 0f
                ? SKImageFilter.CreateBlur(root.ShadowBlur, root.ShadowBlur)
                : null;
            using var paint = new SKPaint
            {
                Color = tint,
                IsAntialias = true,
                ImageFilter = filter
            };
            var radius = Math.Min(Math.Max(0f, root.CornerRadius),
                Math.Min(viewport.Canvas.Width, viewport.Canvas.Height) / 2f);
            shadowCanvas.DrawRoundRect(new SKRect(0, 0, viewport.Canvas.Width, viewport.Canvas.Height),
                radius, radius, paint);
            shadowCanvas.Flush();
        }
        catch
        {
            _canvas.Dispose();
            _composed.Dispose();
            _shadow.Dispose();
            throw;
        }
    }

    internal SKBitmap Compose(SKBitmap logical)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(logical);
        if (logical.Width != _viewport.Canvas.Width || logical.Height != _viewport.Canvas.Height ||
            logical.ColorType != SKColorType.Bgra8888 || logical.AlphaType != SKAlphaType.Premul)
            throw new InvalidOperationException("Desktop shadow input must be the original premultiplied logical Canvas.");

        _canvas.Clear(SKColors.Transparent);
        // No bitmap scaling: use the supported sampling overload without
        // changing the existing per-pixel composition or alpha contract.
        _canvas.DrawBitmap(_shadow, 0, 0, SKSamplingOptions.Default);
        _canvas.DrawBitmap(logical, _viewport.CanvasOffset.X, _viewport.CanvasOffset.Y,
            SKSamplingOptions.Default);
        _canvas.Flush();
        return _composed; // session borrows this only through synchronous Present()
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.Dispose();
        _composed.Dispose();
        _shadow.Dispose();
    }
}
