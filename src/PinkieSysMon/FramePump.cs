using System.Diagnostics;

namespace PinkieSysMon;

internal sealed class FramePump : IOutputSession
{
    private const int RuntimeMetricsPublishIntervalMs = 1000;
    private static readonly TimeSpan HotPathErrorLogThrottleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly MetricStore _metrics;
    private readonly FileLogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private readonly string _targetId;
    private readonly Func<OutputTargetConfig, int, bool, FileLogger, ITrofeoFrameTransport?> _transportFactory;

    private AppConfig _config;
    private OutputTargetConfig _target;
    private string _targetName;
    private DashboardRenderer _renderer;
    private ITrofeoFrameTransport? _display;
    private long _nextUsbAttemptAtMs;
    private long _connectionGeneration;
    private bool _transportRetirePending;
    private string _transportRetireReason = "Output lifecycle changed.";
    private string _transportRetireState = "WAITING";
    private Task? _worker;
    private bool _suspended;
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
    private int _disposeState;

    public FramePump(
        AppConfig config,
        OutputTargetConfig target,
        global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard,
        MetricStore metrics,
        FileLogger log,
        Func<OutputTargetConfig, int, bool, FileLogger, ITrofeoFrameTransport?>? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);

        _config = config;
        _target = target;
        _targetId = target.Id;
        _targetName = target.Name;
        _metrics = metrics;
        _log = log;
        _transportFactory = transportFactory ??
            (static (selectedTarget, timeoutMs, journalEnabled, logger) =>
                TrofeoTransport.TryOpen(selectedTarget, timeoutMs, journalEnabled, logger));
        _renderer = new DashboardRenderer(dashboard);
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

