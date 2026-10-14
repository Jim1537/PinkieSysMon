using System.Text.Json.Nodes;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Saved files plus the actual IPC/controller/manager transaction; sessions are
// deterministic substitutes and never create native windows or USB transports.
internal static class SavedOutputReloadTests
{
    public static void SavedSettingsIsolation()
    {
        using var f = new Fixture();
        var previous = f.Current;
        var sibling = f.Session("b");
        var siblingBoard = f.Manager.GetDashboardAssignments()["b"];
        f.Edit(root =>
        {
            Target(root, "a")["Name"] = "Edited A";
            Target(root, "a")["RefreshIntervalMs"] = 240;
            Target(root, "a")["ProviderEnabled"] = true;
            Target(root, "a")["Desktop"]!["Lock"] = false;
            Target(root, "a")["Desktop"]!["X"] = -180;
            Target(root, "a")["Desktop"]!["Y"] = 260;
            Target(root, "a")["Desktop"]!["ZOrder"] = 7;
            Target(root, "b")["Name"] = "Pending B";
            Target(root, "b")["Dashboard"] = "MissingSibling";
            Target(root, "b")["RefreshIntervalMs"] = 870;
            root["MetricProviders"]![MetricProviderContract.System] = false;
            root["Display"]!["RefreshIntervalMs"] = 920;
            root["Media"]!["EndpointTypeOverrides"] = new JsonObject { ["synthetic-endpoint"] = "headphones" };
        });
        f.Board("Same", 540);
        var bytes = File.ReadAllBytes(f.Path);
        var response = f.Reload("a");
        var status = f.Status("a");
        Check(response.Success && status.TargetName == "Edited A" && status.RefreshIntervalMs == 240 &&
              status.ProviderEnabled == true && status.Lock == false && f.Session("a").Width == 540 &&
              f.Session("a").Target.Desktop is { X: -180, Y: 260, ZOrder: 7 },
            "Reload must apply the selected saved settings and independently loaded shared dashboard.");
        Check(ReferenceEquals(f.Current.Display, previous.Display) &&
              ReferenceEquals(f.Current.Media, previous.Media) &&
              ReferenceEquals(f.Current.MetricProviders, previous.MetricProviders) &&
              ReferenceEquals(f.Current.Outputs.Targets[1], previous.Outputs.Targets[1]) &&
              ReferenceEquals(f.Manager.GetDashboardAssignments()["b"], siblingBoard) &&
              sibling.Width == 320 && sibling.Prepares == 0 && sibling.Commits == 0 && sibling.Disposals == 0 &&
              f.Created.Count == 3 && File.ReadAllBytes(f.Path).SequenceEqual(bytes),
            "Selected reload must preserve current common settings, sibling objects/session/document and every saved pending/unknown value.");
        Check(Equals(f.Metrics.Snapshot()[OutputMetricContract.Id("a", OutputMetricContract.Fps)], 37),
            "Saved per-target Provider Enabled must take effect without depending on a global provider change.");
    }

