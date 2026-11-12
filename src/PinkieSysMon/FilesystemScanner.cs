using System.Diagnostics;
using System.Security;

namespace PinkieSysMon;

internal enum FilesystemScanStatus
{
    Complete,
    InvalidPath,
    PathNotFound,
    AccessDenied,
    ReparseRoot,
    WrongKind,
    EntryLimit,
    TimedOut,
    Cancelled,
    Overflow,
    IoError
}

// SizeBytes and timestamps are populated ONLY for a complete traversal.
internal sealed record FilesystemScanResult(
    FilesystemScanStatus Status, long? SizeBytes, DateTime? Created,
    DateTime? Modified, int Entries, int Files, int Directories,
    int SkippedReparse, TimeSpan Elapsed, string? Error)
{
    public bool Complete => Status == FilesystemScanStatus.Complete;
}

internal static class FilesystemScanner
{
    public const int MaximumEntries = 250_000;
    public static readonly TimeSpan MaximumElapsed = TimeSpan.FromSeconds(15);

    private static readonly EnumerationOptions EntriesOptions = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    // elapsedOverride exists solely for deterministic budget regressions.
    // Native filesystem calls are cooperative: one hung OS call cannot be forcibly interrupted.
    public static FilesystemScanResult Scan(
        FilesystemSourceConfig source, CancellationToken cancellation = default,
        Func<TimeSpan>? elapsedOverride = null) =>
        ScanCore(source, cancellation, elapsedOverride, MaximumEntries);

    // A smaller injected bound tests the exact-limit/completed and +1/incomplete
    // branches without producing hundreds of thousands of regression fixtures.
    internal static FilesystemScanResult ScanWithEntryLimitForTests(FilesystemSourceConfig source, int limit)
    {
        if (limit <= 0 || limit > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(limit));
        return ScanCore(source, default, null, limit);
    }

    private static FilesystemScanResult ScanCore(
        FilesystemSourceConfig source, CancellationToken cancellation,
        Func<TimeSpan>? elapsedOverride, int entryLimit)
    {
        var watch = Stopwatch.StartNew();
        TimeSpan Elapsed() => elapsedOverride?.Invoke() ?? watch.Elapsed;
        var entries = 0;
        var files = 0;
        var directories = 0;
        var skipped = 0;

        FilesystemScanResult Failure(FilesystemScanStatus status, string reason) =>
            new(status, null, null, null, entries, files, directories, skipped, Elapsed(), reason);
        void Check()
        {
            cancellation.ThrowIfCancellationRequested();
            if (Elapsed() > MaximumElapsed)
                throw new ScanBudgetException(FilesystemScanStatus.TimedOut);
        }

        try
        {
            FilesystemSourceContract.Validate(new FilesystemConfig { Sources = [source.Copy()] });
            Check();
            var attributes = File.GetAttributes(source.Path);
            Check();
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return Failure(FilesystemScanStatus.ReparseRoot, "A reparse-point root is not allowed.");
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory != (source.Kind == FilesystemSourceContract.DirectoryKind))
                return Failure(FilesystemScanStatus.WrongKind, "Source path does not match File/Directory kind.");

            long total = 0;
            DateTime created, modified;
            if (!isDirectory)
            {
                var info = new FileInfo(source.Path);
                total = info.Length;
                created = info.CreationTime;
                modified = info.LastWriteTime;
                files = 1;
                Check();
            }
            else
            {
                var root = new DirectoryInfo(source.Path);
                created = root.CreationTime;
                modified = root.LastWriteTime;
                Check();

                // Iterative traversal: at most one open enumerator at a time.
                // The stack and counts are bounded by MaximumEntries, not by recursion depth.
                var pending = new Stack<string>();
                pending.Push(source.Path);
                while (pending.TryPop(out var directory))
                {
                    Check();
                    foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", EntriesOptions))
                    {
                        Check();
                        if (++entries > entryLimit)
                            throw new ScanBudgetException(FilesystemScanStatus.EntryLimit);

                        var flags = item.Attributes;
                        Check();
                        if ((flags & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        if ((flags & FileAttributes.Directory) != 0)
                        {
                            directories++;
                            pending.Push(item.FullName);
                        }
                        else
                        {
                            files++;
                            if (item is not FileInfo file)
                                throw new IOException("Unexpected non-file filesystem entry.");
                            total = checked(total + file.Length);
                        }
                        Check();
                    }
                }
            }

            Check();
            return new FilesystemScanResult(FilesystemScanStatus.Complete, total, created, modified,
                entries, files, directories, skipped, Elapsed(), null);
        }
        catch (ScanBudgetException error)
        {
            return Failure(error.Status, error.Status == FilesystemScanStatus.EntryLimit
                ? "Too many filesystem entries (250,000)." : "Scan timed out (15 s).");
        }
        catch (OperationCanceledException)
        {
            return Failure(FilesystemScanStatus.Cancelled, "Scan cancelled.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(FilesystemScanStatus.AccessDenied, "Access denied.");
        }
        catch (SecurityException)
        {
            return Failure(FilesystemScanStatus.AccessDenied, "Access denied.");
        }
        catch (FileNotFoundException)
        {
            return Failure(FilesystemScanStatus.PathNotFound, "Path not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(FilesystemScanStatus.PathNotFound, "Path not found.");
        }
        catch (OverflowException)
        {
            return Failure(FilesystemScanStatus.Overflow, "Logical size exceeds 64-bit range.");
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(FilesystemScanStatus.InvalidPath, "Invalid source path or configuration.");
        }
        catch (IOException)
        {
            return Failure(FilesystemScanStatus.IoError, "Filesystem metadata or enumeration failed.");
        }
    }

    private sealed class ScanBudgetException(FilesystemScanStatus status) : Exception
    {
        public FilesystemScanStatus Status { get; } = status;
    }
}