        _log.Info(
            $"Output session '{TargetName}' started. USB diagnostic journal=" +
            (_config.Usb.DiagnosticJournalEnabled ? "enabled." : "disabled."));
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
            new DashboardRenderer(dashboard));
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

            var outputConnectionChanged =
                _config.Usb.TransferTimeoutMs != prepared.Config.Usb.TransferTimeoutMs ||
                _config.Usb.DiagnosticJournalEnabled != prepared.Config.Usb.DiagnosticJournalEnabled ||
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
                RequestTransportRetirementLocked(
                    "Output target, USB timeout, or USB diagnostics changed; reconnecting.",
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
            _log.Warn($"Output target '{TargetName}' previous renderer disposal failed after reconfiguration.", ex);
        }

        SignalWake();
        _log.Info(
            $"Output session '{TargetName}' reconfigured. refresh={prepared.Config.Display.RefreshIntervalMs}ms; " +
            $"canvas={prepared.CanvasWidth}x{prepared.CanvasHeight}; orientation={prepared.Orientation}; " +
            $"targetId={_targetId}; jpegQuality={prepared.Config.Renderer.JpegQuality}.");
    }

    public void SetRuntimeMetricsPublisher(bool enabled)
    {
        lock (_sync)
        {
            if (_publishRuntimeMetrics == enabled)
                return;

            _publishRuntimeMetrics = enabled;
            if (RuntimeMetricsEnabled)
            {
                _lastRuntimeMetricsPublishAtMs = 0;
                PublishRuntimeMetricIdentityLocked();
            }
        }
    }

    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
            RequestTransportRetirementLocked("System suspend.", "SUSPENDED");
        }

        _log.Info($"Output session '{TargetName}' suspended.");
        SignalWake();
    }

    public void Resume()
    {
        lock (_sync)
        {
            _suspended = false;
            _nextUsbAttemptAtMs = 0;
            // The old connection is invalid even if suspend and resume race with a USB call.
            RequestTransportRetirementLocked("System resume; discard prior USB handle.", "WAITING");
        }

        _log.Info($"Output session '{TargetName}' resumed.");
        SignalWake();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var nextFrameAt = Stopwatch.GetTimestamp();

        while (!token.IsCancellationRequested)
        {
            int delayMs;
            var intervalMs = 1000;

            try
            {
                lock (_sync)
                    intervalMs = _config.Display.RefreshIntervalMs;

                // The output worker is the sole owner of all WinUSB operations and disposal.
                // No potentially blocking device operation may execute under _sync.
                RenderAndSendWorker(token);

                lock (_sync)
                    delayMs = GetNextDelayMsLocked(intervalMs, ref nextFrameAt);
            }
            catch (Exception ex)
            {
                _log.ErrorThrottled(
                    $"frame.loop.{_targetId}",
                    HotPathErrorLogThrottleInterval,
                    $"Output target '{TargetName}' frame loop error",
                    ex);
                delayMs = Math.Max(1, intervalMs);
                nextFrameAt = Stopwatch.GetTimestamp();
            }

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

        if (_suspended)
            return 60_000;

        var nowTickMs = Environment.TickCount64;
        if (_display is null && _nextUsbAttemptAtMs > nowTickMs)
            return checked((int)Math.Min(60_000L, _nextUsbAttemptAtMs - nowTickMs));

        var now = Stopwatch.GetTimestamp();
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
        ITrofeoFrameTransport? retired;
        string reason;
        string nextState;
        lock (_sync)
        {
            if (!_transportRetirePending)
                return;

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

        if (retired is not null)
            CloseTransportWorker(retired, reason, nextState);
    }

    private void CloseTransportWorker(ITrofeoFrameTransport display, string reason, string nextState)
    {
        _log.Info($"Output target '{TargetName}' disconnected. {reason}");
        try
        {
            display.RecordLifecycle(nextState, reason);
        }
        catch (Exception ex)
        {
            _log.Warn($"Output target '{TargetName}' transport lifecycle diagnostics failed.", ex);
        }

        try
        {
            display.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warn($"Output target '{TargetName}' transport disposal failed during {nextState}.", ex);
        }
    }

    private ITrofeoFrameTransport? EnsureDisplayWorker(CancellationToken token)
    {
        OutputTargetConfig target;
        int timeoutMs;
        int retryMs;
        bool journalEnabled;
        long generation;

        lock (_sync)
        {
            if (_suspended || _transportRetirePending || _disposeState != 0 || token.IsCancellationRequested)
                return null;

            if (_display is not null)
                return _display;

            if (Environment.TickCount64 < _nextUsbAttemptAtMs)
                return null;

            target = _target;
            timeoutMs = _config.Usb.TransferTimeoutMs;
            retryMs = _config.Usb.RetryIntervalMs;
            journalEnabled = _config.Usb.DiagnosticJournalEnabled;
            generation = _connectionGeneration;
        }

        ITrofeoFrameTransport? opened;
        try
        {
            // Discovery, CreateFile, WinUSB handshake and their failure cleanup run
            // OUTSIDE the general session state lock.
            opened = _transportFactory(target, timeoutMs, journalEnabled, _log);
        }
        catch (OutputDeviceResolutionException ex)
        {
            _log.WarnThrottled(
                $"frame.usb-resolution.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"Output target '{TargetName}' device resolution failed: {ex.Message}");

            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended)
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
            _log.ErrorThrottled(
                $"frame.usb-connect.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"Output target '{TargetName}' connection failed",
                ex);

            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended)
                {
                    SetUsbStateLocked("ERROR");
                    Volatile.Write(ref _isConnected, false);
                    _nextUsbAttemptAtMs = Environment.TickCount64 + retryMs;
                }
            }
            return null;
        }

        if (opened is null)
        {
            lock (_sync)
            {
                if (generation == _connectionGeneration && !_transportRetirePending && !_suspended)
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
                !_transportRetirePending && !_suspended &&
                _disposeState == 0 && !token.IsCancellationRequested)
            {
                _display = opened;
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

        return opened;
    }

    private bool CanSendLocked(ITrofeoFrameTransport display, long generation, CancellationToken token) =>
        !_suspended && !_transportRetirePending && _disposeState == 0 &&
        !token.IsCancellationRequested &&
        _connectionGeneration == generation &&
        ReferenceEquals(_display, display);

    private void RenderAndSendWorker(CancellationToken token)
    {
        DrainTransportRetirementWorker();
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
                    display.WireRotationDegrees,
                    _config.Renderer.JpegQuality);
            }
        }
        catch (Exception ex)
        {
            _log.ErrorThrottled(
                $"frame.render.{_targetId}",
                HotPathErrorLogThrottleInterval,
                $"Output target '{TargetName}' frame render/encode failed",
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
                    new TrofeoFrameRenderDiagnostics(rendered.RenderMs, rendered.EncodeMs));
            }
            catch (Exception ex)
            {
                _log.ErrorThrottled(
                    $"frame.usb-transfer.{_targetId}",
                    ConnectionErrorLogThrottleInterval,
                    $"Output target '{TargetName}' frame USB transfer failed",
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
                            SetUsbStateLocked("WAITING");
                            _nextUsbAttemptAtMs = Environment.TickCount64 + _config.Usb.RetryIntervalMs;
                        }
                    }
                }

                if (detached)
                    CloseTransportWorker(display, "USB frame transfer failed.", "WAITING");
                return;
            }

            var frameCompletedAt = Stopwatch.GetTimestamp();
            lock (_sync)
            {
                // Do not publish a stale CONNECTED status after Suspend or reconfiguration
                // was requested while synchronous USB I/O was still in flight.
                if (!CanSendLocked(display, generation, token))
                    return;

                var frameNumber = Interlocked.Increment(ref _sentFrames);
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
            [RuntimeMetricContract.FrameCount] = frameNumber,
            [RuntimeMetricContract.RenderDuration] = _lastRenderMs,
            [RuntimeMetricContract.EncodeDuration] = _lastEncodeMs,
            [RuntimeMetricContract.UsbDuration] = _lastUsbMs,
            [RuntimeMetricContract.FrameDuration] = _lastFrameMs,
            [RuntimeMetricContract.Fps] = _lastFps,
            [RuntimeMetricContract.JpegSize] = _lastJpegBytes,
            [RuntimeMetricContract.UsbState] = "CONNECTED"
        });
    }

    private void PublishRuntimeMetricIdentityLocked()
    {
        _metrics.Publish(RuntimeMetricContract.Version, RuntimeVersion.Current);
        _metrics.Publish(RuntimeMetricContract.UsbState, _usbState);
    }

    private void SetUsbStateLocked(string state)
    {
        if (_usbState.Equals(state, StringComparison.Ordinal))
            return;

        _usbState = state;
        if (RuntimeMetricsEnabled)
            _metrics.Publish(RuntimeMetricContract.UsbState, state);
    }

    private bool RuntimeMetricsEnabled =>
        _publishRuntimeMetrics &&
        MetricProviderContract.IsEnabled(_config.MetricProviders, MetricProviderContract.System);

    private void ClearRuntimeMetrics()
    {
        _metrics.Publish(RuntimeMetricContract.Descriptors.ToDictionary(
            descriptor => descriptor.Id,
            _ => (object?)null,
            StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _cts.Cancel();
        SignalWake();
        try
        {
            // Until true cancellable WinUSB is introduced, this can still wait for an
            // in-flight synchronous operation. It never closes that operation's handle.
            _worker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            ITrofeoFrameTransport? display;
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
