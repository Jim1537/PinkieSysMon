namespace PinkieSysMon;

// This is the Runtime's future IPC-to-worker admission boundary. The wire
// parser validates shape, but only an independent fresh passive snapshot can
// admit a session. This wrapper never opens a USB handle or writes config.
internal sealed class RuntimeUsbTestCoordinator(
    RuntimeUsbTestSessionGate gate,
    Func<CatalogUsbCandidateSnapshot> captureCurrent,
    Func<IReadOnlySet<string>> readIgnoredNodes)
{
    internal RuntimeUsbTestAdmission TryBegin(
        CatalogUsbTestBeginRequest request, long timestamp,
        out RuntimeUsbTestLease? lease, out RuntimeUsbTestPreflightStatus preflight)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = CatalogUsbTestIpcProtocol.BuildBegin(request.ClientId, request.Intent);
        lease = null;
        // A failed or ambiguous discovery must not be mistaken for an absent
        // device and cannot authorize an alternate/previously cached path.
        preflight = RuntimeUsbTestPreflight.Check(
            request.Intent, captureCurrent(), readIgnoredNodes());
        if (preflight != RuntimeUsbTestPreflightStatus.Admitted)
            return RuntimeUsbTestAdmission.CandidateRejected;

        // The native worker must repeat exact binding/owner checks immediately
        // before opening. The observed candidate can disappear after this call.
        return gate.TryBegin(request, timestamp, out lease);
    }

    internal RuntimeUsbTestStatus GetStatus(CatalogUsbTestHandle handle, long timestamp) =>
        gate.GetStatus(handle, timestamp);

    internal bool RequestCancel(CatalogUsbTestHandle handle) => gate.RequestCancel(handle);

    internal void Sweep(long timestamp) => gate.Sweep(timestamp);

    // Trusted worker callback only. IPC Status/Cancel cannot mark USB released.
    internal bool ReportNativeOutcome(RuntimeUsbTestLease lease,
        CatalogUsbTestTechnicalOutcome result) => gate.ReportTechnicalOutcome(lease, result);
}
