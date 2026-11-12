using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal sealed record WidgetActionInteraction(string Id, RectangleF Bounds, double Rotation, int Z, int Order,
    string Target, string Arguments, bool SingleClick, string RunAs = C.WidgetActionDefinition.User,
    string? CalculatorKey = null, PointF? RotationCenter = null)
{
    public bool IsCalculatorKey => CalculatorKey is not null;

    public bool Contains(double x, double y)
    {
        var angle = Rotation % 360 * Math.PI / 180;
        var cx = RotationCenter?.X ?? Bounds.Left + Bounds.Width / 2d;
        var cy = RotationCenter?.Y ?? Bounds.Top + Bounds.Height / 2d;
        var dx = x - cx;
        var dy = y - cy;
        var localX = dx * Math.Cos(angle) + dy * Math.Sin(angle) + cx;
        var localY = -dx * Math.Sin(angle) + dy * Math.Cos(angle) + cy;
        return localX >= Bounds.Left && localX < Bounds.Right &&
               localY >= Bounds.Top && localY < Bounds.Bottom;
    }

    internal (int Left, int Right)? Row(int y, int width)
    {
        var angle = Rotation % 360 * Math.PI / 180;
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var cx = RotationCenter?.X ?? Bounds.Left + Bounds.Width / 2d;
        var cy = RotationCenter?.Y ?? Bounds.Top + Bounds.Height / 2d;
        double left = .5, right = width - .5;
        if (!Constrain(cos, sin * (y + .5 - cy) + cx * (1d - cos) - Bounds.Left, Bounds.Width, ref left, ref right) ||
            !Constrain(-sin, cos * (y + .5 - cy) + sin * cx + cy - Bounds.Top, Bounds.Height, ref left, ref right)) return null;
        // Expand the candidate by one pixel, then use the exact dispatch predicate at both edges.
        var first = Math.Max(0, (int)Math.Ceiling(left - .5) - 1);
        var last = Math.Min(width - 1, (int)Math.Floor(right - .5) + 1);
        while (first <= last && !Contains(first + .5, y + .5)) first++;
        while (last >= first && !Contains(last + .5, y + .5)) last--;
        return first <= last ? (first, last + 1) : null;
    }

    private static bool Constrain(double a, double b, double limit, ref double left, ref double right)
    {
        if (a == 0) return b >= 0 && b < limit;
        var p = -b / a; var q = (limit - b) / a;
        left = Math.Max(left, Math.Min(p, q)); right = Math.Min(right, Math.Max(p, q));
        return left <= right;
    }
}

// Immutable payload + geometry. Effects, label, source alpha and physical wire rotation never enter it.
internal sealed class WidgetActionInteractionMap
{
    internal const int MaximumRegionRectangles = 262144; // 4 MiB native RECT payload, separate from rasters/assets.
    private readonly WidgetActionInteraction[] _frontToBack;
    public Size Canvas { get; }
    public bool IsEmpty => _frontToBack.Length == 0;
    public bool HasDerivedGeometry { get; }

    public bool IsEquivalent(WidgetActionInteractionMap other) =>
        Canvas == other.Canvas && _frontToBack.SequenceEqual(other._frontToBack);

    public WidgetActionInteractionMap(C.DashboardDefinition dashboard, Func<C.WidgetDefinition, RectangleF>? panelBounds = null)
    {
        Canvas = FrameGeometry.GetLogicalSize(dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
        FrameGeometry.ValidateRasterSize(Canvas.Width, Canvas.Height);
        // Value text and state-driven Value profiles can change their Panel size per frame.
        // Static Gauge geometry does not require this refresh.
        HasDerivedGeometry = dashboard.Widgets.Any(w => w.Action.IsActive &&
            w is C.ValueWidgetDefinition or C.StateVisualWidgetDefinition);
        _frontToBack = dashboard.Widgets.SelectMany((widget, index) =>
        {
            var entries = new List<WidgetActionInteraction>();
            if (widget.Action.IsActive)
            {
                var action = widget.Action;
                entries.Add(new WidgetActionInteraction(widget.Id!,
                    panelBounds?.Invoke(widget) ?? new RectangleF(widget.X, widget.Y, widget.Width, widget.Height),
                    widget.Rotation, widget.Z, index, action.Target, action.Arguments,
                    action.Activation == C.WidgetActionDefinition.SingleClick, action.RunAs));
            }
            if (widget is C.CalculatorWidgetDefinition calculator)
            {
                var center = new PointF(widget.X + widget.Width / 2f, widget.Y + widget.Height / 2f);
                foreach (var key in CalculatorGeometry.Keys(calculator))
                    entries.Add(new WidgetActionInteraction(widget.Id!, key.Bounds, widget.Rotation, widget.Z,
                        index, string.Empty, string.Empty, true, CalculatorKey: key.Caption, RotationCenter: center));
            }
            return entries;
        }).OrderByDescending(s => s.Z).ThenByDescending(s => s.Order)
          .ThenByDescending(s => s.IsCalculatorKey).ToArray();
    }

    public WidgetActionInteraction? Hit(Point pixel)
    {
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= Canvas.Width || pixel.Y >= Canvas.Height) return null;
        foreach (var graphic in _frontToBack)
            if (graphic.Contains(pixel.X + .5, pixel.Y + .5)) return graphic;
        return null;
    }

    public IReadOnlyList<Rectangle> Rasterize()
    {
        var result = new List<Rectangle>();
        if (IsEmpty) return result;
        var spans = new List<(int Left, int Right)>(_frontToBack.Length);
        for (var y = 0; y < Canvas.Height; y++)
        {
            spans.Clear();
            foreach (var graphic in _frontToBack)
                if (graphic.Row(y, Canvas.Width) is { } span) spans.Add(span);
            spans.Sort((a, b) => a.Left.CompareTo(b.Left));
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                while (i + 1 < spans.Count && spans[i + 1].Left <= span.Right)
                    span.Right = Math.Max(span.Right, spans[++i].Right);
                if (result.Count >= MaximumRegionRectangles)
                    throw new InvalidDataException("WidgetAction native regions exceed the 4 MiB rectangle budget.");
                result.Add(new Rectangle(span.Left, y, span.Right - span.Left, 1));
            }
        }
        return result;
    }
}
