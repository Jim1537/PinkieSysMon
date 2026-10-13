using System.Text.Json.Nodes;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class RuntimeConfigurationStage24Tests
{
    public static void ConfigurationProtocol()
    {
        const string id = "output\t\"opaque\" ";
        var assignment = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.BuildAssignDashboardCommand(id, "New board"));
        Check(assignment.Kind == RuntimeIpcCommandKind.AssignDashboard && assignment.TargetId == id &&
              assignment.Dashboard == "New board" && assignment.Value is null,
            "Assignment payload must preserve opaque identity and the independent dashboard argument.");
        foreach (var (verb, kind) in new[]
        {
            (RuntimeIpcProtocol.SetDesktopBoardEnabledCommand, RuntimeIpcCommandKind.SetDesktopBoardEnabled),
            (RuntimeIpcProtocol.SetDesktopLockCommand, RuntimeIpcCommandKind.SetDesktopLock)
        })
        {
            foreach (var value in new[] { false, true })
            {
                var parsed = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.BuildDesktopSettingCommand(verb, id, value));
                Check(parsed.Kind == kind && parsed.TargetId == id && parsed.Value == value && parsed.Dashboard is null,
                    "Desktop settings must retain explicit true and false without target delimiter ambiguity.");
            }
        }
        foreach (var payload in new[] { "null", "[]", "{}", "not-json", "{\"TargetId\":\"a\"}",
            "{\"TargetId\":\"a\",\"Value\":\"false\"}", "{\"TargetId\":\"a\",\"Value\":false,\"Dashboard\":\"A\"}" })
        {
            var parsed = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.SetDesktopLockCommand + "\t" + payload);
            Check(parsed.Kind == RuntimeIpcCommandKind.Invalid && !string.IsNullOrWhiteSpace(parsed.Error),
                "Incomplete, mistyped and mixed-intent configuration payloads must fail closed.");
        }
        Check(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.AssignDashboardCommand +
            "\t{\"TargetId\":\"a\",\"Dashboard\":\" \"}").Kind == RuntimeIpcCommandKind.Invalid,
            "An empty dashboard assignment must be rejected by the protocol.");
    }

    public static void AssignmentIsolation()
    {
        using var f = new Fixture();
        var a = f.Latest("desktop-a");
        var b = f.Latest("desktop-b");
        var old = f.Current;
        var siblingBoard = f.Manager.GetDashboardAssignments()["desktop-b"];
        var siblingJson = f.Json()["Outputs"]!["Targets"]![1]!.ToJsonString();
        File.WriteAllText(f.BoardPath("B"), "{broken sibling document");
        var assigned = f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New"));
        Check(assigned.Success && a.Width == 600 && f.Current.Outputs.Targets[0].Dashboard == "New" &&
              f.Disk().Outputs.Targets[0].Dashboard == "New" && old.Outputs.Targets[0].Dashboard == "A",
            "Assignment must commit only A without mutating the prior configuration object.");
        Check(b.Width == 400 && b.Prepares == 0 && b.Commits == 0 && b.Starts == 1 && b.Disposals == 0 &&
              ReferenceEquals(siblingBoard, f.Manager.GetDashboardAssignments()["desktop-b"]) &&
              ReferenceEquals(old.Outputs.Targets[1], f.Current.Outputs.Targets[1]) &&
              f.Json()["Outputs"]!["Targets"]![1]!.ToJsonString() == siblingJson &&
              f.Json()["Future"]!["Keep"]!.GetValue<int>() == 42,
            "A target change must retain the sibling's loaded document, session, target object and unknown persisted fields.");
        f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "device-c", Dashboard: "New"));
        Check(f.Current.Outputs.Targets[2].Dashboard == "New" && !f.Current.Outputs.Targets[2].DeviceEnabled &&
              f.Current.Outputs.Targets[2].ProviderEnabled && f.Created.All(s => s.TargetId != "device-c"),
            "Assigning a passive physical Output must never create or enable hardware.");
        var bytes = File.ReadAllBytes(f.ConfigPath);
        var commits = a.Commits;
        Failure<InvalidDataException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "Missing")));
        Failure<InvalidDataException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "../escape")));
        Check(a.Commits == commits && a.Width == 600 && File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
            "Missing/unsafe dashboards must fail before changing the selected session or file.");
    }

    public static void DesktopSwitches()
    {
        using var f = new Fixture();
        var a = f.Latest("desktop-a");
        var b = f.Latest("desktop-b");
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: false));
        Check(!f.Status("desktop-a").IsActive && f.Status("desktop-a").BoardEnabled == false &&
              f.Status("desktop-a").ProviderEnabled == false && f.Status("desktop-a").Lock == true && a.Disposals == 1,
            "Board Enabled off must unload only A and retain its independent Provider and Lock.");
        var created = f.Created.Count;
        File.WriteAllText(f.BoardPath("A"), "{broken disk document");
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "desktop-a", Value: false));
        Check(f.Created.Count == created && !f.Status("desktop-a").IsActive &&
              f.Status("desktop-a").Lock == false && f.Status("desktop-a").BoardEnabled == false,
            "Lock must use the committed definition and must not reload or secretly enable a hidden panel.");
        f.Board("A", 350);
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: true));
        Check(f.Status("desktop-a").IsActive && f.Latest("desktop-a").Width == 350 &&
              f.Status("desktop-a").Lock == false && f.Status("desktop-a").ProviderEnabled == false &&
              b.Prepares == 0 && b.Commits == 0 && b.Disposals == 0,
            "Explicit enable must reload only A and retain Lock/Provider without reconfiguring its sibling.");
        var bytes = File.ReadAllBytes(f.ConfigPath);
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "device-c", Value: false)));
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "device-c", Value: true)));
        Check(File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && f.Created.All(s => s.TargetId != "device-c"),
            "Desktop requests must reject physical Outputs before saving or opening hardware.");
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: false));
        var hideLast = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-b", Value: false);
        f.Execute(hideLast);
        Check(f.Control.GetStatus().Outputs.All(o => !o.IsActive) && !f.Control.ShouldExitRuntimeAfter(hideLast) &&
              f.Status("desktop-b").ProviderEnabled == true,
            "Hiding the final panel must preserve its Provider and leave Runtime available for a later enable.");
    }

    public static void ConfigurationRollback()
    {
        using var f = new Fixture();
        var a = f.Latest("desktop-a");
        var b = f.Latest("desktop-b");
        var bytes = File.ReadAllBytes(f.ConfigPath);
        a.FailPrepare = true;
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New")));
        a.FailCommit = true;
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New")));
        Check(a.Width == 300 && f.Current.Outputs.Targets[0].Dashboard == "A" &&
              File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
            "Preparation and post-mutation commit failures must retain the old assignment and exact file bytes.");
        f.OnCommit = next =>
        {
            if (next.Outputs.Targets[0].Dashboard == "New" || next.Outputs.Targets[0].Desktop?.BoardEnabled == false)
                throw new InvalidOperationException("Synthetic shared-demand commit failure.");
        };
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New")));
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: false)));
        Check(a.Width == 300 && a.Disposals == 0 && f.Status("desktop-a").IsActive &&
              f.Current.Outputs.Targets[0].Dashboard == "A" && f.Status("desktop-a").BoardEnabled == true &&
              File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && b.Prepares == 0 && b.Commits == 0 && b.Disposals == 0,
            "A failure after persistence must restore the exact file and active session set without disposing A or touching B.");
        f.OnCommit = null;
        f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New"));
        Check(a.Width == 600 && f.Status("desktop-a").Dashboard == "New",
            "Target configuration must remain retryable after rollback.");
    }

    public static void ConcurrentPersistence()
    {
        using var f = new Fixture();
        var a = f.Latest("desktop-a");
        var newer = f.Json();
        newer["Future"]!["Keep"] = 99;
        var newerText = newer.ToJsonString();
        a.OnPrepare = () => File.WriteAllText(f.ConfigPath, newerText);
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New")));
        Check(File.ReadAllText(f.ConfigPath) == newerText && a.Width == 300 && f.Status("desktop-a").Dashboard == "A",
            "A writer during preparation must be retained while the selected live session rolls back.");
        f.OnCommit = next =>
        {
            if (next.Outputs.Targets[0].Dashboard != "New") return;
            File.WriteAllText(f.ConfigPath, newerText);
            throw new InvalidOperationException("Synthetic failure after a newer writer.");
        };
        Failure<AggregateException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New")));
        Check(File.ReadAllText(f.ConfigPath) == newerText && a.Width == 300 && f.Status("desktop-a").Dashboard == "A" &&
              f.Current.Outputs.Targets[0].Dashboard == "A" &&
              Directory.GetFiles(Path.GetDirectoryName(f.ConfigPath)!, "*.tmp").Length == 0,
            "Incomplete disk rollback must report failure, retain the newer writer and recover the old live assignment without temp leaks.");
        f.OnCommit = null;
        var changed = f.Disk();
        changed.Outputs.Targets[0].Dashboard = "New";
        changed.Save(f.ConfigPath);
        var bytes = File.ReadAllBytes(f.ConfigPath);
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "A")));
        Check(File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && a.Width == 300,
            "An already changed requested field must not be overwritten from stale Runtime state.");
        changed.Outputs.Targets[0].Dashboard = "A";
        changed.Outputs.Targets[0].Kind = OutputTargetContract.TrofeoKind;
        changed.Outputs.Targets[0].Transport = OutputTargetContract.TrofeoTransport;
        changed.Outputs.Targets[0].DeviceId = "trofeo:" + new string('a', 64);
        changed.Outputs.Targets[0].Desktop = null;
        changed.Save(f.ConfigPath);
        bytes = File.ReadAllBytes(f.ConfigPath);
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "desktop-a", Value: false)));
        Check(File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && f.Created.All(s => s.TargetId != "device-c"),
            "A rebound target must fail closed without modifying the saved binding or opening hardware.");
    }

    public static void StoppedAndSuspendedState()
    {
        using var f = new Fixture(start: false);
        f.Manager.Suspend();
        f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "New"));
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "desktop-a", Value: false));
        Check(f.Created.Count == 0 && !f.Status("desktop-a").IsActive,
            "Assignment and Lock must not instantiate a configured-enabled but inactive Output.");
        f.FailNextStart = true;
        var bytes = File.ReadAllBytes(f.ConfigPath);
        Failure<InvalidOperationException>(() => f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: true)));
        Check(f.Created.Single().Disposals == 1 && !f.Status("desktop-a").IsActive &&
              File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
            "Failed explicit enable must dispose its candidate without committing persistence or active state.");
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: true));
        Check(f.Status("desktop-a").IsActive && f.Status("desktop-a").IsVisible == false &&
              f.Created.All(s => s.TargetId == "desktop-a"),
            "Enable during suspend must start only the requested lifecycle and retain invisible suspension.");
        f.Manager.Resume();
        Check(f.Status("desktop-a").IsVisible == true, "Resume must show only the successfully enabled Desktop.");
        f.Manager.StopTarget("desktop-a");
        var count = f.Created.Count(s => s.TargetId == "desktop-a");
        f.Execute(new(RuntimeIpcCommandKind.AssignDashboard, "desktop-a", Dashboard: "A"));
        f.Manager.Reconfigure(f.Current, f.Manager.GetDashboardAssignments());
        Check(f.Created.Count(s => s.TargetId == "desktop-a") == count && !f.Status("desktop-a").IsActive,
            "Assignment must retain explicit stopped-target intent across a later configuration reload.");
        f.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "desktop-a", Value: true));
        Check(f.Status("desktop-a").IsActive && f.Latest("desktop-a").Width == 300,
            "Explicit Board Enabled must clear that target's stopped marker and restore its own assignment.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Failure<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-Stage24Config-" + Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Root, "config", "app.json");
        public AppConfig Current;
        public readonly OutputSessionManager Manager;
        public readonly RuntimeOutputControl Control;
        public readonly List<Session> Created = [];
        public Action<AppConfig>? OnCommit;
        public bool FailNextStart;
        private readonly FileLogger _log;
        public Fixture(bool start = true)
        {
            Directory.CreateDirectory(Root);
            _log = new FileLogger(Path.Combine(Root, "test.log"));
            Current = new AppConfig { Outputs = new OutputConfig { Targets =
            [
                new() { Id = "desktop-a", Name = "A", Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport, Dashboard = "A", Desktop = new() },
                new() { Id = "desktop-b", Name = "B", Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport, Dashboard = "B", ProviderEnabled = true, Desktop = new() },
                new() { Id = "device-c", Name = "Disabled C", Dashboard = "C", ProviderEnabled = true,
                    DeviceId = "trofeo:" + new string('c', 64) }
            ] } };
            Current.Save(ConfigPath);
            Board("A", 300); Board("B", 400); Board("C", 500); Board("New", 600);
            var json = Json();
            json["Future"] = new JsonObject { ["Keep"] = 42 };
            json["Outputs"]!["Targets"]![1]!["FutureTarget"] = "preserve";
            File.WriteAllText(ConfigPath, json.ToJsonString());
            Manager = new OutputSessionManager(Current, RuntimeConfigurationValidator.LoadAssignments(Current, Root),
                new MetricStore(), _log, (app, target, board, store, logger) =>
                {
                    var session = new Session(target, board.Canvas.Width) { FailStart = FailNextStart };
                    FailNextStart = false;
                    Created.Add(session);
                    return session;
                });
            var settings = new RuntimeOutputConfigurationControl(Manager, () => Current, Root, _log, next =>
            {
                Current = next;
                OnCommit?.Invoke(next);
            });
            Control = new RuntimeOutputControl(Manager, () => "(per-output dashboards)", settings.Execute);
            if (start) Manager.Start();
        }
        public RuntimeIpcResponse Execute(RuntimeIpcCommand command) => Control.Execute(command);
        public RuntimeOutputStatus Status(string id) => Control.GetStatus().Outputs.Single(o => o.TargetId == id);
        public Session Latest(string id) => Created.Last(s => s.TargetId == id);
        public AppConfig Disk() => AppConfig.Load(ConfigPath, _log);
        public JsonNode Json() => JsonNode.Parse(File.ReadAllText(ConfigPath))!;
        public string BoardPath(string name) => Path.Combine(Root, "dashboards", name, "dashboard.json");
        public void Board(string name, int width)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BoardPath(name))!);
            File.WriteAllText(BoardPath(name), $$"""{"schemaVersion":19,"canvas":{"width":{{width}},"height":300,"orientation":0},"widgets":[]}""");
        }
        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class Session(OutputTargetConfig target, int width) : IOutputSession
    {
        public string TargetId => target.Id;
        public string TargetName => target.Name;
        public int Width = width;
        public int Prepares, Commits, Starts, Disposals;
        public bool FailPrepare, FailCommit, FailStart;
        public Action? OnPrepare;
        private bool _suspended;
        public bool IsConnected { get; private set; }
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => _suspended ? "SUSPENDED" : IsConnected ? "DISPLAYING" : "STOPPED";
        public void Start()
        {
            Starts++;
            IsConnected = !_suspended;
            if (FailStart) throw new InvalidOperationException("Synthetic start failure.");
        }
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig app, OutputTargetConfig next, C.DashboardDefinition board)
        {
            Prepares++;
            var action = OnPrepare;
            OnPrepare = null;
            action?.Invoke();
            if (FailPrepare) { FailPrepare = false; throw new InvalidOperationException("Synthetic preparation failure."); }
            return new Plan(board.Canvas.Width);
        }
        public void CommitReconfiguration(IOutputSessionReconfiguration plan)
        {
            Width = ((Plan)plan).Width;
            Commits++;
            if (FailCommit) { FailCommit = false; throw new InvalidOperationException("Synthetic post-mutation commit failure."); }
        }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { _suspended = true; IsConnected = false; }
        public void Resume() { _suspended = false; IsConnected = Starts > 0 && Disposals == 0; }
        public void Dispose() { Disposals++; IsConnected = false; }
        private sealed record Plan(int Width) : IOutputSessionReconfiguration
        {
            public void Dispose() { }
        }
    }
}
