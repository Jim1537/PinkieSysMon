using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

// Synthetic candidates and a unique test pipe; no production endpoint, process,
// device, dashboard, configuration writer or native transport is used.
internal static class Issue105UsbWorkflowLoggingTests
{
    public static void EditorPhasesAndVisualOutcome()
    {
        foreach (var mode in new[] { "yes", "no", "lost-begin", "lost-status", "cancel", "rejected", "cleanup", "secondary", "limit" })
            Sandbox(root =>
            {
                var seed = Request(); var snapshot = Snapshot(seed.Intent);
                var draft = new CatalogUsbCandidateDraft(snapshot.Candidates[0], Private, [seed.Intent.Protocol])
                { Width = 320, Height = 240, Protocol = seed.Intent.Protocol };
                var log = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
                using var cancellation = new CancellationTokenSource();
                CatalogUsbTestHandle? handle = null;
                var begins = 0; var statuses = 0; var cancels = 0;
                Task<RuntimeIpcResponse> Send(string line)
                {
                    var command = RuntimeIpcProtocol.ParseCommand(line);
                    if (command.Kind == RuntimeIpcCommandKind.Ping) return Task.FromResult(new RuntimeIpcResponse(true, "pong"));
                    if (command.Kind == RuntimeIpcCommandKind.UsbTestBegin)
                    {
                        begins++;
                        var request = command.UsbTestBegin!;
                        handle = new(request.ClientId, request.Intent.AttemptId);
                        if (mode == "cancel") cancellation.Cancel();
                        if (mode is "lost-begin" or "rejected")
                            return Task.FromResult(new RuntimeIpcResponse(false, "Synthetic unavailable/rejected") { ResponseReceived = mode == "rejected" });
                        return Task.FromResult(Reply(handle, new(RuntimeUsbTestPhase.Connecting)));
                    }
                    Check(command.UsbTestHandle == handle, "Every status/cancel must retain the original attempt and client.");
                    if (command.Kind == RuntimeIpcCommandKind.UsbTestCancel)
                    {
                        cancels++;
                        if (mode == "secondary") throw new IOException("Synthetic secondary cancel failure.");
                        return Task.FromResult(Reply(handle!, new(RuntimeUsbTestPhase.CancelRequested)));
                    }
                    statuses++;
                    if (mode == "secondary") throw new Win32Exception(5, "Synthetic primary status failure.");
                    if (mode == "cleanup") return Task.FromResult(Reply(handle!, new(RuntimeUsbTestPhase.CleanupPending)));
                    if (mode == "lost-status" && statuses is 2 or 3)
                        return Task.FromResult(new RuntimeIpcResponse(false, "Synthetic lost status."));
                    return Task.FromResult(Reply(handle!, mode == "limit" ? new(RuntimeUsbTestPhase.Connecting) : statuses <= 20
                        ? new(RuntimeUsbTestPhase.Observing, SecondsRemaining: 21 - statuses)
                        : new(RuntimeUsbTestPhase.SucceededAndReleased, CatalogUsbTestTechnicalOutcome.SucceededAndReleased)));
                }
                var client = new EditorUsbTestClient(Send, () => false,
                    () => throw new InvalidOperationException("Live Runtime must not be relaunched."), () => Task.CompletedTask, log);
                var intent = client.TestAsync(draft, snapshot, _ => { }, cancellation.Token).GetAwaiter().GetResult();
                Check(begins == 1 && !draft.CanSaveConfiguration && draft.IsTesting == (mode is "cleanup" or "secondary" or "limit"),
                    "Logging must not resend Begin, persist anything or turn technical completion into visual approval.");
                Check((intent is not null) == (mode is "yes" or "no"), "Uncertainty, cancellation and pending cleanup cannot pass.");
                if (intent is not null)
                {
                    draft.ConfirmVisibleChangingSeconds(intent, mode == "yes");
                    EditorUsbTestClient.RecordVisualOutcome(log, intent, mode == "yes", draft.CanSaveConfiguration);
                    Check(draft.CanSaveConfiguration == (mode == "yes"), "Only a separate visual Yes may unlock local Save eligibility.");
                }
                var session = Read(log.Path); var errors = Read(log.ErrorsPath);
                var operations = Operations(session.Concat(errors), "editor.usb-test");
                Check(operations.Length > 0 && operations.All(e => e.GetProperty("parentOperationId").GetString() == handle!.AttemptId.ToString("N")),
                    "Editor operation and error records must share the dispatched attempt identity.");
                Check(operations.Count(e => e.GetProperty("phase").GetString() == "status.Observing" &&
                    e.GetProperty("outcome").GetString() == "progress") <= 1,
                    "Countdown changes must not produce per-poll progress records.");
                if (mode == "lost-status")
                    Check(operations.Count(e => e.GetProperty("phase").GetString() == "status.unavailable") == 1 &&
                        operations.Any(e => e.GetProperty("phase").GetString() == "status.availableAgain") &&
                        operations.Any(e => Detail(e).Contains("acceptedOutcome=FailedAndReleased", StringComparison.Ordinal)),
                        "One unavailable interval and its recovery must retain strict failure even after Runtime success.");
                if (mode == "limit")
                    Check(statuses == 200 && cancels == 1 && operations.Any(e => e.GetProperty("phase").GetString() == "completion.unconfirmed"),
                        "The existing observation limit requests Cancel once and retains pending ownership.");
                if (mode == "secondary")
                    Check(cancels == 1 && errors.Any(e => e.GetProperty("nativeCode").ValueKind == JsonValueKind.Number && e.GetProperty("nativeCode").GetInt32() == 5) &&
                        File.ReadAllText(log.ErrorsPath).Contains("Synthetic secondary cancel failure.", StringComparison.Ordinal),
                        "Original native failure and secondary cancel failure must both survive.");
                if (intent is not null)
                {
                    var visual = Operations(session, "editor.usb-test.visual");
                    Check(visual.Length == 2 && visual.All(e => e.GetProperty("parentOperationId").GetString() == intent.AttemptId.ToString("N")) &&
                        visual.Any(e => Detail(e).Contains("configurationSaved=false", StringComparison.Ordinal)),
                        "Visual Yes/No evidence is separate from technical completion and file persistence.");
                }
                Check(!string.Join("\n", Directory.GetFiles(root).Select(File.ReadAllText)).Contains(Private, StringComparison.Ordinal),
                    "Routine workflow evidence must exclude candidate paths and user names.");
            });
    }

