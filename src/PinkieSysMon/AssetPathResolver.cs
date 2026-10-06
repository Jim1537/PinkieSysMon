namespace PinkieSysMon;

internal static class AssetPathResolver
{
    public static string ResolveExistingFile(string dashboardBaseDirectory, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException($"{description} path must not be empty.");
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{description} path must be relative to the dashboard directory.");

        var baseDirectory = Path.GetFullPath(dashboardBaseDirectory);
        var candidates = new List<string>
        {
            ResolveInsideDashboard(baseDirectory, relativePath, description)
        };

        // Convenience convention: a bare asset name may live in the dashboard-local images folder.
        // An explicit subdirectory in JSON always wins and is never rewritten.
        if (string.IsNullOrWhiteSpace(Path.GetDirectoryName(relativePath)))
        {
            candidates.Add(ResolveInsideDashboard(
                baseDirectory,
                Path.Combine("images", relativePath),
                description));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var searched = string.Join(Environment.NewLine + "  - ", candidates.Distinct(StringComparer.OrdinalIgnoreCase));
        throw new FileNotFoundException(
            $"{description} not found. Requested '{relativePath}'. Searched:" +
            Environment.NewLine + "  - " + searched,
            candidates[0]);
    }

    private static string ResolveInsideDashboard(string baseDirectory, string relativePath, string description)
    {
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        var rootPrefix = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{description} path escapes the dashboard directory: '{relativePath}'.");

        return fullPath;
    }
}
