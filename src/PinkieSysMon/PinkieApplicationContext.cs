using System.Drawing;
using System.Diagnostics;
using Microsoft.Win32;
using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

internal sealed class PinkieApplicationContext : ApplicationContext
{
    private readonly string _baseDir;
    private readonly FileLogger _log;
    private readonly MetricStore _metrics = new();
    private readonly IMetricSource[] _sources;

    private readonly NotifyIcon _trayIcon = new();
    private readonly Icon? _applicationIcon;
    private readonly ToolStripMenuItem _statusItem = new("Starting...") { Enabled = false };
    private readonly ToolStripMenuItem _dashboardsMenu = new("Dashboards");
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly Control _uiInvoker = new();
    private readonly RuntimeIpcServer _ipcServer;
    private readonly RuntimeOutputControl _outputControl;
    private readonly WindowsMediaTelemetrySource _mediaTelemetry;
    private readonly WindowsAppNotificationService _notifications;

    private AppConfig _config;
    private TelemetryConfig _telemetryConfig;
    private CanonicalDashboardDefinition _dashboard;
    private TelemetryEngine _telemetry;
    private OutputSessionManager _outputSessions;
    private bool _exiting;

    private string AppConfigPath => Path.Combine(_baseDir, "config", "app.json");
    private string TelemetryConfigPath => Path.Combine(_baseDir, "config", "telemetry.json");

    public PinkieApplicationContext(string baseDir, FileLogger log, bool startOutputSessions = true)
    {
        _baseDir = baseDir;
        _log = log;
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _mediaTelemetry = new WindowsMediaTelemetrySource(log);
        _sources =
        [
            new WindowsSystemTelemetrySource(log),
            new WindowsNetworkTelemetrySource(log),
            new WindowsPowerTelemetrySource(log),
            _mediaTelemetry,
            new LibreHardwareMonitorRawTelemetrySource(log),
            new IcueSensorLogTelemetrySource(baseDir, log)
        ];

        (_config, _telemetryConfig, _dashboard) = LoadInitial();
        _mediaTelemetry.SetEndpointTypeOverrides(_config.Media.EndpointTypeOverrides);
        var requiredMetrics = DashboardMetricUsage.Collect(_dashboard);
        _telemetry = new TelemetryEngine(
            _metrics,
            _sources,
            _telemetryConfig,
            MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders),
            requiredMetrics,
            _log);
        _outputSessions = new OutputSessionManager(_config, _dashboard, _metrics, _log);
        _outputControl = new RuntimeOutputControl(_outputSessions, () => _config.Dashboard.Active);
        _notifications = new WindowsAppNotificationService(log);

        BuildTray();
        _uiInvoker.CreateControl();
        _ipcServer = new RuntimeIpcServer(HandleIpcCommandAsync, _log);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _statusTimer.Tick += (_, _) => RefreshTrayStatus();
        _statusTimer.Start();

        _telemetry.Start();
        if (startOutputSessions)
            _outputSessions.Start();
        _ipcServer.Start();

        _log.Info(
            $"PinkieSysMon started. Direct Skia renderer; no WebView2. " +
            $"dashboard={_config.Dashboard.Active}; refresh={_config.Display.RefreshIntervalMs}ms; " +
            $"canvas={_dashboard.Canvas.Width}x{_dashboard.Canvas.Height}; orientation={_dashboard.Canvas.Orientation}; " +
            $"providers={string.Join(",", MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}; " +
            $"configEnabledMetrics={_telemetryConfig.Metrics.Count(x => x.Value.Enabled)}; " +
            $"dashboardMetrics={requiredMetrics.Count}; outputTargets={_config.Outputs.Targets.Count}; " +
            $"outputAutostart={(startOutputSessions ? "enabled" : "suppressed")}.");
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var reload = new ToolStripMenuItem("Reload Configuration");
        reload.Click += (_, _) => ReloadConfiguration();
        menu.Items.Add(reload);

