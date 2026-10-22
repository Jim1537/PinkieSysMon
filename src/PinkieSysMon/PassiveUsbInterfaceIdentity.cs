using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon;

// Correlates an interface with its exact Windows devnode, without parsing an
// opaque interface path or promoting the instance token to a hardware serial.
internal static class PassiveUsbInterfaceIdentity
{
    private const uint DevPropTypeString = 0x12;
    private static readonly DevicePropertyKey InstanceIdKey = new(
        new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);

    internal static string? TryReadTrofeoNodeKey(string interfacePath)
    {
        var id = TryReadInstanceId(interfacePath);
        return id is not null && UsbDisplayCatalog.TryParseInstanceId(id, out var product, out _) &&
               UsbDisplayCatalog.Find(product)?.CandidateTransport == OutputTargetContract.LyBulkTransport
            ? UsbDisplayDiscovery.CreateObservationId(id) : null;
    }

    internal static string? TryReadInstanceId(string interfacePath)
    {
        try
        {
            // MAX_DEVICE_ID_LEN includes the terminator. A fixed bounded buffer
            // suffices; unavailable metadata does not replace a saved binding.
            var buffer = new byte[200 * sizeof(char)];
            uint length = (uint)buffer.Length;
            var key = InstanceIdKey;
            var result = CM_Get_Device_Interface_PropertyW(interfacePath, ref key,
                out var type, buffer, ref length, 0);
            return result == UsbDisplayDiscovery.Success
                ? DecodeInstanceId(type, buffer, length) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    internal static string? DecodeTrofeoNodeKey(uint type, byte[] buffer, uint length)
    {
        var id = DecodeInstanceId(type, buffer, length);
        return id is not null && UsbDisplayCatalog.TryParseInstanceId(id, out var product, out _) &&
               UsbDisplayCatalog.Find(product)?.CandidateTransport == OutputTargetContract.LyBulkTransport
            ? UsbDisplayDiscovery.CreateObservationId(id) : null;
    }

    private static string? DecodeInstanceId(uint type, byte[] buffer, uint length)
    {
        if (type != DevPropTypeString || length is < 2 or > 400 ||
            length > buffer.Length || length % 2 != 0 ||
            buffer[length - 1] != 0 || buffer[length - 2] != 0)
            return null;
        var id = Encoding.Unicode.GetString(buffer, 0, checked((int)length - 2));
        return UsbDisplayCatalog.TryParseInstanceId(id, out _, out _) ? id : null;
    }

    internal static string? TryReadPhysicalFingerprint(string instanceId)
    {
        if (!UsbDisplayCatalog.TryParseInstanceId(instanceId, out var product, out var function) || function)
            return null;
        var bytes = ReadRegistryProperty(instanceId, 0x10, 4, sizeof(uint)); // CM_DRP_CAPABILITIES / REG_DWORD
        return bytes is not null && bytes.Length == sizeof(uint) && (BitConverter.ToUInt32(bytes) & 0x10) != 0
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                "usb-serial-v1\n" + product + "\n" + instanceId.Split('\\')[2].ToUpperInvariant()))).ToLowerInvariant()
            : null;
    }

    internal static string? TryReadDisplayName(string instanceId)
    {
        var bytes = ReadRegistryProperty(instanceId, 0x0D, 1, 1024) ?? // CM_DRP_FRIENDLYNAME / REG_SZ
            ReadRegistryProperty(instanceId, 0x01, 1, 1024); // CM_DRP_DEVICEDESC
        if (bytes is null || bytes.Length < 2 || bytes.Length % 2 != 0) return null;
        var name = Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        return name.Length == 0 || name.Any(char.IsControl) ? null : name;
    }

    private static byte[]? ReadRegistryProperty(string instanceId, uint property, uint expectedType, int capacity)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0) return null;
            var buffer = new byte[capacity]; uint length = (uint)capacity;
            return CM_Get_DevNode_Registry_PropertyW(node, property, out var type, buffer, ref length, 0) == 0 &&
                type == expectedType && length <= capacity ? buffer[..checked((int)length)] : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property,
        out uint dataType, [Out] byte[] buffer, ref uint length, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string deviceInterface,
        ref DevicePropertyKey propertyKey, out uint propertyType, [Out] byte[] buffer,
        ref uint length, uint flags);
}
