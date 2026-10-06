using System.Diagnostics;
using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal enum TextContentLayoutMode
{
    Anchor,
    Container,
    AutoHeightContainer
}

/// <summary>
/// Shared text layout/overflow renderer used by text/value content regardless of the widget
/// that owns it. Text/Value uses the widget presentation; stateful callers such as Binary may
/// provide an independent presentation profile for the active state.
/// </summary>
internal sealed class TextContentRenderer : IDisposable
{
    private static bool IsContainerLayout(TextContentLayoutMode mode) =>
        mode is TextContentLayoutMode.Container or TextContentLayoutMode.AutoHeightContainer;

    private static bool UsesAutoHeight(TextContentLayoutMode mode) =>
        mode == TextContentLayoutMode.AutoHeightContainer;

    private readonly Dictionary<string, AnimationState> _animationStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedLayout> _layoutCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedTextStyle> _styleCache = new(StringComparer.OrdinalIgnoreCase);

    public void Render(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        string? colorOverride = null,
        float opacity = 1f,
        TextContentLayoutMode layoutMode = TextContentLayoutMode.Anchor,
        TextPresentation? presentationOverride = null)
    {
        var id = widget.Id ?? string.Empty;
        var presentation = presentationOverride ?? widget.GetTextPresentation();
        var style = GetStyle(widget, context, presentation, colorOverride, opacity);

        if (widget.Width <= 0f)
        {
            _animationStates.Remove(id);
            RemoveLayout(id);

            if (IsContainerLayout(layoutMode))
            {
                var bounds = ValueTextLayout.ResolveContainerBounds(
                    widget,
                    context,
                    text,
                    presentation,
                    autoHeight: UsesAutoHeight(layoutMode));
                canvas.Save();
                try
                {
                    // Container-hosted text obeys the effective owner bounds. Width=0 keeps
                    // intrinsic text width. AutoHeightContainer derives the vertical extent from
                    // content; a normal Container keeps explicit Height authoritative.
                    canvas.ClipRect(bounds);
                    DrawTextAt(
                        canvas,
                        text,
                        style.Font,
                        ValueTextLayout.GetAlignedX(bounds, presentation.Align),
                        ValueTextLayout.GetContainerBaseline(bounds, style.Metrics, presentation),
                        ValueTextLayout.GetTextAlign(presentation),
                        style);
                }
                finally
                {
                    canvas.Restore();
                }
            }
            else
            {
                DrawTextAt(canvas, text, style.Font, widget.X, GetBaseline(widget, style.Metrics, presentation), style.Align, style);
            }

            return;
        }

        var mode = ValueOverflowContract.Normalize(presentation.OverflowMode);
        var containerBounds = IsContainerLayout(layoutMode)
            ? ValueTextLayout.ResolveContainerBounds(
                widget,
                context,
                text,
                presentation,
                autoHeight: UsesAutoHeight(layoutMode))
            : default;
        var cached = GetLayout(widget, context, text, mode, layoutMode, containerBounds, presentation);
        var layout = cached.Layout;
        var font = cached.Font;
        var alignmentBounds = IsContainerLayout(layoutMode)
            ? containerBounds
            : new SKRect(widget.X, layout.Bounds.Top, widget.X + widget.Width, layout.Bounds.Bottom);

        canvas.Save();
        try
        {
            if (IsContainerLayout(layoutMode))
            {
                // Container-hosted content must not paint outside its effective bounds. Those
                // bounds may use explicit Height or content-driven height depending on owner mode.
                canvas.ClipRect(alignmentBounds);
            }
            else
            {
                // Intrinsic Text/Value keeps content-driven vertical extent and constrains only
                // the explicit fixed width.
                canvas.ClipRect(new SKRect(alignmentBounds.Left, -1_000_000f, alignmentBounds.Right, 1_000_000f));
            }

            switch (mode)
            {
                case ValueOverflowContract.Ellipsis:
                    DrawSingleLine(canvas, cached.EllipsizedText, font, layout.FirstBaseline, alignmentBounds, style, presentation);
                    break;
                case ValueOverflowContract.ShrinkToFit:
                    DrawSingleLine(canvas, text, font, layout.FirstBaseline, alignmentBounds, style, presentation);
                    break;
                case ValueOverflowContract.Wrap:
                    DrawWrapped(canvas, font, layout, alignmentBounds, style, presentation);
                    break;
                case ValueOverflowContract.Scroll:
                    DrawScroll(canvas, widget, text, cached, layout, alignmentBounds, style, presentation);
                    break;
                case ValueOverflowContract.Bump:
                    DrawBump(canvas, widget, text, cached, layout, alignmentBounds, style, presentation);
                    break;
                default:
                    DrawSingleLine(canvas, text, font, layout.FirstBaseline, alignmentBounds, style, presentation);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private CachedLayout GetLayout(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        string mode,
        TextContentLayoutMode layoutMode,
        SKRect containerBounds,
        TextPresentation presentation)
    {
        var id = widget.Id ?? string.Empty;
        var key = new LayoutKey(
            text,
            mode,
            layoutMode,
            widget.X,
            widget.Y,
            widget.Width,
            widget.Height,
            presentation.FontFamily,
            presentation.FontSize,
            presentation.FontWeight,
            presentation.Italic,
            presentation.Align,
            presentation.VerticalAlign);

        if (_layoutCache.TryGetValue(id, out var cached) && cached.Key == key)
            return cached;

        cached?.Dispose();
        var layout = IsContainerLayout(layoutMode)
            ? ValueTextLayout.CreateContainerFixedLayout(widget, context, text, containerBounds, presentation)
            : ValueTextLayout.CreateFixedLayout(widget, context, text, presentation);
        var font = new SKFont(context.GetTypeface(presentation), layout.FontSize);
        var textWidth = font.MeasureText(text);
        var maxWidth = IsContainerLayout(layoutMode) ? containerBounds.Width : widget.Width;
        var ellipsized = mode == ValueOverflowContract.Ellipsis
            ? ValueTextLayout.Ellipsize(font, text, maxWidth)
            : text;

        var replacement = new CachedLayout(key, layout, font, textWidth, ellipsized);
        _layoutCache[id] = replacement;
        return replacement;
    }

    private void RemoveLayout(string id)
    {
        if (!_layoutCache.Remove(id, out var cached))
            return;

        cached.Dispose();
    }

    private static void DrawSingleLine(
        SKCanvas canvas,
        string text,
        SKFont font,
        float baseline,
        SKRect alignmentBounds,
        CachedTextStyle style,
        TextPresentation presentation)
    {
        DrawTextAt(
            canvas,
            text,
            font,
            ValueTextLayout.GetAlignedX(alignmentBounds, presentation.Align),
            baseline,
            ValueTextLayout.GetTextAlign(presentation),
            style);
    }

    private static void DrawWrapped(
        SKCanvas canvas,
        SKFont font,
        ValueTextBlockLayout layout,
        SKRect alignmentBounds,
        CachedTextStyle style,
        TextPresentation presentation)
    {
        var x = ValueTextLayout.GetAlignedX(alignmentBounds, presentation.Align);
        var align = ValueTextLayout.GetTextAlign(presentation);
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            DrawTextAt(
                canvas,
                layout.Lines[i],
                font,
                x,
                layout.FirstBaseline + i * layout.LineHeight,
                align,
                style);
        }
    }

    private void DrawScroll(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        string text,
        CachedLayout cached,
        ValueTextBlockLayout layout,
        SKRect alignmentBounds,
        CachedTextStyle style,
        TextPresentation presentation)
    {
        var textWidth = cached.TextWidth;
        if (textWidth <= alignmentBounds.Width)
        {
            _animationStates.Remove(widget.Id ?? string.Empty);
            DrawSingleLine(canvas, text, cached.Font, layout.FirstBaseline, alignmentBounds, style, presentation);
            return;
        }

        var elapsed = GetAnimationElapsedSeconds(widget, text, ValueOverflowContract.Scroll, presentation);
        var speed = presentation.ScrollSpeed > 0f
            ? presentation.ScrollSpeed
            : ValueTextLayout.GetAutomaticScrollSpeed(presentation);
        var gap = ValueTextLayout.GetScrollGap(widget, presentation);
        var cycle = textWidth + gap;
        var offset = cycle <= 0f ? 0f : (float)((elapsed * speed) % cycle);
        var x = alignmentBounds.Left - offset;

        DrawTextAt(
            canvas, text, cached.Font, x, layout.FirstBaseline,
            SKTextAlign.Left, style);
        DrawTextAt(
            canvas, text, cached.Font, x + cycle, layout.FirstBaseline,
            SKTextAlign.Left, style);
    }

    private void DrawBump(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        string text,
        CachedLayout cached,
        ValueTextBlockLayout layout,
        SKRect alignmentBounds,
        CachedTextStyle style,
        TextPresentation presentation)
    {
        var travel = cached.TextWidth - alignmentBounds.Width;
        if (travel <= 0f)
        {
            _animationStates.Remove(widget.Id ?? string.Empty);
            DrawSingleLine(canvas, text, cached.Font, layout.FirstBaseline, alignmentBounds, style, presentation);
            return;
        }

        var elapsed = GetAnimationElapsedSeconds(widget, text, ValueOverflowContract.Bump, presentation);
        var speed = presentation.ScrollSpeed > 0f
            ? presentation.ScrollSpeed
            : ValueTextLayout.GetAutomaticScrollSpeed(presentation);
        var pauseSeconds = presentation.BumpPauseMs / 1000.0;
        var travelSeconds = speed <= 0f ? 0.0 : travel / speed;
        var cycle = pauseSeconds + travelSeconds + pauseSeconds + travelSeconds;

        double distance;
        if (cycle <= 0.0)
        {
            distance = 0.0;
        }
        else
        {
            var phase = elapsed % cycle;
            if (phase < pauseSeconds)
            {
                distance = 0.0;
            }
            else if ((phase -= pauseSeconds) < travelSeconds)
            {
                distance = phase * speed;
            }
            else if ((phase -= travelSeconds) < pauseSeconds)
            {
                distance = travel;
            }
            else
            {
                phase -= pauseSeconds;
                distance = travel - phase * speed;
            }
        }

        var x = alignmentBounds.Left - (float)Math.Clamp(distance, 0.0, travel);
        DrawTextAt(
            canvas, text, cached.Font, x, layout.FirstBaseline,
            SKTextAlign.Left, style);
    }

    private CachedTextStyle GetStyle(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        TextPresentation presentation,
        string? colorOverride,
        float opacity)
    {
        var id = widget.Id ?? string.Empty;
        var key = new StyleKey(
            string.IsNullOrWhiteSpace(colorOverride) ? widget.Color : colorOverride!,
            Math.Clamp(opacity, 0f, 1f),
            presentation.OutlineWidth,
            presentation.OutlineColor,
            presentation.FontFamily,
            presentation.FontSize,
            presentation.FontWeight,
            presentation.Italic,
            presentation.Align,
            context.Antialias);

        if (_styleCache.TryGetValue(id, out var cached) && cached.Key == key)
            return cached;

        cached?.Dispose();
        var created = CachedTextStyle.Create(context, key, presentation);
        _styleCache[id] = created;
        return created;
    }

    private static void DrawTextAt(
        SKCanvas canvas,
        string text,
        SKFont font,
        float x,
        float baseline,
        SKTextAlign align,
        CachedTextStyle style)
    {
        if (style.OutlinePaint is not null)
            canvas.DrawText(text, x, baseline, align, font, style.OutlinePaint);

        canvas.DrawText(text, x, baseline, align, font, style.FillPaint);
    }

    private static float GetBaseline(CanonicalWidgetDefinition widget, SKFontMetrics metrics, TextPresentation presentation) =>
        presentation.VerticalAlign.Equals("top", StringComparison.OrdinalIgnoreCase)
            ? widget.Y - metrics.Ascent
            : presentation.VerticalAlign.Equals("middle", StringComparison.OrdinalIgnoreCase) ||
              presentation.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                ? widget.Y - ((metrics.Ascent + metrics.Descent) / 2f)
                : presentation.VerticalAlign.Equals("bottom", StringComparison.OrdinalIgnoreCase)
                    ? widget.Y - metrics.Descent
                    : widget.Y;

    private double GetAnimationElapsedSeconds(CanonicalWidgetDefinition widget, string text, string mode, TextPresentation presentation)
    {
        var id = widget.Id ?? string.Empty;
        var key = new AnimationKey(
            text,
            mode,
            widget.Width,
            presentation.FontFamily,
            presentation.FontSize,
            presentation.FontWeight,
            presentation.Italic,
            presentation.ScrollSpeed,
            presentation.BumpPauseMs);
        var now = Stopwatch.GetTimestamp();

        if (!_animationStates.TryGetValue(id, out var state) || state.Key != key)
        {
            state = new AnimationState(key, now);
            _animationStates[id] = state;
        }

        return (now - state.StartTimestamp) / (double)Stopwatch.Frequency;
    }

    public void Dispose()
    {
        foreach (var cached in _layoutCache.Values)
            cached.Dispose();
        _layoutCache.Clear();
        foreach (var style in _styleCache.Values)
            style.Dispose();
        _styleCache.Clear();
        _animationStates.Clear();
    }

    private readonly record struct LayoutKey(
        string Text,
        string Mode,
        TextContentLayoutMode LayoutMode,
        float X,
        float Y,
        float Width,
        float Height,
        string FontFamily,
        float FontSize,
        int FontWeight,
        bool Italic,
        string Align,
        string VerticalAlign);

    private readonly record struct StyleKey(
        string TextColor,
        float Opacity,
        float OutlineWidth,
        string OutlineColor,
        string FontFamily,
        float FontSize,
        int FontWeight,
        bool Italic,
        string Align,
        bool Antialias);

    private sealed class CachedLayout : IDisposable
    {
        public CachedLayout(
            LayoutKey key,
            ValueTextBlockLayout layout,
            SKFont font,
            float textWidth,
            string ellipsizedText)
        {
            Key = key;
            Layout = layout;
            Font = font;
            TextWidth = textWidth;
            EllipsizedText = ellipsizedText;
        }

        public LayoutKey Key { get; }
        public ValueTextBlockLayout Layout { get; }
        public SKFont Font { get; }
        public float TextWidth { get; }
        public string EllipsizedText { get; }

        public void Dispose() => Font.Dispose();
    }

    private sealed class CachedTextStyle : IDisposable
    {
        private CachedTextStyle(
            StyleKey key,
            SKFont font,
            SKPaint fillPaint,
            SKPaint? outlinePaint,
            SKTextAlign align,
            SKFontMetrics metrics)
        {
            Key = key;
            Font = font;
            FillPaint = fillPaint;
            OutlinePaint = outlinePaint;
            Align = align;
            Metrics = metrics;
        }

        public StyleKey Key { get; }
        public SKFont Font { get; }
        public SKPaint FillPaint { get; }
        public SKPaint? OutlinePaint { get; }
        public SKTextAlign Align { get; }
        public SKFontMetrics Metrics { get; }

        public static CachedTextStyle Create(
            WidgetRenderContext context,
            StyleKey key,
            TextPresentation presentation)
        {
            var font = new SKFont(context.GetTypeface(presentation), presentation.FontSize);
            SKPaint? fill = null;
            SKPaint? outline = null;
            try
            {
                fill = new SKPaint
                {
                    IsAntialias = context.Antialias,
                    Color = ApplyOpacity(ColorParser.Parse(key.TextColor), key.Opacity),
                    Style = SKPaintStyle.Fill
                };

                if (presentation.OutlineWidth > 0f)
                {
                    var outlineColor = ApplyOpacity(ColorParser.Parse(presentation.OutlineColor), key.Opacity);
                    if (outlineColor.Alpha > 0)
                    {
                        outline = new SKPaint
                        {
                            IsAntialias = context.Antialias,
                            Color = outlineColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = presentation.OutlineWidth,
                            StrokeJoin = SKStrokeJoin.Round
                        };
                    }
                }

                var align = ValueTextLayout.GetTextAlign(presentation);
                return new CachedTextStyle(key, font, fill!, outline, align, font.Metrics);
            }
            catch
            {
                outline?.Dispose();
                fill?.Dispose();
                font.Dispose();
                throw;
            }
        }

        private static SKColor ApplyOpacity(SKColor color, float opacity)
        {
            var alpha = (byte)Math.Clamp(
                (int)Math.Round(color.Alpha * Math.Clamp(opacity, 0f, 1f)),
                0,
                255);
            return new SKColor(color.Red, color.Green, color.Blue, alpha);
        }

        public void Dispose()
        {
            OutlinePaint?.Dispose();
            FillPaint.Dispose();
            Font.Dispose();
        }
    }

    private readonly record struct AnimationKey(
        string Text,
        string Mode,
        float Width,
        string FontFamily,
        float FontSize,
        int FontWeight,
        bool Italic,
        float ScrollSpeed,
        int BumpPauseMs);

    private readonly record struct AnimationState(AnimationKey Key, long StartTimestamp);
}
