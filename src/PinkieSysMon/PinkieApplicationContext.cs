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
    private readonly RuntimeTrayMenu _trayMenu;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly Control _uiInvoker = new();
    private readonly RuntimeIpcServer _ipcServer;
    private readonly RuntimeOutputControl _outputControl;
    private readonly RuntimeConfigurationReloadControl _configurationReload;
    private readonly WindowsMediaTelemetrySource _mediaTelemetry;
    private readonly WindowsAppNotificationService _notifications;
    private readonly RuntimeCommandFeedback _commandFeedback;

    private AppConfig _config;
    private TelemetryConfig _telemetryConfig;
    private IReadOnlyDictionary<string, CanonicalDashboardDefinition> _boards;
    private TelemetryEngine _telemetry;
    private OutputSessionManager _outputSessions;
    private IReadOnlyList<PhysicalDeviceInventoryEntry> _physicalInventory = Array.Empty<PhysicalDeviceInventoryEntry>();
    private HashSet<string> _lastRequiredMetrics = new(StringComparer.OrdinalIgnoreCase);
    private bool _exiting;

    private string AppConfigPath => Path.Combine(_baseDir, "config", "app.json");
    private string TelemetryConfigPath => Path.Combine(_baseDir, "config", "telemetry.json");

    public PinkieApplicationContext(string baseDir, FileLogger log)
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
        _outputSessions = new OutputSessionManager(_config, _boards, _metrics, _log);
        _configurationReload = new RuntimeConfigurationReloadControl(
            _outputSessions, _telemetry, _metrics, _sources, _baseDir, _log,
            () => new RuntimeConfigurationSnapshot(_config, _telemetryConfig, _boards),
            _mediaTelemetry.ApplyEndpointTypeOverrides, CommitRuntimeConfiguration);
        var configurationControl = new RuntimeOutputConfigurationControl(
            _outputSessions, () => _config, _baseDir, _log, CommitTargetRuntimeConfiguration);
        _outputControl = new RuntimeOutputControl(_outputSessions, LegacyDashboardStatus,
            configurationControl.Execute, () => _configurationReload.Reload());
        _notifications = new WindowsAppNotificationService(log);
        _commandFeedback = new RuntimeCommandFeedback(
            (message, kind) => _notifications.Show(message, kind),
            ex => _log.Warn("Runtime command notification failed; command result retained.", ex),
            ex => _log.Error("Runtime user command or result verification failed.", ex));

        _trayMenu = new RuntimeTrayMenu(OpenDashboardEditor, ExecuteTrayOutputCommand,
            () => OpenFolder(Path.Combine(_baseDir, "logs")), ChangePhysicalDeviceEnabled);
        BuildTray();
        _uiInvoker.CreateControl();
        _ipcServer = new RuntimeIpcServer(HandleIpcCommandAsync, _log);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _statusTimer.Tick += (_, _) => RefreshTrayStatus();
        _statusTimer.Start();

        _telemetry.Start();
        _outputSessions.Start();
        ReconcileTelemetryDemand();
        _ipcServer.Start();
        RefreshPhysicalInventory();

        _log.Info(
            $"PinkieSysMon started. Direct Skia renderer; no WebView2. " +
            $"assignedBoards={string.Join(",", _config.Outputs.Targets.Select(t => $"{t.Id}:{t.Dashboard}"))}; " +
            $"providers={string.Join(",", MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}; " +
            $"configEnabledMetrics={_telemetryConfig.Metrics.Count(x => x.Value.Enabled)}; " +
            $"dashboardMetrics={_lastRequiredMetrics.Count}; outputTargets={_config.Outputs.Targets.Count}.");
    }

    // Captured on startup, successful reload and root-menu opening, not on the
    // one-second tray timer. Never opens WinUSB or authorizes Device Enabled.
    // An optional discovery failure must not interrupt a working output.
    private bool RefreshPhysicalInventory()
    {
        try
        {
            var snapshot = PhysicalDeviceInventory.CaptureCurrent(_config.Outputs.Targets);
            _physicalInventory = snapshot;
            _log.Info(
                $"Passive physical discovery: " +
                $"knownConnected={snapshot.Count(e => e.IsPersisted && e.Status == PhysicalDeviceInventoryStatus.Connected)}; " +
                $"knownDisconnected={snapshot.Count(e => e.IsPersisted && e.Status == PhysicalDeviceInventoryStatus.Disconnected)}; " +
                $"newCandidates={snapshot.Count(e => e.Status == PhysicalDeviceInventoryStatus.Unconfigured)}; " +
                $"ambiguous={snapshot.Count(e => e.Status == PhysicalDeviceInventoryStatus.Ambiguous)}; " +
                $"serialFingerprints={snapshot.Count(e => e.DiscoveredDevice?.PhysicalFingerprint is not null)}.");
            return true;
        }
        catch (Exception ex)
        {
            // Discovery must not block Runtime or take down active device sessions.
            _log.Warn("Passive physical discovery failed; previous snapshot retained.", ex);
            return false;
        }
    }

    private void ChangePhysicalDeviceEnabled(string targetId, bool enable)
    {
        try
        {
            if (enable)
            {
                // Re-evaluate physical identity immediately before requesting consent.
                if (!RefreshPhysicalInventory())
                    throw new InvalidOperationException("Device discovery is unavailable.");

                var entry = _physicalInventory.SingleOrDefault(e =>
                    e.ConfiguredTarget?.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) == true);
                if (!PhysicalDeviceInventory.CanOfferEnable(entry))
                    throw new InvalidOperationException(
                        "The configured device is absent or its physical identity is ambiguous.");

                var consent = MessageBox.Show(
                    $"Enable PinkieSysMon USB output for '{entry!.ConfiguredTarget!.Name}'?\n\n" +
                    "PinkieSysMon will open the physical USB device and send display frames. " +
                    "Running vendor software or another USB-display application at the same time " +
                    "may cause device-access conflicts.\n\n" +
                    "Continue?",
                    "Enable physical device",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (consent != DialogResult.Yes)
                    return;
            }

            // Start from the disk definition so unrelated local settings are retained.
            var config = AppConfig.Load(AppConfigPath, _log);
            var target = config.Outputs.Targets.SingleOrDefault(t =>
                t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) &&
                t.Kind.Equals(OutputTargetContract.TrofeoKind, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                throw new InvalidOperationException("The physical output is no longer configured.");
            if (target.DeviceEnabled == enable)
                return;

            if (enable)
            {
                // Consent does not reserve a USB interface; verify again afterward.
                if (!RefreshPhysicalInventory())
                    throw new InvalidOperationException("Device discovery is unavailable.");
                var entry = _physicalInventory.SingleOrDefault(e =>
                    e.ConfiguredTarget?.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) == true);
                if (!PhysicalDeviceInventory.CanOfferEnable(entry) ||
                    !target.DeviceId.Equals(entry!.DiscoveredDevice!.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Device identity changed. No USB ownership was taken.");
            }

            // Consent remains above this path. This internal feedback kind has no IPC verb.
            _commandFeedback.Execute(new(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, targetId, Value: enable), () =>
            {
                target.DeviceEnabled = enable;
                SaveAndReloadPhysicalOutputChange(config);
                return new(true, $"Device Enabled = {enable}.");
            }, _outputControl.GetStatus);
        }
        catch (Exception ex)
        {
            _log.Error("Could not change physical device enabled state.", ex);
            _notifications.Show($"Device setting failed for Output '{targetId}': {ex.Message}", RuntimeNotificationKind.Error);
        }
    }

    private void SaveAndReloadPhysicalOutputChange(AppConfig config)
    {
        // This established path reloads all persisted settings, so older completions
        // cannot retain ownership of potentially replaced sibling sessions/bindings.
        _commandFeedback.Clear();
        // Resolve all remaining dashboards before replacing the private configuration.
        _ = RuntimeConfigurationValidator.LoadAssignments(config, _baseDir);
        var previousJson = File.ReadAllText(AppConfigPath);
        config.Save(AppConfigPath);
        var response = ReloadConfigurationCore();
        if (response.Success)
            return;

        // The session manager restores the previous live sessions on failure;
        // restore the exact original local configuration and retry that live state.
        var restorePath = AppConfigPath + ".rollback.tmp";
        File.WriteAllText(restorePath, previousJson);
        File.Move(restorePath, AppConfigPath, true);
        var recovered = ReloadConfigurationCore();
        if (!recovered.Success)
            throw new InvalidOperationException(
                $"The physical Output change failed and the previous configuration was restored on disk, " +
                $"but restoring the live runtime also failed: {recovered.Message}");
        throw new InvalidOperationException(
            $"The physical Output change could not be applied; the previous configuration and runtime were restored: {response.Message}");
    }

    private void BuildTray()
    {
        _trayMenu.Menu.Opening += (_, e) => e.Cancel = _exiting || !RefreshTrayMenu();
        _trayIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _trayIcon.Text = "PinkieSysMon";
        _trayIcon.ContextMenuStrip = _trayMenu.Menu;
        _trayIcon.Visible = true;
    }

    private (AppConfig App, TelemetryConfig Telemetry,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> Boards) LoadInitial()
    {
        var app = AppConfig.Load(AppConfigPath, _log);
        var telemetry = TelemetryConfig.Load(TelemetryConfigPath, _sources, _log);
        return (app, telemetry, RuntimeConfigurationValidator.LoadEnabledAssignments(app, _baseDir));
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

    // Compatibility summary only; RuntimeOutputStatus.Dashboard owns each assignment.
    private string LegacyDashboardStatus() =>
        _config.Outputs.Targets.Count == 1
            ? _config.Outputs.Targets[0].Dashboard ?? string.Empty
            : "(per-output dashboards)";

    private RuntimeIpcResponse ReloadConfigurationCore()
    {
        try
        {
            var result = _configurationReload.Reload(requireRunning: false);
            RefreshPhysicalInventory();
            return result;
        }
        catch (Exception ex)
        {
            _log.Error("Configuration reload failed; inspect the reported apply/rollback outcome", ex);
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

        return InvokeOnUiAsync(() =>
        {
            switch (parsed.Kind)
            {
                case RuntimeIpcCommandKind.ReloadConfiguration:
                case RuntimeIpcCommandKind.Exit:
                case RuntimeIpcCommandKind.StartOrReloadTarget:
                case RuntimeIpcCommandKind.StartOrReloadAll:
                case RuntimeIpcCommandKind.StopAll:
                case RuntimeIpcCommandKind.AssignDashboard:
                case RuntimeIpcCommandKind.SetDesktopBoardEnabled:
                case RuntimeIpcCommandKind.SetDesktopLock:
                {
                    var response = ExecuteOutputCommandOnUi(parsed);
                    if (RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(response))
                        ScheduleExitAfterIpcResponse();
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

    private void CommitRuntimeConfiguration(RuntimeConfigurationSnapshot snapshot)
    {
        _config = snapshot.App;
        _telemetryConfig = snapshot.Telemetry;
        _boards = snapshot.Boards;
        _lastRequiredMetrics = _outputSessions.GetRequiredMetrics().ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void CommitTargetRuntimeConfiguration(AppConfig config)
    {
        // Also used for rollback: reapply the schedule even if the cached demand
        // still matches the old set after a partial telemetry commit failure.
        var required = _outputSessions.GetRequiredMetrics();
        _telemetry.Reconfigure(_telemetryConfig,
            MetricProviderContract.GetEnabledProviderIds(config.MetricProviders), required);
        _lastRequiredMetrics = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _config = config;
        _boards = _outputSessions.GetDashboardAssignments();
    }

    private RuntimeIpcResponse ExecuteOutputCommandOnUi(RuntimeIpcCommand command)
    {
        var response = _commandFeedback.Execute(command, () =>
        {
            var applied = command.Kind switch
            {
                RuntimeIpcCommandKind.ReloadConfiguration => ReloadConfigurationCore(),
                RuntimeIpcCommandKind.Exit => new RuntimeIpcResponse(true, "Shutdown requested."),
                _ => _outputControl.Execute(command)
            };
            if (command.Kind == RuntimeIpcCommandKind.Exit)
                return applied;
            if (applied.Success)
            {
                if (command.Kind == RuntimeIpcCommandKind.StartOrReloadAll)
                    RefreshPhysicalInventory();
                _boards = _outputSessions.GetDashboardAssignments();
                ReconcileTelemetryDemand();
            }
            RefreshTrayStatus();
            return applied;
        }, _outputControl.GetStatus);
        return response.Success && (command.Kind == RuntimeIpcCommandKind.Exit || _outputControl.ShouldExitRuntimeAfter(command))
            ? response with { Message = $"{response.Message} {RuntimeIpcProtocol.RuntimeStoppingMessage}" }
            : response;
    }

    private void ExecuteTrayOutputCommand(RuntimeIpcCommand command)
    {
        try
        {
            var response = ExecuteOutputCommandOnUi(command);
            if (RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(response))
                ExitThread();
        }
        catch (Exception ex)
        {
            _log.Error("Runtime tray Output command failed.", ex);
            _notifications.Show($"Output command failed: {ex.Message}", RuntimeNotificationKind.Error);
        }
    }

    private bool RefreshTrayMenu()
    {
        try
        {
            var discoveryAvailable = RefreshPhysicalInventory();
            var enableable = _physicalInventory
                .Where(entry => discoveryAvailable && PhysicalDeviceInventory.CanOfferEnable(entry))
                .Select(entry => entry.ConfiguredTarget!.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> dashboards;
            try { dashboards = DashboardCatalog.Discover(_baseDir); }
            catch (Exception ex)
            {
                _log.Warn("Tray dashboard discovery failed; assignment choices unavailable.", ex);
                dashboards = [];
            }
            var quality = _config.Outputs.Targets
                .Where(target => target.Kind == OutputTargetContract.TrofeoKind)
                .ToDictionary(target => target.Id,
                    target => target.JpegQuality ?? _config.Renderer.JpegQuality,
                    StringComparer.OrdinalIgnoreCase);
            _trayMenu.RefreshOutputs(_outputSessions.GetStatuses(), dashboards, enableable, quality);
            RefreshTrayStatus();
            return true;
        }
        catch (Exception ex)
        {
            // A failed projection must not expose stale interactive Output entries.
            _log.Warn("Runtime tray snapshot could not be refreshed; menu opening canceled.", ex);
            return false;
        }
    }

    private void RefreshTrayStatus()
    {
        var status = _outputControl.GetStatus();
        _commandFeedback.Observe(status);
        _trayMenu.Status.Text = RuntimeTrayOutputContract.Summary(status);
        var tooltip = RuntimeTrayOutputContract.Tooltip(status);
        _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
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
        _commandFeedback.Clear();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _ipcServer.Dispose();
        _outputSessions.Dispose();
        _telemetry.Dispose();
        foreach (var source in _sources.OfType<IDisposable>())
            source.Dispose();

        _notifications.Dispose();
        _uiInvoker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip = null;
        _trayMenu.Dispose();
        _trayIcon.Dispose();
        _applicationIcon?.Dispose();
        _statusTimer.Dispose();

        base.ExitThreadCore();
    }
}
