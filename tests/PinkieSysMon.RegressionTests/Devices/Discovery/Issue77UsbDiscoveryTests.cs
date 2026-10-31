namespace PinkieSysMon.RegressionTests;

// Exercise the production parser, classifier and bounded enumeration through
// metadata fakes. No native Windows calls, filesystem writes or USB operations.
internal static class Issue77UsbDiscoveryTests
{
    private static readonly Guid UsbSetupClass = new("88BAE032-5A81-49F0-BC3D-A4FF138216D6");

    public static void CatalogAndExactParsing()
    {
        string[] expected = ["0416:5408", "0416:5409", "0416:5302", "0418:5303", "0418:5304",
            "0416:5406", "0402:3922", "87CD:70DB", "87AD:70DB", "1CBE:0123", "1CBE:0028",
            "1CBE:0046", "1CBE:0050", "1CBE:0080", "1CBE:0088", "1CBE:0092"];
        Check(UsbDisplayCatalog.Families.Select(f => f.Product.ToString()).Order().SequenceEqual(expected.Order()),
            "Recognize exactly the reviewed product pairs, without a broad vendor or brand filter.");
        foreach (var family in UsbDisplayCatalog.Families)
        {
            var id = Id(family.Product.ToString());
            Check(UsbDisplayCatalog.TryParseInstanceId(id.ToLowerInvariant(), out var product, out var function) &&
                  product == family.Product && !function && UsbDisplayCatalog.Find(product) == family,
                "Every reviewed product must parse case-insensitively and retain its family.");
            Check(Uri.TryCreate(family.EvidenceUrl, UriKind.Absolute, out var url) && url.Scheme == "https",
                "Every catalog row needs a pinned research reference.");
        }
        Check(UsbDisplayCatalog.TryParseInstanceId(@"USB\VID_1CBE&PID_0123&REV_0100&MI_01\SYNTHETIC", out _, out var mi) && mi,
            "Revision and interface tokens must not be confused with the product ID.");
        foreach (var malformed in new string?[] { null, "", " ", @"HID\VID_1CBE&PID_0123\SYNTHETIC",
            @"USBSTOR\VID_0402&PID_3922\SYNTHETIC", @"USB\VID_1CBE&PID_01230\SYNTHETIC",
            @"USB\VID_1CBE&PID_0123_EXTRA\SYNTHETIC", @"USB\XVID_1CBE&PID_0123\SYNTHETIC",
            @"USB\VID_ZZZZ&PID_0123\SYNTHETIC", @"USB\VID_1CBE&PID_0123\",
            @"USB\VID_1CBE&PID_0123\SYNTHETIC\EXTRA", @"USB\VID_1CBE&PID_0123&MI_00&MI_01\SYNTHETIC",
            @"USB\VID_1CBE&PID_0123&MI_0\SYNTHETIC", @"USB\VID_1CBE&PID_0123&BOGUS\SYNTHETIC",
            @"\\?\usb#vid_1cbe&pid_0123#SYNTHETIC#{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
            "USB\\VID_1CBE&PID_0123\\BAD\nTOKEN", "USB\\VID_1CBE&PID_0123\\" + new string('x', 200) })
            Check(!UsbDisplayCatalog.TryParseInstanceId(malformed, out _, out _),
                "Malformed, unrelated-enumerator or substring identities must be excluded.");
        Check(UsbDisplayCatalog.Find(new UsbProductId(0x0416, 0x8001)) is null &&
              UsbDisplayCatalog.Find(new UsbProductId(0x1CBE, 0x9999)) is null,
            "LED controllers and unknown same-vendor products must stay excluded.");
    }

    public static void PassiveCatalogCapture()
    {
        var ids = UsbDisplayCatalog.Families.Select(f => Id(f.Product.ToString())).ToArray();
        var api = new FakeApi(ids.Concat([ids[0].ToLowerInvariant(), Id("0416:8001"), Id("1234:5678")]).ToArray());
        var observed = UsbDisplayDiscovery.Capture(api);
        Check(observed.Count == 16 && api.ReadCalls.Count == 16 && api.SizeCalls == 1 && api.ListCalls == 1,
            "One passive capture must deduplicate aliases and read metadata only for reviewed display products.");
        Check(observed.All(o => o.Status == UsbDisplayObservationStatus.Recognized && o.NodeCount == 1),
            "Recognition reports metadata presence, without manufacturing a connected Output.");
        Check(UsbDisplayDiscovery.BuildObservations(ids.Reverse().Select(id => Node(id)))
                .Select(o => o.ObservationId).SequenceEqual(observed.Select(o => o.ObservationId)),
            "Observation ordering must be deterministic independently of native enumeration order.");
        Check(UsbDisplayDiscovery.Capture(new FakeApi([])).Count == 0,
            "A successful empty scan must remain distinguishable from unavailable discovery.");
    }

