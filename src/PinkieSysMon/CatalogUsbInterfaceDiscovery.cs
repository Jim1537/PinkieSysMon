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
    string? ReadPhysicalFingerprint(string instanceId) => null;
    string? ReadDisplayName(string instanceId) => null;
}

// A catalog-matched, passive candidate is not a configured Output or
// permission to send a frame. Never expose the raw native path in ToString.
internal sealed class CatalogUsbInterfaceCandidate(
    string id, UsbDisplayFamily family, string interfacePath, string pnpNodeKey, string? displayName = null)
{
    internal string Id { get; } = id;
    internal string DisplayName { get; } = string.IsNullOrWhiteSpace(displayName) ? "USB display" : displayName;
    internal UsbDisplayFamily Family { get; } = family;
    internal string InterfacePath { get; } = interfacePath;
    internal string PnpNodeKey { get; } = pnpNodeKey;
    internal string? PhysicalFingerprint { get; init; }
    internal string? SuggestedTransport => Family.CandidateTransport;

    public override string ToString() => $"{Family.Name} [{Id[^8..]}]";
}

internal sealed record CatalogUsbCandidateSnapshot(
    IReadOnlyList<CatalogUsbInterfaceCandidate> Candidates,
    IReadOnlyList<string> UnavailableNodeKeys);

internal static class CatalogUsbInterfaceDiscovery
{
    // Passive partial-result entry point. Global PnP list failure still throws.
    internal static CatalogUsbCandidateSnapshot CaptureSnapshot() =>
        CaptureSnapshot(new WindowsTurzxInterfaceDiscoveryApi());

    internal static IReadOnlyList<CatalogUsbInterfaceCandidate> Capture() =>
        CaptureSnapshot().Candidates;

    internal static IReadOnlyList<CatalogUsbInterfaceCandidate> Capture(
        IUsbInterfaceDiscoveryApi api) => CaptureSnapshot(api).Candidates;

