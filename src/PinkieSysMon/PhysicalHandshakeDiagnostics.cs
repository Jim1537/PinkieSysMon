using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

// One connection attempt, owned by the opening worker. Adapters describe only
// their existing handshake I/O; this recorder never performs or retries I/O.
internal sealed class PhysicalHandshakeDiagnostics
{
    internal const int MaximumOperations = 8;
    internal const int MaximumResponseBytes = 512;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly string _attemptId = Guid.NewGuid().ToString("N");
    private readonly List<Operation> _operations = new();
    private Operation? _pending;
    private int _omittedOperations;
    private string _stage = "dispatch";

    internal static T Observe<T>(OutputTargetConfig target, int timeoutMs,
        CancellationToken token, Func<PhysicalHandshakeDiagnostics, T> open, Action<string> reportFailure)
    {
        var diagnostics = new PhysicalHandshakeDiagnostics();
        try { return open(diagnostics); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // A diagnostic sink or formatter must never replace the I/O error.
            try { reportFailure(diagnostics.FormatFailure(target, timeoutMs, error)); }
            catch { }
            throw;
        }
    }

    internal void Stage(string stage) => _stage = Limit(stage, 128);

    internal void Begin(string stage, string direction, byte pipe, int requestedBytes)
    {
        Stage(stage);
        _pending = null;
        if (_operations.Count == MaximumOperations)
        {
            _omittedOperations++;
            return;
        }
        _pending = new Operation(_stage, Limit(direction, 16), pipe, requestedBytes);
        _operations.Add(_pending);
    }

    internal void CompleteWrite(int actualBytes)
    {
        if (_pending is not null) _pending.ActualBytes = actualBytes;
        _pending = null;
    }

    internal void CompleteRead(byte[] buffer, int actualBytes)
    {
        if (_pending is null) return;
        _pending.ActualBytes = actualBytes;
        // An invalid count is evidence about the failed completion, not a
        // license to inspect stale/unreturned bytes in the receive buffer.
        if (actualBytes >= 0 && actualBytes <= buffer.Length)
        {
            var captured = Math.Min(actualBytes, MaximumResponseBytes);
            _pending.ResponseHex = Convert.ToHexString(buffer.AsSpan(0, captured));
            _pending.ResponseTruncated = captured != actualBytes;
        }
        _pending = null;
    }

    internal string FormatFailure(OutputTargetConfig target, int timeoutMs, Exception error)
    {
        var errors = new List<object>();
        Exception? current = error;
        while (current is not null && errors.Count < 8)
        {
            errors.Add(new
            {
                type = current.GetType().FullName,
                hResult = $"0x{current.HResult:X8}",
                nativeCode = current is Win32Exception native ? (int?)native.NativeErrorCode : null,
                message = Limit(current.Message, 512)
            });
            current = current.InnerException;
        }
        return "PHYSICAL_OPEN_FAILURE " + JsonSerializer.Serialize(new
        {
            runtime = RuntimeVersion.Current,
            attemptId = _attemptId,
            targetId = Limit(target.Id, 256),
            targetName = Limit(target.Name, 256),
            kind = Limit(target.Kind, 256),
            transport = Limit(target.Transport, 256),
            timeoutMs,
            elapsedMs = Math.Round(Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds, 3),
            stage = _stage,
            operations = _operations,
            omittedOperations = _omittedOperations,
            errors,
            omittedInnerErrors = current is not null
        });
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private sealed class Operation(string stage, string direction, byte pipe, int requestedBytes)
    {
        public string Stage { get; } = stage;
        public string Direction { get; } = direction;
        public string Pipe { get; } = $"0x{pipe:X2}";
        public int RequestedBytes { get; } = requestedBytes;
        public int? ActualBytes { get; set; }
        public string? ResponseHex { get; set; }
        public bool ResponseTruncated { get; set; }
    }
}