    public static void CompositeAndDistinctUnits()
    {
        var root = Id("1CBE:0123", "SYNTHETIC-A");
        var other = Id("1CBE:0123", "SYNTHETIC-B");
        var child = @"USB\VID_1CBE&PID_0123&MI_00\SYNTHETIC-FUNCTION";
        var secondChild = @"USB\VID_1CBE&PID_0123&MI_01\SYNTHETIC-FUNCTION";
        var observed = UsbDisplayDiscovery.BuildObservations([
            Node(root), Node(root.ToLowerInvariant()), Node(other), Node(child, root), Node(secondChild, root)]);
        Check(observed.Count == 2 && observed.Select(o => o.NodeCount).Order().SequenceEqual(new[] { 1, 3 }) &&
              observed.All(o => o.Status == UsbDisplayObservationStatus.Recognized),
            "Only actual matching parent relationships group functions; two same-model units stay distinct.");
        Check(UsbDisplayDiscovery.BuildObservations([Node(child)]) is
                [{ Status: UsbDisplayObservationStatus.MetadataUnavailable, NodeCount: 1 }],
            "An orphaned function cannot assert physical grouping from an instance token.");
        var wrongParent = Id("0416:5408", "SYNTHETIC-A");
        var mismatch = UsbDisplayDiscovery.BuildObservations([Node(wrongParent), Node(child, wrongParent)]);
        Check(mismatch.Count == 2 && mismatch.Count(o => o.Status == UsbDisplayObservationStatus.MetadataUnavailable) == 1,
            "A cross-family parent must never merge observations or promote a TURZX into Trofeo.");
        var cycle = UsbDisplayDiscovery.BuildObservations([Node(child, secondChild), Node(secondChild, child)]);
        Check(cycle.Count == 2 && cycle.All(o => o.Status == UsbDisplayObservationStatus.MetadataUnavailable),
            "MI-to-MI cycles are not verified physical parent relationships.");
    }

    public static void ConflictingAndMissingMetadata()
    {
        var id = Id("0416:5406");
        var conflict = UsbDisplayDiscovery.BuildObservations([Node(id), Node(id.ToLowerInvariant(), service: "USBSTOR")]);
        Check(conflict is [{ Status: UsbDisplayObservationStatus.Ambiguous }],
            "Conflicting observations of one devnode must not choose a driver/profile by enumeration order.");
        var missing = UsbDisplayDiscovery.BuildObservations([new UsbPnpNode(id, null, null, null, 28, false)]);
        Check(missing is [{ Status: UsbDisplayObservationStatus.MetadataUnavailable }] &&
              missing[0].DriverServices.Count == 0 && missing[0].ProblemCodes.SequenceEqual(new uint[] { 28 }),
            "Missing driver/class metadata must remain a visible gap, retaining the observed Windows problem code.");
        var root = Id("1CBE:0123");
        var child = @"USB\VID_1CBE&PID_0123&MI_00\SYNTHETIC-CHILD";
        var grouped = UsbDisplayDiscovery.BuildObservations([Node(root),
            new UsbPnpNode(child, root, null, null, null, false)]);
        Check(grouped is [{ Status: UsbDisplayObservationStatus.MetadataUnavailable, NodeCount: 2 }],
            "Missing function metadata must not become a fully observed composite device.");
    }

    public static void BufferRacesAndErrors()
    {
        var api = new FakeApi([Id("1CBE:0123")]) { BufferSmallCount = 2 };
        Check(UsbDisplayDiscovery.Capture(api).Count == 1 && api.SizeCalls == 3 && api.ListCalls == 3,
            "A changing CM list may retry at most three complete size/list pairs.");
        var unstable = new FakeApi([]) { BufferSmallCount = 4 };
        Throws(() => UsbDisplayDiscovery.Capture(unstable));
        Check(unstable.ListCalls == 3 && unstable.ReadCalls.Count == 0,
            "Persistent buffer races must fail visibly without metadata reads or an unbounded loop.");
        var denied = new FakeApi([]) { ListError = 0x33 };
        Throws(() => UsbDisplayDiscovery.Capture(denied));
        Check(denied.SizeCalls == 1 && denied.ListCalls == 1,
            "Non-buffer CM errors are unavailable discovery, not a retry storm or an empty success.");
        var sizeError = new FakeApi([]) { SizeError = 0x33 };
        Throws(() => UsbDisplayDiscovery.Capture(sizeError));
        Check(sizeError.ListCalls == 0, "A failed size query must not allocate/read the list.");
        foreach (var length in new uint[] { 0, UsbDisplayDiscovery.MaximumListCharacters + 1U, uint.MaxValue })
        {
            var invalid = new FakeApi([]) { ForcedSize = length };
            Throws(() => UsbDisplayDiscovery.Capture(invalid));
            Check(invalid.ListCalls == 0, "Invalid/unbounded native lengths must be rejected before allocation.");
        }
        Throws(() => UsbDisplayDiscovery.ParseMultiSz(['x', '\0']));
        Throws(() => UsbDisplayDiscovery.ParseMultiSz(['\0', 'x']));
        Check(UsbDisplayDiscovery.ParseMultiSz(['\0', '\0']).Count == 0,
            "Only a correctly terminated empty MULTI_SZ is an empty discovery result.");
        Check(UsbDisplayDiscovery.Capture(new FakeApi([]) { RawList = ['\0'] }).Count == 0,
            "Accept an empty native set containing only its final NUL, without skipping the API result check.");
    }

