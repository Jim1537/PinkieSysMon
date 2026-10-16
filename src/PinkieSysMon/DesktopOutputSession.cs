using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Drawing;
using SkiaSharp;
using System.Windows.Forms;
using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

// A Desktop output owns its own canonical renderer and a native Windows window, not a USB transport.
internal sealed class DesktopOutputSession : IOutputSession, IDesktopStackSession
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
    private bool _idlePaused;
    private bool _publisherEnabled;
    private bool _disposed;
    private string _state = "STOPPED";
    private long _frameCount;
    private double _lastFrameMs;
    private long _lastFrameTimestamp;
    private long _lastPublishAt;
    private Action? _restoreStack;

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
                point => PersistPosition(dashboard.BaseDirectory, point),
                () => _restoreStack?.Invoke(),
                error =>
                {
                    if (!_started || _suspended || _idlePaused)
                        return;
                    if (error is null)
                        ChangeState("DISPLAYING");
                    else
                    {
                        ChangeState("HOST_UNAVAILABLE");
                        _log.Warn($"Desktop output '{_id}' cannot use the Explorer wallpaper host: {error}");
                    }
                })))(DrawFrame)
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
    bool IDesktopStackSession.IsStackVisible => _window.IsVisible;
    void IDesktopStackSession.SetStackRestorer(Action restorer) =>
        _restoreStack = restorer ?? throw new ArgumentNullException(nameof(restorer));

    void IDesktopStackSession.PlaceBehind(IDesktopStackSession higher)
    {
        // Fake windows have no native handle. Production windows are positioned only
        // relative to another owned Desktop window, never a foreign HWND.
        if (_window is DesktopOutputWindow lowerWindow &&
            higher is DesktopOutputSession higherSession &&
            higherSession._window is DesktopOutputWindow higherWindow)
            lowerWindow.PlaceBehind(higherWindow);
    }
    public string TargetName => _target.Name;
    public bool IsConnected => _started && !_suspended && !_idlePaused && _window.IsVisible && _state == "DISPLAYING";
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
            if (!_suspended && !_idlePaused)
                _window.ShowWindow();
            ChangeState(_suspended ? "SUSPENDED" : _idlePaused ? "IDLE_PAUSED" :
                _window.IsVisible ? "DISPLAYING" : "HOST_UNAVAILABLE");
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

    public IOutputSessionReconfiguration PrepareRollbackReconfiguration(
        AppConfig config, OutputTargetConfig target, CanonicalDashboardDefinition dashboard)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);
        if (!target.Id.Equals(_id, StringComparison.OrdinalIgnoreCase) ||
            target.Kind != OutputTargetContract.DesktopKind ||
            target.Desktop?.BoardEnabled != true)
            throw new InvalidOperationException("Rollback target does not match the active Desktop Output.");

        var size = FrameGeometry.GetLogicalSize(
            dashboard.Canvas.Width, dashboard.Canvas.Height, dashboard.Canvas.Orientation);
        return new PreparedReconfiguration(config, target, size, _renderer.Retain());
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
        if (_started && !_suspended && !_idlePaused)
            _window.ShowWindow(); // restore Explorer hosting after native geometry/style changes
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

    public void SetIdlePaused(bool paused)
    {
        ThrowIfDisposed();
        if (_idlePaused == paused)
            return;
        _idlePaused = paused;
        _lastFrameTimestamp = 0;
        _lastPublishAt = 0;
        if (_started)
        {
            if (paused)
                _window.HideWindow();
            else if (!_suspended)
                _window.ShowWindow();
        }
        ChangeState(_suspended ? "SUSPENDED" :
            paused ? "IDLE_PAUSED" : _started ? (_window.IsVisible ? "DISPLAYING" : "HOST_UNAVAILABLE") : "STOPPED");
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
        if (_started && !_idlePaused)
            _window.ShowWindow();
        ChangeState(_idlePaused ? "IDLE_PAUSED" : _started ? (_window.IsVisible ? "DISPLAYING" : "HOST_UNAVAILABLE") : "STOPPED");
    }

    public void RequestFrame()
    {
        ThrowIfDisposed();
        if (_started && !_suspended && !_idlePaused)
            _window.RequestFrame();
    }

    private void DrawFrame()
    {
        if (!_started || _suspended || _idlePaused || !_window.IsVisible)
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
        _restoreStack = null;
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

// Native per-pixel layered-window hit-testing is independent of the pixels:
// locked panels ignore *all* mouse input, including over opaque widgets.
// Preserve unrelated extended styles and WS_EX_LAYERED through each toggle.
internal static class DesktopPointerStyle
{
    internal const int WsExLayered = 0x00080000;
    internal const int WsExTransparent = 0x00000020;

    internal static int ForLock(int existingStyles, bool locked)
    {
        var layered = existingStyles | WsExLayered;
        return locked ? layered | WsExTransparent : layered & ~WsExTransparent;
    }
}

// Only an active Desktop session participates in native sibling stacking.
// Separate from IOutputSession: physical transports never receive window operations.
internal interface IDesktopStackSession
{
    bool IsStackVisible { get; }
    void PlaceBehind(IDesktopStackSession higher);
    void SetStackRestorer(Action restorer);
}

internal static class DesktopZOrderPolicy
{
    // Smaller ZOrder values are farther back; ties preserve persisted Targets order.
    internal static IReadOnlyList<string> BackToFront(
        IEnumerable<OutputTargetConfig> targets, IEnumerable<string> activeIds)
    {
        var active = activeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Select((target, index) => (target, index))
            .Where(entry => entry.target.Kind == OutputTargetContract.DesktopKind &&
                            entry.target.Desktop?.BoardEnabled == true && active.Contains(entry.target.Id))
            .OrderBy(entry => entry.target.Desktop!.ZOrder)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.target.Id)
            .ToArray();
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
    private const int GwlExStyle = -20;
    private const uint SwpFrameChangedNoMove = 0x0001u | 0x0002u | 0x0004u | 0x0010u | 0x0020u;
    // Reorder only normal (non-topmost) sibling windows, without resizing or activation.
    internal const uint StackFlags = 0x0001u | 0x0002u | 0x0010u | 0x0200u;
    private const int WmNcHitTest = 0x0084;
    internal const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private readonly Action _render;
    private readonly Action<Point> _savePosition;
    private readonly Action _restoreStack;
    private readonly Action<string?> _hostChanged;
    private readonly System.Windows.Forms.Timer _hostProbe = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _refresh = new();
    private readonly System.Windows.Forms.Timer _moveDebounce = new() { Interval = 500 };
    private DesktopLayeredSurface? _surface;
    private Size _canvas;
    private Point _lastPosition;
    private Point _screenPosition;
    private DesktopExplorerHost.Host? _host;
    private string? _hostError;
    private bool _wantedVisible;
    private bool _locked = true;
    private bool _configuring;
    private bool _disposed;

    public bool IsVisible => Visible && _host is { } host && IsHandleCreated &&
        DesktopExplorerHost.IsAttached(host, Handle);
    protected override CreateParams CreateParams
    {
        get
        {
            var result = base.CreateParams;
            result.ExStyle = DesktopPointerStyle.ForLock(result.ExStyle, _locked);
            return result;
        }
    }

    public DesktopOutputWindow(Action render, Action<Point> savePosition, Action restoreStack,
        Action<string?> hostChanged)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _savePosition = savePosition ?? throw new ArgumentNullException(nameof(savePosition));
        _restoreStack = restoreStack ?? throw new ArgumentNullException(nameof(restoreStack));
        _hostChanged = hostChanged ?? throw new ArgumentNullException(nameof(hostChanged));
        FormBorderStyle = FormBorderStyle.None;
        // Desktop panels are managed through the Runtime tray, not Windows taskbar buttons.
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        _refresh.Tick += (_, _) => { if (IsVisible) _render(); };
        _hostProbe.Tick += (_, _) => TryShowHosted();
        _moveDebounce.Tick += (_, _) =>
        {
            _moveDebounce.Stop();
            if (_locked || !IsVisible)
                return;
            var physical = DesktopExplorerHost.PhysicalPoint(Handle);
            if (physical == _lastPosition)
                return;
            _savePosition(physical);
            _screenPosition = physical;
            _lastPosition = physical;
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }

    private Point Resolve(Point requested) =>
        DesktopPlacement.Recover(requested, _canvas,
            Screen.AllScreens.Select(s => s.WorkingArea).ToArray(),
            Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea);

    public void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked)
    {
        // Never allow WinForms geometry/DPI handle recreation to expose a popup.
        // The caller restores the embedded window after adopting its renderer.
        if (Visible)
        {
            _refresh.Stop();
            Hide();
        }
        _configuring = true;
        try
        {
            _moveDebounce.Stop();
            Text = title;
            _canvas = clientPixels;
            _locked = locked;
            ClientSize = clientPixels;
            _screenPosition = Resolve(location);
            Location = _host is { } host && DesktopExplorerHost.IsAttached(host, Handle)
                ? DesktopExplorerHost.ParentPoint(host.Handle, _screenPosition)
                : _screenPosition;
            _lastPosition = _screenPosition;
            _refresh.Interval = refreshMs;
            ApplyLockStyle();
        }
        finally { _configuring = false; }
    }

    // Lock updates an existing window immediately on Runtime configuration reload.
    // WS_EX_TRANSPARENT with WS_EX_LAYERED bypasses hit-testing for all pixels.
    // We do not use HTTRANSPARENT: it is not a reliable cross-process pass-through.
    private void ApplyLockStyle()
    {
        if (!IsHandleCreated)
            return;

        var current = GetWindowLongPtr(Handle, GwlExStyle);
        if (current == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Desktop extended window styles.");

        var oldStyles = unchecked((int)(long)current);
        var newStyles = DesktopPointerStyle.ForLock(oldStyles, _locked);
        if (oldStyles == newStyles)
            return;

        if (SetWindowLongPtr(Handle, GwlExStyle, new IntPtr(newStyles)) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot change Desktop pointer hit-testing.");
        if (!SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChangedNoMove))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot refresh Desktop pointer hit-testing.");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    internal void PlaceBehind(DesktopOutputWindow higher)
    {
        if (_disposed || higher._disposed || !IsVisible || !higher.IsVisible ||
            _host != higher._host || !IsHandleCreated || !higher.IsHandleCreated)
            return;
        if (!SetWindowPos(Handle, higher.Handle, 0, 0, 0, 0, StackFlags))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot restore Desktop sibling window stacking order.");
    }

    public void ShowWindow()
    {
        _wantedVisible = true;
        _hostProbe.Start();
        TryShowHosted();
    }

    // Probe only Explorer host identity/lifetime, not global Z-order. A missing
    // host must NEVER expose an ordinary top-level Desktop overlay.
    private void TryShowHosted()
    {
        if (_disposed || !_wantedVisible)
            return;
        try
        {
            var found = DesktopExplorerHost.Discover() ??
                throw new InvalidOperationException("No supported Explorer wallpaper WorkerW is available.");
            var wasVisible = IsVisible;
            var isCurrent = _host == found && IsHandleCreated &&
                DesktopExplorerHost.IsAttached(found, Handle);
            if (!isCurrent)
            {
                _refresh.Stop();
                if (Visible)
                    Hide();
                _configuring = true;
                try
                {
                    // Creating Handle while hidden prevents even a transient popup.
                    DesktopExplorerHost.Attach(Handle, found, _screenPosition, _canvas);
                    _host = found;
                    Location = DesktopExplorerHost.ParentPoint(found.Handle, _screenPosition);
                }
                finally { _configuring = false; }
            }
            if (!Visible)
                Show();
            if (!IsVisible)
                throw new InvalidOperationException("Native Desktop panel did not remain parented to Explorer.");
            if (!isCurrent)
                DesktopExplorerHost.RestoreBehindIcons(Handle, found);
            RecoverVisible();
            _refresh.Start();
            if (!isCurrent || _hostError is not null)
            {
                _hostError = null;
                _hostChanged(null);
                _restoreStack();
            }
            if (!wasVisible)
                _render();
        }
        catch (Exception ex)
        {
            _refresh.Stop();
            if (Visible)
                Hide();
            _host = null;
            if (_hostError != ex.Message)
            {
                _hostError = ex.Message;
                _hostChanged(_hostError);
            }
        }
    }

    public void HideWindow()
    {
        _moveDebounce.Stop();
        _refresh.Stop();
        _hostProbe.Stop();
        _wantedVisible = false;
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
        _surface.Present(Handle, frame);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_configuring || _locked || _disposed || !IsVisible)
            return;
        _moveDebounce.Stop();
        _moveDebounce.Start();
    }

    protected override void WndProc(ref Message m)
    {
        // Unlocked window: native dragging without resize; locked: the OS ignores
        // this layered window entirely for mouse targeting (WS_EX_TRANSPARENT).
        if (m.Msg == WmNcHitTest && !_locked)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1) // HTCLIENT
                m.Result = (IntPtr)2; // HTCAPTION
            return;
        }
        // WM_EXITSIZEMOVE is sent when the native caption drag ends.
        // Reconcile sibling order immediately on release, without timers or reload.
        if (m.Msg == WmExitSizeMove)
        {
            base.WndProc(ref m);
            if (!_disposed && !_locked && IsVisible)
                _restoreStack();
            return;
        }
        if (m.Msg == WmDpiChanged)
        {
            // A DPI transition may recreate a WinForms handle: keep it hidden
            // until its Explorer child-window ownership has been verified again.
            if (Visible)
            {
                _refresh.Stop();
                Hide();
            }
            _configuring = true;
            try
            {
                base.WndProc(ref m);
                if (_canvas.Width > 0 && _canvas.Height > 0 && ClientSize != _canvas)
                    ClientSize = _canvas; // preserve canonical physical pixel size
                RecoverVisible();
            }
            finally { _configuring = false; }
            TryShowHosted();
            return;
        }
        base.WndProc(ref m);
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsHandleCreated)
            return;
        try { BeginInvoke((Action)(() => { TryShowHosted(); RecoverVisible(); })); }
        catch (InvalidOperationException) { /* The host may be recreating; the lifetime probe will retry. */ }
    }

    private void RecoverVisible()
    {
        if (_disposed || _canvas.Width <= 0 || _canvas.Height <= 0)
            return;
        var physical = IsVisible ? DesktopExplorerHost.PhysicalPoint(Handle) : _screenPosition;
        var recovered = Resolve(physical);
        if (recovered == physical)
            return;
        _configuring = true;
        try
        {
            _moveDebounce.Stop();
            _screenPosition = recovered;
            if (_host is { } host && IsVisible)
                Location = DesktopExplorerHost.ParentPoint(host.Handle, recovered);
            else
                Location = recovered;
            _lastPosition = recovered; // do not overwrite saved position of detached monitor
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
            _hostProbe.Dispose();
            _moveDebounce.Dispose();
            _surface?.Dispose();
            _surface = null;
        }
        base.Dispose(disposing);
    }
}