    public static void PhysicalDefaultsAndOwnership()
    {
        using var f = new Fixture();
        var previous = f.Current;
        f.Edit(root =>
        {
            root["Dashboard"]!["Active"] = "Inherited";
            root["Display"]!["RefreshIntervalMs"] = 730;
            root["Renderer"]!["JpegQuality"] = 81;
            root["Usb"]!["RetryIntervalMs"] = 2200;
            root["Usb"]!["TransferTimeoutMs"] = 3300;
            root["Usb"]!["DiagnosticJournalEnabled"] = true;
            var target = Target(root, "c");
            target["Dashboard"] = null;
            target["RefreshIntervalMs"] = null;
            target["JpegQuality"] = null;
            target["Usb"] = null;
            target["ProviderEnabled"] = false;
        });
        var bytes = File.ReadAllBytes(f.Path);
        Check(f.Reload("c").Success && f.Session("c").Width == 640 &&
              f.Session("c").Target is { Dashboard: "Inherited", RefreshIntervalMs: 730, JpegQuality: 81,
                  Usb: { RetryIntervalMs: 2200, TransferTimeoutMs: 3300, DiagnosticJournalEnabled: true } } &&
              ReferenceEquals(f.Current.Renderer, previous.Renderer) && ReferenceEquals(f.Current.Usb, previous.Usb) &&
              f.Current.Display.RefreshIntervalMs == 1000 && f.Created.Count == 3 &&
              f.Session("a").Prepares == 0 && f.Session("b").Prepares == 0 &&
              f.Metrics.Snapshot()[OutputMetricContract.Id("c", OutputMetricContract.Fps)] is null &&
              File.ReadAllBytes(f.Path).SequenceEqual(bytes),
            "Physical reload must use selected inherited effective values while preserving shared defaults, consent, siblings and disk bytes.");

        f.Edit(root => Target(root, "c")["DeviceId"] = "trofeo:" + new string('c', 64));
        var commits = f.Session("c").Commits;
        Throws<InvalidOperationException>(() => f.Reload("c"));
        Check(f.Session("c").Commits == commits && f.Created.Count == 3,
            "Reload must reject a changed physical binding before any session or hardware acquisition.");

        // A disabled live target remains disabled even if the saved file asks to enable it.
        f.Edit(root => Target(root, "d")["DeviceEnabled"] = true);
        Throws<InvalidOperationException>(() => f.Reload("d"));
        Check(f.Created.All(s => s.TargetId != "d") && f.Status("d").OutputEnabled == false,
            "A reload request cannot turn saved enablement into new physical-device consent.");

        using var disabledOnDisk = new Fixture();
        disabledOnDisk.Edit(root => Target(root, "a")["Desktop"]!["BoardEnabled"] = false);
        Throws<InvalidOperationException>(() => disabledOnDisk.Reload("a"));
        Check(disabledOnDisk.Status("a").IsActive && disabledOnDisk.Status("a").OutputEnabled == true &&
              disabledOnDisk.Session("a").Prepares == 0,
            "Enablement changes must use the configuration path instead of a reload silently changing lifecycle policy.");
    }

    public static void ValidationAndReadOnlyFailures()
    {
        foreach (var edit in new Action<JsonNode>[]
        {
            root => Target(root, "a")["RefreshIntervalMs"] = 1,
            root => Target(root, "a")["Name"] = "",
            root => Target(root, "a")["Dashboard"] = "Missing",
            root => ((JsonArray)root["Outputs"]!["Targets"]!).Add(Target(root, "a").DeepClone()),
            root => ((JsonArray)root["Outputs"]!["Targets"]!).RemoveAt(0)
        })
        {
            using var f = new Fixture();
            f.Edit(edit);
            var bytes = File.ReadAllBytes(f.Path);
            var response = f.ReloadObserved("a");
            Check(!response.Success && f.Current.Outputs.Targets[0].Name == "A" && f.Session("a").Width == 320 &&
                  f.Session("a").Prepares == 0 && f.Session("b").Prepares == 0 &&
                  f.Created.Count == 3 && File.ReadAllBytes(f.Path).SequenceEqual(bytes),
                "Invalid saved settings, missing target or missing dashboard must fail before mutation without rewriting pending configuration.");
        }
        using var malformed = new Fixture();
        File.WriteAllText(malformed.Path, "{ broken");
        Check(!malformed.ReloadObserved("a").Success && malformed.Session("a").Prepares == 0,
            "Malformed app.json must fail visibly without substituting application defaults.");
        using var brokenBoard = new Fixture();
        brokenBoard.Board("Same", 0);
        var failed = brokenBoard.ReloadObserved("a");
        Check(!failed.Success && failed.Message.Contains("Same", StringComparison.Ordinal) &&
              brokenBoard.Session("a").Width == 320 && brokenBoard.Session("b").Width == 320,
            "A corrupt selected dashboard must identify its assignment and preserve both loaded copies.");
        File.Delete(brokenBoard.Path);
        Check(!brokenBoard.ReloadObserved("a").Success && brokenBoard.Session("a").Prepares == 0,
            "A missing saved application file must not become a default-backed reload.");
    }

