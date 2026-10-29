using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon;

internal sealed record PlatformEvent(long RecordId, string Provider, int EventId, byte? Level, DateTimeOffset EventTimeUtc);
internal sealed record PlatformCursor(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Channel,
    [property: JsonRequired] long RecordId,
    [property: JsonRequired] string Provider,
    [property: JsonRequired] DateTimeOffset EventTimeUtc,
    [property: JsonRequired] DateTimeOffset? LogCreatedUtc,
    [property: JsonRequired] long? BootRecordId,
    [property: JsonRequired] DateTimeOffset? BootTimeUtc);
internal sealed record PlatformEventBatch(IReadOnlyList<PlatformEvent> Events, PlatformCursor? Upper,
    bool Reset = false, bool Capped = false, string? Gap = null);
internal interface IPlatformEventSource
{
    PlatformEventBatch Read(PlatformCursor? cursor, DateTimeOffset from, DateTimeOffset to,
        int maximum, CancellationToken token);
}

// Once per ordinary Runtime. Windows readers stay owned by their background task.
internal sealed class PlatformEventCapture : IDisposable
{
    internal const int MaximumRecords = 128;
    private const int MaximumCursorBytes = 16384;
    private readonly object _sync = new();
    private readonly FileLogger _log;
    private readonly string _cursorPath;
    private readonly IPlatformEventSource _source;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string, bool> _persist;
    private readonly CancellationTokenSource _stop = new();
    private bool _closed, _started;
    private Task _completion = Task.CompletedTask;
    internal Task Completion { get { lock (_sync) return _completion; } }
    private static readonly JsonSerializerOptions CursorJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal PlatformEventCapture(string root, FileLogger log, IPlatformEventSource? source = null,
        Func<DateTimeOffset>? now = null, Func<string, bool>? persist = null)
    {
        _log = log;
        _cursorPath = Path.Combine(root, "config", "platform-events.cursor.json");
        _source = source ?? new WindowsPlatformEventSource();
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _persist = persist ?? log.TryAppendPlatformObservation;
    }

    internal void Start()
    {
        lock (_sync)
        {
            if (_closed || _started) return;
            _started = true;
            var token = _stop.Token;
            _completion = Task.Run(() => Run(token));
        }
    }

    private bool Admit() { lock (_sync) return !_closed; }

