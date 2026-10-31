using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105FinalCoverageTests
{
    public static void PreviousSessionBoundsAndUncertainty()
    {
        Sandbox(root =>
        {
            var path = Path.Combine(root, "session.log");
            Check(PreviousSessionObservation.Read(path, true).State == "active-file-missing",
                "No active file is an explicit observation gap, not evidence of a clean prior run.");
            var log = new FileLogger(path, "Editor");
            using (var session = ApplicationLogSession.Start(log, "fixture")) session.MarkCompleted();
            var clean = PreviousSessionObservation.Read(path, true);
            Check(clean.State == "terminal-marker-observed" && clean.NormalExit == true && clean.Session == log.SessionId,
                "A retained terminal marker preserves its exact session and normal-exit result.");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Check(PreviousSessionObservation.Read(path, false).State == "overlapping-group-not-inspected",
                    "Joining a live group must perform no active-file read.");
                var failed = PreviousSessionObservation.Read(path, true);
                Check(failed.State == "observation-failed" && failed.Failure is IOException && failed.NormalExit is null,
                    "Denied observation retains its original error and cannot fabricate prior completion.");
            }
            var identity = Guid.NewGuid().ToString("N");
            string Record(string message) => JsonSerializer.Serialize(new { session = identity, message });
            File.WriteAllText(path, new string('x', PreviousSessionObservation.MaximumBytes * 4) + "\n" +
                Record("SESSION_START mode=fixture") + "\n" + Record("last observed work") + "\n");
            var before = File.ReadAllBytes(path);
            var partial = PreviousSessionObservation.Read(path, true);
            Check(partial.State == "terminal-marker-not-observed" && partial.StartObserved && partial.NormalExit is null &&
                  partial.BytesRead == PreviousSessionObservation.MaximumBytes && partial.Limited &&
                  partial.RecordsScanned <= PreviousSessionObservation.MaximumRecords && File.ReadAllBytes(path).SequenceEqual(before),
                "Bounded interrupted-tail observation is read-only and never claims a crash.");

            File.WriteAllLines(path, new[] { Record("SESSION_START mode=fixture") }
                .Concat(Enumerable.Repeat(Record("work"), PreviousSessionObservation.MaximumRecords + 8)));
            var limited = PreviousSessionObservation.Read(path, true);
            Check(limited.Limited && !limited.StartObserved && limited.NormalExit is null &&
                  limited.RecordsScanned == PreviousSessionObservation.MaximumRecords,
                "Record-limit exhaustion is explicit uncertainty, never inferred completion.");
            File.WriteAllText(path, "legacy incomplete text\n{bad JSON\n");
            var malformed = PreviousSessionObservation.Read(path, true);
            Check(malformed.State == "session-record-not-observed" && malformed.Skipped == 2 && malformed.Failure is null,
                "Retained legacy/partial rows are counted without inventing an application failure.");
        });
    }

    public static void LhmRecoveryAndClientIsolation()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "session.log"), "Runtime");
            long now = 0;
            var recoverA = false;
            var callsA = 0;
            using var httpA = new HttpClient(new Handler(_ =>
            {
                callsA++;
                return recoverA ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
                    : throw new HttpRequestException("A-transport fixture");
            })) { BaseAddress = new Uri("http://127.0.0.1:8085/") };
            using var httpB = new HttpClient(new Handler(_ => throw new HttpRequestException("B-transport fixture")))
                { BaseAddress = new Uri("http://127.0.0.1:8085/") };
            using var a = new LibreHardwareMonitorHttpClient(log, httpA, clockMs: () => now);
            using var b = new LibreHardwareMonitorHttpClient(log, httpB, clockMs: () => now);
            log.ErrorThrottled("other-incident", TimeSpan.FromHours(1), "Unrelated fixture failure");
            log.ErrorThrottled("other-incident", TimeSpan.FromHours(1), "Unrelated fixture failure");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Check(!a.TryGetTree(out _), "Injected endpoint must remain unavailable.");
                if (attempt == 0)
                    Check(!a.TryGetTree(out _) && callsA == 1, "Diagnostics cannot bypass the existing five-second retry backoff.");
                now += 6000;
            }
            for (var attempt = 0; attempt < 2; attempt++)
            { Check(!b.TryGetTree(out _), "Independent client failure."); now += 6000; }
            var before = Records(log.ErrorsPath);
            var firstA = before.Single(record => ExceptionText(record).Contains("A-transport fixture")).GetProperty("eventId").GetString();
            var firstB = before.Single(record => ExceptionText(record).Contains("B-transport fixture")).GetProperty("eventId").GetString();
            recoverA = true;
            Check(a.TryGetTree(out _) && callsA == 4, "Recovery uses one ordinary request, without diagnostic probes.");
            var summaries = Records(log.ErrorsPath).Where(record => Message(record).StartsWith("Repeated event summary:")).ToArray();
            Check(summaries.Length == 1 && Message(summaries[0]).Contains("firstEvent=" + firstA + "; suppressed=2;") &&
                  Records(log.Path).Any(record => Message(record).Contains("HTTP endpoint recovered")),
                "Recovery before throttle expiry flushes the exact incident's repeats before reporting recovery.");
            a.Dispose();
            Check(Records(log.ErrorsPath).Count(record => Message(record).StartsWith("Repeated event summary:")) == 1,
                "A completed client cannot flush another client's pending totals.");
            b.Dispose();
            Check(Records(log.ErrorsPath).Any(record => Message(record).Contains("firstEvent=" + firstB + "; suppressed=1;")),
                "Client disposal retains its own final transport count.");
            log.FlushSuppressed("other-incident");
            Check(Records(log.ErrorsPath).Any(record => Message(record).Contains("key=other-incident;") && Message(record).Contains("suppressed=1")) &&
                  Records(log.Path).Concat(Records(log.ErrorsPath)).All(record => record.GetProperty("loggingFailures").GetInt32() == 0),
                "Recovery preserves unrelated incidents and all diagnostic records.");
        });
    }

    public static void StorageFaultsPreserveOriginalAndLossEvidence()
    {
        Sandbox(root =>
        {
            foreach (var mode in new[] { "disk-full", "flush-unconfirmed" })
            {
                var path = Path.Combine(root, mode, "session.log");
                var errors = Path.Combine(Path.GetDirectoryName(path)!, "PinkieSysMon.Errors.log");
                var fail = true;
                var log = new FileLogger(path, "Runtime", (target, record, durable) =>
                {
                    if (fail && target == errors)
                    {
                        if (mode == "flush-unconfirmed") LogFileWriter.Append(target, record, durable: false);
                        throw new IOException("Injected " + mode, unchecked((int)0x80070070));
                    }
                    LogFileWriter.Append(target, record, durable);
                });
                log.Info("baseline");
                var original = new Win32Exception(121, "Original application failure");
                CaptureExpected(() => log.Error("Original operation failed", original), "IOException");
                Check(Text(path).Contains("[LOGGING_FAILURE]") && !Text(path).Contains("[ERROR_REF]") &&
                      !Text(path).Contains("Original application failure") && original.NativeErrorCode == 121,
                    "Failed/unconfirmed persistence keeps the original error and exposes loss without a false stored reference.");
                fail = false;
                log.Error("Persist original after storage returns", original);
                Check(Records(errors).Any(record => record.GetProperty("nativeCode").ValueKind == JsonValueKind.Number &&
                          record.GetProperty("nativeCode").GetInt32() == 121 && ExceptionText(record).Contains("Original application failure")) &&
                      Records(path).Last().GetProperty("loggingFailures").GetInt32() >= 1,
                    "An explicit later report retains native context and cumulative loss evidence.");
            }

            var shared = new FileLogger(Path.Combine(root, "sharing", "session.log"), "Runtime");
            shared.Info("baseline");
            using (var held = new FileStream(shared.ErrorsPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                CaptureExpected(() => shared.Error("Sharing fixture", new IOException("Original sharing-boundary error")), "IOException");
            Check(Text(shared.Path).Contains("[LOGGING_FAILURE]"), "Real file sharing refusal is visible as unconfirmed storage.");

            var contended = new FileLogger(Path.Combine(root, "contention", "session.log"), "Runtime");
            contended.Info("baseline");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = Task.Run(() =>
            {
                using var gate = new Mutex(false, LogFileWriter.ObjectName(contended.ErrorsPath, "write"));
                gate.WaitOne();
                try { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Release the isolated fixture gate."); }
                finally { gate.ReleaseMutex(); }
            });
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(10)), "Fixture gate acquisition.");
                CaptureExpected(() => contended.Error("Contended fixture", new IOException("Original contended operation")), "TimeoutException");
            }
            finally { release.Set(); Check(holder.Wait(TimeSpan.FromSeconds(10)), "Terminal fixture gate release."); }
            Check(Text(contended.Path).Contains("[LOGGING_FAILURE]"), "The existing gate timeout returns loss evidence without cleanup deadlock.");
        });
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private static void CaptureExpected(Action action, string exceptionType)
    {
        using var capture = new StringWriter();
        var previous = Console.Error;
        try { Console.SetError(capture); action(); }
        finally { Console.SetError(previous); }
        var lines = capture.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length == 1 && lines[0].StartsWith("PinkieSysMon logging unavailable: " + exceptionType + ":"),
            "Only the deliberately injected sink diagnostic is expected: " + capture);
    }

    private static string Message(JsonElement record) => record.GetProperty("message").GetString()!;
    private static string ExceptionText(JsonElement record) => record.GetProperty("exception").GetString() ?? "";
    private static JsonElement[] Records(string path) => Text(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var document = JsonDocument.Parse(line[line.IndexOf('{')..]); return document.RootElement.Clone(); }).ToArray();
    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () =>
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return "";
        return string.Concat(Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.archive")
            .Order(StringComparer.Ordinal).Select(File.ReadAllText)) + (File.Exists(path) ? File.ReadAllText(path) : "");
    });
    private static void Sandbox(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-final-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
