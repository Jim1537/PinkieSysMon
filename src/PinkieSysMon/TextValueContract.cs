namespace PinkieSysMon;

internal static class TextValueContract
{
    public const string SourceMetric = "Metric";
    public const string SourceText = "Text";

    public static readonly string[] SupportedSources =
    [
        SourceMetric,
        SourceText
    ];

    public static string NormalizeSource(string? source) =>
        SupportedSources.FirstOrDefault(value => value.Equals(source, StringComparison.OrdinalIgnoreCase))
        ?? SourceMetric;

    public static bool IsMetricSource(string? source) =>
        NormalizeSource(source) == SourceMetric;

    public static bool IsTextSource(string? source) =>
        NormalizeSource(source) == SourceText;
}
