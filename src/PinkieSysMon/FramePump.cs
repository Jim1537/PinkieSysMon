using System.Diagnostics;

namespace PinkieSysMon;

internal sealed class FramePump : IOutputSession
{
    private const int RuntimeMetricsPublishIntervalMs = 1000;
    private static readonly TimeSpan HotPathErrorLogThrottleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly MetricStore _metrics;
    private readonly Func<DateTime>? _frameClockNow;
    private readonly FileLogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private readonly string _targetId;
    private readonly int? _maximumFrames;
    private readonly bool _stopOnFailure;
    private Exception? _testFailure;
    private bool _testCleanupFailed;
    private bool _nativeReleaseFailed;
    public bool NativeReleaseConfirmed => Volatile.Read(ref _disposeState) != 0 &&
        (_worker is null || _worker.IsCompleted) && !Volatile.Read(ref _nativeReleaseFailed);
    internal Exception? TestFailure => Volatile.Read(ref _testFailure);
    internal bool TestCleanupFailed => Volatile.Read(ref _testCleanupFailed);
    private void RecordTestFailure(Exception error)
    {
        if (_stopOnFailure) Interlocked.CompareExchange(ref _testFailure, error, null);
    }
    private readonly Func<OutputTargetConfig, int, FileLogger, CancellationToken, IPhysicalFrameTransport?> _transportFactory;
    private readonly Func<IReadOnlyList<OutputDeviceDescriptor>> _turzxRecoveryDiscovery;

    private AppConfig _config;
    private OutputTargetConfig _target;
    private string _targetName;
    private DashboardRenderer _renderer;
    private IPhysicalFrameTransport? _display;
    private CancellationTokenSource _transportIoCancellation;
    private long _nextUsbAttemptAtMs;
    private long _connectionGeneration;
    private bool _transportFaulted;
    private bool _transportCleanupFailed;
    private bool _transportAbsenceObserved;
    private bool _transportRetirePending;
    private string _transportRetireReason = "Output lifecycle changed.";
    private string _transportRetireState = "WAITING";
    private Task? _worker;
    private bool _suspended;
    private bool _idlePaused;
    private bool _resetFrameSchedule;
    private bool _publishRuntimeMetrics;
    private long _sentFrames;
    private double _lastFrameMs;
    private double _lastRenderMs;
    private double _lastEncodeMs;
    private double _lastUsbMs;
    private int _lastJpegBytes;
    private double _lastFps;
    private long _lastFrameTimestamp;
    private long _lastRuntimeMetricsPublishAtMs;
    private string _usbState = "WAITING";
    private bool _isConnected;
    private bool _connectionHasFrame;
    private string LogTarget => $"Output target '{TargetName}' [targetId={_targetId}; protocol={_target.Transport}]";
    private int _disposeState;

    public FramePump(
        AppConfig config,
        OutputTargetConfig target,
        global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard,
        MetricStore metrics,
        FileLogger log,
        Func<OutputTargetConfig, int, FileLogger, CancellationToken, IPhysicalFrameTransport?>? transportFactory = null,
        int? maximumFrames = null, Func<IReadOnlyList<OutputDeviceDescriptor>>? turzxRecoveryDiscovery = null,
        Func<DateTime>? frameClockNow = null, bool stopOnFailure = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);