    public static void RuntimeAdmissionAndWorkerFailures()
    {
        foreach (var mode in new[] { "admission", "worker" }) Sandbox(root =>
        {
            var request = Request(); var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            var failure = new Win32Exception(5, "Synthetic Runtime workflow failure.");
            var service = new RuntimeUsbTestService(
                () => mode == "admission" ? throw failure : Snapshot(request.Intent), (_, _) => false,
                _ => throw failure, log);
            var response = service.Execute(new(RuntimeIpcCommandKind.UsbTestBegin, UsbTestBegin: request));
            if (mode == "worker")
            {
                Check(response.Success && SpinWait.SpinUntil(() => File.Exists(log.Path) &&
                    File.ReadAllText(log.Path).Contains("runtime.usb-test.worker.partial", StringComparison.Ordinal), 5000),
                    "Worker failure must finish with evidence after releasing its unused reservation.");
                log.Info("Test observation fence.");
                for (var i = 0; i < 25; i++)
                {
                    var status = service.Execute(new(RuntimeIpcCommandKind.UsbTestStatus, UsbTestHandle: new(request.ClientId, request.Intent.AttemptId)));
                    Check(JsonSerializer.Deserialize<CatalogUsbTestReply>(status.Message)!.Status.TechnicalOutcome ==
                        CatalogUsbTestTechnicalOutcome.FailedAndReleased, "Diagnostic status polling must not change the failed result.");
                }
                Check(!service.HasActive, "Factory failure must not leave an acquired native transport or reservation.");
            }
            else Check(!response.Success, "Discovery failure remains an admission rejection.");
            var session = Read(log.Path); var errors = Read(log.ErrorsPath);
            var records = Operations(session.Concat(errors), mode == "worker" ? "runtime.usb-test.worker" : "runtime.usb-test.request");
            Check(records.Length > 0 && records.All(e => e.GetProperty("parentOperationId").GetString() == request.Intent.AttemptId.ToString("N")),
                "Runtime uses the same existing cross-process attempt as the Editor.");
            Check(errors.Any(e => e.GetProperty("exceptionType").GetString() == typeof(Win32Exception).FullName &&
                e.GetProperty("nativeCode").GetInt32() == 5 && e.GetProperty("hResult").GetString() is not null),
                "Admission and worker errors retain the actual exception and native details.");
            Check(!Operations(session.Concat(errors), "runtime.usb-test.request").Any(e => Detail(e).Contains("reason=status", StringComparison.Ordinal)),
                "Ordinary successful USB status polling must not emit request operations.");
            var errorId = errors.First(e => e.GetProperty("exceptionType").ValueKind == JsonValueKind.String).GetProperty("eventId").GetString();
            Check(session.Any(e => e.GetProperty("eventId").GetString() == errorId && e.GetProperty("exception").ValueKind == JsonValueKind.Null),
                "Runtime session references the full Errors record without copying the exception.");
        });
    }

