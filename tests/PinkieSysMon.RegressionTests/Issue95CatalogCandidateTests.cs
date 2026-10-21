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
        Check(candidates.Length == 1 && candidates[0].Family.Product ==
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
        Check(candidates.Length == 2 && candidates[0].Id != candidates[1].Id &&
              candidates[0].PnpNodeKey == candidates[1].PnpNodeKey &&
              candidates.All(c => c.SuggestedTransport == OutputTargetContract.TurzxTransport),
            "Two interfaces on one known unit remain separate and ambiguous, never first-selected.");

        api.InterfaceInstanceId = _ => NodeId("1CBE:0123", "ANOTHER");
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Length == 0,
            "Even a matching product/class path must be rejected without exact node correlation.");
        api.InterfaceInstanceId = _ => id;
        api.Read = key => key == id ? null : Node(key);
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Length == 0,
            "An interface disappearing during enumeration cannot be proposed.");
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
            valid.Replace("pid_5408", "pid_5408&mi_00")
        })
            Check(!CatalogUsbInterfaceDiscovery.TryParseExactInterface(
                invalid, ClassA, out _),
                "Malformed, unlisted, composite-unknown or wrong-class interfaces are excluded.");

        var id = NodeId("0416:5408", "THREE");
        var api = new FakeApi([id]);
        api.Nodes[id] = Node(id, "USBSTOR");
        api.Classes[id] = [ClassA];
        api.Paths[id] = [valid];
        Check(CatalogUsbInterfaceDiscovery.Capture(api).Length == 0 &&
              api.ClassesRead.Count == 0,
            "A listed storage-driver device cannot be offered for active WinUSB test.");
    }

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
        public Func<string, UsbPnpNode?>? Read { get; set; }
        public Func<string, string?>? InterfaceInstanceId { get; set; }

        public uint GetListSize(out uint characters)
        {
            characters = checked((uint)_list.Length);
            return UsbDisplayDiscovery.Success;
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
            return Classes.GetValueOrDefault(id) ?? [];
        }

        public IReadOnlyList<string> EnumerateInterfaces(Guid cls, string id) =>
            Paths.GetValueOrDefault(id) ?? [];

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
