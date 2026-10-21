namespace PinkieSysMon;

// A valid IPC payload is not USB authorization. This passive preflight uses
// the COMPLETE Runtime-owned observation, not a caller-supplied interface
// path, a product-name heuristic, or a first-matching device.
internal enum RuntimeUsbTestPreflightStatus
{
    Admitted,
    Unavailable,
    Missing,
    Ambiguous,
    BindingChanged,
    UnsupportedProtocol,
    InvalidGeometry
}

internal static class RuntimeUsbTestPreflight
{
    internal static RuntimeUsbTestPreflightStatus Check(
        CatalogUsbTrialIntent intent, CatalogUsbCandidateSnapshot current,
        IReadOnlySet<string> ignoredNodeKeys)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ignoredNodeKeys);

        if (current.UnavailableNodeKeys.Contains(
            intent.PnpNodeKey, StringComparer.OrdinalIgnoreCase))
            return RuntimeUsbTestPreflightStatus.Unavailable;

        var resolution = CatalogUsbCandidateSelection.Resolve(
            current.Candidates, intent.InterfaceId, ignoredNodeKeys);
        if (resolution.Status == CatalogUsbCandidateResolutionStatus.Ambiguous)
            return RuntimeUsbTestPreflightStatus.Ambiguous;
        if (resolution.Status != CatalogUsbCandidateResolutionStatus.Found ||
            resolution.Candidate is null)
            return RuntimeUsbTestPreflightStatus.Missing;

        var candidate = resolution.Candidate;
        if (!string.Equals(candidate.PnpNodeKey, intent.PnpNodeKey,
                StringComparison.OrdinalIgnoreCase))
            return RuntimeUsbTestPreflightStatus.BindingChanged;

        // Only a catalog-proven implemented hint is authorized in this initial
        // Runtime admission path. Additional protocols will need an explicit
        // eligibility contract, never speculative handler cycling.
        var catalog = UsbDisplayCatalog.Find(candidate.Family.Product);
        if (catalog is null ||
            !OutputTargetContract.HasIntegratedTransport(intent.Protocol) ||
            !string.Equals(catalog.CandidateTransport, intent.Protocol,
                StringComparison.OrdinalIgnoreCase))
            return RuntimeUsbTestPreflightStatus.UnsupportedProtocol;

        try
        {
            // Same canonical bounded #94 geometry generator used by the card.
            // This is an in-memory feasibility check, not a persisted board.
            _ = AdaptiveDashboardFactory.Create(intent.Width, intent.Height, 0, string.Empty);
        }
        catch (ArgumentOutOfRangeException)
        {
            return RuntimeUsbTestPreflightStatus.InvalidGeometry;
        }

        return RuntimeUsbTestPreflightStatus.Admitted;
    }
}
