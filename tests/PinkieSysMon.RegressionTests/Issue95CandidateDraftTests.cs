namespace PinkieSysMon.RegressionTests;

// All candidate identities and interface paths here are synthetic.
internal static class Issue95CandidateDraftTests
{
    private static CatalogUsbInterfaceCandidate Candidate(string product, string unit)
    {
        var bits = product.Split(':');
        var family = UsbDisplayCatalog.Find(new UsbProductId(
            Convert.ToUInt16(bits[0], 16), Convert.ToUInt16(bits[1], 16)))!;
        var path = $@"\\?\usb#vid_{bits[0]}&pid_{bits[1]}#SYNTHETIC-{unit}#{{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}}";
        var node = $@"USB\VID_{bits[0]}&PID_{bits[1]}\SYNTHETIC-{unit}";
        return new CatalogUsbInterfaceCandidate(
            CatalogUsbInterfaceDiscovery.StableInterfaceId(path), family, path,
            UsbDisplayDiscovery.CreateObservationId(node));
    }

    private static CatalogUsbCandidateSnapshot Snapshot(params CatalogUsbInterfaceCandidate[] candidates) =>
        new(candidates, []);

    public static void EditableParametersAndRuntimeIntent()
    {
        var hardware = Candidate("0416:5408", "PONY");
        var draft = new CatalogUsbCandidateDraft(hardware, "Demo", [OutputTargetContract.TrofeoTransport]);
        Check(draft.Protocol == OutputTargetContract.TrofeoTransport && !draft.CanTest,
            "An evidence-backed protocol hint is not a geometry preset.");
        draft.Width = 1920;
        draft.Height = 480;
        Check(draft.CanTest, "Valid user geometry and suggested integrated protocol admit a trial intent.");
        var test = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(draft.IsTesting && draft.IsCurrent(test) &&
              test.InterfaceId == hardware.Id &&
              test.PnpNodeKey == hardware.PnpNodeKey &&
              test.Width == 1920 && test.Height == 480 &&
              test.Protocol == OutputTargetContract.TrofeoTransport,
            "A test intent must bind the exact physical instance and user parameters.");
        Check(!draft.CanTest, "A second test cannot begin during an active attempt.");
        CheckThrows(() => draft.Width = 800, "Inputs must be frozen while a test is running.");
        Check(!draft.CompleteAttempt(test with { AttemptId = Guid.NewGuid() }) &&
              draft.IsTesting, "Unrelated attempt completion cannot release current ownership.");
        Check(draft.CompleteAttempt(test) && !draft.IsTesting,
            "Only the matching attempt can finish; completion does not imply USB or visual success.");
        draft.Name = "Renamed label";
        Check(draft.MatchesLatestWire(test), "Name-only changes preserve the completed wire fingerprint.");
        draft.Width = 960;
        draft.Width = 1920;
        Check(!draft.MatchesLatestWire(test), "Changing geometry and reverting it invalidates approval.");
        var newer = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(!draft.MatchesLatestWire(test) && draft.IsCurrent(newer) &&
              draft.AbandonAttempt(newer) && !draft.MatchesLatestWire(newer),
            "A later or canceled Test must supersede prior attempt identity.");
    }

    public static void InvalidAndUnprovenCandidatesFailClosed()
    {
        var known = Candidate("1CBE:0123", "FIRST");
        var sameProduct = Candidate("1CBE:0123", "SECOND");
        var unhinted = Candidate("0416:5409", "UNKNOWN");
        var draft = new CatalogUsbCandidateDraft(unhinted, "Generic family", []);
        draft.Width = 640; draft.Height = 640;
        Check(draft.Protocol is null && !draft.CanTest,
            "Catalog recognition without an eligible implemented handler is informational.");
        var manual = new CatalogUsbCandidateDraft(unhinted, "Unidentified", [
            OutputTargetContract.TrofeoTransport, OutputTargetContract.TurzxTransport]);
        manual.Width = 640; manual.Height = 640;
        Check(!manual.CanTest, "Ambiguous protocol applicability requires explicit user choice.");
        manual.Protocol = OutputTargetContract.TurzxTransport;
        Check(manual.CanTest, "A user-selected eligible implemented protocol may form a trial request.");
        manual.Protocol = "arbitrary";
        Check(!manual.CanTest, "An unimplemented or inapplicable protocol must never be tested.");
        manual.Protocol = OutputTargetContract.TurzxTransport;
        manual.Name = "  ";
        Check(!manual.CanTest, "Candidate name is mandatory.");
        manual.Name = "Okay";
        manual.Width = 1;
        Check(!manual.CanTest, "Infeasible #94 dashboard geometry must fail before USB Test.");
        manual.Width = 100000;
        manual.Height = 100000;
        Check(!manual.CanTest, "The test must respect the existing safe raster budget.");
        manual.Width = 640; manual.Height = 640;

        var binding = new CatalogUsbCandidateDraft(known, "Known", [OutputTargetContract.TurzxTransport]);
        binding.Width = 1920; binding.Height = 720;
        CheckThrows(() => binding.BeginTest(Snapshot(sameProduct), new HashSet<string>()),
            "Same VID/PID on a different device must not satisfy the selected binding.");
        CheckThrows(() => binding.BeginTest(Snapshot(known, known), new HashSet<string>()),
            "Duplicate exact-interface identities must fail closed.");
        CheckThrows(() => binding.BeginTest(Snapshot(known, sameProduct), new HashSet<string>
        { known.PnpNodeKey.ToUpperInvariant() }), "Forgotten device cannot start a test.");
        CheckThrows(() => binding.BeginTest(new CatalogUsbCandidateSnapshot([known],
            [known.PnpNodeKey]), new HashSet<string>()),
            "Unavailable metadata must deny Test even if the candidate appears in a stale snapshot.");
        Check(!binding.IsTesting, "Every rejected request must leave the draft inactive.");
    }

