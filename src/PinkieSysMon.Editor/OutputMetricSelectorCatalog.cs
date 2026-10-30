using PinkieSysMon;

namespace PinkieSysMon.Editor;

// Deterministic Editor selector projection. Dynamic Output provider branches are
// independent of the System tree and never contact physical device transports.
internal static class OutputMetricSelectorCatalog
{
    public static MetricSelectorCatalogSnapshot Build(AppConfig config, string? currentMetric)
    {
        ArgumentNullException.ThrowIfNull(config);
        var providers = MetricProviderContract.GetDescriptors(config.Outputs.Targets)
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<MetricSelectorMetric>();

        foreach (var output in config.Outputs.Targets)
        {
            var key = OutputProviderContract.Id(output.Id);
            if (!providers.ContainsKey(key))
                throw new InvalidDataException($"Missing Output provider '{key}'.");

            if (!OutputProviderContract.IsEnabled(output))
                continue;

            foreach (var descriptor in OutputMetricContract.DescriptorsFor(output))
            {
                var suffix = descriptor.Id[(key.Length + 1)..];
                items.Add(new MetricSelectorMetric(descriptor.Id, true,
                [
                    new MetricSelectorPathSegment("provider:outputs", "Outputs"),
                    new MetricSelectorPathSegment("provider:" + key,
                        $"{output.Name} [{output.Id}]"),
                    new MetricSelectorPathSegment("metric:" + descriptor.Id, suffix)
                ]));
            }
        }

        // Keep an already assigned metric visible as an unavailable ghost when
        // its Output provider is disabled or that Output was removed.
        MetricSelectorMetric? ghost = null;
        if (!string.IsNullOrWhiteSpace(currentMetric) &&
            currentMetric.StartsWith(OutputMetricContract.Prefix, StringComparison.OrdinalIgnoreCase) &&
            !items.Any(m => m.Id.Equals(currentMetric, StringComparison.OrdinalIgnoreCase)))
        {
            ghost = new MetricSelectorMetric(currentMetric, false,
            [
                new MetricSelectorPathSegment("provider:outputs", "Outputs"),
                new MetricSelectorPathSegment("provider:output-unavailable", "Unavailable Output"),
                new MetricSelectorPathSegment("metric:" + currentMetric, currentMetric)
            ]);
        }

        return new MetricSelectorCatalogSnapshot(items, ghost, ghost is null
            ? null : "The selected Output provider is disabled or no longer configured.");
    }
}
