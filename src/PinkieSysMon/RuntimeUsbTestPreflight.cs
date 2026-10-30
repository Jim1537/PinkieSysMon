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

        // The catalog gates candidates, not the user's choice of implemented
        // protocol. Native endpoint/alternate-setting validation precedes I/O.
        if (UsbDisplayCatalog.Find(candidate.Family.Product) is null ||
            !OutputTargetContract.HasIntegratedTransport(intent.Protocol))
            return RuntimeUsbTestPreflightStatus.UnsupportedProtocol;

        try
        {
            // Same canonical bounded #94 geometry generator used by the card.
            // This is an in-memory feasibility check, not a persisted board.
            AdaptiveDashboardFactory.ValidateGeometry(intent.Width, intent.Height, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return RuntimeUsbTestPreflightStatus.InvalidGeometry;
        }

        return RuntimeUsbTestPreflightStatus.Admitted;
    }
}
