namespace PinkieSysMon;

internal static class RuntimeVersion
{
    public static readonly string Current =
        typeof(RuntimeVersion).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
