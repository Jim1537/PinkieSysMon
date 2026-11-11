using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

// Widget-neutral mapping: saved Desktop X/Y and widget Action geometry are always
// Canvas coordinates; shadow margins exist only in the native visual surface.
internal sealed class DesktopCanvasViewport
{
    private DesktopCanvasViewport(Size canvas, Size surface, Point canvasOffset, bool hasShadow)
    {
        Canvas = canvas;
        Surface = surface;
        CanvasOffset = canvasOffset;
        HasShadow = hasShadow;
    }

    public Size Canvas { get; }
    public Size Surface { get; }
    public Point CanvasOffset { get; }
    public bool HasShadow { get; }
    public Rectangle CanvasBounds => new(CanvasOffset, Canvas);

    public static DesktopCanvasViewport Create(C.CanvasDefinition root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var canvas = FrameGeometry.GetLogicalSize(root.Width, root.Height, root.Orientation);
        FrameGeometry.ValidateRasterSize(canvas.Width, canvas.Height);

        var color = ColorParser.Parse(root.ShadowColor);
        var active = root.ShadowEnabled && color.Alpha > 0;
        if (!active)
            return ForCanvas(canvas);

        if (!float.IsFinite(root.ShadowBlur) || root.ShadowBlur < 0f ||
            !float.IsFinite(root.ShadowOffsetX) || !float.IsFinite(root.ShadowOffsetY))
            throw new InvalidDataException("Invalid Desktop Canvas shadow geometry.");

        // Skia Gaussian blur coverage approaches zero at 3 sigma. Keep one
        // extra pixel for antialiased edges, including blur=0 contour offsets.
        var spread = 3d * root.ShadowBlur + 1d;
        var left = Math.Floor(Math.Min(0d, (double)root.ShadowOffsetX - spread));
        var top = Math.Floor(Math.Min(0d, (double)root.ShadowOffsetY - spread));
        var right = Math.Ceiling(Math.Max(canvas.Width, canvas.Width + (double)root.ShadowOffsetX + spread));
        var bottom = Math.Ceiling(Math.Max(canvas.Height, canvas.Height + (double)root.ShadowOffsetY + spread));
        var width = right - left;
        var height = bottom - top;
        if (left < -int.MaxValue || top < -int.MaxValue ||
            width > int.MaxValue || height > int.MaxValue ||
            width < 1 || height < 1)
            throw new InvalidDataException("Desktop Canvas shadow margins exceed supported window coordinates.");

        var size = new Size((int)width, (int)height);
        FrameGeometry.ValidateRasterSize(size.Width, size.Height);
        return new DesktopCanvasViewport(canvas, size,
            new Point(checked((int)-left), checked((int)-top)), true);
    }

    internal static DesktopCanvasViewport ForCanvas(Size canvas)
    {
        FrameGeometry.ValidateRasterSize(canvas.Width, canvas.Height);
        return new DesktopCanvasViewport(canvas, canvas, Point.Empty, false);
    }

    public Point SurfaceOriginForCanvas(Point canvasLocation) =>
        new(checked(canvasLocation.X - CanvasOffset.X), checked(canvasLocation.Y - CanvasOffset.Y));

    public Point CanvasOriginForSurface(Point surfaceLocation) =>
        new(checked(surfaceLocation.X + CanvasOffset.X), checked(surfaceLocation.Y + CanvasOffset.Y));

    public Point CanvasPointForSurface(Point clientPoint) =>
        new(checked(clientPoint.X - CanvasOffset.X), checked(clientPoint.Y - CanvasOffset.Y));

    public bool ContainsCanvasPixel(Point surfacePoint) => CanvasBounds.Contains(surfacePoint);
}
