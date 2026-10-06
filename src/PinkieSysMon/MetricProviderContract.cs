namespace PinkieSysMon;

internal sealed record MetricProviderDescriptor(
    string Id,
    string DisplayName,
    bool DefaultEnabled);

internal static class MetricProviderContract
{
    public const string System = "system";
    public const string LibreHardwareMonitor = "lhm";
    public const string Icue = "icue";

    public static readonly MetricProviderDescriptor[] Descriptors =
    [
        new(System, "System", true),
        new(LibreHardwareMonitor, "LibreHardwareMonitor", false),
        new(Icue, "iCUE", false)
    ];

    public static Dictionary<string, bool> CreateDefaults() =>
        Descriptors.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => descriptor.DefaultEnabled,
            StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, bool> Normalize(IReadOnlyDictionary<string, bool>? providers)
    {
        var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var knownProviderIds = Descriptors
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (providers is not null)
        {
            foreach (var pair in providers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                var providerId = pair.Key.Trim();
                if (knownProviderIds.Contains(providerId))
                    normalized[providerId] = pair.Value;
            }
        }

        foreach (var descriptor in Descriptors)
        {
            if (!normalized.ContainsKey(descriptor.Id))
                normalized[descriptor.Id] = descriptor.DefaultEnabled;
        }

        return normalized;
    }

    public static bool IsEnabled(IReadOnlyDictionary<string, bool>? providers, string providerId)
    {
        if (providers is not null && providers.TryGetValue(providerId, out var enabled))
            return enabled;

        var descriptor = Descriptors.FirstOrDefault(
            item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        return descriptor?.DefaultEnabled ?? false;
    }

    public static IReadOnlySet<string> GetEnabledProviderIds(IReadOnlyDictionary<string, bool>? providers) =>
        Normalize(providers)
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
