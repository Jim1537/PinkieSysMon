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

    private AppConfig _config;
    private OutputTargetConfig _target;
    private string _targetName;
    private DashboardRenderer _renderer;
    private TrofeoTransport? _display;
    private long _nextUsbAttemptAtMs;
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
        FileLogger log)
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
                DropDisplayLocked("Output target, USB timeout, or USB diagnostics changed; reconnecting.");

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
            DropDisplayLocked("System suspend.");
            SetUsbStateLocked("SUSPENDED");
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
            SetUsbStateLocked("WAITING");
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
                {
                    intervalMs = _config.Display.RefreshIntervalMs;
                    if (!_suspended)
                        RenderAndSendLocked();

                    delayMs = GetNextDelayMsLocked(intervalMs, ref nextFrameAt);
                }
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

    private void RenderAndSendLocked()
    {
        if (!EnsureDisplayLocked())
            return;

        var frameStartedAt = Stopwatch.GetTimestamp();
        DashboardRenderer.RenderedJpeg rendered;
        try
        {
            rendered = _renderer.RenderJpeg(
                _metrics.Snapshot(),
                _display!.WireRotationDegrees,
                _config.Renderer.JpegQuality);
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
            var usbStartedAt = Stopwatch.GetTimestamp();
            try
            {
                _display!.SendJpeg(
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
                DropDisplayLocked("USB frame transfer failed.");
                _nextUsbAttemptAtMs = Environment.TickCount64 + _config.Usb.RetryIntervalMs;
                return;
            }

            var frameCompletedAt = Stopwatch.GetTimestamp();
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

    private bool EnsureDisplayLocked()
    {
        if (_display is not null)
            return true;

        var now = Environment.TickCount64;
        if (now < _nextUsbAttemptAtMs)
            return false;

        try
        {
            _display = TrofeoTransport.TryOpen(
                _target,
                _config.Usb.TransferTimeoutMs,
                _config.Usb.DiagnosticJournalEnabled,
                _log);
            if (_display is null)
            {
                SetUsbStateLocked("WAITING");
                _nextUsbAttemptAtMs = now + _config.Usb.RetryIntervalMs;
                return false;
            }

            SetUsbStateLocked("CONNECTED");
            Volatile.Write(ref _isConnected, true);
            _nextUsbAttemptAtMs = 0;
            return true;
        }
        catch (OutputDeviceResolutionException ex)
        {
            _log.WarnThrottled(
                $"frame.usb-resolution.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"Output target '{TargetName}' device resolution failed: {ex.Message}");
            DropDisplayLocked(
                "Output device resolution failed.",
                ex.Status == OutputDeviceResolutionStatus.Ambiguous ? "AMBIGUOUS" : "ERROR");
            _nextUsbAttemptAtMs = now + _config.Usb.RetryIntervalMs;
            return false;
        }
        catch (Exception ex)
        {
            _log.ErrorThrottled(
                $"frame.usb-connect.{_targetId}",
                ConnectionErrorLogThrottleInterval,
                $"Output target '{TargetName}' connection failed",
                ex);
            DropDisplayLocked("Output connection failed.", "ERROR");
            _nextUsbAttemptAtMs = now + _config.Usb.RetryIntervalMs;
            return false;
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

    private void DropDisplayLocked(string reason, string nextState = "WAITING")
    {
        var display = _display;
        _display = null;

        if (display is not null)
        {
            _log.Info($"Output target '{TargetName}' disconnected. {reason}");
            try
            {
                display.RecordLifecycle(nextState, reason);
                display.Dispose();
            }
            catch (Exception ex)
            {
                // Disconnect/dispose is cleanup, not a reason to fail a prepared configuration
                // commit or an output-loop recovery path.
                _log.Warn($"Output target '{TargetName}' transport disposal failed during disconnect.", ex);
            }
        }

        SetUsbStateLocked(nextState);
        Volatile.Write(ref _isConnected, false);
        _lastFrameTimestamp = 0;
        _lastFps = 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _cts.Cancel();
        SignalWake();
        try
        {
            _worker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            lock (_sync)
            {
                var display = _display;
                _display = null;
                if (display is not null)
                {
                    try
                    {
                        display.RecordLifecycle("SHUTDOWN", "Output session disposed.");
                        display.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"Output target '{TargetName}' transport disposal failed during shutdown.", ex);
                    }
                }

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
