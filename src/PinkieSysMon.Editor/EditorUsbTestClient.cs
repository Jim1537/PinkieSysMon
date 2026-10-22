using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon.Editor;

// Only IPC and process startup live here. Editor never opens a USB transport.
internal sealed class EditorUsbTestClient
{
    private readonly Guid _client = Guid.NewGuid();
    private readonly Func<string, Task<RuntimeIpcResponse>> _send;
    private readonly Func<bool> _ownerExists;
    private readonly Action _launch;
    private readonly Func<Task> _delay;

    internal EditorUsbTestClient(string? root) : this(
        command => RuntimeIpc.SendAsync(command, 1000), RuntimeProcessLease.ExistsForCurrentUser,
        () =>
        {
            var executable = Path.Combine(root ?? ApplicationRootLocator.Find(AppContext.BaseDirectory), "bin", "PinkieSysMon.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Runtime executable is unavailable. Build and publish the current version first.");
            using var process = Process.Start(new ProcessStartInfo(executable)
            { Arguments = "--usb-test-only", WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false })
                ?? throw new InvalidOperationException("Test-only Runtime could not be started.");
        }, () => Task.Delay(500)) { }

    internal EditorUsbTestClient(Func<string, Task<RuntimeIpcResponse>> send, Func<bool> ownerExists,
        Action launch, Func<Task> delay)
    { _send = send; _ownerExists = ownerExists; _launch = launch; _delay = delay; }

    internal async Task<CatalogUsbTrialIntent?> TestAsync(CatalogUsbCandidateDraft draft,
        CatalogUsbCandidateSnapshot snapshot, Action<string> report, CancellationToken cancellation,
        Action<RuntimeUsbTestStatus>? reportStatus = null)
    {
        var intent = draft.BeginTest(snapshot, new HashSet<string>());
        var handle = new CatalogUsbTestHandle(_client, intent.AttemptId);
        var dispatched = false;
        var cancelSent = false;
        try
        {
            report("Preparing Runtime. Configuration is not saved.");
            cancellation.ThrowIfCancellationRequested();
            var ping = await _send(RuntimeIpcProtocol.PingCommand);
            if (!ping.Success)
            {
                if (_ownerExists()) throw new InvalidOperationException("Runtime exists but is not responding. No second Runtime was started. Check its log and finish its current operation.");
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
            draft.MarkDispatchAttempted(intent);
            dispatched = true;
            var begin = await _send(CatalogUsbTestIpcProtocol.BuildBegin(_client, intent));
            if (!begin.Success && begin.ResponseReceived)
            {
                draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.FailedAndReleased);
                report(begin.Message);
                return null;
            }
            // A lost Begin reply is not a rejection. Query this same attempt;
            // never resend it with a new ID or infer that native work stopped.
            var uncertain = !TryRead(begin, handle, out var beginStatus);
            if (!uncertain) reportStatus?.Invoke(beginStatus);
            for (var i = 0; i < 200; i++)
            {
                if (cancellation.IsCancellationRequested && !cancelSent)
                {
                    cancelSent = true;
                    await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle));
                }
                var response = await _send(CatalogUsbTestIpcProtocol.BuildStatus(handle));
                if (TryRead(response, handle, out var status))
                {
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
                        report(terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased
                            ? "Technical Test finished; USB released. Confirm the image and changing seconds you observed."
                            : "Test not passed. Configuration was not saved. Check the Runtime log and retry.");
                        return terminal == CatalogUsbTestTechnicalOutcome.SucceededAndReleased ? intent : null;
                    }
                    if (status.Phase == RuntimeUsbTestPhase.CleanupPending)
                    {
                        draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
                        report("Test not passed: native cleanup is unconfirmed. Runtime retains ownership; check its log.");
                        return null;
                    }
                    report(cancelSent ? "Cancel requested; waiting for Runtime to release USB."
                        : "Observe the current test image and changing seconds. Waiting for Runtime completion…");
                }
                else { uncertain = true; reportStatus?.Invoke(new(RuntimeUsbTestPhase.Missing));
                    report("Runtime outcome is unavailable; waiting for this exact attempt. Configuration remains unsaved."); }
                await _delay();
            }
            await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle));
            draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
            report("Test not passed: Runtime completion could not be confirmed. You may close this card; Runtime retains USB ownership until cleanup.");
            return null;
        }
        catch (Exception error)
        {
            if (!dispatched) draft.AbandonAttempt(intent);
            else
            {
                draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.CleanupPending);
                try { await _send(CatalogUsbTestIpcProtocol.BuildCancel(handle)); } catch { }
            }
            report(error is OperationCanceledException ? "Test canceled before dispatch. Configuration was not saved."
                : dispatched ? "Test not passed: Runtime outcome is unknown. Check its log; configuration was not saved." : error.Message);
            return null;
        }
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
