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
        if (requireRunning) sessions.EnsureCanReloadAll();
        var previous = currentConfiguration();
        var previousRequired = sessions.GetRequiredMetrics();
        var appPath = Path.Combine(applicationRoot, "config", "app.json");
        var telemetryPath = Path.Combine(applicationRoot, "config", "telemetry.json");
        // Existing application input is mandatory here; startup owns new-install defaults.
        var appBytes = File.ReadAllBytes(appPath);
        var telemetryBytes = File.Exists(telemetryPath) ? File.ReadAllBytes(telemetryPath) : null;
        var app = AppConfig.Load(appPath, log);
        var telemetry = TelemetryConfig.Load(telemetryPath, sources, log);
        var boards = RuntimeConfigurationValidator.LoadEnabledAssignments(app, applicationRoot);
        var next = new RuntimeConfigurationSnapshot(app, telemetry, boards);
        var media = WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(app.Media.EndpointTypeOverrides);
        var previousMedia = WindowsMediaTelemetrySource.PrepareEndpointTypeOverrides(previous.App.Media.EndpointTypeOverrides);
        void EnsureInputs()
        {
            EnsureCurrent(appPath, appBytes);
            EnsureCurrent(telemetryPath, telemetryBytes);
        }
        EnsureInputs();
        var outputCommitted = false;
        var telemetryAttempted = false;
        var mediaAttempted = false;
        var catalogAttempted = false;
        var runtimeAttempted = false;
        try
        {
            sessions.Reconfigure(app, boards, EnsureInputs);
            outputCommitted = true;
            EnsureInputs();
            // Demand follows the committed real session set, including suspension.
            var required = sessions.GetRequiredMetrics();
            var plan = telemetryEngine.PrepareReconfiguration(telemetry,
                MetricProviderContract.GetEnabledProviderIds(app.MetricProviders), required);
            telemetryAttempted = true;
            telemetryEngine.CommitReconfiguration(plan);
            mediaAttempted = true;
            applyMedia(media);
            catalogAttempted = true;
            PublishConfiguration(app);
            runtimeAttempted = true;
            commitRuntime(next);
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            void Recover(Action action, string subsystem)
            {
                try { action(); }
                catch (Exception recoveryError)
                {
                    failures.Add(recoveryError);
                    log.Error($"Configuration {subsystem} recovery failed.", recoveryError);
                }
            }
            if (mediaAttempted) Recover(() => applyMedia(previousMedia), "media");
            if (outputCommitted) Recover(() => sessions.Reconfigure(previous.App, previous.Boards), "outputs");
            if (telemetryAttempted) Recover(() => telemetryEngine.Reconfigure(previous.Telemetry,
                MetricProviderContract.GetEnabledProviderIds(previous.App.MetricProviders), previousRequired), "telemetry");
            if (catalogAttempted) Recover(() => PublishConfiguration(previous.App), "catalog/version");
            if (runtimeAttempted) Recover(() => commitRuntime(previous), "committed state");
            if (failures.Count > 0)
                throw new AggregateException("Configuration reload failed and subsystem recovery was incomplete.",
                    [error, .. failures]);
            throw;
        }
        var enabled = app.Outputs.Targets.Count(OutputTargetContract.IsEnabled);
        var message = $"Reloaded common application/telemetry settings and {enabled} enabled Output dashboards; disabled Outputs remain inactive.";
        log.Info(message);
        return new RuntimeIpcResponse(true, message);
    }

    private void PublishConfiguration(AppConfig app)
    {
        MetricContract.ReplaceOutputDescriptors(app.Outputs.Targets);
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
