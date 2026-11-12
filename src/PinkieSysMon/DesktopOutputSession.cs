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
    private readonly OutputDiagnosticEvents? _diagnostics;
    private readonly IDesktopOutputWindow _window;
    private DashboardRenderer _renderer;
    private DesktopCanvasShadowCompositor? _shadowCompositor;
    private WidgetActionInteractionMap _interactions;
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
        Func<Action, IDesktopOutputWindow>? windowFactory = null, OutputDiagnosticEvents? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dashboard);
        if (target.Kind != OutputTargetContract.DesktopKind || target.Desktop?.BoardEnabled != true)
            throw new ArgumentException("Desktop session requires an enabled Desktop output.", nameof(target));

        _id = target.Id;
        _diagnostics = diagnostics;
        _config = config;
        _target = target;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _interactions = new WidgetActionInteractionMap(dashboard);
        var viewport = DesktopCanvasViewport.Create(dashboard.Canvas);
        _renderer = new DashboardRenderer(dashboard, history: _metrics.History, log: _log,
            interactiveCalculators: true, rasterSharpness: config.Renderer.RasterSharpness);
        IDesktopOutputWindow? window = null;
        DesktopCanvasShadowCompositor? shadow = null;
        try
        {
            if (viewport.HasShadow)
                shadow = new DesktopCanvasShadowCompositor(viewport, dashboard.Canvas);
            window = (windowFactory ?? (render => new DesktopOutputWindow(render,
                point => PersistPosition(dashboard.BaseDirectory, point),
                () => _restoreStack?.Invoke(), error => _log.Error($"Desktop WidgetAction input failed; observedErrorCode={error}."),
                _log, (phase, detail) => _diagnostics?.Record(phase, _id, detail))))(DrawFrame)
                ?? throw new InvalidOperationException("Desktop window factory returned null.");
            window.ConfigureViewport(viewport);
            window.Configure(target.Name, viewport.Canvas, LocationFor(target), IntervalFor(config, target),
                target.Desktop?.Lock ?? true);
            window.ConfigureCalculatorInput(PressCalculator);
            window.ConfigureInteractions(_interactions);
            _window = window;
            _shadowCompositor = shadow;
            shadow = null;
        }
        catch
        {
            shadow?.Dispose();
            window?.Dispose();
            _renderer.Dispose();
            throw;
        }
    }

    private void PressCalculator(string id, string key, IntPtr clipboardOwner)
    {
        try
        {
            if (_renderer.PressCalculator(id, key, clipboardOwner))
                _window.RequestFrame();
        }
        catch (Exception error)
        {
            _log.Warn($"Desktop calculator click failed; widget={id}.", error);
        }
    }

    private void PersistPosition(string dashboardDirectory, Point point)
    {
        try
        {
            var path = Path.Combine(ApplicationRootLocator.Find(dashboardDirectory),
                "config", "app.json");
            DesktopPositionStore.Save(path, _id, point);
            _diagnostics?.Record("desktop.placement.committed", _id, "saved=true");
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
    bool IDesktopStackSession.IsBelowDesktop =>
        _window is DesktopOutputWindow native && native.IsBelowDesktop;
    void IDesktopStackSession.SetStackRestorer(Action restorer) =>
        _restoreStack = restorer ?? throw new ArgumentNullException(nameof(restorer));

    void IDesktopStackSession.PlaceAtDesktopBack()
    {
        if (_window is DesktopOutputWindow window)
            window.PlaceAtDesktopBack();
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
                _window.IsVisible ? "DISPLAYING" : "WINDOW_UNAVAILABLE");
            _log.Info($"Desktop output '{TargetName}' started; targetId={_id}; no USB transport; {_renderer.ResourceSummary}.");
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

        var viewport = DesktopCanvasViewport.Create(dashboard.Canvas);
        var interactions = new WidgetActionInteractionMap(dashboard);
        var renderer = new DashboardRenderer(dashboard, history: _metrics.History, log: _log,
            interactiveCalculators: true, rasterSharpness: config.Renderer.RasterSharpness);
        try
        {
            var shadow = viewport.HasShadow ? new DesktopCanvasShadowCompositor(viewport, dashboard.Canvas) : null;
            return new PreparedReconfiguration(config, target, viewport, interactions, renderer, shadow);
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
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

        var viewport = DesktopCanvasViewport.Create(dashboard.Canvas);
        var shadow = viewport.HasShadow ? new DesktopCanvasShadowCompositor(viewport, dashboard.Canvas) : null;
        return new PreparedReconfiguration(config, target, viewport, _interactions, _renderer.Retain(), shadow);
    }

    public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration)
    {
        ThrowIfDisposed();
        if (reconfiguration is not PreparedReconfiguration prepared ||
            !prepared.Target.Id.Equals(_id, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid desktop output reconfiguration.", nameof(reconfiguration));

        // Apply fallible native window configuration before transferring the prepared renderer.
        // On a window error the prepared plan still owns its raster resources for rollback.
        _window.ConfigureViewport(prepared.Viewport);
        _window.Configure(prepared.Target.Name, prepared.Viewport.Canvas,
            LocationFor(prepared.Target), IntervalFor(prepared.Config, prepared.Target),
            prepared.Target.Desktop?.Lock ?? true);
        _window.ConfigureInteractions(prepared.Interactions);
        var previous = _renderer;
        var previousShadow = _shadowCompositor;
        var wasPublishing = Publishing;
        _renderer = prepared.TakeRenderer();
        _shadowCompositor = prepared.TakeShadow();
        _interactions = prepared.Interactions;
        _config = prepared.Config;
        _target = prepared.Target;
        if (wasPublishing && !Publishing)
            _metrics.Publish(OutputMetricContract.Unavailable(_target));
        _lastPublishAt = 0;
        if (Publishing)
            PublishState();
        if (_started && !_suspended && !_idlePaused)
            _window.ShowWindow(); // restore the top-level panel after geometry/style changes
        _window.RequestFrame();
        try { previous.Dispose(); }
        catch (Exception ex) { _log.Warn($"Desktop output '{_id}' former renderer cleanup failed.", ex); }
        try { previousShadow?.Dispose(); }
        catch (Exception ex) { _log.Warn($"Desktop output '{_id}' former shadow compositor cleanup failed.", ex); }
        _diagnostics?.Record("desktop.renderer.replaced", _id,
            $"width={prepared.Viewport.Canvas.Width}; height={prepared.Viewport.Canvas.Height}; locked={_target.Desktop?.Lock}; shadow={prepared.Viewport.HasShadow}");
        _log.Info($"Desktop output '{_id}' reconfigured: canvas={prepared.Viewport.Canvas.Width}x{prepared.Viewport.Canvas.Height} physical pixels.");
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
            paused ? "IDLE_PAUSED" : _started ? (_window.IsVisible ? "DISPLAYING" : "WINDOW_UNAVAILABLE") : "STOPPED");
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
        ChangeState(_idlePaused ? "IDLE_PAUSED" : _started ? (_window.IsVisible ? "DISPLAYING" : "WINDOW_UNAVAILABLE") : "STOPPED");
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
            if (_interactions.HasDerivedGeometry)
            {
                var updated = _renderer.CreateWidgetActionMap();
                if (!_interactions.IsEquivalent(updated))
                {
                    _window.RefreshInteractions(updated);
                    _interactions = updated;
                }
            }
            _window.Present(_shadowCompositor is null ? frame.Bitmap : _shadowCompositor.Compose(frame.Bitmap));

            var now = Stopwatch.GetTimestamp();
            Volatile.Write(ref _lastFrameMs, Stopwatch.GetElapsedTime(startedAt, now).TotalMilliseconds);
            if (Interlocked.Increment(ref _frameCount) == 1)
                _diagnostics?.Record("desktop.first.frame.presented", _id, "presentReturned=true; physicalDeviceObserved=false");
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
        if (_state != state) _diagnostics?.Record("desktop.state.changed", _id, $"previous={_state}; current={state}");
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
        using var cleanup = new CleanupOperation(_log, "desktop.dispose", _id);
        try { cleanup.Step("window.dispose", _window.Dispose); }
        finally
        {
            cleanup.Step("renderer.dispose", _renderer.Dispose);
            if (_shadowCompositor is not null)
                cleanup.Step("desktop.shadow.dispose", _shadowCompositor.Dispose);
            cleanup.Step("metrics.unavailable", () => _metrics.Publish(OutputMetricContract.Unavailable(_target)));
        }
        cleanup.Complete();
        _diagnostics?.Record("desktop.disposed", _id, "cleanupReturned=true");
    }

    private sealed class PreparedReconfiguration(
        AppConfig config, OutputTargetConfig target, DesktopCanvasViewport viewport,
        WidgetActionInteractionMap interactions, DashboardRenderer renderer,
        DesktopCanvasShadowCompositor? shadow)
        : IOutputSessionReconfiguration
    {
        private DashboardRenderer? _renderer = renderer;
        private DesktopCanvasShadowCompositor? _shadow = shadow;
        public AppConfig Config { get; } = config;
        public OutputTargetConfig Target { get; } = target;
        public DesktopCanvasViewport Viewport { get; } = viewport;
        public WidgetActionInteractionMap Interactions { get; } = interactions;
        public DashboardRenderer TakeRenderer()
        {
            var owned = _renderer ?? throw new ObjectDisposedException(nameof(PreparedReconfiguration));
            _renderer = null;
            return owned;
        }
        public DesktopCanvasShadowCompositor? TakeShadow()
        {
            var owned = _shadow;
            _shadow = null;
            return owned;
        }
        public void Dispose()
        {
            _renderer?.Dispose();
            _shadow?.Dispose();
            _renderer = null;
            _shadow = null;
        }
    }
}

// Native per-pixel layered-window hit-testing is independent of the pixels:
// locked visual HWNDs ignore all mouse input, including over opaque widgets.
// A separate region-restricted companion owns widget Action input.
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
    bool IsBelowDesktop { get; }
    void PlaceAtDesktopBack();
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
    // Default interface implementation keeps existing test windows and non-native consumers compatible.
    void ConfigureViewport(DesktopCanvasViewport viewport) { }
    void ConfigureCalculatorInput(Action<string, string, IntPtr> press) { }
    void ConfigureInteractions(WidgetActionInteractionMap interactions) { }
    void RefreshInteractions(WidgetActionInteractionMap interactions) { }
    void ShowWindow();
    void HideWindow();
    void RequestFrame();
    void Present(SKBitmap frame);
}

