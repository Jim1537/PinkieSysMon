using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class GaugeWidgetRenderer : IWidgetRenderer, IDisposable
{
    private const float AngleEpsilon = 0.0001f;
    private const float GeometryEpsilon = 0.0001f;
    private readonly Dictionary<CanonicalWidgetDefinition, CompiledGauge> _compiled = new();

    public string Type => WidgetTypeContract.Gauge;

    public void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var gauge = widget as CanonicalDashboard.GaugeWidgetDefinition
            ?? throw new InvalidDataException($"Gauge renderer received widget type '{widget.Type}'.");
        var compiled = GetCompiled(gauge, context.Antialias);
        var raw = 0d;
        var hasMetric = !string.IsNullOrWhiteSpace(gauge.Metric) &&
                        context.TryGetMetricDouble(gauge, out raw);

        canvas.Save();
        try
        {
            canvas.Translate(gauge.X, gauge.Y);

            if (compiled.TrackEnabled)
            {
                DrawTrack(canvas, compiled);

                if (hasMetric)
                {
                    var ratio = compiled.Indicator.GetRatio(raw);
                    if (ratio > 0.0)
                    {
                        var activeSweep = compiled.SweepAngle * (float)ratio;
                        var activeCornerRadius = ratio >= 1.0 && compiled.FullCircle
                            ? 0f
                            : compiled.TrackCornerRadius;

                        switch (compiled.Indicator.Mode)
                        {
                            case QuantitativeThresholdMode.SegmentTransition:
                                DrawTransitionFill(canvas, compiled, activeSweep, activeCornerRadius);
                                break;

                            case QuantitativeThresholdMode.State:
                            {
                                using var activePath = CreateActivePath(compiled, activeSweep, activeCornerRadius);
                                DrawPath(canvas, activePath, compiled.Indicator.GetStateColor(raw), compiled.FillPaint);
                                break;
                            }

                            default:
                            {
                                using var activePath = CreateActivePath(compiled, activeSweep, activeCornerRadius);
                                DrawSolidSegments(canvas, activePath, compiled);
                                break;
                            }
                        }
                    }
                }

                DrawTrackBorder(canvas, compiled);
            }

            if (compiled.NeedleEnabled && hasMetric)
                DrawNeedle(canvas, compiled, compiled.Indicator.GetRatio(raw));
        }
        finally
        {
            canvas.Restore();
        }
    }

    private CompiledGauge GetCompiled(CanonicalDashboard.GaugeWidgetDefinition widget, bool antialias)
    {
        if (_compiled.TryGetValue(widget, out var compiled))
            return compiled;

        compiled = CompiledGauge.Create(widget, antialias);
        _compiled.Add(widget, compiled);
        return compiled;
    }

    private static SKPath CreateActivePath(CompiledGauge compiled, float activeSweep, float cornerRadius)
    {
        var activeStartAngle = compiled.Reverse
            ? compiled.StartAngle + compiled.SweepAngle - activeSweep
            : compiled.StartAngle;
        return CreateBandPath(
            compiled.Center,
            compiled.TrackOuterRadius,
            compiled.TrackInnerRadius,
            activeStartAngle,
            activeSweep,
            cornerRadius);
    }

    private static void DrawTrack(SKCanvas canvas, CompiledGauge compiled)
    {
        if (compiled.TrackPath is null || compiled.TrackPaint is null)
            return;

        canvas.DrawPath(compiled.TrackPath, compiled.TrackPaint);
    }

    private static void DrawSolidSegments(
        SKCanvas canvas,
        SKPath activePath,
        CompiledGauge compiled)
    {
        canvas.Save();
        try
        {
            canvas.ClipPath(activePath, SKClipOperation.Intersect, compiled.Antialias);
            foreach (var segment in compiled.SolidSegments)
            {
                compiled.FillPaint.Color = segment.Color;
                canvas.DrawPath(segment.Path, compiled.FillPaint);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawTransitionFill(
        SKCanvas canvas,
        CompiledGauge compiled,
        float activeSweep,
        float cornerRadius)
    {
        if (compiled.TransitionPaint is null)
            return;

        var localActiveStart = compiled.Reverse
            ? compiled.SweepAngle - activeSweep
            : 0f;
        using var localPath = CreateBandPath(
            compiled.Center,
            compiled.TrackOuterRadius,
            compiled.TrackInnerRadius,
            localActiveStart,
            activeSweep,
            cornerRadius);

        canvas.Save();
        try
        {
            canvas.RotateDegrees(compiled.StartAngle, compiled.Center.X, compiled.Center.Y);
            canvas.DrawPath(localPath, compiled.TransitionPaint);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawTrackBorder(SKCanvas canvas, CompiledGauge compiled)
    {
        var paint = compiled.TrackBorderPaint;
        if (paint is null)
            return;

        if (compiled.FullCircle)
        {
            if (compiled.TrackBorderOuterCenterRadius > GeometryEpsilon)
                canvas.DrawCircle(compiled.Center.X, compiled.Center.Y, compiled.TrackBorderOuterCenterRadius, paint);
            if (compiled.TrackBorderInnerCenterRadius > GeometryEpsilon)
                canvas.DrawCircle(compiled.Center.X, compiled.Center.Y, compiled.TrackBorderInnerCenterRadius, paint);
            return;
        }

        if (compiled.TrackBorderPath is not null)
            canvas.DrawPath(compiled.TrackBorderPath, paint);
    }

    private static void DrawPath(SKCanvas canvas, SKPath path, SKColor color, SKPaint paint)
    {
        if (color.Alpha == 0)
            return;

        paint.Color = color;
        canvas.DrawPath(path, paint);
    }

    private static void DrawNeedle(SKCanvas canvas, CompiledGauge compiled, double ratio)
    {
        var startRadius = compiled.NeedleStartOffset;
        var endRadius = compiled.OuterRadius - compiled.NeedleEndOffset;
        if (endRadius <= startRadius + GeometryEpsilon)
            return;

        var angle = compiled.Reverse
            ? compiled.StartAngle + compiled.SweepAngle * (1f - (float)ratio)
            : compiled.StartAngle + compiled.SweepAngle * (float)ratio;
        var visibleLength = endRadius - startRadius;
        var pointerLength = Math.Clamp(compiled.NeedlePointerLength, 0f, visibleLength);
        if (pointerLength <= GeometryEpsilon)
        {
            if (compiled.NeedlePaint is not null)
            {
                canvas.DrawLine(
                    PointOnCircle(compiled.Center, startRadius, angle),
                    PointOnCircle(compiled.Center, endRadius, angle),
                    compiled.NeedlePaint);
            }
            return;
        }

        var pointerStartRadius = endRadius - pointerLength;
        if (compiled.NeedlePaint is not null && pointerStartRadius > startRadius + GeometryEpsilon)
        {
            canvas.DrawLine(
                PointOnCircle(compiled.Center, startRadius, angle),
                PointOnCircle(compiled.Center, pointerStartRadius, angle),
                compiled.NeedlePaint);
        }

        if (compiled.NeedlePointerPaint is not null)
        {
            canvas.DrawLine(
                PointOnCircle(compiled.Center, pointerStartRadius, angle),
                PointOnCircle(compiled.Center, endRadius, angle),
                compiled.NeedlePointerPaint);
        }
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

    private sealed class CompiledGauge : IDisposable
    {
        private CompiledGauge(
            CompiledQuantitativeIndicator indicator,
            bool antialias,
            bool trackEnabled,
            bool needleEnabled,
            SKPoint center,
            float outerRadius,
            float trackOuterRadius,
            float trackInnerRadius,
            float startAngle,
            float sweepAngle,
            bool fullCircle,
            float trackCornerRadius,
            bool reverse,
            SKPath? trackPath,
            SKPaint? trackPaint,
            float trackBorderOuterCenterRadius,
            float trackBorderInnerCenterRadius,
            SKPath? trackBorderPath,
            SKPaint? trackBorderPaint,
            CompiledGaugeSegment[] solidSegments,
            SKShader? transitionShader,
            SKPaint? transitionPaint,
            SKPaint fillPaint,
            float needleStartOffset,
            float needleEndOffset,
            float needlePointerLength,
            SKPaint? needlePaint,
            SKPaint? needlePointerPaint)
        {
            Indicator = indicator;
            Antialias = antialias;
            TrackEnabled = trackEnabled;
            NeedleEnabled = needleEnabled;
            Center = center;
            OuterRadius = outerRadius;
            TrackOuterRadius = trackOuterRadius;
            TrackInnerRadius = trackInnerRadius;
            StartAngle = startAngle;
            SweepAngle = sweepAngle;
            FullCircle = fullCircle;
            TrackCornerRadius = trackCornerRadius;
            Reverse = reverse;
            TrackPath = trackPath;
            TrackPaint = trackPaint;
            TrackBorderOuterCenterRadius = trackBorderOuterCenterRadius;
            TrackBorderInnerCenterRadius = trackBorderInnerCenterRadius;
            TrackBorderPath = trackBorderPath;
            TrackBorderPaint = trackBorderPaint;
            SolidSegments = solidSegments;
            TransitionShader = transitionShader;
            TransitionPaint = transitionPaint;
            FillPaint = fillPaint;
            NeedleStartOffset = needleStartOffset;
            NeedleEndOffset = needleEndOffset;
            NeedlePointerLength = needlePointerLength;
            NeedlePaint = needlePaint;
            NeedlePointerPaint = needlePointerPaint;
        }

        public CompiledQuantitativeIndicator Indicator { get; }
        public bool Antialias { get; }
        public bool TrackEnabled { get; }
        public bool NeedleEnabled { get; }
        public SKPoint Center { get; }
        public float OuterRadius { get; }
        public float TrackOuterRadius { get; }
        public float TrackInnerRadius { get; }
        public float StartAngle { get; }
        public float SweepAngle { get; }
        public bool FullCircle { get; }
        public float TrackCornerRadius { get; }
        public bool Reverse { get; }
        public SKPath? TrackPath { get; }
        public SKPaint? TrackPaint { get; }
        public float TrackBorderOuterCenterRadius { get; }
        public float TrackBorderInnerCenterRadius { get; }
        public SKPath? TrackBorderPath { get; }
        public SKPaint? TrackBorderPaint { get; }
        public IReadOnlyList<CompiledGaugeSegment> SolidSegments { get; }
        public SKShader? TransitionShader { get; }
        public SKPaint? TransitionPaint { get; }
        public SKPaint FillPaint { get; }
        public float NeedleStartOffset { get; }
        public float NeedleEndOffset { get; }
        public float NeedlePointerLength { get; }
        public SKPaint? NeedlePaint { get; }
        public SKPaint? NeedlePointerPaint { get; }

        public static CompiledGauge Create(CanonicalDashboard.GaugeWidgetDefinition widget, bool antialias)
        {
            SKPath? trackPath = null;
            SKPaint? trackPaint = null;
            SKPath? trackBorderPath = null;
            SKPaint? trackBorderPaint = null;
            SKShader? transitionShader = null;
            SKPaint? transitionPaint = null;
            SKPaint? fillPaint = null;
            SKPaint? needlePaint = null;
            SKPaint? needlePointerPaint = null;
            CompiledGaugeSegment[] solidSegments = [];

            try
            {
                var center = new SKPoint(widget.Width / 2f, widget.Width / 2f);
                var outerRadius = widget.Width / 2f;
                var trackEnvelopeDepth = 2f * widget.Track.BorderWidth + 2f * widget.Gap + widget.Track.Thickness;
                var trackEnvelopeInnerRadius = Math.Max(0f, outerRadius - trackEnvelopeDepth);
                var trackOuterRadius = Math.Max(0f, outerRadius - widget.Track.BorderWidth - widget.Gap);
                var trackInnerRadius = Math.Max(0f, trackOuterRadius - widget.Track.Thickness);
                var startAngle = ToSkiaAngle(widget.StartAngle);
                var sweepAngle = widget.EndAngle - widget.StartAngle;
                var fullCircle = IsFullCircle(widget);
                var trackCornerRadius = fullCircle
                    ? 0f
                    : Math.Max(0f, widget.Track.CornerRadius - widget.Track.BorderWidth - widget.Gap);
                var trackBorderOuterCenterRadius = 0f;
                var trackBorderInnerCenterRadius = 0f;
                var indicator = CompiledQuantitativeIndicator.Create(widget, widget.Color);

                if (widget.Track.Enabled)
                {
                    var trackColor = ColorParser.Parse(widget.Track.BackgroundColor);
                    if (trackColor.Alpha > 0 && trackOuterRadius > GeometryEpsilon && trackOuterRadius > trackInnerRadius)
                    {
                        trackPath = CreateBandPath(
                            center, trackOuterRadius, trackInnerRadius, startAngle, sweepAngle, trackCornerRadius);
                        trackPaint = new SKPaint
                        {
                            IsAntialias = antialias,
                            Color = trackColor,
                            Style = SKPaintStyle.Fill
                        };
                    }

                    if (indicator.Mode == QuantitativeThresholdMode.SegmentSolid)
                    {
                        solidSegments = BuildSolidSegments(
                            widget, indicator, center, trackOuterRadius, trackInnerRadius, startAngle, sweepAngle);
                    }

                    if (indicator.Mode == QuantitativeThresholdMode.SegmentTransition)
                    {
                        var positions = widget.Reverse
                            ? indicator.ReverseTransitionPositions
                            : indicator.TransitionPositions;
                        var colors = widget.Reverse
                            ? indicator.ReverseTransitionColors
                            : indicator.TransitionColors;
                        transitionShader = SKShader.CreateSweepGradient(
                            center,
                            colors,
                            positions,
                            SKShaderTileMode.Clamp,
                            0f,
                            sweepAngle);
                        transitionPaint = new SKPaint
                        {
                            IsAntialias = antialias,
                            Style = SKPaintStyle.Fill,
                            Shader = transitionShader
                        };
                    }

                    var trackBorderColor = ColorParser.Parse(widget.Track.BorderColor);
                    trackBorderOuterCenterRadius = Math.Max(0f, outerRadius - widget.Track.BorderWidth / 2f);
                    trackBorderInnerCenterRadius = Math.Max(0f, trackEnvelopeInnerRadius + widget.Track.BorderWidth / 2f);
                    if (!fullCircle && widget.Track.BorderWidth > 0f && trackBorderColor.Alpha > 0 &&
                        trackBorderOuterCenterRadius > trackBorderInnerCenterRadius + GeometryEpsilon)
                    {
                        var centerlineCornerRadius = Math.Max(0f, widget.Track.CornerRadius - widget.Track.BorderWidth / 2f);
                        trackBorderPath = CreateBandPath(
                            center,
                            trackBorderOuterCenterRadius,
                            trackBorderInnerCenterRadius,
                            startAngle,
                            sweepAngle,
                            centerlineCornerRadius);
                    }

                    if (widget.Track.BorderWidth > 0f && trackBorderColor.Alpha > 0)
                    {
                        trackBorderPaint = new SKPaint
                        {
                            IsAntialias = antialias,
                            Color = trackBorderColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = widget.Track.BorderWidth,
                            StrokeCap = SKStrokeCap.Butt,
                            StrokeJoin = SKStrokeJoin.Round
                        };
                    }
                }

                if (widget.Needle.Enabled)
                {
                    var needleColor = ColorParser.Parse(widget.Needle.Color);
                    if (needleColor.Alpha > 0 && widget.Needle.Thickness > 0f)
                    {
                        needlePaint = new SKPaint
                        {
                            IsAntialias = antialias,
                            Color = needleColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = widget.Needle.Thickness,
                            StrokeCap = SKStrokeCap.Butt
                        };
                    }

                    var pointerColor = ColorParser.Parse(widget.Needle.Pointer.Color);
                    if (widget.Needle.Pointer.Length > 0f && pointerColor.Alpha > 0 && widget.Needle.Pointer.Thickness > 0f)
                    {
                        needlePointerPaint = new SKPaint
                        {
                            IsAntialias = antialias,
                            Color = pointerColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = widget.Needle.Pointer.Thickness,
                            StrokeCap = SKStrokeCap.Butt
                        };
                    }
                }

                fillPaint = new SKPaint
                {
                    IsAntialias = antialias,
                    Style = SKPaintStyle.Fill
                };

                return new CompiledGauge(
                    indicator,
                    antialias,
                    widget.Track.Enabled,
                    widget.Needle.Enabled,
                    center,
                    outerRadius,
                    trackOuterRadius,
                    trackInnerRadius,
                    startAngle,
                    sweepAngle,
                    fullCircle,
                    trackCornerRadius,
                    widget.Reverse,
                    trackPath,
                    trackPaint,
                    trackBorderOuterCenterRadius,
                    trackBorderInnerCenterRadius,
                    trackBorderPath,
                    trackBorderPaint,
                    solidSegments,
                    transitionShader,
                    transitionPaint,
                    fillPaint,
                    widget.Needle.StartOffset,
                    widget.Needle.EndOffset,
                    widget.Needle.Pointer.Length,
                    needlePaint,
                    needlePointerPaint);
            }
            catch
            {
                needlePointerPaint?.Dispose();
                needlePaint?.Dispose();
                fillPaint?.Dispose();
                transitionPaint?.Dispose();
                transitionShader?.Dispose();
                trackBorderPaint?.Dispose();
                trackBorderPath?.Dispose();
                foreach (var segment in solidSegments)
                    segment.Path.Dispose();
                trackPaint?.Dispose();
                trackPath?.Dispose();
                throw;
            }
        }

        private static CompiledGaugeSegment[] BuildSolidSegments(
            CanonicalDashboard.GaugeWidgetDefinition widget,
            CompiledQuantitativeIndicator indicator,
            SKPoint center,
            float outerRadius,
            float innerRadius,
            float startAngle,
            float sweepAngle)
        {
            var stops = indicator.SolidStops;
            var segments = new List<CompiledGaugeSegment>(stops.Count);
            try
            {
                for (var i = 0; i < stops.Count; i++)
                {
                    var startRatio = indicator.GetRatioFloat(stops[i].Value);
                    var endValue = i + 1 < stops.Count ? stops[i + 1].Value : widget.Max;
                    var endRatio = indicator.GetRatioFloat(endValue);
                    if (endRatio <= startRatio + AngleEpsilon)
                        continue;

                    var segmentSweep = sweepAngle * (endRatio - startRatio);
                    var segmentStartAngle = widget.Reverse
                        ? startAngle + sweepAngle * (1f - endRatio)
                        : startAngle + sweepAngle * startRatio;
                    var path = CreateBandPath(
                        center,
                        outerRadius,
                        innerRadius,
                        segmentStartAngle,
                        segmentSweep,
                        0f);
                    segments.Add(new CompiledGaugeSegment(path, stops[i].Color));
                }

                return segments.ToArray();
            }
            catch
            {
                foreach (var segment in segments)
                    segment.Path.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            NeedlePointerPaint?.Dispose();
            NeedlePaint?.Dispose();
            FillPaint.Dispose();
            TransitionPaint?.Dispose();
            TransitionShader?.Dispose();
            TrackBorderPaint?.Dispose();
            TrackBorderPath?.Dispose();
            foreach (var segment in SolidSegments)
                segment.Path.Dispose();
            TrackPaint?.Dispose();
            TrackPath?.Dispose();
        }
    }

    private sealed record CompiledGaugeSegment(SKPath Path, SKColor Color);
    private static SKPath CreateBandPath(
        SKPoint center,
        float outerRadius,
        float innerRadius,
        float startAngle,
        float sweepAngle,
        float requestedCornerRadius)
    {
        using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        if (outerRadius <= GeometryEpsilon || sweepAngle <= AngleEpsilon)
            return builder.Detach();

        innerRadius = Math.Clamp(innerRadius, 0f, outerRadius);

        if (sweepAngle >= 360f - AngleEpsilon)
        {
            builder.AddCircle(center.X, center.Y, outerRadius, SKPathDirection.Clockwise);
            if (innerRadius > GeometryEpsilon)
                builder.AddCircle(center.X, center.Y, innerRadius, SKPathDirection.CounterClockwise);
            return builder.Detach();
        }

        if (innerRadius <= GeometryEpsilon)
        {
            var outerRect = CircleBounds(center, outerRadius);
            var start = PointOnCircle(center, outerRadius, startAngle);
            builder.MoveTo(center);
            builder.LineTo(start);
            builder.ArcTo(outerRect, startAngle, sweepAngle, false);
            builder.LineTo(center);
            builder.Close();
            return builder.Detach();
        }

        var cornerRadius = GetEffectiveCornerRadius(
            outerRadius,
            innerRadius,
            sweepAngle,
            requestedCornerRadius);
        if (cornerRadius <= GeometryEpsilon)
        {
            var outerRect = CircleBounds(center, outerRadius);
            var innerRect = CircleBounds(center, innerRadius);
            builder.MoveTo(PointOnCircle(center, outerRadius, startAngle));
            builder.ArcTo(outerRect, startAngle, sweepAngle, false);
            builder.LineTo(PointOnCircle(center, innerRadius, startAngle + sweepAngle));
            builder.ArcTo(innerRect, startAngle + sweepAngle, -sweepAngle, false);
            builder.Close();
            return builder.Detach();
        }

        var outerRadial = MathF.Sqrt(Math.Max(0f, outerRadius * outerRadius - 2f * outerRadius * cornerRadius));
        var innerRadial = MathF.Sqrt(Math.Max(0f, innerRadius * innerRadius + 2f * innerRadius * cornerRadius));
        var outerDelta = RadiansToDegrees(MathF.Atan2(cornerRadius, outerRadial));
        var innerDelta = RadiansToDegrees(MathF.Atan2(cornerRadius, innerRadial));
        var endAngle = startAngle + sweepAngle;

        var outerStart = PointOnCircle(center, outerRadius, startAngle + outerDelta);
        builder.MoveTo(outerStart);
        builder.ArcTo(
            CircleBounds(center, outerRadius),
            startAngle + outerDelta,
            sweepAngle - 2f * outerDelta,
            false);

        var endOuterTangent = PointOnCircle(center, outerRadius, endAngle - outerDelta);
        var endOuterCenter = OffsetPolar(center, outerRadial, -cornerRadius, endAngle);
        var endOuterRadial = PointOnCircle(center, outerRadial, endAngle);
        AppendClockwiseCircleArc(builder, endOuterCenter, cornerRadius, endOuterTangent, endOuterRadial);

        var endInnerRadial = PointOnCircle(center, innerRadial, endAngle);
        builder.LineTo(endInnerRadial);
        var endInnerCenter = OffsetPolar(center, innerRadial, -cornerRadius, endAngle);
        var endInnerTangent = PointOnCircle(center, innerRadius, endAngle - innerDelta);
        AppendClockwiseCircleArc(builder, endInnerCenter, cornerRadius, endInnerRadial, endInnerTangent);

        builder.ArcTo(
            CircleBounds(center, innerRadius),
            endAngle - innerDelta,
            -(sweepAngle - 2f * innerDelta),
            false);

        var startInnerTangent = PointOnCircle(center, innerRadius, startAngle + innerDelta);
        var startInnerCenter = OffsetPolar(center, innerRadial, cornerRadius, startAngle);
        var startInnerRadial = PointOnCircle(center, innerRadial, startAngle);
        AppendClockwiseCircleArc(builder, startInnerCenter, cornerRadius, startInnerTangent, startInnerRadial);

        var startOuterRadial = PointOnCircle(center, outerRadial, startAngle);
        builder.LineTo(startOuterRadial);
        var startOuterCenter = OffsetPolar(center, outerRadial, cornerRadius, startAngle);
        AppendClockwiseCircleArc(builder, startOuterCenter, cornerRadius, startOuterRadial, outerStart);
        builder.Close();
        return builder.Detach();
    }

    private static float GetEffectiveCornerRadius(
        float outerRadius,
        float innerRadius,
        float sweepAngle,
        float requested)
    {
        if (requested <= 0f || outerRadius <= innerRadius)
            return 0f;

        var maximum = (outerRadius - innerRadius) / 2f;
        if (sweepAngle < 180f - AngleEpsilon)
        {
            var halfSweepRadians = DegreesToRadians(sweepAngle / 2f);
            var sin = MathF.Sin(halfSweepRadians);
            if (sin > float.Epsilon)
            {
                maximum = Math.Min(maximum, outerRadius * sin / (1f + sin));
                if (innerRadius > GeometryEpsilon && sin < 1f - float.Epsilon)
                    maximum = Math.Min(maximum, innerRadius * sin / (1f - sin));
            }
        }

        return Math.Clamp(requested, 0f, Math.Max(0f, maximum));
    }

    private static void AppendClockwiseCircleArc(
        SKPathBuilder builder,
        SKPoint center,
        float radius,
        SKPoint from,
        SKPoint to)
    {
        if (radius <= GeometryEpsilon)
        {
            builder.LineTo(to);
            return;
        }

        var start = VectorAngle(center, from);
        var end = VectorAngle(center, to);
        var sweep = end - start;
        while (sweep <= 0f)
            sweep += 360f;
        if (sweep >= 360f)
            sweep %= 360f;

        builder.ArcTo(CircleBounds(center, radius), start, sweep, false);
    }

    private static SKPoint OffsetPolar(SKPoint center, float radial, float tangent, float angleDegrees)
    {
        var radians = DegreesToRadians(angleDegrees);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var radialX = cos;
        var radialY = sin;
        var tangentX = -sin;
        var tangentY = cos;
        return new SKPoint(
            center.X + radial * radialX + tangent * tangentX,
            center.Y + radial * radialY + tangent * tangentY);
    }

    private static SKPoint PointOnCircle(SKPoint center, float radius, float angleDegrees)
    {
        var radians = DegreesToRadians(angleDegrees);
        return new SKPoint(
            center.X + radius * MathF.Cos(radians),
            center.Y + radius * MathF.Sin(radians));
    }

    private static SKRect CircleBounds(SKPoint center, float radius) => new(
        center.X - radius,
        center.Y - radius,
        center.X + radius,
        center.Y + radius);

    private static float VectorAngle(SKPoint center, SKPoint point)
    {
        var angle = RadiansToDegrees(MathF.Atan2(point.Y - center.Y, point.X - center.X));
        return angle < 0f ? angle + 360f : angle;
    }

    private static float ToSkiaAngle(float gaugeAngle) => gaugeAngle + 90f;

    private static bool IsFullCircle(CanonicalDashboard.GaugeWidgetDefinition widget) =>
        Math.Abs(widget.StartAngle) <= AngleEpsilon &&
        Math.Abs(widget.EndAngle - 360f) <= AngleEpsilon;

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
    private static float RadiansToDegrees(float radians) => radians * 180f / MathF.PI;
}
