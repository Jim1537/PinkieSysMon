using SkiaSharp;
using PinkieSysMon;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal readonly record struct QuantitativeThreshold(double Value, SKColor Color);
internal readonly record struct QuantitativeStop(double Value, SKColor Color);

internal enum QuantitativeThresholdMode
{
    SegmentSolid,
    SegmentTransition,
    State
}

internal sealed class CompiledQuantitativeIndicator
{
    private readonly double _min;
    private readonly double _inverseRange;

    private CompiledQuantitativeIndicator(
        double min,
        double inverseRange,
        SKColor baseColor,
        QuantitativeThresholdMode mode,
        QuantitativeThreshold[] thresholds,
        QuantitativeStop[] solidStops,
        float[] transitionPositions,
        SKColor[] transitionColors,
        float[] reverseTransitionPositions,
        SKColor[] reverseTransitionColors)
    {
        _min = min;
        _inverseRange = inverseRange;
        BaseColor = baseColor;
        Mode = mode;
        Thresholds = thresholds;
        SolidStops = solidStops;
        TransitionPositions = transitionPositions;
        TransitionColors = transitionColors;
        ReverseTransitionPositions = reverseTransitionPositions;
        ReverseTransitionColors = reverseTransitionColors;
    }

    public SKColor BaseColor { get; }
    public QuantitativeThresholdMode Mode { get; }
    public IReadOnlyList<QuantitativeThreshold> Thresholds { get; }
    public IReadOnlyList<QuantitativeStop> SolidStops { get; }
    public float[] TransitionPositions { get; }
    public SKColor[] TransitionColors { get; }
    public float[] ReverseTransitionPositions { get; }
    public SKColor[] ReverseTransitionColors { get; }

    public static CompiledQuantitativeIndicator Create(CanonicalDashboard.QuantitativeWidgetDefinition widget, string baseColor)
    {
        var thresholds = widget.Thresholds.Items
            .Where(item => item.Enabled)
            .Select(item => new QuantitativeThreshold(item.Value, ColorParser.Parse(item.Color)))
            .OrderBy(item => item.Value)
            .ToList();

        var parsedBaseColor = ColorParser.Parse(baseColor);
        var solidStops = new QuantitativeStop[thresholds.Count + 1];
        solidStops[0] = new QuantitativeStop(widget.Min, parsedBaseColor);
        for (var i = 0; i < thresholds.Count; i++)
            solidStops[i + 1] = new QuantitativeStop(thresholds[i].Value, thresholds[i].Color);

        var positions = new List<float>(thresholds.Count + 2) { 0f };
        var colors = new List<SKColor>(thresholds.Count + 2) { parsedBaseColor };
        var inverseRange = 1.0 / (widget.Max - widget.Min);
        foreach (var threshold in thresholds)
        {
            positions.Add(ToRatio(widget.Min, inverseRange, threshold.Value));
            colors.Add(threshold.Color);
        }

        if (positions[^1] < 1f)
        {
            positions.Add(1f);
            colors.Add(colors[^1]);
        }

        var transitionPositions = positions.ToArray();
        var transitionColors = colors.ToArray();
        var reversePositions = transitionPositions
            .Reverse()
            .Select(position => 1f - position)
            .ToArray();
        var reverseColors = transitionColors.Reverse().ToArray();

        var mode = widget.Thresholds.Mode switch
        {
            "State" => QuantitativeThresholdMode.State,
            "SegmentTransition" => QuantitativeThresholdMode.SegmentTransition,
            _ => QuantitativeThresholdMode.SegmentSolid
        };

        return new CompiledQuantitativeIndicator(
            widget.Min,
            inverseRange,
            parsedBaseColor,
            mode,
            thresholds.ToArray(),
            solidStops,
            transitionPositions,
            transitionColors,
            reversePositions,
            reverseColors);
    }

    public double GetRatio(double value) =>
        Math.Clamp((value - _min) * _inverseRange, 0.0, 1.0);

    public float GetRatioFloat(double value) =>
        ToRatio(_min, _inverseRange, value);

    public SKColor GetStateColor(double value)
    {
        var color = BaseColor;
        foreach (var threshold in Thresholds)
        {
            if (value < threshold.Value)
                break;
            color = threshold.Color;
        }
        return color;
    }

    private static float ToRatio(double min, double inverseRange, double value) =>
        (float)Math.Clamp((value - min) * inverseRange, 0.0, 1.0);
}
