namespace PinkieSysMon;

// One owner for generated first-assignment names and explicit user geometry.
// Missing assignments do not use a global active/first-saved dashboard.
internal static class OutputDefaultDashboardPolicy
{
    public const string DesktopName = "desktop_default";

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
        OutputTargetContract.PhysicalKind => NameForPhysicalGeometry(target.Width, target.Height),
        _ => throw new InvalidDataException($"Unsupported Output kind '{target.Kind}'.")
    };

    public static (string Name, int Width, int Height) PhysicalProfile(OutputTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        OutputTargetContract.ValidatePhysicalConnection(target);
        return (NameForPhysicalGeometry(target.Width, target.Height), target.Width, target.Height);
    }
}
