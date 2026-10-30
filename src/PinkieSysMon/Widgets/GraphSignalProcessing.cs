namespace PinkieSysMon.Widgets;

internal readonly record struct GraphPoint(double X, double Value, bool Connects);

// Per-consumer projection; the shared raw history is never changed. Cache by publication
// sequence so scrolling and repeated frames cannot apply a recursive filter twice.
internal sealed class GraphFilterProjection(string mode, int capacity)
{
    internal readonly record struct Entry(long Sequence, double Value, bool Valid);
    private readonly Entry[] _cache = new Entry[capacity];
    private readonly double[] _window = new double[5];
    private int _head, _count, _windowCount, _windowNext;
    private long _epoch = -1, _lastSequence;
    private double _estimate, _covariance;

    public void Begin(long epoch, ReadOnlySpan<MetricHistorySample> samples)
    {
        if (_epoch != epoch || (samples.Length > 0 && _lastSequence > 0 && samples[0].Sequence > _lastSequence))
        {
            _head = _count = 0; _lastSequence = 0; ResetFilter();
        }
        _epoch = epoch;
    }

    public bool TryResolve(MetricHistorySample sample, MetricDescriptor? descriptor,
        DashboardModel.GraphWidgetDefinition graph, out double value)
    {
        if (sample.Sequence <= _lastSequence)
        {
            var lo = 0; var hi = _count - 1;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2; var entry = _cache[(_head + mid) % _cache.Length];
                if (entry.Sequence == sample.Sequence) { value = entry.Value; return entry.Valid; }
                if (entry.Sequence < sample.Sequence) lo = mid + 1; else hi = mid - 1;
            }
            value = default; return false;
        }
        var valid = MetricValueConverter.TryResolveQuantitative(sample, descriptor,
            graph.Unit, graph.Component, graph.ComponentFormat, out value);
        if (!sample.ConnectsToPrevious || !valid) ResetFilter();
        if (valid) value = Apply(value);
        if (_count == _cache.Length) { _head = (_head + 1) % _cache.Length; _count--; }
        _cache[(_head + _count++) % _cache.Length] = new(sample.Sequence, value, valid);
        _lastSequence = sample.Sequence;
        return valid;
    }

    private void ResetFilter() { _windowCount = _windowNext = 0; _estimate = 0; _covariance = 1; }

    private double Apply(double value)
    {
        if (mode == "Kalman")
        {
            if (_windowCount == 0) { _windowCount = 1; return _estimate = value; }
            // Scalar random walk, normalized by R: Q/R=.1 per publication, P0/R=1.
            // Only covariance ratios affect the gain; units and offsets do not.
            var predicted = _covariance + .1;
            var gain = predicted / (predicted + 1);
            _covariance = 1 - gain;
            return _estimate = GraphBezierSegment.Blend(_estimate, value, gain);
        }
        _window[_windowNext] = value; _windowNext = (_windowNext + 1) % _window.Length;
        _windowCount = Math.Min(_windowCount + 1, _window.Length);
        if (mode == "Median")
        {
            Span<double> sorted = stackalloc double[5];
            _window.AsSpan(0, _windowCount).CopyTo(sorted); sorted = sorted[.._windowCount]; sorted.Sort();
            return _windowCount % 2 == 1 ? sorted[_windowCount / 2]
                : GraphBezierSegment.Blend(sorted[_windowCount / 2 - 1], sorted[_windowCount / 2], .5);
        }
        var mean = _window[0];
        for (var i = 1; i < _windowCount; i++) mean = GraphBezierSegment.Blend(mean, _window[i], 1d / (i + 1));
        return mean;
    }
}

// Cubic Hermite segments represented as Bezier controls. Minmod tangents are
// shared at each knot, respect unequal time spacing and keep controls ordered.
// Thus the curve is C1 within strictly increasing runs and cannot invent peaks.
internal readonly record struct GraphBezierSegment(double X0, double X1, double V0, double C1, double C2, double V1)
{
    public static GraphBezierSegment Create(GraphPoint a, GraphPoint b, GraphPoint? previous, GraphPoint? next)
    {
        var left = 1d / 3; var right = 1d / 3;
        if (previous is { } p) left = TangentFraction(p.Value, a.Value, b.Value, a.X - p.X, b.X - a.X);
        if (next is { } n) right = TangentFraction(n.Value, b.Value, a.Value, n.X - b.X, b.X - a.X);
        return new(a.X, b.X, a.Value, Blend(a.Value, b.Value, left), Blend(b.Value, a.Value, right), b.Value);
    }

    private static double TangentFraction(double outer, double at, double other, double outerWidth, double width)
    {
        if (width <= 0 || outerWidth <= 0 || (at > outer) != (other > at) || at == outer || at == other) return 0;
        var scale = Math.Max(Math.Abs(outer), Math.Max(Math.Abs(at), Math.Abs(other)));
        var ratio = Math.Abs((at / scale - outer / scale) / (other / scale - at / scale)) * (width / outerWidth);
        return double.IsNaN(ratio) ? 0 : Math.Min(1, ratio) / 3;
    }

    public static double Blend(double a, double b, double t) => t <= 0 ? a : t >= 1 ? b : Math.Clamp(a * (1 - t) + b * t, Math.Min(a, b), Math.Max(a, b));
    public double Value(double t)
    {
        var a = Blend(V0, C1, t); var b = Blend(C1, C2, t); var c = Blend(C2, V1, t);
        return Blend(Blend(a, b, t), Blend(b, c, t), t);
    }
    public double Crossing(double value)
    {
        if (V0 == V1) return 0;
        var lo = 0d; var hi = 1d;
        for (var i = 0; i < 48; i++)
        {
            var mid = (lo + hi) / 2;
            if ((Value(mid) < value) == (V1 > V0)) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
    private void Split(double t, out GraphBezierSegment left, out GraphBezierSegment right)
    {
        var a = Blend(V0, C1, t); var b = Blend(C1, C2, t); var c = Blend(C2, V1, t);
        var d = Blend(a, b, t); var e = Blend(b, c, t); var v = Blend(d, e, t); var x = Blend(X0, X1, t);
        left = new(X0, x, V0, a, d, v); right = new(x, X1, v, e, c, V1);
    }
    public GraphBezierSegment Slice(double lo, double hi)
    {
        Split(hi, out var left, out _);
        if (lo <= 0 || hi <= 0) return left;
        left.Split(lo / hi, out _, out var result); return result;
    }
}
