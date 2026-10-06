using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

internal interface IOutputSessionReconfiguration : IDisposable
{
}

internal interface IOutputSession : IDisposable
{
    string TargetId { get; }
    string TargetName { get; }
    bool IsConnected { get; }
    long SentFrames { get; }
    double LastFrameMs { get; }
    int LastJpegBytes { get; }
    string UsbState { get; }

    void Start();
    IOutputSessionReconfiguration PrepareReconfiguration(
        AppConfig config,
        OutputTargetConfig target,
        global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard);
    // After a plan returned by PrepareReconfiguration succeeds, CommitReconfiguration must
    // not fail for expected operational conditions. All fallible I/O/resource preparation
    // belongs in PrepareReconfiguration; contract/precondition violations may still throw.
    void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration);
    void SetRuntimeMetricsPublisher(bool enabled);
    void Suspend();
    void Resume();
}

internal delegate IOutputSession OutputSessionFactory(
    AppConfig config,
    OutputTargetConfig target,
    global::PinkieSysMon.DashboardModel.DashboardDefinition dashboard,
    MetricStore metrics,
    FileLogger log);

internal sealed record OutputSessionStatus(
    string TargetId,
    string TargetName,
    bool IsActive,
    bool IsConnected,
    string UsbState,
    long SentFrames,
    double LastFrameMs,
    int LastJpegBytes);

internal sealed class OutputSessionManager : IDisposable
{
    private readonly object _sync = new();
    private readonly MetricStore _metrics;
    private readonly FileLogger _log;
    private readonly OutputSessionFactory _factory;
    private readonly Dictionary<string, IOutputSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stoppedTargets = new(StringComparer.OrdinalIgnoreCase);

    private AppConfig _config;
    private CanonicalDashboardDefinition _dashboard;
    private bool _started;
    private bool _suspended;
    private bool _disposed;

