namespace PinkieSysMon;

// A temporary request is only a validated snapshot; it never opens USB,
// indicates a successful image test or authorizes configuration persistence.
internal sealed record CatalogUsbTrialIntent(
    Guid AttemptId, string InterfaceId, string PnpNodeKey,
    string Protocol, int Width, int Height, int Revision);

// A Runtime result is not a visual confirmation. In particular, native
// cancellation/Dispose being requested is not proof of USB handle release.
internal enum CatalogUsbTestTechnicalOutcome
{
    SucceededAndReleased,
    FailedAndReleased,
    CanceledAndReleased,
    CleanupPending
}

internal sealed class CatalogUsbCandidateDraft
{
    private readonly IReadOnlySet<string> _applicable;
    private string _name;
    private int _width;
    private int _height;
    private string? _protocol;
    private CatalogUsbTrialIntent? _active;
    private Guid? _latest;
    private int _revision;
    private CatalogUsbTrialIntent? _technicallyPassed;
    private bool _visualConfirmed;
    private bool _attemptHadFailure;
    private bool _dispatchAttempted;

    internal CatalogUsbCandidateDraft(CatalogUsbInterfaceCandidate candidate,
        string suggestedName, IEnumerable<string> applicableImplementedProtocols)
    {
        ProtocolChoices = UsbDisplayCatalog.ProtocolChoices(candidate.Family);
        InterfaceId = candidate.Id;
        PnpNodeKey = candidate.PnpNodeKey;
        _name = suggestedName;
        ArgumentNullException.ThrowIfNull(applicableImplementedProtocols);
        _applicable = applicableImplementedProtocols.Where(OutputTargetContract.HasIntegratedTransport)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _protocol = candidate.SuggestedTransport is { } hint && _applicable.Contains(hint) ? hint : null;
    }

    internal OutputTargetConfig CreateConfirmedOutput(string id)
    {
        if (!CanSaveConfiguration)
            throw new InvalidOperationException("A clean Runtime Test and positive visual confirmation are required.");
        var target = new OutputTargetConfig
        {
            Id = id, Name = Name.Trim(), Kind = OutputTargetContract.PhysicalKind,
            Transport = Protocol!, DeviceId = InterfaceId, Width = Width, Height = Height,
            Dashboard = OutputDefaultDashboardPolicy.NameForPhysicalGeometry(Width, Height),
            DeviceEnabled = false, ProviderEnabled = false,
            RefreshIntervalMs = 1000, JpegQuality = 92, Usb = new(),
            EncryptedJpeg = Protocol == OutputTargetContract.EncryptedJpegTransport ? new() : null
        };
        OutputTargetContract.ValidateAndNormalize([target]);
        return target;
    }

    internal IReadOnlyList<UsbDisplayProtocolChoice> ProtocolChoices { get; }
    internal string InterfaceId { get; }
    internal string PnpNodeKey { get; }
    internal IReadOnlySet<string> ApplicableProtocols => _applicable;
    internal bool IsTesting => _active is not null;

    // Only the exact most recent, technically error-free, fully released Test
    // plus explicit visible changing-seconds confirmation may unlock Save.
    // This is a local draft gate, not authorization to persist without a fresh
    // exact binding and concurrent-write check in the eventual Save transaction.
    internal bool CanSaveConfiguration =>
        !IsTesting && _visualConfirmed && MatchesLatestWire(_technicallyPassed) &&
        !string.IsNullOrWhiteSpace(Name) && !Name.Any(char.IsControl);

    internal string Name
    {
        get => _name;
        set { EnsureEditable(); _name = value ?? string.Empty; }
    }

    internal int Width
    {
        get => _width;
        set { EnsureEditable(); if (_width != value) { _width = value; _revision++; } }
    }

    internal int Height
    {
        get => _height;
        set { EnsureEditable(); if (_height != value) { _height = value; _revision++; } }
    }

    internal string? Protocol
    {
        get => _protocol;
        set { EnsureEditable(); if (!string.Equals(_protocol, value, StringComparison.OrdinalIgnoreCase)) { _protocol = value; _revision++; } }
    }

