using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon;

// Read existing device registry values via Configuration Manager, never a
// constructed HKLM path. KEY_QUERY_VALUE / OpenExisting cannot create keys.
internal sealed class WindowsTurzxInterfaceDiscoveryApi : ITurzxInterfaceDiscoveryApi
{
    private readonly WindowsUsbPnpDiscoveryApi _pnp = new();
    private const uint QueryValue = 0x0001;
    private const uint OpenExisting = 1;
    private const int MoreData = 234;
    private const int NotFound = 2;
    private const int AccessDenied = 5;
    private const uint MaximumPropertyBytes = 4096;

    public uint GetListSize(out uint characters) => _pnp.GetListSize(out characters);
    public uint GetList(char[] buffer) => _pnp.GetList(buffer);
    public UsbPnpNode? ReadNode(string instanceId) => _pnp.ReadNode(instanceId);
    public string? ReadInterfaceInstanceId(string path) => PassiveUsbInterfaceIdentity.TryReadInstanceId(path);

    public IReadOnlyList<Guid> ReadInterfaceClasses(string instanceId)
    {
        var result = CM_Locate_DevNodeW(out var node, instanceId, 0);
        if (result == 0x0D) return [];
        if (result != UsbDisplayDiscovery.Success) throw Failure("locate", result);
        result = CM_Open_DevNode_Key(node, QueryValue, 0, OpenExisting, out var key, 0);
        using (key)
        {
            // Missing/denied metadata supplies no usable interface. Never ask
            // for elevated access, create a key, or install another driver.
            if (result != UsbDisplayDiscovery.Success) return [];
            return ReadClasses(key, "DeviceInterfaceGUIDs", 7)
                .Concat(ReadClasses(key, "DeviceInterfaceGUID", 1)).Distinct().ToArray();
        }
    }

    private static IReadOnlyList<Guid> ReadClasses(SafeRegistryHandle key, string name, uint expectedType)
    {
        uint length = 0;
        var result = RegQueryValueExW(key, name, IntPtr.Zero, out var type, null, ref length);
        if (result is NotFound or AccessDenied) return [];
        if (result != 0 && result != MoreData) throw Failure("registry size", unchecked((uint)result));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (length is < 2 or > MaximumPropertyBytes || length % 2 != 0)
                throw new InvalidOperationException("TURZX interface registry value has an invalid size.");
            var buffer = new byte[checked((int)length)];
            result = RegQueryValueExW(key, name, IntPtr.Zero, out type, buffer, ref length);
            if (result == MoreData && attempt < 2) continue;
            if (result is NotFound or AccessDenied) return [];
            if (result != 0) throw Failure("registry read", unchecked((uint)result));
            if (type != expectedType)
                throw new InvalidOperationException("TURZX interface registry value has an invalid type.");
            return DecodeRegisteredClasses(type, buffer, length);
        }
        throw new InvalidOperationException("TURZX interface registry value did not stabilize.");
    }

    internal static IReadOnlyList<Guid> DecodeRegisteredClasses(uint type, byte[] buffer, uint length)
    {
        if (length is < 2 or > MaximumPropertyBytes || length > buffer.Length || length % 2 != 0 ||
            buffer[length - 1] != 0 || buffer[length - 2] != 0 || type is not (1 or 7))
            throw new InvalidOperationException("TURZX interface registry data is malformed.");
        var text = Encoding.Unicode.GetString(buffer, 0, checked((int)length));
        var values = type == 1 ? new[] { text[..^1] } :
            UsbDisplayDiscovery.ParseMultiSz(text.ToCharArray()).ToArray();
        // Do not accept hidden values after a MULTI_SZ terminator.
        if (type == 7 && text != string.Join('\0', values) + (values.Length == 0 ? "\0" : "\0\0") &&
            !(values.Length == 0 && text == "\0\0"))
            throw new InvalidOperationException("TURZX interface registry data has trailing content.");
        if (values.Length > 16 || values.Any(value => !Guid.TryParse(value, out var id) || id == Guid.Empty))
            throw new InvalidOperationException("TURZX interface registry data contains invalid classes.");
        return values.Select(Guid.Parse).Distinct().ToArray();
    }

    public IReadOnlyList<string> EnumerateInterfaces(Guid interfaceClass, string instanceId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = CM_Get_Device_Interface_List_SizeW(out var length, ref interfaceClass, instanceId, 0);
            if (result == 0x0D) return [];
            if (result != UsbDisplayDiscovery.Success) throw Failure("interface size", result);
            if (length is < 1 or > UsbDisplayDiscovery.MaximumListCharacters)
                throw new InvalidOperationException("TURZX interface list has an invalid size.");
            var buffer = new char[checked((int)length)];
            result = CM_Get_Device_Interface_ListW(ref interfaceClass, instanceId, buffer, length, 0);
            if (result == UsbDisplayDiscovery.BufferSmall && attempt < 2) continue;
            if (result == 0x0D) return [];
            if (result != UsbDisplayDiscovery.Success) throw Failure("interface list", result);
            return UsbDisplayDiscovery.ParseMultiSz(buffer);
        }
        throw new InvalidOperationException("TURZX interface list did not stabilize.");
    }

    private static InvalidOperationException Failure(string operation, uint code) =>
        new($"TURZX passive discovery {operation} failed: 0x{code:X8}.");

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string instanceId, uint flags);
    [DllImport("CfgMgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Open_DevNode_Key(uint node, uint access, uint profile,
        uint disposition, out SafeRegistryHandle key, uint flags);
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegQueryValueExW(SafeRegistryHandle key, string name, IntPtr reserved,
        out uint type, [Out] byte[]? data, ref uint length);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass,
        string instanceId, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string instanceId,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer,
        uint length, uint flags);
}
