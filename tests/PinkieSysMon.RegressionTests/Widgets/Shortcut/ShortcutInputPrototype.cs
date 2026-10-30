using System.Drawing;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutProbeLifetime
{
    // Console.In is synchronized: even ReadLineAsync can synchronously block.
    // The borrowed reader stays owned by the host until the controller closes stdin.
    public static Task WaitForExitAsync(TextReader input) => Task.Run(() =>
    {
        while (input.ReadLine() is { } line)
            if (line == "close") break;
    });
}

// Stage 02 experiment only. No canonical widget, schema or production input path.
internal sealed record ShortcutProbeGraphic(string Id, RectangleF Bounds, double Rotation, int Z, int Order)
{
    public bool Contains(double x, double y)
    {
        var radians = Rotation % 360 * Math.PI / 180;
        var dx = x - (Bounds.Left + Bounds.Width / 2d);
        var dy = y - (Bounds.Top + Bounds.Height / 2d);
        var localX = dx * Math.Cos(radians) + dy * Math.Sin(radians) + Bounds.Width / 2d;
        var localY = -dx * Math.Sin(radians) + dy * Math.Cos(radians) + Bounds.Height / 2d;
        return localX >= 0 && localX < Bounds.Width && localY >= 0 && localY < Bounds.Height;
    }
}

internal sealed class ShortcutProbeMap
{
    private readonly ShortcutProbeGraphic[] _frontToBack;
    public Size Canvas { get; }
    public int Generation { get; }

    public ShortcutProbeMap(Size canvas, int generation, IEnumerable<ShortcutProbeGraphic> graphics)
    {
        if (canvas.Width <= 0 || canvas.Height <= 0 || (long)canvas.Width * canvas.Height > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(canvas), "The experiment is limited to one million pixels.");
        Canvas = canvas;
        Generation = generation;
        _frontToBack = graphics.OrderByDescending(g => g.Z).ThenByDescending(g => g.Order).ToArray();
        if (_frontToBack.Length > 16 || _frontToBack.Select(g => g.Id).Distinct().Count() != _frontToBack.Length)
            throw new ArgumentException("The experiment requires at most sixteen uniquely identified graphics.", nameof(graphics));
        foreach (var g in _frontToBack)
            if (string.IsNullOrEmpty(g.Id) || !float.IsFinite(g.Bounds.X) || !float.IsFinite(g.Bounds.Y) ||
                !float.IsFinite(g.Bounds.Width) || !float.IsFinite(g.Bounds.Height) ||
                g.Bounds.Width <= 0 || g.Bounds.Height <= 0 || !double.IsFinite(g.Rotation))
                throw new ArgumentException("Graphic geometry must be finite and positive.", nameof(graphics));
    }

    // Native integer pixel membership and dispatch use the SAME pixel-center rule.
    // Neither pixel alpha nor visual effects participate. Right/bottom edges are exclusive.
    public ShortcutProbeGraphic? Hit(Point pixel)
    {
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= Canvas.Width || pixel.Y >= Canvas.Height)
            return null;
        foreach (var graphic in _frontToBack)
            if (graphic.Contains(pixel.X + .5d, pixel.Y + .5d)) return graphic;
        return null;
    }

    public IReadOnlyList<Rectangle> Rasterize()
    {
        var result = new List<Rectangle>();
        for (var y = 0; y < Canvas.Height; y++)
        {
            var start = -1;
            for (var x = 0; x <= Canvas.Width; x++)
            {
                var covered = x < Canvas.Width && Hit(new Point(x, y)) is not null;
                if (covered && start < 0) start = x;
                if (!covered && start >= 0)
                {
                    result.Add(new Rectangle(start, y, x - start, 1));
                    start = -1;
                }
            }
        }
        return result;
    }
}

// Mouse and promoted native-touch mouse events enter one event-driven recognizer.
// Whether a particular touchscreen promotes events is an explicit native test.
internal sealed class ShortcutProbeGesture(uint doubleClickMs, Size doubleClickSize, Size dragSize)
{
    private record Press(string Id, int Generation, Point Point, uint Time, bool IsSecond);
    private Press? _press;
    private Press? _firstClick;
    public bool Dragged { get; private set; }

    public void Down(string? id, int generation, Point point, uint time)
    {
        _press = null;
        Dragged = false;
        if (id is null) { _firstClick = null; return; }
        var previous = _firstClick;
        var second = previous is not null && previous.Id == id && previous.Generation == generation &&
            unchecked(time - previous.Time) <= doubleClickMs && Inside(previous.Point, point, doubleClickSize);
        _firstClick = null;
        _press = new Press(id, generation, point, time, second);
    }

    public bool Move(Point point)
    {
        if (_press is null || Inside(_press.Point, point, dragSize)) return false;
        _press = null;
        _firstClick = null;
        Dragged = true;
        return true;
    }

    public bool Up(string? id, int generation, Point point, bool single)
    {
        var press = _press;
        _press = null;
        if (press is null || press.Id != id || press.Generation != generation ||
            !Inside(press.Point, point, dragSize))
        {
            _firstClick = null;
            return false;
        }
        if (!press.IsSecond) _firstClick = press;
        return single ? !press.IsSecond : press.IsSecond;
    }

    public void Cancel() { _press = null; _firstClick = null; Dragged = false; }

    private static bool Inside(Point origin, Point point, Size size) =>
        point.X >= (long)origin.X - size.Width / 2 && point.X < (long)origin.X + (size.Width + 1) / 2 &&
        point.Y >= (long)origin.Y - size.Height / 2 && point.Y < (long)origin.Y + (size.Height + 1) / 2;
}
