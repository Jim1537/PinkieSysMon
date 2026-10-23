using System.Drawing;

namespace PinkieSysMon;

// Mouse and Windows-promoted touch mouse messages share one recognizer.
internal sealed class ShortcutGesture(uint doubleClickMs, Size doubleClickSize, Size dragSize)
{
    private record Press(string Id, long Generation, Point Point, uint Time, bool IsSecond);
    private Press? _press;
    private Press? _firstClick;
    public bool Dragged { get; private set; }

    public void Down(string? id, long generation, Point point, uint time)
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

    public bool Up(string? id, long generation, Point point, bool single)
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