        var openLogs = new ToolStripMenuItem("Open Logs Folder");
        openLogs.Click += (_, _) => OpenFolder(Path.Combine(_baseDir, "logs"));
        menu.Items.Add(openLogs);

        var editDashboard = new ToolStripMenuItem("Edit Current Dashboard...");
        editDashboard.Click += (_, _) => OpenDashboardEditor();
        menu.Items.Add(editDashboard);

        menu.Items.Add(new ToolStripSeparator());
        _dashboardsMenu.DropDownOpening += (_, _) => RefreshDashboardMenuSafe();
        menu.Items.Add(_dashboardsMenu);
        RefreshDashboardMenuSafe();

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        _trayIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _trayIcon.Text = "PinkieSysMon";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
    }

    private (AppConfig App, TelemetryConfig Telemetry, CanonicalDashboardDefinition Dashboard) LoadInitial()
    {
        var app = AppConfig.Load(AppConfigPath, _log);
        var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);

        try
        {
            var dashboard = LoadDashboard(app.Dashboard.Active);
            return (app, telemetry, dashboard);
        }
        catch (Exception ex)
        {
            _log.Error($"Configured dashboard '{app.Dashboard.Active}' could not be loaded; searching for a fallback", ex);
        }

        foreach (var name in DashboardCatalog.Discover(_baseDir))
        {
            if (name.Equals(app.Dashboard.Active, StringComparison.OrdinalIgnoreCase))
                continue;

            CanonicalDashboardDefinition dashboard;
            try
            {
                dashboard = LoadDashboard(name);
            }
            catch (Exception ex)
            {
                _log.Error($"Fallback dashboard '{name}' could not be loaded", ex);
                continue;
            }

            app.Dashboard.Active = name;
            try
            {
                app.Save(AppConfigPath);
            }
            catch (Exception saveEx)
            {
                _log.Warn(
                    $"Fallback dashboard '{name}' loaded successfully, but app.json could not persist the selection; continuing with the valid in-memory fallback.",
                    saveEx);
            }

            _log.Info($"Using fallback dashboard '{name}'.");
            return (app, telemetry, dashboard);
        }

        throw new InvalidDataException("No valid dashboard is available in the dashboards directory.");
    }

    private CanonicalDashboardDefinition LoadDashboard(string name)
    {
        var path = DashboardCatalog.GetDefinitionPath(_baseDir, name);
        return CanonicalDashboardDefinition.Load(path);
    }

    private RuntimeIpcResponse ReloadConfiguration(bool showNotification = true)
    {
        try
        {
            var app = AppConfig.Load(AppConfigPath, _log);
            var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);
            var dashboard = LoadDashboard(app.Dashboard.Active);

            ApplyConfiguration(app, telemetry, dashboard);
            _log.Info($"Configuration, telemetry policy, and dashboard '{app.Dashboard.Active}' reloaded successfully.");
            if (showNotification)
                _notifications.Show("Configuration reloaded.");
            return new RuntimeIpcResponse(true, $"Reloaded dashboard '{app.Dashboard.Active}'.");
        }
        catch (Exception ex)
        {
            _log.Error("Configuration reload failed; previous configuration remains active", ex);
            if (showNotification)
                _notifications.Show($"Reload failed: {ex.Message}", RuntimeNotificationKind.Error);
            return new RuntimeIpcResponse(false, ex.Message);
        }
    }

    private Task<RuntimeIpcResponse> HandleIpcCommandAsync(string command, CancellationToken cancellationToken)
    {
        var parsed = RuntimeIpcProtocol.ParseCommand(command);

        if (parsed.Kind == RuntimeIpcCommandKind.Ping)
            return Task.FromResult(new RuntimeIpcResponse(true, _config.Dashboard.Active));

        if (_exiting || _uiInvoker.IsDisposed)
            return Task.FromResult(new RuntimeIpcResponse(false, "PinkieSysMon runtime is shutting down."));

        if (parsed.Kind == RuntimeIpcCommandKind.Invalid)
            return Task.FromResult(new RuntimeIpcResponse(false, parsed.Error ?? "Invalid IPC command."));

        if (parsed.Kind == RuntimeIpcCommandKind.Status)
            return Task.FromResult(new RuntimeIpcResponse(true, RuntimeIpcProtocol.SerializeStatus(_outputControl.GetStatus())));

        if (parsed.Kind == RuntimeIpcCommandKind.Exit)
        {
            ScheduleExitAfterIpcResponse();
            return Task.FromResult(new RuntimeIpcResponse(true, "Runtime is stopping and unloading."));
        }

        return InvokeOnUiAsync(() =>
        {
            switch (parsed.Kind)
            {
                case RuntimeIpcCommandKind.ReloadConfiguration:
                    return ReloadConfiguration(showNotification: false);

                case RuntimeIpcCommandKind.StartOrReloadTarget:
                case RuntimeIpcCommandKind.StopTarget:
                case RuntimeIpcCommandKind.StartOrReloadAll:
                case RuntimeIpcCommandKind.StopAll:
                {
                    var response = _outputControl.Execute(parsed);
                    RefreshTrayStatus();

                    if (response.Success && _outputControl.ShouldExitRuntimeAfter(parsed))
                    {
                        ScheduleExitAfterIpcResponse();
                        return response with { Message = $"{response.Message} {RuntimeIpcProtocol.RuntimeStoppingMessage}" };
                    }

                    return response;
                }

                default:
                    return new RuntimeIpcResponse(false, $"Unsupported IPC command kind: {parsed.Kind}.");
            }
        }, cancellationToken);
    }

    private Task<RuntimeIpcResponse> InvokeOnUiAsync(
        Func<RuntimeIpcResponse> action,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<RuntimeIpcResponse>(cancellationToken);

        var completion = new TaskCompletionSource<RuntimeIpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var tuple = ((TaskCompletionSource<RuntimeIpcResponse> Completion, CancellationToken Token))state!;
                tuple.Completion.TrySetCanceled(tuple.Token);
            },
            (completion, cancellationToken));

        _ = completion.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            cancellationRegistration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            _uiInvoker.BeginInvoke((Action)(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    _log.Error("Runtime IPC output command failed", ex);
                    completion.TrySetResult(new RuntimeIpcResponse(false, ex.Message));
                }
            }));
        }
        catch (Exception ex)
        {
            completion.TrySetResult(new RuntimeIpcResponse(false, ex.Message));
        }

        return completion.Task;
    }

    private void ScheduleExitAfterIpcResponse()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(150).ConfigureAwait(false);
            if (_exiting || _uiInvoker.IsDisposed)
                return;

            try
            {
                _uiInvoker.BeginInvoke((Action)ExitThread);
            }
            catch (ObjectDisposedException)
            {
                // The UI invoker was disposed while shutdown was already in progress.
            }
            catch (InvalidOperationException)
            {
                // The UI handle was destroyed while shutdown was already in progress.
            }
        });
    }

    private void SwitchDashboard(string dashboardName)
    {
        if (dashboardName.Equals(_config.Dashboard.Active, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var app = AppConfig.Load(AppConfigPath, _log);
            app.Dashboard.Active = dashboardName;

            var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);
            var dashboard = LoadDashboard(dashboardName);

            ApplyConfiguration(app, telemetry, dashboard);

            try
            {
                app.Save(AppConfigPath);
            }
            catch (Exception saveEx)
            {
                _log.Error($"Dashboard '{dashboardName}' is active but the selection could not be persisted", saveEx);
                _notifications.Show("Dashboard switched, but app.json could not be saved.", RuntimeNotificationKind.Warning);
                return;
            }

            _log.Info($"Dashboard switched to '{dashboardName}'.");
            RefreshDashboardMenuSafe();
            _notifications.Show($"Dashboard: {dashboardName}");
        }
        catch (Exception ex)
        {
            _log.Error($"Dashboard switch to '{dashboardName}' failed; previous dashboard remains active", ex);
            _notifications.Show($"Dashboard switch failed: {ex.Message}", RuntimeNotificationKind.Error);
        }
    }

    private void ApplyConfiguration(AppConfig app, TelemetryConfig telemetry, CanonicalDashboardDefinition dashboard)
    {
        // Phase 1: prepare every operation that can fail before the live runtime is mutated.
        // OutputSessionManager performs its own renderer/session preparation internally.
        var requiredMetrics = DashboardMetricUsage.Collect(dashboard);
        var telemetryPlan = _telemetry.PrepareReconfiguration(
            telemetry,
            MetricProviderContract.GetEnabledProviderIds(app.MetricProviders),
            requiredMetrics);
        var mediaOverrides =
            WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(app.Media.EndpointTypeOverrides);

        var previousApp = _config;
        var previousTelemetry = _telemetryConfig;
        var previousDashboard = _dashboard;
        var previousMediaOverrides =
            WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(previousApp.Media.EndpointTypeOverrides);
        var previousRequiredMetrics = DashboardMetricUsage.Collect(previousDashboard);

        var outputCommitted = false;
        var telemetryCommitted = false;
        var mediaCommitted = false;

        try
        {
            // Phase 2: commit. Every stage has a prepared/validated rollback path. The output
            // manager is itself transactional across sibling sessions.
            _outputSessions.Reconfigure(app, dashboard);
            outputCommitted = true;

            _telemetry.CommitReconfiguration(telemetryPlan);
            telemetryCommitted = true;

            _mediaTelemetry.ApplyEndpointTypeOverrides(mediaOverrides);
            mediaCommitted = true;

            _config = app;
            _telemetryConfig = telemetry;
            _dashboard = dashboard;
        }
        catch (Exception applyEx)
        {
            var rollbackFailures = new List<Exception>();

            if (mediaCommitted)
            {
                try
                {
                    _mediaTelemetry.ApplyEndpointTypeOverrides(previousMediaOverrides);
                }
                catch (Exception rollbackEx)
                {
                    rollbackFailures.Add(rollbackEx);
                    _log.Error("Media configuration rollback failed.", rollbackEx);
                }
            }

            if (telemetryCommitted)
            {
                try
                {
                    _telemetry.Reconfigure(
                        previousTelemetry,
                        MetricProviderContract.GetEnabledProviderIds(previousApp.MetricProviders),
                        previousRequiredMetrics);
                }
                catch (Exception rollbackEx)
                {
                    rollbackFailures.Add(rollbackEx);
                    _log.Error("Telemetry configuration rollback failed.", rollbackEx);
                }
            }

            if (outputCommitted)
            {
                try
                {
                    _outputSessions.Reconfigure(previousApp, previousDashboard);
                }
                catch (Exception rollbackEx)
                {
                    rollbackFailures.Add(rollbackEx);
                    _log.Error("Output configuration rollback failed.", rollbackEx);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(
                    "Configuration apply failed and one or more subsystem rollback operations also failed.",
                    [applyEx, .. rollbackFailures]);
            }

            throw;
        }

        RefreshDashboardMenuSafe();
    }

    private void RefreshDashboardMenuSafe()
    {
        try
        {
            RefreshDashboardMenu();
        }
        catch (Exception ex)
        {
            // Tray menu discovery is derived UI state. Filesystem/menu failures are recoverable
            // and must not misreport or roll back an otherwise consistent runtime configuration.
            _log.Warn("Dashboard tray menu refresh failed.", ex);
            _dashboardsMenu.DropDownItems.Clear();
            _dashboardsMenu.DropDownItems.Add(
                new ToolStripMenuItem("(dashboard list unavailable)") { Enabled = false });
        }
    }

    private void RefreshDashboardMenu()
    {
        _dashboardsMenu.DropDownItems.Clear();
        var names = DashboardCatalog.Discover(_baseDir);

        if (names.Count == 0)
        {
            _dashboardsMenu.DropDownItems.Add(new ToolStripMenuItem("(no dashboards)") { Enabled = false });
            return;
        }

        foreach (var name in names)
        {
            var dashboardName = name;
            var item = new ToolStripMenuItem(dashboardName)
            {
                Checked = dashboardName.Equals(_config.Dashboard.Active, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => SwitchDashboard(dashboardName);
            _dashboardsMenu.DropDownItems.Add(item);
        }
    }

    private void RefreshTrayStatus()
    {
        var statuses = _outputSessions.GetStatuses();
        if (statuses.Count == 1)
        {
            var status = statuses[0];
            _statusItem.Text = status.IsConnected
                ? $"{status.TargetName} connected · {_config.Dashboard.Active} · frame {status.SentFrames} · {status.LastFrameMs:F1} ms · {status.LastJpegBytes / 1024.0:F0} KiB"
                : $"{status.TargetName} {status.UsbState.ToLowerInvariant()} · {_config.Dashboard.Active}";

            var tooltip = status.IsConnected
                ? $"PinkieSysMon - {_config.Dashboard.Active}"
                : "PinkieSysMon - output waiting";
            _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
            return;
        }

        var active = statuses.Count(status => status.IsActive);
        var connected = statuses.Count(status => status.IsConnected);
        _statusItem.Text = $"Outputs {connected}/{active} connected · {statuses.Count} configured · {_config.Dashboard.Active}";

        var multiTooltip = connected == active && active > 0
            ? $"PinkieSysMon - {_config.Dashboard.Active}"
            : $"PinkieSysMon - outputs {connected}/{active}";
        _trayIcon.Text = multiTooltip.Length <= 63 ? multiTooltip : multiTooltip[..63];
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_exiting)
            return;

        try
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    _log.Info("Windows power event: suspend.");
                    _outputSessions.Suspend();
                    break;
                case PowerModes.Resume:
                    _log.Info("Windows power event: resume.");
                    _outputSessions.Resume();
                    break;
                case PowerModes.StatusChange:
                    _log.Info("Windows power event: status change.");
                    break;
            }
        }
        catch (ObjectDisposedException) when (_exiting)
        {
            // Shutdown won the race with a queued SystemEvents callback.
        }
        catch (Exception ex)
        {
            // Power/output lifecycle failures are operational and isolated; an OS power event
            // must not become an unhandled UI exception under the fatal WinForms policy.
            _log.Error($"Windows power event '{e.Mode}' handling failed", ex);
        }
    }

    private void OpenDashboardEditor()
    {
        try
        {
            var editorPath = Path.Combine(AppContext.BaseDirectory, "PinkieSysMon.Editor.exe");
            if (!File.Exists(editorPath))
                throw new FileNotFoundException("Dashboard editor executable was not found.", editorPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = editorPath,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("--root");
            startInfo.ArgumentList.Add(_baseDir);
            startInfo.ArgumentList.Add("--dashboard");
            startInfo.ArgumentList.Add(_config.Dashboard.Active);
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _log.Error("Could not launch dashboard editor", ex);
            _notifications.Show($"Editor launch failed: {ex.Message}", RuntimeNotificationKind.Error);
        }
    }


    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open folder '{path}'", ex);
            _notifications.Show($"Could not open folder: {ex.Message}", RuntimeNotificationKind.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        if (_exiting)
            return;

        _exiting = true;
        _log.Info("PinkieSysMon shutting down.");

        _statusTimer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _ipcServer.Dispose();
        _outputSessions.Dispose();
        _telemetry.Dispose();
        foreach (var source in _sources.OfType<IDisposable>())
            source.Dispose();

        _notifications.Dispose();
        _uiInvoker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _applicationIcon?.Dispose();
        _statusTimer.Dispose();

        base.ExitThreadCore();
    }
}
