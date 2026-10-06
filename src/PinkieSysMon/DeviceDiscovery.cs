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

        if (!string.IsNullOrWhiteSpace(target.DeviceId))
        {
            var selected = candidates.FirstOrDefault(device =>
                device.DeviceId.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase));
            return selected is null
                ? new OutputDeviceResolution(
                    OutputDeviceResolutionStatus.Missing,
                    null,
                    $"Configured output device for target '{target.Name}' is not present.")
                : new OutputDeviceResolution(OutputDeviceResolutionStatus.Found, selected, string.Empty);
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
                return CreateTrofeoDescriptors(ParseMultiSz(buffer));
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
