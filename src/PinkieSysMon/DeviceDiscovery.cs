using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

internal sealed record OutputDeviceDescriptor(
    string DeviceId,
    string Transport,
    string DisplayName,
    string DevicePath)
{
    // Optional read-only PnP fingerprint, never a replacement for the existing
    // persisted interface-path DeviceId without an explicit migration.
    public string? PhysicalFingerprint { get; init; }

    public string ShortId
    {
        get
        {
            var separator = DeviceId.IndexOf(':');
            var token = separator >= 0 ? DeviceId[(separator + 1)..] : DeviceId;
            return token.Length <= 8 ? token : token[..8];
        }
    }

    public string SelectionLabel => $"{DisplayName} [{ShortId}]";
}

internal enum OutputDeviceResolutionStatus
{
    Found,
    Missing,
    Ambiguous,
    UnsupportedTransport
}

internal readonly record struct OutputDeviceResolution(
    OutputDeviceResolutionStatus Status,
    OutputDeviceDescriptor? Device,
    string Message);

internal sealed class OutputDeviceResolutionException : InvalidOperationException
{
    public OutputDeviceResolutionException(OutputDeviceResolutionStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    public OutputDeviceResolutionStatus Status { get; }
}

internal static class OutputDeviceSelection
{
    public static OutputDeviceResolution Resolve(
        OutputTargetConfig target,
        IReadOnlyList<OutputDeviceDescriptor> discoveredDevices)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(discoveredDevices);

        if (!target.Transport.Equals(OutputTargetContract.TrofeoTransport, StringComparison.OrdinalIgnoreCase))
        {
            return new OutputDeviceResolution(
                OutputDeviceResolutionStatus.UnsupportedTransport,
                null,
                $"Output target '{target.Name}' uses unsupported transport '{target.Transport}'.");
        }

        var candidates = discoveredDevices
            .Where(device => device.Transport.Equals(target.Transport, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // One claimed physical serial on two interfaces is ambiguous regardless
        // of whether the legacy per-path ID uniquely matches one of them.
        var duplicatePhysicalIds = candidates
            .Where(device => !string.IsNullOrWhiteSpace(device.PhysicalFingerprint))
            .GroupBy(device => device.PhysicalFingerprint!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(target.DeviceId))
        {
            // Exact ID matching is necessary but not sufficient: if future identity
            // sources (such as manufacturer serials) produce a duplicate, never
            // silently choose the first physical interface.
            var matches = candidates
                .Where(device => device.DeviceId.Equals(
                    target.DeviceId, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return matches.Length switch
            {
                0 => new OutputDeviceResolution(
                    OutputDeviceResolutionStatus.Missing,
                    null,
                    $"Configured output device for target '{target.Name}' is not present."),
                1 when matches[0].PhysicalFingerprint is { } fingerprint &&
                       duplicatePhysicalIds.Contains(fingerprint) =>
                    new OutputDeviceResolution(
                        OutputDeviceResolutionStatus.Ambiguous,
                        null,
                        $"Physical identity for output target '{target.Name}' is duplicated across attached interfaces."),
                1 => new OutputDeviceResolution(OutputDeviceResolutionStatus.Found, matches[0], string.Empty),
                _ => new OutputDeviceResolution(
                    OutputDeviceResolutionStatus.Ambiguous,
                    null,
                    $"Configured output identity for target '{target.Name}' matches multiple physical interfaces. Explicit resolution is required.")
            };
        }

        return candidates.Length switch
        {
            0 => new OutputDeviceResolution(
                OutputDeviceResolutionStatus.Missing,
                null,
                $"No compatible output device is present for target '{target.Name}'."),
            1 => new OutputDeviceResolution(OutputDeviceResolutionStatus.Found, candidates[0], string.Empty),
            _ => new OutputDeviceResolution(
                OutputDeviceResolutionStatus.Ambiguous,
                null,
                $"Multiple compatible output devices are present for target '{target.Name}'. Select an explicit device in Settings.")
        };
    }
}

internal static class DeviceDiscovery
{
    // Device interface class exposed by Trofeo Vision 9.16 through its WinUSB binding.
    private static readonly Guid InterfaceClassGuid = new("DEE824EF-729B-4A0E-9C14-B7117D33A817");

    private const uint PresentOnly = 0x00000000;
    private const uint CrSuccess = 0x00000000;
    private const string TrofeoVidPidToken = "vid_0416&pid_5408";

    public static IReadOnlyList<OutputDeviceDescriptor> EnumerateTrofeoDevices()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var guid = InterfaceClassGuid;
            var cr = CM_Get_Device_Interface_List_SizeW(out var length, ref guid, null, PresentOnly);
            if (cr != CrSuccess)
                throw new InvalidOperationException($"CM_Get_Device_Interface_List_SizeW failed: 0x{cr:X8}");

            if (length <= 1)
                return Array.Empty<OutputDeviceDescriptor>();

            var bytes = checked((int)length * sizeof(char));
            var nativeBuffer = Marshal.AllocHGlobal(bytes);
            try
            {
                cr = CM_Get_Device_Interface_ListW(ref guid, null, nativeBuffer, length, PresentOnly);
                if (cr != CrSuccess)
                {
                    // The interface list can change between the size and list calls. Retry from scratch.
                    if (attempt < 2)
                        continue;
                    throw new InvalidOperationException($"CM_Get_Device_Interface_ListW failed: 0x{cr:X8}");
                }

                var buffer = new char[checked((int)length)];
                Marshal.Copy(nativeBuffer, buffer, 0, checked((int)length));
                // Pure CM/PnP metadata lookup: never open the WinUSB transport
                // simply to discover or inspect a physical output identity.
                return CreateTrofeoDescriptors(ParseMultiSz(buffer))
                    .Select(device => device with
                    {
                        PhysicalFingerprint = PassiveTrofeoIdentity.TryReadFingerprint(device.DevicePath)
                    })
                    .ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(nativeBuffer);
            }
        }

        return Array.Empty<OutputDeviceDescriptor>();
    }

    internal static IReadOnlyList<OutputDeviceDescriptor> CreateTrofeoDescriptors(IEnumerable<string> interfacePaths)
    {
        ArgumentNullException.ThrowIfNull(interfacePaths);

        return interfacePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           path.Contains(TrofeoVidPidToken, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new OutputDeviceDescriptor(
                CreateStableDeviceId(path),
                OutputTargetContract.TrofeoTransport,
                OutputTargetContract.DefaultTargetName,
                path))
            .OrderBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string CreateStableDeviceId(string interfacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfacePath);
        var canonicalPath = interfacePath.Trim().ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath));
        return $"trofeo:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static IEnumerable<string> ParseMultiSz(char[] buffer)
    {
        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0')
                continue;

            if (i == start)
                yield break;

            yield return new string(buffer, start, i - start);
            start = i + 1;
        }
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(
        out uint pulLen,
        ref Guid interfaceClassGuid,
        string? pDeviceId,
        uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid,
        string? pDeviceId,
        IntPtr buffer,
        uint bufferLen,
        uint ulFlags);
}

// An optional serial-backed identity observation, not an automatic rebind.
// Validates that the interface path maps to a present USB PnP devnode which
// advertises a system-unique instance ID. Windows USB supplies the instance
// token from iSerialNumber for properly enumerated serial-bearing devices.
// Only a SHA-256 fingerprint is exposed to Runtime; the raw serial is never
// logged, persisted or returned in an OutputDeviceDescriptor.
internal static class PassiveTrofeoIdentity
{
    private const uint CrSuccess = 0;
    private const uint CmDrpCapabilities = 0x10;
    private const uint CmDevcapUniqueId = 0x10;
    private const uint RegDword = 4;
    private const string InstancePrefix = @"USB\VID_0416&PID_5408\";
    private static readonly Guid InterfaceClassGuid = new("DEE824EF-729B-4A0E-9C14-B7117D33A817");

