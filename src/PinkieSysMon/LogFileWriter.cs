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

    internal static void Append(string path, string record, bool durable)
    {
        UnderGate(path, "write", () =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length > 0)
            {
                stream.Position = stream.Length - 1;
                var last = stream.ReadByte();
                stream.Position = stream.Length;
                // Preserve an interrupted final record and start a new boundary.
                if (last != '\n') stream.WriteByte((byte)'\n');
            }
            var bytes = Encoding.UTF8.GetBytes(record + Environment.NewLine);
            stream.Write(bytes);
            stream.Flush(flushToDisk: durable);
            return true;
        });
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

    internal static void Reset(string path)
    {
        UnderGate(path, "write", () =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Create,
                FileAccess.Write, FileShare.Read);
            return true;
        });
    }
}
