using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class GraphWidgetRenderer(ImageAssetCache images, IconAssetCache icons) : IWidgetRenderer, IDisposable
{
    private const long MaximumScratchBytes = 32L * 1024 * 1024;
    private readonly Dictionary<C.WidgetDefinition, Resources> _resources = new();
    private long _scratchBytes;
    public string Type => WidgetTypeContract.Graph;

    public void Render(SKCanvas canvas, C.WidgetDefinition widget, WidgetRenderContext context)
    {
        var graph = (C.GraphWidgetDefinition)widget;
        if (!_resources.TryGetValue(widget, out var r))
        {
            var capacity = context.History?.SamplesPerMetric ?? 0;
            var bytes = capacity * (graph.Filter == "None" ? 68L : 92L) + (graph.Filter == "None" ? 0 : 40);
            if (bytes > MaximumScratchBytes - _scratchBytes)
                throw new InvalidDataException("Graph render scratch exceeds the 32 MiB per-dashboard budget.");
            r = new Resources(graph, images, icons, capacity, context.Antialias);
            _resources.Add(widget, r); _scratchBytes += bytes;
        }
        if (r.Bounds.Width <= 0 || r.Bounds.Height <= 0) return;
        r.PrepareFrame(graph, context);
        canvas.Save();
        try
        {
            canvas.Translate(graph.X, graph.Y);
            canvas.ClipRoundRect(r.Clip, SKClipOperation.Intersect, context.Antialias);
            r.Image?.Draw(canvas, r.Bounds, context.Antialias);
            if (r.SelectedCount == 0) return;
            if (graph.Mode == "Line" && graph.FillEnabled) r.DrawFills(canvas, graph);
            for (var i = 0; i < r.SelectedCount; i++)
            {
                var point = r.Points[r.Selected[i]];
                if (graph.Mode == "Columns")
                {
                    if (point.X < 0 || point.X > 1) continue;
                    var previous = i > 0 ? r.Points[r.Selected[i - 1]] : point;
                    var next = i + 1 < r.SelectedCount ? r.Points[r.Selected[i + 1]] : point;
                    var spacing = next.Connects && next.X > point.X ? next.X - point.X
                        : point.Connects && point.X > previous.X ? point.X - previous.X : 0;
                    var width = (float)Math.Clamp(spacing * r.Bounds.Width * .8, 1, 8);
                    var x = r.X(point.X);
                    r.SetColor(point.Value, fill: false);
                    canvas.DrawRect(new SKRect(x - width / 2, r.Y(point.Value), x + width / 2, r.Bounds.Bottom), r.Fill);
                }
                else if (i > 0 && point.Connects)
                {
                    if (graph.Interpolation == "Bezier") r.DrawBezierSegment(canvas, i);
                    else r.DrawSegment(canvas, graph, r.Points[r.Selected[i - 1]], point);
                }
                else if (point.X is >= 0 and <= 1 && point.Value >= r.Range.Min && point.Value <= r.Range.Max)
                {
                    r.SetColor(point.Value, fill: false);
                    canvas.DrawCircle(r.X(point.X), r.Y(point.Value), graph.StrokeWidth / 2, r.Fill);
                }
            }
        }
        finally { canvas.Restore(); }
    }

    public void InvalidateGeometry(C.WidgetDefinition widget)
    {
        if (_resources.TryGetValue(widget, out var r)) r.UpdateGeometry((C.GraphWidgetDefinition)widget);
    }
    public void Dispose()
    {
        foreach (var r in _resources.Values) r.Dispose();
        _resources.Clear(); _scratchBytes = 0;
    }

    private sealed class Resources : IDisposable
    {
        public readonly int Capacity;
        public readonly MetricHistorySample[] Raw;
        public readonly GraphPoint[] Points;
        public readonly int[] Selected;
        public readonly QuantitativeColorScale Colors;
        public readonly bool Reverse;
        private readonly GraphFilterProjection? _filter;
        public SKRect Bounds;
        public readonly SKRoundRect Clip = new();
        public readonly SKPaint Stroke;
        public readonly SKPaint Fill;
        public readonly SKPathBuilder Builder = new();
        public readonly ImageVisual? Image;
        public GraphRange Range;
        public int SelectedCount;
        private long _frame = -1;
        private SKShader? _shader;
        private GraphRange? _shaderRange;

        public Resources(C.GraphWidgetDefinition graph, ImageAssetCache images, IconAssetCache icons, int capacity, bool antialias)
        {
            Capacity = capacity;
            Raw = new MetricHistorySample[capacity]; Points = new GraphPoint[capacity]; Selected = new int[capacity];
            Colors = new(graph); Reverse = graph.Reverse;
            if (graph.Filter != "None") _filter = new(graph.Filter, capacity);
            UpdateGeometry(graph);
            Stroke = new() { IsAntialias = antialias, Style = SKPaintStyle.Stroke, StrokeWidth = graph.StrokeWidth, StrokeCap = SKStrokeCap.Round };
            Fill = new() { IsAntialias = antialias, Style = SKPaintStyle.Fill };
            try
            {
                if (!string.IsNullOrWhiteSpace(graph.Image.Asset.Source))
                    Image = ImageVisual.Create(images, icons, graph.Image.Asset.SourceType, graph.Image.Asset.Source,
                        graph.Image.Asset.Fit, graph.Image.Asset.Loop, graph.Image.Opacity, graph.Image.Color, antialias);
            }
            catch { Dispose(); throw; }
        }

        public void UpdateGeometry(C.GraphWidgetDefinition graph)
        {
            var inset = Math.Max(0, graph.BorderWidth);
            Bounds = new(inset, inset, graph.Width - inset, graph.Height - inset);
            var radius = Math.Max(0, Math.Min(graph.CornerRadius - inset, Math.Min(Bounds.Width, Bounds.Height) / 2));
            Clip.SetRect(Bounds, radius, radius);
            _frame = -1; _shaderRange = null;
        }

        public void PrepareFrame(C.GraphWidgetDefinition graph, WidgetRenderContext context)
        {
            if (_frame == context.FrameSequence) return;
            _frame = context.FrameSequence; SelectedCount = 0;
            if (context.History is null || string.IsNullOrWhiteSpace(graph.Metric)) return;
            var query = context.History.CopyTo(graph.Metric, graph.WindowSeconds, context.HistoryTimestamp, Raw);
            _filter?.Begin(query.Epoch, Raw.AsSpan(0, query.Count));
            var min = double.PositiveInfinity; var max = double.NegativeInfinity;
            var count = 0; var previousValid = false;
            for (var i = 0; i < query.Count; i++)
            {
                var raw = Raw[i];
                double value;
                var valid = _filter is not null ? _filter.TryResolve(raw, query.Descriptor, graph, out value)
                    : MetricValueConverter.TryResolveQuantitative(raw, query.Descriptor,
                        graph.Unit, graph.Component, graph.ComponentFormat, out value);
                if (!valid)
                { previousValid = false; continue; }
                Points[count++] = new((raw.Timestamp - query.WindowStart) / (graph.WindowSeconds * TimeSpan.TicksPerSecond),
                    value, previousValid && raw.ConnectsToPrevious);
                previousValid = true;
                if (raw.Timestamp >= query.WindowStart) { min = Math.Min(min, value); max = Math.Max(max, value); }
            }
            if (!double.IsFinite(min)) return;
            Range = graph.AutoScale ? GraphRange.Auto(min, max) : new(graph.Min, graph.Max);
            if (!double.IsFinite(Range.Min) || !double.IsFinite(Range.Max) || Range.Min >= Range.Max) return;
            if (_shaderRange != Range && Colors.Mode != C.ThresholdModeContract.State)
            {
                var anchor = graph.AutoScale && Colors.Thresholds.Length > 0
                    ? Math.Min(Range.Min, Colors.Thresholds[0].Value) : Range.Min;
                var replacement = Colors.CreateShader(Range, Bounds, anchor);
                Stroke.Shader = replacement; Fill.Shader = replacement;
                _shader?.Dispose(); _shader = replacement; _shaderRange = Range;
            }
            Reduce(count);
        }

        // Retain chronological first/min/max/last observations in each pixel bin and run.
        private void Reduce(int count)
        {
            var first = 0;
            Span<int> indices = stackalloc int[4];
            while (first < count)
            {
                var bin = Math.Floor(Points[first].X * Bounds.Width);
                var last = first; var min = first; var max = first;
                while (last + 1 < count && Points[last + 1].Connects && Math.Floor(Points[last + 1].X * Bounds.Width) == bin)
                {
                    last++;
                    if (Points[last].Value < Points[min].Value) min = last;
                    if (Points[last].Value > Points[max].Value) max = last;
                }
                indices[0] = first; indices[1] = min; indices[2] = max; indices[3] = last;
                indices.Sort();
                for (var i = 0; i < indices.Length; i++)
                    if (i == 0 || indices[i] != indices[i - 1]) Selected[SelectedCount++] = indices[i];
                first = last + 1;
            }
        }

        public float X(double x) => Bounds.Left + (float)(Reverse ? x : 1 - x) * Bounds.Width;
        public float Y(double value) => Bounds.Bottom - (float)Range.Normalize(value) * Bounds.Height;
        public void SetColor(double value, bool fill, float opacity = 1)
        {
            var color = Colors.Mode == C.ThresholdModeContract.State
                ? QuantitativeColorScale.StateColor(Colors.BaseColor, Colors.Thresholds, value) : SKColors.White;
            var paint = fill ? Fill : Stroke;
            paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * opacity));
            if (!fill) Fill.Color = paint.Color;
        }

        private GraphBezierSegment Curve(int index)
        {
            var a = Points[Selected[index - 1]]; var b = Points[Selected[index]];
            GraphPoint? previous = index > 1 && a.Connects ? Points[Selected[index - 2]] : null;
            GraphPoint? next = index + 1 < SelectedCount && Points[Selected[index + 1]].Connects
                ? Points[Selected[index + 1]] : null;
            var curve = GraphBezierSegment.Create(a, b, previous, next);
            return a.X < 0 && b.X > a.X ? curve.Slice(-a.X / (b.X - a.X), 1) : curve;
        }

        private void AppendCurve(GraphBezierSegment curve)
        {
            var width = curve.X1 - curve.X0;
            Builder.CubicTo(X(curve.X0 + width / 3), Y(curve.C1),
                X(curve.X1 - width / 3), Y(curve.C2), X(curve.X1), Y(curve.V1));
        }

        public void DrawBezierSegment(SKCanvas canvas, int index)
        {
            var a = Points[Selected[index - 1]]; var b = Points[Selected[index]];
            if (b.X < 0 || a.X > 1) return;
            var curve = Curve(index);
            if (curve.V0 < Range.Min && curve.V1 < Range.Min || curve.V0 > Range.Max && curve.V1 > Range.Max) return;
            var lo = 0d; var hi = 1d;
            if (curve.V0 < Range.Min) lo = curve.Crossing(Range.Min);
            if (curve.V0 > Range.Max) lo = curve.Crossing(Range.Max);
            if (curve.V1 < Range.Min) hi = curve.Crossing(Range.Min);
            if (curve.V1 > Range.Max) hi = curve.Crossing(Range.Max);
            if (lo > hi) return;
            curve = curve.Slice(lo, hi);
            Builder.Reset(); Builder.MoveTo(X(curve.X0), Y(curve.V0)); AppendCurve(curve);
            SetColor(b.Value, fill: false);
            using var path = Builder.Detach(); canvas.DrawPath(path, Stroke);
        }

        private void AppendBezierFill(GraphBezierSegment curve, Span<double> cuts)
        {
            var n = 0; cuts[n++] = 0;
            for (var boundaryIndex = 0; boundaryIndex < 2; boundaryIndex++)
            {
                var boundary = boundaryIndex == 0 ? Range.Min : Range.Max;
                if ((curve.V0 < boundary && curve.V1 > boundary) || (curve.V1 < boundary && curve.V0 > boundary))
                    cuts[n++] = curve.Crossing(boundary);
            }
            cuts[n++] = 1; cuts[..n].Sort();
            for (var j = 1; j < n; j++)
            {
                var part = curve.Slice(cuts[j - 1], cuts[j]);
                var value = curve.Value((cuts[j - 1] + cuts[j]) / 2);
                if (value > Range.Min && value < Range.Max) AppendCurve(part);
                else Builder.LineTo(X(part.X1), Y(part.V1));
            }
        }

        public void DrawFills(SKCanvas canvas, C.GraphWidgetDefinition graph)
        {
            var active = false; var lastX = 0d; var lastColor = default(SKColor); var stateValue = 0d;
            Span<double> fractions = stackalloc double[4];
            void Flush()
            {
                if (!active) return;
                Builder.LineTo(X(lastX), Bounds.Bottom); Builder.Close();
                SetColor(stateValue, fill: true, graph.FillOpacity);
                using var path = Builder.Detach();
                canvas.DrawPath(path, Fill); active = false;
            }
            for (var i = 1; i < SelectedCount; i++)
            {
                var a = Points[Selected[i - 1]]; var b = Points[Selected[i]];
                if (!b.Connects || b.X < 0 || a.X > 1) { Flush(); continue; }
                var curve = default(GraphBezierSegment);
                if (graph.Interpolation == "Bezier")
                {
                    curve = Curve(i); a = a with { X = curve.X0, Value = curve.V0 };
                }
                else if (a.X < 0 && b.X > a.X) a = a with { Value = Lerp(a.Value, b.Value, -a.X / (b.X - a.X)), X = 0 };
                var color = Colors.Mode == C.ThresholdModeContract.State
                    ? QuantitativeColorScale.StateColor(Colors.BaseColor, Colors.Thresholds, b.Value) : SKColors.White;
                if (active && color != lastColor) Flush();
                if (!active)
                {
                    Builder.Reset(); Builder.MoveTo(X(a.X), Bounds.Bottom); Builder.LineTo(X(a.X), Y(a.Value));
                    active = true; lastColor = color;
                }
                if (graph.Interpolation == "Bezier") AppendBezierFill(curve, fractions);
                else AppendLinearFill(a, b, fractions);
                lastX = b.X; stateValue = b.Value;
            }
            Flush();
        }

        private void AppendLinearFill(GraphPoint a, GraphPoint b, Span<double> fractions)
        {
            var n = 0;
            for (var boundaryIndex = 0; boundaryIndex < 2; boundaryIndex++)
            {
                var boundary = boundaryIndex == 0 ? Range.Min : Range.Max;
                if ((a.Value < boundary && b.Value > boundary) || (b.Value < boundary && a.Value > boundary))
                    fractions[n++] = GraphRange.Fraction(boundary, a.Value, b.Value);
            }
            fractions[n++] = 1; fractions[..n].Sort();
            for (var j = 0; j < n; j++)
            {
                var t = fractions[j]; Builder.LineTo(X(Lerp(a.X, b.X, t)), Y(Lerp(a.Value, b.Value, t)));
            }
        }

        public void DrawSegment(SKCanvas canvas, C.GraphWidgetDefinition graph, GraphPoint a, GraphPoint b)
        {
            if (b.X < 0 || a.X > 1) return;
            if (a.X < 0 && b.X > a.X)
            {
                var t = -a.X / (b.X - a.X);
                a = a with { X = 0, Value = Lerp(a.Value, b.Value, t) };
            }
            SetColor(b.Value, fill: false);
            var lo = 0d; var hi = 1d;
            if (a.Value < Range.Min && b.Value < Range.Min || a.Value > Range.Max && b.Value > Range.Max) return;
            if (a.Value < Range.Min) lo = GraphRange.Fraction(Range.Min, a.Value, b.Value);
            if (a.Value > Range.Max) lo = GraphRange.Fraction(Range.Max, a.Value, b.Value);
            if (b.Value < Range.Min) hi = GraphRange.Fraction(Range.Min, a.Value, b.Value);
            if (b.Value > Range.Max) hi = GraphRange.Fraction(Range.Max, a.Value, b.Value);
            canvas.DrawLine(X(Lerp(a.X, b.X, lo)), Y(Lerp(a.Value, b.Value, lo)),
                X(Lerp(a.X, b.X, hi)), Y(Lerp(a.Value, b.Value, hi)), Stroke);
        }
        private static double Lerp(double a, double b, double t) => a * (1 - t) + b * t;
        public void Dispose() { Image?.Dispose(); Stroke.Dispose(); Fill.Dispose(); _shader?.Dispose(); Clip.Dispose(); Builder.Dispose(); }
    }
}
