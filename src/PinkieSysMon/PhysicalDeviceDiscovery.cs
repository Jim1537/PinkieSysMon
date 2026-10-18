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
    public static IReadOnlyList<OutputDeviceDescriptor> Capture() =>
        Capture(DeviceDiscovery.EnumerateTrofeoDevices, TurzxDeviceDiscovery.Capture);

    public static IReadOnlyList<OutputDeviceDescriptor> CaptureTransport(string transport) => transport switch
    {
        OutputTargetContract.TrofeoTransport => DeviceDiscovery.EnumerateTrofeoDevices(),
        OutputTargetContract.TurzxTransport => TurzxDeviceDiscovery.Capture(),
        _ => throw new InvalidOperationException("Unsupported physical discovery transport.")
    };

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
        Read(OutputTargetContract.TrofeoTransport, trofeo);
        Read(OutputTargetContract.TurzxTransport, turzx);
        if (failures.Count != 0)
            throw new PhysicalDeviceDiscoveryException(devices.AsReadOnly(), unavailable, failures.AsReadOnly());
        return devices.AsReadOnly();
    }
}
