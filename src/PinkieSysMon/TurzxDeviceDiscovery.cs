using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

// All values here come from cached Windows metadata. Implementations cannot
// initialize WinUSB, request USB descriptors, or send any device command.
internal interface ITurzxInterfaceDiscoveryApi : IUsbInterfaceDiscoveryApi { }

internal static class TurzxDeviceDiscovery
{
    internal static readonly UsbProductId Product = new(0x1CBE, 0x0123);
    internal const string ModelName = "TURZX 12.3-inch";

    // Shared inventory/Settings consume these exact passive bindings. Presence
    // alone never grants consent or opens a frame transport.
    public static IReadOnlyList<OutputDeviceDescriptor> Capture() => Capture(new WindowsTurzxInterfaceDiscoveryApi());

    public static IReadOnlyList<OutputDeviceDescriptor> CaptureForRecovery() =>
        Capture(new WindowsTurzxInterfaceDiscoveryApi(), requireCompleteMetadata: true);

    internal static IReadOnlyList<OutputDeviceDescriptor> Capture(ITurzxInterfaceDiscoveryApi api,
        bool requireCompleteMetadata = false)
    {
        var devices = new List<OutputDeviceDescriptor>();
        foreach (var node in UsbDisplayDiscovery.CaptureNodes(api, product => product == Product))
        {
            if (!IsEligibleNode(node))
            {
                if (requireCompleteMetadata) throw IncompleteRecoveryScan();
                continue;
            }
            var nodeStart = devices.Count;
            var classes = api.ReadInterfaceClasses(node.InstanceId);
            if (classes.Count > 16 || classes.Any(value => value == Guid.Empty))
                throw new InvalidOperationException("TURZX interface metadata is invalid or exceeds its bound.");
            foreach (var interfaceClass in classes.Distinct())
            foreach (var path in api.EnumerateInterfaces(interfaceClass, node.InstanceId)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsExactInterface(path, interfaceClass) ||
                    !node.InstanceId.Equals(api.ReadInterfaceInstanceId(path), StringComparison.OrdinalIgnoreCase))
                    continue;
                var current = api.ReadNode(node.InstanceId);
                if (current is null || !IsEligibleNode(current) ||
                    !node.InstanceId.Equals(current.InstanceId, StringComparison.OrdinalIgnoreCase))
                    continue;
                devices.Add(new OutputDeviceDescriptor(CreateStableDeviceId(path),
                    OutputTargetContract.EncryptedJpegTransport, ModelName, path)
                { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(node.InstanceId) });
            }
            // Missing registry access/registration, driver problems or an
            // uncorrelated interface on a PRESENT node cannot prove unplugging.
            if (requireCompleteMetadata && devices.Count == nodeStart)
                throw IncompleteRecoveryScan();
        }
        // Multiple distinct interfaces for one node remain distinct descriptors:
        // the existing duplicate-node guard rejects them, even for an exact ID.
        return devices.DistinctBy(device => (device.DeviceId, device.PnpNodeKey))
            .OrderBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static InvalidOperationException IncompleteRecoveryScan() =>
        new("TURZX recovery discovery cannot establish complete present-interface metadata.");

    internal static bool IsEligibleNode(UsbPnpNode node) =>
        UsbDisplayCatalog.TryParseInstanceId(node.InstanceId, out var product, out var function) &&
        product == Product && !function && node.MetadataAvailable && node.ProblemCode == 0 &&
        string.Equals(node.DriverService, "WINUSB", StringComparison.OrdinalIgnoreCase);

    internal static bool IsExactInterface(string? path, Guid registeredClass)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 ||
            path.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch))) return false;
        var parts = path.Split('#');
        return parts.Length == 4 && parts[0].Equals(@"\\?\usb", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("vid_1cbe&pid_0123", StringComparison.OrdinalIgnoreCase) && parts[2].Length > 0 &&
            !parts[2].Any(ch => ch is '\\' or '/') && Guid.TryParse(parts[3], out var actualClass) &&
            registeredClass != Guid.Empty && actualClass == registeredClass;
    }

    internal static string CreateStableDeviceId(string interfacePath)
    {
        return CatalogUsbInterfaceDiscovery.StableInterfaceId(interfacePath);
    }
}
