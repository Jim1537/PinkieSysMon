using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

// A Desktop output owns its own canonical renderer and a native Windows window, not a USB transport.
internal sealed class DesktopOutputSession : IOutputSession
{
    private const int MetricsPublishIntervalMs = 1000;
    private readonly string _id;
    private readonly MetricStore _metrics;
    private readonly FileLogger _log;
    private readonly IDesktopOutputWindow _window;
    private DashboardRenderer _renderer;
    private AppConfig _config;
    private OutputTargetConfig _target;
    private bool _started;
    private bool _suspended;
    private bool _publisherEnabled;
    private bool _disposed;
    private string _state = "STOPPED";
    private long _frameCount;
    private double _lastFrameMs;
    private long _lastFrameTimestamp;
    private long _lastPublishAt;

    public DesktopOutputSession(
        AppConfig config,
        OutputTargetConfig target,
        CanonicalDashboardDefinition dashboard,
        MetricStore metrics,
        FileLogger log,
        Func<Action<Graphics>, IDesktopOutputWindow>? windowFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);
        if (target.Kind != OutputTargetContract.DesktopKind || target.Desktop?.BoardEnabled != true)
            throw new ArgumentException("Desktop session requires an enabled Desktop output.", nameof(target));

        _id = target.Id;
        _config = config;
        _target = target;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _renderer = new DashboardRenderer(dashboard);
        IDesktopOutputWindow? window = null;
        try
        {
            window = (windowFactory ?? (draw => new DesktopOutputWindow(draw)))(DrawFrame)
                ?? throw new InvalidOperationException("Desktop window factory returned null.");
            var size = FrameGeometry.GetLogicalSize(
                dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
            window.Configure(target.Name, size, LocationFor(target), IntervalFor(config, target));
            _window = window;
        }
        catch
        {
            window?.Dispose();
            _renderer.Dispose();
            throw;
        }
    }

    public string TargetId => _id;
    public string TargetName => _target.Name;
    public bool IsConnected => _started && !_suspended && _window.IsVisible;
    public long SentFrames => Interlocked.Read(ref _frameCount);
    public double LastFrameMs => Volatile.Read(ref _lastFrameMs);
    public int LastJpegBytes => 0;
    // The interface's legacy name remains until target-aware IPC in Stage 24.
    public string UsbState => _state;

    private bool Publishing => _publisherEnabled && _target.ProviderEnabled;
    private static int IntervalFor(AppConfig config, OutputTargetConfig target) =>
        Math.Max(10, target.RefreshIntervalMs ?? config.Display.RefreshIntervalMs);
    private static Point LocationFor(OutputTargetConfig target) =>
        new(target.Desktop?.X ?? 0, target.Desktop?.Y ?? 0);

    public void Start()
    {
        ThrowIfDisposed();
        if (_started)
            return;
        _started = true;
        try
        {
            if (!_suspended)
                _window.ShowWindow();
            ChangeState(_suspended ? "SUSPENDED" : "DISPLAYING");
            _log.Info($"Desktop output '{TargetName}' started; targetId={_id}; no USB transport.");
        }
        catch (Exception ex)
        {
            _started = false;
            ChangeState("ERROR");
            _log.Error($"Desktop output '{TargetName}' failed to open its window.", ex);
            throw;
        }
    }

