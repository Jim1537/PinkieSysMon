using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal static class WidgetGeometry
{
    private const float MinimumPointExtent = 1f;

    public static float GetNormalizedRotation(CanonicalWidgetDefinition widget)
    {
        if (double.IsNaN(widget.Rotation) || double.IsInfinity(widget.Rotation))
            throw new InvalidDataException($"Widget '{widget.Id}' Rotation must be a finite number.");

        var normalized = widget.Rotation % 360d;
        if (Math.Abs(normalized) < 0.000001d)
            return 0f;

        return (float)normalized;
    }


    public static SKRect GetLayoutBounds(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        if (widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
        {
            if (context.TryResolveStateVisualProfile(widget, out var stateKey, out var activeProfile) &&
                StateVisualProfileContract.IsValueSource(activeProfile.SourceType))
            {
                var presentation = activeProfile.Text ?? throw new InvalidDataException(
                    $"{StateVisualProfileContract.GetWidgetDisplayName(widget.Type)} widget '{widget.Id}' active value state requires a text presentation profile.");
                return Normalize(ValueTextLayout.ResolveContainerBounds(
                    widget,
                    context,
                    context.ResolveStateValueText(widget, stateKey),
                    presentation,
                    autoHeight: stateWidget.UsesOnlyValueSources()));
            }

            if (widget is CanonicalDashboard.BinaryWidgetDefinition binary &&
                stateWidget.UsesOnlyValueSources())
            {
                return GetUnavailableBinaryValueLayoutBounds(binary, context);
            }
        }

        if (HasExplicitContainer(widget))
        {
            return Normalize(new SKRect(
                widget.X,
                widget.Y,
                widget.X + widget.Width,
                widget.Y + widget.Height));
        }

        if (widget is CanonicalDashboard.ValueWidgetDefinition value && value.Width > 0f)
            return Normalize(ValueTextLayout.CreateFixedLayout(value, context, context.ResolveValueText(value), value.TextPresentation.ToRenderProfile()).Bounds);

        if (widget is CanonicalDashboard.ValueWidgetDefinition intrinsicValue)
            return GetTextLayoutBounds(intrinsicValue, context, context.ResolveValueText(intrinsicValue));

        return new SKRect(
            widget.X - MinimumPointExtent / 2f,
            widget.Y - MinimumPointExtent / 2f,
            widget.X + MinimumPointExtent / 2f,
            widget.Y + MinimumPointExtent / 2f);
    }

    public static SKRect GetContainerBounds(CanonicalWidgetDefinition widget, WidgetRenderContext context) =>
        GetLayoutBounds(widget, context);

    public static float GetEffectiveCornerRadius(CanonicalWidgetDefinition widget, SKRect bounds)
    {
        if (widget.CornerRadius <= 0f || bounds.Width <= 0f || bounds.Height <= 0f)
            return 0f;

        return Math.Min(widget.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2f);
    }

    public static bool HasExplicitContainer(CanonicalWidgetDefinition widget) =>
        widget is not CanonicalDashboard.ValueWidgetDefinition;

    public static SKRect GetVisualBounds(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var isTextContent = IsType(widget, WidgetTypeContract.Value) || UsesStateValueLayout(widget, context);
        var rect = GetContainerBounds(widget, context);

        var textOutlineWidth = GetTextOutlineWidth(widget, context);
        if (isTextContent && textOutlineWidth > 0)
        {
            var inflate = textOutlineWidth / 2f + 1f;
            if (widget.Width > 0f)
                rect = new SKRect(rect.Left, rect.Top - inflate, rect.Right, rect.Bottom + inflate);
            else
                rect = Inflate(rect, inflate);
        }

        if (context.GetContainerResources(widget).BorderPaint is not null)
            rect = Inflate(rect, widget.BorderWidth / 2f + 1f);

        return Normalize(rect);
    }

    public static SKPoint GetRotationCenter(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var layout = GetLayoutBounds(widget, context);
        return new SKPoint(layout.MidX, layout.MidY);
    }

    public static SKPoint[] GetRotatedVisualCorners(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var bounds = GetVisualBounds(widget, context);
        var corners = new[]
        {
            new SKPoint(bounds.Left, bounds.Top),
            new SKPoint(bounds.Right, bounds.Top),
            new SKPoint(bounds.Right, bounds.Bottom),
            new SKPoint(bounds.Left, bounds.Bottom)
        };

        var rotation = GetNormalizedRotation(widget);
        if (rotation == 0f)
            return corners;

        var center = GetRotationCenter(widget, context);
        for (var i = 0; i < corners.Length; i++)
            corners[i] = RotatePoint(corners[i], center, rotation);

        return corners;
    }

    public static SKRect GetRotatedVisualBounds(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var bounds = GetVisualBounds(widget, context);
        var rotation = GetNormalizedRotation(widget);

        SKRect rect;
        if (rotation == 0f)
        {
            rect = bounds;
        }
        else
        {
            var center = GetRotationCenter(widget, context);
            var p0 = RotatePoint(new SKPoint(bounds.Left, bounds.Top), center, rotation);
            var p1 = RotatePoint(new SKPoint(bounds.Right, bounds.Top), center, rotation);
            var p2 = RotatePoint(new SKPoint(bounds.Right, bounds.Bottom), center, rotation);
            var p3 = RotatePoint(new SKPoint(bounds.Left, bounds.Bottom), center, rotation);

            var left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
            var top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
            var right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
            var bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
            rect = new SKRect(left, top, right, bottom);
        }

        // Shadow offsets are defined in canvas coordinates. The visible widget is rotated
        // first; the shadow is then displaced without rotating the offset vector.
        if (context.GetShadowResources(widget) is not null)
        {
            var spread = Math.Max(0f, widget.ShadowBlur) * 3f + 1f;
            var shadow = new SKRect(
                rect.Left + widget.ShadowOffsetX - spread,
                rect.Top + widget.ShadowOffsetY - spread,
                rect.Right + widget.ShadowOffsetX + spread,
                rect.Bottom + widget.ShadowOffsetY + spread);
            rect = Union(rect, shadow);
        }

        return Normalize(rect);
    }

    public static bool HitTest(CanonicalWidgetDefinition widget, WidgetRenderContext context, float x, float y)
    {
        var point = new SKPoint(x, y);
        var rotation = GetNormalizedRotation(widget);
        if (rotation != 0f)
        {
            var center = GetRotationCenter(widget, context);
            point = RotatePoint(point, center, -rotation);
        }

        var bounds = GetVisualBounds(widget, context);
        return point.X >= bounds.Left && point.X <= bounds.Right &&
               point.Y >= bounds.Top && point.Y <= bounds.Bottom;
    }

    private static SKRect GetUnavailableBinaryValueLayoutBounds(
        CanonicalDashboard.BinaryWidgetDefinition widget,
        WidgetRenderContext context)
    {
        var text = context.FormatMetric(widget);
        SKRect? bounds = null;
        foreach (var profile in widget.Profiles.Values)
        {
            if (!CanonicalDashboard.StateContentType.IsValue(profile.ContentType))
                continue;

            var current = ValueTextLayout.ResolveContainerBounds(
                widget,
                context,
                text,
                profile.TextPresentation.ToRenderProfile(),
                autoHeight: true);
            bounds = bounds is null ? current : Union(bounds.Value, current);
        }

        return Normalize(bounds ?? new SKRect(
            widget.X - MinimumPointExtent / 2f,
            widget.Y - MinimumPointExtent / 2f,
            widget.X + MinimumPointExtent / 2f,
            widget.Y + MinimumPointExtent / 2f));
    }

    private static float GetTextOutlineWidth(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        if (widget is CanonicalDashboard.ValueWidgetDefinition value)
            return value.TextPresentation.OutlineWidth;

        if (widget is not CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
            return 0f;

        if (context.TryResolveStateVisualProfile(widget, out _, out var activeProfile) &&
            StateVisualProfileContract.IsValueSource(activeProfile.SourceType))
        {
            return activeProfile.Text?.OutlineWidth ?? 0f;
        }

        if (!stateWidget.UsesOnlyValueSources())
            return 0f;

        return stateWidget.Profiles.Values
            .Where(profile => CanonicalDashboard.StateContentType.IsValue(profile.ContentType))
            .Select(profile => profile.TextPresentation.OutlineWidth)
            .DefaultIfEmpty(0f)
            .Max();
    }

    private static bool UsesStateValueLayout(CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        if (widget is not CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
            return false;

        if (context.TryResolveStateVisualProfile(widget, out _, out var profile))
            return StateVisualProfileContract.IsValueSource(profile.SourceType);

        return widget is CanonicalDashboard.BinaryWidgetDefinition && stateWidget.UsesOnlyValueSources();
    }

    private static SKRect GetTextLayoutBounds(
        CanonicalDashboard.ValueWidgetDefinition widget,
        WidgetRenderContext context,
        string text)
    {
        var presentation = widget.TextPresentation.ToRenderProfile();
        using var font = new SKFont(context.GetTypeface(presentation), presentation.FontSize);
        var width = Math.Max(MinimumPointExtent, font.MeasureText(text));
        var metrics = font.Metrics;
        var baseline =
            presentation.VerticalAlign.Equals("top", StringComparison.OrdinalIgnoreCase)
                ? widget.Y - metrics.Ascent
                : presentation.VerticalAlign.Equals("middle", StringComparison.OrdinalIgnoreCase) ||
                  presentation.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                    ? widget.Y - ((metrics.Ascent + metrics.Descent) / 2f)
                    : presentation.VerticalAlign.Equals("bottom", StringComparison.OrdinalIgnoreCase)
                        ? widget.Y - metrics.Descent
                        : widget.Y;

        var left =
            presentation.Align.Equals("center", StringComparison.OrdinalIgnoreCase)
                ? widget.X - width / 2f
                : presentation.Align.Equals("right", StringComparison.OrdinalIgnoreCase)
                    ? widget.X - width
                    : widget.X;

        return Normalize(new SKRect(
            left,
            baseline + metrics.Ascent,
            left + width,
            baseline + metrics.Descent));
    }

    private static SKPoint RotatePoint(SKPoint point, SKPoint center, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new SKPoint(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static bool IsType(CanonicalWidgetDefinition widget, string type) =>
        widget.Type.Equals(type, StringComparison.OrdinalIgnoreCase);

    private static SKRect Inflate(SKRect rect, float amount) => new(
        rect.Left - amount,
        rect.Top - amount,
        rect.Right + amount,
        rect.Bottom + amount);

    private static SKRect Union(SKRect a, SKRect b) => new(
        Math.Min(a.Left, b.Left),
        Math.Min(a.Top, b.Top),
        Math.Max(a.Right, b.Right),
        Math.Max(a.Bottom, b.Bottom));

    private static SKRect Normalize(SKRect rect) => new(
        Math.Min(rect.Left, rect.Right),
        Math.Min(rect.Top, rect.Bottom),
        Math.Max(rect.Left, rect.Right),
        Math.Max(rect.Top, rect.Bottom));
}

internal static class WidgetTransform
{


    public static void Render(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        IWidgetRenderer renderer)
    {
        // Shadow is a screen/canvas-space effect. Keep the image filter outside the widget
        // transform so ShadowOffsetX/Y never rotate with the widget itself.
        var shadow = context.GetShadowResources(widget);
        if (shadow is not null)
        {
            if (!shadow.TryDrawCachedComposite(canvas, widget, context, renderer))
                WidgetEffects.RenderShadowUncached(canvas, widget, context, renderer, shadow.LayerPaint);
            return;
        }

        RenderVisible(canvas, widget, context, renderer);
    }

    internal static void RenderVisible(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        IWidgetRenderer renderer)
    {
        var rotation = WidgetGeometry.GetNormalizedRotation(widget);
        if (rotation == 0f)
        {
            WidgetEffects.RenderVisible(canvas, widget, context, renderer);
            return;
        }

        var center = WidgetGeometry.GetRotationCenter(widget, context);
        canvas.Save();
        try
        {
            canvas.RotateDegrees(rotation, center.X, center.Y);
            WidgetEffects.RenderVisible(canvas, widget, context, renderer);
        }
        finally
        {
            canvas.Restore();
        }
    }
}

internal static class WidgetEffects
{
    public static void RenderShadowUncached(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        IWidgetRenderer renderer,
        SKPaint layerPaint)
    {
        // A bounded layer avoids allocating a full-canvas offscreen surface for every
        // shadowed widget. The bounds already include rotation, blur spread, and the
        // canvas-space shadow offset.
        var layerBounds = WidgetGeometry.GetRotatedVisualBounds(widget, context);
        canvas.SaveLayer(layerBounds, layerPaint);
        try
        {
            WidgetTransform.RenderVisible(canvas, widget, context, renderer);
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal static void RenderVisible(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        IWidgetRenderer renderer)
    {
        var container = context.GetContainerResources(widget);
        if (container.BackgroundPaint is null &&
            container.BorderPaint is null &&
            widget.CornerRadius <= 0f)
        {
            renderer.Render(canvas, widget, context);
            return;
        }

        var bounds = WidgetGeometry.GetContainerBounds(widget, context);
        var radius = WidgetGeometry.GetEffectiveCornerRadius(widget, bounds);

        DrawContainerBackground(canvas, container, bounds, radius);

        // CornerRadius defines the container shape for every widget type. Only a positive
        // radius needs an explicit clip; square-layout renderers already stay in bounds.
        if (radius > 0f)
        {
            var clip = container.ClipRoundRect;
            clip.SetRect(bounds, radius, radius);
            canvas.Save();
            try
            {
                canvas.ClipRoundRect(clip, SKClipOperation.Intersect, context.Antialias);
                renderer.Render(canvas, widget, context);
            }
            finally
            {
                canvas.Restore();
            }
        }
        else
        {
            renderer.Render(canvas, widget, context);
        }

        DrawContainerBorder(canvas, widget, container, bounds, radius);
    }

    private static void DrawContainerBackground(
        SKCanvas canvas,
        WidgetRenderContext.ContainerRenderResources container,
        SKRect bounds,
        float radius)
    {
        var paint = container.BackgroundPaint;
        if (paint is null)
            return;

        if (radius > 0f)
            canvas.DrawRoundRect(bounds, radius, radius, paint);
        else
            canvas.DrawRect(bounds, paint);
    }

    private static void DrawContainerBorder(
        SKCanvas canvas,
        CanonicalWidgetDefinition widget,
        WidgetRenderContext.ContainerRenderResources container,
        SKRect bounds,
        float radius)
    {
        var paint = container.BorderPaint;
        if (paint is null)
            return;

        var inset = Math.Min(widget.BorderWidth / 2f, Math.Min(bounds.Width, bounds.Height) / 2f);
        var borderBounds = new SKRect(
            bounds.Left + inset, bounds.Top + inset,
            bounds.Right - inset, bounds.Bottom - inset);
        if (borderBounds.Width <= 0f || borderBounds.Height <= 0f)
            return;

        var borderRadius = Math.Min(
            Math.Max(0f, radius - inset),
            Math.Min(borderBounds.Width, borderBounds.Height) / 2f);

        if (borderRadius > 0f)
            canvas.DrawRoundRect(borderBounds, borderRadius, borderRadius, paint);
        else
            canvas.DrawRect(borderBounds, paint);
    }
}
