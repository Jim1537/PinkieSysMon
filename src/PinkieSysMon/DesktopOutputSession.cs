using System.Diagnostics;
using Microsoft.Win32;
using System.Drawing;
using SkiaSharp;
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
        Func<Action, IDesktopOutputWindow>? windowFactory = null)
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
            window = (windowFactory ?? (render => new DesktopOutputWindow(render,
                point => PersistPosition(dashboard.BaseDirectory, point))))(DrawFrame)
                ?? throw new InvalidOperationException("Desktop window factory returned null.");
            var size = FrameGeometry.GetLogicalSize(
                dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
            window.Configure(target.Name, size, LocationFor(target), IntervalFor(config, target),
                target.Desktop?.Lock ?? true);
            _window = window;
        }
        catch
        {
            window?.Dispose();
            _renderer.Dispose();
            throw;
        }
    }

    private void PersistPosition(string dashboardDirectory, Point point)
    {
        try
        {
            var path = Path.Combine(ApplicationRootLocator.Find(dashboardDirectory),
                "config", "app.json");
            DesktopPositionStore.Save(path, _id, point);
            if (_target.Desktop is { } desktop)
            {
                desktop.X = point.X;
                desktop.Y = point.Y;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Desktop output '{_id}' failed to persist placement.", ex);
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

        // Apply fallible native window configuration before transferring the prepared renderer.
        // On a window error the prepared plan still owns its raster resources for rollback.
        _window.Configure(prepared.Target.Name, prepared.Size,
            LocationFor(prepared.Target), IntervalFor(prepared.Config, prepared.Target),
            prepared.Target.Desktop?.Lock ?? true);
        var previous = _renderer;
        var wasPublishing = Publishing;
        _renderer = prepared.TakeRenderer();
        _config = prepared.Config;
        _target = prepared.Target;
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

    private void DrawFrame()
    {
        if (!_started || _suspended || !_window.IsVisible)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            // Borrow the exact premultiplied BGRA pixels only until native presentation ends.
            // The window does not retain the frame or fall back to opaque GDI+ painting.
            using var frame = _renderer.RenderBgra(_metrics.Snapshot());
            _window.Present(frame.Bitmap);

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

// Native Windows Desktop presentation and persistence are UI-thread owned.
internal interface IDesktopOutputWindow : IDisposable
{
    bool IsVisible { get; }
    void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked);
    void ShowWindow();
    void HideWindow();
    void RequestFrame();
    void Present(SKBitmap frame);
}

internal sealed class DesktopOutputWindow : Form, IDesktopOutputWindow
{
    private const int WsExLayered = 0x00080000;
    private const int WmNcHitTest = 0x0084;
    private const int WmDpiChanged = 0x02E0;
    private readonly Action _render;
    private readonly Action<Point> _savePosition;
    private readonly System.Windows.Forms.Timer _refresh = new();
    private readonly System.Windows.Forms.Timer _moveDebounce = new() { Interval = 500 };
    private DesktopLayeredSurface? _surface;
    private Size _canvas;
    private Point _lastPosition;
    private bool _locked = true;
    private bool _configuring;
    private bool _disposed;

    public bool IsVisible => Visible;
    protected override CreateParams CreateParams
    {
        get
        {
            var result = base.CreateParams;
            result.ExStyle |= WsExLayered;
            return result;
        }
    }

    public DesktopOutputWindow(Action render, Action<Point> savePosition)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _savePosition = savePosition ?? throw new ArgumentNullException(nameof(savePosition));
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        _refresh.Tick += (_, _) => _render();
        _moveDebounce.Tick += (_, _) =>
        {
            _moveDebounce.Stop();
            if (_locked || !Visible || Location == _lastPosition)
                return;
            _savePosition(Location);
            _lastPosition = Location;
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }

    private Point Resolve(Point requested) =>
        DesktopPlacement.Recover(requested, _canvas,
            Screen.AllScreens.Select(s => s.WorkingArea).ToArray(),
            Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea);

    public void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked)
    {
        _configuring = true;
        try
        {
            _moveDebounce.Stop();
            Text = title;
            _canvas = clientPixels;
            _locked = locked;
            ClientSize = clientPixels;
            Location = Resolve(location);
            _lastPosition = Location;
            _refresh.Interval = refreshMs;
        }
        finally { _configuring = false; }
    }

    public void ShowWindow()
    {
        if (!Visible)
            Show();
        RecoverVisible();
        _refresh.Start();
        _render();
    }

    public void HideWindow()
    {
        _moveDebounce.Stop();
        _refresh.Stop();
        Hide();
    }

    public void RequestFrame()
    {
        if (Visible)
            _render();
    }

    public void Present(SKBitmap frame)
    {
        if (!Visible || !IsHandleCreated)
            return;
        if (_surface is null || _surface.Width != frame.Width || _surface.Height != frame.Height)
        {
            var newSurface = new DesktopLayeredSurface(frame.Width, frame.Height);
            var old = _surface;
            _surface = newSurface;
            old?.Dispose();
        }
        _surface.Present(Handle, Location, frame);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_configuring || _locked || _disposed || !Visible)
            return;
        _moveDebounce.Stop();
        _moveDebounce.Start();
    }

    protected override void WndProc(ref Message m)
    {
        // Minimal native dragging while unlocked; full Lock/click-through is Stage12.
        if (m.Msg == WmNcHitTest && !_locked)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1) // HTCLIENT
                m.Result = (IntPtr)2; // HTCAPTION
            return;
        }
        if (m.Msg == WmDpiChanged)
        {
            _configuring = true;
            try
            {
                base.WndProc(ref m);
                if (_canvas.Width > 0 && _canvas.Height > 0 && ClientSize != _canvas)
                    ClientSize = _canvas; // preserve canonical physical pixel size
                RecoverVisible();
            }
            finally { _configuring = false; }
            if (Visible)
                _render();
            return;
        }
        base.WndProc(ref m);
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsHandleCreated)
            return;
        try { BeginInvoke((Action)RecoverVisible); }
        catch (InvalidOperationException) when (_disposed || !IsHandleCreated) { }
    }

    private void RecoverVisible()
    {
        if (_disposed || _canvas.Width <= 0 || _canvas.Height <= 0)
            return;
        var recovered = Resolve(Location);
        if (recovered == Location)
            return;
        _configuring = true;
        try
        {
            _moveDebounce.Stop();
            Location = recovered;
            _lastPosition = Location; // do not overwrite location of detached monitor
        }
        finally { _configuring = false; }
        if (Visible)
            _render();
    }

    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            _refresh.Dispose();
            _moveDebounce.Dispose();
            _surface?.Dispose();
            _surface = null;
        }
        base.Dispose(disposing);
    }
}