    public static void RollbackAndConcurrentWrite()
    {
        foreach (var failure in new[] { "prepare", "session commit", "runtime commit" })
        {
            using var f = new Fixture();
            var previous = f.Current;
            f.Edit(root => Target(root, "a")["Name"] = "Saved A");
            f.Board("Same", 560);
            var bytes = File.ReadAllBytes(f.Path);
            f.Session("a").FailPrepare = failure == "prepare";
            f.Session("a").FailCommit = failure == "session commit";
            var rejected = false;
            f.OnCommit = _ =>
            {
                if (failure == "runtime commit" && !rejected)
                {
                    rejected = true;
                    throw new IOException("Synthetic Runtime commit failure.");
                }
            };
            var result = f.ReloadObserved("a");
            Check(!result.Success && ReferenceEquals(f.Current, previous) && f.Status("a").TargetName == "A" &&
                  f.Session("a").Width == 320 && f.Session("b").Prepares == 0 &&
                  f.Session("b").Commits == 0 && f.Session("a").Disposals == 0 &&
                  f.Session("a").Plans.All(p => p.Disposals == 1) && File.ReadAllBytes(f.Path).SequenceEqual(bytes),
                "Prepare, post-mutation session and composition-root failures must recover the selected state, release plans and retain saved edits.");
        }
        using var newer = new Fixture();
        newer.Edit(root => Target(root, "a")["Name"] = "Saved A");
        newer.Session("a").OnPrepare = () => newer.Edit(root => root["Future"]!["Keep"] = 99);
        var response = newer.ReloadObserved("a");
        Check(!response.Success && response.Message.Contains("newer file was retained", StringComparison.Ordinal) &&
              newer.Session("a").Commits == 0 && newer.Session("a").Plans.All(p => p.Disposals == 1) &&
              JsonNode.Parse(File.ReadAllText(newer.Path))!["Future"]!["Keep"]!.GetValue<int>() == 99 &&
              newer.Session("b").Prepares == 0,
            "A detected intervening write must reject reload before session commit and preserve the newer file.");

        using var recovery = new Fixture();
        recovery.OnCommit = _ => throw new IOException("Synthetic recovery failure.");
        response = recovery.ReloadObserved("a");
        Check(!response.Success && response.Message.Contains("recovery was incomplete", StringComparison.Ordinal),
            "A failed recovery callback must report incomplete recovery instead of implying full rollback.");
    }

    public static void LifecycleAndFeedback()
    {
        const string opaque = "panel\t\"quoted\"";
        using var f = new Fixture(opaque);
        f.Manager.Suspend();
        f.Board("Same", 620);
        var response = f.ReloadObserved(opaque);
        Check(response.Success && f.Session(opaque).Width == 620 && f.Status(opaque).IsVisible == false &&
              f.Events.Single().Kind == RuntimeNotificationKind.Warning &&
              response.Message.Contains("SUSPENDED", StringComparison.Ordinal) &&
              f.Session("b").Prepares == 0 && f.Created.Count == 3,
            "The existing target wire request must preserve opaque identity and report reload under real suspension without claiming visibility.");
        f.Manager.Resume();
        f.Feedback.Observe(f.Control.GetStatus());
        Check(f.Events.Count == 2 && f.Events[^1].Kind == RuntimeNotificationKind.Information,
            "Existing observed-readiness feedback must complete the suspended reload after actual fixture visibility resumes.");
        f.Manager.StopAll();
        var commits = f.Session(opaque).Commits;
        response = f.ReloadObserved(opaque);
        Check(!response.Success && f.Session(opaque).Commits == commits &&
              f.Created.Count == 3 && f.Control.GetStatus().Outputs.All(o => !o.IsActive),
            "A target request during global shutdown must not create sessions or bypass the global lifetime gate.");
        var unavailable = new RuntimeOutputControl(f.Manager, () => "Summary");
        Check(!unavailable.Execute(new(RuntimeIpcCommandKind.StartOrReloadTarget, opaque)).Success,
            "A missing saved-configuration controller must fail rather than fall back to board-only reloading.");
    }