    public static void TransportUnknownReplyAndOriginalException()
    {
        foreach (var mode in new[] { "empty", "invalid", "rejected", "canceled" }) Sandbox(root =>
        {
            var request = Request(); var log = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
            var command = CatalogUsbTestIpcProtocol.BuildBegin(request.ClientId, request.Intent);
            var reply = Exchange(command, mode == "invalid" ? "BAD\tignored" : mode == "rejected" ? "ERR\tdeclined" : "",
                log, cancelAfterRead: mode == "canceled").GetAwaiter().GetResult();
            Check(!reply.Success && reply.ResponseReceived == (mode == "rejected"), "Logging preserves explicit rejection versus unconfirmed response.");
            if (mode != "rejected") Check(reply.Message.Contains("may have been applied", StringComparison.Ordinal) || mode == "invalid",
                "A lost response after write retains the unknown-after-send warning.");
            var errors = Read(log.ErrorsPath);
            var operations = Operations(Read(log.Path).Concat(errors), "editor.ipc.usb-test");
            Check(operations.All(e => e.GetProperty("parentOperationId").GetString() == request.Intent.AttemptId.ToString("N")),
                "Transport evidence must retain the exact attempt without a new wire envelope.");
            Check(mode == "rejected" ? errors.Length == 0 : errors.Length > 0,
                "Expected admission rejection stays distinct from transport/protocol failure.");
            if (mode == "canceled") Check(errors.Any(e =>
                e.GetProperty("exceptionType").GetString()?.Contains("OperationCanceledException", StringComparison.Ordinal) == true ||
                e.GetProperty("exceptionType").GetString()?.Contains("TaskCanceledException", StringComparison.Ordinal) == true) &&
                operations.Any(e => e.GetProperty("phase").GetString() == "response" && e.GetProperty("outcome").GetString() == "failed"),
                "Cancellation after the server reads the command retains the actual response-phase exception.");
        });
    }

