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
    // Production sessions retain their currently loaded assets. This default keeps
    // existing synthetic session implementations source-compatible.
    IOutputSessionReconfiguration PrepareRollbackReconfiguration(
        AppConfig config, OutputTargetConfig target, CanonicalDashboardDefinition dashboard) =>
        PrepareReconfiguration(config, target, dashboard);
    void SetRuntimeMetricsPublisher(bool enabled);
    void Suspend();
    void Resume();
    // A one-shot telemetry-ready repaint; synthetic regression sessions may ignore it.
    void RequestFrame() { }
    void SetIdlePaused(bool paused) { }
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
    int LastJpegBytes)
{
    public string Kind { get; init; } = string.Empty;
    public string Dashboard { get; init; } = string.Empty;
    public bool OutputEnabled { get; init; }
    public bool ProviderEnabled { get; init; }
    public bool? DeviceEnabled { get; init; }
    public bool? BoardEnabled { get; init; }
    public bool? Lock { get; init; }
    public bool? IsVisible { get; init; }
    public int RefreshIntervalMs { get; init; }
}
