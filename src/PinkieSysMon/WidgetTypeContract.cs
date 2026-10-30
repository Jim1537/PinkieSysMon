namespace PinkieSysMon;

internal static class WidgetTypeContract
{
    public const string Value = "value";
    public const string Bar = "bar";
    public const string Gauge = "gauge";
    public const string Graph = "graph";
    public const string Image = "image";
    public const string Shortcut = "shortcut";
    public const string Binary = "binary";
    public const string Power = "power";
    public const string MediaSystem = "media.system";
    public const string MediaPlayer = "media.player";

    public static readonly string[] KnownTypes =
    [
        Value,
        Bar,
        Gauge,
        Graph,
        Image,
        Shortcut,
        Binary,
        Power,
        MediaSystem,
        MediaPlayer
    ];

    public static readonly string[] EditorAddableTypes =
    [
        Value,
        Bar,
        Gauge,
        Graph,
        Image,
        Shortcut,
        Binary,
        Power,
        MediaSystem,
        MediaPlayer
    ];

    private static readonly HashSet<string> KnownTypeSet = new(KnownTypes, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? type) =>
        !string.IsNullOrWhiteSpace(type) && KnownTypeSet.Contains(type);

    public static bool Is(string? type, string expected) =>
        string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);
}
