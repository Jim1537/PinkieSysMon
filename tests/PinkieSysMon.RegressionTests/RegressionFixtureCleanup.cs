using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

// Failure evidence for these tests' own scratch directories only. Never retries
// deletion, releases another owner's handle, or shuts down/restarts a process.
internal static class RegressionFixtureCleanup
{
    internal const int MaximumFiles = 64;
    private const uint MaximumProcesses = 256;
    private const int ErrorMoreData = 234;

    internal static void Delete(string root, Exception? scenarioFailure = null, bool? editorDisposed = null,
        Action<string>? delete = null, Func<string, bool?, string>? capture = null)
    {
        try
        {
            if (delete is null) Directory.Delete(root, recursive: true);
            else delete(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            string evidence;
            try { evidence = (capture ?? Capture)(root, editorDisposed); }
            catch (Exception observationError)
            {
                evidence = $"captureUnavailable={observationError.GetType().Name}; hResult=0x{observationError.HResult:X8}";
            }
            var augmented = new IOException(
                $"Fixture cleanup failed once; originalHResult=0x{error.HResult:X8}; FIXTURE_CLEANUP_DIAGNOSTICS {evidence}", error);
            if (scenarioFailure is not null)
                throw new AggregateException("Both the scenario and its fixture cleanup failed.", scenarioFailure, augmented);
            throw augmented;
        }
    }

    internal static string Capture(string root, bool? editorDisposed)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var sample = Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        }).Take(MaximumFiles + 1).ToArray();
        var files = sample.Take(MaximumFiles).ToArray();
        // Query users before probes so our short-lived probe handles cannot
        // appear in the Restart Manager observation.
        var users = FindResourceUsers(files);
        var probes = files.Select(path => Probe(root, path)).ToArray();
        return JsonSerializer.Serialize(new
        {
            observedAt, completedAt = DateTimeOffset.UtcNow, fixture = Path.GetFileName(root),
            currentPid = Environment.ProcessId, editorDisposed, filesOmitted = sample.Length > MaximumFiles,
            restartManager = users, files = probes,
            limits = "Resource users and exclusive-open probes are later snapshots, not proof of the deleting handle or cause. Empty/unavailable results do not rule out transient or kernel/filter-driver locks."
        });
    }

    private static object Probe(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return new { file = relative, observedAt = DateTimeOffset.UtcNow, exclusiveReadOpened = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new { file = relative, observedAt = DateTimeOffset.UtcNow, exclusiveReadOpened = false,
                errorType = error.GetType().Name, hResult = $"0x{error.HResult:X8}" };
        }
    }

    private static ResourceUsers FindResourceUsers(string[] files)
    {
        var result = new ResourceUsers();
        if (files.Length == 0) { result.Status = "no-remaining-files"; return result; }
        if (!OperatingSystem.IsWindows()) { result.Status = "platform-unavailable"; return result; }
        uint session = 0;
        var started = false;
        try
        {
            result.StartCode = RmStartSession(out session, 0, new StringBuilder(33));
            if (result.StartCode != 0) { result.Status = "start-failed"; return result; }
            started = true;
            result.RegisterCode = RmRegisterResources(session, (uint)files.Length, files, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (result.RegisterCode != 0) { result.Status = "register-failed"; return result; }
            uint count = 0;
            result.ListCode = RmGetList(session, out var needed, ref count, null, out var rebootReasons);
            result.RequiredProcesses = needed;
            result.RebootReasons = rebootReasons;
            if (result.ListCode == 0 && needed == 0) { result.Status = "no-reported-users"; return result; }
            if (result.ListCode != ErrorMoreData) { result.Status = "list-failed"; return result; }
            if (needed == 0 || needed > MaximumProcesses) { result.Status = "process-bound-exceeded"; return result; }
            var processes = new RmProcessInfo[(int)needed];
            count = needed;
            result.ListCode = RmGetList(session, out needed, ref count, processes, out rebootReasons);
            result.RequiredProcesses = needed;
            result.RebootReasons = rebootReasons;
            if (result.ListCode != 0) { result.Status = "list-changed-or-failed"; return result; }
            if (count > (uint)processes.Length) { result.Status = "invalid-returned-count"; return result; }
            result.Users = processes.Take((int)count).Select(p => new ResourceUser(
                p.Process.ProcessId, ((ulong)(uint)p.Process.StartTime.dwHighDateTime << 32) |
                    (uint)p.Process.StartTime.dwLowDateTime,
                p.Process.ProcessId == (uint)Environment.ProcessId,
                Bound(p.ApplicationName), p.ApplicationType)).ToArray();
            result.Status = result.Users.Length == 0 ? "no-reported-users" : "users-reported";
        }
        catch (Exception error)
        {
            result.Status = "query-unavailable";
            result.QueryErrorType = error.GetType().Name;
            result.QueryHResult = $"0x{error.HResult:X8}";
        }
        finally
        {
            if (started)
            {
                try { result.EndCode = RmEndSession(session); }
                catch (Exception error) { result.EndErrorType = error.GetType().Name; }
            }
            result.CompletedAt = DateTimeOffset.UtcNow;
        }
        return result;
    }

    private static string? Bound(string? value) => value?.Length > 128 ? value[..128] : value;

    private sealed class ResourceUsers
    {
        public string Status { get; set; } = "not-queried";
        public DateTimeOffset ObservedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; set; }
        public int? StartCode { get; set; }
        public int? RegisterCode { get; set; }
        public int? ListCode { get; set; }
        public int? EndCode { get; set; }
        public uint RequiredProcesses { get; set; }
        public uint RebootReasons { get; set; }
        public ResourceUser[] Users { get; set; } = [];
        public string? QueryErrorType { get; set; }
        public string? QueryHResult { get; set; }
        public string? EndErrorType { get; set; }
    }

    private sealed record ResourceUser(uint Pid, ulong ProcessStartFileTime, bool IsCurrentPid,
        string? ReportedApplicationName, uint ApplicationType);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RmUniqueProcess
    {
        public uint ProcessId;
        public FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? ServiceName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RmStartSession(out uint session, uint flags, [Out] StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RmRegisterResources(uint session, uint fileCount,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] files,
        uint processCount, IntPtr processes, uint serviceCount, IntPtr services);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RmGetList(uint session, out uint needed, ref uint count,
        [In, Out] RmProcessInfo[]? processes, out uint rebootReasons);

    [DllImport("rstrtmgr.dll", ExactSpelling = true)]
    private static extern int RmEndSession(uint session);
}
