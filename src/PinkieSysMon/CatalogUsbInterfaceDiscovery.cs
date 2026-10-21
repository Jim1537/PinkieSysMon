using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

// Reuses the existing passive Windows/PnP metadata API. This interface
// deliberately has no transport-open, descriptor-probe or USB-write method.
internal interface IUsbInterfaceDiscoveryApi : IUsbPnpDiscoveryApi
{
    IReadOnlyList<Guid> ReadInterfaceClasses(string instanceId);
    IReadOnlyList<string> EnumerateInterfaces(Guid interfaceClass, string instanceId);
    string? ReadInterfaceInstanceId(string interfacePath);
}

// A catalog-matched, passive candidate is not a configured Output or
// permission to send a frame. Never expose the raw native path in ToString.
internal sealed class CatalogUsbInterfaceCandidate(
    string id, UsbDisplayFamily family, string interfacePath, string pnpNodeKey)
{
    internal string Id { get; } = id;
    internal UsbDisplayFamily Family { get; } = family;
    internal string InterfacePath { get; } = interfacePath;
    internal string PnpNodeKey { get; } = pnpNodeKey;
    internal string? SuggestedTransport => Family.CandidateTransport;

    public override string ToString() => $"{Family.Name} [{Id[^8..]}]";
}

internal static class CatalogUsbInterfaceDiscovery
{
    // Production entry point: passive Windows PnP and registered-interface
    // queries only. No USB handle is opened by this discovery API.
    internal static IReadOnlyList<CatalogUsbInterfaceCandidate> Capture() =>
        Capture(new WindowsTurzxInterfaceDiscoveryApi());

