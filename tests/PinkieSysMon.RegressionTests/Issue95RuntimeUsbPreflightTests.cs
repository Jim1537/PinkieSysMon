namespace PinkieSysMon.RegressionTests;

// Synthetic passive-only candidates; none of these tests opens USB.
internal static class Issue95RuntimeUsbPreflightTests
{
    private const string InterfaceA = "usb-interface:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InterfaceB = "usb-interface:" + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string NodeA = "usb-pnp:" + "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string NodeB = "usb-pnp:" + "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    private static CatalogUsbInterfaceCandidate Candidate(string id, string node,
        ushort vendor = 0x0416, ushort product = 0x5408)
    {
        var family = UsbDisplayCatalog.Find(new UsbProductId(vendor, product))!;
        return new(id, family, "synthetic-passive-only", node);
    }

    private static CatalogUsbTrialIntent Intent(
        string id = InterfaceA, string node = NodeA,
        string protocol = OutputTargetContract.LyBulkTransport,
        int width = 1920, int height = 480) =>
        new(Guid.NewGuid(), id, node, protocol, width, height, 0);

    private static RuntimeUsbTestPreflightStatus Check(
        CatalogUsbTrialIntent intent, CatalogUsbCandidateSnapshot snapshot,
        params string[] ignored) =>
        RuntimeUsbTestPreflight.Check(intent, snapshot,
            ignored.ToHashSet(StringComparer.OrdinalIgnoreCase));

    public static void ExactRuntimeAdmissionAndNegativeCases()
    {
        var first = Candidate(InterfaceA, NodeA);
        var sibling = Candidate(InterfaceB, NodeB);
        var both = new CatalogUsbCandidateSnapshot([sibling, first], []);
        Check(Check(Intent(), both) == RuntimeUsbTestPreflightStatus.Admitted,
            "Fresh catalog WinUSB observation must resolve a selected exact physical unit.");
        Check(Check(Intent(InterfaceB, NodeB), both) == RuntimeUsbTestPreflightStatus.Admitted,
            "An equal-VID/PID sibling must remain independently selectable.");
        Check(Check(Intent(InterfaceA, NodeB), both) == RuntimeUsbTestPreflightStatus.BindingChanged,
            "A matching interface from a different physical node must fail closed.");
        Check(Check(Intent(), both, NodeA) == RuntimeUsbTestPreflightStatus.Missing &&
              Check(Intent(InterfaceB, NodeB), both, NodeA) == RuntimeUsbTestPreflightStatus.Admitted,
            "Forget excludes only the selected node, not its matching-product sibling.");
        Check(Check(Intent(), new CatalogUsbCandidateSnapshot([sibling], [])) ==
              RuntimeUsbTestPreflightStatus.Missing,
            "Never use a same-product fallback when the exact interface is missing.");
        Check(Check(Intent(), new CatalogUsbCandidateSnapshot([first, first], [])) ==
              RuntimeUsbTestPreflightStatus.Ambiguous,
            "Duplicate interface ownership must never resolve by first match.");
        Check(Check(Intent(), new CatalogUsbCandidateSnapshot(
                  [first, Candidate(InterfaceB, NodeA)], [])) ==
              RuntimeUsbTestPreflightStatus.Ambiguous,
            "Multiple interfaces for one physical node remain ambiguous.");
        Check(Check(Intent(), new CatalogUsbCandidateSnapshot([first], [NodeA])) ==
              RuntimeUsbTestPreflightStatus.Unavailable,
            "An incompletely enumerated node is unavailable, not safe to test.");
    }

    public static void ProtocolAndGeometryFailClosed()
    {
        var known = Candidate(InterfaceA, NodeA);
        var snapshot = new CatalogUsbCandidateSnapshot([known], []);
        Check(Check(Intent(protocol: OutputTargetContract.EncryptedJpegTransport), snapshot) ==
              RuntimeUsbTestPreflightStatus.Admitted,
            "An explicit implemented protocol choice is independent of a passive catalog hint.");
        Check(Check(Intent(width: 1, height: 1), snapshot) ==
              RuntimeUsbTestPreflightStatus.InvalidGeometry,
            "The same #94 generator limits must apply at Runtime admission.");
        var unhinted = Candidate(InterfaceB, NodeB, product: 0x5409);
        Check(Check(Intent(InterfaceB, NodeB), new([unhinted], [])) ==
              RuntimeUsbTestPreflightStatus.Admitted,
            "An eligible exact catalog interface may be tested with an explicitly chosen protocol even without a hint.");
        var knownSecondProtocol = Candidate(InterfaceB, NodeB, 0x1CBE, 0x0123);
        Check(Check(Intent(InterfaceB, NodeB, OutputTargetContract.EncryptedJpegTransport, 1920, 720),
                  new([knownSecondProtocol], [])) == RuntimeUsbTestPreflightStatus.Admitted,
            "An independently mapped implemented protocol must have its own eligible admission.");
    }

    public static void CoordinatorRequiresFreshPreflightBeforeLease()
    {
        var first = Candidate(InterfaceA, NodeA);
        CatalogUsbCandidateSnapshot current = new([first], [NodeA]);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gate = new RuntimeUsbTestSessionGate(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40));
        var coordinator = new RuntimeUsbTestCoordinator(
            gate, () => current, () => ignored);
        var request = new CatalogUsbTestBeginRequest(
            Guid.NewGuid(), Intent());

        Check(coordinator.TryBegin(request, 0, out var denied, out var why) ==
              RuntimeUsbTestAdmission.CandidateRejected &&
              denied is null && why == RuntimeUsbTestPreflightStatus.Unavailable,
            "Stale or incomplete passive metadata cannot reserve a USB Test lease.");

        current = new([first], []);
        ignored.Add(NodeA);
        Check(coordinator.TryBegin(request, 0, out denied, out why) ==
              RuntimeUsbTestAdmission.CandidateRejected &&
              denied is null && why == RuntimeUsbTestPreflightStatus.Missing,
            "Forget must block Runtime Test acquisition, not only Editor display.");

        ignored.Clear();
        Check(coordinator.TryBegin(request, 0, out var acquired, out why) ==
              RuntimeUsbTestAdmission.Started &&
              why == RuntimeUsbTestPreflightStatus.Admitted,
            "An exact supported fresh observation may reserve a transient session.");
        var lease = acquired ?? throw new InvalidOperationException("Missing lease.");
        Check(coordinator.GetStatus(lease.Handle, 0).Phase == RuntimeUsbTestPhase.Running &&
              coordinator.RequestCancel(lease.Handle) &&
              coordinator.GetStatus(lease.Handle, 0).Phase == RuntimeUsbTestPhase.CancelRequested,
            "Only the matching handle may cancel, without signaling a terminal release.");
        Check(coordinator.ReportNativeOutcome(lease,
                  CatalogUsbTestTechnicalOutcome.CanceledAndReleased) &&
              coordinator.GetStatus(lease.Handle, 0).Phase ==
                  RuntimeUsbTestPhase.CanceledAndReleased,
            "Only a trusted native terminal callback releases the session.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
