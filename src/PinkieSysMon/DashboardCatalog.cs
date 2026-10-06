namespace PinkieSysMon;

internal static class DashboardCatalog
{
    public static string GetRoot(string baseDir) => Path.Combine(baseDir, "dashboards");

    public static IReadOnlyList<string> Discover(string baseDir)
    {
        var root = GetRoot(baseDir);
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "dashboard.json")))
            .Select(directory => Path.GetFileName(directory))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string GetDefinitionPath(string baseDir, string dashboardName)
    {
        if (string.IsNullOrWhiteSpace(dashboardName))
            throw new InvalidDataException("Dashboard name must not be empty.");

        var root = Path.GetFullPath(GetRoot(baseDir));
        var directory = Path.GetFullPath(Path.Combine(root, dashboardName));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!directory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Dashboard name escapes the dashboards directory: '{dashboardName}'.");

        return Path.Combine(directory, "dashboard.json");
    }
}
