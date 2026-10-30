using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon;

// One request can cross the pipe worker, UI dispatch and later feedback.
// Scopes restore the prior context. Their shared error slot retains only the
// first persisted error identity, never an exception object.
internal sealed class RuntimeIpcTrace(Guid id, string actor, RuntimeIpcCommand command)
{
    private static readonly AsyncLocal<Scope?> Slot = new();
    private string? _errorEventId;
    private readonly long _started = Stopwatch.GetTimestamp();
    internal static RuntimeIpcTrace? Current => Slot.Value?.Trace;
    internal Guid Id { get; } = id;
    internal string Actor { get; } = actor;
    internal RuntimeIpcCommand Command { get; } = command;
    internal string? ErrorEventId => Volatile.Read(ref _errorEventId);
    internal void ObserveError(string eventId) => Interlocked.CompareExchange(ref _errorEventId, eventId, null);
    internal static IDisposable Enter(RuntimeIpcTrace? trace) => new Scope(trace);
    internal void Progress(FileLogger log, string phase) => log.Info(JsonSerializer.Serialize(new
    {
        eventName = "runtime.ipc.progress", requestId = Id.ToString("N"), actor = Actor,
        verb = Command.Kind.ToString(), target = BoundTarget(Command.TargetId), phase,
        elapsedMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds
    }));
    internal static string? BoundTarget(string? target) => target is null ? null : target.Length <= 128 ? target : target[..128] + " [TRUNCATED]";
    private sealed class Scope : IDisposable
    {
        private readonly Scope? _previous = Slot.Value;
        private RuntimeIpcTrace? _trace;
        internal RuntimeIpcTrace? Trace => Volatile.Read(ref _trace);
        internal Scope(RuntimeIpcTrace? trace) { _trace = trace; Slot.Value = this; }
        public void Dispose()
        {
            // Forked background workers must not keep attributing unrelated
            // future errors to the request that originally started them.
            Interlocked.Exchange(ref _trace, null);
            Slot.Value = _previous;
        }
    }
}

internal static class RuntimeIpcDiagnostics
{
    internal const int Version = 1;
    internal const string RequestVerb = "IPC_REQUEST_V1";
    private sealed record Request([property: JsonRequired] Guid RequestId, [property: JsonRequired] string Actor, [property: JsonRequired] string Command);
    private sealed record Reply([property: JsonRequired] Guid RequestId, [property: JsonRequired] bool Success, [property: JsonRequired] string Message, string? ErrorEventId);
    private static readonly JsonSerializerOptions Strict = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static bool IsOrdinaryCommand(RuntimeIpcCommandKind kind) => kind is
        RuntimeIpcCommandKind.ReloadConfiguration or RuntimeIpcCommandKind.Exit or
        RuntimeIpcCommandKind.StartOrReloadTarget or RuntimeIpcCommandKind.StartOrReloadAll or
        RuntimeIpcCommandKind.StopAll or RuntimeIpcCommandKind.PreparePhysicalRemoval or
        RuntimeIpcCommandKind.AssignDashboard or RuntimeIpcCommandKind.SetDesktopBoardEnabled or RuntimeIpcCommandKind.SetDesktopLock;

    internal static string Wrap(string command, RuntimeIpcTrace trace)
    {
        var line = RequestVerb + "\t" + JsonSerializer.Serialize(new Request(trace.Id, trace.Actor, command));
        if (line.Length > RuntimeIpcRequestReader.MaxCommandCharacters)
            throw new InvalidDataException("Correlated IPC request exceeds the existing input limit.");
        return line;
    }

    internal static RuntimeIpcResponse Unwrap(RuntimeIpcResponse response, RuntimeIpcTrace trace)
    {
        var reply = JsonSerializer.Deserialize<Reply>(response.Message, Strict);
        if (!response.ResponseReceived || reply is null || reply.RequestId != trace.Id || reply.Success != response.Success ||
            reply.Message is null || (reply.ErrorEventId is not null && (!Guid.TryParseExact(reply.ErrorEventId, "N", out var eventId) || eventId == Guid.Empty)))
            throw new InvalidDataException("Runtime returned an invalid IPC correlation response.");
        return new(reply.Success, reply.Message) { ResponseReceived = true, RequestId = trace.Id, ErrorEventId = reply.ErrorEventId };
    }

