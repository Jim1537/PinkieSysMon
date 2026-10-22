using System.Text.Json;

namespace PinkieSysMon.Editor;

// A per-card commit. The currently saved configuration is the merge base, so
// edits to other Outputs made while this card was open are retained.
internal static class PhysicalDeviceSettingsTransaction
{
    internal static async Task<(AppConfig Config, bool RuntimeReloaded)> SaveCandidateAsync(
        string applicationRoot, string configPath, FileLogger log,
        OutputTargetConfig confirmed, string confirmedPnpNodeKey,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover,
        Func<AppConfig, byte[]?, Task<bool>> persistAndApply)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (confirmed.DeviceEnabled || confirmed.ProviderEnabled)
            throw new InvalidOperationException("A newly saved device must remain disabled.");

        // Re-enumerate immediately before writing, independent of the dialog's
        // last WM_DEVICECHANGE snapshot. Never adopt by VID/PID or name.
        var resolution = OutputDeviceSelection.Resolve(confirmed, discover());
        if (resolution.Status != OutputDeviceResolutionStatus.Found ||
            !string.Equals(resolution.Device!.PnpNodeKey, confirmedPnpNodeKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The confirmed interface is no longer uniquely present on the same physical node.");

        var (current, bytes) = ReadCurrent(configPath, log);
        if (current.Outputs.Targets.Any(t =>
                t.Id.Equals(confirmed.Id, StringComparison.OrdinalIgnoreCase) ||
                (OutputTargetContract.IsPhysicalKind(t.Kind) &&
                 t.DeviceId.Equals(confirmed.DeviceId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("This device was configured elsewhere while the card was open. Reopen Settings.");

        var previousIds = current.Outputs.Targets.Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        current.Outputs.Targets.Add(confirmed);
        using var provision = InitialOutputDashboardProvisioning.Prepare(
            applicationRoot, configPath, bytes, previousIds, current);
        _ = RuntimeConfigurationValidator.LoadTarget(confirmed, applicationRoot);
        var reloaded = await persistAndApply(current, bytes);
        provision.Commit();
        return (current, reloaded);
    }

    internal static async Task<(AppConfig Config, bool RuntimeReloaded)> SaveConfiguredAsync(
        string applicationRoot, string configPath, FileLogger log,
        OutputTargetConfig original, OutputTargetConfig updated,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover,
        Func<AppConfig, byte[]?, Task<bool>> persistAndApply)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        if (!OutputTargetContract.ConnectionEquals(original, updated))
            throw new InvalidOperationException("Changing a configured connection requires Remove, Test and Save Configuration.");
        var resolution = OutputDeviceSelection.Resolve(updated, discover());
        if (resolution.Status != OutputDeviceResolutionStatus.Found)
            throw new InvalidOperationException("The selected physical interface is no longer uniquely present.");

        var (current, bytes) = ReadCurrent(configPath, log);
        var index = current.Outputs.Targets.FindIndex(t => t.Id.Equals(original.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !OutputTargetContract.IsPhysicalKind(current.Outputs.Targets[index].Kind) ||
            JsonSerializer.Serialize(current.Outputs.Targets[index]) != JsonSerializer.Serialize(original))
            throw new InvalidOperationException("This Output changed elsewhere while its card was open. Reopen Settings.");
        current.Outputs.Targets[index] = updated;
        _ = RuntimeConfigurationValidator.LoadTarget(updated, applicationRoot);
        var reloaded = await persistAndApply(current, bytes);
        return (current, reloaded);
    }

    private static (AppConfig Config, byte[]? Bytes) ReadCurrent(string path, FileLogger log)
    {
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        return (AppConfig.Load(path, log), bytes);
    }
}
