using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105LoggingTests
{
    internal static int? TryRunChild(string[] args)
    {
        if (args.Length == 0 || args[0] != "--logging-child") return null;
        try
        {
            var mode = args[1];
            var path = args[2];
            var token = args[3];
            var log = new FileLogger(path, "Editor");
            if (mode == "write")
            {
                for (var i = 0; i < 24; i++) log.Error(token + "-" + i, new IOException("fixture"));
                return 0;
            }
            if (mode == "hold-gate")
            {
                using var gate = new Mutex(false, LogFileWriter.ObjectName(path, "write"));
                gate.WaitOne();
                Console.WriteLine("READY");
                Console.ReadLine(); // Parent may terminate this isolated process.
                gate.ReleaseMutex();
                return 0;
            }
            using var session = ApplicationLogSession.Start(log, "regression-child");
            log.Info(token);
            Console.WriteLine("READY");
            Console.ReadLine();
            session.MarkCompleted();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    public static void ErrorRoutingAndPropagation()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            var error = new IOException("private failure\nsecond line", new Win32Exception(121));
            log.Info("ordinary event");
            log.Error("operation failed", error);
            log.Error("propagated to another boundary", error);
            var records = Read(log.ErrorsPath);
            var session = Read(log.Path);
            Check(records.Length == 2 && session.Length == 3 &&
                  records.Count(r => r.GetProperty("exception").ValueKind == JsonValueKind.String) == 1 &&
                  records[1].GetProperty("message").GetString() == "propagated to another boundary",
                  "One exception is stored once with additional boundary context and correlated session references.");
            Check(records[0].GetProperty("exception").GetString()!.Contains("private failure\nsecond line"),
                "The original exception chain is preserved without injecting record boundaries.");
            var id = records[0].GetProperty("eventId").GetString();
            Check(session.Skip(1).All(r => r.GetProperty("eventId").GetString() == id) &&
                  !File.ReadAllText(log.Path).Contains("private failure"), "Session references correlate without copying failure payloads.");
            log.Warn("caught warning failure", new IOException("warning exception"));
            Check(Read(log.ErrorsPath).Length == 3, "An exception supplied to Warn is retained in Errors.");
        });
    }

    public static void ThrottledFinalTotalsAndChangedNativeError()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            for (var i = 0; i < 3; i++)
                log.ErrorThrottled("same-operation", TimeSpan.FromHours(1), "native operation", new Win32Exception(121, "same text"));
            log.ErrorThrottled("same-operation", TimeSpan.FromHours(1), "native operation", new Win32Exception(31, "same text"));
            log.FlushSuppressed();
            var errors = Read(log.ErrorsPath);
            Check(errors.Length == 3 && errors.Count(r => r.GetProperty("exception").ValueKind == JsonValueKind.String) == 2 &&
                  errors.Where(r => r.GetProperty("nativeCode").ValueKind == JsonValueKind.Number)
                      .Select(r => r.GetProperty("nativeCode").GetInt32()).SequenceEqual(new[] { 121, 31 }),
                "A changed native code bypasses throttling while repeated failures retain one count summary.");
            Check(errors.Any(r => r.GetProperty("message").GetString()!.Contains("suppressed=2")),
                "Suppressed occurrences are not lost when the original error stops.");
            log.ErrorThrottled("final", TimeSpan.FromHours(1), "last failure");
            log.ErrorThrottled("final", TimeSpan.FromHours(1), "last failure");
            log.FlushSuppressed();
            Check(Read(log.ErrorsPath).Last().GetProperty("message").GetString()!.Contains("Repeated event final summary"),
                "Shutdown flush retains a pending suppression total.");
        });
    }

    public static void AppendHistoryPartialTailAndSinkFailure()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            var priorHistory = new string('x', 5 * 1024 * 1024 + 16);
            File.WriteAllText(log.ErrorsPath, priorHistory);
            var priorLength = new FileInfo(log.ErrorsPath).Length;
            log.Error("after interrupted history");
            var archives = Directory.GetFiles(root, "PinkieSysMon.Errors.log.*.archive");
            Check(archives.Length == 1 && File.ReadAllText(archives[0]) == priorHistory,
                "The oversize legacy error history must be archived without truncation.");
            Check(new FileInfo(archives[0]).Length == priorLength,
                "The preserved legacy segment must have exactly its original length.");
            var lines = File.ReadAllLines(log.ErrorsPath);
            Check(lines.Length == 1, "The current error segment contains only the newly appended record.");
            using var appended = JsonDocument.Parse(lines[0][lines[0].IndexOf('{')..]);
            Check(appended.RootElement.GetProperty("message").GetString() == "after interrupted history",
                "The appended error record remains complete and parseable.");
        });
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "runtime.log"), "Runtime");
            Directory.CreateDirectory(log.ErrorsPath);
            using var fallback = new StringWriter();
            var originalError = Console.Error;
            try
            {
                // Only this synchronous, deliberately denied write is captured.
                Console.SetError(fallback);
                log.Error("must not leak into the session", new IOException("original failure"));
            }
            finally { Console.SetError(originalError); }
            var fallbackLines = fallback.ToString().Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check(fallbackLines.Length == 1 &&
                  fallbackLines[0].StartsWith("PinkieSysMon logging unavailable: UnauthorizedAccessException:",
                      StringComparison.Ordinal) &&
                  fallbackLines[0].Contains(log.ErrorsPath, StringComparison.Ordinal),
                "Expected exactly the denied fixture sink diagnostic; captured: " + fallback);
            Check(File.ReadAllText(log.Path).Contains("[LOGGING_FAILURE]") &&
                  !File.ReadAllText(log.Path).Contains("original failure"),
                "An unusable error sink does not throw or claim a confirmed persisted error.");
        });
    }

    public static void ComponentAndOverlappingEditorSessions()
    {
        Sandbox(root =>
        {
            var editor = Path.Combine(root, "PinkieSysMon.Editor.log");
            var runtime = Path.Combine(root, "PinkieSysMon.Runtime.log");
            File.WriteAllText(editor, "old editor");
            File.WriteAllText(runtime, "old runtime");
            var first = new FileLogger(editor, "Editor");
            using var firstSession = ApplicationLogSession.Start(first, "editor");
            Check(File.Exists(first.ErrorsPath), "Session admission creates Errors without truncating it.");
            first.Info("first editor marker");
            using var child = StartChild("session", editor, "child marker");
            Ready(child);
            firstSession.Dispose();
            var second = new FileLogger(editor, "Editor");
            using (var joined = ApplicationLogSession.Start(second, "editor"))
            {
                new FileLogger(editor, "Editor").Info("helper marker");
                Check(File.ReadAllText(editor).Contains("first editor marker") &&
                      File.ReadAllText(editor).Contains("child marker"),
                    "A joining Editor and helper logger never reset a live shared session.");
                StopChild(child);
                using var runtimeSession = ApplicationLogSession.Start(new FileLogger(runtime, "Runtime"), "ordinary");
                Check(File.ReadAllText(runtime).Contains("old runtime") &&
                      File.ReadAllText(editor).Contains("first editor marker"),
                    "A Runtime session retains previous Runtime and Editor history.");
            }
            using var replacement = ApplicationLogSession.Start(new FileLogger(editor, "Editor"), "editor");
            Check(File.ReadAllText(editor).Contains("first editor marker") &&
                  File.ReadAllText(editor).Contains("child marker"),
                "A new Editor session retains the previous overlapping group's history.");
        });
    }

    public static void ConcurrentWritersAndAbandonedGate()
    {
        Sandbox(root =>
        {
            var path = Path.Combine(root, "runtime.log");
            using var a = StartChild("write", path, "A");
            using var b = StartChild("write", path, "B");
            var parent = new FileLogger(path, "Runtime");
            for (var i = 0; i < 24; i++) parent.Error("P-" + i);
            WaitSuccess(a); WaitSuccess(b);
            var records = Read(parent.ErrorsPath);
            Check(records.Length == 72 && records.Select(r => r.GetProperty("eventId").GetString()).Distinct().Count() == 72,
                "Concurrent processes preserve every complete error record without interleaving.");
            using var witness = new Mutex(false, LogFileWriter.ObjectName(path, "write"));
            using var owner = StartChild("hold-gate", path, "gate");
            Ready(owner);
            StopChild(owner);
            parent.Error("after abandoned gate");
            Check(Read(parent.ErrorsPath).Length == 73, "An abandoned writer gate is recovered.");
        });
    }

    public static void CrashedLastEditorReleasesSession()
    {
        Sandbox(root =>
        {
            var path = Path.Combine(root, "PinkieSysMon.Editor.log");
            var log = new FileLogger(path, "Editor");
            log.Error("retain this history");
            using var child = StartChild("session", path, "crashed session marker");
            Ready(child);
            StopChild(child);
            using var replacement = ApplicationLogSession.Start(log, "editor");
            Check(File.ReadAllText(path).Contains("crashed session marker") && Read(log.ErrorsPath).Length == 1,
                "Process death retains interrupted Editor history and previously recorded errors.");
        });
    }

    public static void RollingRetentionAndIdleErrors()
    {
        Sandbox(root =>
        {
            var path = Path.Combine(root, "PinkieSysMon.Runtime.log");
            var log = new FileLogger(path, "Runtime");
            log.Info("before time rollover");
            var before = DateTime.UtcNow.AddMinutes(-20);
            File.SetLastWriteTimeUtc(path, before);
            log.Info("after time rollover");

            var archives = Directory.GetFiles(root, "PinkieSysMon.Runtime.log.*.archive");
            Check(archives.Length == 1 && File.ReadAllText(archives[0]).Contains("before time rollover"),
                "Quarter-hour rotation archives the complete previous segment.");
            Check(File.ReadAllText(path).Contains("after time rollover") &&
                  !File.ReadAllText(path).Contains("before time rollover"),
                "The current stream contains only post-rotation records.");

            // Test 72-hour pruning without waiting in wall-clock time.
            var obsolete = archives[0];
            File.SetLastWriteTimeUtc(obsolete, DateTime.UtcNow.AddHours(-73));
            LogFileWriter.Maintain(path);
            Check(!File.Exists(obsolete) && File.ReadAllText(path).Contains("after time rollover"),
                "Expired closed segments are pruned without touching current history.");

            log.Error("saved error marker");
            var errors = log.ErrorsPath;
            File.SetLastWriteTimeUtc(errors, DateTime.UtcNow.AddHours(-73));
            LogFileWriter.Maintain(errors);
            Check(File.Exists(errors) && new FileInfo(errors).Length == 0 &&
                  Directory.GetFiles(root, "PinkieSysMon.Errors.log.*.archive").Length == 0,
                "A quiet expired error stream rotates and prunes without a new error.");
            log.Error("new retained error");
            Check(Read(errors).Length == 1 &&
                  Read(errors)[0].GetProperty("message").GetString() == "new retained error",
                "The error stream remains writable after idle-pruning.");
        });
    }

    private static ChildProcess StartChild(string mode, string path, string token)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No regression host path.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Issue105LoggingTests).Assembly.Location);
        foreach (var value in new[] { "--logging-child", mode, path, token }) start.ArgumentList.Add(value);
        return new ChildProcess(Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated log fixture."));
    }

    private static void Ready(ChildProcess child)
    {
        var process = child.Process;
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result != "READY")
        {
            StopChild(child);
            throw new InvalidOperationException("The isolated log fixture did not become ready.");
        }
    }

    private static void WaitSuccess(ChildProcess child)
    {
        var process = child.Process;
        if (!process.WaitForExit(10000)) { StopChild(child); throw new InvalidOperationException("Log fixture timed out."); }
        Check(process.ExitCode == 0, "Log fixture failed: " + process.StandardError.ReadToEnd());
    }

    private static void StopChild(ChildProcess child)
    {
        var process = child.Process;
        if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(10000); }
    }

    private sealed class ChildProcess(Process process) : IDisposable
    {
        internal Process Process { get; } = process;
        public void Dispose()
        {
            try { StopChild(this); }
            finally { Process.Dispose(); }
        }
    }

    private static JsonElement[] Read(string path) => File.ReadAllLines(path).Select(line =>
    {
        using var json = JsonDocument.Parse(line[line.IndexOf('{')..]);
        return json.RootElement.Clone();
    }).ToArray();

    private static void Sandbox(Action<string> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { work(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
