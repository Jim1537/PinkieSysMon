using System.Drawing;
using PinkieSysMon;
using CanonicalGaugeWidgetDefinition = PinkieSysMon.DashboardModel.GaugeWidgetDefinition;

namespace PinkieSysMon.Editor;

internal static class WidgetResizeGeometry
{
    public const float MinimumDimension = 1f;

    public static float NormalizeLogicalCoordinate(float value)
    {
        if (!float.IsFinite(value))
            return value;

        return MathF.Round(value, MidpointRounding.AwayFromZero);
    }

    public static float NormalizeLogicalDimension(float value) =>
        NormalizeLogicalCoordinate(value);

    public static float NormalizeResizedDimension(float value, float minimum = MinimumDimension)
    {
        var normalizedMinimum = float.IsFinite(minimum)
            ? MathF.Ceiling(Math.Max(MinimumDimension, minimum))
            : MinimumDimension;
        var normalizedValue = NormalizeLogicalDimension(value);
        return float.IsFinite(normalizedValue)
            ? Math.Max(normalizedMinimum, normalizedValue)
            : normalizedMinimum;
    }

    public static PointF ProjectCanvasDeltaToLocal(PointF canvasDelta, double rotationDegrees)
    {
        var radians = (float)(rotationDegrees * Math.PI / 180d);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new PointF(
            canvasDelta.X * cos + canvasDelta.Y * sin,
            -canvasDelta.X * sin + canvasDelta.Y * cos);
    }

    public static float ResolveGaugeSize(
        CanonicalGaugeWidgetDefinition widget,
        float initialSize,
        float localDx,
        float localDy)
    {
        // A square constrained to the pointer is the least-squares projection of the free
        // X/Y resize onto the local (1,1) diagonal, hence the average delta.
        var requested = initialSize + (localDx + localDy) / 2f;
        return Math.Max(GetMinimumGaugeSize(widget), requested);
    }

    public static float GetMinimumGaugeSize(CanonicalGaugeWidgetDefinition widget)
    {
        var minimum = MinimumDimension;

        if (widget.Track.Enabled)
        {
            var radialDepth = 2f * widget.Track.BorderWidth + 2f * widget.Gap + widget.Track.Thickness;
            if (float.IsFinite(radialDepth))
                minimum = Math.Max(minimum, 2f * radialDepth);
        }

        if (widget.Needle.Enabled)
        {
            var offsets = widget.Needle.StartOffset + widget.Needle.EndOffset;
            if (float.IsFinite(offsets))
            {
                // Validation requires offsets < radius, not <= radius. BitIncrement keeps the
                // clamp mathematically on the valid side without inventing a visible margin.
                var strictMinimum = MathF.BitIncrement(2f * offsets);
                minimum = Math.Max(minimum, strictMinimum);
            }
        }

        return minimum;
    }
}
