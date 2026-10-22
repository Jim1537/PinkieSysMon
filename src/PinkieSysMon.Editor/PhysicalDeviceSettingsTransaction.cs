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
        if (current.Outputs.IgnoredDeviceNodeKeys.Contains(confirmedPnpNodeKey, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("This physical node was forgotten elsewhere. Reopen Settings.");
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

    internal static async Task<(AppConfig Config, bool RuntimeReloaded)> ForgetCandidateAsync(
        string configPath, FileLogger log, string interfaceId, string nodeKey,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover,
        Func<AppConfig, byte[]?, Task<bool>> persistAndApply)
    {
        // The node key is a hash of one observed PnP instance. A path, model
        // name or VID/PID cannot stand in for this identity.
        var devices = discover();
        var matches = devices.Where(d => d.DeviceId.Equals(interfaceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].PnpNodeKey is not { } actualNode ||
            !actualNode.Equals(nodeKey, StringComparison.OrdinalIgnoreCase) ||
            devices.Count(d => d.PnpNodeKey?.Equals(nodeKey, StringComparison.OrdinalIgnoreCase) == true) != 1 ||
            OutputDeviceSelection.FindAmbiguousDeviceIds(devices).Contains(interfaceId))
            throw new InvalidOperationException("The candidate's exact physical identity is no longer unique. Reopen Settings.");

        var (current, bytes) = ReadCurrent(configPath, log);
        if (current.Outputs.IgnoredDeviceNodeKeys.Contains(nodeKey, StringComparer.OrdinalIgnoreCase) ||
            current.Outputs.Targets.Any(t => OutputTargetContract.IsPhysicalKind(t.Kind) &&
                (t.DeviceId.Equals(interfaceId, StringComparison.OrdinalIgnoreCase) ||
                 devices.Any(d => d.DeviceId.Equals(t.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                     d.PnpNodeKey?.Equals(nodeKey, StringComparison.OrdinalIgnoreCase) == true))))
            throw new InvalidOperationException("This device was already configured or forgotten elsewhere. Reopen Settings.");
        current.Outputs.IgnoredDeviceNodeKeys.Add(nodeKey);
        var reloaded = await persistAndApply(current, bytes);
        return (current, reloaded);
    }

    internal static async Task<(AppConfig Config, bool RuntimeReloaded, string? Warning)> RemoveConfiguredAsync(
        string configPath, FileLogger log, OutputTargetConfig original,
        Func<Task<RuntimeIpcStatusResponse>> runtimeStatus, Func<bool> ownerExists,
        Func<IDisposable?> acquireAbsentRuntime,
        Func<string, string, Task<RuntimeIpcResponse>> stopSelected,
        Func<Task<RuntimeIpcResponse>> reload)
    {
        var (current, _) = ReadCurrent(configPath, log);
        static int Index(AppConfig app, OutputTargetConfig selected) => app.Outputs.Targets.FindIndex(t =>
            t.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        var index = Index(current, original);
        if (index < 0 || !OutputTargetContract.IsPhysicalKind(current.Outputs.Targets[index].Kind) ||
            JsonSerializer.Serialize(current.Outputs.Targets[index]) != JsonSerializer.Serialize(original))
            throw new InvalidOperationException("The selected Output changed elsewhere. Reopen Settings.");
        if (current.Outputs.Targets.Count <= 1)
            throw new InvalidOperationException("At least one Output must remain configured.");

        var status = await runtimeStatus();
        var running = status.Success;
        if (running)
        {
            if (status.Status?.Outputs.Count(o => o.TargetId.Equals(original.Id, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidOperationException("Runtime cannot identify exactly one selected Output. No record was removed.");
            var stopped = await stopSelected(original.Id, RuntimeIpcProtocol.TargetRecordHash(original));
            if (!stopped.Success || !stopped.ResponseReceived)
                throw new InvalidOperationException("Remove may have stopped the selected Runtime session, but terminal native release was not confirmed. Configuration remains saved; inspect Runtime before retrying. " + stopped.Message);
        }
        else if (ownerExists())
            throw new InvalidOperationException("Runtime ownership exists but its status is unavailable. No record was removed.");

        if (!running)
        {
            // Hold the same process lease that Runtime requires while the file
            // changes, so a new Runtime cannot start from the old binding.
            using var lease = acquireAbsentRuntime() ?? throw new InvalidOperationException(
                "A Runtime started while Remove was preparing. No record was removed.");
            var (inactive, expectedInactive) = ReadCurrent(configPath, log);
            var inactiveIndex = Index(inactive, original);
            if (inactiveIndex < 0 || JsonSerializer.Serialize(inactive.Outputs.Targets[inactiveIndex]) != JsonSerializer.Serialize(original) ||
                inactive.Outputs.Targets.Count <= 1)
                throw new InvalidOperationException("The selected Output changed while Remove was preparing. No record was removed.");
            inactive.Outputs.Targets.RemoveAt(inactiveIndex);
            EditorSettingsPersistence.ApplyRemovalWithoutRuntime(configPath, inactive, expectedInactive);
            return (inactive, false, null);
        }

        // Recheck after native cleanup; a concurrent writer's unrelated edits
        // survive, while a changed selected record cannot be overwritten.
        try
        {
            var (latest, expected) = ReadCurrent(configPath, log);
            index = Index(latest, original);
            if (index < 0 || JsonSerializer.Serialize(latest.Outputs.Targets[index]) != JsonSerializer.Serialize(original) ||
                latest.Outputs.Targets.Count <= 1)
                throw new InvalidOperationException("The selected Output changed after Runtime stop.");
            latest.Outputs.Targets.RemoveAt(index);
            var (reloaded, warning) = await EditorSettingsPersistence.ApplyRemovalAsync(
                configPath, latest, expected, reload);
            return (latest, reloaded, warning);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Runtime stopped the selected Output, but its saved record could not be removed. It remains stopped in this Runtime; inspect the file and reopen Settings to resolve this partial Remove.", error);
        }
    }

    private static (AppConfig Config, byte[]? Bytes) ReadCurrent(string path, FileLogger log)
    {
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        return (AppConfig.Load(path, log), bytes);
    }
}
