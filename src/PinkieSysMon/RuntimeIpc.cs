using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace PinkieSysMon;

internal sealed record RuntimeIpcResponse(bool Success, string Message)
{
    internal bool ResponseReceived { get; init; }
    internal Guid? RequestId { get; init; }
    internal string? ErrorEventId { get; init; }
}

internal static class RuntimeStartupOptions
{
    public static void Validate(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // Unsupported arguments must fail before any Output can acquire hardware.
        if (args.Any() && !args.SequenceEqual(["--usb-test-only"]))
            throw new ArgumentException("Unsupported Runtime arguments. Use normal startup or the exact --usb-test-only mode.", nameof(args));
    }
}

internal sealed record RuntimeOutputStatus(
    string TargetId,
    string TargetName,
    bool IsActive,
    bool IsConnected,
    string UsbState)
{
    // Additive v1 fields. Null means unavailable in a legacy Runtime response.
    public string? Kind { get; init; }
    public string? Dashboard { get; init; }
    public bool? OutputEnabled { get; init; }
    public bool? ProviderEnabled { get; init; }
    public bool? DeviceEnabled { get; init; }
    public bool? BoardEnabled { get; init; }
    public bool? Lock { get; init; }
    public bool? IsVisible { get; init; }
    public int? RefreshIntervalMs { get; init; }
}

internal sealed record RuntimeStatusSnapshot(
    string Dashboard,
    RuntimeOutputStatus[] Outputs)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? IpcDiagnosticsVersion { get; init; }
}

internal sealed record RuntimeIpcStatusResponse(
    bool Success,
    string Message,
    RuntimeStatusSnapshot? Status);

internal enum RuntimeIpcCommandKind
{
    Invalid,
    Ping,
    Status,
    ReloadConfiguration,
    Exit,
    StartOrReloadTarget,
    StartOrReloadAll,
    StopAll,
    PreparePhysicalRemoval,
    AssignDashboard,
    SetDesktopBoardEnabled,
    SetDesktopLock,
    UsbTestBegin,
    UsbTestStatus,
    UsbTestCancel,
    // Internal tray feedback only; deliberately no IPC verb or parser route grants USB ownership.
    SetPhysicalDeviceEnabled
}

internal sealed record RuntimeIpcCommand(
    RuntimeIpcCommandKind Kind,
    string? TargetId = null,
    string? Error = null,
    string? Dashboard = null,
    bool? Value = null,
    CatalogUsbTestBeginRequest? UsbTestBegin = null,
    CatalogUsbTestHandle? UsbTestHandle = null,
    string? RecordHash = null);

