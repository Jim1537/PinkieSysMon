using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class BarWidgetRenderer : IWidgetRenderer, IDisposable
{
    private readonly ImageAssetCache _images;
    private readonly Dictionary<CanonicalWidgetDefinition, CompiledBar> _compiled = new();

    public BarWidgetRenderer(ImageAssetCache images)
    {
        _images = images;
    }

    public string Type => WidgetTypeContract.Bar;

    public void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var bar = widget as CanonicalDashboard.BarWidgetDefinition
            ?? throw new InvalidDataException($"Bar renderer received widget type '{widget.Type}'.");
        if (string.IsNullOrWhiteSpace(bar.Metric) ||
            !context.TryGetMetricDouble(bar, out var raw))
            return;

        var compiled = GetCompiled(bar, context.Antialias);
        var ratio = compiled.GetRatio(raw);
        if (ratio <= 0.0 || compiled.Inner.Width <= 0f || compiled.Inner.Height <= 0f)
            return;

        var activeWidth = compiled.Inner.Width * (float)ratio;
        var fill = compiled.Reverse
            ? new SKRect(compiled.Inner.Right - activeWidth, compiled.Inner.Top, compiled.Inner.Right, compiled.Inner.Bottom)
            : new SKRect(compiled.Inner.Left, compiled.Inner.Top, compiled.Inner.Left + activeWidth, compiled.Inner.Bottom);
        if (fill.Width <= 0f)
            return;

        canvas.Save();
        try
        {
            canvas.Translate(bar.X, bar.Y);
            if (compiled.Indicator is null)
            {
                if (compiled.Image is not null)
                    DrawImageProgress(canvas, fill, (float)ratio, compiled);
                return;
            }

            var indicator = compiled.Indicator!;
            switch (indicator.Mode)
            {
                case QuantitativeThresholdMode.State:
                    compiled.FillPaint!.Color = indicator.GetStateColor(raw);
                    DrawFill(canvas, fill, compiled.InnerRadius, compiled.FillPaint!);
                    break;

                case QuantitativeThresholdMode.SegmentTransition:
                    DrawFill(canvas, fill, compiled.InnerRadius, compiled.FillPaint!);
                    break;

                default:
                    DrawSolidSegments(canvas, fill, compiled);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private CompiledBar GetCompiled(CanonicalDashboard.BarWidgetDefinition widget, bool antialias)
    {
        if (_compiled.TryGetValue(widget, out var compiled))
            return compiled;

        compiled = CompiledBar.Create(widget, _images, antialias);
        _compiled.Add(widget, compiled);
        return compiled;
    }

    private static void DrawImageProgress(SKCanvas canvas, SKRect active, float ratio, CompiledBar compiled)
    {
        canvas.Save();
        try
        {
            ClipActiveRegion(canvas, active, compiled);

            switch (compiled.ProgressMode)
            {
                case BarImageProgressMode.Slide:
                    var slide = (1f - ratio) * compiled.Inner.Width * (compiled.Reverse ? 1f : -1f);
                    canvas.Translate(slide, 0f);
                    DrawPreparedImage(canvas, compiled);
                    break;

                case BarImageProgressMode.Reveal:
                    DrawPreparedImage(canvas, compiled);
                    break;

                default:
                    var anchorX = compiled.Reverse ? compiled.Inner.Right : compiled.Inner.Left;
                    canvas.Scale(ratio, 1f, anchorX, 0f);
                    DrawPreparedImage(canvas, compiled);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void ClipActiveRegion(SKCanvas canvas, SKRect active, CompiledBar compiled)
    {
        var radius = Math.Min(compiled.InnerRadius, Math.Min(active.Width, active.Height) / 2f);
        if (radius > 0f)
        {
            compiled.ActiveClipRoundRect.SetRect(active, radius, radius);
            canvas.ClipRoundRect(compiled.ActiveClipRoundRect, SKClipOperation.Intersect, compiled.Antialias);
        }
        else
        {
            canvas.ClipRect(active, SKClipOperation.Intersect, compiled.Antialias);
        }
    }

    private static void DrawPreparedImage(SKCanvas canvas, CompiledBar compiled)
    {
        canvas.Save();
        try
        {
            if (compiled.InnerRadius > 0f)
            {
                compiled.PreparedClipRoundRect.SetRect(compiled.Inner, compiled.InnerRadius, compiled.InnerRadius);
                canvas.ClipRoundRect(compiled.PreparedClipRoundRect, SKClipOperation.Intersect, compiled.Antialias);
            }
            else
            {
                canvas.ClipRect(compiled.Inner, SKClipOperation.Intersect, compiled.Antialias);
            }

            canvas.DrawBitmap(
                compiled.Image!.GetFrame(compiled.Loop),
                compiled.ImageDestination,
                SKSamplingOptions.Default,
                null);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawSolidSegments(SKCanvas canvas, SKRect fill, CompiledBar compiled)
    {
        canvas.Save();
        try
        {
            var radius = Math.Min(compiled.InnerRadius, Math.Min(fill.Width, fill.Height) / 2f);
            compiled.ActiveClipRoundRect.SetRect(fill, radius, radius);
            canvas.ClipRoundRect(compiled.ActiveClipRoundRect, SKClipOperation.Intersect, compiled.Antialias);

            var stops = compiled.Indicator!.SolidStops;
            for (var i = 0; i < stops.Count; i++)
            {
                var startX = ValueToX(stops[i].Value, compiled);
                var endValue = i + 1 < stops.Count ? stops[i + 1].Value : compiled.Max;
                var endX = ValueToX(endValue, compiled);
                var left = Math.Max(fill.Left, Math.Min(startX, endX));
                var right = Math.Min(fill.Right, Math.Max(startX, endX));
                if (right <= left)
                    continue;

                compiled.FillPaint!.Color = stops[i].Color;
                canvas.DrawRect(new SKRect(left, fill.Top, right, fill.Bottom), compiled.FillPaint!);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawFill(SKCanvas canvas, SKRect fill, float radius, SKPaint paint)
    {
        var effectiveRadius = Math.Min(radius, Math.Min(fill.Width, fill.Height) / 2f);
        if (effectiveRadius > 0f)
            canvas.DrawRoundRect(fill, effectiveRadius, effectiveRadius, paint);
        else
            canvas.DrawRect(fill, paint);
    }

    private static float ValueToX(double value, CompiledBar compiled)
    {
        var ratio = compiled.GetRatioFloat(value);
        return compiled.Reverse
            ? compiled.Inner.Right - compiled.Inner.Width * ratio
            : compiled.Inner.Left + compiled.Inner.Width * ratio;
    }

    public void InvalidateGeometry(CanonicalWidgetDefinition widget)
    {
        if (!_compiled.Remove(widget, out var compiled))
            return;

        compiled.Dispose();
    }

    public void Dispose()
    {
        foreach (var compiled in _compiled.Values)
            compiled.Dispose();
        _compiled.Clear();
    }

    private enum BarImageFitMode
    {
        Clip,
        Contain,
        Cover,
        Stretch
    }

    private enum BarImageProgressMode
    {
        Scale,
        Slide,
        Reveal
    }

    private sealed class CompiledBar : IDisposable
    {
        private readonly double _min;
        private readonly double _inverseRange;

        private CompiledBar(
            double min,
            double inverseRange,
            CompiledQuantitativeIndicator? indicator,
            bool antialias,
            SKRect inner,
            float innerRadius,
            bool reverse,
            double max,
            ImageAsset? image,
            bool loop,
            SKRect imageDestination,
            BarImageProgressMode progressMode,
            SKShader? transitionShader,
            SKPaint? fillPaint,
            SKRoundRect activeClipRoundRect,
            SKRoundRect preparedClipRoundRect)
        {
            _min = min;
            _inverseRange = inverseRange;
            Indicator = indicator;
            Antialias = antialias;
            Inner = inner;
            InnerRadius = innerRadius;
            Reverse = reverse;
            Max = max;
            Image = image;
            Loop = loop;
            ImageDestination = imageDestination;
            ProgressMode = progressMode;
            TransitionShader = transitionShader;
            FillPaint = fillPaint;
            ActiveClipRoundRect = activeClipRoundRect;
            PreparedClipRoundRect = preparedClipRoundRect;
        }

        public CompiledQuantitativeIndicator? Indicator { get; }
        public bool Antialias { get; }
        public SKRect Inner { get; }
        public float InnerRadius { get; }
        public bool Reverse { get; }
        public double Max { get; }
        public ImageAsset? Image { get; }
        public bool Loop { get; }
        public SKRect ImageDestination { get; }
        public BarImageProgressMode ProgressMode { get; }
        public SKShader? TransitionShader { get; }
        public SKPaint? FillPaint { get; }
        public SKRoundRect ActiveClipRoundRect { get; }
        public SKRoundRect PreparedClipRoundRect { get; }

        public static CompiledBar Create(CanonicalDashboard.BarWidgetDefinition widget, ImageAssetCache images, bool antialias)
        {
            SKShader? transitionShader = null;
            SKPaint? fillPaint = null;
            SKRoundRect? activeClipRoundRect = null;
            SKRoundRect? preparedClipRoundRect = null;
            try
            {
                var inset = Math.Max(0f, widget.BorderWidth) + widget.Gap;
                var inner = new SKRect(
                    inset,
                    inset,
                    widget.Width - inset,
                    widget.Height - inset);

                var outerRadius = Math.Min(
                    Math.Max(0f, widget.CornerRadius),
                    Math.Min(widget.Width, widget.Height) / 2f);
                var innerRadius = inner.Width > 0f && inner.Height > 0f
                    ? Math.Min(Math.Max(0f, outerRadius - inset), Math.Min(inner.Width, inner.Height) / 2f)
                    : 0f;

                var inverseRange = 1.0 / (widget.Max - widget.Min);
                activeClipRoundRect = new SKRoundRect();
                preparedClipRoundRect = new SKRoundRect();

                if (BarImageContract.IsImageMode(widget.ContentMode))
                {
                    ImageAsset? image = null;
                    var imageDestination = inner;
                    if (!string.IsNullOrWhiteSpace(widget.Image.Source) && inner.Width > 0f && inner.Height > 0f)
                    {
                        image = images.Get(widget.Image.Source!);
                        imageDestination = GetImageDestination(
                            inner,
                            image,
                            NormalizeImageFit(widget.Image.Fit));
                    }

                    return new CompiledBar(
                        widget.Min,
                        inverseRange,
                        null,
                        antialias,
                        inner,
                        innerRadius,
                        widget.Reverse,
                        widget.Max,
                        image,
                        widget.Image.Loop,
                        imageDestination,
                        NormalizeProgressMode(widget.Image.ProgressMode),
                        null,
                        null,
                        activeClipRoundRect,
                        preparedClipRoundRect);
                }

                var indicator = CompiledQuantitativeIndicator.Create(widget, widget.Color);
                if (indicator.Mode == QuantitativeThresholdMode.SegmentTransition && inner.Width > 0f && inner.Height > 0f)
                {
                    var gradientStart = widget.Reverse
                        ? new SKPoint(inner.Right, inner.MidY)
                        : new SKPoint(inner.Left, inner.MidY);
                    var gradientEnd = widget.Reverse
                        ? new SKPoint(inner.Left, inner.MidY)
                        : new SKPoint(inner.Right, inner.MidY);

                    transitionShader = SKShader.CreateLinearGradient(
                        gradientStart,
                        gradientEnd,
                        indicator.TransitionColors,
                        indicator.TransitionPositions,
                        SKShaderTileMode.Clamp);
                }

                fillPaint = new SKPaint
                {
                    IsAntialias = antialias,
                    Style = SKPaintStyle.Fill,
                    Shader = transitionShader
                };

                return new CompiledBar(
                    widget.Min,
                    inverseRange,
                    indicator,
                    antialias,
                    inner,
                    innerRadius,
                    widget.Reverse,
                    widget.Max,
                    null,
                    false,
                    default,
                    BarImageProgressMode.Scale,
                    transitionShader,
                    fillPaint,
                    activeClipRoundRect,
                    preparedClipRoundRect);
            }
            catch
            {
                preparedClipRoundRect?.Dispose();
                activeClipRoundRect?.Dispose();
                fillPaint?.Dispose();
                transitionShader?.Dispose();
                throw;
            }
        }

        public double GetRatio(double value) =>
            Math.Clamp((value - _min) * _inverseRange, 0.0, 1.0);

        public float GetRatioFloat(double value) =>
            (float)GetRatio(value);

        private static BarImageFitMode NormalizeImageFit(string? fit)
        {
            var normalized = BarImageContract.NormalizeImageFit(fit);
            return normalized switch
            {
                BarImageContract.ImageFitClip => BarImageFitMode.Clip,
                BarImageContract.ImageFitContain => BarImageFitMode.Contain,
                BarImageContract.ImageFitCover => BarImageFitMode.Cover,
                _ => BarImageFitMode.Stretch
            };
        }

        private static BarImageProgressMode NormalizeProgressMode(string? mode)
        {
            var normalized = BarImageContract.NormalizeProgressMode(mode);
            return normalized switch
            {
                BarImageContract.ProgressModeSlide => BarImageProgressMode.Slide,
                BarImageContract.ProgressModeReveal => BarImageProgressMode.Reveal,
                _ => BarImageProgressMode.Scale
            };
        }

        private static SKRect GetImageDestination(SKRect bounds, ImageAsset image, BarImageFitMode fit)
        {
            if (fit == BarImageFitMode.Stretch)
                return bounds;

            if (fit == BarImageFitMode.Clip)
            {
                var width = (float)image.Width;
                var height = (float)image.Height;
                return new SKRect(
                    bounds.MidX - width / 2f,
                    bounds.MidY - height / 2f,
                    bounds.MidX + width / 2f,
                    bounds.MidY + height / 2f);
            }

            var scale = fit == BarImageFitMode.Cover
                ? Math.Max(bounds.Width / image.Width, bounds.Height / image.Height)
                : Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            var scaledWidth = image.Width * scale;
            var scaledHeight = image.Height * scale;
            return new SKRect(
                bounds.MidX - scaledWidth / 2f,
                bounds.MidY - scaledHeight / 2f,
                bounds.MidX + scaledWidth / 2f,
                bounds.MidY + scaledHeight / 2f);
        }

        public void Dispose()
        {
            PreparedClipRoundRect.Dispose();
            ActiveClipRoundRect.Dispose();
            FillPaint?.Dispose();
            TransitionShader?.Dispose();
        }
    }
}
