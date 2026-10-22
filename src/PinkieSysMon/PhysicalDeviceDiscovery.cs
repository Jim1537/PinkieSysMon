using System.ComponentModel;

namespace PinkieSysMon;

// A partial scan is not a successful empty scan. Consumers can retain the
// independent healthy family while marking failed families unavailable.
internal sealed class PhysicalDeviceDiscoveryException(
    IReadOnlyList<OutputDeviceDescriptor> devices, IReadOnlySet<string> unavailableTransports,
    IReadOnlyList<Exception> failures) : InvalidOperationException(
        "Physical discovery is unavailable for: " + string.Join(", ", unavailableTransports.Order()) + ".",
        new AggregateException(failures))
{
    public IReadOnlyList<OutputDeviceDescriptor> Devices { get; } = devices;
    public IReadOnlySet<string> UnavailableTransports { get; } = unavailableTransports;
}

internal static class PhysicalDeviceDiscovery
{
    public static IReadOnlyList<OutputDeviceDescriptor> Capture()
    {
        var snapshot = CatalogUsbInterfaceDiscovery.CaptureSnapshot();
        var devices = Project(snapshot);
        if (snapshot.UnavailableNodeKeys.Count != 0)
            throw new PhysicalDeviceDiscoveryException(devices,
                new HashSet<string>([OutputTargetContract.LyBulkTransport, OutputTargetContract.EncryptedJpegTransport]),
                [new InvalidOperationException("Some catalog interface metadata is unavailable.")]);
        return devices;
    }

    internal static IReadOnlyList<OutputDeviceDescriptor> CaptureForTarget(OutputTargetConfig target)
    {
        var snapshot = CatalogUsbInterfaceDiscovery.CaptureSnapshot();
        var devices = Project(snapshot);
        if (snapshot.UnavailableNodeKeys.Count != 0 &&
            OutputDeviceSelection.Resolve(target, devices).Status == OutputDeviceResolutionStatus.Missing)
            throw new InvalidOperationException("Selected physical interface metadata cannot be established.");
        return devices;
    }

    public static IReadOnlyList<OutputDeviceDescriptor> CaptureTransport(string transport)
    {
        if (!OutputTargetContract.HasIntegratedTransport(transport))
            throw new InvalidOperationException("Unsupported physical discovery protocol.");
        // A hint is not an admission gate for the explicitly selected protocol.
        return Capture();
    }

    internal static IReadOnlyList<OutputDeviceDescriptor> Project(CatalogUsbCandidateSnapshot snapshot) =>
        snapshot.Candidates.Where(candidate => !snapshot.UnavailableNodeKeys.Contains(
            candidate.PnpNodeKey, StringComparer.OrdinalIgnoreCase)).Select(candidate => new OutputDeviceDescriptor(
            candidate.Id, candidate.SuggestedTransport ?? string.Empty,
            candidate.DisplayName, candidate.InterfacePath)
        { PnpNodeKey = candidate.PnpNodeKey, Candidate = candidate, PhysicalFingerprint = candidate.PhysicalFingerprint }).ToArray();

    internal static IReadOnlyList<OutputDeviceDescriptor> Capture(
        Func<IReadOnlyList<OutputDeviceDescriptor>> trofeo,
        Func<IReadOnlyList<OutputDeviceDescriptor>> turzx)
    {
        var devices = new List<OutputDeviceDescriptor>();
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<Exception>();
        void Read(string transport, Func<IReadOnlyList<OutputDeviceDescriptor>> discover)
        {
            try
            {
                var result = discover();
                if (result.Any(device => !device.Transport.Equals(transport, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Physical discovery returned another adapter's descriptor.");
                devices.AddRange(result);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or
                DllNotFoundException or EntryPointNotFoundException)
            {
                unavailable.Add(transport);
                failures.Add(error);
            }
        }
        Read(OutputTargetContract.LyBulkTransport, trofeo);
        Read(OutputTargetContract.EncryptedJpegTransport, turzx);
        if (failures.Count != 0)
            throw new PhysicalDeviceDiscoveryException(devices.AsReadOnly(), unavailable, failures.AsReadOnly());
        return devices.AsReadOnly();
    }
}
