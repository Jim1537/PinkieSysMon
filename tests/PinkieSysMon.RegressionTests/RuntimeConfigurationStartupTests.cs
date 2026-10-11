using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class RuntimeConfigurationStartupTests
{
    public static void DesktopOnly()
    {
        InSandbox((root, path, log) =>
        {
            CreateBoard(root, "PanelA");
            CreateBoard(root, "PanelB");
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig { Id = "desktop-a", Name = "Panel A",
                            Kind = OutputTargetContract.DesktopKind, Dashboard = "PanelA" },
                        new OutputTargetConfig { Id = "desktop-b", Name = "Panel B",
                            Kind = OutputTargetContract.DesktopKind, Dashboard = "PanelB" }
                    ]
                }
            };
            config.Save(path);
            var saved = File.ReadAllBytes(path);
            var loaded = AppConfig.Load(path, log);
            var (selected, board) = RuntimeConfigurationValidator.ValidateAndSelect(loaded, root);
            Check(selected == "PanelA" && board.Canvas.Width == 400, "Desktop-only start must select a valid board without needing USB.");
            using (var manager = MakeManager(loaded, log, out var sessions))
            {
                manager.Start();
                var statuses = manager.GetStatuses();
                Check(sessions.Count == 0 && statuses.Count == 2 &&
                      statuses.All(s => !s.IsActive && s.UsbState == "DESKTOP_PENDING"),
                      "Desktop-only startup must not instantiate a USB frame pump or report phantom frames.");
                Check(File.ReadAllBytes(path).SequenceEqual(saved),
                      "Startup must not rewrite the persisted app.json.");
                Check(Throws<InvalidOperationException>(() => manager.StartOrReloadTarget("desktop-a")),
                      "An unavailable Desktop session must fail truthfully until the Desktop renderer is implemented.");
                manager.StartOrReloadAll();
                Check(sessions.Count == 0, "Start All must not create USB sessions for Desktop targets.");
            }

            // Parsing a default file without Outputs must not invent a physical display.
            File.WriteAllText(path, """{"Dashboard":{"Active":"PanelA"}}""");
            var defaults = AppConfig.Load(path, log);
            Check(defaults.Outputs.Targets.Count == 1 &&
                  defaults.Outputs.Targets[0].Kind == OutputTargetContract.DesktopKind,
                  "New default configuration must contain only a virtual target.");
            RuntimeConfigurationValidator.ValidateAndSelect(defaults, root);
        });
    }

    public static void MixedTargets()
    {
        InSandbox((root, path, log) =>
        {
            CreateBoard(root, "PanelA");
            CreateBoard(root, "Hardware");
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig { Id = "desktop", Name = "Desktop",
                            Kind = OutputTargetContract.DesktopKind, Dashboard = "PanelA" },
                        new OutputTargetConfig { Id = "usb-off", Name = "Disabled",
                            Dashboard = "Hardware", DeviceEnabled = false,
                            DeviceId = "trofeo:" + new string('b', 64) },
                        new OutputTargetConfig { Id = "usb-on", Name = "Enabled",
                            Dashboard = "Hardware", DeviceEnabled = true,
                            DeviceId = "trofeo:" + new string('a', 64) }
                    ]
                }
            };
            config.Save(path);
            var loaded = AppConfig.Load(path, log);
            var (selected, _) = RuntimeConfigurationValidator.ValidateAndSelect(loaded, root);
            Check(selected == "Hardware", "An enabled physical output must select the temporary legacy runtime document.");
            using var manager = MakeManager(loaded, log, out var sessions);
            manager.Start();
            Check(sessions.Count == 1 && sessions[0].TargetId == "usb-on" &&
                  manager.GetStatuses().Single(s => s.TargetId == "usb-off").UsbState == "DISABLED",
                  "Only explicitly enabled Trofeo targets may create sessions.");
            Check(Throws<InvalidOperationException>(() => manager.StartOrReloadTarget("usb-off")),
                  "Starting a disabled physical target must not open USB.");

            // Applying a new persisted configuration must retire the now-disabled live session.
            loaded.Outputs.Targets.Single(t => t.Id == "usb-on").DeviceEnabled = false;
            loaded.Save(path);
            var reloaded = AppConfig.Load(path, log);
            RuntimeConfigurationValidator.ValidateAndSelect(reloaded, root);
            manager.Reconfigure(reloaded, new C.DashboardDefinition());
            Check(manager.GetStatuses().All(s => !s.IsActive) && sessions.Count == 1,
                  "Reload must stop an output disabled in its new saved configuration.");
            manager.StartOrReloadAll();
            Check(sessions.Count == 1, "Reload and Start All must not revive disabled hardware.");
        });
    }

    public static void InvalidInput()
    {
        InSandbox((root, path, log) =>
        {
            CreateBoard(root, "PanelA");
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig { Id = "desktop", Name = "Desktop",
                            Kind = OutputTargetContract.DesktopKind, Dashboard = "PanelA" }
                    ]
                }
            };
            config.Save(path);
            var original = File.ReadAllBytes(path);
            RuntimeConfigurationValidator.ValidateAndSelect(AppConfig.Load(path, log), root);

            config.Outputs.Targets[0].Dashboard = "Missing";
            config.Save(path);
            var badRef = File.ReadAllBytes(path);
            Check(Throws<InvalidDataException>(() =>
                RuntimeConfigurationValidator.ValidateAndSelect(AppConfig.Load(path, log), root),
                out var missingError) && missingError.Contains("desktop") &&
                missingError.Contains("Missing"),
                "A missing board must produce an output-specific diagnostic, never fallback silently.");
            Check(File.ReadAllBytes(path).SequenceEqual(badRef),
                  "Invalid board must never cause implicit rewriting of app.json.");

            // A malformed canvas is a dashboard error, not a reason to choose another board.
            CreateBoard(root, "Broken", width: 0);
            config.Outputs.Targets[0].Dashboard = "Broken";
            config.Save(path);
            Check(Throws<InvalidDataException>(() =>
                RuntimeConfigurationValidator.ValidateAndSelect(AppConfig.Load(path, log), root),
                out var malformedBoard) && malformedBoard.Contains("Broken"),
                "Invalid canvas dimensions must be diagnosed with the target's board name.");

            File.WriteAllText(path, """{"Outputs":{"Targets":[{"Id":"x","Kind":"unknown"}]}}""");
            Check(Throws<InvalidDataException>(() => AppConfig.Load(path, log)),
                "Unknown kinds must reject configuration on load.");

            File.WriteAllText(path, """{"Outputs":{"Targets":null}}""");
            Check(Throws<InvalidDataException>(() => AppConfig.Load(path, log)),
                "Null output targets must reject configuration on load.");

            File.WriteAllText(path, """{"Dashboard":null}""");
            Check(Throws<InvalidDataException>(() => AppConfig.Load(path, log)),
                "Explicitly null top-level settings must produce an actionable validation error.");

            File.WriteAllText(path, """{"Outputs":{"Targets":[}""");
            Check(Throws<InvalidDataException>(() => AppConfig.Load(path, log), out var badJson) &&
                  badJson.Contains("invalid JSON"), "Malformed JSON must produce a comprehensible diagnostic.");

            Check(original.Length > 0, "Configuration test must have started from a real persisted file.");
        });
    }

    private static OutputSessionManager MakeManager(
        AppConfig config, FileLogger log, out List<FakeSession> created)
    {
        var list = new List<FakeSession>();
        created = list;
        return new OutputSessionManager(config, new C.DashboardDefinition(), new MetricStore(), log,
            (app, target, board, metrics, logger) =>
            {
                var session = new FakeSession(target.Id, target.Name);
                list.Add(session);
                return session;
            });
    }

    private static void CreateBoard(string root, string name, int width = 400)
    {
        var dir = Path.Combine(root, "dashboards", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dashboard.json"),
            $$"""{"schemaVersion":19,"canvas":{"width":{{width}},"height":300,"orientation":0},"widgets":[]}""");
    }

    private static void InSandbox(Action<string, string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-stage03-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            action(root, Path.Combine(root, "app.json"),
                   new FileLogger(Path.Combine(root, "regression.log")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static bool Throws<T>(Action action) where T : Exception =>
        Throws<T>(action, out _);

    private static bool Throws<T>(Action action, out string message) where T : Exception
    {
        try { action(); }
        catch (T ex) { message = ex.Message; return true; }
        message = string.Empty;
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeSession : IOutputSession
    {
        public FakeSession(string id, string name) { TargetId = id; TargetName = name; }
        public string TargetId { get; }
        public string TargetName { get; }
        public bool IsConnected => false;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig config,
            OutputTargetConfig target, C.DashboardDefinition dashboard) => new FakePlan();
        public void CommitReconfiguration(IOutputSessionReconfiguration plan) { }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
        private sealed class FakePlan : IOutputSessionReconfiguration { public void Dispose() { } }
    }
}
