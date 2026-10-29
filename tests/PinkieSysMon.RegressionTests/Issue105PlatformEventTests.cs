using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105PlatformEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static PlatformCursor Upper(long id) => new(1, "System", id, "EventLog", Now.AddMinutes(-1), Now.AddDays(-1), null, null);

    public static void ChronologyDeduplicationAndReset()
    {
        Sandbox((root, log) =>
        {
            var reads = 0;
            var source = new Source((cursor, from, to, maximum, token) =>
            {
                reads++;
                Check(from == Now.AddDays(-3) && to == Now && maximum == 128, "Exact bounded query window.");
                if (cursor is not null)
                {
                    Check(cursor.RecordId == 50 && cursor.BootRecordId == 10, "Persisted System and observed boot identity must be supplied.");
                    return new([], Upper(50));
                }
                return new([
                    new(30, "Microsoft-Windows-Kernel-Power", 41, 1, Now.AddMinutes(-9)),
                    new(10, "Microsoft-Windows-Kernel-General", 12, 4, Now.AddMinutes(-10)),
                    new(20, "Microsoft-Windows-WHEA-Logger", 18, 2, Now.AddMinutes(-8))], Upper(50));
            });
            var persistenceCalls = 0;
            Run(root, log, source, messages =>
            {
                persistenceCalls++;
                Check(messages.Count == 3, "One bounded batch must carry every observation.");
                return log.TryAppendPlatformObservations(messages);
            });
            Run(root, log, source);
            Check(persistenceCalls == 1, "Startup observation persistence must use one batch boundary.");
            var records = Observations(log).ToArray();
            Check(reads == 2 && records.Length == 3 && records.Select(e => e.GetProperty("recordId").GetInt64()).SequenceEqual(new long[] { 10, 20, 30 }),
                "Healthy restart reads the cursor rather than replaying prior observations.");
            Check(records.All(e => e.GetProperty("bootBoundaryRecordId").GetInt64() == 10 &&
                e.GetProperty("clockOrderingConflict").GetBoolean() && !e.GetProperty("causeEstablished").GetBoolean() &&
                e.GetProperty("importedAtUtc").GetDateTimeOffset() == Now), "Boot, event/import times and clock disagreement remain observations, never causes.");
            Check(records[1].GetProperty("eventTimeUtc").GetDateTimeOffset() > records[2].GetProperty("eventTimeUtc").GetDateTimeOffset(),
                "Clock conflict must not be silently sorted away or rewritten.");
            Run(root, log, new Source((cursor, from, to, maximum, token) => new([
                new(2, "Microsoft-Windows-WHEA-Logger", 19, 3, Now.AddMinutes(-1))], Upper(3), Reset: true)));
            Check(Observations(log).Count() == 4 && Text(log.ErrorsPath).Contains("system-log-identity-reset"),
                "A confirmed identity reset may admit reused IDs with an explicit gap.");
            Check(!Text(log.Path).Contains("Microsoft-Windows-WHEA-Logger"), "Full Windows metadata belongs only to Errors.");
        });
    }

    public static void BoundsCursorAndPersistenceFailures()
    {
        Sandbox((root, log) =>
        {
            var source = new Source((cursor, from, to, maximum, token) => new(
                Enumerable.Range(1, 128).Select(i => new PlatformEvent(i, "Microsoft-Windows-WHEA-Logger", 18, 2,
                    Now.AddMinutes(-5).AddSeconds(i))).ToArray(), Upper(200), Capped: true));
            Run(root, log, source);
            Check(Observations(log).Count() == 128 && Text(log.ErrorsPath).Contains("record-limit"), "Bounded import declares omitted history.");
            var path = Path.Combine(root, "config", "platform-events.cursor.json");
            File.WriteAllText(path, "{invalid-cursor");
            var before = File.ReadAllBytes(path);
            var calls = 0;
            Run(root, log, new Source((cursor, from, to, maximum, token) => { calls++; throw new InvalidOperationException(); }));
            Check(calls == 0 && before.SequenceEqual(File.ReadAllBytes(path)), "Invalid cursor fails closed without querying or replacing evidence.");
        });
        Sandbox((root, log) =>
        {
            var path = Path.Combine(root, "config", "platform-events.cursor.json");
            var source = new Source((cursor, from, to, maximum, token) => new([
                new(1, "Microsoft-Windows-WHEA-Logger", 18, 2, Now.AddMinutes(-1))], Upper(2)));
            Run(root, log, source, _ => false);
            Check(!File.Exists(path) && !Observations(log).Any() && Text(log.ErrorsPath).Contains("persistence-unconfirmed"),
                "Unconfirmed durable observation persistence cannot advance the cursor.");
            var newer = JsonSerializer.SerializeToUtf8Bytes(Upper(999));
            Run(root, log, new Source((cursor, from, to, maximum, token) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, newer);
                return new([], Upper(2));
            }));
            Check(newer.SequenceEqual(File.ReadAllBytes(path)) && Text(log.ErrorsPath).Contains("newer state was preserved"),
                "Concurrent cursor write is preserved with the original conflict exception.");
        });
    }

    public static void AccessFailureCancellationAndAllowlist()
    {
        Sandbox((root, log) =>
        {
            Run(root, log, new Source((cursor, from, to, maximum, token) =>
                throw new System.ComponentModel.Win32Exception(5, "Injected platform access denial")));
            Check(Text(log.ErrorsPath).Contains("\"nativeCode\":5") && !Text(log.Path).Contains("Injected platform access denial") &&
                !File.Exists(Path.Combine(root, "config", "platform-events.cursor.json")), "Access denial is a capture gap, not a clean result or elevated retry.");
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var capture = new PlatformEventCapture(root, log, new Source((cursor, from, to, maximum, token) =>
            { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10)), "Reader release."); return new([], Upper(1)); }), () => Now);
            capture.Start();
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(10)), "Reader did not enter.");
                capture.Dispose();
                Check(!capture.Completion.IsCompleted, "Stop must not manufacture native completion or join the blocked source.");
                var before = Text(log.Path) + Text(log.ErrorsPath);
                release.Set(); Check(capture.Completion.Wait(TimeSpan.FromSeconds(10)), "Reader completion fence.");
                Check(before == Text(log.Path) + Text(log.ErrorsPath), "Late read cannot admit new records after close.");
            }
            finally { release.Set(); Check(capture.Completion.Wait(TimeSpan.FromSeconds(10)), "Final reader fence."); }
        });
        Check(WindowsPlatformEventSource.IsAllowed("Microsoft-Windows-WHEA-Logger", 18) &&
            WindowsPlatformEventSource.IsAllowed("Microsoft-Windows-Kernel-Power", 41) &&
            !WindowsPlatformEventSource.IsAllowed("Microsoft-Windows-Kernel-Power", 42) &&
            !WindowsPlatformEventSource.IsAllowed("PRIVATE-PROVIDER", 41), "Provider/event allowlist must fail closed.");
        var xpath = WindowsPlatformEventSource.BuildQuery(Now.AddDays(-1), Now, 12, 34);
        _ = System.Xml.XPath.XPathExpression.Compile(xpath);
        Check(xpath.Contains("EventRecordID > 12") && xpath.Contains("EventRecordID <= 34") &&
            !xpath.Contains("PRIVATE-"), "Query syntax and watermark bounds are static evidence, not an executed Windows query.");
    }

    private sealed class Source(Func<PlatformCursor?, DateTimeOffset, DateTimeOffset, int, CancellationToken, PlatformEventBatch> read) : IPlatformEventSource
    {
        public PlatformEventBatch Read(PlatformCursor? cursor, DateTimeOffset from, DateTimeOffset to, int maximum, CancellationToken token) =>
            read(cursor, from, to, maximum, token);
    }
    private static void Run(string root, FileLogger log, IPlatformEventSource source, Func<IReadOnlyList<string>, bool>? persist = null)
    {
        using var capture = new PlatformEventCapture(root, log, source, () => Now, persist);
        capture.Start(); Check(capture.Completion.Wait(TimeSpan.FromSeconds(20)), "Capture completion fence.");
    }
    private static IEnumerable<JsonElement> Observations(FileLogger log) => Records(log.ErrorsPath)
        .Where(e => e.GetProperty("message").GetString()!.StartsWith('{'))
        .Select(e => { using var j = JsonDocument.Parse(e.GetProperty("message").GetString()!); return j.RootElement.Clone(); })
        .Where(e => e.GetProperty("eventName").GetString() == "platform.observation");
    private static JsonElement[] Records(string path) => Text(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var j = JsonDocument.Parse(line[line.IndexOf('{')..]); return j.RootElement.Clone(); }).ToArray();
    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () =>
        string.Concat(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.archive").Order()
            .Select(File.ReadAllText)) + (File.Exists(path) ? File.ReadAllText(path) : ""));
    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-platform-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var log = new FileLogger(Path.Combine(root, "logs", "PinkieSysMon.Runtime.log"), "Runtime");
        try
        {
            action(root, log);
            Check(Records(log.Path).Concat(Records(log.ErrorsPath)).All(e => e.GetProperty("loggingFailures").GetInt32() == 0), "No fixture logger loss.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
