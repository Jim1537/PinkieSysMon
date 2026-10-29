using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PinkieSysMon.Launcher;

// Source-shared with Editor; the standalone build links only the existing writer,
// never the Runtime assembly. Successful standalone actions create no log stream.
internal sealed class LauncherErrorLog(string root)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(root), "logs", "PinkieSysMon.Errors.log");
    private readonly object _sync = new();
    private readonly ConditionalWeakTable<Exception, object> _recorded = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    private long _lastFallback;
    private int _lostRecords;

    internal void Report(string message, Exception? error)
    {
        if (error is null) return;
        lock (_sync)
        {
            if (_recorded.TryGetValue(error, out _)) return;
            try
            {
                var record = $"{DateTimeOffset.Now:O} [ERROR] " + JsonSerializer.Serialize(new
                {
                    component = "Launcher", session = _session, pid = Environment.ProcessId,
                    eventId = Guid.NewGuid().ToString("N"), reportedLevel = "ERROR",
                    message = Bound(message), exception = Bound(error.ToString()),
                    exceptionType = error.GetType().FullName, hResult = $"0x{error.HResult:X8}",
                    nativeCode = error is Win32Exception native ? (int?)native.NativeErrorCode : null,
                    loggingFailures = _lostRecords
                });
                LogFileWriter.Append(_path, record, durable: true);
                _recorded.Add(error, new object());
                LogFileWriter.Maintain(_path);
            }
            catch (Exception failure)
            {
                _lostRecords++;
                var now = Stopwatch.GetTimestamp();
                if (_lastFallback != 0 && Stopwatch.GetElapsedTime(_lastFallback, now) < TimeSpan.FromSeconds(10)) return;
                _lastFallback = now;
                // Preserve the original action error; never recurse into a failed sink.
                try { Console.Error.WriteLine($"PinkieSysMon logging unavailable: {failure.GetType().Name}: {failure.Message}"); }
                catch { }
                try { Trace.WriteLine($"PinkieSysMon Launcher logging unavailable: {failure}"); }
                catch { }
            }
        }
    }

    private static string Bound(string value) => value.Length <= LogFileWriter.MaximumFieldCharacters
        ? value : value[..LogFileWriter.MaximumFieldCharacters] + " [TRUNCATED: field limit exceeded]";
}

internal static class ProjectActionDiagnostics
{
    internal static void Execute(string operation, string subject, Action<Action<string>> action,
        Action<string, Exception?>? report)
    {
        var id = Guid.NewGuid().ToString("N");
        var started = Stopwatch.GetTimestamp();
        var phase = "begin";
        var reached = new List<string>();
        void Write(string outcome, Exception? error = null) => ReportSafely(report, JsonSerializer.Serialize(new
        {
            eventName = operation + "." + outcome, operation, operationId = id,
            subject, phase, outcome, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            reachedPhases = reached.ToArray(), childReadinessConfirmed = false
        }), error);
        Write("started");
        try
        {
            action(value =>
            {
                phase = value.Length <= 128 ? value : value[..128] + " [TRUNCATED]";
                if (reached.Count < 16) reached.Add(phase);
                Write("progress");
            });
        }
        catch (Exception error) { Write("failed", error); throw; }
        Write("returned");
    }

    internal static void ReportSafely(Action<string, Exception?>? report, string message, Exception? error)
    {
        try { report?.Invoke(message, error); }
        catch (Exception failure)
        {
            // A diagnostic observer must not replace the actual action result.
            try { Trace.WriteLine($"PinkieSysMon action diagnostic callback failed: {failure}"); }
            catch { }
        }
    }
}
