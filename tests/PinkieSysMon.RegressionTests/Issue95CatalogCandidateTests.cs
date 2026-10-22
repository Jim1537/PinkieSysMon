namespace PinkieSysMon.RegressionTests;

// Synthetic Configuration Manager metadata only; no device open or USB I/O.
internal static class Issue95CatalogCandidateTests
{
    private static readonly Guid ClassA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClassB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static void CatalogBoundedPassiveCandidates()
    {
        var known = NodeId("0416:5409", "ONE");
        var unknown = NodeId("0416:8001", "FOREIGN");
        var vendorUnknown = NodeId("1CBE:9999", "FOREIGN");
        var hid = NodeId("1CBE:0123", "HID");
        var api = new FakeApi([known, unknown, vendorUnknown, hid]);
        api.Nodes[hid] = Node(hid, "HIDUSB");
        api.Classes[known] = [ClassA];
        api.Paths[known] = [Path("0416:5409", "ONE", ClassA)];

        var candidates = CatalogUsbInterfaceDiscovery.Capture(api);
        Check(candidates.Count == 1 && candidates[0].Family.Product ==
              new UsbProductId(0x0416, 0x5409) &&
              candidates[0].SuggestedTransport is null,
            "A listed unknown commercial model is offered without invented protocol hints.");
        Check(!api.ReadCalls.Contains(unknown) && !api.ReadCalls.Contains(vendorUnknown) &&
              api.ReadCalls.Contains(known) && api.ReadCalls.Contains(hid),
            "Unlisted VID/PID pairs must be excluded before native per-node reads.");
        Check(api.ClassesRead.SequenceEqual([known]),
            "Non-WinUSB devices must not have interface registries or paths queried.");
        Check(candidates[0].Id.StartsWith("usb-interface:", StringComparison.Ordinal) &&
              candidates[0].Id == CatalogUsbInterfaceDiscovery.StableInterfaceId(
                  api.Paths[known][0]) &&
              !candidates[0].ToString().Contains("ONE", StringComparison.Ordinal) &&
              !candidates[0].ToString().Contains(@"\\?\usb", StringComparison.Ordinal),
            "Candidate IDs must be exact-interface hashes, not path text or model bindings.");
        // Include isolated-native-error cases in this existing registered test.
        PartialFailureIsolation();
    }

