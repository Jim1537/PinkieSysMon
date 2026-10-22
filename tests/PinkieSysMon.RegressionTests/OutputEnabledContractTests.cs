using System.Text.Json;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class OutputEnabledContractTests
{
    public static void PersistenceAndConsumers()
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-Enabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "app.json");
            var log = new FileLogger(Path.Combine(root, "test.log"));
            foreach (var desktopEnabled in new[] { false, true })
            foreach (var deviceEnabled in new[] { false, true })
            foreach (var providerEnabled in new[] { false, true })
            {
                // Exercise supported existing JSON with inherited target values,
                // rather than serializing a new model to manufacture the input.
                File.WriteAllText(path, $$"""
                    {
                      "Dashboard": { "Active": "Legacy" },
                      "Display": { "RefreshIntervalMs": 740 },
                      "Renderer": { "JpegQuality": 81 },
                      "Usb": { "RetryIntervalMs": 850, "TransferTimeoutMs": 950, "DiagnosticJournalEnabled": true },
                      "Outputs": { "Targets": [
                        {
                          "Id": "panel", "Name": "Panel", "Kind": "desktop",
                          "ProviderEnabled": {{providerEnabled.ToString().ToLowerInvariant()}},
                          "Desktop": { "BoardEnabled": {{desktopEnabled.ToString().ToLowerInvariant()}},
                            "Lock": false, "X": -100, "Y": 240, "ZOrder": 2 }
                        },
                        {
                          "Id": "device", "Name": "Device", "Kind": "physical", "Transport": "ly-bulk", "Width": 1920, "Height": 480,
                          "DeviceId": "usb-interface:{{new string('a', 64)}}",
                          "ProviderEnabled": {{(!providerEnabled).ToString().ToLowerInvariant()}},
                          "DeviceEnabled": {{deviceEnabled.ToString().ToLowerInvariant()}}
                        }
                      ] }
                    }
                    """);
                var config = AppConfig.Load(path, log);
                config.Save(path);
                var firstSave = File.ReadAllBytes(path);
                config = AppConfig.Load(path, log);
                config.Save(path);
                Check(File.ReadAllBytes(path).SequenceEqual(firstSave),
                    "Repeated load/save must stabilize without renaming or recreating enablement fields.");

                var panel = config.Outputs.Targets[0];
                var device = config.Outputs.Targets[1];
                Check(panel.Id == "panel" && device.Id == "device" &&
                      panel.Desktop is { Lock: false, X: -100, Y: 240, ZOrder: 2 } &&
                      panel.Dashboard == OutputDefaultDashboardPolicy.DesktopName &&
                      device.Dashboard == OutputDefaultDashboardPolicy.PhysicalProfile(device).Name &&
                      panel.RefreshIntervalMs == 740 && device.RefreshIntervalMs == 740 &&
                      device.DeviceId == "usb-interface:" + new string('a', 64) && device.JpegQuality == 81 &&
                      device.Usb is { RetryIntervalMs: 850, TransferTimeoutMs: 950, DiagnosticJournalEnabled: true } &&
                      panel.ProviderEnabled == providerEnabled && device.ProviderEnabled == !providerEnabled,
                    "Enablement policy must preserve identity, placement, publication and inherited settings.");
                using (var json = JsonDocument.Parse(firstSave))
                {
                    Check(!json.RootElement.TryGetProperty("Dashboard", out _),
                        "Retired global Dashboard.Active must not survive normalized persistence.");
                    var targets = json.RootElement.GetProperty("Outputs").GetProperty("Targets");
                    Check(!targets[0].TryGetProperty("Enabled", out _) &&
                          !targets[0].GetProperty("Desktop").TryGetProperty("Enabled", out _) &&
                          !targets[1].TryGetProperty("Enabled", out _) &&
                          targets[0].GetProperty("Desktop").GetProperty("BoardEnabled").GetBoolean() == desktopEnabled &&
                          targets[1].GetProperty("DeviceEnabled").GetBoolean() == deviceEnabled,
                        "Logical Enabled must use existing per-kind storage without a competing persisted switch.");
                }

                var expected = new Dictionary<string, bool>
                {
                    ["panel"] = desktopEnabled, ["device"] = deviceEnabled
                };
                var boards = config.Outputs.Targets.ToDictionary(t => t.Id, t => new C.DashboardDefinition
                {
                    Widgets = [new C.ValueWidgetDefinition
                    {
                        Id = "value", SourceKind = C.ValueSourceKind.Metric, Metric = "test.enabled." + t.Id
                    }]
                });
                var created = new List<string>();
                using var manager = new OutputSessionManager(config, boards, new MetricStore(), log,
                    (app, target, board, store, logger) =>
                    {
                        created.Add(target.Id);
                        return new TestSession(target.Id, target.Name);
                    });
                manager.Start();
                var status = new RuntimeOutputControl(manager, () => "Legacy").GetStatus();
                var offline = EditorOutputStartWorkflow.FromConfiguration(config);
                var offlineMenu = ScreenMenuContract.Build(config.Outputs.Targets, false, null, false);
                foreach (var target in config.Outputs.Targets)
                {
                    Check(OutputTargetContract.IsEnabled(target) == expected[target.Id] &&
                          status.Outputs.Single(o => o.TargetId == target.Id).OutputEnabled == expected[target.Id] &&
                          status.Outputs.Single(o => o.TargetId == target.Id).IsActive == expected[target.Id] &&
                          offline.Outputs.Single(o => o.TargetId == target.Id).OutputEnabled == expected[target.Id] &&
                          offlineMenu.Targets.Single(o => o.TargetId == target.Id).CanStartOrReload == expected[target.Id],
                        "Runtime and provisional Editor policy must agree regardless of provider publication.");
                }
                Check(created.ToHashSet().SetEquals(expected.Where(p => p.Value).Select(p => p.Key)),
                    "Disabled Outputs must not reach the session factory, including physical hardware.");
                var expectedDemand = expected.Where(p => p.Value).Select(p => "test.enabled." + p.Key).ToHashSet();
                Check(manager.GetRequiredMetrics().SetEquals(expectedDemand) &&
                      OutputTelemetryDemand.Collect(config, boards, ["panel", "device"]).SetEquals(expectedDemand),
                    "Only enabled active dashboards may demand telemetry, even if a caller supplies a disabled ID.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Deterministic lifecycle substitute; never creates a window or a USB transport.
    private sealed class TestSession(string id, string name) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => name;
        public bool IsConnected => false;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig config,
            OutputTargetConfig target, C.DashboardDefinition dashboard) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
        private sealed class Plan : IOutputSessionReconfiguration { public void Dispose() { } }
    }
}