    private static JsonObject Target(JsonNode root, string id) => root["Outputs"]!["Targets"]!.AsArray()
        .OfType<JsonObject>().Single(t => t["Id"]!.GetValue<string>() == id);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PinkieSysMon-SavedReload-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "config", "app.json");
        public AppConfig Current;
        public readonly MetricStore Metrics = new();
        public readonly List<Session> Created = [];
        public readonly OutputSessionManager Manager;
        public readonly RuntimeOutputControl Control;
        public readonly RuntimeCommandFeedback Feedback;
        public readonly List<(string Message, RuntimeNotificationKind Kind)> Events = [];
        public Action<AppConfig>? OnCommit;
        public Fixture(string firstId = "a")
        {
            Directory.CreateDirectory(Root);
            var log = new FileLogger(System.IO.Path.Combine(Root, "test.log"));
            Current = new AppConfig { Outputs = new OutputConfig { Targets =
            [
                new() { Id = firstId, Name = "A", Kind = OutputTargetContract.DesktopKind, Dashboard = "Same", Desktop = new() },
                new() { Id = "b", Name = "B", Kind = OutputTargetContract.DesktopKind, Dashboard = "Same", ProviderEnabled = true, Desktop = new() },
                new() { Id = "c", Name = "C", Dashboard = "Hardware", DeviceEnabled = true, ProviderEnabled = true,
                    DeviceId = "trofeo:" + new string('a', 64) },
                new() { Id = "d", Name = "Disabled D", Dashboard = "Disabled", ProviderEnabled = true,
                    DeviceId = "trofeo:" + new string('b', 64) }
            ] } };
            Current.Save(Path);
            Edit(root => root["Future"] = new JsonObject { ["Keep"] = 42 });
            Board("Same", 320); Board("Hardware", 480); Board("Disabled", 500); Board("Inherited", 640);
            Manager = new OutputSessionManager(Current, RuntimeConfigurationValidator.LoadAssignments(Current, Root), Metrics, log,
                (app, target, board, store, logger) =>
                {
                    var session = new Session(target, board.Canvas.Width, store);
                    Created.Add(session);
                    return session;
                });
            var settings = new RuntimeOutputConfigurationControl(Manager, () => Current, Root, log, next =>
            {
                Current = next;
                OnCommit?.Invoke(next);
            });
            Control = new RuntimeOutputControl(Manager, () => "Summary", settings.Execute);
            Feedback = new((message, kind) => Events.Add((message, kind)), _ => { }, _ => { });
            Manager.Start();
        }
        public Session Session(string id) => Created.Single(s => s.TargetId == id);
        public RuntimeOutputStatus Status(string id) => Control.GetStatus().Outputs.Single(o => o.TargetId == id);
        private static RuntimeIpcCommand Request(string id) => RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, id));
        public RuntimeIpcResponse Reload(string id) => Control.Execute(Request(id));
        public RuntimeIpcResponse ReloadObserved(string id)
        {
            var command = Request(id);
            return Feedback.Execute(command, () => Control.Execute(command), Control.GetStatus);
        }
        public void Edit(Action<JsonNode> edit)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path))!;
            edit(json);
            File.WriteAllText(Path, json.ToJsonString());
        }
        public void Board(string name, int width)
        {
            var path = System.IO.Path.Combine(Root, "dashboards", name, "dashboard.json");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $$"""{"schemaVersion":19,"canvas":{"width":{{width}},"height":300,"orientation":0},"widgets":[]}""");
        }
        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class Session(OutputTargetConfig target, int width, MetricStore metrics) : IOutputSession
    {
        public OutputTargetConfig Target = target;
        public string TargetId => Target.Id;
        public string TargetName => Target.Name;
        public int Width = width, Prepares, Commits, Disposals;
        public bool FailPrepare, FailCommit;
        public Action? OnPrepare;
        public readonly List<Plan> Plans = [];
        private bool _started, _suspended;
        public bool IsConnected => _started && !_suspended && Disposals == 0;
        public long SentFrames => 7;
        public double LastFrameMs => 3.5;
        public int LastJpegBytes => 128;
        public string UsbState => _suspended ? "SUSPENDED" : IsConnected ? "DISPLAYING" : "STOPPED";
        public void Start() => _started = true;
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig app, OutputTargetConfig next, C.DashboardDefinition board)
        {
            Prepares++;
            var action = OnPrepare;
            OnPrepare = null;
            action?.Invoke();
            if (FailPrepare) { FailPrepare = false; throw new InvalidOperationException("Synthetic preparation failure."); }
            var plan = new Plan(next, board.Canvas.Width);
            Plans.Add(plan);
            return plan;
        }
        public void CommitReconfiguration(IOutputSessionReconfiguration value)
        {
            var plan = (Plan)value;
            Target = plan.Target; Width = plan.Width; Commits++;
            if (FailCommit) { FailCommit = false; throw new InvalidOperationException("Synthetic post-mutation commit failure."); }
        }
        public void SetRuntimeMetricsPublisher(bool enabled)
        {
            if (enabled) metrics.Publish(OutputMetricContract.Id(TargetId, OutputMetricContract.Fps), 37);
            else metrics.Publish(OutputMetricContract.Unavailable(Target));
        }
        public void Suspend() => _suspended = true;
        public void Resume() => _suspended = false;
        public void Dispose() => Disposals++;
        public sealed record Plan(OutputTargetConfig Target, int Width) : IOutputSessionReconfiguration
        {
            public int Disposals;
            public void Dispose() => Disposals++;
        }
    }
}
