using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon;

// Reads cached Configuration Manager metadata only. USB enumerator devnodes
// remain observable independently of WinUSB, HID or mass-storage driver choice.
internal sealed class WindowsUsbPnpDiscoveryApi : IUsbPnpDiscoveryApi
{
    private const uint PresentUsbEnumerator = 0x00000101;
    private const uint NoSuchDevnode = 0x0D;
    private const uint ServiceProperty = 0x05;
    private const uint ClassGuidProperty = 0x09;
    private const uint RegSz = 1;
    private const int MaximumPropertyBytes = 4096;

    public uint GetListSize(out uint characters) =>
        CM_Get_Device_ID_List_SizeW(out characters, "USB", PresentUsbEnumerator);

    public uint GetList(char[] buffer) =>
        CM_Get_Device_ID_ListW("USB", buffer, checked((uint)buffer.Length), PresentUsbEnumerator);

    public UsbPnpNode? ReadNode(string instanceId)
    {
        var result = CM_Locate_DevNodeW(out var node, instanceId, 0);
        if (result == NoSuchDevnode) return null;
        if (result != UsbDisplayDiscovery.Success)
            throw new InvalidOperationException($"USB PnP locate failed: 0x{result:X8}.");

        result = CM_Get_DevNode_Status(out _, out _, node, 0);
        if (result == NoSuchDevnode) return null;
        var service = ReadString(node, ServiceProperty);
        var classValue = ReadString(node, ClassGuidProperty);
        Guid? setupClass = Guid.TryParse(classValue, out var parsed) ? parsed : null;
        string? parentId = null;
        var parentAvailable = true;
        if (UsbDisplayCatalog.TryParseInstanceId(instanceId, out _, out var function) && function)
        {
            parentAvailable = false;
            if (CM_Get_Parent(out var parent, node, 0) == UsbDisplayDiscovery.Success)
            {
                var buffer = new StringBuilder(200);
                if (CM_Get_Device_IDW(parent, buffer, buffer.Capacity, 0) == UsbDisplayDiscovery.Success)
                {
                    parentId = buffer.ToString();
                    parentAvailable = true;
                }
            }
        }
        // Recheck presence after metadata reads; never publish a removed devnode
        // as a fresh successful observation. Other failures remain visible gaps.
        var finalResult = CM_Get_DevNode_Status(out _, out var finalProblem, node, 0);
        if (finalResult == NoSuchDevnode) return null;
        return new UsbPnpNode(instanceId, parentId, service, setupClass,
            finalResult == UsbDisplayDiscovery.Success ? finalProblem : null,
            result == UsbDisplayDiscovery.Success && finalResult == UsbDisplayDiscovery.Success &&
            service is not null && setupClass is not null && parentAvailable);
    }

    private static string? ReadString(uint node, uint property)
    {
        uint length = 0;
        var result = CM_Get_DevNode_Registry_PropertyW(node, property, out var type, null, ref length, 0);
        if (result != UsbDisplayDiscovery.BufferSmall ||
            length is < 2 or > MaximumPropertyBytes || length % 2 != 0)
            return null;
        // Retry only bounded buffer growth. Missing properties/permissions do
        // not imply a usable driver; the caller marks metadata unavailable.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[checked((int)length)];
            result = CM_Get_DevNode_Registry_PropertyW(node, property, out type, buffer, ref length, 0);
            if (result == UsbDisplayDiscovery.BufferSmall && attempt < 2 &&
                length is >= 2 and <= MaximumPropertyBytes && length % 2 == 0)
                continue;
            if (result != UsbDisplayDiscovery.Success || type != RegSz || length < 2 ||
                length > buffer.Length || length % 2 != 0 || buffer[length - 1] != 0 || buffer[length - 2] != 0)
                return null;
            var value = Encoding.Unicode.GetString(buffer, 0, checked((int)length - 2));
            return string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) ? null : value;
        }
        return null;
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer,
        uint length, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string instanceId, uint flags);
    [DllImport("CfgMgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
    [DllImport("CfgMgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, int length, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type,
        [Out] byte[]? buffer, ref uint length, uint flags);
}
