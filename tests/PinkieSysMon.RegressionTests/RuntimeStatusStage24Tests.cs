using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class RuntimeStatusStage24Tests
{
    public static void IndependentOutputStatus()
    {
        InSandbox(root =>
        {
            var config = Config();
            var created = new List<Session>();
            using var manager = new OutputSessionManager(config,
                config.Outputs.Targets.ToDictionary(t => t.Id, _ => new C.DashboardDefinition()),
                new MetricStore(), new FileLogger(Path.Combine(root, "test.log")),
                (app, target, board, store, log) =>
                {
                    var session = new Session(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });
            manager.Start();
            var control = new RuntimeOutputControl(manager, () => "(per-output dashboards)");
            var status = control.GetStatus();
            var desktop = status.Outputs.Single(o => o.TargetId == "desktop-a");
            var physical = status.Outputs.Single(o => o.TargetId == "device-b");
            Check(created.Count == 1 && desktop.Kind == OutputTargetContract.DesktopKind &&
                  desktop.Dashboard == "First" && desktop.OutputEnabled == true && desktop.BoardEnabled == true &&
                  desktop.DeviceEnabled is null && desktop.ProviderEnabled == false && desktop.Lock == true &&
                  desktop.IsVisible == true && desktop.RefreshIntervalMs == 250,
                "Desktop status must expose its actual assignment, independent switches, cadence and observed visibility.");
            Check(physical.Kind == OutputTargetContract.TrofeoKind && physical.Dashboard == "Second" &&
                  physical.OutputEnabled == false && physical.DeviceEnabled == false && physical.ProviderEnabled == true &&
                  physical.BoardEnabled is null && physical.Lock is null && physical.IsVisible is null &&
                  !physical.IsActive && !physical.IsConnected && created.All(s => s.TargetId != "device-b"),
                "Passive physical status must expose no fake Desktop fields and must not instantiate disabled hardware.");
            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(config, "desktop-a", false),
                manager.GetDashboardAssignments());
            var disabledDesktop = control.GetStatus().Outputs.Single(o => o.TargetId == "desktop-a");
            Check(!disabledDesktop.IsActive && disabledDesktop.IsVisible == false &&
                  disabledDesktop.OutputEnabled == false && disabledDesktop.Dashboard == "First" && disabledDesktop.UsbState == "DISABLED",
                "Disabling must unload the session, preserve its assignment and report the canonical Enabled value.");
            manager.Reconfigure(config, manager.GetDashboardAssignments());
            Check(control.GetStatus().Outputs.Single(o => o.TargetId == "desktop-a").IsVisible == true,
                "Enabling the Desktop must restore its visibility without a second Start command.");
        });
    }

    public static void StatusWireCompatibility()
    {
        var output = new RuntimeOutputStatus("desktop\t\"one\"", "Panel", true, true, "DISPLAYING")
        {
            Kind = OutputTargetContract.DesktopKind, Dashboard = "First", OutputEnabled = true,
            ProviderEnabled = false, BoardEnabled = true, Lock = true, IsVisible = true, RefreshIntervalMs = 250
        };
        var expected = new RuntimeStatusSnapshot("(per-output dashboards)", [output]);
        var actual = RuntimeIpcProtocol.DeserializeStatus(RuntimeIpcProtocol.SerializeStatus(expected));
        Check(actual.Outputs.Single() == output && actual.Dashboard == expected.Dashboard,
            "Additive output fields and opaque target IDs must survive v1 status serialization exactly.");
        const string legacy = "{\"Dashboard\":\"Legacy\",\"Outputs\":[{\"TargetId\":\"old\",\"TargetName\":\"Old\",\"IsActive\":true,\"IsConnected\":false,\"UsbState\":\"WAITING\"}]}";
        var old = RuntimeIpcProtocol.DeserializeStatus(legacy).Outputs.Single();
        Check(old.TargetId == "old" && old.IsActive && !old.IsConnected && old.UsbState == "WAITING" &&
              old.Kind is null && old.Dashboard is null && old.OutputEnabled is null &&
              old.ProviderEnabled is null && old.IsVisible is null,
            "Missing v2 fields in a legacy status payload must remain unknown, not invented off/default values.");
        var command = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.BuildTargetCommand(
            RuntimeIpcProtocol.StartOrReloadTargetCommand, output.TargetId));
        Check(command.TargetId == output.TargetId && command.Kind == RuntimeIpcCommandKind.StartOrReloadTarget,
            "Status enrichment must retain the established target-scoped command format.");
    }

    public static void TrayAndStartupPolicy()
    {
        InSandbox(root =>
        {
            var config = Config();
            using var manager = new OutputSessionManager(config, new C.DashboardDefinition(),
                new MetricStore(), new FileLogger(Path.Combine(root, "test.log")),
                (app, target, board, store, log) => new Session(target.Id, target.Name));
            var control = new RuntimeOutputControl(manager, () => "(per-output dashboards)");
            manager.Start();
            var status = control.GetStatus();
            var desktop = status.Outputs.Single(o => o.TargetId == "desktop-a");
            var physical = status.Outputs.Single(o => o.TargetId == "device-b");
            Check(desktop.IsActive && !physical.IsActive,
                "Normal startup must include eligible Desktop sessions and retain disabled physical policy.");
            Check(RuntimeTrayOutputContract.CanUseActions(desktop.OutputEnabled) && !RuntimeTrayOutputContract.CanUseActions(physical.OutputEnabled),
                "Tray reload eligibility must follow canonical enablement independently of Provider Enabled.");
            Check(RuntimeTrayOutputContract.Summary(status) ==
                  "2 configured · 1 active · 1 Desktop visible · 0 devices connected",
                "Mixed-output summary must not count a visible Desktop as a connected USB device.");
            Check(RuntimeTrayOutputContract.Tooltip(status).Length <= 63,
                "The native tray tooltip must fit its Windows limit without a fictional global dashboard.");
            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(config, "desktop-a", false),
                manager.GetDashboardAssignments());
            desktop = control.GetStatus().Outputs.Single(o => o.TargetId == "desktop-a");
            Check(!RuntimeTrayOutputContract.CanUseActions(desktop.OutputEnabled) && !desktop.IsActive,
                "A disabled Desktop must deny reload instead of retaining an independent manual stopped state.");
        });
    }

    private static AppConfig Config()
    {
        var config = new AppConfig();
        config.Outputs.Targets =
        [
            new() { Id = "desktop-a", Name = "Panel", Kind = OutputTargetContract.DesktopKind,
                Transport = OutputTargetContract.DesktopTransport, Dashboard = "First", RefreshIntervalMs = 250,
                Desktop = new DesktopOutputConfig { BoardEnabled = true, Lock = true } },
            new() { Id = "device-b", Name = "Disabled device", Kind = OutputTargetContract.TrofeoKind,
                Dashboard = "Second", DeviceEnabled = false, ProviderEnabled = true }
        ];
        return config;
    }

    private static void InSandbox(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-Stage24-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Session(string id, string name) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => name;
        public bool IsConnected { get; private set; }
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => IsConnected ? "DISPLAYING" : "STOPPED";
        public void Start() => IsConnected = true;
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig config, OutputTargetConfig target,
            C.DashboardDefinition dashboard) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() => IsConnected = false;
        public void Resume() => IsConnected = true;
        public void Dispose() => IsConnected = false;
    }

    private sealed class Plan : IOutputSessionReconfiguration
    {
        public void Dispose() { }
    }
}
