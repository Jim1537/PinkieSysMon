using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class OutputDashboardOwnershipTests
{
    public static void IndependentAssignments()
    {
        InSandbox((root, log) =>
        {
            Board(root, "Left", 320);
            Board(root, "Right", 640);
            var config = Config(("a", "Left"), ("b", "Right"));
            var boards = RuntimeConfigurationValidator.LoadAssignments(config, root);
            Check(boards["a"].Canvas.Width == 320 && boards["b"].Canvas.Width == 640,
                "Independent output identities must resolve distinct canonical documents.");

            var sessions = new Dictionary<string, FakeOutput>(StringComparer.OrdinalIgnoreCase);
            using var manager = Manager(config, boards, log, sessions);
            manager.Start();
            Check(sessions["a"].Width == 320 && sessions["b"].Width == 640,
                "Each running session must receive its assigned canonical document.");

            // Reassign A to a new board. B must retain its assignment and session instance.
            var next = Config(("a", "Right"), ("b", "Right"));
            manager.Reconfigure(next, RuntimeConfigurationValidator.LoadAssignments(next, root));
            Check(sessions["a"].Width == 640 && sessions["b"].Width == 640 &&
                  manager.GetDashboardName("a") == "Right" && manager.GetDashboardName("b") == "Right",
                "Changing A must not change B's assigned board or create a new B session.");
            Check(sessions.Count == 2, "Configuration must not duplicate or restart sibling sessions.");

            // A broken document must fail before any existing session is touched.
            var broken = Config(("a", "Missing"), ("b", "Right"));
            Check(Throws<InvalidDataException>(() =>
                RuntimeConfigurationValidator.LoadAssignments(broken, root), out var error) &&
                error.Contains("a") && error.Contains("Missing"),
                "A missing board must identify the corresponding output.");
            Check(manager.GetDashboardName("a") == "Right" && sessions["b"].Width == 640,
                "Failed validation must not replace a running output assignment.");
        });
    }

    public static void SharedBoardTargetReload()
    {
        InSandbox((root, log) =>
        {
            Board(root, "Same", 400);
            var config = Config(("a", "Same"), ("b", "Same"));
            var boards = RuntimeConfigurationValidator.LoadAssignments(config, root);
            Check(!ReferenceEquals(boards["a"], boards["b"]),
                "Outputs sharing a filename must get distinct mutable document objects.");

            var sessions = new Dictionary<string, FakeOutput>(StringComparer.OrdinalIgnoreCase);
            using var manager = Manager(config, boards, log, sessions);
            manager.Start();
            Board(root, "Same", 520);
            ReloadTarget(manager, config, "a", root);
            Check(sessions["a"].Width == 520 && sessions["b"].Width == 400 &&
                  sessions["b"].Commits == 0,
                "Target reload must not re-read or reconfigure a sibling sharing the same file.");

            Board(root, "Same", 0);
            Check(Throws<InvalidDataException>(() => ReloadTarget(manager, config, "a", root), out var message) &&
                  message.Contains("a") && message.Contains("Same"),
                "Target reload must surface corrupt board diagnostics.");
            Check(sessions["a"].Width == 520 && sessions["b"].Width == 400 &&
                  sessions["b"].Commits == 0,
                "A failed reload must leave both the target and siblings intact.");
        });
    }

    public static void Rollback()
    {
        InSandbox((root, log) =>
        {
            Board(root, "A", 300);
            Board(root, "B", 600);
            var config = Config(("a", "A"), ("b", "B"));
            var sessions = new Dictionary<string, FakeOutput>(StringComparer.OrdinalIgnoreCase);
            using var manager = Manager(config,
                RuntimeConfigurationValidator.LoadAssignments(config, root), log, sessions);
            manager.Start();

            Board(root, "A", 350);
            Board(root, "B", 650);
            sessions["b"].FailNextCommit = true;
            Check(Throws<InvalidOperationException>(() =>
                manager.Reconfigure(config, RuntimeConfigurationValidator.LoadAssignments(config, root))),
                "A failed sibling commit must reject the entire reconfiguration.");
            Check(sessions["a"].Width == 300 && sessions["b"].Width == 600 &&
                  manager.GetDashboardName("a") == "A" && manager.GetDashboardName("b") == "B",
                "Failed multi-target reconfiguration must restore both old document assignments.");

            sessions["a"].FailNextCommit = true;
            Check(Throws<InvalidOperationException>(() => ReloadTarget(manager, config, "a", root)),
                "Single-target commit failure must be surfaced.");
            Check(sessions["a"].Width == 300 && sessions["b"].Width == 600,
                "Failed targeted commit must roll back without touching another board.");

            sessions["b"].FailNextCommit = true;
            Check(Throws<InvalidOperationException>(() => manager.Reconfigure(config, RuntimeConfigurationValidator.LoadEnabledAssignments(config, root))),
                "Set reconfiguration must reject a failed multi-target commit.");
            Check(sessions["a"].Width == 300 && sessions["b"].Width == 600,
                "Set reconfiguration must restore prior boards after a sibling commit error.");
            manager.Reconfigure(config, RuntimeConfigurationValidator.LoadEnabledAssignments(config, root));
            Check(sessions["a"].Width == 350 && sessions["b"].Width == 650,
                "A successful retry must load the latest independent boards.");
        });
    }

    private static void ReloadTarget(OutputSessionManager manager, AppConfig config, string id, string root)
    {
        var target = config.Outputs.Targets.Single(t => t.Id == id);
        manager.ReloadTarget(target, RuntimeConfigurationValidator.LoadTarget(target, root), _ => { });
    }

    private static AppConfig Config(params (string Id, string Dashboard)[] assignments) => new()
    {
        Outputs = new OutputConfig
        {
            Targets = assignments.Select(item => new OutputTargetConfig
            { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                Id = item.Id,
                Name = item.Id.ToUpperInvariant(),
                Dashboard = item.Dashboard,
                DeviceEnabled = true,
                DeviceId = "usb-interface:" + new string(item.Id[0], 64)
            }).ToList()
        }
    };

    private static OutputSessionManager Manager(AppConfig config,
        IReadOnlyDictionary<string, C.DashboardDefinition> boards,
        FileLogger log, Dictionary<string, FakeOutput> created) =>
        new(config, boards, new MetricStore(), log,
            (app, target, board, metrics, logger) =>
            {
                var session = new FakeOutput(target.Id, target.Name, board.Canvas.Width);
                created.Add(target.Id, session);
                return session;
            });

    private static void Board(string root, string name, int width)
    {
        var folder = Path.Combine(root, "dashboards", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dashboard.json"),
            $$"""{"schemaVersion":19,"canvas":{"width":{{width}},"height":300,"orientation":0},"widgets":[]}""");
    }

    private static void InSandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-stage04-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { action(root, new FileLogger(Path.Combine(root, "test.log"))); }
        finally { Directory.Delete(root, true); }
    }

    private static bool Throws<T>(Action work) where T : Exception => Throws<T>(work, out _);
    private static bool Throws<T>(Action work, out string message) where T : Exception
    {
        try { work(); }
        catch (T error) { message = error.Message; return true; }
        message = string.Empty;
        return false;
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class FakeOutput : IOutputSession
    {
        public FakeOutput(string id, string name, int width)
        {
            TargetId = id; TargetName = name; Width = width;
        }
        public string TargetId { get; }
        public string TargetName { get; private set; }
        public int Width { get; private set; }
        public int Commits { get; private set; }
        public bool FailNextCommit { get; set; }
        public bool IsConnected => false;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "WAITING";
        public void Start() { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition board) =>
            new Plan(target.Name, board.Canvas.Width);
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration)
        {
            var plan = (Plan)reconfiguration;
            TargetName = plan.Name; Width = plan.Width; Commits++;
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new InvalidOperationException("Synthetic post-mutation commit failure.");
            }
        }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
        private sealed record Plan(string Name, int Width) : IOutputSessionReconfiguration
        {
            public void Dispose() { }
        }
    }
}
