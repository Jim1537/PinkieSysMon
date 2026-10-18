using System.Drawing;

namespace PinkieSysMon;

// Physical geometry is an output capability, never a dashboard schema field.
internal readonly record struct PhysicalFrameProfile(
    int RotationDegrees, int? NativeWidth = null, int? NativeHeight = null,
    int MaximumImageBytes = int.MaxValue)
{
    public static PhysicalFrameProfile Turzx { get; } = new(90, 1920, 720, TurzxWireProtocol.MaximumImageBytes);

    public Size PhysicalSize(int dashboardWidth, int dashboardHeight)
    {
        FrameGeometry.ValidateNativeSize(dashboardWidth, dashboardHeight);
        if ((NativeWidth is null) != (NativeHeight is null) || MaximumImageBytes < 1 ||
            RotationDegrees is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Physical frame capabilities are invalid.");
        var width = NativeWidth ?? dashboardWidth;
        var height = NativeHeight ?? dashboardHeight;
        FrameGeometry.ValidateNativeSize(width, height);
        return new Size(width, height);
    }

    public Size WireSize(int dashboardWidth, int dashboardHeight)
    {
        var physical = PhysicalSize(dashboardWidth, dashboardHeight);
        return RotationDegrees is 90 or 270 ? new Size(physical.Height, physical.Width) : physical;
    }
}