internal static class RuntimeIpcProtocol
{
    public const string PingCommand = "PING";
    public const string StatusCommand = "STATUS";
    public const string ReloadCommand = "RELOAD";
    public const string ExitCommand = "EXIT";
    public const string StartOrReloadTargetCommand = "OUTPUT_START_RELOAD";
    public const string StartOrReloadAllCommand = "OUTPUT_START_RELOAD_ALL";
    public const string StopAllCommand = "OUTPUT_STOP_ALL";
    public const string PreparePhysicalRemovalCommand = "OUTPUT_PREPARE_REMOVE";
    public const string AssignDashboardCommand = "OUTPUT_ASSIGN_DASHBOARD";
    public const string SetDesktopBoardEnabledCommand = "DESKTOP_SET_BOARD_ENABLED";
    public const string SetDesktopLockCommand = "DESKTOP_SET_LOCK";
    public const string RuntimeStoppingMessage = "Runtime is stopping.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string BuildTargetCommand(string verb, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Output target ID must not be empty.", nameof(targetId));

        return $"{verb}\t{JsonSerializer.Serialize(targetId, JsonOptions)}";
    }

    internal static string TargetRecordHash(OutputTargetConfig target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(target))));

    internal static string BuildPreparePhysicalRemoval(string targetId, string recordHash) =>
        PreparePhysicalRemovalCommand + "\t" + JsonSerializer.Serialize(new RemovalRequest(targetId, recordHash));

    private sealed record RemovalRequest(string TargetId, string RecordHash);

    private sealed record ConfigurationRequest(string? TargetId, string? Dashboard = null, bool? Value = null);

    public static string BuildAssignDashboardCommand(string targetId, string dashboard) =>
        BuildConfigurationCommand(AssignDashboardCommand, new(targetId, dashboard));

    public static string BuildDesktopSettingCommand(string verb, string targetId, bool value) =>
        BuildConfigurationCommand(verb, new(targetId, Value: value));

    private static string BuildConfigurationCommand(string verb, ConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetId))
            throw new ArgumentException("Output target ID must not be empty.", nameof(request));
        return $"{verb}\t{JsonSerializer.Serialize(request, JsonOptions)}";
    }

    public static RuntimeIpcCommand ParseCommand(string command)
    {
        if (command.Equals(PingCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.Ping);
        if (command.Equals(StatusCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.Status);
        if (command.Equals(ReloadCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.ReloadConfiguration);
        if (command.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.Exit);
        if (command.Equals(StartOrReloadAllCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadAll);
        if (command.Equals(StopAllCommand, StringComparison.OrdinalIgnoreCase))
            return new RuntimeIpcCommand(RuntimeIpcCommandKind.StopAll);

        if (CatalogUsbTestIpcProtocol.TryParse(command, out var usbTest))
            return usbTest;

        if (command.StartsWith(PreparePhysicalRemovalCommand + "\t", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemovalRequest>(
                    command[(PreparePhysicalRemovalCommand.Length + 1)..],
                    new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
                if (request is null || string.IsNullOrWhiteSpace(request.TargetId) ||
                    request.TargetId.Length > 128 || request.RecordHash?.Length != 64 ||
                    !request.RecordHash!.All(Uri.IsHexDigit))
                    throw new JsonException("Expected an Output ID and exact record hash.");
                return new(RuntimeIpcCommandKind.PreparePhysicalRemoval, request.TargetId, RecordHash: request.RecordHash);
            }
            catch (JsonException) { return new(RuntimeIpcCommandKind.Invalid, Error: "Invalid physical Remove request."); }
        }

        if (TryParseTargetCommand(command, StartOrReloadTargetCommand, RuntimeIpcCommandKind.StartOrReloadTarget, out var parsed))
            return parsed;
        if (TryParseConfigurationCommand(command, AssignDashboardCommand, RuntimeIpcCommandKind.AssignDashboard, out parsed) ||
            TryParseConfigurationCommand(command, SetDesktopBoardEnabledCommand, RuntimeIpcCommandKind.SetDesktopBoardEnabled, out parsed) ||
            TryParseConfigurationCommand(command, SetDesktopLockCommand, RuntimeIpcCommandKind.SetDesktopLock, out parsed))
            return parsed;

        return new RuntimeIpcCommand(RuntimeIpcCommandKind.Invalid, Error: $"Unknown IPC command: '{command}'.");
    }

    public static string SerializeStatus(RuntimeStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    public static bool ResponseIndicatesRuntimeStopping(RuntimeIpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Success && response.Message.EndsWith(RuntimeStoppingMessage, StringComparison.Ordinal);
    }

    public static RuntimeStatusSnapshot DeserializeStatus(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidDataException("Runtime IPC status payload is empty.");

        return JsonSerializer.Deserialize<RuntimeStatusSnapshot>(payload, JsonOptions)
            ?? throw new InvalidDataException("Runtime IPC status payload could not be decoded.");
    }

    private static bool TryParseConfigurationCommand(string command, string verb,
        RuntimeIpcCommandKind kind, out RuntimeIpcCommand parsed)
    {
        var prefix = verb + "\t";
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            parsed = null!;
            return false;
        }
        try
        {
            var request = JsonSerializer.Deserialize<ConfigurationRequest>(command[prefix.Length..], JsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.TargetId) ||
                (kind == RuntimeIpcCommandKind.AssignDashboard
                    ? string.IsNullOrWhiteSpace(request.Dashboard) || request.Value is not null
                    : request.Value is null || request.Dashboard is not null))
                throw new JsonException("Expected a target ID and exactly one dashboard name or boolean setting.");
            parsed = new(kind, request.TargetId, Dashboard: request.Dashboard, Value: request.Value);
        }
        catch (JsonException ex)
        {
            parsed = new(RuntimeIpcCommandKind.Invalid, Error: $"IPC command '{verb}' has invalid settings: {ex.Message}");
        }
        return true;
    }

    private static bool TryParseTargetCommand(
        string command,
        string verb,
        RuntimeIpcCommandKind kind,
        out RuntimeIpcCommand parsed)
    {
        var prefix = verb + "\t";
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            parsed = null!;
            return false;
        }

        var argument = command[prefix.Length..];
        try
        {
            var targetId = JsonSerializer.Deserialize<string>(argument, JsonOptions);
            if (string.IsNullOrWhiteSpace(targetId))
            {
                parsed = new RuntimeIpcCommand(
                    RuntimeIpcCommandKind.Invalid,
                    Error: $"IPC command '{verb}' requires a non-empty output target ID.");
                return true;
            }

            parsed = new RuntimeIpcCommand(kind, targetId);
            return true;
        }
        catch (JsonException ex)
        {
            parsed = new RuntimeIpcCommand(
                RuntimeIpcCommandKind.Invalid,
                Error: $"IPC command '{verb}' has an invalid target ID: {ex.Message}");
            return true;
        }
    }
}

internal static class RuntimeIpc
{
    public const string PipeName = "PinkieSysMon.Runtime.v1";

    public static Task<RuntimeIpcResponse> SendAsync(string command, int timeoutMs = 1500,
        CancellationToken cancellationToken = default) =>
        SendCoreAsync(command, PipeName, timeoutMs, cancellationToken);

    internal static Task<RuntimeIpcResponse> SendUsbTestAsync(string command, FileLogger? log,
        int timeoutMs = 1500, CancellationToken cancellationToken = default) =>
        SendCoreAsync(command, PipeName, timeoutMs, cancellationToken, log);

    // A distinct pipe can be supplied by deterministic tests; production always
    // uses the same v1 endpoint. A negotiated wrapper still carries one command.
    internal static async Task<RuntimeIpcResponse> SendCoreAsync(string command, string pipeName,
        int timeoutMs = 1500, CancellationToken cancellationToken = default, FileLogger? log = null,
        RuntimeIpcTrace? request = null, bool correlated = false,
        Action<Exception, string>? observeStatusFailure = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(100, timeoutMs));
        var requestAttempted = false;
        var readOnly = command.Equals(RuntimeIpcProtocol.PingCommand, StringComparison.OrdinalIgnoreCase) ||
            command.Equals(RuntimeIpcProtocol.StatusCommand, StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith(CatalogUsbTestIpcProtocol.StatusCommand + "\t", StringComparison.OrdinalIgnoreCase);

        using var requestContext = RuntimeIpcTrace.Enter(request);
        var parsed = request?.Command ?? (log is null ? null : RuntimeIpcProtocol.ParseCommand(command));
        var attempt = parsed?.UsbTestBegin?.Intent.AttemptId ?? parsed?.UsbTestHandle?.AttemptId;
        using var operation = log is not null && request is not null
            ? new LogOperation(log, "editor.ipc.command", RuntimeIpcTrace.BoundTarget(parsed?.TargetId), request.Id.ToString("N"))
            : log is not null && (parsed?.Kind is RuntimeIpcCommandKind.UsbTestBegin or RuntimeIpcCommandKind.UsbTestCancel)
                ? new LogOperation(log, "editor.ipc.usb-test", "usb-test-" + attempt?.ToString("N"), attempt?.ToString("N")) : null;
        var statusIncident = "ipc.usb-test.status." + attempt?.ToString("N");
        var phase = "connect";
        void ObserveFailure(Exception error)
        {
            var detail = $"verb={parsed?.Kind}; requestAttempted={requestAttempted}; readOnly={readOnly}; outcome={(requestAttempted && !readOnly ? "unknown-after-send" : "unavailable")}";
            if (operation is not null && request is not null && error is OperationCanceledException &&
                cancellationToken.IsCancellationRequested && !requestAttempted)
                operation.Cancel("callerCancelledBeforeWrite=true; commandNotDispatched=true");
            else if (operation is not null) operation.Fail(error, detail);
            else if (parsed?.Kind == RuntimeIpcCommandKind.Status && requestAttempted &&
                !cancellationToken.IsCancellationRequested)
            {
                if (observeStatusFailure is not null) observeStatusFailure(error, phase);
                else log?.ErrorThrottled("ipc.status.transport", TimeSpan.FromSeconds(30),
                    $"Runtime status transport failed; phase={phase}; responseUnavailable=true.", error);
            }
            else if (log is not null && parsed?.Kind == RuntimeIpcCommandKind.UsbTestStatus)
                log.ErrorThrottled(statusIncident, TimeSpan.FromSeconds(30), JsonSerializer.Serialize(new
                {
                    eventName = "editor.ipc.usb-test.status.failure", operation = "editor.ipc.usb-test.status",
                    parentOperationId = attempt?.ToString("N"), phase, detail
                }), error);
        }
        try
        {
            operation?.Phase(phase, $"verb={parsed?.Kind}; actor={request?.Actor}; correlation={(request is null ? "usb-attempt-or-none" : correlated ? "shared" : "legacy-local-only")}");
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            // Even a partially failed write cannot prove the server did not receive the request.
            phase = "write";
            operation?.Phase(phase);
            requestAttempted = true;
            await writer.WriteLineAsync(command.AsMemory(), timeout.Token).ConfigureAwait(false);
            phase = "response";
            operation?.Phase(phase);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                operation?.RecordFailure(null, "response.empty", $"verb={parsed?.Kind}; requestAttempted=true; outcome=unknown-after-send; requestNotRetried=true");
                operation?.Partial("responseReceived=false; outcome=unknown-after-send");
                return new(false, DescribeUnavailableResponse(requestAttempted, "Runtime returned an empty IPC response.", readOnly));
            }

            var separator = line.IndexOf('\t');
            var status = separator >= 0 ? line[..separator] : line;
            var message = separator >= 0 ? line[(separator + 1)..] : string.Empty;
            if (request is not null && correlated && (status.Equals("OK", StringComparison.OrdinalIgnoreCase) || status.Equals("ERR", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var reply = RuntimeIpcDiagnostics.Unwrap(new(status.Equals("OK", StringComparison.OrdinalIgnoreCase), message)
                        { ResponseReceived = true }, request);
                    if (reply.Success) operation?.Complete("replyReceived=true; visibleOutputNotAsserted=true");
                    else if (reply.ErrorEventId is not null)
                    {
                        operation?.RecordFailure(null, "remote.failure", $"originErrorEventId={reply.ErrorEventId}; remoteExceptionNotDuplicated=true");
                        operation?.Partial("replyReceived=true; responseSuccess=false; visibleOutputNotAsserted=true");
                    }
                    else operation?.Cancel("replyReceived=true; rejectionReported=true");
                    return reply;
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
                {
                    operation?.Fail(error, "requestAttempted=true; outcome=unknown-after-send; commandNotRetried=true");
                    return new(false, DescribeUnavailableResponse(true, "Runtime returned invalid IPC correlation data."));
                }
            }
            if (status.Equals("OK", StringComparison.OrdinalIgnoreCase)) operation?.Complete("response=OK; replyOnly=true; technicalReleaseNotAsserted=true");
            else if (status.Equals("ERR", StringComparison.OrdinalIgnoreCase)) operation?.Cancel("response=ERR; rejectionReported=true; releaseNotAsserted=true");
            else
            {
                operation?.RecordFailure(null, "response.invalid", $"verb={parsed?.Kind}; recognizedStatus=false; outcome=unknown-after-send");
                operation?.Partial("responseReceived=false; outcome=unknown-after-send");
                if (request is not null)
                    return new(false, DescribeUnavailableResponse(true, "Runtime returned an unrecognized IPC response."));
            }
            if (log is not null && parsed?.Kind == RuntimeIpcCommandKind.UsbTestStatus &&
                (status.Equals("OK", StringComparison.OrdinalIgnoreCase) || status.Equals("ERR", StringComparison.OrdinalIgnoreCase)))
                log.FlushSuppressed(statusIncident);
            return status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                ? new RuntimeIpcResponse(true, message) { ResponseReceived = true }
                : new RuntimeIpcResponse(false, string.IsNullOrWhiteSpace(message) ? "PinkieSysMon runtime rejected the request." : message)
                    { ResponseReceived = status.Equals("ERR", StringComparison.OrdinalIgnoreCase) };
        }
        catch (OperationCanceledException error)
        {
            ObserveFailure(error);
            return new(false, DescribeUnavailableResponse(requestAttempted,
                cancellationToken.IsCancellationRequested ? "Runtime IPC request was canceled." : "Runtime IPC request timed out.", readOnly));
        }
        catch (TimeoutException error)
        {
            ObserveFailure(error);
            return new(false, DescribeUnavailableResponse(requestAttempted, "Runtime IPC request timed out.", readOnly));
        }
        catch (IOException error)
        {
            ObserveFailure(error);
            return new(false, DescribeUnavailableResponse(requestAttempted, "Runtime IPC connection closed or could not be opened.", readOnly));
        }
        catch (UnauthorizedAccessException ex)
        {
            ObserveFailure(ex);
            return new(false, DescribeUnavailableResponse(requestAttempted, $"Runtime IPC access denied: {ex.Message}", readOnly));
        }
    }

    internal static string DescribeUnavailableResponse(bool requestAttempted, string reason, bool readOnly = false) => requestAttempted
        ? readOnly
            ? $"{reason} Runtime status/response is unavailable; this read-only request changes no settings."
            : $"{reason} The command may have been applied. Query Runtime status before retrying."
        : $"{reason} Runtime could not be contacted; no command write was attempted.";

    public static async Task<RuntimeIpcStatusResponse> GetStatusAsync(int timeoutMs = 750, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(RuntimeIpcProtocol.StatusCommand, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            return new RuntimeIpcStatusResponse(false, response.Message, null);

        try
        {
            var status = RuntimeIpcProtocol.DeserializeStatus(response.Message);
            return new RuntimeIpcStatusResponse(true, string.Empty, status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            return new RuntimeIpcStatusResponse(false, $"PinkieSysMon runtime returned invalid status data: {ex.Message}", null);
        }
    }

    public static Task<RuntimeIpcResponse> ReloadAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.ReloadCommand, timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> StartOrReloadTargetAsync(string targetId, int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, targetId), timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> AssignDashboardAsync(string targetId, string dashboard, int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.BuildAssignDashboardCommand(targetId, dashboard), timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> SetDesktopBoardEnabledAsync(string targetId, bool enabled, int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.BuildDesktopSettingCommand(RuntimeIpcProtocol.SetDesktopBoardEnabledCommand, targetId, enabled), timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> SetDesktopLockAsync(string targetId, bool locked, int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.BuildDesktopSettingCommand(RuntimeIpcProtocol.SetDesktopLockCommand, targetId, locked), timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> StartOrReloadAllAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.StartOrReloadAllCommand, timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> StopAllOutputsAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.StopAllCommand, timeoutMs, cancellationToken);

    internal static Task<RuntimeIpcResponse> PreparePhysicalRemovalAsync(string targetId, string recordHash,
        int timeoutMs = 120_000) =>
        SendAsync(RuntimeIpcProtocol.BuildPreparePhysicalRemoval(targetId, recordHash), timeoutMs);

}

internal sealed class RuntimeIpcServer : IDisposable
{
    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly Func<string, CancellationToken, Task<RuntimeIpcResponse>> _handler;
    private readonly FileLogger _log;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _disposeState;

    public RuntimeIpcServer(Func<string, CancellationToken, Task<RuntimeIpcResponse>> handler, FileLogger log, string? pipeName = null)
    {
        _handler = handler;
        _log = log;
        _pipeName = pipeName ?? RuntimeIpc.PipeName;
    }

    public void Start()
    {
        if (_loop is not null)
            return;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // A connected but silent peer cannot hold the only IPC listener forever.
                // The timeout covers request admission, not native session cleanup.
                receiveDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                var command = await RuntimeIpcRequestReader.ReadLineAsync(reader,
                    receiveDeadline.Token).ConfigureAwait(false) ?? string.Empty;
                await RuntimeIpcDiagnostics.DispatchAsync(command.Trim(), _handler, _log, cancellationToken, async response =>
                {
                    var status = response.Success ? "OK" : "ERR";
                    var message = (response.Message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                    await writer.WriteLineAsync($"{status}\t{message}".AsMemory(), cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.WarnThrottled("runtime-ipc.loop", ErrorLogThrottleInterval, "Runtime IPC server error", ex);
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _stop.Cancel();
        try
        {
            _loop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warn("Runtime IPC server shutdown failed after cancellation.", ex);
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
