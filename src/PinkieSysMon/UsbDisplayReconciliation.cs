namespace PinkieSysMon;

internal static class UsbDisplayReconciliation
{
    // Informational observations never become transport descriptors. Suppress
    // a duplicate informational row only for a proven one-to-one devnode match
    // with the existing, independently discovered Trofeo interface.
    internal static IReadOnlyList<UsbDisplayObservation> ReadOnlyObservations(
        IReadOnlyList<UsbDisplayObservation> observations,
        IReadOnlyList<OutputDeviceDescriptor> supportedDevices)
    {
        var candidates = supportedDevices.Where(device =>
            device.Transport == OutputTargetContract.TrofeoTransport).ToArray();
        var ambiguousIds = OutputDeviceSelection.FindAmbiguousDeviceIds(candidates);
        return observations.Select(observation =>
        {
            if (observation.Family.CandidateTransport != OutputTargetContract.TrofeoTransport)
                return observation;
            var matches = candidates.Where(device => device.PnpNodeKey is { } key &&
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