    public static void OnlyCleanRuntimeAndPositiveVisualAllowSave()
    {
        var hardware = Candidate("0416:5408", "TEST-GOOD");
        var draft = new CatalogUsbCandidateDraft(hardware, "Display", [OutputTargetContract.TrofeoTransport])
        { Width = 1920, Height = 480 };
        var first = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(!draft.CanSaveConfiguration &&
              !draft.RecordTechnicalOutcome(first, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              !draft.ConfirmVisibleChangingSeconds(first, true),
            "No synthetic Runtime success or early visual answer can approve an undispatched Test.");
        Check(draft.MarkDispatchAttempted(first) && !draft.MarkDispatchAttempted(first) &&
              !draft.CompleteAttempt(first) && !draft.AbandonAttempt(first),
            "A dispatched Test cannot be silently released or restarted from the Editor.");
        Check(draft.RecordTechnicalOutcome(first, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              !draft.CanSaveConfiguration,
            "Even a clean technical completion does not authorize Save without visual Yes.");
        Check(draft.ConfirmVisibleChangingSeconds(first, true) && draft.CanSaveConfiguration,
            "Exact clean, terminal Runtime success plus changing-seconds Yes grants Save eligibility.");
        draft.Name = "After test rename";
        Check(draft.CanSaveConfiguration, "Name-only edits must retain approval.");
        draft.Name = "";
        Check(!draft.CanSaveConfiguration, "An invalid name must block Save despite old approval.");
        draft.Name = "Restored label";
        Check(draft.CanSaveConfiguration, "Valid name can restore existing exact-wire approval.");
        draft.Height = 600;
        draft.Height = 480;
        Check(!draft.CanSaveConfiguration,
            "A changed-and-restored wire parameter must invalidate prior approval forever.");
        var second = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(!draft.ConfirmVisibleChangingSeconds(first, true) &&
              !draft.MarkDispatchAttempted(first) && !draft.CanSaveConfiguration,
            "Late results from an earlier attempt must not authorize the current card.");
        Check(draft.MarkDispatchAttempted(second) &&
              draft.RecordTechnicalOutcome(second, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              draft.ConfirmVisibleChangingSeconds(second, false) &&
              !draft.CanSaveConfiguration &&
              !draft.ConfirmVisibleChangingSeconds(second, true),
            "A visual No must revoke the result; Yes cannot resurrect it without a new Test.");
    }

    public static void TechnicalFailureAndNativeCleanupFailClosed()
    {
        var hardware = Candidate("0416:5408", "TEST-FAIL");
        var draft = new CatalogUsbCandidateDraft(hardware, "Demo", [OutputTargetContract.TrofeoTransport])
        { Width = 640, Height = 640 };
        var pending = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(draft.MarkDispatchAttempted(pending) &&
              draft.RecordTechnicalOutcome(pending, CatalogUsbTestTechnicalOutcome.CleanupPending) &&
              draft.IsTesting && !draft.CanSaveConfiguration &&
              !draft.ConfirmVisibleChangingSeconds(pending, true) &&
              !draft.CompleteAttempt(pending) && !draft.AbandonAttempt(pending),
            "Native cleanup pending must hold ownership and deny positive visual confirmation.");
        CheckThrows(() => draft.BeginTest(Snapshot(hardware), new HashSet<string>()),
            "A second trial is forbidden while native I/O is pending.");
        Check(draft.RecordTechnicalOutcome(pending, CatalogUsbTestTechnicalOutcome.SucceededAndReleased) &&
              !draft.IsTesting && !draft.CanSaveConfiguration &&
              !draft.ConfirmVisibleChangingSeconds(pending, true),
            "A late success after any pending-cleanup failure cannot convert that Test into PASS.");

        foreach (var outcome in new[]
        {
            CatalogUsbTestTechnicalOutcome.FailedAndReleased,
            CatalogUsbTestTechnicalOutcome.CanceledAndReleased
        })
        {
            var attempt = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
            Check(draft.MarkDispatchAttempted(attempt) &&
                  !draft.RecordTechnicalOutcome(attempt, (CatalogUsbTestTechnicalOutcome)999) &&
                  draft.IsTesting,
                "Invalid terminal result types must not release a running trial.");
            Check(draft.RecordTechnicalOutcome(attempt, outcome) &&
                  !draft.CanSaveConfiguration &&
                  !draft.ConfirmVisibleChangingSeconds(attempt, true),
                "USB/ACK errors and cancellation must never be overridden by a visible image.");
            Check(!draft.RecordTechnicalOutcome(attempt,
                      CatalogUsbTestTechnicalOutcome.SucceededAndReleased),
                "A late result cannot upgrade an already completed failed attempt.");
        }
        var unsent = draft.BeginTest(Snapshot(hardware), new HashSet<string>());
        Check(draft.AbandonAttempt(unsent) && !draft.CanSaveConfiguration,
            "A never-dispatched draft attempt may be dropped without USB ownership.");
    }

    private static void CheckThrows(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