    internal static string? TryReadFingerprint(string interfacePath)
    {
        if (!TryParseInstanceId(interfacePath, out var instanceId, out var instanceToken))
            return null;

        // No CreateFile, USB descriptor requests, WinUsb_Initialize or retries:
        // merely inspect metadata already held by Windows' device tree.
        try
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
                return null;

            var bytes = new byte[sizeof(uint)];
            uint length = sizeof(uint);
            if (CM_Get_DevNode_Registry_PropertyW(
                    devInst, CmDrpCapabilities, out var dataType, bytes, ref length, 0) != CrSuccess ||
                dataType != RegDword || length != sizeof(uint))
                return null;

            return FingerprintIfUnique(instanceToken, BitConverter.ToUInt32(bytes));
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    // Interface paths have the form \\?\usb#vid_0416&pid_5408#<instance>#{GUID}.
    // This parser alone cannot prove the instance token is a hardware serial.
    // Only native PnP CM_DEVCAP_UNIQUEID promotes it to a candidate fingerprint.
    internal static bool TryParseInstanceId(
        string path, out string instanceId, out string instanceToken)
    {
        instanceId = string.Empty;
        instanceToken = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var parts = path.Split('#');
        if (parts.Length != 4 ||
            !parts[0].Equals(@"\\?\usb", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("vid_0416&pid_5408", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(parts[3], out var classGuid) ||
            classGuid != InterfaceClassGuid ||
            string.IsNullOrWhiteSpace(parts[2]) ||
            parts[2].Any(ch => char.IsControl(ch) || ch == '\\' || ch == '/' || ch == '#'))
            return false;

        instanceToken = parts[2];
        instanceId = InstancePrefix + instanceToken;
        return true;
    }

    // Do not treat a port-derived or bus-local PnP instance token as a serial.
    internal static string? FingerprintIfUnique(string token, uint capabilities) =>
        (capabilities & CmDevcapUniqueId) != 0 ? Fingerprint(token) : null;

    internal static string Fingerprint(string instanceToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceToken);
        var canonical = "trofeo-usb-serial-v1\nVID_0416&PID_5408\n" +
            instanceToken.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(
        out uint devInst, string deviceId, uint flags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(
        uint devInst, uint property, out uint dataType, [Out] byte[] buffer,
        ref uint length, uint flags);
}
