using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon;

internal sealed record RuntimeIpcResponse(bool Success, string Message);

internal static class RuntimeStartupOptions
{
    public const string OutputsStoppedArgument = "--outputs-stopped";

    public static bool ShouldStartOutputs(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return !args.Any(arg => arg.Equals(OutputsStoppedArgument, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record RuntimeOutputStatus(
    string TargetId,
    string TargetName,
    bool IsActive,
    bool IsConnected,
    string UsbState);

internal sealed record RuntimeStatusSnapshot(
    string Dashboard,
    RuntimeOutputStatus[] Outputs);

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
    StopTarget,
    StartOrReloadAll,
    StopAll
}

internal sealed record RuntimeIpcCommand(
    RuntimeIpcCommandKind Kind,
    string? TargetId = null,
    string? Error = null);

internal static class RuntimeIpcProtocol
{
    public const string PingCommand = "PING";
    public const string StatusCommand = "STATUS";
    public const string ReloadCommand = "RELOAD";
    public const string ExitCommand = "EXIT";
    public const string StartOrReloadTargetCommand = "OUTPUT_START_RELOAD";
    public const string StopTargetCommand = "OUTPUT_STOP";
    public const string StartOrReloadAllCommand = "OUTPUT_START_RELOAD_ALL";
    public const string StopAllCommand = "OUTPUT_STOP_ALL";
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

        if (TryParseTargetCommand(command, StartOrReloadTargetCommand, RuntimeIpcCommandKind.StartOrReloadTarget, out var parsed))
            return parsed;
        if (TryParseTargetCommand(command, StopTargetCommand, RuntimeIpcCommandKind.StopTarget, out parsed))
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

    public static async Task<RuntimeIpcResponse> SendAsync(string command, int timeoutMs = 1500, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(100, timeoutMs));

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            await writer.WriteLineAsync(command.AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                return new RuntimeIpcResponse(false, "PinkieSysMon runtime returned an empty IPC response.");

            var separator = line.IndexOf('\t');
            var status = separator >= 0 ? line[..separator] : line;
            var message = separator >= 0 ? line[(separator + 1)..] : string.Empty;
            return status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                ? new RuntimeIpcResponse(true, message)
                : new RuntimeIpcResponse(false, string.IsNullOrWhiteSpace(message) ? "PinkieSysMon runtime rejected the request." : message);
        }
        catch (OperationCanceledException)
        {
            return new RuntimeIpcResponse(false, "PinkieSysMon runtime is not running.");
        }
        catch (TimeoutException)
        {
            return new RuntimeIpcResponse(false, "PinkieSysMon runtime is not running.");
        }
        catch (IOException)
        {
            return new RuntimeIpcResponse(false, "PinkieSysMon runtime is not running.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new RuntimeIpcResponse(false, $"Runtime IPC access denied: {ex.Message}");
        }
    }

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

    public static Task<RuntimeIpcResponse> StopTargetAsync(string targetId, int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StopTargetCommand, targetId), timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> StartOrReloadAllAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.StartOrReloadAllCommand, timeoutMs, cancellationToken);

    public static Task<RuntimeIpcResponse> StopAllOutputsAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default) =>
        SendAsync(RuntimeIpcProtocol.StopAllCommand, timeoutMs, cancellationToken);

}

internal sealed class RuntimeIpcServer : IDisposable
{
    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly Func<string, CancellationToken, Task<RuntimeIpcResponse>> _handler;
    private readonly FileLogger _log;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _disposeState;

    public RuntimeIpcServer(Func<string, CancellationToken, Task<RuntimeIpcResponse>> handler, FileLogger log)
    {
        _handler = handler;
        _log = log;
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
                    RuntimeIpc.PipeName,
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

                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
                var response = await _handler(command.Trim(), cancellationToken).ConfigureAwait(false);
                var status = response.Success ? "OK" : "ERR";
                var message = (response.Message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                await writer.WriteLineAsync($"{status}\t{message}".AsMemory(), cancellationToken).ConfigureAwait(false);
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
