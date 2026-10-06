using System.Drawing;
using SkiaSharp;

namespace PinkieSysMon;

internal static class FrameGeometry
{
    // Historical/default Trofeo geometry. These values are defaults and schema-migration
    // constants only; active dashboard geometry comes from CanvasDefinition.
    public const int DefaultNativeWidth = 1920;
    public const int DefaultNativeHeight = 480;

    public static Size GetLogicalSize(int nativeWidth, int nativeHeight, int orientationDegrees)
    {
        ValidateNativeSize(nativeWidth, nativeHeight);

        return NormalizeOrientation(orientationDegrees) switch
        {
            0 or 180 => new Size(nativeWidth, nativeHeight),
            90 or 270 => new Size(nativeHeight, nativeWidth),
            _ => throw new InvalidOperationException("Unexpected orientation value.")
        };
    }

    public static void DrawLogicalToWire(
        SKCanvas destination,
        SKSurface logical,
        int logicalWidth,
        int logicalHeight,
        int nativeWidth,
        int nativeHeight,
        int orientationDegrees,
        int wireRotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(logical);
        ValidateNativeSize(nativeWidth, nativeHeight);

        var orientation = NormalizeOrientation(orientationDegrees);
        var wire = NormalizeWireRotation(wireRotationDegrees);
        var expectedLogical = GetLogicalSize(nativeWidth, nativeHeight, orientation);
        if (logicalWidth != expectedLogical.Width || logicalHeight != expectedLogical.Height)
        {
            throw new ArgumentException(
                $"Logical frame is {logicalWidth}x{logicalHeight}; expected {expectedLogical.Width}x{expectedLogical.Height} " +
                $"for native {nativeWidth}x{nativeHeight} at orientation {orientation}.",
                nameof(logical));
        }

        destination.Save();
        try
        {
            // Preserve the exact previous two-stage transform order:
            // wire transform is applied to the already-oriented physical frame.
            ApplyWireRotation(destination, nativeWidth, nativeHeight, wire);
            ApplyOrientation(destination, nativeWidth, nativeHeight, orientation);

            // Both renderer surfaces are raster-backed. Draw the logical surface directly
            // into the wire surface instead of manufacturing an immutable SKImage snapshot
            // for every frame.
            logical.Draw(destination, 0, 0, SKSamplingOptions.Default);
        }
        finally
        {
            destination.Restore();
        }
    }

    public static void ValidateNativeSize(int nativeWidth, int nativeHeight)
    {
        if (nativeWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(nativeWidth), nativeWidth, "Canvas Width must be a positive integer.");
        if (nativeHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(nativeHeight), nativeHeight, "Canvas Height must be a positive integer.");
    }

    private static void ApplyOrientation(SKCanvas destination, int nativeWidth, int nativeHeight, int orientation)
    {
        switch (orientation)
        {
            case 0:
                break;
            case 90:
                destination.Translate(nativeWidth, 0);
                destination.RotateDegrees(90);
                break;
            case 180:
                destination.Translate(nativeWidth, nativeHeight);
                destination.RotateDegrees(180);
                break;
            case 270:
                destination.Translate(0, nativeHeight);
                destination.RotateDegrees(270);
                break;
        }
    }

    private static void ApplyWireRotation(SKCanvas destination, int nativeWidth, int nativeHeight, int wireRotationDegrees)
    {
        if (wireRotationDegrees != 180)
            return;

        destination.Translate(nativeWidth, nativeHeight);
        destination.RotateDegrees(180);
    }

    private static int NormalizeOrientation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Orientation must be 0, 90, 180, or 270 degrees.");

        return normalized;
    }

    private static int NormalizeWireRotation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized is not (0 or 180))
            throw new NotSupportedException($"Trofeo wire rotation must be 0 or 180 degrees; got {degrees}.");

        return normalized;
    }
}
