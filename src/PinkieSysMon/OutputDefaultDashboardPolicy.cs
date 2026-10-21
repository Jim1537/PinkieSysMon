namespace PinkieSysMon;

// One owner for generated first-assignment names and verified native geometry.
// Missing assignments do not use a global active/first-saved dashboard.
internal static class OutputDefaultDashboardPolicy
{
    public const string DesktopName = "desktop_default";
    public const string TrofeoName = "device_1920x480_default";
    public const string TurzxName = "device_1920x720_default";

    // The user's entered screen geometry determines a new physical Output's
    // first editable board name; USB product, protocol and model do not.
    // Frame/rendering resource limits are validated separately.
    public static string NameForPhysicalGeometry(int width, int height)
    {
        FrameGeometry.ValidateNativeSize(width, height);
        return $"device_{width}x{height}_default";
    }

    // Kind classification only; OutputTargetContract validates the exact
    // transport pair subsequently and returns its normal InvalidDataException.
    public static string NameFor(OutputTargetConfig target) => target.Kind switch
    {
        OutputTargetContract.DesktopKind => DesktopName,
        OutputTargetContract.TrofeoKind => TrofeoName,
        OutputTargetContract.TurzxKind => TurzxName,
        _ => throw new InvalidDataException($"Unsupported Output kind '{target.Kind}'.")
    };

    public static (string Name, int Width, int Height) PhysicalProfile(OutputTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.Equals(target.Kind, OutputTargetContract.TrofeoKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target.Transport, OutputTargetContract.TrofeoTransport, StringComparison.OrdinalIgnoreCase))
            return (TrofeoName, 1920, 480);
        if (string.Equals(target.Kind, OutputTargetContract.TurzxKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target.Transport, OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase))
            return (TurzxName, 1920, 720);
        throw new InvalidOperationException(
            $"Output '{target.Name}' has no verified integrated native geometry; " +
            "a model-specific geometry policy is required before generating a dashboard.");
    }
}
