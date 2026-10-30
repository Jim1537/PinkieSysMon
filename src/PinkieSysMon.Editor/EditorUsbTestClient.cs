using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon.Editor;

// Only IPC and process startup live here. Editor never opens a USB transport.
internal sealed class EditorUsbTestClient
{
    private readonly Guid _client = Guid.NewGuid();
    private readonly FileLogger? _log;
    private readonly Func<string, Task<RuntimeIpcResponse>> _send;
    private readonly Func<bool> _ownerExists;
    private readonly Action _launch;
    private readonly Func<Task> _delay;

    internal EditorUsbTestClient(string? root, FileLogger? log = null) : this(
        command => RuntimeIpc.SendUsbTestAsync(command, log, 1000), RuntimeProcessLease.ExistsForCurrentUser,
        () =>
        {
            var executable = Path.Combine(root ?? ApplicationRootLocator.Find(AppContext.BaseDirectory), "bin", "PinkieSysMon.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Runtime executable is unavailable. Build and publish the current version first.");
            using var process = Process.Start(new ProcessStartInfo(executable)
            { Arguments = "--usb-test-only", WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false })
                ?? throw new InvalidOperationException("Test-only Runtime could not be started.");
        }, () => Task.Delay(500), log) { }

    internal EditorUsbTestClient(Func<string, Task<RuntimeIpcResponse>> send, Func<bool> ownerExists,
        Action launch, Func<Task> delay, FileLogger? log = null)
    { _send = send; _ownerExists = ownerExists; _launch = launch; _delay = delay; _log = log; }

