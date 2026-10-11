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
    private IReadOnlyDictionary<string, CanonicalDashboardDefinition> _boards;
    private TelemetryEngine _telemetry;
    private OutputSessionManager _outputSessions;
    private HashSet<string> _lastRequiredMetrics = new(StringComparer.OrdinalIgnoreCase);
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

        (_config, _telemetryConfig, _boards) = LoadInitial();
        MetricContract.ReplaceOutputDescriptors(_config.Outputs.Targets);
        _metrics.Publish(RuntimeMetricContract.Version,
            MetricProviderContract.IsEnabled(_config.MetricProviders, MetricProviderContract.System)
                ? RuntimeVersion.Current : null);
        _mediaTelemetry.SetEndpointTypeOverrides(_config.Media.EndpointTypeOverrides);
        // No session has been activated yet. A configured but inactive board
        // must never cause background polling or retain a stale valid value.
        var requiredMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _telemetry = new TelemetryEngine(
            _metrics,
            _sources,
            _telemetryConfig,
            MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders),
            requiredMetrics,
            _log);
        _outputSessions = new OutputSessionManager(_config, _boards, _metrics, _log,
            reloadBoard: target => RuntimeConfigurationValidator.LoadTarget(target, _baseDir));
        _outputControl = new RuntimeOutputControl(_outputSessions, LegacyDashboardStatus);
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
        ReconcileTelemetryDemand();
        _ipcServer.Start();

        _log.Info(
            $"PinkieSysMon started. Direct Skia renderer; no WebView2. " +
            $"assignedBoards={string.Join(",", _config.Outputs.Targets.Select(t => $"{t.Id}:{t.Dashboard}"))}; " +
            $"providers={string.Join(",", MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}; " +
            $"configEnabledMetrics={_telemetryConfig.Metrics.Count(x => x.Value.Enabled)}; " +
            $"dashboardMetrics={_lastRequiredMetrics.Count}; outputTargets={_config.Outputs.Targets.Count}; " +
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

        // Dashboard selection belongs to each Output, never a global tray switch.
        _dashboardsMenu.Text = "Edit Output Dashboard";
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

    private (AppConfig App, TelemetryConfig Telemetry,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> Boards) LoadInitial()
    {
        var app = AppConfig.Load(AppConfigPath, _log);
        var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);
        return (app, telemetry, RuntimeConfigurationValidator.LoadAssignments(app, _baseDir));
    }

    // OutputSessionManager supplies an atomic active-session/board snapshot.
    // Inactive physical hardware and invisible desktop outputs never demand metrics.
    private void ReconcileTelemetryDemand()
    {
        var required = _outputSessions.GetRequiredMetrics();
        if (_lastRequiredMetrics.SetEquals(required))
            return;

        _telemetry.Reconfigure(_telemetryConfig,
            MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders),
            required);
        _lastRequiredMetrics = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Kept only for the old IPC field until Stage 24 adds target-scoped status.
    private string LegacyDashboardStatus() =>
        _config.Outputs.Targets.Count == 1
            ? _config.Outputs.Targets[0].Dashboard ?? string.Empty
            : "(per-output dashboards)";

    private RuntimeIpcResponse ReloadConfiguration(bool showNotification = true)
    {
        try
        {
            var app = AppConfig.Load(AppConfigPath, _log);
            var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);
            var boards = RuntimeConfigurationValidator.LoadAssignments(app, _baseDir);
            ApplyConfiguration(app, telemetry, boards);
            _log.Info($"Configuration and independent dashboards for {app.Outputs.Targets.Count} outputs reloaded.");
            if (showNotification)
                _notifications.Show("Configuration reloaded.");
            return new RuntimeIpcResponse(true, $"Reloaded {app.Outputs.Targets.Count} output dashboards.");
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
            return Task.FromResult(new RuntimeIpcResponse(true, LegacyDashboardStatus()));

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
                    if (response.Success)
                    {
                        // Keep Runtime's rollback snapshot aligned with target-scoped
                        // dashboard reloads, not just full configuration reloads.
                        _boards = _outputSessions.GetDashboardAssignments();
                        ReconcileTelemetryDemand();
                    }
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

    private void ApplyConfiguration(AppConfig app, TelemetryConfig telemetry,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards)
    {
        // Phase 1: prepare every operation that can fail before the live runtime is mutated.
        // OutputSessionManager performs its own renderer/session preparation internally.
        var mediaOverrides =
            WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(app.Media.EndpointTypeOverrides);

        var previousApp = _config;
        var previousTelemetry = _telemetryConfig;
        var previousBoards = _boards;
        var previousMediaOverrides =
            WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(previousApp.Media.EndpointTypeOverrides);
        var previousRequiredMetrics = _outputSessions.GetRequiredMetrics();

        var outputCommitted = false;
        var telemetryCommitted = false;
        var mediaCommitted = false;

        try
        {
            // Phase 2: commit. Every stage has a prepared/validated rollback path. The output
            // manager is itself transactional across sibling sessions.
            _outputSessions.Reconfigure(app, boards);
            outputCommitted = true;

            // Live session creation/removal has committed; derive real demand,
            // not merely configured/DeviceEnabled targets. Preparation still
            // precedes the telemetry scheduler's own atomic commit.
            var requiredMetrics = _outputSessions.GetRequiredMetrics();
            var telemetryPlan = _telemetry.PrepareReconfiguration(
                telemetry,
                MetricProviderContract.GetEnabledProviderIds(app.MetricProviders),
                requiredMetrics);
            _telemetry.CommitReconfiguration(telemetryPlan);
            telemetryCommitted = true;

            _mediaTelemetry.ApplyEndpointTypeOverrides(mediaOverrides);
            mediaCommitted = true;

            MetricContract.ReplaceOutputDescriptors(app.Outputs.Targets);
            _config = app;
            _metrics.Publish(RuntimeMetricContract.Version,
                MetricProviderContract.IsEnabled(app.MetricProviders, MetricProviderContract.System)
                    ? RuntimeVersion.Current : null);
            _telemetryConfig = telemetry;
            _boards = boards;
            _lastRequiredMetrics = requiredMetrics.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                    _outputSessions.Reconfigure(previousApp, previousBoards);
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
        foreach (var target in _config.Outputs.Targets)
        {
            var board = target.Dashboard ?? string.Empty;
            var item = new ToolStripMenuItem($"{target.Name} — {board}");
            item.Click += (_, _) => OpenDashboardEditor(board);
            _dashboardsMenu.DropDownItems.Add(item);
        }
    }

    private void RefreshTrayStatus()
    {
        var statuses = _outputSessions.GetStatuses();
        if (statuses.Count == 1)
        {
            var status = statuses[0];
            var board = _config.Outputs.Targets[0].Dashboard ?? "(unassigned)";
            var isDesktop = _config.Outputs.Targets[0].Kind == OutputTargetContract.DesktopKind;
            _statusItem.Text = status.IsConnected
                ? isDesktop
                    ? $"{status.TargetName} displaying · {board} · frame {status.SentFrames} · {status.LastFrameMs:F1} ms"
                    : $"{status.TargetName} connected · {board} · frame {status.SentFrames} · {status.LastFrameMs:F1} ms · {status.LastJpegBytes / 1024.0:F0} KiB"
                : $"{status.TargetName} {status.UsbState.ToLowerInvariant()} · {board}";
            var tooltip = status.IsConnected
                ? $"PinkieSysMon - {board}"
                : "PinkieSysMon - output waiting";
            _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
            return;
        }

        var active = statuses.Count(status => status.IsActive);
        var connected = statuses.Count(status => status.IsConnected);
        _statusItem.Text = $"Outputs {connected}/{active} connected · {statuses.Count} configured";
        var multiTooltip = $"PinkieSysMon - outputs {connected}/{active}";
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
                    ReconcileTelemetryDemand();
                    break;
                case PowerModes.Resume:
                    _log.Info("Windows power event: resume.");
                    _outputSessions.Resume();
                    ReconcileTelemetryDemand();
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

    private void OpenDashboardEditor(string dashboardName)
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
            startInfo.ArgumentList.Add(dashboardName);
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
