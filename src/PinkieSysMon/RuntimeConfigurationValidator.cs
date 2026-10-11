using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

// Startup/reload validation is read-only: a broken board assignment is diagnosed,
// never repaired by switching app.json to a different dashboard behind the owner's back.
internal static class RuntimeConfigurationValidator
{
    public static (string DashboardName, CanonicalDashboardDefinition Dashboard) ValidateAndSelect(
        AppConfig config, string applicationRoot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);

        var loaded = new Dictionary<string, CanonicalDashboardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in config.Outputs.Targets)
        {
            var name = target.Dashboard
                ?? throw new InvalidDataException($"Output '{target.Id}' has no dashboard assignment.");

            if (!WindowsPathComponentValidator.IsSafe(name))
                throw new InvalidDataException($"Output '{target.Id}' has invalid dashboard assignment '{name}'.");

            if (loaded.ContainsKey(name))
                continue;

            try
            {
                loaded.Add(name, CanonicalDashboardDefinition.Load(
                    DashboardCatalog.GetDefinitionPath(applicationRoot, name)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or System.Text.Json.JsonException or ArgumentException)
            {
                throw new InvalidDataException(
                    $"Output '{target.Id}' ('{target.Name}') references dashboard '{name}' that cannot be loaded: {ex.Message}",
                    ex);
            }
        }

        if (config.Outputs.Targets.Count == 0)
            throw new InvalidDataException("No outputs configured.");

        // Stage 03 bridge: when hardware is enabled use its document for the legacy
        // single-dashboard Runtime. Stage 04 will load per-session dashboards.
        var activePhysical = config.Outputs.Targets.FirstOrDefault(target =>
            target.Kind == OutputTargetContract.TrofeoKind && target.DeviceEnabled);
        var selectedName = activePhysical?.Dashboard ?? config.Outputs.Targets[0].Dashboard!;
        return (selectedName, loaded[selectedName]);
    }
}
