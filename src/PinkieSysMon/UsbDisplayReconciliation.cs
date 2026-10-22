namespace PinkieSysMon;

internal static class UsbDisplayReconciliation
{
    // Informational observations never become transport descriptors. Suppress
    // a duplicate informational row only for a proven one-to-one devnode match
    // with an independently discovered interface from the same adapter.
    internal static IReadOnlyList<UsbDisplayObservation> ReadOnlyObservations(
        IReadOnlyList<UsbDisplayObservation> observations,
        IReadOnlyList<OutputDeviceDescriptor> supportedDevices)
    {
        var candidates = supportedDevices.ToArray();
        var ambiguousIds = OutputDeviceSelection.FindAmbiguousDeviceIds(candidates);
        return observations.Select(observation =>
        {
            var matches = candidates.Where(device =>
                (ProductOf(device) is not { } product || product == observation.Family.Product) &&
                device.PnpNodeKey is { } key &&
                observation.NodeKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray();
            var overlaps = matches.Any(device => observations.Count(other =>
                other.NodeKeys.Contains(device.PnpNodeKey!, StringComparer.OrdinalIgnoreCase)) != 1);
            if (matches.Length > 1 || overlaps || matches.Any(device => ambiguousIds.Contains(device.DeviceId)))
                return observation with { Status = UsbDisplayObservationStatus.Ambiguous };
            return matches.Length == 1 && observation.Status == UsbDisplayObservationStatus.Recognized
                ? null : observation;
        }).OfType<UsbDisplayObservation>().OrderBy(o => o.ObservationId, StringComparer.Ordinal).ToArray();
    }
    private static UsbProductId? ProductOf(OutputDeviceDescriptor device)
    {
        if (device.Candidate is { } candidate) return candidate.Family.Product;
        var parts = device.DevicePath.Split('#');
        return parts.Length == 4 && Guid.TryParse(parts[3], out var registered) &&
            CatalogUsbInterfaceDiscovery.TryParseExactInterface(device.DevicePath, registered, out var product)
            ? product : null;
    }

}