    public static void TransportQuietPollingAndRecoveryTotals()
    {
        Sandbox(root =>
        {
            var request = Request(); var handle = new CatalogUsbTestHandle(request.ClientId, request.Intent.AttemptId);
            var log = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
            foreach (var command in new[] { RuntimeIpcProtocol.PingCommand, RuntimeIpcProtocol.StatusCommand, CatalogUsbTestIpcProtocol.BuildStatus(handle) })
                for (var i = 0; i < 5; i++)
                    Check(Exchange(command, "OK\t" + Private, log).GetAwaiter().GetResult().Success, "Read-only reply remains successful.");
            Check(!File.Exists(log.Path) && !File.Exists(log.ErrorsPath), "Healthy polling and response payloads must not enter the log.");
            using var stopped = new CancellationTokenSource(); stopped.Cancel();
            var commandLine = CatalogUsbTestIpcProtocol.BuildStatus(handle);
            var unusedPipe = "pinkie-issue105-" + Guid.NewGuid().ToString("N");
            for (var i = 0; i < 10; i++)
                Check(!RuntimeIpc.SendCoreAsync(commandLine, unusedPipe, cancellationToken: stopped.Token, log: log)
                    .GetAwaiter().GetResult().Success, "An unavailable read-only endpoint cannot become success.");
            Check(Read(log.ErrorsPath).Length == 1, "Repeated identical status failures retain one full exception during the throttle interval.");
            Check(Exchange(commandLine, "OK\t" + Private, log).GetAwaiter().GetResult().Success, "Recognized reply closes only that status incident.");
            Check(File.ReadAllText(log.ErrorsPath).Contains("suppressed=9", StringComparison.Ordinal) &&
                !File.ReadAllText(log.ErrorsPath).Contains(Private, StringComparison.Ordinal),
                "Recovery retains suppressed totals without recording response/configuration payloads.");
        });
    }

    private static async Task<RuntimeIpcResponse> Exchange(string command, string reply, FileLogger log, bool cancelAfterRead = false)
    {
        var pipeName = "pinkie-issue105-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var canceled = new CancellationTokenSource();
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var clientDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Serve()
        {
            await server.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            Check(await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false) == command, "The test server must receive the original command exactly once.");
            if (cancelAfterRead) { canceled.Cancel(); await clientDone.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
            else await writer.WriteLineAsync(reply.AsMemory(), deadline.Token).ConfigureAwait(false);
        }
        var serving = Serve();
        try { return await RuntimeIpc.SendCoreAsync(command, pipeName, 5000, canceled.Token, log).ConfigureAwait(false); }
        finally { clientDone.TrySetResult(); await serving.ConfigureAwait(false); }
    }

    private const string Private = "PRIVATE-USB-WORKFLOW-SENTINEL";
    private static CatalogUsbTestBeginRequest Request() => new(Guid.NewGuid(), new(Guid.NewGuid(),
        "usb-interface:" + new string('a', 64), "usb-pnp:" + new string('b', 64), OutputTargetContract.LyBulkTransport, 320, 240, 0));
    private static CatalogUsbCandidateSnapshot Snapshot(CatalogUsbTrialIntent intent) => new([
        new(intent.InterfaceId, UsbDisplayCatalog.Find(new(0x0416, 0x5408))!, Private, intent.PnpNodeKey, Private)], []);
    private static RuntimeIpcResponse Reply(CatalogUsbTestHandle handle, RuntimeUsbTestStatus status) =>
        new(true, JsonSerializer.Serialize(new CatalogUsbTestReply(handle, status)));
    private static string Detail(JsonElement e) => e.GetProperty("detail").GetString() ?? "";
    private static JsonElement[] Read(string path) => !File.Exists(path) ? [] : File.ReadAllLines(path).Select(line =>
    {
        using var json = JsonDocument.Parse(line[line.IndexOf('{')..]); return json.RootElement.Clone();
    }).ToArray();
    private static JsonElement[] Operations(IEnumerable<JsonElement> records, string operation) => records
        .Select(e => e.GetProperty("message").GetString()).Where(message => message?.StartsWith('{') == true)
        .Select(message => { using var json = JsonDocument.Parse(message!); return json.RootElement.Clone(); })
        .Where(e => e.GetProperty("operation").GetString() == operation).ToArray();
    private static void Sandbox(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-usb-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
