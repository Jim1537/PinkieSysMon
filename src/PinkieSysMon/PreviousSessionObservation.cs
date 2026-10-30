using System.Text;
using System.Text.Json;

namespace PinkieSysMon;

// A startup hint from one bounded active-file tail, never a crash diagnosis.
internal static class PreviousSessionObservation
{
    internal const int MaximumBytes = 16 * 1024;
    internal const int MaximumRecords = 64;

    internal sealed record Result(string State, string? Session = null, bool? NormalExit = null,
        bool StartObserved = false, int BytesRead = 0, int RecordsScanned = 0,
        int Skipped = 0, bool Limited = false, Exception? Failure = null);

    internal static void Record(FileLogger log, bool newGroup)
    {
        var observation = Read(log.Path, newGroup);
        if (observation.Failure is not null)
            log.Warn("Previous-session observation unavailable; prior completion is unknown.", observation.Failure);
        log.Info(JsonSerializer.Serialize(new
        {
            eventName = "session.previous.observation", scope = "active-file-tail",
            state = observation.State, previousSession = observation.Session,
            normalExitObserved = observation.NormalExit, startMarkerObserved = observation.StartObserved,
            bytesRead = observation.BytesRead, recordsScanned = observation.RecordsScanned,
            skippedRecords = observation.Skipped, scanLimited = observation.Limited,
            maximumBytes = MaximumBytes, maximumRecords = MaximumRecords,
            archivesInspected = false, allPriorSessionsClassified = false, osCrashConfirmed = false
        }));
    }

    internal static Result Read(string path, bool newGroup)
    {
        // A joining Editor cannot classify a live overlapping session as interrupted.
        if (!newGroup) return new("overlapping-group-not-inspected");
        try
        {
            return LogFileWriter.UnderGate<Result>(path, "write", () =>
            {
                if (!File.Exists(path)) return new("active-file-missing");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var length = stream.Length;
                if (length == 0) return new("active-file-empty");
                var offset = Math.Max(0, length - MaximumBytes);
                stream.Position = offset;
                var bytes = new byte[(int)(length - offset)];
                stream.ReadExactly(bytes);
                var text = Encoding.UTF8.GetString(bytes);
                var end = text.Length;
                var records = 0;
                var skipped = 0;
                string? session = null;
                while (end > 0 && records < MaximumRecords)
                {
                    var separator = text.LastIndexOf('\n', end - 1);
                    var line = text.Substring(separator + 1, end - separator - 1).TrimEnd('\r');
                    end = separator < 0 ? 0 : separator;
                    records++;
                    if (line.Length == 0) continue;
                    // The first byte may be in the middle of a record/UTF-8 codepoint.
                    if (offset > 0 && separator < 0) { skipped++; break; }
                    try
                    {
                        var brace = line.IndexOf('{');
                        if (brace < 0) { skipped++; continue; }
                        using var document = JsonDocument.Parse(line[brace..]);
                        var record = document.RootElement;
                        if (!record.TryGetProperty("session", out var identity) || identity.ValueKind != JsonValueKind.String ||
                            !Guid.TryParseExact(identity.GetString(), "N", out _) ||
                            !record.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                        { skipped++; continue; }
                        session ??= identity.GetString();
                        if (identity.GetString() != session) continue;
                        var value = message.GetString()!;
                        if (value is "SESSION_END normalExit=true" or "SESSION_END normalExit=false")
                            return new("terminal-marker-observed", session, value.EndsWith("=true", StringComparison.Ordinal),
                                BytesRead: bytes.Length, RecordsScanned: records, Skipped: skipped,
                                Limited: offset > 0 || end > 0);
                        if (value.StartsWith("SESSION_START ", StringComparison.Ordinal))
                            return new("terminal-marker-not-observed", session, StartObserved: true,
                                BytesRead: bytes.Length, RecordsScanned: records, Skipped: skipped,
                                Limited: offset > 0 || end > 0);
                    }
                    catch (JsonException) { skipped++; }
                    catch (InvalidOperationException) { skipped++; }
                }
                return new(session is null ? "session-record-not-observed" : "terminal-marker-not-observed", session,
                    BytesRead: bytes.Length, RecordsScanned: records, Skipped: skipped, Limited: offset > 0 || end > 0);
            });
        }
        catch (Exception error) { return new("observation-failed", Failure: error); }
    }
}