    public static void ExactCorrelationAndAmbiguity()
    {
        var id = NodeId("1CBE:0123", "TWO");
        var goodA = Path("1CBE:0123", "TWO", ClassA);
        var goodB = Path("1CBE:0123", "TWO", ClassB);
        var api = new FakeApi([id]);
        api.Classes[id] = [ClassA, ClassB];
        api.Paths[id] = [goodA, goodA.ToUpperInvariant(), goodB,
            Path("0416:5408", "OTHER", ClassA),
            Path("1CBE:0123", "TWO", ClassB).Replace(ClassB.ToString(), ClassA.ToString())];
        var candidates = CatalogUsbInterfaceDiscovery.Capture(api);
        Check(candidates.Count == 2 && candidates[0].Id != candidates[1].Id &&
              candidates[0].PnpNodeKey == candidates[1].PnpNodeKey &&
              candidates.All(c => c.SuggestedTransport == OutputTargetContract.EncryptedJpegTransport),
            "Two interfaces on one known unit remain separate and ambiguous, never first-selected.");

        api.InterfaceInstanceId = _ => NodeId("1CBE:0123", "ANOTHER");
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 0,
            "Even a matching product/class path must be rejected without exact node correlation.");
        api.InterfaceInstanceId = _ => id;
        var reads = 0;
        api.Read = key => ++reads == 1 ? Node(key) : null;
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 0 && reads >= 2,
            "An interface disappearing after the first snapshot cannot be proposed.");
        reads = 0;
        api.Read = key => ++reads == 1 ? Node(key) : Node(key, "HIDUSB");
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 0 && reads >= 2,
            "A driver changing after initial enumeration cannot authorize stale WinUSB I/O.");
    }

    public static void StrictPathAndMissingDriver()
    {
        var valid = Path("0416:5408", "THREE", ClassA);
        Check(CatalogUsbInterfaceDiscovery.TryParseExactInterface(valid, ClassA, out var product) &&
              product == new UsbProductId(0x0416, 0x5408),
            "A registered and catalog-listed interface is parsed exactly.");
        foreach (var invalid in new[]
        {
            valid.Replace("vid_0416", "vid_0417", StringComparison.OrdinalIgnoreCase),
            valid.Replace("usb#", "hid#", StringComparison.OrdinalIgnoreCase),
            valid.Replace(ClassA.ToString(), ClassB.ToString()),
            Path("0416:8001", "THREE", ClassA),
            Path("1CBE:9999", "THREE", ClassA),
            valid + "#extra",
            valid.Replace("THREE", "BAD/PORT"),
            valid.Replace("pid_5408", "pid_5408&mi_0G")
        })
            Check(!CatalogUsbInterfaceDiscovery.TryParseExactInterface(
                invalid, ClassA, out _),
                "Malformed, unlisted, composite-unknown or wrong-class interfaces are excluded.");

        var id = NodeId("0416:5408", "THREE");
        var api = new FakeApi([id]);
        api.Nodes[id] = Node(id, "USBSTOR");
        api.Classes[id] = [ClassA];
        api.Paths[id] = [valid];
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 0 &&
              api.ClassesRead.Count == 0,
            "A listed storage-driver device cannot be offered for active WinUSB test.");
        CompositeFunctionCorrelation();
    }


    public static void ExactSelectionAndPerInstanceIgnore()
    {
        var firstNode = NodeId("0416:5408", "FIRST");
        var secondNode = NodeId("0416:5408", "SECOND");
        var api = new FakeApi([secondNode, firstNode]);
        api.Classes[firstNode] = [ClassA];
        api.Classes[secondNode] = [ClassA];
        api.Paths[firstNode] = [Path("0416:5408", "FIRST", ClassA)];
        api.Paths[secondNode] = [Path("0416:5408", "SECOND", ClassA)];

        var discovered = CatalogUsbInterfaceDiscovery.Capture(api);
        Check(discovered.Count == 2 && discovered[0].Id != discovered[1].Id &&
              discovered[0].PnpNodeKey != discovered[1].PnpNodeKey,
            "Equal VID/PID must not merge distinct physical units.");

        var ignored = new HashSet<string>();
        var selectedId = CatalogUsbInterfaceDiscovery.StableInterfaceId(
            Path("0416:5408", "SECOND", ClassA));
        var selected = CatalogUsbCandidateSelection.Resolve(discovered, selectedId, ignored);
        Check(selected.Status == CatalogUsbCandidateResolutionStatus.Found &&
              selected.Candidate?.Id == selectedId,
            "Exact binding, not enumeration order, selects the physical candidate.");
        Check(CatalogUsbCandidateSelection.Resolve(
                  discovered, "usb-interface:" + new string('0', 64), ignored).Status ==
              CatalogUsbCandidateResolutionStatus.Missing,
            "Absent exact binding cannot silently fall back to a same-product sibling.");

        ignored.Add(selected.Candidate!.PnpNodeKey.ToUpperInvariant());
        var available = CatalogUsbCandidateSelection.Available(discovered, ignored);
        Check(available.Count == 1 && available[0].Id != selectedId &&
              CatalogUsbCandidateSelection.Resolve(discovered, selectedId, ignored).Status ==
              CatalogUsbCandidateResolutionStatus.Missing &&
              CatalogUsbCandidateSelection.Resolve(discovered, available[0].Id, ignored).Status ==
              CatalogUsbCandidateResolutionStatus.Found,
            "Case-insensitive physical-instance Forget must not hide other units with the same VID/PID.");
    }

    public static void MultiInterfaceSelectionFailsClosed()
    {
        var node = NodeId("1CBE:0123", "MULTI");
        var api = new FakeApi([node]);
        api.Classes[node] = [ClassA, ClassB];
        api.Paths[node] = [Path("1CBE:0123", "MULTI", ClassA),
                           Path("1CBE:0123", "MULTI", ClassB)];
        var interfaces = CatalogUsbInterfaceDiscovery.Capture(api);
        var ignored = new HashSet<string>();
        Check(interfaces.Count == 2 &&
              interfaces.All(candidate => CatalogUsbCandidateSelection.Resolve(
                  interfaces, candidate.Id, ignored).Status ==
                  CatalogUsbCandidateResolutionStatus.Ambiguous),
            "Even an explicit interface ID cannot bypass ambiguous physical node identity.");

        ignored.Add(interfaces[0].PnpNodeKey);
        Check(CatalogUsbCandidateSelection.Available(interfaces, ignored).Count == 0 &&
              CatalogUsbCandidateSelection.Resolve(
                  interfaces, interfaces[1].Id, ignored).Status ==
              CatalogUsbCandidateResolutionStatus.Missing,
            "Forgetting the physical unit hides all its registered interfaces.");

        var duplicates = new[] { interfaces[1], interfaces[1] };
        Check(CatalogUsbCandidateSelection.Resolve(
                  duplicates, interfaces[1].Id, new HashSet<string>()).Status ==
              CatalogUsbCandidateResolutionStatus.Ambiguous,
            "Duplicate candidate identities must never be selected by first match.");
    }


    public static void PartialFailureIsolation()
    {
        var good = NodeId("0416:5409", "GOOD");
        var failed = NodeId("1CBE:0123", "FAIL");
        var incomplete = NodeId("0416:5408", "INCOMPLETE");
        var api = new FakeApi([good, failed, incomplete]);
        api.Classes[good] = [ClassA];
        api.Paths[good] = [Path("0416:5409", "GOOD", ClassA)];
        api.Classes[failed] = [ClassA];
        api.Classes[incomplete] = [ClassA, ClassB];
        api.Paths[incomplete] = [Path("0416:5408", "INCOMPLETE", ClassA)];
        api.FailedClassNode = failed;
        api.FailedEnumeratedClass = ClassB;

        var snapshot = CatalogUsbInterfaceDiscovery.CaptureSnapshot(api);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            UsbDisplayDiscovery.CreateObservationId(failed),
            UsbDisplayDiscovery.CreateObservationId(incomplete)
        };
        Check(snapshot.Candidates.Count == 1 &&
              snapshot.Candidates[0].Family.Product == new UsbProductId(0x0416, 0x5409) &&
              snapshot.UnavailableNodeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                  .SetEquals(expected),
            "Partial failures must preserve healthy catalog units without publishing incomplete candidates.");
        Check(snapshot.UnavailableNodeKeys.All(key => key.StartsWith("usb-pnp:", StringComparison.Ordinal)
                  && !key.Contains("INCOMPLETE", StringComparison.OrdinalIgnoreCase)),
            "Only hashed private node identities may be exposed as unavailable.");
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 1,
            "Candidate-only enumeration must preserve independent healthy units.");

        // A node that disappears after its first valid interface must not
        // leave that one interface spuriously testable as an unambiguous device.
        var unstable = NodeId("0416:5408", "UNSTABLE");
        var racing = new FakeApi([good, unstable]);
        racing.Classes[good] = [ClassA];
        racing.Paths[good] = [Path("0416:5409", "GOOD", ClassA)];
        racing.Classes[unstable] = [ClassA, ClassB];
        racing.Paths[unstable] = [Path("0416:5408", "UNSTABLE", ClassA),
                                   Path("0416:5408", "UNSTABLE", ClassB)];
        var reads = 0;
        racing.Read = id => id.Equals(unstable, StringComparison.OrdinalIgnoreCase)
            ? ++reads < 3 ? Node(id) : null : Node(id);
        var removed = CatalogUsbInterfaceDiscovery.CaptureSnapshot(racing);
        Check(reads >= 3 && removed.Candidates.Count == 1 &&
              removed.Candidates[0].Family.Product == new UsbProductId(0x0416, 0x5409) &&
              removed.UnavailableNodeKeys.Contains(
                  UsbDisplayDiscovery.CreateObservationId(unstable)),
            "Removal during multi-interface discovery must discard the entire unstable node.");

        reads = 0;
        racing.Read = id => id.Equals(unstable, StringComparison.OrdinalIgnoreCase)
            ? ++reads < 3 ? Node(id) : Node(id, "HIDUSB") : Node(id);
        var changed = CatalogUsbInterfaceDiscovery.CaptureSnapshot(racing);
        Check(reads >= 3 && changed.Candidates.Count == 1 &&
              changed.UnavailableNodeKeys.Contains(
                  UsbDisplayDiscovery.CreateObservationId(unstable)),
            "Driver changes during enumeration must discard all earlier interfaces.");

        // The initial PnP metadata read may fail for one admitted node.
        // Catalog capture isolates it; all existing strict callers retain
        // their original fail-fast behavior with no missing-device guess.
        var initialFailure = NodeId("1CBE:0123", "INITIAL-ERROR");
        var initialGood = NodeId("0416:5409", "INITIAL-GOOD");
        var initial = new FakeApi([initialFailure, initialGood]);
        initial.Classes[initialGood] = [ClassA];
        initial.Paths[initialGood] = [Path("0416:5409", "INITIAL-GOOD", ClassA)];
        initial.Read = id => id.Equals(initialFailure, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidOperationException("Synthetic native PnP metadata failure.")
            : Node(id);
        var partial = CatalogUsbInterfaceDiscovery.CaptureSnapshot(initial);
        Check(partial.Candidates.Count == 1 &&
              partial.Candidates[0].Family.Product == new UsbProductId(0x0416, 0x5409) &&
              partial.UnavailableNodeKeys.SequenceEqual(
                  [UsbDisplayDiscovery.CreateObservationId(initialFailure)]),
            "Initial per-node PnP failure must not hide healthy catalog candidates.");

        var strictFailure = false;
        try
        {
            _ = UsbDisplayDiscovery.CaptureNodes(initial,
                _ => true);
        }
        catch (InvalidOperationException) { strictFailure = true; }
        Check(strictFailure,
            "Legacy strict PnP discovery must still report native metadata errors.");

        api.FailListSize = true;
        var failedGlobal = false;
        try { CatalogUsbInterfaceDiscovery.CaptureSnapshot(api); }
        catch (InvalidOperationException) { failedGlobal = true; }
        Check(failedGlobal, "A failed PnP list must not be mistaken for an empty successful scan.");
    }


    // A parent may run USBCCGP while its exact WinUSB MI_XX child is the
    // interface-owning device. Physical identity belongs to the observed parent.
    public static void CompositeFunctionCorrelation()
    {
        var parent = NodeId("0416:5408", "PARENT-A");
        var child = FunctionId("0416:5408", "FUNCTION-A", 1);
        var otherParent = NodeId("0416:5408", "PARENT-B");
        var otherChild = FunctionId("0416:5408", "FUNCTION-B", 1);
        var api = new FakeApi([parent, child, otherParent, otherChild]);
        api.Nodes[parent] = Node(parent, "USBCCGP");
        api.Nodes[otherParent] = Node(otherParent, "USBCCGP");
        api.Nodes[child] = new UsbPnpNode(child, parent, "WINUSB", Guid.Empty, 0, true);
        api.Nodes[otherChild] = new UsbPnpNode(otherChild, otherParent, "WINUSB", Guid.Empty, 0, true);
        api.Classes[child] = [ClassA];
        api.Classes[otherChild] = [ClassA];
        var pathA = FunctionPath("0416:5408", "FUNCTION-A", 1, ClassA);
        var pathB = FunctionPath("0416:5408", "FUNCTION-B", 1, ClassA);
        api.Paths[child] = [pathA];
        api.Paths[otherChild] = [pathB];

        Check(CatalogUsbInterfaceDiscovery.TryParseExactInterface(pathA, ClassA,
                  out var product, out var parsedMi) &&
              product == new UsbProductId(0x0416, 0x5408) && parsedMi == 1,
            "An exact registered MI_XX interface may be correlated to its own function.");
        var snapshot = CatalogUsbInterfaceDiscovery.CaptureSnapshot(api);
        Check(snapshot.Candidates.Count == 2 && snapshot.UnavailableNodeKeys.Count == 0 &&
              snapshot.Candidates.Any(c => c.Id ==
                  CatalogUsbInterfaceDiscovery.StableInterfaceId(pathA) &&
                  c.PnpNodeKey == UsbDisplayDiscovery.CreateObservationId(parent)) &&
              snapshot.Candidates.Any(c => c.Id ==
                  CatalogUsbInterfaceDiscovery.StableInterfaceId(pathB) &&
                  c.PnpNodeKey == UsbDisplayDiscovery.CreateObservationId(otherParent)),
            "Two equal-product composite devices must retain separate physical parent identities.");
        foreach (var candidate in snapshot.Candidates)
        {
            Check(CatalogUsbCandidateSelection.Resolve(snapshot.Candidates, candidate.Id,
                      new HashSet<string>()).Status == CatalogUsbCandidateResolutionStatus.Found,
                "A unique MI interface must select its exact observed physical parent.");
        }
        var ignoreFirst = new HashSet<string> { UsbDisplayDiscovery.CreateObservationId(parent) };
        Check(CatalogUsbCandidateSelection.Available(snapshot.Candidates, ignoreFirst).Count == 1,
            "Forgetting one composite device must not exclude an equal-product sibling.");

        api.Paths[child] = [FunctionPath("0416:5408", "FUNCTION-A", 2, ClassA)];
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 1,
            "A registered MI_02 path cannot be assigned to the MI_01 devnode.");
        api.Paths[child] = [pathA];
        api.Nodes[child] = new UsbPnpNode(child, otherParent, "WINUSB", Guid.Empty, 0, true);
        var switchedParent = CatalogUsbInterfaceDiscovery.Capture(api);
        Check(switchedParent.Count == 2 &&
              switchedParent.Count(c => c.PnpNodeKey ==
                  UsbDisplayDiscovery.CreateObservationId(otherParent)) == 2 &&
              CatalogUsbCandidateSelection.Resolve(switchedParent,
                  CatalogUsbInterfaceDiscovery.StableInterfaceId(pathB),
                  new HashSet<string>()).Status == CatalogUsbCandidateResolutionStatus.Ambiguous,
            "Competing functions on one verified parent remain ambiguous, never first-selected.");

        api.Nodes[child] = new UsbPnpNode(child, "USB\\VID_0416&PID_5408\\MISSING-PARENT",
            "WINUSB", Guid.Empty, 0, true);
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 1,
            "An unobserved parent, even with matching VID/PID, cannot authorize an MI candidate.");
        api.Nodes[child] = new UsbPnpNode(child, parent, "HIDUSB", Guid.Empty, 0, true);
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Count == 1,
            "An MI function must have its own WinUSB driver, regardless of the parent.");
    }

    private static string FunctionId(string pair, string unit, byte mi) =>
        $"USB\\VID_{pair[..4]}&PID_{pair[5..]}&MI_{mi:X2}\\SYNTHETIC-{unit}";

    private static string FunctionPath(string pair, string unit, byte mi, Guid cls) =>
        $@"\\?\usb#vid_{pair[..4]}&pid_{pair[5..]}&mi_{mi:X2}#SYNTHETIC-{unit}#{{{cls}}}";

    private static string NodeId(string pair, string unit) =>
        $"USB\\VID_{pair[..4]}&PID_{pair[5..]}\\SYNTHETIC-{unit}";

    private static string Path(string pair, string unit, Guid cls) =>
        $@"\\?\usb#vid_{pair[..4]}&pid_{pair[5..]}#SYNTHETIC-{unit}#{{{cls}}}";

    private static UsbPnpNode Node(string id, string service = "WINUSB") =>
        new(id, null, service, Guid.Empty, 0, true);

    private sealed class FakeApi(string[] ids) : IUsbInterfaceDiscoveryApi
    {
        private readonly char[] _list = ids.Length == 0 ? ['\0', '\0'] :
            (string.Join('\0', ids) + "\0\0").ToCharArray();
        public Dictionary<string, UsbPnpNode> Nodes { get; } =
            ids.ToDictionary(id => id, id => Node(id), StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid[]> Classes { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Paths { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> ReadCalls { get; } = [];
        public List<string> ClassesRead { get; } = [];
        public string? FailedClassNode { get; set; }
        public Guid? FailedEnumeratedClass { get; set; }
        public bool FailListSize { get; set; }
        public Func<string, UsbPnpNode?>? Read { get; set; }
        public Func<string, string?>? InterfaceInstanceId { get; set; }

        public uint GetListSize(out uint characters)
        {
            characters = checked((uint)_list.Length);
            return FailListSize ? 0x0D : UsbDisplayDiscovery.Success;
        }

        public uint GetList(char[] buffer)
        {
            _list.CopyTo(buffer, 0);
            return UsbDisplayDiscovery.Success;
        }

        public UsbPnpNode? ReadNode(string id)
        {
            ReadCalls.Add(id);
            return Read?.Invoke(id) ?? (Read is null && Nodes.TryGetValue(id, out var node) ? node : null);
        }

        public IReadOnlyList<Guid> ReadInterfaceClasses(string id)
        {
            ClassesRead.Add(id);
            if (id.Equals(FailedClassNode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Synthetic class metadata error.");
            return Classes.GetValueOrDefault(id) ?? [];
        }

        public IReadOnlyList<string> EnumerateInterfaces(Guid cls, string id)
        {
            if (cls == FailedEnumeratedClass)
                throw new InvalidOperationException("Synthetic interface enumeration error.");
            return Paths.GetValueOrDefault(id) ?? [];
        }

        public string? ReadInterfaceInstanceId(string path) =>
            InterfaceInstanceId?.Invoke(path) ??
            Nodes.Keys.SingleOrDefault(key => path.Contains(
                key.Split('\\')[^1], StringComparison.OrdinalIgnoreCase));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
