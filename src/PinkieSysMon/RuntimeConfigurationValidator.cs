using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

// Each output owns an independently loaded document. Sharing the same saved
// dashboard name does not share mutable renderer/dashboard state between outputs.
internal static class RuntimeConfigurationValidator
{
    public static Dictionary<string, CanonicalDashboardDefinition> LoadAssignments(
        AppConfig config, string applicationRoot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        var result = new Dictionary<string, CanonicalDashboardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in config.Outputs.Targets)
        {
            if (!result.TryAdd(target.Id, LoadTarget(target, applicationRoot)))
                throw new InvalidDataException($"Duplicate output ID '{target.Id}' during dashboard loading.");
        }
        if (result.Count == 0)
            throw new InvalidDataException("No outputs configured.");
        return result;
    }

    public static CanonicalDashboardDefinition LoadTarget(OutputTargetConfig target, string applicationRoot)
    {
        ArgumentNullException.ThrowIfNull(target);
        var name = target.Dashboard
            ?? throw new InvalidDataException($"Output '{target.Id}' has no dashboard assignment.");
        if (!WindowsPathComponentValidator.IsSafe(name))
            throw new InvalidDataException($"Output '{target.Id}' has invalid dashboard assignment '{name}'.");

        try
        {
            return CanonicalDashboardDefinition.Load(DashboardCatalog.GetDefinitionPath(applicationRoot, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or System.Text.Json.JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Output '{target.Id}' ('{target.Name}') references dashboard '{name}' that cannot be loaded: {ex.Message}",
                ex);
        }
    }

    // Runtime needs documents only for enabled Outputs. Configuration validation
    // still owns every record, including disabled identities and saved references.
    public static Dictionary<string, CanonicalDashboardDefinition> LoadEnabledAssignments(
        AppConfig config, string applicationRoot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        if (config.Outputs.Targets.Count == 0)
            throw new InvalidDataException("No outputs configured.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, CanonicalDashboardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in config.Outputs.Targets)
        {
            if (!ids.Add(target.Id))
                throw new InvalidDataException($"Duplicate output ID '{target.Id}' during dashboard loading.");
            if (OutputTargetContract.IsEnabled(target))
                result.Add(target.Id, LoadTarget(target, applicationRoot));
        }
        return result;
    }

    // Legacy helper used by the Stage 03 regression contract. Runtime and output
    // sessions do not use the single-board selection. Settings can still validate
    // all assignments; Runtime uses the enabled-only loader.
    public static (string DashboardName, CanonicalDashboardDefinition Dashboard) ValidateAndSelect(
        AppConfig config, string applicationRoot)
    {
        var assignments = LoadAssignments(config, applicationRoot);
        var target = config.Outputs.Targets.FirstOrDefault(t =>
            t.Kind == OutputTargetContract.TrofeoKind && t.DeviceEnabled)
            ?? config.Outputs.Targets[0];
        return (target.Dashboard!, assignments[target.Id]);
    }
}
