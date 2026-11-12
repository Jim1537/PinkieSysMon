namespace PinkieSysMon;

// Filesystem paths are private application settings, never dashboard metric identities.
internal sealed class FilesystemConfig
{
    public List<FilesystemSourceConfig> Sources { get; set; } = [];
}

internal sealed class FilesystemSourceConfig
{
    public string Id { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = FilesystemSourceContract.FileKind;

    public FilesystemSourceConfig Copy() => new()
    {
        Id = Id, Alias = Alias, Path = Path, Kind = Kind
    };
}

internal static class FilesystemSourceContract
{
    public const string FileKind = "File";
    public const string DirectoryKind = "Directory";
    public const string Prefix = "filesystem.";
    public const string Size = "size";
    public const string Created = "created";
    public const string Modified = "modified";
    public const string Files = "files";
    public const string Directories = "directories";

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static string MetricId(string sourceId, string suffix) =>
        Prefix + sourceId + "." + suffix;

    public static bool IsMetricId(string? metric) =>
        metric?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    public static string? SourceIdForMetric(string metric)
    {
        if (!IsMetricId(metric)) return null;
        var remaining = metric[Prefix.Length..];
        var separator = remaining.IndexOf('.');
        if (separator <= 0) return null;
        var id = remaining[..separator];
        var suffix = remaining[(separator + 1)..];
        return Guid.TryParseExact(id, "N", out _) &&
               (suffix.Equals(Size, StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals(Created, StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals(Modified, StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals(Files, StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals(Directories, StringComparison.OrdinalIgnoreCase))
            ? id.ToLowerInvariant() : null;
    }

    public static bool IsDirectoryOnlyMetric(string metricName) =>
        metricName.EndsWith("." + Files, StringComparison.OrdinalIgnoreCase) ||
        metricName.EndsWith("." + Directories, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<MetricDescriptor> DescriptorsFor(IEnumerable<FilesystemSourceConfig> sources)
    {
        var descriptors = new List<MetricDescriptor>();
        foreach (var source in sources)
        {
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Size), MetricValueKind.DataSize, MetricUnit.Bytes));
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Created), MetricValueKind.DateTime, MetricUnit.None));
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Modified), MetricValueKind.DateTime, MetricUnit.None));
            if (source.Kind == DirectoryKind)
            {
                descriptors.Add(new MetricDescriptor(MetricId(source.Id, Files), MetricValueKind.Number, MetricUnit.None));
                descriptors.Add(new MetricDescriptor(MetricId(source.Id, Directories), MetricValueKind.Number, MetricUnit.None));
            }
        }
        return descriptors;
    }

    // Validation is deliberately metadata-only. The Editor's explicit preflight
    // is responsible for verifying actual filesystem access before admission.
    public static void Validate(FilesystemConfig? config)
    {
        if (config?.Sources is null)
            throw new InvalidDataException("Filesystem.Sources must be a source list.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in config.Sources)
        {
            if (source is null)
                throw new InvalidDataException("Filesystem.Sources contains a null record.");
            if (!Guid.TryParseExact(source.Id, "N", out var parsed) || parsed == Guid.Empty)
                throw new InvalidDataException("Filesystem source has an invalid stable ID.");
            source.Id = source.Id.ToLowerInvariant();
            if (!ids.Add(source.Id))
                throw new InvalidDataException("Filesystem sources contain duplicate IDs.");
            if (string.IsNullOrWhiteSpace(source.Alias))
                throw new InvalidDataException("Filesystem source alias cannot be empty.");
            source.Alias = source.Alias.Trim();
            if (!aliases.Add(source.Alias))
                throw new InvalidDataException("Filesystem sources must have distinct aliases.");
            if (source.Kind is not (FileKind or DirectoryKind))
                throw new InvalidDataException("Filesystem source kind must be File or Directory.");
            if (string.IsNullOrWhiteSpace(source.Path) || source.Path != source.Path.Trim())
                throw new InvalidDataException("Filesystem source path must be a literal absolute path without surrounding whitespace.");

            string pathKey;
            try
            {
                if (!Path.IsPathFullyQualified(source.Path))
                    throw new InvalidDataException("Filesystem source path must be fully qualified.");
                pathKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Path));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException("Filesystem source path is invalid.", error);
            }
            if (!paths.Add(pathKey))
                throw new InvalidDataException("Filesystem sources contain duplicate paths.");
        }
    }

    public static bool SameSources(IEnumerable<FilesystemSourceConfig> left, IEnumerable<FilesystemSourceConfig> right) =>
        left.Select(s => (s.Id, s.Alias, s.Path, s.Kind))
            .SequenceEqual(right.Select(s => (s.Id, s.Alias, s.Path, s.Kind)));
}
