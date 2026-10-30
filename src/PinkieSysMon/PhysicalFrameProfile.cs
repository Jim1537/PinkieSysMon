using System.Drawing;

namespace PinkieSysMon;

// Protocol conversion only: panel dimensions never resize the assigned Canvas.
internal readonly record struct PhysicalFrameProfile(int RotationDegrees, int MaximumImageBytes = int.MaxValue)
{
    public static PhysicalFrameProfile EncryptedJpeg { get; } = new(90, TurzxWireProtocol.MaximumImageBytes);

    public Size WireSize(int dashboardWidth, int dashboardHeight)
    {
        FrameGeometry.ValidateRasterSize(dashboardWidth, dashboardHeight);
        if (MaximumImageBytes < 1 || RotationDegrees is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Physical frame capabilities are invalid.");
        return RotationDegrees is 90 or 270
            ? new Size(dashboardHeight, dashboardWidth)
            : new Size(dashboardWidth, dashboardHeight);
    }
}
