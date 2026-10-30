using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

// Absolute-value color evaluation, independent of a viewport or chart geometry.
internal sealed class QuantitativeColorScale
{
    public SKColor BaseColor { get; }
    public QuantitativeThreshold[] Thresholds { get; }
    public string Mode { get; }

    public QuantitativeColorScale(C.NumericWidgetDefinition widget)
    {
        BaseColor = ColorParser.Parse(widget.Color);
        Mode = C.ThresholdModeContract.SupportedModes.First(mode =>
            string.Equals(mode, widget.Thresholds.Mode, StringComparison.OrdinalIgnoreCase));
        Thresholds = CompileThresholds(widget.Thresholds);
    }

    public static QuantitativeThreshold[] CompileThresholds(C.ThresholdSetDefinition definition) =>
        definition.Items.Where(t => t.Enabled)
            .Select(t => new QuantitativeThreshold(t.Value, ColorParser.Parse(t.Color)))
            .OrderBy(t => t.Value).ToArray();

    public static SKColor StateColor(SKColor baseColor, IReadOnlyList<QuantitativeThreshold> thresholds, double value)
    {
        var color = baseColor;
        for (var i = 0; i < thresholds.Count; i++)
        {
            var threshold = thresholds[i];
            if (value < threshold.Value) break;
            color = threshold.Color;
        }
        return color;
    }

    public SKColor At(double value, double anchor)
    {
        if (Mode != C.ThresholdModeContract.SegmentTransition)
            return StateColor(BaseColor, Thresholds, value);
        var previousValue = anchor;
        var previousColor = BaseColor;
        foreach (var threshold in Thresholds)
        {
            if (value < threshold.Value)
                return Interpolate(previousColor, threshold.Color,
                    GraphRange.Fraction(value, previousValue, threshold.Value));
            previousValue = threshold.Value;
            previousColor = threshold.Color;
        }
        return previousColor;
    }

    private static SKColor Interpolate(SKColor a, SKColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte Channel(byte x, byte y, double f) => (byte)Math.Round(x * (1 - f) + y * f);
        return new(Channel(a.Red, b.Red, t), Channel(a.Green, b.Green, t),
            Channel(a.Blue, b.Blue, t), Channel(a.Alpha, b.Alpha, t));
    }

    public SKShader CreateShader(GraphRange range, SKRect bounds, double anchor)
    {
        var positions = new List<float>(8) { 0 };
        var colors = new List<SKColor>(8) { At(range.Min, anchor) };
        var previous = colors[0];
        foreach (var threshold in Thresholds)
        {
            if (threshold.Value <= range.Min || threshold.Value > range.Max) continue;
            var position = (float)range.Normalize(threshold.Value);
            if (Mode != C.ThresholdModeContract.SegmentTransition)
            {
                positions.Add(position); colors.Add(previous);
            }
            positions.Add(position); colors.Add(threshold.Color);
            previous = threshold.Color;
        }
        positions.Add(1); colors.Add(At(range.Max, anchor));
        return SKShader.CreateLinearGradient(new(bounds.MidX, bounds.Bottom), new(bounds.MidX, bounds.Top),
            colors.ToArray(), positions.ToArray(), SKShaderTileMode.Clamp);
    }
}

internal readonly record struct GraphRange(double Min, double Max)
{
    // Scaling first avoids overflow for opposite finite endpoints and keeps subnormals usable.
    public static double Fraction(double value, double lower, double upper)
    {
        if (lower == upper) return value >= upper ? 1 : 0;
        var span = upper - lower;
        if (double.IsFinite(span) && span != 0) return (value - lower) / span;
        var scale = Math.Max(Math.Abs(lower), Math.Abs(upper));
        return (value / scale - lower / scale) / (upper / scale - lower / scale);
    }
    public double Normalize(double value) => Math.Clamp(Fraction(value, Min, Max), 0, 1);
    public static GraphRange Auto(double min, double max)
    {
        if (min < max) return new(min, max);
        var padding = Math.Max(1e-6, Math.Abs(min) * 1e-6);
        var lower = Math.Max(-double.MaxValue, min - padding);
        var upper = Math.Min(double.MaxValue, max + padding);
        if (!(lower < upper))
        {
            lower = min == -double.MaxValue ? min : double.BitDecrement(min);
            upper = max == double.MaxValue ? max : double.BitIncrement(max);
        }
        return new(lower, upper);
    }
}
