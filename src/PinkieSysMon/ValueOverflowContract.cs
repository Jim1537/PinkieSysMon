namespace PinkieSysMon;

internal static class ValueOverflowContract
{
    public const string None = "None";
    public const string Clip = "Clip";
    public const string Ellipsis = "Ellipsis";
    public const string ShrinkToFit = "ShrinkToFit";
    public const string Wrap = "Wrap";
    public const string Scroll = "Scroll";
    public const string Bump = "Bump";

    public static readonly string[] SupportedModes =
    [
        None,
        Clip,
        Ellipsis,
        ShrinkToFit,
        Wrap,
        Scroll,
        Bump
    ];

    public static readonly string[] ConstrainedModes =
    [
        Clip,
        Ellipsis,
        ShrinkToFit,
        Wrap,
        Scroll,
        Bump
    ];

    public static string Normalize(string? value) =>
        SupportedModes.FirstOrDefault(mode => mode.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Clip;

    public static bool IsAnimated(string? value)
    {
        var mode = Normalize(value);
        return mode is Scroll or Bump;
    }
}
