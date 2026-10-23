using System.Drawing;
using System.Diagnostics;
using Microsoft.Win32;
using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

internal sealed class PinkieApplicationContext : ApplicationContext
{
    private readonly string _baseDir;
    private readonly FileLogger _log;
    private readonly MetricStore _metrics = new(new MetricHistoryStore());
    private long _historyConfigurationEpoch;
    private readonly IMetricSource[] _sources;

    private readonly NotifyIcon _trayIcon = new();
    private readonly Icon? _applicationIcon;
    private readonly RuntimeTrayMenu _trayMenu;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private DesktopShellZOrderWatcher? _desktopShellWatcher;
    private readonly RuntimeIdleMonitor _idleMonitor = new();
    private readonly Control _uiInvoker = new();
    private readonly RuntimeIpcServer _ipcServer;
    private readonly RuntimeOutputControl _outputControl;
    private readonly RuntimeOutputConfigurationControl _outputConfiguration;
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
    private readonly RuntimeUsbTestService _usbTests;
    private bool _exitAfterTests;
    private bool _exiting;
    private bool _idlePaused;

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
        _outputConfiguration = new RuntimeOutputConfigurationControl(
            _outputSessions, () => _config, _baseDir, _log, CommitTargetRuntimeConfiguration);
        _outputControl = new RuntimeOutputControl(_outputSessions, LegacyDashboardStatus,
            _outputConfiguration.Execute, () => _configurationReload.Reload());
        _notifications = new WindowsAppNotificationService(log);
        _commandFeedback = new RuntimeCommandFeedback(
            (message, kind) => _notifications.Show(message, kind),
            ex => _log.Warn("Runtime command notification failed; command result retained.", ex),
            ex => _log.Error("Runtime user command or feedback reporting failed.", ex),
            (message, kind) =>
            {
                if (kind == RuntimeNotificationKind.Error)
                    _log.Warn($"Runtime command feedback: {message}");
                else
                    _log.Info($"Runtime command feedback: {message}");
            });

