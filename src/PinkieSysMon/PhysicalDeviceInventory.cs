namespace PinkieSysMon;

// Read-only view of configured and currently observed physical outputs.
// It does not persist settings, open devices, or authorize device enablement.
// Stage 23 can consume this snapshot to present configured and new hardware.
internal enum PhysicalDeviceInventoryStatus
{
    Connected,
    Disconnected,
    Ambiguous,
    Unconfigured
}

internal sealed record PhysicalDeviceInventoryEntry(
    OutputTargetConfig? ConfiguredTarget,
    OutputDeviceDescriptor? DiscoveredDevice,
    PhysicalDeviceInventoryStatus Status)
{
    public bool IsPersisted => ConfiguredTarget is not null;
    public bool CanEditSettings => IsPersisted && Status == PhysicalDeviceInventoryStatus.Connected;
    public bool CanRemove => IsPersisted;
}

internal static class PhysicalDeviceInventory
{
    // Enabling requires a known persisted binding to one presently resolved device.
    // A fresh passive snapshot must be taken just before requesting consent.
    internal static bool CanOfferEnable(PhysicalDeviceInventoryEntry? entry) =>
        entry?.Status == PhysicalDeviceInventoryStatus.Connected &&
        entry.ConfiguredTarget is { DeviceEnabled: false } target &&
        entry.DiscoveredDevice is { } device &&
        !string.IsNullOrWhiteSpace(target.DeviceId) &&
        target.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase);

    // Read-only native discovery uses Windows Configuration Manager only.
    // The overload with a fake discoverer keeps native I/O out of regressions.
    public static IReadOnlyList<PhysicalDeviceInventoryEntry> CaptureCurrent(
        IReadOnlyList<OutputTargetConfig> configuredTargets) =>
        Capture(configuredTargets, DeviceDiscovery.EnumerateTrofeoDevices);

    internal static IReadOnlyList<PhysicalDeviceInventoryEntry> Capture(
        IReadOnlyList<OutputTargetConfig> configuredTargets,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover)
    {
        ArgumentNullException.ThrowIfNull(configuredTargets);
        ArgumentNullException.ThrowIfNull(discover);
        return Build(configuredTargets, discover());
    }

    public static IReadOnlyList<PhysicalDeviceInventoryEntry> Build(
        IReadOnlyList<OutputTargetConfig> configuredTargets,
        IReadOnlyList<OutputDeviceDescriptor> discoveredDevices)
    {
        ArgumentNullException.ThrowIfNull(configuredTargets);
        ArgumentNullException.ThrowIfNull(discoveredDevices);

        var targets = configuredTargets
            .Where(target => target.Kind.Equals(
                OutputTargetContract.TrofeoKind, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var candidates = discoveredDevices
            .Where(device => device.Transport.Equals(
                OutputTargetContract.TrofeoTransport, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var duplicateConfiguredIds = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.DeviceId))
            .GroupBy(target => target.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateDeviceIds = candidates
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateFingerprints = candidates
            .Where(device => !string.IsNullOrWhiteSpace(device.PhysicalFingerprint))
            .GroupBy(device => device.PhysicalFingerprint!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PhysicalDeviceInventoryEntry>();

        // Persisted targets always remain visible, even when unplugged.
        // An unbound legacy target may resolve a single attached device, but its
        // configuration identity remains unchanged. No record is auto-persisted.
        foreach (var target in targets)
        {
            var resolution = OutputDeviceSelection.Resolve(target, candidates);
            var configuredCollision = !string.IsNullOrWhiteSpace(target.DeviceId) &&
                duplicateConfiguredIds.Contains(target.DeviceId);
            var status = configuredCollision
                ? PhysicalDeviceInventoryStatus.Ambiguous
                : resolution.Status switch
                {
                    OutputDeviceResolutionStatus.Found => PhysicalDeviceInventoryStatus.Connected,
                    OutputDeviceResolutionStatus.Missing => PhysicalDeviceInventoryStatus.Disconnected,
                    _ => PhysicalDeviceInventoryStatus.Ambiguous
                };

            var selected = status == PhysicalDeviceInventoryStatus.Connected
                ? resolution.Device
                : null;
            if (selected is not null)
                assignedIds.Add(selected.DeviceId);

            entries.Add(new PhysicalDeviceInventoryEntry(target, selected, status));
        }

        // A newly detected but not yet configured device is only a candidate.
        // If identities conflict, display the ambiguity; never choose one.
        // Adoption/persistence and Device Enabled consent belong to later stages.
        foreach (var device in candidates
            .Where(candidate => !assignedIds.Contains(candidate.DeviceId))
            .OrderBy(candidate => candidate.DeviceId, StringComparer.OrdinalIgnoreCase))
        {
            var conflictsWithConfiguredTarget = targets.Any(target =>
                !string.IsNullOrWhiteSpace(target.DeviceId) &&
                target.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
            var duplicateFingerprint = device.PhysicalFingerprint is { } fingerprint &&
                duplicateFingerprints.Contains(fingerprint);

            var status = conflictsWithConfiguredTarget ||
                         duplicateDeviceIds.Contains(device.DeviceId) ||
                         duplicateFingerprint
                ? PhysicalDeviceInventoryStatus.Ambiguous
                : PhysicalDeviceInventoryStatus.Unconfigured;

            entries.Add(new PhysicalDeviceInventoryEntry(null, device, status));
        }

        return entries;
    }
}

// Configuration-only removal: physical discovery never mutates or recreates an Output.
internal static class PhysicalDeviceRemovalPolicy
{
    public static bool CanRemove(AppConfig config, string targetId) =>
        config.Outputs.Targets.Count > 1 &&
        config.Outputs.Targets.Any(target =>
            target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) &&
            target.Kind.Equals(OutputTargetContract.TrofeoKind, StringComparison.OrdinalIgnoreCase));

    public static void Remove(AppConfig config, string targetId)
    {
        if (!CanRemove(config, targetId))
            throw new InvalidOperationException("Cannot remove an unknown physical Output or the last remaining Output.");
        config.Outputs.Targets.RemoveAll(target =>
            target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
    }
}
