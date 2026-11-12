using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

// One layout owns both Skia key rectangles and the native Desktop hit regions.
internal static class CalculatorGeometry
{
    internal readonly record struct Key(string Caption, RectangleF Bounds);

    private static readonly (string Caption, int Row, int Column, int Span)[] Layout =
    [
        ("C", 0, 0, 1), ("CE", 0, 1, 1), ("⌫", 0, 2, 1), ("÷", 0, 3, 1),
        ("7", 1, 0, 1), ("8", 1, 1, 1), ("9", 1, 2, 1), ("×", 1, 3, 1),
        ("4", 2, 0, 1), ("5", 2, 1, 1), ("6", 2, 2, 1), ("−", 2, 3, 1),
        ("1", 3, 0, 1), ("2", 3, 1, 1), ("3", 3, 2, 1), ("+", 3, 3, 1),
        ("±", 4, 0, 1), ("0", 4, 1, 1), (".", 4, 2, 1), ("=", 4, 3, 1),
        ("Copy", 5, 0, 2), ("Paste", 5, 2, 2)
    ];

    internal static RectangleF Content(C.CalculatorWidgetDefinition widget)
    {
        var width = Math.Max(0f, widget.Width - widget.Padding.Left - widget.Padding.Right);
        var height = Math.Max(0f, widget.Height - widget.Padding.Top - widget.Padding.Bottom);
        return new RectangleF(widget.X + widget.Padding.Left, widget.Y + widget.Padding.Top, width, height);
    }

    internal static RectangleF Readout(C.CalculatorWidgetDefinition widget)
    {
        var content = Content(widget);
        return new RectangleF(content.X, content.Y, content.Width, content.Height * .19f);
    }

    internal static IReadOnlyList<Key> Keys(C.CalculatorWidgetDefinition widget)
    {
        var content = Content(widget);
        if (content.Width < 8f || content.Height < 20f) return [];
        var gap = Math.Clamp(Math.Min(content.Width, content.Height) * .015f, 1f, 4f);
        var top = content.Y + content.Height * .19f + gap;
        var availableHeight = content.Bottom - top;
        var keyWidth = (content.Width - gap * 3f) / 4f;
        var keyHeight = (availableHeight - gap * 5f) / 6f;
        if (keyWidth <= 0f || keyHeight <= 0f) return [];
        return Layout.Select(spec => new Key(spec.Caption,
            new RectangleF(
                content.X + spec.Column * (keyWidth + gap),
                top + spec.Row * (keyHeight + gap),
                keyWidth * spec.Span + gap * (spec.Span - 1),
                keyHeight))).ToArray();
    }
}
