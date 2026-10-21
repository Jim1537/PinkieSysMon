using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

// PnP IDs remain private to discovery. In particular, do not use a record's
// generated ToString to expose an instance token in diagnostics.
internal sealed class UsbPnpNode(string instanceId, string? parentInstanceId,
    string? driverService, Guid? setupClass, uint? problemCode, bool metadataAvailable)
{
    internal string InstanceId { get; } = instanceId;
    internal string? ParentInstanceId { get; } = parentInstanceId;
    internal string? DriverService { get; } = driverService;
    internal Guid? SetupClass { get; } = setupClass;
    internal uint? ProblemCode { get; } = problemCode;
    internal bool MetadataAvailable { get; } = metadataAvailable;
}

internal enum UsbDisplayObservationStatus
{
    Recognized,
    MetadataUnavailable,
    Ambiguous
}

// A recognition-only observation is deliberately not an OutputDeviceDescriptor:
// it has neither a transport key nor an openable interface path. ObservationId
// is a snapshot correlation key, not a serial or a persisted Output binding.
internal sealed record UsbDisplayObservation(string ObservationId, UsbDisplayFamily Family,
    UsbDisplayObservationStatus Status, int NodeCount,
    IReadOnlyList<string> DriverServices, IReadOnlyList<Guid> SetupClasses,
    IReadOnlyList<uint> ProblemCodes)
{
    // Exact devnode membership for snapshot reconciliation only. These hashes
    // carry no promise of stability across a topology change or Windows restart.
    public IReadOnlyList<string> NodeKeys { get; init; } = [];
}

// The only native seam exposes Windows' existing PnP metadata. There is no
// method for opening a device, requesting descriptors, or sending commands.
internal interface IUsbPnpDiscoveryApi
{
    uint GetListSize(out uint characters);
    uint GetList(char[] buffer);
    UsbPnpNode? ReadNode(string instanceId);
}

internal static class UsbDisplayDiscovery
{
    internal const uint Success = 0;
    internal const uint BufferSmall = 0x1A;
    internal const int MaximumListCharacters = 1_048_576;

    public static IReadOnlyList<UsbDisplayObservation> Capture() => Capture(new WindowsUsbPnpDiscoveryApi());

    internal static IReadOnlyList<UsbDisplayObservation> Capture(IUsbPnpDiscoveryApi api)
        => BuildObservations(CaptureNodes(api, product => UsbDisplayCatalog.Find(product) is not null));

    // Shared bounded metadata capture; callers select products before per-node reads.
    internal static IReadOnlyList<UsbPnpNode> CaptureNodes(IUsbPnpDiscoveryApi api,
        Func<UsbProductId, bool> include, Action<string>? onReadFailure = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(include);
        // A changing device list is retried, but API errors are not empty scans.
        // Use no timer, global subscription, device handle or speculative I/O.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = api.GetListSize(out var length);
            if (result != Success) throw Failure("size", result);
            if (length is < 1 or > MaximumListCharacters)
                throw new InvalidOperationException("USB PnP discovery returned an invalid list size.");
            var buffer = new char[checked((int)length)];
            result = api.GetList(buffer);
            if (result == BufferSmall && attempt < 2) continue;
            if (result != Success) throw Failure("list", result);

            var nodes = new List<UsbPnpNode>();
            foreach (var id in ParseMultiSz(buffer).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!UsbDisplayCatalog.TryParseInstanceId(id, out var product, out _) ||
                    !include(product))
                    continue;
                // A device removed after enumeration is absent, not an available
                // transport. Only matching families incur per-node metadata reads.
                UsbPnpNode? node;
                try { node = api.ReadNode(id); }
                catch (Exception error) when (onReadFailure is not null &&
                    (error is InvalidOperationException or System.ComponentModel.Win32Exception or
                        DllNotFoundException or EntryPointNotFoundException))
                {
                    // Only callers opting into partial snapshots isolate native
                    // per-node errors. Existing strict callers still fail.
                    onReadFailure(id);
                    continue;
                }
                if (node is not null)
                {
                    if (!id.Equals(node.InstanceId, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("USB PnP discovery returned inconsistent node metadata.");
                    nodes.Add(node);
                }
            }
            return nodes;
        }
        throw new InvalidOperationException("USB PnP discovery did not produce a stable list.");
    }

