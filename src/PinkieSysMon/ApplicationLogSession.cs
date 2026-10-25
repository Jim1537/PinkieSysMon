namespace PinkieSysMon;

// A named event is a lifetime lease, not a notification channel. Windows closes
// process handles on exit/crash; a new group exists only after every lease closes.
internal sealed class ApplicationLogSession : IDisposable
{
    private readonly FileLogger _log;
    private EventWaitHandle? _lifetime;
    private bool _completed;
    private bool _disposed;

    private ApplicationLogSession(FileLogger log, string mode)
    {
        _log = log;
        try
        {
            LogFileWriter.UnderGate(log.Path, "session", () =>
            {
                _lifetime = new EventWaitHandle(false, EventResetMode.ManualReset,
                    LogFileWriter.ObjectName(log.Path, "lifetime"), out var first);
                if (first) LogFileWriter.Reset(log.Path);
                LogFileWriter.EnsureExists(log.ErrorsPath);
                log.Info($"SESSION_START mode={mode}; version={RuntimeVersion.Current}; " +
                    $"newLogGroup={first}; errorHistory=append-only.");
                return true;
            });
        }
        catch (Exception error)
        {
            // Do not retry a failed admission/reset; preserve the remaining evidence.
            log.Error("Log session admission failed; no further reset will be attempted.", error);
        }
    }

    internal static ApplicationLogSession Start(FileLogger log, string mode) => new(log, mode);
    internal void Ready() => _log.Info("SESSION_READY");
    internal void MarkCompleted() => _completed = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