internal sealed class DesktopOutputWindow : Form, IDesktopOutputWindow
{
    private const int GwlExStyle = -20;
    private const uint SwpFrameChangedNoMove = 0x0001u | 0x0002u | 0x0004u | 0x0010u | 0x0020u;
    private const int WmNcHitTest = 0x0084;
    private const int WmEnterSizeMove = 0x0231;
    internal const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private readonly Action _render;
    private readonly Action<Point> _savePosition;
    private readonly Action _restoreStack;
    private readonly System.Windows.Forms.Timer _refresh = new();
    private readonly System.Windows.Forms.Timer _moveDebounce = new() { Interval = 500 };
    private DesktopLayeredSurface? _surface;
    private DesktopWidgetActionInput? _input;
    private Action<string, string, IntPtr>? _calculatorInput;
    private readonly Action<int> _reportWidgetActionFailure;
    private Size _canvas;
    private DesktopCanvasViewport? _viewport;
    private Point _lastPosition;
    private bool _locked = true;
    private bool _configuring;
    private bool _nativeDragActive;
    private bool _recoverAfterDrag;
    private bool _disposed;

    public bool IsVisible => Visible;
    internal bool IsBelowDesktop => !_disposed && Visible && IsHandleCreated &&
        (DesktopBottomPlacement.IsBelowDesktop(Handle) || _input?.IsPairBroken == true);