    public static void RemovalAndPrivateIdentity()
    {
        var removed = Id("1CBE:0123", "SYNTHETIC-REMOVED");
        var alive = Id("0416:5408", "SYNTHETIC-ALIVE");
        var api = new FakeApi([removed, alive]) { Read = id => id == removed ? null : Node(id) };
        var observed = UsbDisplayDiscovery.Capture(api);
        Check(observed.Count == 1 && observed[0].Family.Product == new UsbProductId(0x0416, 0x5408),
            "A node removed between list and lookup must not survive as a current observation or affect its sibling.");
        var failure = new FakeApi([alive]) { Read = _ => throw new InvalidOperationException("Synthetic CM lookup error.") };
        Throws(() => UsbDisplayDiscovery.Capture(failure));
        var wrong = new FakeApi([alive]) { Read = _ => Node(removed) };
        Throws(() => UsbDisplayDiscovery.Capture(wrong));
        Check(!observed[0].ToString().Contains("SYNTHETIC", StringComparison.Ordinal) &&
              !Node(alive).ToString()!.Contains("SYNTHETIC", StringComparison.Ordinal) &&
              observed[0].ObservationId.StartsWith("usb-pnp:", StringComparison.Ordinal) &&
              !observed[0].ObservationId.StartsWith("usb-interface:", StringComparison.Ordinal),
            "Public observation text cannot expose raw instance tokens or impersonate persisted Trofeo identity.");
        var reobserved = UsbDisplayDiscovery.BuildObservations([Node(alive.ToLowerInvariant())]);
        Check(reobserved[0].ObservationId == observed[0].ObservationId &&
              UsbDisplayDiscovery.BuildObservations([Node(removed)])[0].ObservationId != observed[0].ObservationId,
            "Snapshot keys must be case-stable and unit-specific, without asserting serial or port stability.");
    }

    private static string Id(string pair, string token = "SYNTHETIC-UNIT") =>
        $"USB\\VID_{pair[..4]}&PID_{pair[5..]}\\{token}";
    private static UsbPnpNode Node(string id, string? parent = null, string service = "WINUSB") =>
        new(id, parent, service, UsbSetupClass, 0, true);

    private sealed class FakeApi(string[] ids) : IUsbPnpDiscoveryApi
    {
        private readonly char[] _list = ids.Length == 0 ? ['\0', '\0'] : (string.Join('\0', ids) + "\0\0").ToCharArray();
        public int SizeCalls { get; private set; }
        public int ListCalls { get; private set; }
        public List<string> ReadCalls { get; } = [];
        public int BufferSmallCount { get; init; }
        public uint SizeError { get; init; }
        public uint ListError { get; init; }
        public uint? ForcedSize { get; init; }
        public char[]? RawList { get; init; }
        public Func<string, UsbPnpNode?> Read { get; init; } = id => Node(id);
        public uint GetListSize(out uint characters)
        {
            SizeCalls++;
            characters = ForcedSize ?? checked((uint)(RawList ?? _list).Length);
            return SizeError;
        }
        public uint GetList(char[] buffer)
        {
            ListCalls++;
            if (ListCalls <= BufferSmallCount) return UsbDisplayDiscovery.BufferSmall;
            if (ListError != 0) return ListError;
            var list = RawList ?? _list;
            if (list.Length > buffer.Length) return UsbDisplayDiscovery.BufferSmall;
            list.CopyTo(buffer, 0);
            return UsbDisplayDiscovery.Success;
        }
        public UsbPnpNode? ReadNode(string id)
        {
            ReadCalls.Add(id);
            return Read(id);
        }
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected unsafe/unavailable discovery to be rejected.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