    internal static async Task<RuntimeIpcResponse> DispatchAsync(string line,
        Func<string, CancellationToken, Task<RuntimeIpcResponse>> handler, FileLogger log,
        CancellationToken cancellationToken, Func<RuntimeIpcResponse, Task>? write = null)
    {
        var correlated = line.StartsWith("IPC_REQUEST_", StringComparison.OrdinalIgnoreCase);
        RuntimeIpcTrace? trace = null;
        if (correlated)
        {
            try
            {
                if (!line.StartsWith(RequestVerb + "\t", StringComparison.OrdinalIgnoreCase) ||
                    line.Length > RuntimeIpcRequestReader.MaxCommandCharacters || line.Contains('\r') || line.Contains('\n'))
                    throw new InvalidDataException();
                var request = JsonSerializer.Deserialize<Request>(line[(RequestVerb.Length + 1)..], Strict);
                if (request is null || request.RequestId == Guid.Empty || request.Actor != "Editor" || request.Command is null ||
                    request.Command.Contains('\r') || request.Command.Contains('\n')) throw new InvalidDataException();
                var command = RuntimeIpcProtocol.ParseCommand(request.Command);
                if (!IsOrdinaryCommand(command.Kind)) throw new InvalidDataException();
                trace = new(request.RequestId, request.Actor, command);
                line = request.Command;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
            {
                // No payload echo and no handler invocation on malformed or nested envelopes.
                log.WarnThrottled("ipc.envelope.rejected", TimeSpan.FromSeconds(30),
                    "Correlated IPC request refused before dispatch: invalid metadata or unsupported command.");
                var rejected = new RuntimeIpcResponse(false, "Invalid correlated IPC request.");
                if (write is not null) await write(rejected).ConfigureAwait(false);
                return rejected;
            }
        }
        else
        {
            var command = RuntimeIpcProtocol.ParseCommand(line);
            if (IsOrdinaryCommand(command.Kind)) trace = new(Guid.NewGuid(), "LegacyClient", command);
        }

        using var context = RuntimeIpcTrace.Enter(trace);
        using var operation = trace is null ? null : new LogOperation(log, "runtime.ipc.command",
            RuntimeIpcTrace.BoundTarget(trace.Command.TargetId), trace.Id.ToString("N"));
        try
        {
            trace?.Progress(log, "received");
            operation?.Phase("dispatch");
            var response = await handler(line, cancellationToken).ConfigureAwait(false);
            trace?.Progress(log, "handler.returned");
            var wire = correlated ? response with { Message = JsonSerializer.Serialize(new Reply(trace!.Id,
                response.Success, response.Message.Replace('\r', ' ').Replace('\n', ' '), trace.ErrorEventId)) } : response;
            if (write is not null)
            {
                operation?.Phase("reply.write", "handlerReturned=true; visibleOutputNotAsserted=true");
                await write(wire).ConfigureAwait(false);
            }
            var detail = $"actor={trace?.Actor}; verb={trace?.Command.Kind}; responseSuccess={response.Success}; errorEventId={trace?.ErrorEventId}; replyOnly=true; visibleOutputNotAsserted=true";
            if (response.Success) operation?.Complete(detail);
            else if (trace?.ErrorEventId is not null) operation?.Partial(detail);
            else operation?.Cancel(detail);
            return wire;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Cancel("serverStopping=true; dispatchOrReleaseCompletionNotAsserted=true");
            throw;
        }
        catch (Exception error)
        {
            operation?.Fail(error, $"verb={trace?.Command.Kind}; dispatchOrReplyFailed=true; commandNotRetried=true");
            throw;
        }
    }
}