    internal bool CanTest
    {
        get
        {
            if (IsTesting || string.IsNullOrWhiteSpace(Name) ||
                Name.Any(char.IsControl) || Protocol is null ||
                !_applicable.Contains(Protocol))
                return false;
            try
            {
                // Reuse the actual #94 geometry/layout limit rather than
                // inventing a second resolution/model restriction.
                AdaptiveDashboardFactory.ValidateGeometry(Width, Height, 0);
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }
    }

    internal CatalogUsbTrialIntent BeginTest(CatalogUsbCandidateSnapshot current,
        IReadOnlySet<string> ignoredNodes)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ignoredNodes);
        if (!CanTest)
            throw new InvalidOperationException("Candidate fields are invalid or a test is already running.");
        if (current.UnavailableNodeKeys.Contains(PnpNodeKey, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Candidate USB metadata is unavailable.");
        var found = CatalogUsbCandidateSelection.Resolve(current.Candidates, InterfaceId, ignoredNodes);
        if (found.Status != CatalogUsbCandidateResolutionStatus.Found ||
            !string.Equals(found.Candidate!.PnpNodeKey, PnpNodeKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Candidate binding is absent or ambiguous.");
        _active = new CatalogUsbTrialIntent(Guid.NewGuid(), InterfaceId,
            PnpNodeKey, Protocol!, Width, Height, _revision);
        _latest = _active.AttemptId;
        _technicallyPassed = null;
        _visualConfirmed = false;
        _attemptHadFailure = false;
        _dispatchAttempted = false;
        return _active;
    }

    // Mark before any possibly transmitted IPC write. A request timeout
    // cannot undo an attempted remote command or prove USB ownership released.
    internal bool MarkDispatchAttempted(CatalogUsbTrialIntent intent)
    {
        if (!IsCurrent(intent) || _dispatchAttempted)
            return false;
        _dispatchAttempted = true;
        return true;
    }

    // Apply a Runtime-owned *technical* result only to the exact active attempt.
    // A pending native completion holds the draft busy; no positive result
    // can resurrect an attempt that has ever reported an error/pending cleanup.
    // Runtime must log and notify on failure; this pure model neither opens
    // USB nor manufactures a success from a positive screen observation.
    internal bool RecordTechnicalOutcome(CatalogUsbTrialIntent intent,
        CatalogUsbTestTechnicalOutcome outcome)
    {
        if (!_dispatchAttempted || !IsCurrent(intent) || !Enum.IsDefined(outcome))
            return false;

        if (outcome == CatalogUsbTestTechnicalOutcome.CleanupPending)
        {
            _attemptHadFailure = true;
            _technicallyPassed = null;
            _visualConfirmed = false;
            return true;
        }

        _active = null;
        _dispatchAttempted = false;
        _technicallyPassed = outcome == CatalogUsbTestTechnicalOutcome.SucceededAndReleased &&
            !_attemptHadFailure ? intent : null;
        _visualConfirmed = false;
        return true;
    }

    // The user can approve only a clean terminal result for the current wire
    // snapshot. "No" explicitly revokes the result; a fresh Test is required.
    internal bool ConfirmVisibleChangingSeconds(CatalogUsbTrialIntent intent,
        bool confirmed)
    {
        if (IsTesting || !MatchesLatestWire(intent) || _technicallyPassed != intent)
            return false;
        if (!confirmed)
        {
            _technicallyPassed = null;
            _visualConfirmed = false;
            return true;
        }
        _visualConfirmed = true;
        return true;
    }

    // Name-only edits after a completed attempt do not alter this wire snapshot.
    // Completion is NOT technical success, visual Yes, or Save eligibility;
    // those gates require the separately approved Runtime/IPC result policy.
    internal bool CompleteAttempt(CatalogUsbTrialIntent intent)
    {
        // Legacy pre-dispatch release: never bypass Runtime native completion.
        if (_dispatchAttempted || !IsCurrent(intent))
            return false;
        _active = null;
        _technicallyPassed = null;
        _visualConfirmed = false;
        return true;
    }

    // Editing dimensions or protocol away and back never restores old approval.
    // Name-only edits leave the exact wire request unchanged.
    internal bool MatchesLatestWire(CatalogUsbTrialIntent? intent) =>
        intent is not null && _latest == intent.AttemptId &&
        intent.Revision == _revision &&
        string.Equals(intent.InterfaceId, InterfaceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(intent.PnpNodeKey, PnpNodeKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(intent.Protocol, Protocol, StringComparison.OrdinalIgnoreCase) &&
        intent.Width == Width && intent.Height == Height;

    internal bool IsCurrent(CatalogUsbTrialIntent? intent) =>
        _active is not null && _active == intent && MatchesLatestWire(intent);

    internal bool AbandonAttempt(CatalogUsbTrialIntent intent)
    {
        if (!IsCurrent(intent) || _dispatchAttempted) return false;
        // This is only permitted before any possibly dispatched IPC command.
        // After dispatch, cancellation must first be reported by Runtime after
        // native I/O has reached terminal release.
        _active = null;
        _latest = null;
        _technicallyPassed = null;
        _visualConfirmed = false;
        return true;
    }

    private void EnsureEditable()
    {
        if (_active is not null)
            throw new InvalidOperationException("Candidate fields cannot change while a test is in progress.");
    }
}
