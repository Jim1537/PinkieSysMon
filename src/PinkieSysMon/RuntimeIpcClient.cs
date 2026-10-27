using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon;

// Editor-side client. Capability discovery is read-only; the actual command is
// sent exactly once, in the advertised format or the unchanged legacy format.
internal sealed class RuntimeIpcClient(FileLogger? log, string? pipeName = null, Func<long>? timestamp = null) : IDisposable
{
    private readonly string _pipeName = pipeName ?? RuntimeIpc.PipeName;
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    // Diagnostic-only gate: never held for network I/O or application state.
    private readonly object _statusSync = new();
    private readonly string _observerId = Guid.NewGuid().ToString("N");
    private string? _statusFingerprint;
    private bool? _available;
    private long _lastStatusRecord;
    private long _checks, _unchanged, _unavailable;
    private bool _closed;

    internal async Task<RuntimeIpcResponse> SendAsync(string command, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var logger = log;
        var parsed = RuntimeIpcProtocol.ParseCommand(command);
        if (logger is null || !RuntimeIpcDiagnostics.IsOrdinaryCommand(parsed.Kind))
            return await RuntimeIpc.SendCoreAsync(command, _pipeName, timeoutMs, cancellationToken).ConfigureAwait(false);
        var trace = new RuntimeIpcTrace(Guid.NewGuid(), "Editor", parsed);
        var status = await GetStatusAsync(Math.Min(750, Math.Max(100, timeoutMs)), cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            using var context = RuntimeIpcTrace.Enter(trace);
            using var operation = new LogOperation(logger, "editor.ipc.command", RuntimeIpcTrace.BoundTarget(parsed.TargetId), trace.Id.ToString("N"));
            operation.Cancel("callerCancelledDuringCapabilityCheck=true; commandNotDispatched=true");
            return new(false, RuntimeIpc.DescribeUnavailableResponse(false, "IPC request was canceled before command dispatch."));
        }
        var correlated = status.Success && status.Status?.IpcDiagnosticsVersion == RuntimeIpcDiagnostics.Version;
        string line;
        try { line = correlated ? RuntimeIpcDiagnostics.Wrap(command, trace) : command; }
        catch (InvalidDataException error)
        {
            using var context = RuntimeIpcTrace.Enter(trace);
            logger.Error("IPC command framing failed before dispatch; no command write was attempted.", error);
            return new(false, RuntimeIpc.DescribeUnavailableResponse(false, "IPC command framing failed."));
        }
        // Never fall back or resend after this write, including explicit errors
        // from a Runtime replaced between discovery and command dispatch.
        return await RuntimeIpc.SendCoreAsync(line, _pipeName, timeoutMs, cancellationToken, logger, trace, correlated).ConfigureAwait(false);
    }

