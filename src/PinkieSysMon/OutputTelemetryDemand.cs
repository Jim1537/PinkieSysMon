using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

// Demand is keyed by active Output ID, never by device count. The union feeds the
// existing single TelemetryEngine/source schedule, so sibling outputs cannot duplicate polls.
internal static class OutputTelemetryDemand
{
    public static IReadOnlySet<string> Collect(
        AppConfig config,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards,
        IEnumerable<string> activeOutputIds)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(boards);
        ArgumentNullException.ThrowIfNull(activeOutputIds);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in ActiveBoards(config, boards, activeOutputIds))
            result.UnionWith(DashboardMetricUsage.Collect(board));

        return result;
    }
    public static MetricHistoryDemand[] CollectHistory(AppConfig config,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards, IEnumerable<string> activeOutputIds) =>
        ActiveBoards(config, boards, activeOutputIds).SelectMany(DashboardMetricUsage.CollectHistory).ToArray();

    private static IEnumerable<CanonicalDashboardDefinition> ActiveBoards(AppConfig config,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards, IEnumerable<string> activeOutputIds)
    {
        var active = activeOutputIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in config.Outputs.Targets)
        {
            if (!active.Contains(target.Id))
                continue;

            // Provider Enabled governs publication of output status, not whether a
            // visible board requests metrics. Never conflate it with rendering.
            if (!OutputTargetContract.IsEnabled(target))
                continue;

            if (!boards.TryGetValue(target.Id, out var board) || board is null)
                throw new InvalidDataException($"Active Output '{target.Id}' has no assigned dashboard definition.");
            yield return board;
        }
    }

}
