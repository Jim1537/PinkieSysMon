namespace PinkieSysMon.DashboardModel;

internal static class ValueSourceKind
{
    public const string Metric = "Metric";
    public const string Text = "Text";

    public static bool IsMetric(string? value) =>
        string.Equals(value, Metric, StringComparison.OrdinalIgnoreCase);

    public static bool IsText(string? value) =>
        string.Equals(value, Text, StringComparison.OrdinalIgnoreCase);
}

internal static class ImageAssetSourceType
{
    public const string File = "file";
    public const string Icon = "icon";

    public static bool IsFile(string? value) =>
        string.Equals(value, File, StringComparison.OrdinalIgnoreCase);

    public static bool IsIcon(string? value) =>
        string.Equals(value, Icon, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupported(string? value) => IsFile(value) || IsIcon(value);

    public static string Normalize(string? value) => IsIcon(value) ? Icon : File;
}

internal static class StateContentType
{
    public const string Image = "image";
    public const string Value = "value";

    public static bool IsImage(string? value) =>
        string.Equals(value, Image, StringComparison.OrdinalIgnoreCase);

    public static bool IsValue(string? value) =>
        string.Equals(value, Value, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupported(string? value) => IsImage(value) || IsValue(value);
}

internal static class ThresholdModeContract
{
    public const string SegmentTransition = "SegmentTransition";
    public const string SegmentSolid = "SegmentSolid";
    public const string State = "State";

    public static readonly string[] SupportedModes =
    [
        SegmentTransition,
        SegmentSolid,
        State
    ];
}
