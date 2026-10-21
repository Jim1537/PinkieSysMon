namespace PinkieSysMon;

// Read-only view of configured and currently observed physical outputs.
// It does not persist settings, open devices, or authorize device enablement.
// Stage 23 can consume this snapshot to present configured and new hardware.
internal enum PhysicalDeviceInventoryStatus
{
    Connected,
    Disconnected,
    Ambiguous,
    Unconfigured,
    DiscoveryUnavailable
}

internal sealed record PhysicalDeviceInventoryEntry(
    OutputTargetConfig? ConfiguredTarget,
    OutputDeviceDescriptor? DiscoveredDevice,
    PhysicalDeviceInventoryStatus Status)
{
    public bool IsPersisted => ConfiguredTarget is not null;
    public bool CanEditSettings => IsPersisted && Status == PhysicalDeviceInventoryStatus.Connected &&
        OutputTargetContract.HasIntegratedTransport(ConfiguredTarget!.Transport);
    public bool CanRemove => IsPersisted;
}

internal static class PhysicalDeviceInventory
{
    // Enabling requires a known persisted binding to one presently resolved device.
    // A fresh passive snapshot must be taken just before requesting consent.
    internal static bool CanOfferEnable(PhysicalDeviceInventoryEntry? entry) =>
        entry?.Status == PhysicalDeviceInventoryStatus.Connected &&
        entry.ConfiguredTarget is { DeviceEnabled: false } target &&
        OutputTargetContract.HasIntegratedTransport(target.Transport) &&
        entry.DiscoveredDevice is { } device &&
        !string.IsNullOrWhiteSpace(target.DeviceId) &&
        target.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase);

    // Read-only native discovery uses Windows Configuration Manager only.
    // The overload with a fake discoverer keeps native I/O out of regressions.
    public static IReadOnlyList<PhysicalDeviceInventoryEntry> CaptureCurrent(
        IReadOnlyList<OutputTargetConfig> configuredTargets) =>
        Capture(configuredTargets, PhysicalDeviceDiscovery.Capture);

    internal static IReadOnlyList<PhysicalDeviceInventoryEntry> Capture(
        IReadOnlyList<OutputTargetConfig> configuredTargets,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover)
    {
        ArgumentNullException.ThrowIfNull(configuredTargets);
        ArgumentNullException.ThrowIfNull(discover);
        try { return Build(configuredTargets, discover()); }
        catch (PhysicalDeviceDiscoveryException error)
        {
            return Build(configuredTargets, error.Devices).Select(entry =>
                entry.ConfiguredTarget is { } target && error.UnavailableTransports.Contains(target.Transport)
                    ? entry with { Status = PhysicalDeviceInventoryStatus.DiscoveryUnavailable, DiscoveredDevice = null }
                    : entry).ToArray();
        }
    }

    public static IReadOnlyList<PhysicalDeviceInventoryEntry> Build(
        IReadOnlyList<OutputTargetConfig> configuredTargets,
        IReadOnlyList<OutputDeviceDescriptor> discoveredDevices)
    {
        ArgumentNullException.ThrowIfNull(configuredTargets);
        ArgumentNullException.ThrowIfNull(discoveredDevices);

        var targets = configuredTargets
            .Where(target => OutputTargetContract.IsPhysicalKind(target.Kind))
            .ToArray();
        var candidates = discoveredDevices
            .Where(device => device.Transport.Equals(OutputTargetContract.TrofeoTransport, StringComparison.OrdinalIgnoreCase) ||
                device.Transport.Equals(OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var duplicateConfiguredIds = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.DeviceId))
            .GroupBy(target => target.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ambiguousDeviceIds = OutputDeviceSelection.FindAmbiguousDeviceIds(candidates);

        var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PhysicalDeviceInventoryEntry>();

        // Persisted targets always remain visible, even when unplugged.
        // A target without an exact saved interface ID stays disconnected,
        // even if there is only one compatible discovered device.
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
            var status = conflictsWithConfiguredTarget ||
                         ambiguousDeviceIds.Contains(device.DeviceId)
                ? PhysicalDeviceInventoryStatus.Ambiguous
                : PhysicalDeviceInventoryStatus.Unconfigured;

            entries.Add(new PhysicalDeviceInventoryEntry(null, device, status));
        }

        return entries;
    }
}