    protected override CreateParams CreateParams
    {
        get
        {
            var result = base.CreateParams;
            result.ExStyle = DesktopPointerStyle.ForLock(
                (result.ExStyle & ~DesktopBottomPlacement.WsExAppWindow) |
                    DesktopBottomPlacement.WsExToolWindow, _locked);
            return result;
        }
    }

    private readonly FileLogger? _log;
    private readonly Action<string, string>? _observe;
    private bool? _lastBottomFallback;

    public DesktopOutputWindow(Action render, Action<Point> savePosition, Action restoreStack, Action<int>? reportWidgetActionFailure = null,
        FileLogger? log = null, Action<string, string>? observe = null)
    {
        _log = log; _observe = observe;
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _savePosition = savePosition ?? throw new ArgumentNullException(nameof(savePosition));
        _restoreStack = restoreStack ?? throw new ArgumentNullException(nameof(restoreStack));
        _reportWidgetActionFailure = reportWidgetActionFailure ?? (_ => { });
        FormBorderStyle = FormBorderStyle.None;
        // Desktop panels are managed through the Runtime tray, not Windows taskbar buttons.
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        _refresh.Tick += (_, _) => _render();
        _moveDebounce.Tick += (_, _) =>
        {
            _moveDebounce.Stop();
            if (_locked || !Visible || _viewport is null)
                return;
            var canvasPosition = _viewport.CanvasOriginForSurface(Location);
            if (canvasPosition == _lastPosition)
                return;
            _savePosition(canvasPosition);
            _lastPosition = canvasPosition;
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }

    private Point Resolve(Point requested) =>
        DesktopPlacement.Recover(requested, _canvas,
            Screen.AllScreens.Select(s => s.WorkingArea).ToArray(),
            Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea);

    public void ConfigureViewport(DesktopCanvasViewport viewport) =>
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

    public void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked)
    {
        _configuring = true;
        try
        {
            _input?.Hide();
            _moveDebounce.Stop();
            Text = title;
            _canvas = clientPixels;
            _viewport ??= DesktopCanvasViewport.ForCanvas(clientPixels);
            if (_viewport.Canvas != clientPixels)
                throw new InvalidOperationException("Desktop viewport does not match the logical Canvas.");
            _locked = locked;
            ClientSize = _viewport.Surface;
            var recovered = Resolve(location);
            Location = _viewport.SurfaceOriginForCanvas(recovered);
            _lastPosition = recovered;
            _refresh.Interval = refreshMs;
            ApplyLockStyle();
            _observe?.Invoke("desktop.window.configured", $"locked={locked}; width={clientPixels.Width}; height={clientPixels.Height}; refreshMs={refreshMs}");
        }
        finally { _configuring = false; }
    }