    internal static CatalogUsbCandidateSnapshot CaptureSnapshot(IUsbInterfaceDiscoveryApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var candidates = new List<CatalogUsbInterfaceCandidate>();
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedFunctions = new HashSet<UsbProductId>();
        var nodes = UsbDisplayDiscovery.CaptureNodes(api,
            product => UsbDisplayCatalog.Find(product) is not null,
            failedId =>
            {
                unavailable.Add(UsbDisplayDiscovery.CreateObservationId(failedId));
                if (UsbDisplayCatalog.TryParseInstanceId(failedId, out var product, out var function) && function)
                    unresolvedFunctions.Add(product);
            });
        foreach (var node in nodes)
        {
            if (!TryEligibleNode(node, out var product))
            {
                // A healthy composite parent is represented by its WinUSB
                // function; USBCCGP on that parent is not a metadata failure.
                var representedParent = node.MetadataAvailable && node.ProblemCode == 0 &&
                    nodes.Any(child => string.Equals(child.ParentInstanceId, node.InstanceId,
                        StringComparison.OrdinalIgnoreCase) && TryEligibleNode(child, out var childProduct) &&
                        childProduct == product);
                if (!representedParent)
                    unavailable.Add(UsbDisplayDiscovery.CreateObservationId(node.InstanceId));
                if ((!node.MetadataAvailable || node.ProblemCode != 0) && node.ParentInstanceId is { } parentId &&
                    nodes.Any(parent => parent.InstanceId.Equals(parentId, StringComparison.OrdinalIgnoreCase) &&
                        UsbDisplayCatalog.TryParseInstanceId(parent.InstanceId, out var parentProduct, out var function) &&
                        parentProduct == product && !function))
                    unavailable.Add(UsbDisplayDiscovery.CreateObservationId(parentId));
                continue;
            }

            // A WinUSB function of a composite device belongs to its actually
            // observed parent, never another unit with an equal VID/PID. The
            // parent can legitimately use USBCCGP instead of WinUSB.
            var isFunction = UsbDisplayCatalog.TryParseInstanceId(
                node.InstanceId, out _, out var parsedFunction) && parsedFunction;
            var physicalId = node.InstanceId;
            if (isFunction)
            {
                var parent = nodes.Where(other =>
                    node.ParentInstanceId is not null &&
                    node.ParentInstanceId.Equals(other.InstanceId, StringComparison.OrdinalIgnoreCase) &&
                    UsbDisplayCatalog.TryParseInstanceId(other.InstanceId,
                        out var parentProduct, out var parentIsFunction) &&
                    parentProduct == product && !parentIsFunction &&
                    other.MetadataAvailable && other.ProblemCode == 0).ToArray();
                if (parent.Length != 1)
                {
                    unavailable.Add(UsbDisplayDiscovery.CreateObservationId(node.InstanceId));
                    continue;
                }
                physicalId = parent[0].InstanceId;
            }

            // A failed function read has no trustworthy parent relation. Until
            // that metadata is available, another same-product unit cannot be
            // proved unrelated; never infer a unique interface by omission.
            if (unresolvedFunctions.Contains(product))
            {
                unavailable.Add(UsbDisplayDiscovery.CreateObservationId(physicalId));
                continue;
            }

            // Only publish a node after all of its interfaces are enumerated:
            // partial enumeration could falsely appear uniquely bindable.
            var perNode = new List<CatalogUsbInterfaceCandidate>();
            try
            {
                var registered = api.ReadInterfaceClasses(node.InstanceId);
                if (registered.Count > 16 || registered.Any(guid => guid == Guid.Empty))
                    throw new InvalidOperationException("USB candidate interface classes are invalid.");
                foreach (var interfaceClass in registered.Distinct())
                foreach (var path in api.EnumerateInterfaces(interfaceClass, node.InstanceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryParseExactInterface(path, interfaceClass, out var pathProduct,
                            out var pathInterfaceNumber) ||
                        pathProduct != product ||
                        isFunction != pathInterfaceNumber.HasValue ||
                        (isFunction && pathInterfaceNumber != DeviceInterfaceNumber(node.InstanceId)) ||
                        !node.InstanceId.Equals(api.ReadInterfaceInstanceId(path),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (api.ReadNode(node.InstanceId) is not { } current ||
                        !TryEligibleNode(current, out var currentProduct) ||
                        currentProduct != product ||
                        !node.InstanceId.Equals(current.InstanceId,
                            StringComparison.OrdinalIgnoreCase) ||
                        isFunction && (!string.Equals(current.ParentInstanceId, physicalId, StringComparison.OrdinalIgnoreCase) ||
                            api.ReadNode(physicalId) is not { } currentParent ||
                            !string.Equals(currentParent.InstanceId, physicalId, StringComparison.OrdinalIgnoreCase) ||
                            !currentParent.MetadataAvailable || currentParent.ProblemCode != 0))
                        throw new InvalidOperationException(
                            "USB candidate changed during interface enumeration.");

                    perNode.Add(new CatalogUsbInterfaceCandidate(
                        StableInterfaceId(path), UsbDisplayCatalog.Find(product)!, path,
                        UsbDisplayDiscovery.CreateObservationId(physicalId), api.ReadDisplayName(physicalId))
                        { PhysicalFingerprint = api.ReadPhysicalFingerprint(physicalId) });
                }
            }
            catch (Exception error) when (error is InvalidOperationException or
                System.ComponentModel.Win32Exception or DllNotFoundException or
                EntryPointNotFoundException)
            {
                unavailable.Add(UsbDisplayDiscovery.CreateObservationId(physicalId));
                continue;
            }
            if (perNode.Count == 0)
                unavailable.Add(UsbDisplayDiscovery.CreateObservationId(physicalId));
            candidates.AddRange(perNode);
        }
        // Multiple functions share one physical unit. A failure in any of its
        // WinUSB functions invalidates provisional interfaces from its siblings.
        foreach (var claims in candidates.GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
            if (claims.Select(candidate => candidate.PnpNodeKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                foreach (var claim in claims) unavailable.Add(claim.PnpNodeKey);
        return new CatalogUsbCandidateSnapshot(
            candidates.Where(candidate => !unavailable.Contains(candidate.PnpNodeKey))
                .DistinctBy(candidate => candidate.Id)
                .OrderBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray(),
            unavailable.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    private static bool TryEligibleNode(UsbPnpNode node, out UsbProductId product)
    {
        product = default;
        return UsbDisplayCatalog.TryParseInstanceId(node.InstanceId, out product,
                   out _) &&
               UsbDisplayCatalog.Find(product) is not null &&
               node.MetadataAvailable && node.ProblemCode == 0 &&
               string.Equals(node.DriverService, "WINUSB", StringComparison.OrdinalIgnoreCase);
    }

    // A registered class AND independent exact devnode correlation are
    // required. A VID/PID substring, arbitrary HID path or compatible brand
    // name is never enough.
    internal static bool TryParseExactInterface(string? path, Guid registeredClass,
        out UsbProductId product) =>
        TryParseExactInterface(path, registeredClass, out product, out _);

    internal static bool TryParseExactInterface(string? path, Guid registeredClass,
        out UsbProductId product, out byte? interfaceNumber)
    {
        product = default;
        interfaceNumber = null;
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
        if (pair.Length is not (2 or 3) ||
            pair[0].Length != 8 || pair[1].Length != 8 ||
            !pair[0].StartsWith("vid_", StringComparison.OrdinalIgnoreCase) ||
            !pair[1].StartsWith("pid_", StringComparison.OrdinalIgnoreCase) ||
            !ushort.TryParse(pair[0].AsSpan(4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var vid) ||
            !ushort.TryParse(pair[1].AsSpan(4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var pid))
            return false;

        if (pair.Length == 3)
        {
            if (pair[2].Length != 5 ||
                !pair[2].StartsWith("mi_", StringComparison.OrdinalIgnoreCase) ||
                !byte.TryParse(pair[2].AsSpan(3), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var value))
                return false;
            interfaceNumber = value;
        }

        product = new UsbProductId(vid, pid);
        return UsbDisplayCatalog.Find(product) is not null;
    }

    private static byte? DeviceInterfaceNumber(string instanceId)
    {
        // Input has already passed the strict USB devnode parser.
        var tokens = instanceId.Split('\\')[1].Split('&');
        var token = tokens.FirstOrDefault(part =>
            part.StartsWith("MI_", StringComparison.OrdinalIgnoreCase));
        return token is not null &&
               byte.TryParse(token.AsSpan(3), NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    internal static string StableInterfaceId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "usb-interface:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(path.Trim().ToUpperInvariant()))).ToLowerInvariant();
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
                selected.PnpNodeKey, StringComparison.OrdinalIgnoreCase)) != 1 ||
            selected.PhysicalFingerprint is { } fingerprint && candidates.Count(candidate =>
                string.Equals(candidate.PhysicalFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) != 1)
            return new(CatalogUsbCandidateResolutionStatus.Ambiguous, null);

        return new(CatalogUsbCandidateResolutionStatus.Found, selected);
    }

    private static bool IsIgnored(IReadOnlySet<string> ignored, string id) =>
        ignored.Contains(id) || ignored.Any(other =>
            other.Equals(id, StringComparison.OrdinalIgnoreCase));
}
