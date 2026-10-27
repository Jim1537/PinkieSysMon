using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105IpcLoggingTests
{
    private const string Private = "PRIVATE-IPC-PAYLOAD-SENTINEL";

    public static void SharedRequestAndOriginalError()
    {
        Sandbox(root =>
        {
            var runtime = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            var editor = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
            var pipe = Pipe(); var actions = 0;
            var observedRequest = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<RuntimeIpcResponse> Handler(string line, CancellationToken token)
            {
                if (line == RuntimeIpcProtocol.StatusCommand) return Task.FromResult(StatusReply(true));
                Interlocked.Increment(ref actions);
                var trace = RuntimeIpcTrace.Current ?? throw new InvalidOperationException("Missing server request.");
                observedRequest.TrySetResult(trace.Id);
                trace.Progress(runtime, "ui.executing");
                runtime.Error("Synthetic original native command failure.", new Win32Exception(5, "Synthetic native detail."));
                return Task.FromResult(new RuntimeIpcResponse(false, "Synthetic command failure."));
            }
            using (var server = new RuntimeIpcServer(Handler, runtime, pipe))
            {
                server.Start();
                using var client = new RuntimeIpcClient(editor, pipe);
                var response = client.AssignDashboardAsync("target-1", Private).GetAwaiter().GetResult();
                var id = observedRequest.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                Check(!response.Success && response.ResponseReceived && Volatile.Read(ref actions) == 1 && response.RequestId == id,
                    "New peers must share one request identity and dispatch exactly one command.");
                RuntimeIpc.SendCoreAsync(RuntimeIpcProtocol.StatusCommand, pipe, 5000).GetAwaiter().GetResult();
                var error = Read(runtime.ErrorsPath).Single(e => e.GetProperty("exceptionType").GetString() == typeof(Win32Exception).FullName);
                Check(error.GetProperty("ipcRequestId").GetString() == id.ToString("N") &&
                    error.GetProperty("ipcActor").GetString() == "Editor" && error.GetProperty("ipcTarget").GetString() == "target-1" &&
                    error.GetProperty("nativeCode").GetInt32() == 5 && response.ErrorEventId == error.GetProperty("eventId").GetString(),
                    "The response must identify the actually persisted original error with request/actor/verb/target context.");
                var reference = Read(runtime.Path).Single(e => e.GetProperty("eventId").GetString() == response.ErrorEventId);
                Check(reference.GetProperty("exception").ValueKind == JsonValueKind.Null &&
                    Read(editor.ErrorsPath).Any(e => e.GetProperty("component").GetString() == "Editor" &&
                        e.GetProperty("message").GetString()!.Contains(response.ErrorEventId!, StringComparison.Ordinal)) &&
                    Read(editor.ErrorsPath).Where(e => e.GetProperty("component").GetString() == "Editor")
                        .All(e => e.GetProperty("exception").ValueKind == JsonValueKind.Null),
                    "Editor links the Runtime error identity without duplicating its exception dump.");
            }
            Check(RuntimeIpcTrace.Current is null && !AllLogs(root).Contains(Private, StringComparison.Ordinal),
                "Request context must unwind and command/settings payloads must remain outside logs.");
        });
    }

    public static void LegacyCompatibilityAndEnvelopeAdmission()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            foreach (var advertised in new[] { false, true })
            {
                var pipe = Pipe(); var actions = new System.Collections.Concurrent.ConcurrentQueue<string>();
                using var server = new RuntimeIpcServer((line, _) =>
                {
                    if (line == RuntimeIpcProtocol.StatusCommand) return Task.FromResult(StatusReply(advertised));
                    actions.Enqueue(line); return Task.FromResult(new RuntimeIpcResponse(true, "legacy-visible-message"));
                }, log, pipe);
                server.Start();
                using var client = new RuntimeIpcClient(new FileLogger(Path.Combine(root, "editor.log"), "Editor"), pipe);
                var result = client.ReloadAsync().GetAwaiter().GetResult();
                var legacy = RuntimeIpc.SendCoreAsync(RuntimeIpcProtocol.StopAllCommand, pipe).GetAwaiter().GetResult();
                Check(result.Success && result.ResponseReceived && (result.RequestId is not null) == advertised &&
                    result.Message == "legacy-visible-message" && legacy.Success && legacy.RequestId is null &&
                    legacy.Message == "legacy-visible-message" && actions.SequenceEqual(new[] { "RELOAD", "OUTPUT_STOP_ALL" }),
                    "Advertised correlation and both legacy directions must preserve one action, verb and user-visible reply.");
            }
            var command = RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, "target-1");
            var trace = new RuntimeIpcTrace(Guid.NewGuid(), "Editor", RuntimeIpcProtocol.ParseCommand(command));
            var valid = RuntimeIpcDiagnostics.Wrap(command, trace);
            var dispatched = 0;
            foreach (var bad in new[] { "IPC_REQUEST_V1\t{}", "IPC_REQUEST_V2\t" + Private,
                valid.Replace("\"Actor\":\"Editor\"", "\"Actor\":\"Untrusted\"", StringComparison.Ordinal),
                valid.Replace(trace.Id.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal),
                "IPC_REQUEST_V1\t" + JsonSerializer.Serialize(new { RequestId = trace.Id, Actor = "Editor", Command = valid }),
                "IPC_REQUEST_V1\t" + JsonSerializer.Serialize(new { RequestId = trace.Id, Actor = "Editor", Command = "PING", Extra = Private }),
                "IPC_REQUEST_V1\t" + new string('x', RuntimeIpcRequestReader.MaxCommandCharacters) })
            {
                var result = RuntimeIpcDiagnostics.DispatchAsync(bad, (_, _) => { dispatched++; return Task.FromResult(new RuntimeIpcResponse(true, "unexpected")); },
                    log, CancellationToken.None).GetAwaiter().GetResult();
                Check(!result.Success && !result.Message.Contains(Private, StringComparison.Ordinal), "Malformed/nested/extended envelopes fail closed without payload echo.");
            }
            Check(dispatched == 0, "Rejected metadata must not invoke the handler or mutate settings.");
            foreach (var reply in new[] {
                JsonSerializer.Serialize(new { RequestId = Guid.NewGuid(), Success = true, Message = "wrong", ErrorEventId = (string?)null }),
                JsonSerializer.Serialize(new { RequestId = trace.Id, Success = false, Message = "wrong", ErrorEventId = (string?)null }),
                JsonSerializer.Serialize(new { RequestId = trace.Id, Success = true, Message = "wrong", ErrorEventId = "not-an-event-id" }) })
            {
                try { RuntimeIpcDiagnostics.Unwrap(new(true, reply) { ResponseReceived = true }, trace); throw new InvalidOperationException("Invalid reply admitted."); }
                catch (InvalidDataException) { }
            }
        });
    }

    public static void LostResponseNeverRepeatsCommand()
    {
        Sandbox(root =>
        {
            foreach (var mode in new[] { "lost", "cancel" })
            {
                var pipe = Pipe(); var actions = 0;
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<RuntimeIpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancellation = new CancellationTokenSource();
                using var server = new RuntimeIpcServer((line, _) =>
                {
                    if (line == RuntimeIpcProtocol.StatusCommand) return Task.FromResult(StatusReply(true));
                    Interlocked.Increment(ref actions); entered.TrySetResult();
                    if (mode == "lost") throw new IOException("Synthetic failure after command admission.");
                    return release.Task;
                }, new FileLogger(Path.Combine(root, "runtime.log"), "Runtime"), pipe);
                server.Start();
                using var client = new RuntimeIpcClient(new FileLogger(Path.Combine(root, "editor.log"), "Editor"), pipe);
                var sending = client.SendAsync(RuntimeIpcProtocol.ReloadCommand, 5000, cancellation.Token);
                try
                {
                    entered.Task.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
                    if (mode == "cancel") cancellation.Cancel();
                    var result = sending.GetAwaiter().GetResult();
                    Check(!result.Success && !result.ResponseReceived && Volatile.Read(ref actions) == 1 &&
                        result.Message.Contains("may have been applied", StringComparison.Ordinal),
                        "A lost/canceled response after receipt stays unknown and must never cause legacy fallback or resend.");
                }
                finally { release.TrySetResult(new(true, "released")); }
            }
        });
    }

    public static void ContextIsolationAndDeferredFeedback()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            var trace = new RuntimeIpcTrace(Guid.NewGuid(), "Editor", new(RuntimeIpcCommandKind.StartOrReloadTarget, "target-1"));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> child;
            using (RuntimeIpcTrace.Enter(trace))
            {
                child = Task.Run(async () => { await release.Task.ConfigureAwait(false); return RuntimeIpcTrace.Current is null; });
                using (RuntimeIpcTrace.Enter(new(Guid.NewGuid(), "Editor", new(RuntimeIpcCommandKind.StopAll))))
                    Check(RuntimeIpcTrace.Current?.Id != trace.Id, "Nested request context must be distinct.");
                Check(RuntimeIpcTrace.Current == trace, "Unwinding a nested scope restores the caller.");
                log.Error("Synthetic first request error."); var first = trace.ErrorEventId;
                log.Error("Synthetic second request error.");
                Check(first is not null && trace.ErrorEventId == first, "Reply identity must name the first persisted error, not an arbitrary later record.");
            }
            release.TrySetResult();
            Check(child.GetAwaiter().GetResult() && RuntimeIpcTrace.Current is null, "Forked long-lived workers must not inherit a disposed request attribution.");
            var before = Snapshot(false); var after = Snapshot(true); var current = before;
            var feedback = new RuntimeCommandFeedback((_, _) => { }, e => log.Error("Notification", e),
                e => log.Error("Command", e), (_, _) => log.Info("Synthetic deferred feedback."));
            using (RuntimeIpcTrace.Enter(trace))
                Check(feedback.Execute(trace.Command, () => { current = after; return new(true, "accepted"); }, () => current).Success,
                    "The existing pending-readiness policy must remain intact.");
            feedback.Observe(new("board", [after.Outputs[0] with { IsConnected = true, UsbState = "CONNECTED" }]));
            log.Info("Outside request.");
            var records = Read(log.Path);
            Check(records.Count(e => e.GetProperty("message").GetString() == "Synthetic deferred feedback." &&
                e.GetProperty("ipcRequestId").GetString() == trace.Id.ToString("N")) == 2 &&
                records.Last().GetProperty("ipcRequestId").ValueKind == JsonValueKind.Null,
                "Deferred terminal feedback re-enters only its captured request and restores unrelated logging.");
            feedback.Clear();
            var sharedError = new IOException("Synthetic reused request exception.");
            var next = new RuntimeIpcTrace(Guid.NewGuid(), "Editor", new(RuntimeIpcCommandKind.ReloadConfiguration));
            using (RuntimeIpcTrace.Enter(trace)) log.Error("Same failure at another request boundary.", sharedError);
            using (RuntimeIpcTrace.Enter(next)) log.Error("Same failure at another request boundary.", sharedError);
            var repeated = Read(log.ErrorsPath).Where(e => e.GetProperty("message").GetString() ==
                "Same failure at another request boundary.").ToArray();
            Check(repeated.Length == 2 && repeated.Select(e => e.GetProperty("eventId").GetString()).Distinct().Count() == 1 &&
                repeated.Count(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) == 1 &&
                repeated.Select(e => e.GetProperty("ipcRequestId").GetString()).Distinct().Count() == 2,
                "A reused exception keeps one dump/error identity but persists each distinct request context.");
        });
    }

    public static void CountedStatusAndObserverIsolation()
    {
        Sandbox(root =>
        {
            var pipe = Pipe(); var log = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
            long now = 0; var changed = 0;
            using var server = new RuntimeIpcServer((line, _) => Task.FromResult(new RuntimeIpcResponse(true,
                RuntimeIpcProtocol.SerializeStatus(Snapshot(Volatile.Read(ref changed) != 0)))), new FileLogger(Path.Combine(root, "runtime.log"), "Runtime"), pipe);
            server.Start();
            using var client = new RuntimeIpcClient(log, pipe, () => now);
            Check(client.GetStatusAsync(5000).GetAwaiter().GetResult().Success, "First status must be available.");
            for (var i = 0; i < 10; i++) Check(client.GetStatusAsync().GetAwaiter().GetResult().Success, "Healthy unchanged status remains successful.");
            Check(StatusEvents(log.Path).Length == 1, "Healthy unchanged polls must not write one record per request.");
            now = Stopwatch.Frequency * 31L;
            client.GetStatusAsync().GetAwaiter().GetResult();
            Check(StatusEvents(log.Path).Last().GetProperty("unchangedSinceRecord").GetInt64() == 11, "Counted summary retains every unchanged check.");
            Volatile.Write(ref changed, 1); client.GetStatusAsync().GetAwaiter().GetResult();
            Check(StatusEvents(log.Path).Last().GetProperty("phase").GetString() == "changed", "Meaningful status changes cannot wait for the summary interval.");
            using var faulty = new RuntimeIpcClient(log, pipe, () => throw new IOException("Synthetic diagnostic clock failure."));
            Check(faulty.GetStatusAsync().GetAwaiter().GetResult().Success &&
                ReadText(log.ErrorsPath).Contains("Synthetic diagnostic clock failure.", StringComparison.Ordinal),
                "Observer failure is retained without changing a successful status response.");
            client.GetStatusAsync().GetAwaiter().GetResult();
            client.Dispose();
            Check(StatusEvents(log.Path).Last().GetProperty("phase").GetString() == "final" &&
                StatusEvents(log.Path).Last().GetProperty("unchangedSinceRecord").GetInt64() == 1,
                "Owner close must retain the final unchanged-response total without waiting for another interval.");
            faulty.Dispose();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<RuntimeIpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var latePipe = Pipe();
            using var lateServer = new RuntimeIpcServer((_, _) => { entered.TrySetResult(); return release.Task; },
                new FileLogger(Path.Combine(root, "late-runtime.log"), "Runtime"), latePipe);
            lateServer.Start();
            var lateLog = new FileLogger(Path.Combine(root, "late-editor.log"), "Editor");
            using var late = new RuntimeIpcClient(lateLog, latePipe);
            var pending = late.GetStatusAsync(5000);
            try
            {
                entered.Task.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
                late.Dispose();
                Check(!pending.IsCompleted, "Closing diagnostics must not join or complete the underlying status read.");
                var before = ReadText(lateLog.Path);
                release.TrySetResult(StatusReply(true));
                Check(pending.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult().Success &&
                    ReadText(lateLog.Path) == before, "A late successful status response must not write after observer disposal.");
            }
            finally { late.Dispose(); release.TrySetResult(StatusReply(true)); }
            Check(!AllLogs(root).Contains(Private, StringComparison.Ordinal), "Status summaries omit response names/dashboard content.");
        });
    }

    private static RuntimeStatusSnapshot Snapshot(bool active) => new(Private, [new("target-1", Private, active, false, "CONNECTING")
        { Kind = OutputTargetContract.PhysicalKind, Dashboard = Private, OutputEnabled = true, DeviceEnabled = true }]);
    private static RuntimeIpcResponse StatusReply(bool advertise) => new(true,
        RuntimeIpcProtocol.SerializeStatus(new RuntimeStatusSnapshot("board", []) { IpcDiagnosticsVersion = advertise ? RuntimeIpcDiagnostics.Version : null }));
    private static string Pipe() => "pinkie-issue105-ipc-" + Guid.NewGuid().ToString("N");
    private static string AllLogs(string root) => string.Join("\n", Directory.GetFiles(root, "*.log").Select(ReadText));
    private static string ReadText(string path) => LogFileWriter.UnderGate(path, "write",
        () => File.Exists(path) ? File.ReadAllText(path) : string.Empty);
    private static JsonElement[] Read(string path) => ReadText(path)
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line =>
        { using var json = JsonDocument.Parse(line[line.IndexOf('{')..]); return json.RootElement.Clone(); }).ToArray();
    private static JsonElement[] StatusEvents(string path) => Read(path).Select(e => e.GetProperty("message").GetString())
        .Where(m => m?.StartsWith('{') == true).Select(m => { using var json = JsonDocument.Parse(m!); return json.RootElement.Clone(); })
        .Where(e => e.TryGetProperty("eventName", out var name) && name.GetString() == "editor.ipc.status").ToArray();
    private static void Sandbox(Action<string> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-ipc-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            work(root);
            Check(Directory.GetFiles(root, "*.log").SelectMany(Read)
                .All(e => e.GetProperty("loggingFailures").GetInt32() == 0),
                "IPC fixtures must not conceal unconfirmed log writes.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