    internal async Task<RuntimeIpcStatusResponse> GetStatusAsync(int timeoutMs = 750, CancellationToken cancellationToken = default)
    {
        var response = await RuntimeIpc.SendCoreAsync(RuntimeIpcProtocol.StatusCommand, _pipeName, timeoutMs, cancellationToken, log,
            observeStatusFailure: ObserveTransportFailure).ConfigureAwait(false);
        if (!response.Success)
        {
            ObserveStatus(null);
            return new(false, response.Message, null);
        }
        try
        {
            var status = RuntimeIpcProtocol.DeserializeStatus(response.Message);
            ObserveStatus(status);
            return new(true, string.Empty, status);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        {
            ObserveStatus(null, error);
            return new(false, $"PinkieSysMon runtime returned invalid status data: {error.Message}", null);
        }
    }

    private string Incident(string phase) => "ipc.status." + _observerId + "." + phase;

    private void ObserveTransportFailure(Exception error, string phase)
    {
        lock (_statusSync)
        {
            if (!_closed) log?.ErrorThrottled(Incident("transport"), TimeSpan.FromSeconds(30),
                $"Runtime status transport failed; phase={phase}; responseUnavailable=true.", error);
        }
    }

    private void ObserveStatus(RuntimeStatusSnapshot? status, Exception? decodeFailure = null)
    {
        lock (_statusSync)
        {
            if (log is null || _closed) return;
            try
            {
                if (decodeFailure is not null)
                    log.ErrorThrottled(Incident("decode"), TimeSpan.FromSeconds(30), "Runtime IPC status could not be decoded.", decodeFailure);
                else if (status is not null)
                {
                    log.FlushSuppressed(Incident("decode"));
                    log.FlushSuppressed(Incident("transport"));
                }
                var message = BuildStatusSummary(status);
                if (message is not null) log.Info(message);
            }
            catch (Exception error)
            {
                log.WarnThrottled(Incident("observer"), TimeSpan.FromSeconds(30),
                    "IPC status diagnostics failed; original response retained.", error);
            }
        }
    }

    private string? BuildStatusSummary(RuntimeStatusSnapshot? status)
    {
        // Caller holds the diagnostic-only gate.
        if (_closed) return null;
        // Hash only in memory; no payload/name/dashboard value is logged.
        var fingerprint = status is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(status.Outputs.OrderBy(o => o.TargetId, StringComparer.Ordinal).Select(o => new
            { o.TargetId, o.Kind, o.Dashboard, o.OutputEnabled, o.ProviderEnabled, o.DeviceEnabled, o.BoardEnabled,
                o.Lock, o.IsActive, o.IsConnected, o.UsbState, o.IsVisible, o.RefreshIntervalMs })))));
        var available = status is not null;
        var changed = _available != available || fingerprint != _statusFingerprint;
        _checks++;
        if (!available) _unavailable++;
        if (!changed) _unchanged++;
        var now = _timestamp();
        if (!changed && Stopwatch.GetElapsedTime(_lastStatusRecord, now) < TimeSpan.FromSeconds(30)) return null;
        var message = JsonSerializer.Serialize(new
        {
            eventName = "editor.ipc.status", observerId = _observerId, phase = _available is null ? "first" : changed ? "changed" : "summary",
            available, checks = _checks, unchangedSinceRecord = _unchanged, unavailableSinceRecord = _unavailable,
            outputs = status?.Outputs.Length,
            active = status?.Outputs.Count(o => o.IsActive), connected = status?.Outputs.Count(o => o.IsConnected),
            targetIds = status?.Outputs.Take(64).Select(o => RuntimeIpcTrace.BoundTarget(o.TargetId)).ToArray(),
            targetIdsOmitted = Math.Max(0, (status?.Outputs.Length ?? 0) - 64),
            visibleOutputNotAsserted = true
        });
        _available = available; _statusFingerprint = fingerprint; _lastStatusRecord = now;
        _unchanged = 0; _unavailable = 0;
        return message;
    }

    public void Dispose()
    {
        string? message;
        lock (_statusSync)
        {
            if (_closed) return;
            _closed = true;
            message = _checks == 0 ? null : JsonSerializer.Serialize(new
            {
                eventName = "editor.ipc.status", observerId = _observerId, phase = "final", available = _available,
                checks = _checks, unchangedSinceRecord = _unchanged, unavailableSinceRecord = _unavailable,
                completedResponsesOnly = true, lateResponsesNotJoined = true
            });
            log?.FlushSuppressed(Incident("transport"));
            log?.FlushSuppressed(Incident("decode"));
            log?.FlushSuppressed(Incident("observer"));
            if (message is not null) log?.Info(message);
        }
    }

    internal Task<RuntimeIpcResponse> ReloadAsync(int timeoutMs = 3000) => SendAsync(RuntimeIpcProtocol.ReloadCommand, timeoutMs);
    internal Task<RuntimeIpcResponse> StartOrReloadTargetAsync(string id, int timeoutMs = 3000) =>
        SendAsync(RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, id), timeoutMs);
    internal Task<RuntimeIpcResponse> StartOrReloadAllAsync(int timeoutMs = 3000) => SendAsync(RuntimeIpcProtocol.StartOrReloadAllCommand, timeoutMs);
    internal Task<RuntimeIpcResponse> StopAllOutputsAsync(int timeoutMs = 3000) => SendAsync(RuntimeIpcProtocol.StopAllCommand, timeoutMs);
    internal Task<RuntimeIpcResponse> AssignDashboardAsync(string id, string board, int timeoutMs = 3000) =>
        SendAsync(RuntimeIpcProtocol.BuildAssignDashboardCommand(id, board), timeoutMs);
    internal Task<RuntimeIpcResponse> PreparePhysicalRemovalAsync(string id, string hash, int timeoutMs = 120_000) =>
        SendAsync(RuntimeIpcProtocol.BuildPreparePhysicalRemoval(id, hash), timeoutMs);
}