    public void ConfigureCalculatorInput(Action<string, string, IntPtr> press) =>
        _calculatorInput = press ?? throw new ArgumentNullException(nameof(press));

    public void ConfigureInteractions(WidgetActionInteractionMap interactions)
    {
        DesktopWidgetActionInput? candidate = null;
        try
        {
            if (!interactions.IsEmpty)
                candidate = new DesktopWidgetActionInput(interactions, this, _locked, BeginWidgetActionDrag,
                    _reportWidgetActionFailure, _log, _viewport, _calculatorInput);
            var previous = _input; _input = candidate; candidate = null;
            previous?.Dispose();
            if (Visible) _input?.ShowRestricted();
        }
        finally { candidate?.Dispose(); }
    }

    public void RefreshInteractions(WidgetActionInteractionMap interactions)
    {
        if (_input is null)
        {
            ConfigureInteractions(interactions);
            return;
        }
        _input.UpdateMap(interactions);
    }

    private void BeginWidgetActionDrag(Point screen)
    {
        if (_locked || _disposed || !Visible) return;
        var packed = new IntPtr(unchecked((int)((ushort)screen.X | ((uint)(ushort)screen.Y << 16))));
        SendMessage(Handle, 0x00a1, new IntPtr(2), packed);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

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

    internal void PlaceAtDesktopBack()
    {
        if (!_disposed && IsVisible && IsHandleCreated)
        {
            var fallback = DesktopBottomPlacement.PlaceAtDesktopBack(Handle);
            if (_lastBottomFallback != fallback)
                _observe?.Invoke("desktop.bottom.placement", $"shellFallback={fallback}; nativePlacementReturned=true");
            _lastBottomFallback = fallback;
            _input?.PlaceAboveVisual();
        }
    }

    public void ShowWindow()
    {
        if (!Visible)
            Show();
        RecoverVisible();
        PlaceAtDesktopBack();
        _input?.ShowRestricted();
        _refresh.Start();
        _render();
    }

    public void HideWindow()
    {
        _input?.Hide();
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
        if (_viewport is not null && (frame.Width != _viewport.Surface.Width ||
            frame.Height != _viewport.Surface.Height || ClientSize != _viewport.Surface))
            throw new InvalidOperationException("Desktop frame and padded native surface dimensions disagree.");
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
        _input?.MoveWithVisual();
        if (_configuring || _locked || _disposed || !Visible)
            return;
        _moveDebounce.Stop();
        _moveDebounce.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmEnterSizeMove)
        {
            _nativeDragActive = true;
            base.WndProc(ref m);
            return;
        }
        // Unlocked window: native dragging without resize; locked: the OS ignores
        // this layered window entirely for mouse targeting (WS_EX_TRANSPARENT).
        if (m.Msg == WmNcHitTest && !_locked)
        {
            // The shadow is visible outside the logical Canvas but never
            // belongs to the native dragging/activation region.
            if (_viewport is { HasShadow: true } viewport)
            {
                var raw = m.LParam.ToInt64();
                var screen = new Point(unchecked((short)raw), unchecked((short)(raw >> 16)));
                if (!viewport.ContainsCanvasPixel(PointToClient(screen)))
                {
                    m.Result = IntPtr.Zero; // HTNOWHERE: no caption drag on shadow margins
                    return;
                }
            }

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
            _nativeDragActive = false;
            if (_recoverAfterDrag)
            {
                _recoverAfterDrag = false;
                RecoverVisible();
            }
            if (!_disposed && !_locked && Visible)
                _restoreStack();
            return;
        }
        if (m.Msg == WmDpiChanged)
        {
            _observe?.Invoke("desktop.dpi.changed", "Windows DPI message received");
            _input?.Hide();
            _configuring = true;
            try
            {
                base.WndProc(ref m);
                if (_viewport is not null && ClientSize != _viewport.Surface)
                    ClientSize = _viewport.Surface; // preserve native padded physical pixel size
                RecoverVisible();
            }
            finally { _configuring = false; }
            if (Visible)
            {
                _input?.ShowRestricted();
                _render();
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsHandleCreated)
            return;
        _observe?.Invoke("desktop.display.topology.changed", "Windows display notification received");
        try { BeginInvoke((Action)RecoverVisible); }
        catch (InvalidOperationException) when (_disposed || !IsHandleCreated) { }
    }

    private void RecoverVisible()
    {
        if (_disposed || _canvas.Width <= 0 || _canvas.Height <= 0)
            return;
        // Do not reposition the HWND while Windows owns a native caption drag.
        // Recheck after release if a DPI/display event requested recovery.
        if (_nativeDragActive)
        {
            _recoverAfterDrag = true;
            return;
        }
        if (_viewport is null)
            return;
        var canvasPosition = _viewport.CanvasOriginForSurface(Location);
        var recovered = Resolve(canvasPosition);
        if (recovered == canvasPosition)
            return;
        _configuring = true;
        try
        {
            _moveDebounce.Stop();
            Location = _viewport.SurfaceOriginForCanvas(recovered);
            _observe?.Invoke("desktop.placement.recovered", "visibleAreaRecoveryApplied=true; savedPositionUnchanged=true");
            _lastPosition = recovered; // do not overwrite location of detached monitor
        }
        finally { _configuring = false; }
        if (Visible)
            _render();
    }

    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _input?.Hide();
        else if (!_configuring) _input?.ShowRestricted();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _input?.Hide();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _input?.Dispose();
            _input = null;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            _refresh.Dispose();
            _moveDebounce.Dispose();
            _surface?.Dispose();
            _surface = null;
        }
        base.Dispose(disposing);
    }
}

