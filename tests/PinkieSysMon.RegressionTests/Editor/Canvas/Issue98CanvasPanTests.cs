using System.Drawing;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue98CanvasPanTests
{
    public static void DeltaAndBounds()
    {
        var origin = new Point(400, 250);
        var pressed = new Point(180, 120);

        Check(EditorForm.CalculatePanScrollPosition(origin, pressed, pressed) == origin,
            "Pressing the middle button without moving must not scroll.");

        Check(EditorForm.CalculatePanScrollPosition(origin, pressed, new Point(220, 150)) == new Point(360, 220),
            "Dragging right and down must move the viewport left and up on both axes.");

        Check(EditorForm.CalculatePanScrollPosition(origin, pressed, new Point(150, 80)) == new Point(430, 290),
            "Dragging left and up must scroll right and down on both axes.");

        var canvas = new Size(2000, 1500);
        var viewport = new Size(600, 400);
        Check(EditorForm.ClampScrollPosition(new Point(-50, -100), canvas, viewport) == Point.Empty,
            "Panning must clamp independently at the top and left edges.");
        Check(EditorForm.ClampScrollPosition(new Point(2000, 1500), canvas, viewport) == new Point(1400, 1100),
            "Panning must clamp independently at the right and bottom edges.");

        Check(EditorForm.ClampScrollPosition(new Point(600, 700), new Size(1500, 300),
                new Size(500, 400)) == new Point(600, 0),
            "An axis without overflow must remain at zero while the other axis scrolls.");

        Check(EditorForm.ClampScrollPosition(new Point(900, 700), canvas,
                new Size(1400, 1200)) == new Point(600, 300),
            "A resized viewport must use the new scroll limits.");

        Check(EditorForm.ClampScrollPosition(new Point(900, 700), new Size(800, 600),
                viewport) == new Point(200, 200),
            "A smaller zoomed canvas must reduce both scroll limits.");

        Check(EditorForm.ClampScrollPosition(origin, new Size(300, 200),
                viewport) == Point.Empty,
            "A canvas smaller than the viewport must not retain an obsolete scroll offset.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
