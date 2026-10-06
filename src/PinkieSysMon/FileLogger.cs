using System.Diagnostics;
using System.Text;

namespace PinkieSysMon;

internal sealed class FileLogger
{
    private const long MaxLogBytes = 5L * 1024 * 1024;
    private const int MaxArchiveCount = 5;

    private readonly string _path;
    private readonly object _sync = new();
    private readonly Dictionary<string, ThrottleState> _throttle = new(StringComparer.Ordinal);

    public FileLogger(string path)
    {
        _path = path;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PinkieSysMon logger initialization failed: {ex}");
        }
    }

    public string Path => _path;

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public void WarnThrottled(string key, TimeSpan interval, string message, Exception? ex = null) =>
        WriteThrottled(key, interval, "WARN", message, ex);

    public void ErrorThrottled(string key, TimeSpan interval, string message, Exception? ex = null) =>
        WriteThrottled(key, interval, "ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (_sync)
                WriteLocked(level, message, ex);
        }
        catch (Exception logEx)
        {
            ReportLoggingFailure(level, message, ex, logEx);
        }
    }

    private void WriteThrottled(
        string key,
        TimeSpan interval,
        string level,
        string message,
        Exception? ex)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Throttle key cannot be empty.", nameof(key));
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        try
        {
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                if (_throttle.TryGetValue(key, out var state) && now - state.LastWrite < interval)
                {
                    _throttle[key] = state with { Suppressed = state.Suppressed + 1 };
                    return;
                }

                var suppressed = state?.Suppressed ?? 0;
                _throttle[key] = new ThrottleState(now, 0);
                if (suppressed > 0)
                    message += $" ({suppressed} repeated messages suppressed)";

                WriteLocked(level, message, ex);
            }
        }
        catch (Exception logEx)
        {
            ReportLoggingFailure(level, message, ex, logEx);
        }
    }

    private void WriteLocked(string level, string message, Exception? ex)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        if (ex is not null)
            line += Environment.NewLine + ex;
        line += Environment.NewLine;

        TryRotateLocked(Encoding.UTF8.GetByteCount(line));
        File.AppendAllText(_path, line);
    }

    private void TryRotateLocked(int incomingBytes)
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var length = new FileInfo(_path).Length;
            if (length + incomingBytes <= MaxLogBytes)
                return;

            var oldest = ArchivePath(MaxArchiveCount);
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (var i = MaxArchiveCount - 1; i >= 1; i--)
            {
                var source = ArchivePath(i);
                if (File.Exists(source))
                    File.Move(source, ArchivePath(i + 1), overwrite: true);
            }

            File.Move(_path, ArchivePath(1), overwrite: true);
        }
        catch (Exception ex)
        {
            // Rotation failure must not turn diagnostics into a runtime failure.
            Debug.WriteLine($"PinkieSysMon log rotation failed: {ex}");
        }
    }

    private string ArchivePath(int index) => $"{_path}.{index}";

    private static void ReportLoggingFailure(string level, string message, Exception? ex, Exception logEx)
    {
        Debug.WriteLine($"PinkieSysMon logging failed [{level}] {message}: {logEx}");
        if (ex is not null)
            Debug.WriteLine(ex);
    }

    private sealed record ThrottleState(DateTimeOffset LastWrite, int Suppressed);
}
