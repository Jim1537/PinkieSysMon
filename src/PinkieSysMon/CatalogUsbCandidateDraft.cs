namespace PinkieSysMon;

// A temporary request is only a validated snapshot; it never opens USB,
// indicates a successful image test or authorizes configuration persistence.
internal sealed record CatalogUsbTrialIntent(
    Guid AttemptId, string InterfaceId, string PnpNodeKey,
    string Protocol, int Width, int Height, int Revision);

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

    internal CatalogUsbCandidateDraft(CatalogUsbInterfaceCandidate candidate,
        string suggestedName, IEnumerable<string> applicableImplementedProtocols)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(applicableImplementedProtocols);
        InterfaceId = candidate.Id;
        PnpNodeKey = candidate.PnpNodeKey;
        _name = suggestedName;
        _applicable = applicableImplementedProtocols
            .Where(OutputTargetContract.HasIntegratedTransport)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _protocol = candidate.SuggestedTransport is { } hint && _applicable.Contains(hint)
            ? hint : null;
    }

    internal string InterfaceId { get; }
    internal string PnpNodeKey { get; }
    internal IReadOnlySet<string> ApplicableProtocols => _applicable;
    internal bool IsTesting => _active is not null;

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
                _ = AdaptiveDashboardFactory.Create(Width, Height, 0, string.Empty);
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
        return _active;
    }

    // Name-only edits after a completed attempt do not alter this wire snapshot.
    // Completion is NOT technical success, visual Yes, or Save eligibility;
    // those gates require the separately approved Runtime/IPC result policy.
    internal bool CompleteAttempt(CatalogUsbTrialIntent intent)
    {
        if (!IsCurrent(intent))
            return false;
        _active = null;
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
        if (!IsCurrent(intent)) return false;
        _active = null;
        _latest = null;
        return true;
    }

    private void EnsureEditable()
    {
        if (_active is not null)
            throw new InvalidOperationException("Candidate fields cannot change while a test is in progress.");
    }
}