    private void Run(CancellationToken token)
    {
        try
        {
            var expected = ReadCursorBytes();
            var cursor = expected is null ? null : JsonSerializer.Deserialize<PlatformCursor>(expected, CursorJson);
            if (expected is not null) Validate(cursor);
            var importedAt = _now().ToUniversalTime();
            var from = importedAt.AddDays(-Math.Min(3, LogRetentionPolicy.GetRetentionDays(_log.Path)));
            if (!Admit()) return;
            _log.Info($"PLATFORM_CAPTURE_STARTED channel=System; maximumRecords={MaximumRecords}; windowStartUtc={from:O}; upperTimeUtc={importedAt:O}; cursorPresent={cursor is not null}.");
            var batch = _source.Read(cursor, from, importedAt, MaximumRecords, token);
            token.ThrowIfCancellationRequested();
            if (!Admit()) return;
            if (batch.Upper is null)
            {
                Gap(batch.Gap ?? "channel-empty-or-metadata-unavailable");
                return;
            }
            Validate(batch.Upper);
            if (!batch.Reset && cursor is not null && batch.Upper.RecordId < cursor.RecordId)
                throw new InvalidDataException("Platform source regressed its watermark without an identity reset.");
            var rows = batch.Events.OrderBy(e => e.RecordId).ToArray();
            if (rows.Length > MaximumRecords || rows.Select(e => e.RecordId).Distinct().Count() != rows.Length ||
                rows.Any(e => !WindowsPlatformEventSource.IsAllowed(e.Provider, e.EventId) || e.RecordId <= 0 ||
                    e.RecordId > batch.Upper.RecordId || e.EventTimeUtc < from || e.EventTimeUtc > importedAt ||
                    !batch.Reset && cursor is not null && e.RecordId <= cursor.RecordId))
                throw new InvalidDataException("Platform event source returned an invalid bounded batch.");
            var clockConflict = rows.Zip(rows.Skip(1), (a, b) => a.EventTimeUtc > b.EventTimeUtc).Any(v => v) ||
                batch.Upper.EventTimeUtc > importedAt;
            var bootId = batch.Reset || batch.Capped || batch.Gap is not null ? null : cursor?.BootRecordId;
            var bootTime = bootId is null ? null : cursor?.BootTimeUtc;
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                if (row.Provider == "Microsoft-Windows-Kernel-General" && row.EventId == 12)
                { bootId = row.RecordId; bootTime = row.EventTimeUtc; }
                var record = JsonSerializer.Serialize(new
                {
                    eventName = "platform.observation", source = "WindowsEventLog", channel = "System",
                    provider = row.Provider, recordId = row.RecordId, eventId = row.EventId, level = row.Level,
                    eventTimeUtc = row.EventTimeUtc, importedAtUtc = importedAt,
                    applicationImportSession = _log.SessionId,
                    bootBoundaryRecordId = bootId, bootBoundaryTimeUtc = bootTime,
                    clockOrderingConflict = clockConflict, causeEstablished = false
                });
                if (!Admit()) return;
                if (!_persist(record)) { Gap("observation-persistence-unconfirmed; cursor-not-advanced"); return; }
            }
            if (batch.Reset) Gap("system-log-identity-reset");
            if (batch.Capped) Gap("record-limit; older-matching-events-not-imported");
            if (batch.Gap is not null) Gap(batch.Gap);
            if (clockConflict) Gap("clock-order-disagreement");
            token.ThrowIfCancellationRequested();
            if (!Admit()) return;
            SaveCursor(batch.Upper with { BootRecordId = bootId, BootTimeUtc = bootTime }, expected);
            if (Admit()) _log.Info($"PLATFORM_CAPTURE_COMPLETED channel=System; imported={rows.Length}; capped={batch.Capped}; reset={batch.Reset}; gap={batch.Gap is not null}; clockConflict={clockConflict}; cursorCommitted=true; causeEstablished=false.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Gap("capture-failed; completion-not-confirmed", error); }
        finally { _stop.Dispose(); }
    }

    private byte[]? ReadCursorBytes()
    {
        try
        {
            using var stream = new FileStream(_cursorPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is <= 0 or > MaximumCursorBytes) throw new InvalidDataException("Platform cursor length is invalid.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void Validate(PlatformCursor? cursor)
    {
        if (cursor is null || cursor.Version != 1 || cursor.Channel != "System" || cursor.RecordId <= 0 ||
            string.IsNullOrWhiteSpace(cursor.Provider) || cursor.Provider.Length > 256 || cursor.EventTimeUtc == default ||
            (cursor.BootRecordId is null) != (cursor.BootTimeUtc is null) || cursor.BootRecordId is <= 0 ||
            cursor.BootRecordId > cursor.RecordId)
            throw new InvalidDataException("Platform cursor metadata is invalid or unsupported.");
    }

    private void SaveCursor(PlatformCursor cursor, byte[]? expected)
    {
        LogFileWriter.UnderGate(_cursorPath, "platform-cursor", () =>
        {
            var current = ReadCursorBytes();
            if (expected is null ? current is not null : current is null || !current.SequenceEqual(expected))
                throw new IOException("Platform cursor changed concurrently; the newer state was preserved.");
            Directory.CreateDirectory(Path.GetDirectoryName(_cursorPath)!);
            var temporary = _cursorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(JsonSerializer.SerializeToUtf8Bytes(cursor)); file.Flush(flushToDisk: true); }
                if (current is null) File.Move(temporary, _cursorPath);
                else File.Replace(temporary, _cursorPath, null);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception cleanup) { Gap("cursor-temporary-cleanup-failed", cleanup); }
            }
            return true;
        });
    }

    private void Gap(string reason, Exception? error = null)
    {
        if (!Admit()) return;
        _log.Error(JsonSerializer.Serialize(new
        { eventName = "platform.capture.gap", source = "WindowsEventLog", channel = "System", reason, causeEstablished = false }), error);
    }

    public void Dispose()
    {
        lock (_sync) { if (_closed) return; _closed = true; }
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        // No join or cross-thread reader disposal. Already-admitted storage I/O
        // may finish; a returned/late reader cannot admit more writes or a cursor commit.
        if (!_started) _stop.Dispose();
    }
}