    internal async Task<CatalogUsbTrialIntent?> TestAsync(CatalogUsbCandidateDraft draft,
        CatalogUsbCandidateSnapshot snapshot, Action<string> report, CancellationToken cancellation,
        Action<RuntimeUsbTestStatus>? reportStatus = null)
    {
        var intent = draft.BeginTest(snapshot, new HashSet<string>());
        var handle = new CatalogUsbTestHandle(_client, intent.AttemptId);
        using var operation = _log is null ? null : new LogOperation(_log, "editor.usb-test",
            "usb-test-" + intent.AttemptId.ToString("N"), intent.AttemptId.ToString("N"));
        var context = $"protocol={intent.Protocol}; geometry={intent.Width}x{intent.Height}";
        var statusPolls = 0;
        RuntimeUsbTestPhase? lastPhase = null;
        var statusUnavailable = false;
        void ObserveStatus(RuntimeUsbTestStatus status)
        {
            if (status.Phase == lastPhase) return;
            lastPhase = status.Phase;
            operation?.Phase("status." + status.Phase, $"statusPolls={statusPolls}; reportedOutcome={status.TechnicalOutcome}");
        }
        var dispatched = false;
        var cancelSent = false;
        try
        {
            report("Preparing Runtime. Configuration is not saved.");
            cancellation.ThrowIfCancellationRequested();
            operation?.Phase("runtime.prepare", context + "; configurationSaved=false");
            var ping = await _send(RuntimeIpcProtocol.PingCommand);
            if (!ping.Success)
            {
                if (_ownerExists()) throw new InvalidOperationException("Runtime exists but is not responding. No second Runtime was started. Check its log and finish its current operation.");
                operation?.Phase("runtime.launch.test-only");
                _launch();
                for (var i = 0; i < 20 && !ping.Success; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    await _delay();
                    ping = await _send(RuntimeIpcProtocol.PingCommand);
                }
                if (!ping.Success) throw new InvalidOperationException("Test-only Runtime did not become available. Check its log; no Test request was sent.");
            }
            cancellation.ThrowIfCancellationRequested();
            operation?.Phase("runtime.ready");
            draft.MarkDispatchAttempted(intent);
            dispatched = true;
            operation?.Phase("begin.dispatchAttempted");
            var begin = await _send(CatalogUsbTestIpcProtocol.BuildBegin(_client, intent));
            if (!begin.Success && begin.ResponseReceived)
            {
                draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.FailedAndReleased);
                operation?.Cancel("runtimeReply=ERR; acceptedOutcome=FailedAndReleased; configurationSaved=false");
                report(begin.Message);
                return null;
            }
            // A lost Begin reply is not a rejection. Query this same attempt;
            // never resend it with a new ID or infer that native work stopped.
            var uncertain = !TryRead(begin, handle, out var beginStatus);
            if (!uncertain) { ObserveStatus(beginStatus); reportStatus?.Invoke(beginStatus); }
            else operation?.RecordFailure(null, "begin.response", context + "; dispatchAttempted=true; replyUnavailableOrInvalid=true; outcome=unknown; beginNotRetried=true");
            for (var i = 0; i < 200; i++)
            {
                if (cancellation.IsCancellationRequested && !cancelSent)
                {
                    cancelSent = true;
                    operation?.Phase("cancel.requested", "nativeRelease=unconfirmed");
                    var cancel = await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle));
                    operation?.Phase("cancel.response", $"success={cancel.Success}; responseReceived={cancel.ResponseReceived}; releaseNotAsserted=true");
                }
                statusPolls++;
                var response = await _send(CatalogUsbTestIpcProtocol.BuildStatus(handle));
                if (TryRead(response, handle, out var status))
                {
                    if (statusUnavailable)
                    {
                        operation?.Phase("status.availableAgain", $"statusPolls={statusPolls}; earlierUncertaintyRetained=true");
                        statusUnavailable = false;
                    }
                    ObserveStatus(status);
                    reportStatus?.Invoke(status);
                    if (status.TechnicalOutcome is { } terminal)
                    {
                        // A local Cancel cannot be turned into visual success by
                        // a terminal success racing the cancel request.
                        if (cancellation.IsCancellationRequested && terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased)
                            terminal = CatalogUsbTestTechnicalOutcome.CanceledAndReleased;
                        if (uncertain && terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased)
                            terminal = CatalogUsbTestTechnicalOutcome.FailedAndReleased;
                        draft.RecordTechnicalOutcome(intent, terminal);
                        var resultDetail = $"{context}; reportedOutcome={status.TechnicalOutcome}; acceptedOutcome={terminal}; statusPolls={statusPolls}; visualApproval=false; configurationSaved=false";
                        if (terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased)
                            operation?.Complete(resultDetail);
                        else if (terminal == CatalogUsbTestTechnicalOutcome.CanceledAndReleased)
                            operation?.Cancel(resultDetail);
                        else
                        {
                            operation?.RecordFailure(null, "technical.result", resultDetail);
                            operation?.Partial(resultDetail);
                        }
                        report(terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased
                            ? "Technical Test finished; USB released. Confirm the image and changing seconds you observed."
                            : "Test not passed. Configuration was not saved. Check the Runtime log and retry.");
                        return terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased ? intent : null;
                    }
                    if (status.Phase == RuntimeUsbTestPhase.CleanupPending)
                    {
                        draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
                        operation?.Pending("cleanup.pending", context + "; nativeRelease=unconfirmed; owner=Runtime; configurationSaved=false");
                        operation?.Partial("technical=CleanupPending; nativeRelease=unconfirmed");
                        report("Test not passed: native cleanup is unconfirmed. Runtime retains ownership; check its log.");
                        return null;
                    }
                    report(cancelSent ? "Cancel requested; waiting for Runtime to release USB."
                        : "Observe the current test image and changing seconds. Waiting for Runtime completion…");
                }
                else
                {
                    uncertain = true;
                    if (!statusUnavailable) operation?.RecordFailure(null, "status.unavailable", $"{context}; statusPolls={statusPolls}; exactAttemptRetained=true; outcome=unknown");
                    statusUnavailable = true;
                    reportStatus?.Invoke(new(RuntimeUsbTestPhase.Missing));
                    report("Runtime outcome is unavailable; waiting for this exact attempt. Configuration remains unsaved.");
                }
                await _delay();
            }
            operation?.Phase("cancel.observationLimit", "nativeRelease=unconfirmed");
            await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle));
            draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
            operation?.Pending("completion.unconfirmed", $"{context}; statusPolls={statusPolls}; owner=Runtime; nativeRelease=unconfirmed");
            operation?.Partial("technical=CleanupPending; nativeRelease=unconfirmed");
            report("Test not passed: Runtime completion could not be confirmed. You may close this card; Runtime retains USB ownership until cleanup.");
            return null;
        }
        catch (Exception error)
        {
            if (!dispatched) draft.AbandonAttempt(intent);
            else
            {
                draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
                try { await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle)); }
                catch (Exception cancelError) { operation?.RecordFailure(cancelError, "cancel.afterFailure", context + "; nativeRelease=unconfirmed"); }
            }
            if (error is OperationCanceledException && !dispatched)
                operation?.Cancel("cancelledBeforeDispatch=true; configurationSaved=false");
            else operation?.Fail(error, $"{context}; dispatchAttempted={dispatched}; nativeRelease={(dispatched ? "unconfirmed" : "not-acquired")}; statusPolls={statusPolls}");
            report(error is OperationCanceledException ? "Test canceled before dispatch. Configuration was not saved."
                : dispatched ? "Test not passed: Runtime outcome is unknown. Check its log; configuration was not saved." : error.Message);
            return null;
        }
    }

    internal static void RecordVisualOutcome(FileLogger? log, CatalogUsbTrialIntent intent, bool yes, bool canSave)
    {
        if (log is null) return;
        using var operation = new LogOperation(log, "editor.usb-test.visual",
            "usb-test-" + intent.AttemptId.ToString("N"), intent.AttemptId.ToString("N"));
        var detail = $"visualYes={yes}; saveEligible={canSave}; configurationSaved=false";
        if (yes) operation.Complete(detail); else operation.Cancel(detail);
    }

    internal static bool TryRead(RuntimeIpcResponse response, CatalogUsbTestHandle handle, out RuntimeUsbTestStatus status)
    {
        status = new(RuntimeUsbTestPhase.Missing);
        if (!response.Success) return false;
        try
        {
            var reply = JsonSerializer.Deserialize<CatalogUsbTestReply>(response.Message);
            if (reply?.Handle != handle || reply.Status is null || !Enum.IsDefined(reply.Status.Phase)) return false;
            if (reply.Status.Phase == RuntimeUsbTestPhase.Observing)
            {
                if (reply.Status.SecondsRemaining is not int seconds || seconds < 0 ||
                    seconds > RuntimeUsbTestService.ObservationDuration.TotalSeconds) return false;
            }
            else if (reply.Status.SecondsRemaining is not null) return false;
            var expected = reply.Status.Phase switch
            {
                RuntimeUsbTestPhase.SucceededAndReleased => CatalogUsbTestTechnicalOutcome.SucceededAndReleased,
                RuntimeUsbTestPhase.FailedAndReleased => CatalogUsbTestTechnicalOutcome.FailedAndReleased,
                RuntimeUsbTestPhase.CanceledAndReleased => CatalogUsbTestTechnicalOutcome.CanceledAndReleased,
                _ => (CatalogUsbTestTechnicalOutcome?)null
            };
            if (reply.Status.TechnicalOutcome != expected) return false;
            status = reply.Status;
            return status.Phase != RuntimeUsbTestPhase.Missing;
        }
        catch (JsonException) { return false; }
    }
}
