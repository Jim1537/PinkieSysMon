using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

internal sealed class OutputSessionManager : IDisposable
{
    private readonly object _sync = new();
    private readonly MetricStore _metrics;
    private readonly FileLogger _log;
    private readonly OutputSessionFactory _factory;
    private readonly Dictionary<string, IOutputSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private AppConfig _config;
    private IReadOnlyDictionary<string, CanonicalDashboardDefinition> _boards;
    private bool _started;
    private bool _suspended;
    private bool _idlePaused;
    private bool _disposed;

    // Legacy regression-fixture constructor. Production uses per-Output definitions.
    public OutputSessionManager(AppConfig config, CanonicalDashboardDefinition dashboard,
        MetricStore metrics, FileLogger log, OutputSessionFactory? factory = null,
        Func<OutputTargetConfig, int, bool, FileLogger, CancellationToken, IPhysicalFrameTransport?>? physicalTransportFactory = null)
        : this(config, config.Outputs.Targets.ToDictionary(t => t.Id, _ => dashboard,
              StringComparer.OrdinalIgnoreCase), metrics, log, factory, physicalTransportFactory) { }

    public OutputSessionManager(AppConfig config,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards,
        MetricStore metrics, FileLogger log, OutputSessionFactory? factory = null,
        Func<OutputTargetConfig, int, bool, FileLogger, CancellationToken, IPhysicalFrameTransport?>? physicalTransportFactory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _boards = ValidateBoards(config, boards);
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _factory = factory ?? ((app, target, board, store, logger) =>
            target.Kind switch
            {
                OutputTargetContract.DesktopKind => new DesktopOutputSession(app, target, board, store, logger),
                OutputTargetContract.TrofeoKind when target.Transport == OutputTargetContract.TrofeoTransport =>
                    new FramePump(app, target, board, store, logger, physicalTransportFactory),
                OutputTargetContract.TurzxKind when target.Transport == OutputTargetContract.TurzxTransport =>
                    new FramePump(app, target, board, store, logger, physicalTransportFactory),
                _ => throw new InvalidOperationException("The selected Output adapter is not integrated.")
            });
    }

