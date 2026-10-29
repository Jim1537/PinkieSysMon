using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

// All cooperating writers use the same path-derived, cross-session gate.
// The timeout bounds gate acquisition, not an underlying filesystem call.
internal static class LogFileWriter
{
    internal static string ObjectName(string path, string purpose)
    {
        var canonical = System.IO.Path.GetFullPath(path).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return @"Global\PinkieSysMon.Logging." + purpose + "." + hash;
    }

    internal static T UnderGate<T>(string path, string purpose, Func<T> action,
        int timeoutMs = 2000)
    {
        using var gate = new Mutex(false, ObjectName(path, purpose));
        var owned = false;
        try
        {
            try { owned = gate.WaitOne(timeoutMs); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new TimeoutException("The log writer gate is busy.");
            return action();
        }
        finally
        {
            if (owned) gate.ReleaseMutex();
        }
    }

    // Three logical streams share this implementation. Their closed segments are
    // rotated by UTC quarter-hour and capped individually; no session reset is allowed.
    internal const long MaximumSegmentBytes = 4L * 1024 * 1024;
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(72);
    private static readonly TimeSpan BucketWidth = TimeSpan.FromMinutes(15);

    internal static void Append(string path, string record, bool durable)
    {
        UnderGate(path, "write", () =>
        {
            var now = DateTime.UtcNow;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(record + Environment.NewLine);
            RotateIfNecessary(path, now, bytes.Length);
            using (var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.Read))
            {
                if (stream.Length > 0)
                {
                    stream.Position = stream.Length - 1;
                    var last = stream.ReadByte();
                    stream.Position = stream.Length;
                    // A crash may leave an incomplete final line; never overwrite it.
                    if (last != '\n') stream.WriteByte((byte)'\n');
                }
                stream.Write(bytes);
                stream.Flush(flushToDisk: durable);
            }
            return true;
        });
    }

    // Called at application admission and periodically by active loggers. This
    // also retires a quiet Errors.log without waiting for a new error to occur.
    internal static void Maintain(string path, DateTimeOffset? clock = null)
    {
        UnderGate(path, "write", () =>
        {
            var now = (clock ?? DateTimeOffset.UtcNow).UtcDateTime;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            // A quiet maintenance pass must not invent an Errors.log when
            // no error has ever been recorded. Still prune old archives.
            // If a stale active stream is rotated, recreate its active slot.
            if (File.Exists(path))
            {
                RotateIfNecessary(path, now, 0);
                using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)) { }
            }
            PruneExpired(path, now);
            return true;
        });
    }

    private static void RotateIfNecessary(string path, DateTime now, int incomingBytes)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.Length == 0) return;

        var lastWrite = info.LastWriteTimeUtc;
        var changedBucket = lastWrite.Ticks / BucketWidth.Ticks != now.Ticks / BucketWidth.Ticks;
        var exceedsSize = incomingBytes > 0 && info.Length + incomingBytes > MaximumSegmentBytes;
        if (!changedBucket && !exceedsSize) return;

        // Force all previously buffered bytes to stable storage before moving.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            stream.Flush(flushToDisk: true);

        // Archive names are unique even when a single time bucket fills repeatedly.
        var archive = path + "." + lastWrite.ToString("yyyyMMddTHHmmssfffffffZ",
            System.Globalization.CultureInfo.InvariantCulture) +
            "." + Guid.NewGuid().ToString("N") + ".archive";
        File.Move(path, archive);
    }

    private static void PruneExpired(string path, DateTime now)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var prefix = System.IO.Path.GetFileName(path) + ".";
        var cutoff = now - Retention;
        foreach (var archive in Directory.EnumerateFiles(directory, prefix + "*.archive"))
        {
            var name = System.IO.Path.GetFileName(archive);
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var suffix = name[prefix.Length..];
            // Never delete unrelated files merely because they share the prefix.
            if (suffix.Length != 64 || suffix[8] != 'T' || suffix[22] != 'Z' ||
                suffix[23] != '.' || !suffix.EndsWith(".archive", StringComparison.Ordinal) ||
                !Guid.TryParseExact(suffix.Substring(24, 32), "N", out _)) continue;
            if (Enumerable.Range(0, 23).Any(i =>
                i != 8 && i != 22 && !char.IsAsciiDigit(suffix[i]))) continue;
            // LastWriteTimeUtc is the upper bound on records originally written to
            // this closed segment. Keep any segment that overlaps the 72h window.
            if (File.GetLastWriteTimeUtc(archive) < cutoff)
                File.Delete(archive);
        }
    }

    internal static void EnsureExists(string path)
    {
        UnderGate(path, "write", () =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.Read);
            return true;
        });
    }

}
