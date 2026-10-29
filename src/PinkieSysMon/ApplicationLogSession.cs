namespace PinkieSysMon;

// A named event remains a lifetime marker for overlapping sessions. Log retention
// is independent of the lease: startup never truncates a previous session.
internal sealed class ApplicationLogSession : IDisposable
{
    private readonly FileLogger _log;
    private EventWaitHandle? _lifetime;
    private System.Threading.Timer? _maintenanceTimer;
    private int _maintenanceRunning;
    private bool _completed;
    private volatile bool _disposed;

    private ApplicationLogSession(FileLogger log, string mode)
    {
        _log = log;
        try
        {
            LogFileWriter.UnderGate(log.Path, "session", () =>
            {
                _lifetime = new EventWaitHandle(false, EventResetMode.ManualReset,
                    LogFileWriter.ObjectName(log.Path, "lifetime"), out var first);
                LogFileWriter.Maintain(log.Path);
                LogFileWriter.Maintain(log.ErrorsPath);
                // Preserve the established session-admission contract: a
                // running app has an empty error file even before first error.
                // Ordinary FileLogger instances without a session stay sparse.
                LogFileWriter.EnsureExists(log.ErrorsPath);
                log.Info($"SESSION_START mode={mode}; version={RuntimeVersion.Current}; " +
                    $"newLogGroup={first}; rollingHistoryDays={LogRetentionPolicy.GetRetentionDays(log.Path)}; previousSessionsRetained=true.");
                return true;
            });
            // Independent of logging cadence: cleanup continues through quiet periods.
            _maintenanceTimer = new System.Threading.Timer(
                static state => ((ApplicationLogSession)state!).MaintainNow(),
                this, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        catch (Exception error)
        {
            // A retention failure must not discard previous session evidence.
            log.Error("Log session admission failed; no further reset will be attempted.", error);
        }
    }

    internal static ApplicationLogSession Start(FileLogger log, string mode) => new(log, mode);
    internal void Ready() => _log.Info("SESSION_READY");
    internal void MarkCompleted() => _completed = true;

    // One in-process observer per admitted session; shared file gates prevent
    // overlapping Editor processes from pruning an open writer's segment.
    internal void MaintainNow()
    {
        if (_disposed || Interlocked.CompareExchange(ref _maintenanceRunning, 1, 0) != 0) return;
        try
        {
            foreach (var path in new[] { _log.Path, _log.ErrorsPath })
            {
                if (_disposed) break;
                try { LogFileWriter.Maintain(path); }
                catch (Exception error)
                {
                    // A retention failure must not remove evidence or stop Runtime.
                    _log.Error("Rolling log maintenance failed.", error);
                }
            }
        }
        finally { Volatile.Write(ref _maintenanceRunning, 0); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _maintenanceTimer?.Dispose();
        _maintenanceTimer = null;
        try
        {
            LogFileWriter.UnderGate(_log.Path, "session", () =>
            {
                _log.FlushSuppressed();
                _log.Info(_completed ? "SESSION_END normalExit=true" : "SESSION_END normalExit=false");
                _lifetime?.Dispose();
                _lifetime = null;
                return true;
            });
        }
        catch (Exception error)
        {
            _log.Error("Log session finalization failed; terminal session status is unconfirmed.", error);
        }
        finally
        {
            _lifetime?.Dispose();
            _lifetime = null;
        }
    }
}
