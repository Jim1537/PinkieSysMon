using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal sealed record ShortcutInteraction(string Id, RectangleF Bounds, double Rotation, int Z, int Order,
    string Target, string Arguments, bool SingleClick)
{
    public bool Contains(double x, double y)
    {
        var angle = Rotation % 360 * Math.PI / 180;
        var dx = x - (Bounds.Left + Bounds.Width / 2d);
        var dy = y - (Bounds.Top + Bounds.Height / 2d);
        var localX = dx * Math.Cos(angle) + dy * Math.Sin(angle) + Bounds.Width / 2d;
        var localY = -dx * Math.Sin(angle) + dy * Math.Cos(angle) + Bounds.Height / 2d;
        return localX >= 0 && localX < Bounds.Width && localY >= 0 && localY < Bounds.Height;
    }

    internal (int Left, int Right)? Row(int y, int width)
    {
        var angle = Rotation % 360 * Math.PI / 180;
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var cx = Bounds.Left + Bounds.Width / 2d; var cy = Bounds.Top + Bounds.Height / 2d;
        double left = .5, right = width - .5;
        if (!Constrain(cos, sin * (y + .5 - cy) - cos * cx + Bounds.Width / 2d, Bounds.Width, ref left, ref right) ||
            !Constrain(-sin, cos * (y + .5 - cy) + sin * cx + Bounds.Height / 2d, Bounds.Height, ref left, ref right)) return null;
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
internal sealed class ShortcutInteractionMap
{
    internal const int MaximumRegionRectangles = 262144; // 4 MiB native RECT payload, separate from rasters/assets.
    private readonly ShortcutInteraction[] _frontToBack;
    public Size Canvas { get; }
    public bool IsEmpty => _frontToBack.Length == 0;

    public ShortcutInteractionMap(C.DashboardDefinition dashboard)
    {
        Canvas = FrameGeometry.GetLogicalSize(dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
        FrameGeometry.ValidateRasterSize(Canvas.Width, Canvas.Height);
        _frontToBack = dashboard.Widgets.Select((widget, index) => (widget, index))
            .Where(entry => entry.widget is C.ShortcutWidgetDefinition)
            .Select(entry =>
            {
                var s = (C.ShortcutWidgetDefinition)entry.widget;
                return new ShortcutInteraction(s.Id!, new RectangleF(s.X, s.Y, s.Width, s.Height), s.Rotation,
                    s.Z, entry.index, s.Target, s.Arguments,
                    string.Equals(s.Activation, ShortcutContract.SingleClick, StringComparison.OrdinalIgnoreCase));
            }).OrderByDescending(s => s.Z).ThenByDescending(s => s.Order).ToArray();
    }

    public ShortcutInteraction? Hit(Point pixel)
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
                    throw new InvalidDataException("Shortcut native regions exceed the 4 MiB rectangle budget.");
                result.Add(new Rectangle(span.Left, y, span.Right - span.Left, 1));
            }
        }
        return result;
    }
}