    public OutputSessionManager(
        AppConfig config,
        CanonicalDashboardDefinition dashboard,
        MetricStore metrics,
        FileLogger log,
        OutputSessionFactory? factory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _factory = factory ?? (static (app, target, board, store, logger) =>
            new FramePump(app, target, board, store, logger));
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

                foreach (var target in _config.Outputs.Targets)
                    created.Add(_factory(_config, target, _dashboard, _metrics, _log));

                // Do not publish a partially started output set. A session that cannot enter
                // its worker lifecycle must leave the manager exactly as it was before Start.
                if (_suspended)
                {
                    foreach (var session in created)
                        session.Suspend();
                }
                StartSessions(created);

                _started = true;
                _stoppedTargets.Clear();
                foreach (var session in created)
                    _sessions.Add(session.TargetId, session);

                UpdateRuntimeMetricPublisherLocked();
            }
        }
        catch
        {
            DisposeSessionsBestEffort(created);
            throw;
        }
    }


    public void Reconfigure(AppConfig config, CanonicalDashboardDefinition dashboard)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(dashboard);

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

                // Phase 1: prepare every live-session replacement and a rollback plan before
                // mutating any session. This makes the manager formally transactional even if a
                // future IOutputSession implementation violates the normal non-failing Commit
                // contract after successful preparation.
                foreach (var pair in _sessions)
                {
                    if (newTargets.TryGetValue(pair.Key, out var replacementTarget))
                    {
                        prepared.Add((
                            pair.Value,
                            pair.Value.PrepareReconfiguration(config, replacementTarget, dashboard)));

                        if (!oldTargets.TryGetValue(pair.Key, out var oldTarget))
                            throw new InvalidOperationException(
                                $"Active output session '{pair.Key}' has no matching target in the current configuration.");

                        rollback.Add((
                            pair.Value,
                            pair.Value.PrepareReconfiguration(_config, oldTarget, _dashboard)));
                    }
                    else
                    {
                        removed.Add(pair.Value);
                    }
                }

                var stoppedForNewConfig = _stoppedTargets
                    .Where(newTargets.ContainsKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (_started)
                {
                    foreach (var target in config.Outputs.Targets)
                    {
                        if (_sessions.ContainsKey(target.Id) || stoppedForNewConfig.Contains(target.Id))
                            continue;

                        created.Add(_factory(config, target, dashboard, _metrics, _log));
                    }
                }

                // Newly introduced sessions must prove that their worker lifecycle can start
                // before any existing session is committed or removed.
                if (_suspended)
                {
                    foreach (var session in created)
                        session.Suspend();
                }
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
                }
                foreach (var session in created)
                    _sessions.Add(session.TargetId, session);

                _stoppedTargets.Clear();
                foreach (var targetId in stoppedForNewConfig)
                    _stoppedTargets.Add(targetId);

                _config = config;
                _dashboard = dashboard;

                UpdateRuntimeMetricPublisherLocked();
            }

            DisposeSessionsBestEffort(removed);
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

    public void StartOrReloadTarget(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Output target ID must not be empty.", nameof(targetId));

        IOutputSession? created = null;
        IOutputSessionReconfiguration? prepared = null;
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var target = FindConfiguredTargetLocked(targetId);

                if (_sessions.TryGetValue(target.Id, out var existing))
                {
                    prepared = existing.PrepareReconfiguration(_config, target, _dashboard);
                    existing.CommitReconfiguration(prepared);
                }
                else
                {
                    created = _factory(_config, target, _dashboard, _metrics, _log);
                    if (_suspended)
                        created.Suspend();
                    created.Start();
                    _sessions.Add(target.Id, created);
                }

                _started = true;
                _stoppedTargets.Remove(target.Id);
                UpdateRuntimeMetricPublisherLocked();
            }
        }
        catch
        {
            if (created is not null)
            {
                lock (_sync)
                {
                    if (_sessions.TryGetValue(created.TargetId, out var active) && ReferenceEquals(active, created))
                        _sessions.Remove(created.TargetId);
                }
                DisposeSessionsBestEffort([created]);
            }
            throw;
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    public bool StopTarget(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Output target ID must not be empty.", nameof(targetId));

        IOutputSession? removed = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            var target = FindConfiguredTargetLocked(targetId);
            _stoppedTargets.Add(target.Id);

            if (_sessions.Remove(target.Id, out removed))
            {
                removed.SetRuntimeMetricsPublisher(false);
                UpdateRuntimeMetricPublisherLocked();
            }
        }

        if (removed is not null)
            DisposeSessionsBestEffort([removed]);
        return removed is not null;
    }

    public void StartOrReloadAll()
    {
        List<(IOutputSession Session, IOutputSessionReconfiguration Reconfiguration)> prepared = [];
        List<IOutputSession> created = [];
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                foreach (var target in _config.Outputs.Targets)
                {
                    if (_sessions.TryGetValue(target.Id, out var existing))
                    {
                        prepared.Add((
                            existing,
                            existing.PrepareReconfiguration(_config, target, _dashboard)));
                    }
                    else
                    {
                        created.Add(_factory(_config, target, _dashboard, _metrics, _log));
                    }
                }

                if (_suspended)
                {
                    foreach (var session in created)
                        session.Suspend();
                }
                StartSessions(created);

                foreach (var item in prepared)
                    item.Session.CommitReconfiguration(item.Reconfiguration);

                _started = true;
                _stoppedTargets.Clear();
                foreach (var session in created)
                    _sessions.Add(session.TargetId, session);

                UpdateRuntimeMetricPublisherLocked();
            }
        }
        catch
        {
            DisposeSessionsBestEffort(created);
            throw;
        }
        finally
        {
            foreach (var item in prepared)
                item.Reconfiguration.Dispose();
        }
    }

    public void StopAll()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            foreach (var target in _config.Outputs.Targets)
                _stoppedTargets.Add(target.Id);

            _started = false;
            sessions = _sessions.Values.ToArray();
            foreach (var session in sessions)
                session.SetRuntimeMetricsPublisher(false);
            _sessions.Clear();
            ClearRuntimeMetricsLocked();
        }

        DisposeSessionsBestEffort(sessions);
    }

    public void Suspend()
    {
        IOutputSession[] sessions;
        lock (_sync)
        {
            ThrowIfDisposed();
            _suspended = true;
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
    }

    public IReadOnlyList<OutputSessionStatus> GetStatuses()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _config.Outputs.Targets.Select(target =>
            {
                if (!_sessions.TryGetValue(target.Id, out var session))
                {
                    return new OutputSessionStatus(
                        target.Id,
                        target.Name,
                        false,
                        false,
                        "STOPPED",
                        0,
                        0,
                        0);
                }

                return new OutputSessionStatus(
                    session.TargetId,
                    session.TargetName,
                    true,
                    session.IsConnected,
                    session.UsbState,
                    session.SentFrames,
                    session.LastFrameMs,
                    session.LastJpegBytes);
            }).ToArray();
        }
    }

    private OutputTargetConfig FindConfiguredTargetLocked(string targetId) =>
        _config.Outputs.Targets.FirstOrDefault(target =>
            target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Output target '{targetId}' is not configured.");

    private void UpdateRuntimeMetricPublisherLocked()
    {
        IOutputSession? publisher = null;
        foreach (var target in _config.Outputs.Targets)
        {
            if (_sessions.TryGetValue(target.Id, out publisher))
                break;
        }

        if (publisher is null)
        {
            ClearRuntimeMetricsLocked();
            return;
        }

        // Legacy system.runtime.* metrics are global. Until that namespace is ever made
        // target-specific, exactly one active output session owns publication. Enable the
        // replacement first so a publisher handoff does not clear live values.
        publisher.SetRuntimeMetricsPublisher(true);
        foreach (var session in _sessions.Values)
        {
            if (!ReferenceEquals(session, publisher))
                session.SetRuntimeMetricsPublisher(false);
        }
    }

    private void ClearRuntimeMetricsLocked()
    {
        _metrics.Publish(RuntimeMetricContract.Descriptors.ToDictionary(
            descriptor => descriptor.Id,
            _ => (object?)null,
            StringComparer.OrdinalIgnoreCase));
    }

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
    }
}