        if (maximumFrames is < 1) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        if (target.Transport == OutputTargetContract.EncryptedJpegTransport)
            TurzxFrameTransport.ValidateOptions(target);
        _maximumFrames = maximumFrames;
        _stopOnFailure = stopOnFailure;
        _config = config;
        _target = target;
        _turzxRecoveryDiscovery = turzxRecoveryDiscovery ?? (() => PhysicalDeviceDiscovery.CaptureForTarget(_target));
        _targetId = target.Id;
        _targetName = target.Name;
        _metrics = metrics;
        _log = log;
        _frameClockNow = frameClockNow;
        _transportIoCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _transportFactory = transportFactory ??
            (static (selectedTarget, timeoutMs, logger, ioToken) =>
                PhysicalFrameTransportFactory.TryOpen(selectedTarget, timeoutMs, logger, ioToken));
        _renderer = new DashboardRenderer(dashboard, _frameClockNow, _metrics.History);
    }

    public string TargetId => _targetId;
    public string TargetName => Volatile.Read(ref _targetName);

    // Status is sampled by the WinForms tray timer. Do not make the UI thread wait for
    // the render/USB critical section, which may legitimately block until the USB timeout.
    public bool IsConnected => Volatile.Read(ref _isConnected);
    public long SentFrames => Interlocked.Read(ref _sentFrames);
    public double LastFrameMs => Volatile.Read(ref _lastFrameMs);
    public int LastJpegBytes => Volatile.Read(ref _lastJpegBytes);
    public string UsbState => Volatile.Read(ref _usbState);

    public void Start()
    {
        if (_worker is not null)
            return;

        _log.Info($"{LogTarget} started.");
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public IOutputSessionReconfiguration PrepareReconfiguration(
        AppConfig config,
        OutputTargetConfig target,
        global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);

        if (!target.Id.Equals(_targetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Output session '{_targetId}' cannot be reconfigured as target '{target.Id}'.");
        }

        // All renderer construction and asset loading happens before the live session is mutated.
        return new PreparedReconfiguration(
            config,
            target,
            dashboard.Canvas.Width,
            dashboard.Canvas.Height,
            dashboard.Canvas.Orientation,
            new DashboardRenderer(dashboard, _frameClockNow, _metrics.History));
    }

    public IOutputSessionReconfiguration PrepareRollbackReconfiguration(
        AppConfig config, OutputTargetConfig target,
        global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);
        if (!target.Id.Equals(_targetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Rollback target does not match the active Output session.");

        lock (_sync)
        {
            return new PreparedReconfiguration(config, target,
                dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation,
                _renderer.Retain());
        }
    }

    public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration)
    {
        if (reconfiguration is not PreparedReconfiguration prepared)
            throw new ArgumentException("Unexpected output-session reconfiguration object.", nameof(reconfiguration));
        if (!prepared.Target.Id.Equals(_targetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prepared output-session target does not match this session.");

        DashboardRenderer oldRenderer;
        lock (_sync)
        {
            oldRenderer = _renderer;
            _renderer = prepared.TakeRenderer();

            var oldUsb = _target.Usb ?? _config.Usb;
            var newUsb = prepared.Target.Usb ?? prepared.Config.Usb;
            var outputConnectionChanged =
                oldUsb.TransferTimeoutMs != newUsb.TransferTimeoutMs ||
                !OutputTargetContract.ConnectionEquals(_target, prepared.Target);
            var runtimeMetricsWereEnabled = RuntimeMetricsEnabled;

            _config = prepared.Config;
            _target = prepared.Target;
            Volatile.Write(ref _targetName, prepared.Target.Name);

            var runtimeMetricsEnabled = RuntimeMetricsEnabled;
            if (runtimeMetricsWereEnabled && !runtimeMetricsEnabled)
            {
                ClearRuntimeMetrics();
            }
            else if (!runtimeMetricsWereEnabled && runtimeMetricsEnabled)
            {
                _lastRuntimeMetricsPublishAtMs = 0;
                PublishRuntimeMetricIdentityLocked();
            }

            if (outputConnectionChanged)
            {
                _transportAbsenceObserved = false;
                RequestTransportRetirementLocked(
                    "Output target or USB timeout changed; reconnecting.",
                    "WAITING");
            }

            _nextUsbAttemptAtMs = 0;
        }

        try
        {
            oldRenderer.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warn($"{LogTarget} previous renderer disposal failed after reconfiguration.", ex);
        }

        SignalWake();
        _log.Info(
            $"{LogTarget} reconfigured. refresh={prepared.Target.RefreshIntervalMs ?? prepared.Config.Display.RefreshIntervalMs}ms; " +
            $"canvas={prepared.CanvasWidth}x{prepared.CanvasHeight}; orientation={prepared.Orientation}; " +
            $"targetId={_targetId}; jpegQuality={prepared.Target.JpegQuality ?? prepared.Config.Renderer.JpegQuality}.");
    }

    public void SetRuntimeMetricsPublisher(bool enabled)
    {
        lock (_sync)
        {
            if (_publishRuntimeMetrics == enabled)
                return;

            var wasEnabled = RuntimeMetricsEnabled;
            _publishRuntimeMetrics = enabled;
            var isEnabled = RuntimeMetricsEnabled;
            if (wasEnabled && !isEnabled)
                ClearRuntimeMetrics();
            else if (!wasEnabled && isEnabled)
            {
                _lastRuntimeMetricsPublishAtMs = 0;
                PublishRuntimeMetricIdentityLocked();
            }
        }
    }

    public void SetIdlePaused(bool paused)
    {
        lock (_sync)
        {
            if (_idlePaused == paused)
                return;

            _idlePaused = paused;
            _resetFrameSchedule = true;
            _lastFrameTimestamp = 0;
            _lastFps = 0;
            _lastRuntimeMetricsPublishAtMs = 0;
            if (paused)
                SetUsbStateLocked("IDLE_PAUSED");
            else
            {
                // Preserve the USB handle; prove readiness with the next real frame.
                _nextUsbAttemptAtMs = 0;
                SetUsbStateLocked(_suspended ? "SUSPENDED" : "WAITING");
            }
        }
        SignalWake();
    }

    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
            RequestTransportRetirementLocked("System suspend.", "SUSPENDED");
        }

        _log.Info($"{LogTarget} suspended.");
        SignalWake();
    }

    public void Resume()
    {
        lock (_sync)
        {
            _suspended = false;
            _nextUsbAttemptAtMs = 0;
            // The old connection is invalid even if suspend and resume race with a USB call.
            RequestTransportRetirementLocked("System resume; discard prior USB handle.",
                _idlePaused ? "IDLE_PAUSED" : "WAITING");
        }

        _log.Info($"{LogTarget} resumed.");
        SignalWake();
    }

    public void RequestFrame()
    {
        lock (_sync)
        {
            if (_disposeState != 0 || _worker is null || _suspended || _idlePaused)
                return;
            _resetFrameSchedule = true;
        }
        SignalWake();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var nextFrameAt = Stopwatch.GetTimestamp();

        while (!token.IsCancellationRequested && Volatile.Read(ref _disposeState) == 0)
        {
            int delayMs;
            var intervalMs = 1000;

            try
            {
                lock (_sync)
                    intervalMs = _target.RefreshIntervalMs ?? _config.Display.RefreshIntervalMs;

                // The output worker is the sole owner of all WinUSB operations and disposal.
                // No potentially blocking device operation may execute under _sync.
                RenderAndSendWorker(token);

                lock (_sync)
                    delayMs = GetNextDelayMsLocked(intervalMs, ref nextFrameAt);
            }
            catch (Exception ex)
            {
                RecordTestFailure(ex);
                _log.ErrorThrottled(
                    $"frame.loop.{_targetId}",
                    HotPathErrorLogThrottleInterval,
                    $"{LogTarget} frame loop error",
                    ex);
                delayMs = Math.Max(1, intervalMs);
                nextFrameAt = Stopwatch.GetTimestamp();
            }

            if (Volatile.Read(ref _disposeState) != 0)
                break;

            try
            {
                if (delayMs <= 0)
                {
                    await Task.Yield();
                }
                else
                {
                    await WaitForWakeOrDelayAsync(delayMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private int GetNextDelayMsLocked(int intervalMs, ref long nextFrameAt)
    {
        if (_transportRetirePending)
            return 0;

        if (_suspended || _idlePaused)
            return 60_000;

        var nowTickMs = Environment.TickCount64;
        if (_display is null && _nextUsbAttemptAtMs > nowTickMs)
            return checked((int)Math.Min(60_000L, _nextUsbAttemptAtMs - nowTickMs));

        var now = Stopwatch.GetTimestamp();
        if (_resetFrameSchedule)
        {
            nextFrameAt = now;
            _resetFrameSchedule = false;
        }
        var intervalTicks = Math.Max(1L, (long)Math.Round(intervalMs * (double)Stopwatch.Frequency / 1000.0));
        nextFrameAt += intervalTicks;
        if (nextFrameAt <= now)
        {
            // An overrun starts the next frame immediately; do not add an artificial 10 ms penalty.
            nextFrameAt = now;
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling((nextFrameAt - now) * 1000.0 / Stopwatch.Frequency));
    }

    private async Task WaitForWakeOrDelayAsync(int delayMs, CancellationToken token)
    {
        await _wakeSignal.WaitAsync(TimeSpan.FromMilliseconds(delayMs), token).ConfigureAwait(false);
    }

    private void SignalWake()
    {
        try
        {
            if (_wakeSignal.CurrentCount == 0)
                _wakeSignal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private void RequestTransportRetirementLocked(string reason, string nextState)
    {
        // Invalidate any connection currently being opened or any completed frame whose
        // send began before this lifecycle transition.
        _connectionGeneration++;
        // The worker observes cancellation outside _sync and drains the native
        // operation before disposing its transport handle.
        _transportIoCancellation.Cancel();
        _transportRetirePending = true;
        _transportRetireReason = reason;
        _transportRetireState = nextState;
        SetUsbStateLocked(nextState);
        Volatile.Write(ref _isConnected, false);
        _lastFrameTimestamp = 0;
        _lastFps = 0;
    }

    private void DrainTransportRetirementWorker()
    {
        IPhysicalFrameTransport? retired;
        CancellationTokenSource retiredCancellation;
        string reason;
        string nextState;
        lock (_sync)
        {
            if (!_transportRetirePending)
                return;

            retiredCancellation = _transportIoCancellation;
            _transportIoCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            retired = _display;
            _display = null;
            reason = _transportRetireReason;
            nextState = _suspended ? "SUSPENDED" : _transportRetireState;
            _transportRetirePending = false;
            SetUsbStateLocked(nextState);
            Volatile.Write(ref _isConnected, false);
            _lastFrameTimestamp = 0;
            _lastFps = 0;
        }

        try
        {
            if (retired is not null)
                CloseTransportWorker(retired, reason, nextState);
        }
        finally
        {
            // No transfer can reference the previous generation after the
            // worker drained and closed the previous transport.
            retiredCancellation.Dispose();
        }
    }

    private void CloseTransportWorker(IPhysicalFrameTransport display, string reason, string nextState)
    {
        _log.Info($"{LogTarget} retiring transport; nextState={nextState}; reason={reason}");
        try
        {
            display.RecordLifecycle(nextState, reason);
        }
        catch (Exception ex)
        {
            RecordTestFailure(ex);
            _log.Warn($"{LogTarget} transport lifecycle diagnostics failed.", ex);
        }

        try
        {
            display.Dispose();
            _log.Info($"{LogTarget} transport retired; nextState={nextState}; framesSent={SentFrames}.");
        }
        catch (Exception ex)
        {
            RecordTestFailure(ex);
            Volatile.Write(ref _nativeReleaseFailed, true);
            if (_stopOnFailure) Volatile.Write(ref _testCleanupFailed, true);
            _log.Warn($"{LogTarget} transport disposal failed during {nextState}.", ex);
            lock (_sync)
            {
                // A failed release is unsafe for either protocol to reacquire.
                _transportCleanupFailed = true;
                _transportFaulted = true;
                _transportAbsenceObserved = false;
                SetUsbStateLocked("ERROR");
            }
        }
    }

    private IPhysicalFrameTransport? EnsureDisplayWorker(CancellationToken token)
    {
        OutputTargetConfig target;
        int timeoutMs;
        int retryMs;
        CancellationToken ioToken;
        long generation;
        bool faulted;

        lock (_sync)
        {
            if (_suspended || _idlePaused || _transportRetirePending || _transportCleanupFailed ||
                _disposeState != 0 || token.IsCancellationRequested)
                return null;

            if (_display is not null)
                return _display;

            if (Environment.TickCount64 < _nextUsbAttemptAtMs)
                return null;

            target = _target;
            var usb = target.Usb ?? _config.Usb;
            timeoutMs = usb.TransferTimeoutMs;
            retryMs = usb.RetryIntervalMs;
            ioToken = _transportIoCancellation.Token;
            generation = _connectionGeneration;
            faulted = _transportFaulted;
        }

        if (faulted && !ObserveTransportRecoveryWorker())
            return null;

        IPhysicalFrameTransport? opened;
        try
        {
            // Discovery, CreateFile, WinUSB handshake and their failure cleanup run
            // OUTSIDE the general session state lock.
            opened = _transportFactory(target, timeoutMs, _log, ioToken);
        }
        catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
        {
            // Lifecycle intent superseded the connection during handshake.
            // The worker will drain retirement on its next iteration.
            return null;
        }
        catch (OutputDeviceResolutionException ex)
        {
            RecordTestFailure(ex);
            _log.ErrorThrottled(
                $"frame.usb-resolution.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"{LogTarget} device resolution failed.", ex);

            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended && !_idlePaused)
                {
                    SetUsbStateLocked(
                        ex.Status == OutputDeviceResolutionStatus.Ambiguous ? "AMBIGUOUS" : "ERROR");
                    Volatile.Write(ref _isConnected, false);
                    _nextUsbAttemptAtMs = Environment.TickCount64 + retryMs;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            RecordTestFailure(ex);
            if (ex is PhysicalTransportCleanupException)
            {
                Volatile.Write(ref _testCleanupFailed, true);
                Volatile.Write(ref _nativeReleaseFailed, true);
                lock (_sync) _transportCleanupFailed = true;
            }
            _log.ErrorThrottled(
                $"frame.usb-connect.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"{LogTarget} connection failed",
                ex);

            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended && !_idlePaused)
                {
                    SetUsbStateLocked("ERROR");
                    _transportFaulted = _transportCleanupFailed ||
                        target.Transport == OutputTargetContract.EncryptedJpegTransport;
                    _transportAbsenceObserved = false;
                    Volatile.Write(ref _isConnected, false);
                    _nextUsbAttemptAtMs = Environment.TickCount64 + retryMs;
                }
            }
            ObserveTransportRecoveryWorker();
            return null;
        }

        if (opened is null)
        {
            RecordTestFailure(new InvalidOperationException("The exact Test interface is no longer available."));
            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended && !_idlePaused)
                {
                    SetUsbStateLocked("WAITING");
                    _nextUsbAttemptAtMs = Environment.TickCount64 + retryMs;
                }
            }
            return null;
        }

        var accepted = false;
        lock (_sync)
        {
            if (generation == _connectionGeneration &&
                !_transportRetirePending && !_suspended && !_idlePaused &&
                _disposeState == 0 && !token.IsCancellationRequested)
            {
                _display = opened;
                _connectionHasFrame = false;
                SetUsbStateLocked("CONNECTED");
                Volatile.Write(ref _isConnected, true);
                _nextUsbAttemptAtMs = 0;
                accepted = true;
            }
        }

        if (!accepted)
        {
            // Suspend/reconfigure/shutdown overtook an in-progress connection.
            CloseTransportWorker(opened, "Connection superseded while opening.", "WAITING");
            return null;
        }

        _log.Info($"{LogTarget} transport connected; timeoutMs={timeoutMs}; retryMs={retryMs}.");
        return opened;
    }

    private bool ObserveTransportRecoveryWorker()
    {
        OutputTargetConfig target;
        long generation;
        lock (_sync)
        {
            if (!_transportFaulted) return true;
            if (_maximumFrames is not null || _transportCleanupFailed || _transportRetirePending || _suspended || _idlePaused || _disposeState != 0)
                return false;
            target = _target;
            generation = _connectionGeneration;
            _nextUsbAttemptAtMs = Environment.TickCount64 + (target.Usb ?? _config.Usb).RetryIntervalMs;
        }

        OutputDeviceResolution resolution;
        try
        {
            // Passive metadata only. A scan failure is never evidence of removal.
            resolution = OutputDeviceSelection.Resolve(target, _turzxRecoveryDiscovery());
        }
        catch (Exception ex)
        {
            _log.WarnThrottled($"frame.usb-recovery.{_targetId}", ConnectionErrorLogThrottleInterval,
                $"{LogTarget} passive recovery discovery failed.", ex);
            lock (_sync)
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended && !_idlePaused && _disposeState == 0)
                    SetUsbStateLocked("ERROR");
            return false;
        }

        lock (_sync)
        {
            if (generation != _connectionGeneration || !_transportFaulted || _transportRetirePending ||
                _suspended || _idlePaused || _disposeState != 0 || _cts.IsCancellationRequested)
                return false;
            if (resolution.Status == OutputDeviceResolutionStatus.Missing)
            {
                if (!_transportAbsenceObserved)
                    _log.Info($"{LogTarget} binding absent after transport failure; awaiting reattachment.");
                _transportAbsenceObserved = true;
                SetUsbStateLocked("WAITING");
                return false;
            }
            if (!_transportAbsenceObserved || resolution.Status != OutputDeviceResolutionStatus.Found)
            {
                SetUsbStateLocked(resolution.Status == OutputDeviceResolutionStatus.Ambiguous ? "AMBIGUOUS" : "ERROR");
                return false;
            }

            // The retired connection is already drained/disposed by this worker.
            // Revalidate the exact binding again inside the normal open factory.
            _transportFaulted = false;
            _transportAbsenceObserved = false;
            SetUsbStateLocked("WAITING");
            _log.Info($"{LogTarget} binding reattached; permitting a fresh connection.");
            return true;
        }
    }

    private bool CanSendLocked(IPhysicalFrameTransport display, long generation, CancellationToken token) =>
        !_suspended && !_idlePaused && !_transportRetirePending && _disposeState == 0 &&
        !token.IsCancellationRequested &&
        _connectionGeneration == generation &&
        ReferenceEquals(_display, display);

    private void RenderAndSendWorker(CancellationToken token)
    {
        DrainTransportRetirementWorker();
        if (_stopOnFailure && TestFailure is not null) return;
        if (_maximumFrames is { } limit && SentFrames >= limit) return;
        var display = EnsureDisplayWorker(token);
        if (display is null)
            return;

        var frameStartedAt = Stopwatch.GetTimestamp();
        DashboardRenderer.RenderedJpeg rendered;
        long generation;

        try
        {
            lock (_sync)
            {
                generation = _connectionGeneration;
                if (!CanSendLocked(display, generation, token))
                    return;

                // Renderer ownership is protected by _sync; CommitReconfiguration can
                // swap/dispose it after rendering, not during rendering.
                rendered = _renderer.RenderJpeg(
                    _metrics.Snapshot(),
                    display.Profile,
                    _target.JpegQuality ?? _config.Renderer.JpegQuality);
            }
        }
        catch (Exception ex)
        {
            RecordTestFailure(ex);
            _log.ErrorThrottled(
                $"frame.render.{_targetId}",
                HotPathErrorLogThrottleInterval,
                $"{LogTarget} frame render/encode failed",
                ex);
            return;
        }

        using (rendered)
        {
            lock (_sync)
            {
                if (!CanSendLocked(display, generation, token))
                    return;
            }

            var usbStartedAt = Stopwatch.GetTimestamp();
            try
            {
                // No other thread can dispose this worker-owned display during SendJpeg.
                display.SendJpegWithDiagnostics(
                    rendered.Bytes,
                    new FrameRenderDiagnostics(rendered.RenderMs, rendered.EncodeMs));
            }
            catch (OperationCanceledException) when (
                token.IsCancellationRequested || _transportIoCancellation.IsCancellationRequested)
            {
                // The owner still holds the handle. Retirement/shutdown will
                // dispose it only AFTER native completion has been drained.
                return;
            }
            catch (Exception ex)
            {
                RecordTestFailure(ex);
                _log.ErrorThrottled(
                    $"frame.usb-transfer.{_targetId}",
                    ConnectionErrorLogThrottleInterval,
                    $"{LogTarget} frame USB transfer failed",
                    ex);

                var detached = false;
                lock (_sync)
                {
                    if (ReferenceEquals(_display, display))
                    {
                        _display = null;
                        detached = true;
                        Volatile.Write(ref _isConnected, false);
                        _lastFrameTimestamp = 0;
                        _lastFps = 0;
                        if (!_transportRetirePending && !_suspended)
                        {
                            _transportFaulted = _target.Transport == OutputTargetContract.EncryptedJpegTransport;
                            _transportAbsenceObserved = false;
                            SetUsbStateLocked(_transportFaulted ? "ERROR" : "WAITING");
                            _nextUsbAttemptAtMs = Environment.TickCount64 + (_target.Usb ?? _config.Usb).RetryIntervalMs;
                        }
                    }
                }

                if (detached)
                    CloseTransportWorker(display, "USB frame transfer failed.", "WAITING");
                ObserveTransportRecoveryWorker();
                return;
            }

            var frameCompletedAt = Stopwatch.GetTimestamp();
            string? firstFrameMessage = null;
            lock (_sync)
            {
                // Do not publish a stale CONNECTED status after Suspend or reconfiguration
                // was requested while synchronous USB I/O was still in flight.
                if (!CanSendLocked(display, generation, token))
                    return;

                var frameNumber = Interlocked.Increment(ref _sentFrames);
                if (!_connectionHasFrame)
                    firstFrameMessage = $"{LogTarget} first frame completed; jpegBytes={rendered.Length}.";
                _connectionHasFrame = true;
                if (_lastFrameTimestamp != 0)
                {
                    var seconds = (frameCompletedAt - _lastFrameTimestamp) / (double)Stopwatch.Frequency;
                    if (seconds > 0)
                        _lastFps = 1.0 / seconds;
                }
                _lastFrameTimestamp = frameCompletedAt;

                _lastRenderMs = rendered.RenderMs;
                _lastEncodeMs = rendered.EncodeMs;
                _lastUsbMs = Stopwatch.GetElapsedTime(usbStartedAt, frameCompletedAt).TotalMilliseconds;
                _lastFrameMs = Stopwatch.GetElapsedTime(frameStartedAt, frameCompletedAt).TotalMilliseconds;
                _lastJpegBytes = rendered.Length;
                SetUsbStateLocked("CONNECTED");
                Volatile.Write(ref _isConnected, true);

                PublishRuntimeMetricsIfDue(frameNumber);
            }
            if (firstFrameMessage is not null)
                _log.Info(firstFrameMessage);
        }
    }

    private void PublishRuntimeMetricsIfDue(long frameNumber)
    {
        if (!RuntimeMetricsEnabled)
            return;

        var now = Environment.TickCount64;
        if (_lastRuntimeMetricsPublishAtMs != 0 && now - _lastRuntimeMetricsPublishAtMs < RuntimeMetricsPublishIntervalMs)
            return;

        _lastRuntimeMetricsPublishAtMs = now;
        _metrics.Publish(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [OutputMetricContract.Id(_targetId, OutputMetricContract.FrameCount)] = frameNumber,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.RenderDuration)] = _lastRenderMs,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.EncodeDuration)] = _lastEncodeMs,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.UsbDuration)] = _lastUsbMs,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.FrameDuration)] = _lastFrameMs,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.Fps)] = _lastFps,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.JpegSize)] = _lastJpegBytes,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.UsbState)] = "CONNECTED"
        });
    }

    private void PublishRuntimeMetricIdentityLocked()
    {
        _metrics.Publish(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [OutputMetricContract.Id(_targetId, OutputMetricContract.UsbState)] = _usbState,
            [OutputMetricContract.Id(_targetId, OutputMetricContract.Status)] = _usbState
        });
    }

    private void SetUsbStateLocked(string state)
    {
        if (_transportFaulted && !_transportAbsenceObserved && state == "WAITING") state = "ERROR";
        if (_usbState.Equals(state, StringComparison.Ordinal))
            return;

        _usbState = state;
        if (RuntimeMetricsEnabled)
        {
            if (!state.Equals("CONNECTED", StringComparison.Ordinal))
            {
                // A disconnected/suspended display must not keep prior FPS,
                // frame timings or JPEG sizes as currently available readings.
                ClearRuntimeMetrics();
            }
            _metrics.Publish(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputMetricContract.Id(_targetId, OutputMetricContract.UsbState)] = state,
                [OutputMetricContract.Id(_targetId, OutputMetricContract.Status)] = state
            });
        }
    }

    private bool RuntimeMetricsEnabled =>
        _publishRuntimeMetrics && _target.ProviderEnabled;

    private void ClearRuntimeMetrics()
    {
        _metrics.Publish(OutputMetricContract.Unavailable(_target));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        SignalWake();
        try
        {
            if (_worker is { } worker)
            {
                int graceMs;
                lock (_sync)
                    graceMs = (_target.Usb ?? _config.Usb).TransferTimeoutMs;

                try
                {
                    // Stop admission first, then let an in-flight handshake or complete
                    // JPEG/ACK transaction finish without cancelling it between writes.
                    worker.WaitAsync(TimeSpan.FromMilliseconds(graceMs)).GetAwaiter().GetResult();
                }
                catch (TimeoutException)
                {
                    _log.Error($"{LogTarget} USB_SHUTDOWN_GRACE_EXPIRED; graceMs={graceMs}; " +
                        "workerCompletion=unconfirmed; requesting cancellation; native resources remain owned until terminal completion.");
                    _cts.Cancel();
                    // Cancellation is only a request. Keep buffers and handles alive
                    // until the worker has drained actual native completion.
                    worker.GetAwaiter().GetResult();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            IPhysicalFrameTransport? display;
            lock (_sync)
            {
                display = _display;
                _display = null;
            }

            try
            {
                if (display is not null)
                    CloseTransportWorker(display, "Output session disposed.", "SHUTDOWN");
            }
            finally
            {
                lock (_sync)
                    _renderer.Dispose();
            }
        }
        finally
        {
            // The worker has fully stopped: no later publication can overwrite
            // this Output's unavailable values with an in-flight frame.
            lock (_sync)
                ClearRuntimeMetrics();
            _transportIoCancellation.Dispose();
            _wakeSignal.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class PreparedReconfiguration : IOutputSessionReconfiguration
    {
        private DashboardRenderer? _renderer;

        public PreparedReconfiguration(
            AppConfig config,
            OutputTargetConfig target,
            int canvasWidth,
            int canvasHeight,
            int orientation,
            DashboardRenderer renderer)
        {
            Config = config;
            Target = target;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            Orientation = orientation;
            _renderer = renderer;
        }

        public AppConfig Config { get; }
        public OutputTargetConfig Target { get; }
        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public int Orientation { get; }
        public DashboardRenderer TakeRenderer()
        {
            var renderer = _renderer ?? throw new InvalidOperationException("Prepared renderer has already been committed or disposed.");
            _renderer = null;
            return renderer;
        }

        public void Dispose()
        {
            _renderer?.Dispose();
            _renderer = null;
        }
    }
}