    public IOutputSessionReconfiguration PrepareReconfiguration(
        AppConfig config, OutputTargetConfig target, CanonicalDashboardDefinition dashboard)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);
        if (!target.Id.Equals(_id, StringComparison.OrdinalIgnoreCase) ||
            target.Kind != OutputTargetContract.DesktopKind ||
            target.Desktop?.BoardEnabled != true)
            throw new InvalidOperationException($"Desktop session '{_id}' cannot be rebound to '{target.Id}'.");

        var size = FrameGeometry.GetLogicalSize(
            dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
        return new PreparedReconfiguration(config, target, size, new DashboardRenderer(dashboard));
    }

    public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration)
    {
        ThrowIfDisposed();
        if (reconfiguration is not PreparedReconfiguration prepared ||
            !prepared.Target.Id.Equals(_id, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid desktop output reconfiguration.", nameof(reconfiguration));

        var previous = _renderer;
        var wasPublishing = Publishing;
        _renderer = prepared.TakeRenderer();
        _config = prepared.Config;
        _target = prepared.Target;
        _window.Configure(_target.Name, prepared.Size, LocationFor(_target), IntervalFor(_config, _target));
        if (wasPublishing && !Publishing)
            _metrics.Publish(OutputMetricContract.Unavailable(_target));
        _lastPublishAt = 0;
        if (Publishing)
            PublishState();
        _window.RequestFrame();
        try { previous.Dispose(); }
        catch (Exception ex) { _log.Warn($"Desktop output '{_id}' former renderer cleanup failed.", ex); }
        _log.Info($"Desktop output '{_id}' reconfigured: canvas={prepared.Size.Width}x{prepared.Size.Height} physical pixels.");
    }

    public void SetRuntimeMetricsPublisher(bool enabled)
    {
        ThrowIfDisposed();
        if (_publisherEnabled == enabled)
            return;
        var wasPublishing = Publishing;
        _publisherEnabled = enabled;
        if (wasPublishing && !Publishing)
            _metrics.Publish(OutputMetricContract.Unavailable(_target));
        else if (!wasPublishing && Publishing)
        {
            _lastPublishAt = 0;
            PublishState();
        }
    }

    public void Suspend()
    {
        ThrowIfDisposed();
        _suspended = true;
        if (_started)
            _window.HideWindow();
        ChangeState("SUSPENDED");
    }

    public void Resume()
    {
        ThrowIfDisposed();
        _suspended = false;
        if (_started)
            _window.ShowWindow();
        ChangeState(_started ? "DISPLAYING" : "STOPPED");
    }

    private void DrawFrame(Graphics graphics)
    {
        if (!_started || _suspended || !_window.IsVisible)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            // GDI+ borrows Skia's premultiplied BGRA pixels only for this synchronous paint.
            using var frame = _renderer.RenderBgra(_metrics.Snapshot());
            using var pixels = new Bitmap(
                frame.Bitmap.Width, frame.Bitmap.Height, frame.Bitmap.RowBytes,
                PixelFormat.Format32bppPArgb, frame.Bitmap.GetPixels());
            graphics.DrawImageUnscaled(pixels, 0, 0);

            var now = Stopwatch.GetTimestamp();
            Volatile.Write(ref _lastFrameMs, Stopwatch.GetElapsedTime(startedAt, now).TotalMilliseconds);
            Interlocked.Increment(ref _frameCount);
            if (_state == "ERROR")
                ChangeState("DISPLAYING");

            var nowMs = Environment.TickCount64;
            if (Publishing && (nowMs - _lastPublishAt >= MetricsPublishIntervalMs || _lastPublishAt == 0))
            {
                var fps = _lastFrameTimestamp == 0 ? 0d :
                    1d / Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalSeconds;
                _lastFrameTimestamp = now;
                _lastPublishAt = nowMs;
                _metrics.Publish(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputMetricContract.Id(_id, OutputMetricContract.FrameCount)] = SentFrames,
                    [OutputMetricContract.Id(_id, OutputMetricContract.RenderDuration)] = frame.RenderMs,
                    [OutputMetricContract.Id(_id, OutputMetricContract.FrameDuration)] = LastFrameMs,
                    [OutputMetricContract.Id(_id, OutputMetricContract.Fps)] = fps,
                    [OutputMetricContract.Id(_id, OutputMetricContract.Status)] = _state
                });
            }
        }
        catch (Exception ex)
        {
            ChangeState("ERROR");
            _log.ErrorThrottled("desktop-frame-" + _id, TimeSpan.FromSeconds(5),
                $"Desktop output '{_id}' render/present failed.", ex);
        }
    }

    private void ChangeState(string state)
    {
        _state = state;
        if (Publishing)
        {
            if (state != "DISPLAYING")
                _metrics.Publish(OutputMetricContract.Unavailable(_target));
            PublishState();
        }
    }

    private void PublishState()
    {
        if (Publishing)
            _metrics.Publish(OutputMetricContract.Id(_id, OutputMetricContract.Status), _state);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DesktopOutputSession));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _window.Dispose(); }
        finally
        {
            _renderer.Dispose();
            _metrics.Publish(OutputMetricContract.Unavailable(_target));
            _log.Info($"Desktop output '{_id}' stopped and disposed.");
        }
    }

    private sealed class PreparedReconfiguration(
        AppConfig config, OutputTargetConfig target, Size size, DashboardRenderer renderer)
        : IOutputSessionReconfiguration
    {
        private DashboardRenderer? _renderer = renderer;
        public AppConfig Config { get; } = config;
        public OutputTargetConfig Target { get; } = target;
        public Size Size { get; } = size;
        public DashboardRenderer TakeRenderer()
        {
            var owned = _renderer ?? throw new ObjectDisposedException(nameof(PreparedReconfiguration));
            _renderer = null;
            return owned;
        }
        public void Dispose()
        {
            _renderer?.Dispose();
            _renderer = null;
        }
    }
}

// Session/window operations run on Runtime's WinForms UI thread.
// Stage 09 presents an opaque GDI+ surface; per-pixel window alpha belongs to Stage 10.
internal interface IDesktopOutputWindow : IDisposable
{
    bool IsVisible { get; }
    void Configure(string title, Size clientPixels, Point location, int refreshMs);
    void ShowWindow();
    void HideWindow();
    void RequestFrame();
}

internal sealed class DesktopOutputWindow : Form, IDesktopOutputWindow
{
    private readonly Action<Graphics> _paint;
    private readonly System.Windows.Forms.Timer _timer = new();
    public bool IsVisible => Visible;

    public DesktopOutputWindow(Action<Graphics> paint)
    {
        _paint = paint ?? throw new ArgumentNullException(nameof(paint));
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        _timer.Tick += (_, _) => Invalidate();
    }

    public void Configure(string title, Size clientPixels, Point location, int refreshMs)
    {
        Text = title;
        ClientSize = clientPixels;
        Location = location;
        _timer.Interval = refreshMs;
    }

    public void ShowWindow()
    {
        if (!Visible)
            Show();
        _timer.Start();
        Invalidate();
    }

    public void HideWindow()
    {
        _timer.Stop();
        Hide();
    }

    public void RequestFrame() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _paint(e.Graphics);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
