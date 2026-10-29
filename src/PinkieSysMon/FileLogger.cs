using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon;

internal sealed class FileLogger
{
    internal const int MaximumThrottleKeys = 1024;
    private const int MaximumFieldCharacters = LogFileWriter.MaximumFieldCharacters;
    private readonly object _sync = new();
    private readonly ConditionalWeakTable<Exception, RecordedFailure> _recordedFailures = new();
    private readonly Dictionary<string, ThrottleState> _throttle = new(StringComparer.Ordinal);
    private static readonly TimeSpan PeriodicSyncInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(1);
    private long _lastDurableAt;
    private long _lastMaintenanceAt;
    private long _lastFallbackAt;
    private int _lostRecords;

    public FileLogger(string path) : this(path, "Diagnostic") { }

    internal FileLogger(string path, string component)
    {
        Path = System.IO.Path.GetFullPath(path);
        Component = component;
        ErrorsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
            "PinkieSysMon.Errors.log");
    }

    public string Path { get; }
    internal string ErrorsPath { get; }
    internal string Component { get; }
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public void WarnThrottled(string key, TimeSpan interval, string message, Exception? ex = null) =>
        WriteThrottled(key, interval, "WARN", message, ex);

    public void ErrorThrottled(string key, TimeSpan interval, string message, Exception? ex = null) =>
        WriteThrottled(key, interval, "ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try { lock (_sync) WriteLocked(level, message, ex); }
        catch (Exception failure) { ReportFailure(failure); }
    }

    private void WriteThrottled(string key, TimeSpan interval, string level,
        string message, Exception? ex)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Throttle key cannot be empty.", nameof(key));
        if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        try
        {
            lock (_sync)
            {
                var now = Stopwatch.GetTimestamp();
                var signature = Signature(level, message, ex);
                if (_throttle.TryGetValue(key, out var state))
                {
                    if (state.Signature == signature && Stopwatch.GetElapsedTime(state.LastWrite, now) < interval)
                    {
                        state.Suppressed++;
                        state.LastOccurrence = DateTimeOffset.Now;
                        return;
                    }
                    FlushSummaryLocked(key, state);
                }
                else if (_throttle.Count >= MaximumThrottleKeys)
                {
                    var oldest = _throttle.MinBy(pair => pair.Value.LastWrite);
                    FlushSummaryLocked(oldest.Key, oldest.Value);
                    _throttle.Remove(oldest.Key);
                }

                var eventId = WriteLocked(level, message, ex);
                _throttle[key] = new ThrottleState(signature, level == "ERROR" || ex is not null,
                    eventId, now, DateTimeOffset.Now);
            }
        }
        catch (Exception failure) { ReportFailure(failure); }
    }

    internal void FlushSuppressed(string key)
    {
        try
        {
            lock (_sync)
            {
                if (_throttle.TryGetValue(key, out var state))
                {
                    FlushSummaryLocked(key, state);
                    _throttle.Remove(key);
                }
            }
        }
        catch (Exception failure) { ReportFailure(failure); }
    }

    internal void FlushSuppressed()
    {
        try
        {
            lock (_sync)
            {
                foreach (var errors in new[] { true, false })
                {
                    var totals = _throttle.Where(pair => pair.Value.IsError == errors && pair.Value.Suppressed > 0)
                        .Select(pair => new
                        {
                            key = pair.Key, firstEvent = pair.Value.EventId,
                            suppressed = pair.Value.Suppressed,
                            first = pair.Value.FirstOccurrence, last = pair.Value.LastOccurrence
                        }).ToArray();
                    if (totals.Length > 0)
                        WriteLocked(errors ? "ERROR" : "INFO",
                            "Repeated event final summary: " + JsonSerializer.Serialize(totals), null);
                }
                _throttle.Clear();
            }
        }
        catch (Exception failure) { ReportFailure(failure); }
    }

    private void FlushSummaryLocked(string key, ThrottleState state)
    {
        if (state.Suppressed == 0) return;
        WriteLocked(state.IsError ? "ERROR" : "INFO",
            $"Repeated event summary: key={key}; firstEvent={state.EventId}; " +
            $"suppressed={state.Suppressed}; first={state.FirstOccurrence:O}; last={state.LastOccurrence:O}.",
            null);
        state.Suppressed = 0;
    }

    private string WriteLocked(string level, string message, Exception? ex)
    {
        var request = RuntimeIpcTrace.Current;
        var eventId = Guid.NewGuid().ToString("N");
        var isError = level == "ERROR" || ex is not null;
        if (!isError)
        {
            TryAppend(Path, Format(level, eventId, message, null, request), durable: false);
            return eventId;
        }

        bool stored;
        if (ex is not null && _recordedFailures.TryGetValue(ex, out var prior))
        {
            eventId = prior.EventId;
            // Preserve the new reporting boundary without duplicating the stack.
            stored = (message == prior.Message && request?.Id == prior.RequestId) || TryAppend(ErrorsPath,
                Format("ERROR_CONTEXT", eventId, message, null, request, level), durable: true);
        }
        else
        {
            stored = TryAppend(ErrorsPath, Format("ERROR", eventId, message, ex, request, level), durable: true);
            if (stored && ex is not null) _recordedFailures.Add(ex, new RecordedFailure(eventId, message, request?.Id));
        }
        if (stored) request?.ObserveError(eventId);
        // The session reference contains no original failure text or exception.
        TryAppend(Path, Format(stored ? "ERROR_REF" : "LOGGING_FAILURE", eventId,
            stored ? "Full failure is recorded in PinkieSysMon.Errors.log."
                : "Failure persistence was not confirmed; inspect log storage/access.",
            null, request), durable: false);
        return eventId;
    }

    // Imported platform metadata is external observation, not an app exception.
    // The collector advances its cursor only after this durable append succeeds.
    internal bool TryAppendPlatformObservations(IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) return true;
        if (messages.Count > PlatformEventCapture.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(messages));
        try
        {
            lock (_sync)
            {
                // One writer gate, append and durable flush for the whole bounded
                // startup batch. Cursor commit follows only this confirmed write.
                var records = messages.Select(message => Format("PLATFORM_OBSERVATION",
                    Guid.NewGuid().ToString("N"), message, null, null));
                return TryAppend(ErrorsPath, string.Join(Environment.NewLine, records), durable: true);
            }
        }
        catch (Exception failure) { ReportFailure(failure); return false; }
    }

    private string Format(string level, string eventId, string message, Exception? ex,
        RuntimeIpcTrace? request, string? reportedLevel = null) =>
        $"{DateTimeOffset.Now:O} [{level}] " + JsonSerializer.Serialize(new
        {
            component = Component,
            session = SessionId,
            pid = Environment.ProcessId,
            eventId,
            reportedLevel,
            ipcRequestId = request?.Id.ToString("N"),
            ipcActor = request?.Actor,
            ipcVerb = request?.Command.Kind.ToString(),
            ipcTarget = RuntimeIpcTrace.BoundTarget(request?.Command.TargetId),
            message = Bound(message),
            exception = ex is null ? null : Bound(ex.ToString()),
            exceptionType = ex?.GetType().FullName,
            renderContext = RenderFailureContext.Find(ex),
            hResult = ex is null ? null : $"0x{ex.HResult:X8}",
            nativeCode = ex is Win32Exception native ? (int?)native.NativeErrorCode : null,
            loggingFailures = Volatile.Read(ref _lostRecords)
        });

    private static string Bound(string value) => value.Length <= MaximumFieldCharacters
        ? value : value[..MaximumFieldCharacters] + " [TRUNCATED: field limit exceeded]";

    private bool TryAppend(string path, string record, bool durable)
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            var lastSync = Interlocked.Read(ref _lastDurableAt);
            var sync = durable || lastSync == 0 ||
                Stopwatch.GetElapsedTime(lastSync, now) >= PeriodicSyncInterval;
            LogFileWriter.Append(path, record, sync);
            if (sync) Interlocked.Exchange(ref _lastDurableAt, now);

            // Retention failure is diagnosed independently of the successfully
            // appended original record; never misreport it as a lost log entry.
            if (string.Equals(path, Path, StringComparison.OrdinalIgnoreCase))
            {
                var last = Interlocked.Read(ref _lastMaintenanceAt);
                if (last == 0 || Stopwatch.GetElapsedTime(last, now) >= MaintenanceInterval)
                {
                    try
                    {
                        LogFileWriter.Maintain(ErrorsPath);
                        Interlocked.Exchange(ref _lastMaintenanceAt, now);
                    }
                    catch (Exception failure) { ReportFailure(failure); }
                }
            }
            return true;
        }
        catch (Exception failure) { ReportFailure(failure); return false; }
    }

    private void ReportFailure(Exception failure)
    {
        Interlocked.Increment(ref _lostRecords);
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastFallbackAt);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(10)) return;
        Interlocked.Exchange(ref _lastFallbackAt, now);
        // No recursion into a failed sink and no original error payload in session logs.
        try { Console.Error.WriteLine($"PinkieSysMon logging unavailable: {failure.GetType().Name}: {failure.Message}"); }
        catch { }
        try { Trace.WriteLine($"PinkieSysMon logging unavailable: {failure}"); }
        catch { }
    }

    private static string Signature(string level, string message, Exception? error)
    {
        var causes = new List<object>();
        for (var depth = 0; error is not null && depth < 8; depth++, error = error.InnerException)
            causes.Add(new
            {
                type = error.GetType().FullName,
                text = Bound(error.Message),
                hResult = error.HResult,
                nativeCode = error is Win32Exception native ? (int?)native.NativeErrorCode : null
            });
        var text = JsonSerializer.Serialize(new { level, message = Bound(message), causes });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private sealed record RecordedFailure(string EventId, string Message, Guid? RequestId);

    private sealed class ThrottleState(string signature, bool isError, string eventId,
        long lastWrite, DateTimeOffset firstOccurrence)
    {
        public string Signature { get; } = signature;
        public bool IsError { get; } = isError;
        public string EventId { get; } = eventId;
        public long LastWrite { get; } = lastWrite;
        public DateTimeOffset FirstOccurrence { get; } = firstOccurrence;
        public DateTimeOffset LastOccurrence { get; set; } = firstOccurrence;
        public long Suppressed { get; set; }
    }
}