        _trayMenu = new RuntimeTrayMenu(OpenDashboardEditor, ExecuteTrayOutputCommand,
            () => OpenFolder(Path.Combine(_baseDir, "logs")), ChangePhysicalDeviceEnabled);
        BuildTray();
        _uiInvoker.CreateControl();
        _usbTests = new RuntimeUsbTestService(CatalogUsbInterfaceDiscovery.CaptureSnapshot,
            (intent, snapshot) => RuntimeUsbTestService.IsOwned(intent, snapshot,
                _config.Outputs.Targets.Where(target => target.DeviceEnabled ||
                    _outputSessions.GetStatuses().Any(status => status.TargetId == target.Id && status.IsActive))),
            intent => RuntimeUsbTestService.CreatePump(_baseDir, _log, intent), _log,
            () => _config.Outputs.IgnoredDeviceNodeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        _usbTests.TestFailed += () => _ = _uiInvoker.InvokeAsync(() => _notifications.Show(
            "USB Test not passed. Configuration was not saved. Check the Runtime log and retry after cleanup.", RuntimeNotificationKind.Error));
        _ipcServer = new RuntimeIpcServer(HandleIpcCommandAsync, _log);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _statusTimer.Tick += (_, _) =>
        {
            _usbTests.Tick();
            if (_exitAfterTests && !_usbTests.HasActive) { ExitThread(); return; }
            EvaluateRuntimeIdle();
            RefreshTrayStatus();
        };
        _statusTimer.Start();

        _telemetry.InitialValuesPublished += OnInitialTelemetryPublished;
        _telemetry.Start();
        _outputSessions.Start();
        _desktopShellWatcher = new DesktopShellZOrderWatcher(
            _outputSessions.RestoreDesktopAfterShellChange, _log);
        ReconcileTelemetryDemand();
        EvaluateRuntimeIdle();
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
                $"discoveryUnavailable={snapshot.Count(e => e.Status == PhysicalDeviceInventoryStatus.DiscoveryUnavailable)}; " +
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
            if (_exiting) return;
            if (_usbTests.HasActive) throw new InvalidOperationException("Finish or cancel USB Test before changing physical enablement.");
            var apply = _outputConfiguration.PreparePhysicalEnablement(targetId, enable, target =>
            {
                var consent = MessageBox.Show(
                    $"Enable PinkieSysMon USB output for '{target.Name}'?\n\n" +
                    "PinkieSysMon will open the physical USB device and send display frames. " +
                    "Running vendor software or another USB-display application at the same time " +
                    "may cause device-access conflicts.\n\n" +
                    "Continue?",
                    "Enable physical device",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                return consent == DialogResult.Yes;
            }, () => PhysicalDeviceDiscovery.CaptureTransport(
                _config.Outputs.Targets.Single(target => target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)).Transport));
            if (apply is null || _exiting || _exitAfterTests) return;
            // Native consent runs a nested message loop. Another Editor may
            // have admitted a Test while this prompt was open.
            if (_usbTests.HasActive) throw new InvalidOperationException("USB Test started during consent. Finish it before retrying enablement.");

            // Consent remains above this path. This internal feedback kind has no IPC verb.
            _commandFeedback.Execute(new(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, targetId, Value: enable),
                apply, _outputControl.GetStatus);
            RefreshTrayStatus();
        }
        catch (Exception ex)
        {
            _log.Error("Could not change physical device enabled state.", ex);
            _notifications.Show("Device setting failed; check Runtime log.", RuntimeNotificationKind.Error);
        }
    }

    private void BuildTray()
    {
        _trayMenu.Menu.Opening += (_, e) => e.Cancel = _exiting || !RefreshTrayMenu();
        _trayIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _trayIcon.Text = "PinkieSysMon";
        _trayIcon.ContextMenuStrip = _trayMenu.Menu;
        _trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (!_exiting && e.Button == MouseButtons.Left)
                _trayMenu.ControlCenter.PerformClick();
        };
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
        ReconcileHistoryDemand();
        if (_lastRequiredMetrics.SetEquals(required))
            return;

        _telemetry.Reconfigure(_telemetryConfig,
            MetricProviderContract.GetEnabledProviderIds(_config.MetricProviders),
            required);
        _lastRequiredMetrics = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // First values arrive asynchronously after the panel's initial paint. Schedule one
    // additional frame on the UI thread, without changing either polling or redraw cadence.
    private void OnInitialTelemetryPublished()
    {
        if (Volatile.Read(ref _exiting) || _uiInvoker.IsDisposed)
            return;
        try
        {
            _uiInvoker.BeginInvoke((Action)(() =>
            {
                if (_exiting)
                    return;
                try { _outputSessions.RequestFramesAfterInitialTelemetry(); }
                catch (Exception ex)
                {
                    _log.Warn("Initial telemetry frame request failed.", ex);
                }
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            if (!Volatile.Read(ref _exiting))
                _log.Warn("Initial telemetry frame could not be dispatched.", ex);
        }
    }

    // The existing one-second tray timer owns idle detection; there is no input hook,
    // additional worker or active polling while paused. OS sleep is a separate cause.
    private void EvaluateRuntimeIdle()
    {
        if (_exiting)
            return;
        bool shouldPause;
        try
        {
            shouldPause = _idleMonitor.ShouldPause(_config.Runtime.IdleTimeoutMinutes);
        }
        catch (Exception ex)
        {
            _log.WarnThrottled("runtime.idle.monitor", TimeSpan.FromSeconds(30),
                "Could not read Windows idle/power policy; leaving the idle gate open.", ex);
            shouldPause = false;
        }
        if (_idlePaused == shouldPause)
            return;

        try
        {
            if (shouldPause)
            {
                _telemetry.SetIdlePaused(true);
                _outputSessions.SetIdlePaused(true);
                ReconcileTelemetryDemand();
            }
            else
            {
                _outputSessions.SetIdlePaused(false);
                ReconcileTelemetryDemand();
                _telemetry.SetIdlePaused(false);
            }
            _idlePaused = shouldPause;
            _log.Info(shouldPause ? "Runtime idle: telemetry and Output frames paused."
                : "Runtime idle: fresh telemetry and Output frames resumed.");
        }
        catch (Exception ex)
        {
            _log.ErrorThrottled("runtime.idle.transition", TimeSpan.FromSeconds(5),
                "Runtime idle transition failed; a later evaluation will retry.", ex);
        }
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
            _idleMonitor.InvalidatePowerSetting();
            EvaluateRuntimeIdle();
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

        if (parsed.Kind is RuntimeIpcCommandKind.UsbTestBegin or
            RuntimeIpcCommandKind.UsbTestStatus or RuntimeIpcCommandKind.UsbTestCancel)
            return InvokeOnUiAsync(() => _exitAfterTests && parsed.Kind == RuntimeIpcCommandKind.UsbTestBegin
                ? new RuntimeIpcResponse(false, "Runtime is stopping after USB Test cleanup.")
                : _usbTests.Execute(parsed), cancellationToken);

        return InvokeOnUiAsync(() =>
        {
            switch (parsed.Kind)
            {
                case RuntimeIpcCommandKind.ReloadConfiguration:
                case RuntimeIpcCommandKind.Exit:
                case RuntimeIpcCommandKind.StartOrReloadTarget:
                case RuntimeIpcCommandKind.StartOrReloadAll:
                case RuntimeIpcCommandKind.StopAll:
                case RuntimeIpcCommandKind.PreparePhysicalRemoval:
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

    private void ReconcileHistoryDemand(bool newConfiguration = false)
    {
        if (newConfiguration) _historyConfigurationEpoch++;
        _telemetry.SetHistoryDemand(_outputSessions.GetRequiredHistoryDemands()
            .Select(d => d with { OwnerGeneration = _historyConfigurationEpoch }));
    }

    private void CommitRuntimeConfiguration(RuntimeConfigurationSnapshot snapshot)
    {
        ReconcileHistoryDemand(newConfiguration: true);
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
        ReconcileHistoryDemand(newConfiguration: true);
        _lastRequiredMetrics = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _config = config;
        _boards = _outputSessions.GetDashboardAssignments();
    }

    private RuntimeIpcResponse ExecuteOutputCommandOnUi(RuntimeIpcCommand command)
    {
        if ((_usbTests.HasActive || _exitAfterTests) && command.Kind is not (RuntimeIpcCommandKind.Exit or RuntimeIpcCommandKind.StopAll))
            return new(false, "Finish or cancel USB Test before applying Output configuration changes. Existing Outputs continue running.");
        var response = _commandFeedback.Execute(command, () =>
        {
            var applied = command.Kind switch
            {
                RuntimeIpcCommandKind.ReloadConfiguration => ReloadConfigurationCore(),
                RuntimeIpcCommandKind.PreparePhysicalRemoval => _outputConfiguration.PreparePhysicalRemoval(command),
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
                EvaluateRuntimeIdle();
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
            _notifications.Show("Output command failed; check Runtime log.", RuntimeNotificationKind.Error);
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
                .Where(target => OutputTargetContract.IsPhysicalKind(target.Kind) &&
                    OutputTargetContract.HasIntegratedTransport(target.Transport))
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

        // SystemEvents callbacks are not guaranteed to run on the WinForms thread.
        if (_uiInvoker.InvokeRequired)
        {
            try { _uiInvoker.BeginInvoke((Action)(() => OnPowerModeChanged(sender, e))); }
            catch (InvalidOperationException) when (_exiting) { }
            catch (ObjectDisposedException) when (_exiting) { }
            return;
        }

        try
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    _usbTests.CancelActive();
                    _log.Info("Windows power event: suspend.");
                    _outputSessions.Suspend();
                    ReconcileTelemetryDemand();
                    break;
                case PowerModes.Resume:
                    _log.Info("Windows power event: resume.");
                    _idleMonitor.InvalidatePowerSetting();
                    EvaluateRuntimeIdle();
                    _outputSessions.Resume();
                    ReconcileTelemetryDemand();
                    break;
                case PowerModes.StatusChange:
                    _idleMonitor.InvalidatePowerSetting();
                    EvaluateRuntimeIdle();
                    break;
            }
        }
        catch (ObjectDisposedException) when (_exiting)
        {
        }
        catch (Exception ex)
        {
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
            _notifications.Show("Editor could not be opened; check Runtime log.", RuntimeNotificationKind.Error);
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
            _notifications.Show("Folder could not be opened; check Runtime log.", RuntimeNotificationKind.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        if (_exiting)
            return;

        if (_usbTests.HasActive)
        {
            _exitAfterTests = true;
            _usbTests.CancelActive();
            _log.Info("Runtime shutdown awaits native USB Test release; IPC/UI remain available.");
            return;
        }
        _exiting = true;
        _log.Info("PinkieSysMon shutting down.");

        _statusTimer.Stop();
        _commandFeedback.Clear();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _ipcServer.Dispose();
        _desktopShellWatcher?.Dispose();
        _telemetry.InitialValuesPublished -= OnInitialTelemetryPublished;
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