    internal static IReadOnlyList<UsbDisplayObservation> BuildObservations(IEnumerable<UsbPnpNode> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var nodes = source.Where(node =>
                UsbDisplayCatalog.TryParseInstanceId(node.InstanceId, out var product, out _) &&
                UsbDisplayCatalog.Find(product) is not null)
            .GroupBy(node => node.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var grouped = new Dictionary<string, List<UsbPnpNode>>(StringComparer.OrdinalIgnoreCase);
        var incomplete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, copies) in nodes)
        {
            UsbDisplayCatalog.TryParseInstanceId(id, out var product, out var isFunction);
            var key = id;
            if (isFunction)
            {
                var parents = copies.Select(node => node.ParentInstanceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // Group a function only through an observed, matching, non-MI
                // parent. Neither equal VID/PID nor similar serial text suffices.
                if (parents is [not null] && nodes.ContainsKey(parents[0]!) &&
                    UsbDisplayCatalog.TryParseInstanceId(parents[0], out var parentProduct, out var parentFunction) &&
                    parentProduct == product && !parentFunction)
                    key = parents[0]!;
                else
                    incomplete.Add(key);
            }
            if (!grouped.TryGetValue(key, out var members)) grouped.Add(key, members = []);
            members.AddRange(copies);
        }

        return grouped.Select(pair =>
        {
            var members = pair.Value;
            UsbDisplayCatalog.TryParseInstanceId(pair.Key, out var product, out _);
            var ambiguous = members.GroupBy(node => node.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Select(node => (
                    Parent: node.ParentInstanceId?.ToUpperInvariant(),
                    Driver: node.DriverService?.ToUpperInvariant(), node.SetupClass,
                    node.ProblemCode, node.MetadataAvailable)).Distinct().Count() > 1);
            var status = ambiguous ? UsbDisplayObservationStatus.Ambiguous :
                incomplete.Contains(pair.Key) || members.Any(node => !node.MetadataAvailable)
                    ? UsbDisplayObservationStatus.MetadataUnavailable : UsbDisplayObservationStatus.Recognized;
            return new UsbDisplayObservation(CreateObservationId(pair.Key), UsbDisplayCatalog.Find(product)!, status,
                members.Select(node => node.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Array.AsReadOnly(members.Select(node => node.DriverService).OfType<string>()
                    .Select(value => value.ToUpperInvariant()).Distinct().Order().ToArray()),
                Array.AsReadOnly(members.Select(node => node.SetupClass).OfType<Guid>().Distinct().Order().ToArray()),
                Array.AsReadOnly(members.Select(node => node.ProblemCode).OfType<uint>().Distinct().Order().ToArray()))
            {
                NodeKeys = Array.AsReadOnly(members.Select(node => CreateObservationId(node.InstanceId))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            };
        }).OrderBy(observation => observation.ObservationId, StringComparer.Ordinal).ToArray();
    }

    internal static string CreateObservationId(string instanceId) => "usb-pnp:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId.ToUpperInvariant()))).ToLowerInvariant();

    internal static IReadOnlyList<string> ParseMultiSz(char[] buffer)
    {
        var values = new List<string>();
        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0') continue;
            if (i == start)
            {
                // An empty set may contain just its terminating NUL. A
                // nonempty set still requires the final extra terminator.
                if (i == 0 && buffer.Length > 1 && buffer[1] != '\0') break;
                return values;
            }
            values.Add(new string(buffer, start, i - start));
            start = i + 1;
        }
        throw new InvalidOperationException("USB PnP discovery returned an unterminated list.");
    }

    private static InvalidOperationException Failure(string operation, uint result) =>
        new($"USB PnP discovery {operation} failed: 0x{result:X8}.");
}
