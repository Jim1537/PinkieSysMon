namespace PinkieSysMon;

internal static class ShortcutContract
{
    public const string AutoSource = "auto";
    public const string SingleClick = "Single";
    public const string DoubleClick = "Double";
    public const string Top = "Top";
    public const string Middle = "Middle";
    public const string Bottom = "Bottom";
    public static readonly string[] Activations = [SingleClick, DoubleClick];
    public static readonly string[] LabelPositions = [Top, Middle, Bottom];
    public static readonly string[] GraphicSources = [AutoSource, "file", "icon"];

    public static bool IsAuto(string? sourceType) =>
        string.Equals(sourceType, AutoSource, StringComparison.OrdinalIgnoreCase);
}
