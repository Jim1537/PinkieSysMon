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
        var candidates = supportedDevices.Where(device =>
            OutputTargetContract.HasIntegratedTransport(device.Transport)).ToArray();
        var ambiguousIds = OutputDeviceSelection.FindAmbiguousDeviceIds(candidates);
        return observations.Select(observation =>
        {
            if (!OutputTargetContract.HasIntegratedTransport(observation.Family.CandidateTransport))
                return observation;
            var matches = candidates.Where(device =>
                device.Transport.Equals(observation.Family.CandidateTransport, StringComparison.OrdinalIgnoreCase) &&
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
}