    private static IReadOnlyDictionary<string, CanonicalDashboardDefinition> ValidateBoards(
        AppConfig config, IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards)
    {
        ArgumentNullException.ThrowIfNull(boards);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, CanonicalDashboardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in config.Outputs.Targets)
        {
            if (!ids.Add(target.Id))
                throw new InvalidDataException($"Duplicate output ID '{target.Id}'.");
            if (!boards.TryGetValue(target.Id, out var board) || board is null)
            {
                if (OutputTargetContract.IsEnabled(target))
                    throw new InvalidDataException($"Output '{target.Id}' has no loaded dashboard '{target.Dashboard}'.");
                continue;
            }
            result.Add(target.Id, board);
        }
        return result;
    }

    private static CanonicalDashboardDefinition BoardFor(
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> boards, OutputTargetConfig target) =>
        boards.TryGetValue(target.Id, out var board) ? board :
        throw new InvalidDataException($"Output '{target.Id}' has no loaded dashboard '{target.Dashboard}'.");

    public string GetDashboardName(string targetId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return FindConfiguredTargetLocked(targetId).Dashboard
                ?? throw new InvalidDataException($"Output '{targetId}' has no board assignment.");
        }
    }

    // A targeted reload can change an in-memory document without changing app.json.
    // Expose a coherent snapshot for the composition root's rollback ownership.
    public IReadOnlyDictionary<string, CanonicalDashboardDefinition> GetDashboardAssignments()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _boards.ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    // One atomic snapshot of the active session set and its loaded documents.
    // Stopped and not-yet-created outputs never demand telemetry.
    public IReadOnlySet<string> GetRequiredMetrics()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _suspended || _idlePaused
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : OutputTelemetryDemand.Collect(_config, _boards, _sessions.Keys);
        }
    }

    public void Start()
    {
        List<IOutputSession> created = [];
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_started)
                    return;

                foreach (var target in _config.Outputs.Targets.Where(OutputTargetContract.IsEnabled))
                    created.Add(CreateSession(_config, target, BoardFor(_boards, target)));

                // Do not publish a partially started output set. A session that cannot enter
                // its worker lifecycle must leave the manager exactly as it was before Start.
                if (_suspended)
                {
                    foreach (var session in created)
                        session.Suspend();
                }
                if (_idlePaused)
                    foreach (var session in created)
                        session.SetIdlePaused(true);
                StartSessions(created);

                _started = true;
                foreach (var session in created)
                    _sessions.Add(session.TargetId, session);

                UpdateRuntimeMetricPublisherLocked();
                RestoreDesktopStackLocked();
            }
        }
        catch
        {
            DisposeSessionsBestEffort(created);
            throw;
        }
    }


    public void Reconfigure(AppConfig config, CanonicalDashboardDefinition dashboard) =>
        Reconfigure(config, config.Outputs.Targets.ToDictionary(
            t => t.Id, _ => dashboard, StringComparer.OrdinalIgnoreCase));

    public void Reconfigure(AppConfig config,
        IReadOnlyDictionary<string, CanonicalDashboardDefinition> dashboards, Action? validateInput = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var nextBoards = ValidateBoards(config, dashboards);

        List<(IOutputSession Session, IOutputSessionReconfiguration Reconfiguration)> prepared = [];
        List<(IOutputSession Session, IOutputSessionReconfiguration Reconfiguration)> rollback = [];
        List<IOutputSession> created = [];
        List<IOutputSession> removed = [];

        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var newTargets = config.Outputs.Targets.ToDictionary(
                    target => target.Id,
                    StringComparer.OrdinalIgnoreCase);
                var oldTargets = _config.Outputs.Targets.ToDictionary(
                    target => target.Id,
                    StringComparer.OrdinalIgnoreCase);

                // Phase 1: prepare replacements and retain each live renderer for rollback.
                // Former image files can disappear between saves; the already-loaded renderer
                // must remain available if a later commit fails.
                foreach (var pair in _sessions)
                {
                    if (newTargets.TryGetValue(pair.Key, out var replacementTarget) &&
                        OutputTargetContract.IsEnabled(replacementTarget))
                    {
                        prepared.Add((
                            pair.Value,
                            pair.Value.PrepareReconfiguration(config, replacementTarget, BoardFor(nextBoards, replacementTarget))));

                        if (!oldTargets.TryGetValue(pair.Key, out var oldTarget))
                            throw new InvalidOperationException(
                                $"Active output session '{pair.Key}' has no matching target in the current configuration.");

                        rollback.Add((
                            pair.Value,
                            pair.Value.PrepareRollbackReconfiguration(_config, oldTarget, BoardFor(_boards, oldTarget))));
                    }
                    else
                    {
                        removed.Add(pair.Value);
                    }
                }

                if (_started)
                {
                    foreach (var target in config.Outputs.Targets)
                    {
                        if (!OutputTargetContract.IsEnabled(target) || _sessions.ContainsKey(target.Id))
                            continue;

                        created.Add(CreateSession(config, target, BoardFor(nextBoards, target)));
                    }
                }

                // Newly introduced sessions must prove that their worker lifecycle can start
                // before any existing session is committed or removed.
                if (_suspended)
                {
                    foreach (var session in created)
                        session.Suspend();
                }
                validateInput?.Invoke();
                if (_idlePaused)
                    foreach (var session in created)
                        session.SetIdlePaused(true);
                StartSessions(created);

                // Phase 2: production CommitReconfiguration is assignment/cleanup-only and should
                // not fail for operational reasons. Still, if any implementation does throw,
                // restore every existing session from the already-prepared old-state plan.
                try
                {
                    foreach (var item in prepared)
                        item.Session.CommitReconfiguration(item.Reconfiguration);
                }
                catch (Exception commitEx)
                {
                    var rollbackFailures = new List<Exception>();
                    foreach (var item in rollback)
                    {
                        try
                        {
                            item.Session.CommitReconfiguration(item.Reconfiguration);
                        }
                        catch (Exception rollbackEx)
                        {
                            rollbackFailures.Add(rollbackEx);
                            _log.Error(
                                $"Output target '{item.Session.TargetName}' rollback failed after a reconfiguration commit error.",
                                rollbackEx);
                        }
                    }

                    if (rollbackFailures.Count > 0)
                    {
                        throw new AggregateException(
                            "Output reconfiguration commit failed and one or more rollback operations also failed.",
                            [commitEx, .. rollbackFailures]);
                    }

                    throw;
                }

                foreach (var session in removed)
                {
                    _sessions.Remove(session.TargetId);
                    session.SetRuntimeMetricsPublisher(false);
                    ClearOutputMetricsLocked(FindConfiguredTargetLocked(session.TargetId));
                }
                foreach (var oldTarget in _config.Outputs.Targets)
                    if (!config.Outputs.Targets.Any(target =>
                        target.Id.Equals(oldTarget.Id, StringComparison.OrdinalIgnoreCase)))
                        ClearOutputMetricsLocked(oldTarget);
                foreach (var session in created)
                    _sessions.Add(session.TargetId, session);

                _config = config;
                _boards = nextBoards;

                UpdateRuntimeMetricPublisherLocked();
            }

            DisposeSessionsBestEffort(removed);
            lock (_sync)
            {
                UpdateRuntimeMetricPublisherLocked();
                RestoreDesktopStackLocked();
            }
        }
        catch
        {
            // Created sessions are not inserted until every existing session has committed.
            DisposeSessionsBestEffort(created);
            throw;
        }
        finally
        {
            foreach (var item in prepared)
                item.Reconfiguration.Dispose();
            foreach (var item in rollback)
                item.Reconfiguration.Dispose();
        }
    }

    public void EnsureCanReloadTarget(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        lock (_sync)
        {
            ThrowIfDisposed();
            EnsureCanReloadTargetLocked(FindConfiguredTargetLocked(targetId));
        }
    }

    private void EnsureCanReloadTargetLocked(OutputTargetConfig target)
    {
        if (!_started)
            throw new InvalidOperationException("Runtime output lifetime is stopped. Start Runtime normally before reloading an individual Output.");
        if (!OutputTargetContract.IsEnabled(target))
            throw new InvalidOperationException($"Output '{target.Id}' is disabled. Enable it through its established settings first.");
    }

    // Saved reload and field changes share one selected-session transaction. Only
    // reload accepts saved provider policy, and it cannot change enablement/binding.
    // The dedicated physical path requires its consent/input validator before Start.
    public void ReloadTarget(OutputTargetConfig replacement, CanonicalDashboardDefinition board,
        Action<AppConfig> commit, Action? validateInput = null) =>
        ApplyTarget(replacement, board, commit, requireEnabledLifetime: true, validateInput: validateInput);

    public void ReconfigureTarget(OutputTargetConfig replacement, CanonicalDashboardDefinition? board,
        Action<AppConfig> commit) =>
        ApplyTarget(replacement, board, commit, requireEnabledLifetime: false, validateInput: null);

    public void ReconfigurePhysicalEnablement(OutputTargetConfig replacement, CanonicalDashboardDefinition? board,
        Action<AppConfig> commit, Action validateInput)
    {
        ArgumentNullException.ThrowIfNull(validateInput);
        ApplyTarget(replacement, board, commit, requireEnabledLifetime: false,
            validateInput: validateInput, allowPhysicalEnablement: true);
    }

    private void ApplyTarget(OutputTargetConfig replacement, CanonicalDashboardDefinition? board,
        Action<AppConfig> commit, bool requireEnabledLifetime, Action? validateInput, bool allowPhysicalEnablement = false)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (OutputTargetContract.IsEnabled(replacement) && board is null)
            throw new InvalidDataException($"Enabled Output '{replacement.Id}' has no loaded dashboard.");
        ArgumentNullException.ThrowIfNull(commit);
        IOutputSession? created = null;
        IOutputSession? removed = null;
        IOutputSessionReconfiguration? prepared = null;
        IOutputSessionReconfiguration? rollback = null;
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var previousTarget = FindConfiguredTargetLocked(replacement.Id);
                if (requireEnabledLifetime)
                {
                    EnsureCanReloadTargetLocked(previousTarget);
                    if (!OutputTargetContract.IsEnabled(replacement))
                        throw new InvalidOperationException("Output enablement changed on disk. Apply configuration before reloading this Output.");
                }
                if ((allowPhysicalEnablement && !OutputTargetContract.IsPhysicalKind(previousTarget.Kind)) ||
                    !OutputTargetContract.ConnectionEquals(previousTarget, replacement) ||
                    (!allowPhysicalEnablement && previousTarget.DeviceEnabled != replacement.DeviceEnabled) ||
                    (!requireEnabledLifetime && previousTarget.ProviderEnabled != replacement.ProviderEnabled))
                    throw new InvalidOperationException("Target settings cannot change device ownership; field commands cannot change Provider Enabled.");
                var previousConfig = _config;
                var previousBoards = _boards;
                _sessions.TryGetValue(previousTarget.Id, out var existing);
                var nextConfig = new AppConfig
                {
                    Display = _config.Display, Runtime = _config.Runtime,
                    Renderer = _config.Renderer, Usb = _config.Usb, Media = _config.Media,
                    MetricProviders = _config.MetricProviders,
                    Outputs = new OutputConfig { Targets = _config.Outputs.Targets
                        .Select(t => ReferenceEquals(t, previousTarget) ? replacement : t).ToList() }
                };
                var nextBoards = _boards.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                if (board is null) nextBoards.Remove(previousTarget.Id);
                else nextBoards[previousTarget.Id] = board;
                var commitAttempted = false;
                try
                {
                    if (existing is not null && OutputTargetContract.IsEnabled(replacement))
                    {
                        prepared = existing.PrepareReconfiguration(nextConfig, replacement, board!);
                        rollback = existing.PrepareRollbackReconfiguration(previousConfig, previousTarget,
                            BoardFor(previousBoards, previousTarget));
                    }
                    else if (existing is null && _started && OutputTargetContract.IsEnabled(replacement))
                    {
                        created = CreateSession(nextConfig, replacement, board!);
                        if (_suspended) created.Suspend();
                        if (_idlePaused) created.SetIdlePaused(true);
                    }

                    validateInput?.Invoke();
                    created?.Start();
                    if (prepared is not null)
                    {
                        commitAttempted = true;
                        existing!.CommitReconfiguration(prepared);
                    }
                    if (existing is not null && !OutputTargetContract.IsEnabled(replacement))
                    {
                        _sessions.Remove(previousTarget.Id);
                        removed = existing;
                    }
                    if (created is not null) _sessions.Add(previousTarget.Id, created);
                    _config = nextConfig;
                    _boards = nextBoards;
                    commit(nextConfig);
                    UpdateRuntimeMetricPublisherLocked();
                }
                catch (Exception error)
                {
                    _config = previousConfig;
                    _boards = previousBoards;
                    _sessions.Remove(previousTarget.Id);
                    if (existing is not null) _sessions.Add(previousTarget.Id, existing);
                    removed = null;
                    if (commitAttempted && rollback is not null)
                    {
                        try { existing!.CommitReconfiguration(rollback); }
                        catch (Exception rollbackError)
                        {
                            _log.Error($"Output '{previousTarget.Id}' setting rollback failed.", rollbackError);
                            throw new AggregateException("Output setting failed and session rollback was incomplete.",
                                error, rollbackError);
                        }
                    }
                    UpdateRuntimeMetricPublisherLocked();
                    throw;
                }
                if (removed is null)
                    RestoreDesktopStackLocked();
            }
        }
        catch
        {
            if (created is not null) DisposeSessionsBestEffort([created]);
            throw;
        }
        finally
        {
            prepared?.Dispose();
            rollback?.Dispose();
        }
        if (removed is not null)
        {
            try { removed.SetRuntimeMetricsPublisher(false); }
            catch (Exception ex) { _log.Warn("Disabled Output publisher cleanup failed.", ex); }
            DisposeSessionsBestEffort([removed]);
            lock (_sync)
            {
                ClearOutputMetricsLocked(replacement);
                UpdateRuntimeMetricPublisherLocked();
                RestoreDesktopStackLocked();
            }
        }
    }

    public void EnsureCanReloadAll()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_started)
                throw new InvalidOperationException("Runtime output lifetime is stopped. Reload All cannot wake it.");
        }
    }

    public void StopAll()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            _started = false;
            sessions = _sessions.Values.ToArray();
            foreach (var session in sessions)
                session.SetRuntimeMetricsPublisher(false);
            _sessions.Clear();
            foreach (var target in _config.Outputs.Targets)
                ClearOutputMetricsLocked(target);
            UpdateRuntimeMetricPublisherLocked();
        }

        DisposeSessionsBestEffort(sessions);
        lock (_sync)
            UpdateRuntimeMetricPublisherLocked();
    }

    public void SetIdlePaused(bool paused)
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_idlePaused == paused)
                return;
            _idlePaused = paused;
            sessions = _sessions.Values.ToArray();
        }

        foreach (var session in sessions)
        {
            try { session.SetIdlePaused(paused); }
            catch (Exception ex)
            {
                _log.Warn($"Output target '{session.TargetName}' idle transition failed; continuing siblings.", ex);
            }
        }
        lock (_sync)
        {
            if (!_disposed)
            {
                UpdateRuntimeMetricPublisherLocked();
                if (!paused && !_suspended)
                    RestoreDesktopStackLocked();
            }
        }
    }

    // Called on the Runtime UI thread after the first capture of an active telemetry schedule.
    // Do not impose the telemetry cadence on normal rendering, or wake paused Outputs.
    public void RequestFramesAfterInitialTelemetry()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_started || _suspended || _idlePaused)
                return;

            foreach (var session in _sessions.Values)
            {
                try { session.RequestFrame(); }
                catch (Exception ex)
                {
                    _log.Warn($"Output target '{session.TargetName}' initial telemetry frame failed.", ex);
                }
            }
        }
    }

    public void Suspend()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            _suspended = true;
            UpdateRuntimeMetricPublisherLocked();
            sessions = _sessions.Values.ToArray();
        }

        foreach (var session in sessions)
        {
            try
            {
                session.Suspend();
            }
            catch (Exception ex)
            {
                _log.Warn($"Output target '{session.TargetName}' suspend failed; continuing with sibling sessions.", ex);
            }
        }
    }

    public void Resume()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            _suspended = false;
            UpdateRuntimeMetricPublisherLocked();
            sessions = _sessions.Values.ToArray();
        }

        foreach (var session in sessions)
        {
            try
            {
                session.Resume();
            }
            catch (Exception ex)
            {
                _log.Warn($"Output target '{session.TargetName}' resume failed; continuing with sibling sessions.", ex);
            }
        }
        lock (_sync)
        {
            if (!_disposed)
                RestoreDesktopStackLocked();
        }
    }

    public IReadOnlyList<OutputSessionStatus> GetStatuses()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _config.Outputs.Targets.Select(target =>
            {
                _sessions.TryGetValue(target.Id, out var session);
                var status = session is null
                    ? new OutputSessionStatus(
                        target.Id,
                        target.Name,
                        false,
                        false,
                        InactiveStateLocked(target),
                        0,
                        0,
                        0)
                    : new OutputSessionStatus(
                        session.TargetId,
                        session.TargetName,
                        true,
                        session.IsConnected,
                        session.UsbState,
                        session.SentFrames,
                        session.LastFrameMs,
                        session.LastJpegBytes);
                var desktop = target.Kind == OutputTargetContract.DesktopKind;
                return status with
                {
                    Kind = target.Kind,
                    Dashboard = target.Dashboard ?? OutputDefaultDashboardPolicy.NameFor(target),
                    OutputEnabled = OutputTargetContract.IsEnabled(target),
                    ProviderEnabled = target.ProviderEnabled,
                    DeviceEnabled = desktop ? null : target.DeviceEnabled,
                    BoardEnabled = desktop ? target.Desktop?.BoardEnabled : null,
                    Lock = desktop ? target.Desktop?.Lock : null,
                    // Desktop IsConnected observes its visible native window.
                    // Physical connection means the existing transport, not passive PnP presence.
                    IsVisible = desktop ? status.IsConnected : null,
                    RefreshIntervalMs = target.RefreshIntervalMs ?? _config.Display.RefreshIntervalMs
                };
            }).ToArray();
        }
    }

    private IOutputSession CreateSession(AppConfig config, OutputTargetConfig target,
        CanonicalDashboardDefinition dashboard)
    {
        var session = _factory(config, target, dashboard, _metrics, _log);
        if (session is IDesktopStackSession desktop)
        {
            // Native drag completion runs on the WinForms UI thread. Ignore callbacks
            // from sessions already disabled, replaced, or stopped.
            desktop.SetStackRestorer(() =>
            {
                lock (_sync)
                {
                    if (!_disposed &&
                        _sessions.TryGetValue(session.TargetId, out var active) &&
                        ReferenceEquals(active, session))
                        RestoreDesktopStackLocked();
                }
            });
        }
        return session;
    }

    // An out-of-context shell event is delivered to the Runtime UI thread.
    // Do not perturb ordinary window activation or restart hidden/suspended panels:
    // only recover when a visible owned Desktop panel is physically below Progman.
    internal void RestoreDesktopAfterShellChange()
    {
        lock (_sync)
        {
            if (_disposed || !_started || _suspended || _idlePaused)
                return;
            if (_sessions.Values.OfType<IDesktopStackSession>()
                .Any(session => session.IsStackVisible && session.IsBelowDesktop))
                RestoreDesktopStackLocked();
        }
    }

    // Restore sibling order after creation, configuration reload, and visibility changes.
    // Native operations are best-effort: a failed window move must not roll back the
    // committed session/configuration transaction or affect USB sessions.
    private void RestoreDesktopStackLocked()
    {
        var ordered = DesktopZOrderPolicy.BackToFront(_config.Outputs.Targets, _sessions.Keys)
            .Select(id => _sessions[id])
            .OfType<IDesktopStackSession>()
            .Where(session => session.IsStackVisible)
            .ToArray();

        // Insert each owned panel immediately above desktop wallpaper, high-to-low
        // so the final insertion is the lowest saved Desktop.ZOrder.
        // No global timer, no third-party HWND reordering.
        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            try { ordered[index].PlaceAtDesktopBack(); }
            catch (Exception ex)
            {
                _log.Warn("Desktop bottom-stack adjustment failed; continuing other outputs.", ex);
            }
        }
    }

    private OutputTargetConfig FindConfiguredTargetLocked(string targetId) =>
        _config.Outputs.Targets.FirstOrDefault(target =>
            target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Output target '{targetId}' is not configured.");

    private string InactiveStateLocked(OutputTargetConfig target) =>
        !OutputTargetContract.IsEnabled(target) ? "DISABLED" :
        !_started ? "STOPPED" :
        target.Kind == OutputTargetContract.DesktopKind ? "DESKTOP_PENDING" : "WAITING";

    private void UpdateRuntimeMetricPublisherLocked()
    {
        foreach (var target in _config.Outputs.Targets)
        {
            if (_sessions.TryGetValue(target.Id, out var session))
                session.SetRuntimeMetricsPublisher(target.ProviderEnabled);

            // This is existing manager-observed lifecycle state only.
            // Disabled Desktop / Trofeo outputs do not open a device or render
            // simply because ProviderEnabled is true.
            var state = _suspended && session is not null ? "SUSPENDED" :
                _idlePaused && session is not null ? "IDLE_PAUSED" :
                session?.UsbState ?? InactiveStateLocked(target);
            if (!target.ProviderEnabled)
                ClearOutputMetricsLocked(target);
            else
                _metrics.Publish(OutputProviderContract.PassiveStatus(target, state));
        }
    }

    private void ClearOutputMetricsLocked(OutputTargetConfig target) =>
        _metrics.Publish(OutputMetricContract.Unavailable(target));

    private static void StartSessions(IEnumerable<IOutputSession> sessions)
    {
        foreach (var session in sessions)
            session.Start();
    }

    private void DisposeSessionsBestEffort(IEnumerable<IOutputSession> sessions)
    {
        foreach (var session in sessions)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn($"Output target '{session.TargetName}' disposal failed; continuing sibling cleanup.", ex);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OutputSessionManager));
    }

    public void Dispose()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        DisposeSessionsBestEffort(sessions);
        foreach (var target in _config.Outputs.Targets)
            _metrics.Publish(OutputMetricContract.Unavailable(target));
    }
}
