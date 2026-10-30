using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace PinkieSysMon;

internal sealed class WindowsPlatformEventSource : IPlatformEventSource
{
    internal static bool IsAllowed(string provider, int id) => provider switch
    {
        "Microsoft-Windows-WHEA-Logger" => true,
        "Microsoft-Windows-Kernel-Power" => id == 41,
        "EventLog" => id is 6005 or 6006 or 6008 or 6009,
        "Microsoft-Windows-WER-SystemErrorReporting" or "BugCheck" => id == 1001,
        "Microsoft-Windows-Kernel-General" => id is 12 or 13,
        "Microsoft-Windows-Kernel-PnP" or "Microsoft-Windows-USB-USBHUB3" => true,
        _ => false
    };

    private const string Providers =
        "Provider[@Name='Microsoft-Windows-WHEA-Logger'] or " +
        "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or " +
        "(Provider[@Name='EventLog'] and (EventID=6005 or EventID=6006 or EventID=6008 or EventID=6009)) or " +
        "((Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck']) and EventID=1001) or " +
        "(Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12 or EventID=13)) or " +
        "Provider[@Name='Microsoft-Windows-Kernel-PnP'] or Provider[@Name='Microsoft-Windows-USB-USBHUB3']";

    public PlatformEventBatch Read(PlatformCursor? cursor, DateTimeOffset from, DateTimeOffset to,
        int maximum, CancellationToken token)
    {
        if (maximum is < 1 or > PlatformEventCapture.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var watch = Stopwatch.StartNew();
        token.ThrowIfCancellationRequested();
        var information = EventLogSession.GlobalSession.GetLogInformation("System", PathType.LogName);
        var created = information.CreationTime is { } time ? new DateTimeOffset(time.ToUniversalTime()) : (DateTimeOffset?)null;
        var upper = ReadOne("*", token, watch);
        if (upper is null) return new([], null, Gap: "system-channel-empty");
        var reset = cursor is not null && (cursor.RecordId > upper.RecordId || cursor.LogCreatedUtc != created);
        string? gap = null;
        if (cursor is not null && !reset)
        {
            var previous = ReadOne($"*[System[EventRecordID={cursor.RecordId.ToString(CultureInfo.InvariantCulture)}]]", token, watch);
            if (previous is null)
            {
                if (information.OldestRecordNumber is { } oldest && cursor.RecordId < oldest)
                    gap = "system-history-rolled-over; intervening-evidence-unavailable";
                else { reset = true; gap = "previous-record-identity-unavailable"; }
            }
            else if (previous.EventTimeUtc != cursor.EventTimeUtc || previous.Provider != cursor.Provider)
            { reset = true; gap = "system-record-id-reused"; }
        }
        var after = reset ? 0 : cursor?.RecordId ?? 0;
        var query = BuildQuery(from, to, after, upper.RecordId);
        using var reader = Reader(query);
        var rows = new List<PlatformEvent>();
        var capped = false;
        for (var index = 0; index <= maximum; index++)
        {
            token.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(5))
            { gap = "observed-query-budget-reached; older-matching-evidence-not-imported"; break; }
            using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(250));
            if (record is null) break;
            if (index == maximum) { capped = true; break; }
            rows.Add(Project(record));
        }
        token.ThrowIfCancellationRequested();
        return new(rows, new(1, "System", upper.RecordId, upper.Provider, upper.EventTimeUtc, created, null, null), reset, capped, gap);
    }

    internal static string BuildQuery(DateTimeOffset from, DateTimeOffset to, long after, long upper) =>
        $"*[System[({Providers}) and EventRecordID > {after.ToString(CultureInfo.InvariantCulture)} and " +
        $"EventRecordID <= {upper.ToString(CultureInfo.InvariantCulture)} and " +
        $"TimeCreated[@SystemTime >= '{Utc(from)}' and @SystemTime <= '{Utc(to)}']]]";

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
    private static EventLogReader Reader(string query) => new(new EventLogQuery("System", PathType.LogName, query)
    { ReverseDirection = true, TolerateQueryErrors = false });
    private static PlatformEvent? ReadOne(string query, CancellationToken token, Stopwatch watch)
    {
        token.ThrowIfCancellationRequested();
        if (watch.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Platform query observation budget was reached.");
        using var reader = Reader(query);
        using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(250));
        token.ThrowIfCancellationRequested();
        return record is null ? null : Project(record);
    }
    private static PlatformEvent Project(EventRecord record)
    {
        var provider = record.ProviderName;
        if (record.RecordId is not { } id || record.TimeCreated is not { } time ||
            string.IsNullOrWhiteSpace(provider))
            throw new InvalidDataException("Windows event identity/time metadata is incomplete.");
        return new(id, provider, record.Id, record.Level, new DateTimeOffset(time.ToUniversalTime()));
    }
}
