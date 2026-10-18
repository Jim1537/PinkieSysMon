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
                ? DecodeTrofeoNodeKey(type, buffer, length) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    internal static string? DecodeTrofeoNodeKey(uint type, byte[] buffer, uint length)
    {
        if (type != DevPropTypeString || length is < 2 or > 400 ||
            length > buffer.Length || length % 2 != 0 ||
            buffer[length - 1] != 0 || buffer[length - 2] != 0)
            return null;
        var id = Encoding.Unicode.GetString(buffer, 0, checked((int)length - 2));
        return UsbDisplayCatalog.TryParseInstanceId(id, out var product, out _) &&
               UsbDisplayCatalog.Find(product)?.CandidateTransport == OutputTargetContract.TrofeoTransport
            ? UsbDisplayDiscovery.CreateObservationId(id) : null;
    }

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
