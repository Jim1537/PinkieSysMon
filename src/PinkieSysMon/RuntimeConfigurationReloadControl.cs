using CanonicalDashboardDefinition = global::PinkieSysMon.DashboardModel.DashboardDefinition;

namespace PinkieSysMon;

internal sealed record RuntimeConfigurationSnapshot(AppConfig App, TelemetryConfig Telemetry,
    IReadOnlyDictionary<string, CanonicalDashboardDefinition> Boards);

// UI-thread coordination shared by Reload All and the internal Settings RELOAD.
// The composition root owns committed state; this controller owns no second cache.
internal sealed class RuntimeConfigurationReloadControl(
    OutputSessionManager sessions, TelemetryEngine telemetryEngine, MetricStore metrics,
    IReadOnlyCollection<IMetricSource> sources, string applicationRoot, FileLogger log,
    Func<RuntimeConfigurationSnapshot> currentConfiguration,
    Action<IReadOnlyDictionary<string, string>> applyMedia,
    Action<RuntimeConfigurationSnapshot> commitRuntime)
{
    public RuntimeIpcResponse Reload(bool requireRunning = true)
    {
        using var operation = new LogOperation(log, "runtime.settings.reload", null);
        try
        {
            operation.Phase("admission", $"requireRunning={requireRunning}");
            if (requireRunning) sessions.EnsureCanReloadAll();
            var previous = currentConfiguration();
            var previousRequired = sessions.GetRequiredMetrics();
            var appPath = Path.Combine(applicationRoot, "config", "app.json");
            var telemetryPath = Path.Combine(applicationRoot, "config", "telemetry.json");
            // Existing application input is mandatory here; startup owns new-install defaults.
            operation.Phase("file.readAndValidate", "sections=application,telemetry");
            var appBytes = File.ReadAllBytes(appPath);
            var telemetryBytes = File.Exists(telemetryPath) ? File.ReadAllBytes(telemetryPath) : null;
            var app = AppConfig.Load(appPath, log);
            // Normalize the new metric catalog before mutating the active source.
            // Otherwise a newly admitted source's explicit polling policy is lost.
            var activeFilesystem = sources.OfType<FilesystemTelemetrySource>().SingleOrDefault();
            using var pendingFilesystem = activeFilesystem is null
                ? null : new FilesystemTelemetrySource(app.Filesystem, log);
            IReadOnlyCollection<IMetricSource> candidateSources = pendingFilesystem is null
                ? sources
                : [.. sources.Where(source => !ReferenceEquals(source, activeFilesystem)), pendingFilesystem];
            var telemetry = TelemetryConfig.Load(telemetryPath, candidateSources, log);
            operation.Phase("dashboard.validate");
            var boards = RuntimeConfigurationValidator.LoadEnabledAssignments(app, applicationRoot);
            var next = new RuntimeConfigurationSnapshot(app, telemetry, boards);
            operation.Phase("media.validate");
            var media = WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(app.Media.EndpointTypeOverrides);
            var previousMedia = WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(previous.App.Media.EndpointTypeOverrides);
            var inputCheckFailed = false;
            void EnsureInputs()
            {
                // Also invoked under the manager gate: retain only in-memory
                // failure context here; the caller logs after it unwinds.
                try
                {
                    EnsureCurrent(appPath, appBytes);
                    EnsureCurrent(telemetryPath, telemetryBytes);
                }
                catch { inputCheckFailed = true; throw; }
            }
            operation.Phase("conflict.inputs");
            EnsureInputs();
            var filesystemSource = sources.OfType<FilesystemTelemetrySource>().SingleOrDefault();
            var filesystemAttempted = false;
            var outputCommitted = false;
            var telemetryAttempted = false;
            var mediaAttempted = false;
            var catalogAttempted = false;
            var runtimeAttempted = false;
            try
            {
                operation.Phase("filesystem.configure");
                filesystemAttempted = filesystemSource is not null;
                filesystemSource?.ReplaceSources(app.Filesystem);
                // Descriptor meaning must be visible before the telemetry/history
                // prepare/commit path sees newly admitted metric IDs.
                catalogAttempted = true;
                MetricContract.ReplaceProviderDescriptors(MetricProviderContract.Filesystem,
                    FilesystemSourceContract.DescriptorsFor(app.Filesystem.Sources));
                operation.Phase("outputs.apply");
                sessions.Reconfigure(app, boards, EnsureInputs);
                outputCommitted = true;
                operation.Phase("conflict.inputs");
                EnsureInputs();
                // Demand follows the committed real session set, including suspension.
                var required = sessions.GetRequiredMetrics();
                operation.Phase("telemetry.prepare");
                var plan = telemetryEngine.PrepareReconfiguration(telemetry,
                    MetricProviderContract.GetEnabledProviderIds(app.MetricProviders), required);
                telemetryAttempted = true;
                operation.Phase("telemetry.apply");
                telemetryEngine.CommitReconfiguration(plan);
                mediaAttempted = true;
                operation.Phase("media.apply");
                applyMedia(media);
                catalogAttempted = true;
                operation.Phase("catalog.apply");
                PublishConfiguration(app);
                runtimeAttempted = true;
                operation.Phase("runtime.commit");
                commitRuntime(next);
            }
            catch (Exception error)
            {
                operation.RecordFailure(error, inputCheckFailed ? "conflict.inputs" : operation.CurrentPhase, "recoveryRequired=true");
                var failures = new List<Exception>();
                void Recover(Action action, string subsystem)
                {
                    try
                    {
                        operation.Phase("rollback." + subsystem);
                        action();
                        operation.Phase("rollback." + subsystem + ".returned");
                    }
                    catch (Exception recoveryError)
                    {
                        failures.Add(recoveryError);
                        operation.RecordFailure(recoveryError, "rollback." + subsystem, "recovery=failed");
                    }
                }
                if (mediaAttempted) Recover(() => applyMedia(previousMedia), "media");
                // Restore the old dynamic source catalog before reconstructing
                // the previous telemetry schedule and its enabled metric demand.
                if (filesystemAttempted) Recover(() => filesystemSource!.ReplaceSources(previous.App.Filesystem), "filesystem");
                if (outputCommitted) Recover(() => sessions.Reconfigure(previous.App, previous.Boards), "outputs");
                if (catalogAttempted) Recover(() => PublishConfiguration(previous.App), "catalog/version");
                if (telemetryAttempted) Recover(() => telemetryEngine.Reconfigure(previous.Telemetry,
                    MetricProviderContract.GetEnabledProviderIds(previous.App.MetricProviders), previousRequired), "telemetry");
                if (runtimeAttempted) Recover(() => commitRuntime(previous), "committed state");
                if (failures.Count > 0)
                    throw new AggregateException("Configuration reload failed and subsystem recovery was incomplete.",
                        [error, .. failures]);
                throw;
            }
            var enabled = app.Outputs.Targets.Count(OutputTargetContract.IsEnabled);
            var message = $"Reloaded common application/telemetry settings and {enabled} enabled Output dashboards; disabled Outputs remain inactive.";
            log.Info(message);
            operation.Complete($"enabledOutputs={enabled}; compositionRootCommitted=true");
            return new RuntimeIpcResponse(true, message);
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    private void PublishConfiguration(AppConfig app)
    {
        MetricContract.ReplaceOutputDescriptors(app.Outputs.Targets);
        MetricContract.ReplaceProviderDescriptors(MetricProviderContract.Filesystem,
            FilesystemSourceContract.DescriptorsFor(app.Filesystem.Sources));
        metrics.Publish(RuntimeMetricContract.Version,
            MetricProviderContract.IsEnabled(app.MetricProviders, MetricProviderContract.System)
                ? RuntimeVersion.Current : null);
    }

    private static void EnsureCurrent(string path, byte[]? expected)
    {
        var exists = File.Exists(path);
        if (expected is null ? exists : !exists || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("Configuration input changed during reload; the newer file was retained.");
    }
}
