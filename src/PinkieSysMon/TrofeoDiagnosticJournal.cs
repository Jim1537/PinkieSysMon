using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon;

internal sealed class TrofeoDiagnosticJournal : IDisposable
{
    private const long DefaultMaxBytes = 8L * 1024 * 1024;
    private const int DefaultArchiveCount = 4;
    private static readonly TimeSpan DefaultDurableFlushInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxArchiveCount;
    private readonly TimeSpan _durableFlushInterval;

    private FileStream? _stream;
    private StreamWriter? _writer;
    private long _lastDurableFlushAt;
    private int _disposeState;

    internal TrofeoDiagnosticJournal(
        string path,
        long maxBytes = DefaultMaxBytes,
        int maxArchiveCount = DefaultArchiveCount,
        TimeSpan? durableFlushInterval = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Diagnostic journal path cannot be empty.", nameof(path));
        if (maxBytes < 256)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxArchiveCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxArchiveCount));

        _path = System.IO.Path.GetFullPath(path);
        _maxBytes = maxBytes;
        _maxArchiveCount = maxArchiveCount;
        _durableFlushInterval = durableFlushInterval ?? DefaultDurableFlushInterval;
        if (_durableFlushInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(durableFlushInterval));

        OpenWriterLocked();
    }

    public string Path => _path;

    public static string GetPath(string mainLogPath, string targetId)
    {
        if (string.IsNullOrWhiteSpace(mainLogPath))
            throw new ArgumentException("Main log path cannot be empty.", nameof(mainLogPath));
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Target ID cannot be empty.", nameof(targetId));

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainLogPath))
            ?? AppContext.BaseDirectory;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(targetId));
        var token = Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
        return System.IO.Path.Combine(directory, $"trofeo-usb-{token}.journal.log");
    }

    public void Write(string eventName, params (string Key, object? Value)[] fields)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        try
        {
            lock (_sync)
            {
                if (_disposeState != 0)
                    return;

                var line = FormatLine(eventName, fields);
                var incomingBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                RotateIfNeededLocked(incomingBytes);

                _writer!.WriteLine(line);
                _writer.Flush();
                DurableFlushIfDueLocked();
            }
        }
        catch (Exception ex)
        {
            // Diagnostics are evidence only. They must never destabilize the output path.
            Debug.WriteLine($"Trofeo diagnostic journal write failed: {ex}");
        }
    }

    public void FlushDurable()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        try
        {
            lock (_sync)
            {
                if (_disposeState != 0)
                    return;

                FlushDurableLocked();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Trofeo diagnostic journal flush failed: {ex}");
        }
    }

    private static string FormatLine(string eventName, IReadOnlyList<(string Key, object? Value)> fields)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Diagnostic event name cannot be empty.", nameof(eventName));

        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append('\t');
        builder.Append(Escape(eventName));

        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            builder.Append('\t');
            builder.Append(Escape(key));
            builder.Append('=');
            builder.Append(Escape(FormatValue(value)));
        }

        return builder.ToString();
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private void DurableFlushIfDueLocked()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastDurableFlushAt != 0 &&
            Stopwatch.GetElapsedTime(_lastDurableFlushAt, now) < _durableFlushInterval)
        {
            return;
        }

        FlushDurableLocked();
        _lastDurableFlushAt = now;
    }

    private void FlushDurableLocked()
    {
        _writer?.Flush();
        _stream?.Flush(flushToDisk: true);
    }

    private void RotateIfNeededLocked(int incomingBytes)
    {
        if (_stream is null || _stream.Length + incomingBytes <= _maxBytes)
            return;

        CloseWriterLocked(durable: true);

        var oldest = ArchivePath(_maxArchiveCount);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var i = _maxArchiveCount - 1; i >= 1; i--)
        {
            var source = ArchivePath(i);
            if (File.Exists(source))
                File.Move(source, ArchivePath(i + 1), overwrite: true);
        }

        if (File.Exists(_path))
            File.Move(_path, ArchivePath(1), overwrite: true);

        OpenWriterLocked();
    }

    private void OpenWriterLocked()
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _stream = new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
        _lastDurableFlushAt = Stopwatch.GetTimestamp();
    }

    private void CloseWriterLocked(bool durable)
    {
        if (durable)
            FlushDurableLocked();
        else
            _writer?.Flush();

        _writer?.Dispose();
        _writer = null;
        _stream?.Dispose();
        _stream = null;
    }

    private string ArchivePath(int index) => $"{_path}.{index}";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            lock (_sync)
                CloseWriterLocked(durable: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Trofeo diagnostic journal disposal failed: {ex}");
        }
    }
}