    // No generic USB scan and no arbitrary WinUSB picker: only exact catalog
    // VID/PID products are eligible even for passive candidate enumeration.
    internal static IReadOnlyList<CatalogUsbInterfaceCandidate> Capture(
        IUsbInterfaceDiscoveryApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var candidates = new List<CatalogUsbInterfaceCandidate>();
        var nodes = UsbDisplayDiscovery.CaptureNodes(api,
            product => UsbDisplayCatalog.Find(product) is not null);
        foreach (var node in nodes)
        {
            if (!TryEligibleNode(node, out var product))
                continue;

            var registered = api.ReadInterfaceClasses(node.InstanceId);
            if (registered.Count > 16 || registered.Any(guid => guid == Guid.Empty))
                throw new InvalidOperationException("USB candidate interface classes are invalid.");
            foreach (var interfaceClass in registered.Distinct())
            {
                foreach (var path in api.EnumerateInterfaces(interfaceClass, node.InstanceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryParseExactInterface(path, interfaceClass, out var pathProduct) ||
                        pathProduct != product ||
                        !node.InstanceId.Equals(api.ReadInterfaceInstanceId(path),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    // A PnP removal or driver change during enumeration cannot
                    // promote stale data to an openable candidate.
                    if (api.ReadNode(node.InstanceId) is not { } current ||
                        !TryEligibleNode(current, out var currentProduct) ||
                        currentProduct != product ||
                        !node.InstanceId.Equals(current.InstanceId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    candidates.Add(new CatalogUsbInterfaceCandidate(
                        StableInterfaceId(path), UsbDisplayCatalog.Find(product)!, path,
                        UsbDisplayDiscovery.CreateObservationId(node.InstanceId)));
                }
            }
        }

        // One node may advertise several distinct interfaces. Preserve each
        // for later explicit ambiguity handling; never choose a first match.
        return candidates
            .DistinctBy(candidate => candidate.Id)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryEligibleNode(UsbPnpNode node, out UsbProductId product)
    {
        product = default;
        return UsbDisplayCatalog.TryParseInstanceId(node.InstanceId, out product,
                   out var isFunction) &&
               !isFunction &&
               UsbDisplayCatalog.Find(product) is not null &&
               node.MetadataAvailable && node.ProblemCode == 0 &&
               string.Equals(node.DriverService, "WINUSB", StringComparison.OrdinalIgnoreCase);
    }

    // A registered class AND independent exact devnode correlation are
    // required. A VID/PID substring, arbitrary HID path or compatible brand
    // name is never enough.
    internal static bool TryParseExactInterface(string? path, Guid registeredClass,
        out UsbProductId product)
    {
        product = default;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 ||
            path.Any(char.IsControl) || registeredClass == Guid.Empty)
            return false;

        var parts = path.Split('#');
        if (parts.Length != 4 ||
            !parts[0].Equals(@"\\?\usb", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(parts[3], out var actualClass) ||
            actualClass != registeredClass ||
            parts[2].Length == 0 || parts[2].Any(ch => ch is '\\' or '/' or '#'))
            return false;

        var pair = parts[1].Split('&');
        if (pair.Length != 2 ||
            pair[0].Length != 8 || pair[1].Length != 8 ||
            !pair[0].StartsWith("vid_", StringComparison.OrdinalIgnoreCase) ||
            !pair[1].StartsWith("pid_", StringComparison.OrdinalIgnoreCase) ||
            !ushort.TryParse(pair[0].AsSpan(4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var vid) ||
            !ushort.TryParse(pair[1].AsSpan(4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var pid))
            return false;

        product = new UsbProductId(vid, pid);
        return UsbDisplayCatalog.Find(product) is not null;
    }

    internal static string StableInterfaceId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "usb-interface:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).ToLowerInvariant();
    }
}

internal enum CatalogUsbCandidateResolutionStatus
{
    Found,
    Missing,
    Ambiguous
}

internal readonly record struct CatalogUsbCandidateResolution(
    CatalogUsbCandidateResolutionStatus Status, CatalogUsbInterfaceCandidate? Candidate);

// Protocol choice, USB session acquisition and persistence are independent.
// Ignoring a device excludes its physical node, not merely one interface.
// Always resolve against the COMPLETE passive snapshot, not a visible subset.
internal static class CatalogUsbCandidateSelection
{
    internal static IReadOnlyList<CatalogUsbInterfaceCandidate> Available(
        IReadOnlyList<CatalogUsbInterfaceCandidate> candidates,
        IReadOnlySet<string> ignoredDeviceNodeKeys)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ignoredDeviceNodeKeys);
        return candidates.Where(candidate => !IsIgnored(ignoredDeviceNodeKeys, candidate.PnpNodeKey)).ToArray();
    }

    internal static CatalogUsbCandidateResolution Resolve(
        IReadOnlyList<CatalogUsbInterfaceCandidate> candidates,
        string? selectedInterfaceId,
        IReadOnlySet<string> ignoredDeviceNodeKeys)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ignoredDeviceNodeKeys);

        if (string.IsNullOrWhiteSpace(selectedInterfaceId))
            return new(CatalogUsbCandidateResolutionStatus.Missing, null);

        var matches = candidates.Where(candidate => candidate.Id.Equals(
            selectedInterfaceId, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length == 0)
            return new(CatalogUsbCandidateResolutionStatus.Missing, null);
        if (matches.Length > 1)
            return new(CatalogUsbCandidateResolutionStatus.Ambiguous, null);

        var selected = matches[0];
        if (IsIgnored(ignoredDeviceNodeKeys, selected.PnpNodeKey))
            return new(CatalogUsbCandidateResolutionStatus.Missing, null);
        // One PnP node advertising multiple distinct WinUSB interfaces is not
        // silently resolved by selecting the first or ignoring its sibling.
        if (candidates.Count(candidate => candidate.PnpNodeKey.Equals(
                selected.PnpNodeKey, StringComparison.OrdinalIgnoreCase)) != 1)
            return new(CatalogUsbCandidateResolutionStatus.Ambiguous, null);

        return new(CatalogUsbCandidateResolutionStatus.Found, selected);
    }

    private static bool IsIgnored(IReadOnlySet<string> ignored, string id) =>
        ignored.Contains(id) || ignored.Any(other =>
            other.Equals(id, StringComparison.OrdinalIgnoreCase));
}
