namespace PinkieSysMon;

internal static class GlobalAssetResolver
{
    private static readonly string[] SupportedIconExtensions =
    [
        ".svg",
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".ico",
        ".webp"
    ];

    public sealed record IconEntry(
        string LogicalName,
        string Library,
        string Name,
        string Format,
        string Path);

    public static string GetApplicationRoot(string dashboardBaseDirectory) =>
        ApplicationRootLocator.Find(dashboardBaseDirectory);

    public static string GetFontsRoot(string dashboardBaseDirectory) =>
        Path.Combine(GetApplicationRoot(dashboardBaseDirectory), "assets", "fonts");

    public static string ResolveIcon(string dashboardBaseDirectory, string logicalName) =>
        ResolveIconFromRoot(GetApplicationRoot(dashboardBaseDirectory), logicalName);

    public static string ResolveIconFromRoot(string applicationRoot, string logicalName)
    {
        var (library, name) = ParseIconName(logicalName);
        var iconsRoot = Path.Combine(applicationRoot, "assets", "icons");
        if (!Directory.Exists(iconsRoot))
            throw new DirectoryNotFoundException($"Global icons directory was not found: '{iconsRoot}'.");

        var libraryDirectory = Directory
            .EnumerateDirectories(iconsRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), library, StringComparison.OrdinalIgnoreCase));

        if (libraryDirectory is null)
            throw new FileNotFoundException(
                $"Icon library '{library}' was not found under '{iconsRoot}'.",
                Path.Combine(iconsRoot, library));

        var explicitExtension = Path.GetExtension(name);
        if (!string.IsNullOrWhiteSpace(explicitExtension))
        {
            if (!IsSupportedIconExtension(explicitExtension))
            {
                throw new InvalidDataException(
                    $"Global icon '{logicalName}' uses unsupported format '{explicitExtension}'. " +
                    $"Supported formats: {string.Join(", ", SupportedIconExtensions)}.");
            }

            var exact = FindFileCaseInsensitive(libraryDirectory, name);
            if (exact is not null)
                return exact;

            throw new FileNotFoundException(
                $"Global icon '{logicalName}' was not found in '{libraryDirectory}'.",
                Path.Combine(libraryDirectory, name));
        }

        // Preserve the established extensionless SVG identity (for example lucide:cpu).
        // Raster assets are normally persisted with their extension so mixed-format libraries
        // cannot become ambiguous, but an extensionless name may resolve to one unique raster
        // asset when no SVG with that stem exists.
        var legacySvg = FindFileCaseInsensitive(libraryDirectory, name + ".svg");
        if (legacySvg is not null)
            return legacySvg;

        var matches = Directory
            .EnumerateFiles(libraryDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedIconPath)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 1)
            return matches[0];

        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Global icon '{logicalName}' is ambiguous in '{libraryDirectory}'. " +
                $"Specify the file extension explicitly: {string.Join(", ", matches.Select(Path.GetFileName))}.");
        }

        throw new FileNotFoundException(
            $"Global icon '{logicalName}' was not found in '{libraryDirectory}'.",
            Path.Combine(libraryDirectory, name));
    }

    public static IReadOnlyList<IconEntry> EnumerateIconsFromRoot(string applicationRoot)
    {
        var iconsRoot = Path.Combine(applicationRoot, "assets", "icons");
        if (!Directory.Exists(iconsRoot))
            return [];

        var result = new List<IconEntry>();
        foreach (var libraryDirectory in Directory
                     .EnumerateDirectories(iconsRoot, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var library = Path.GetFileName(libraryDirectory);
            var logicalLibrary = library.ToLowerInvariant();
            foreach (var path in Directory
                         .EnumerateFiles(libraryDirectory, "*", SearchOption.TopDirectoryOnly)
                         .Where(IsSupportedIconPath)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var fileName = Path.GetFileName(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                var logicalAsset = extension == ".svg" ? stem : fileName;
                result.Add(new IconEntry(
                    $"{logicalLibrary}:{logicalAsset}",
                    library,
                    stem,
                    extension.TrimStart('.').ToUpperInvariant(),
                    path));
            }
        }

        return result;
    }

    public static bool IsSupportedIconPath(string path) =>
        IsSupportedIconExtension(Path.GetExtension(path));

    public static bool IsSupportedIconExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        SupportedIconExtensions.Contains(
            extension.StartsWith('.') ? extension : "." + extension,
            StringComparer.OrdinalIgnoreCase);

    private static string? FindFileCaseInsensitive(string directory, string fileName) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

    private static (string Library, string Name) ParseIconName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new InvalidDataException("Icon source must not be empty.");

        var text = logicalName.Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1 || text.IndexOf(':', colon + 1) >= 0)
            throw new InvalidDataException(
                $"Icon source '{logicalName}' is invalid. Expected a logical name such as 'library:icon'.");

        var library = text[..colon].Trim();
        var name = text[(colon + 1)..].Trim();
        if (!IsSafeLogicalPart(library) || !IsSafeLogicalPart(name))
            throw new InvalidDataException(
                $"Icon source '{logicalName}' contains invalid characters. Use a library and icon name only, for example 'library:icon'.");

        return (library, name);
    }

    private static bool IsSafeLogicalPart(string value) =>
        WindowsPathComponentValidator.IsSafe(value);
}
