using System.Text.Json;
using System.Text.Json.Nodes;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Production controller/manager with passive discovery, consent and session fakes; no USB or modal UI.
internal static class PhysicalOutputEnablementTests
{
    public static void ConsentAndExactIdentity()
    {
        using (var f = new Fixture())
        {
            var bytes = File.ReadAllBytes(f.ConfigPath);
            f.Consent = false;
            Check(f.Prepare(true) is null && f.ConsentCalls == 1 && f.DiscoveryCalls == 1 &&
                  f.Created.All(s => s.TargetId != "device-a") && File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
                "Declined consent must return no apply operation, persistence or candidate session.");
        }
        foreach (var mode in new[] { "missing", "duplicate binding", "duplicate fingerprint", "discovery error" })
        {
            using var f = new Fixture();
            var bytes = File.ReadAllBytes(f.ConfigPath);
            f.Observed = mode switch
            {
                "missing" => [],
                "duplicate binding" => [Device('a'), Device('a')],
                "duplicate fingerprint" => [Device('a') with { PhysicalFingerprint = "synthetic-shared" },
                    Device('d') with { PhysicalFingerprint = "synthetic-shared" }],
                _ => [Device('a')]
            };
            if (mode == "discovery error") f.OnDiscover = () => throw new InvalidOperationException("Synthetic discovery failure.");
            Failure<InvalidOperationException>(() => f.Prepare(true));
            Check(f.ConsentCalls == 0 && f.Created.All(s => s.TargetId != "device-a") &&
                  File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
                "Missing, ambiguous or failed discovery must reject before consent and never adopt another device.");
        }
        using (var f = new Fixture(start: false, single: true))
        {
            f.Target().DeviceId = string.Empty;
            f.Current.Save(f.ConfigPath);
            f.Observed = [Device('a')];
            Failure<InvalidOperationException>(() => f.Prepare(true));
            Check(f.ConsentCalls == 0 && f.Created.Count == 0,
                "A legacy unbound target must not turn one passive match into an explicit enable binding.");
        }
        foreach (var afterConsent in new[] { false, true })
        {
            using var f = new Fixture();
            if (afterConsent) f.OnConsent = _ => f.Observed = [];
            else f.OnCreate = s => { if (s.TargetId == "device-a") f.Observed = []; };
            Failure<InvalidOperationException>(() => f.Change(true));
            Check(!f.Status().OutputEnabled!.Value && !f.Status().IsActive && !f.Disk().Outputs.Targets[0].DeviceEnabled &&
                  f.Created.Where(s => s.TargetId == "device-a").All(s => s.Starts == 0 && s.Disposals == 1),
                "Loss after consent or renderer preparation must fail before candidate Start and dispose any prepared session.");
        }
        using (var f = new Fixture())
        {
            f.OnConsent = shown => shown.DeviceId = Binding('d');
            Check(f.Change(true).Success && f.Target().DeviceId == Binding('a') && f.Current.Outputs.Targets.Count == 3,
                "The consent presentation receives a detached target; enabling must not rewrite its binding or adopt unconfigured hardware.");
            Failure<InvalidOperationException>(() => f.Prepare(true));
            Failure<InvalidOperationException>(() => f.Prepare(false, "desktop-b"));
        }
    }

    public static void SelectedPersistenceAndIsolation()
    {
        using var f = new Fixture();
        var old = f.Current;
        var b = f.Latest("desktop-b");
        var c = f.Latest("device-c");
        var boards = f.Manager.GetDashboardAssignments();
        var json = f.Json();
        var target = json["Outputs"]!["Targets"]![0]!.AsObject();
        var enabled = target["DeviceEnabled"];
        target.Remove("DeviceEnabled");
        target["deviceenabled"] = enabled;
        target["Dashboard"] = "Pending";
        target["ProviderEnabled"] = true;
        target["JpegQuality"] = 80;
        target["FutureTarget"] = "keep A";
        json["Outputs"]!["Targets"]![1]!["Dashboard"] = "Missing";
        json["Display"]!["RefreshIntervalMs"] = 1700;
        json["Renderer"]!["JpegQuality"] = 75;
        json["MetricProviders"]![MetricProviderContract.System] = false;
        File.WriteAllText(f.ConfigPath, json.ToJsonString());
        var pendingSibling = json["Outputs"]!["Targets"]![1]!.ToJsonString();
        var expected = json.DeepClone();
        expected["Outputs"]!["Targets"]![0]!["deviceenabled"] = true;
        f.Board("A", 333);
        File.WriteAllText(f.BoardPath("B"), "{broken sibling dashboard");
        File.WriteAllText(f.BoardPath("C"), "{broken sibling dashboard");
        Check(f.Change(true).Success && f.Status() is { IsActive: true, OutputEnabled: true, ProviderEnabled: false } &&
              f.Latest("device-a").Width == 333 && !old.Outputs.Targets[0].DeviceEnabled,
            "Enable must load only A's currently assigned saved document and retain its committed Provider and old snapshot.");
        var disk = f.Json();
        var saved = disk["Outputs"]!["Targets"]![0]!.AsObject();
        Check(JsonNode.DeepEquals(expected, disk),
            "The complete saved JSON must differ semantically only in the selected DeviceEnabled boolean.");
        Check(saved["deviceenabled"]!.GetValue<bool>() && !saved.ContainsKey("DeviceEnabled") && saved["Desktop"] is null &&
              saved["Dashboard"]!.GetValue<string>() == "Pending" && saved["ProviderEnabled"]!.GetValue<bool>() &&
              saved["JpegQuality"]!.GetValue<int>() == 80 && saved["FutureTarget"]!.GetValue<string>() == "keep A" &&
              disk["Future"]!["Keep"]!.GetValue<int>() == 42 &&
              disk["Outputs"]!["Targets"]![1]!.ToJsonString() == pendingSibling,
            "Physical enable must patch only the existing root DeviceEnabled key, retaining pending and unknown JSON values.");
        Check(ReferenceEquals(old.Display, f.Current.Display) && ReferenceEquals(old.Renderer, f.Current.Renderer) &&
              ReferenceEquals(old.MetricProviders, f.Current.MetricProviders) &&
              ReferenceEquals(old.Outputs.Targets[1], f.Current.Outputs.Targets[1]) &&
              ReferenceEquals(old.Outputs.Targets[2], f.Current.Outputs.Targets[2]) &&
              ReferenceEquals(boards["desktop-b"], f.Manager.GetDashboardAssignments()["desktop-b"]) &&
              ReferenceEquals(boards["device-c"], f.Manager.GetDashboardAssignments()["device-c"]),
            "A field change must retain common settings and every sibling target/document instance.");
        var discovery = f.DiscoveryCalls;
        var consent = f.ConsentCalls;
        var a = f.Latest("device-a");
        f.Observed = [];
        File.Delete(f.BoardPath("A"));
        Check(f.Change(false).Success && f.Status() is { IsActive: false, OutputEnabled: false } && a.Disposals == 1 &&
              f.DiscoveryCalls == discovery && f.ConsentCalls == consent &&
              b.Prepares == 0 && b.Commits == 0 && b.Disposals == 0 &&
              c.Prepares == 0 && c.Commits == 0 && c.Disposals == 0,
            "Disable must unload only A without discovery, consent, document loading or sibling reconfiguration.");
        Check(f.Json()["Outputs"]!["Targets"]![1]!.ToJsonString() == pendingSibling,
            "Disabling must also retain unrelated pending saved edits.");
    }

    public static void ConcurrentInputGuards()
    {
        foreach (var changed in new[] { "binding", "enablement" })
        {
            using var f = new Fixture();
            var json = f.Json();
            if (changed == "binding") json["Outputs"]!["Targets"]![0]!["DeviceId"] = Binding('d');
            else json["Outputs"]!["Targets"]![0]!["DeviceEnabled"] = true;
            var text = json.ToJsonString();
            File.WriteAllText(f.ConfigPath, text);
            Failure<InvalidOperationException>(() => f.Prepare(true));
            Check(f.ConsentCalls == 0 && File.ReadAllText(f.ConfigPath) == text && f.Created.All(s => s.TargetId != "device-a"),
                "A disk binding or requested-field drift must reject before consent without overwriting the saved edit.");
        }
        using (var f = new Fixture())
        {
            var json = f.Json();
            json["Outputs"]!["Targets"]![0]!["DEVICEENABLED"] = false;
            File.WriteAllText(f.ConfigPath, json.ToJsonString());
            Failure<InvalidDataException>(() => f.Prepare(true));
            Check(f.ConsentCalls == 0, "Case-duplicate requested properties must not be patched ambiguously.");
        }
        foreach (var point in new[] { "consent", "candidate", "start" })
        {
            using var f = new Fixture();
            var newer = f.Json();
            newer["Future"]!["Keep"] = 99;
            var text = newer.ToJsonString();
            void WriteNewer() => File.WriteAllText(f.ConfigPath, text);
            if (point == "consent") f.OnConsent = _ => WriteNewer();
            else f.OnCreate = s =>
            {
                if (s.TargetId != "device-a") return;
                if (point == "candidate") WriteNewer();
                else s.OnStart = WriteNewer;
            };
            Failure<InvalidOperationException>(() => f.Change(true));
            Check(File.ReadAllText(f.ConfigPath) == text && f.Status() is { IsActive: false, OutputEnabled: false } &&
                  f.Created.Where(s => s.TargetId == "device-a").All(s => s.Disposals == 1 && s.Starts == (point == "start" ? 1 : 0)),
                "Intervening writers must survive rejection/recovery, including the unavoidable interval after the last pre-Start check.");
            Check(Directory.GetFiles(Path.GetDirectoryName(f.ConfigPath)!, "*.tmp").Length == 0,
                "Rejected physical writes must release temporary files.");
        }
        foreach (var replaceRoot in new[] { false, true })
        {
            using var f = new Fixture();
            var bytes = File.ReadAllBytes(f.ConfigPath);
            f.OnConsent = _ =>
            {
                if (replaceRoot) f.Current = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(f.Current))!;
                else f.Target().DeviceId = Binding('d');
            };
            Failure<InvalidOperationException>(() => f.Change(true));
            Check(File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && f.Created.All(s => s.TargetId != "device-a"),
                "Runtime replacement or in-place selected binding drift during modal consent must reject the stale callback.");
        }
    }

    public static void ApplyAndRecoveryFailures()
    {
        using (var f = new Fixture())
        {
            var bytes = File.ReadAllBytes(f.ConfigPath);
            File.Delete(f.BoardPath("A"));
            Failure<InvalidDataException>(() => f.Change(true));
            Check(f.Created.All(s => s.TargetId != "device-a") && File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
                "A missing required board must fail without a candidate, fallback document or configuration write.");
            f.Board("A", 300);
            f.FailNextStart = true;
            Failure<InvalidOperationException>(() => f.Change(true));
            Check(f.Latest("device-a").Disposals == 1 && !f.Status().IsActive &&
                  File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes),
                "Failed candidate Start must release the session and retain original file/runtime state.");
            var previous = f.Current;
            f.OnCommit = next =>
            {
                if (next.Outputs.Targets[0].DeviceEnabled) throw new InvalidOperationException("Synthetic common-demand failure.");
            };
            Failure<InvalidOperationException>(() => f.Change(true));
            Check(ReferenceEquals(previous, f.Current) && !f.Status().IsActive && !f.Target().DeviceEnabled &&
                  File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) && f.Latest("device-a").Disposals == 1,
                "Failure after persistence must restore the old live/config snapshot and exact original bytes.");
            f.OnCommit = null;
            Check(f.Change(true).Success, "A recovered enable transition must remain retryable.");
            var active = f.Latest("device-a");
            bytes = File.ReadAllBytes(f.ConfigPath);
            f.OnCommit = next =>
            {
                if (!next.Outputs.Targets[0].DeviceEnabled) throw new InvalidOperationException("Synthetic disable commit failure.");
            };
            Failure<InvalidOperationException>(() => f.Change(false));
            Check(f.Status() is { IsActive: true, OutputEnabled: true } && active.Disposals == 0 &&
                  File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes) &&
                  f.Latest("desktop-b").Prepares == 0 && f.Latest("device-c").Prepares == 0,
                "Failed disable must retain the original active session instead of disposing it or reloading siblings.");
        }
        foreach (var newerWriter in new[] { false, true })
        {
            using var f = new Fixture();
            var bytes = File.ReadAllBytes(f.ConfigPath);
            var newer = f.Json();
            newer["Future"]!["Keep"] = 123;
            var text = newer.ToJsonString();
            f.OnCommit = next =>
            {
                if (newerWriter && !next.Outputs.Targets[0].DeviceEnabled) return;
                if (newerWriter) File.WriteAllText(f.ConfigPath, text);
                throw new InvalidOperationException("Synthetic apply/recovery failure.");
            };
            var error = Failure<AggregateException>(() => f.Change(true));
            Check(error.Message.Contains("rollback was incomplete", StringComparison.OrdinalIgnoreCase) &&
                  !f.Status().IsActive && !f.Target().DeviceEnabled && f.Latest("device-a").Disposals == 1 &&
                  (newerWriter ? File.ReadAllText(f.ConfigPath) == text : File.ReadAllBytes(f.ConfigPath).SequenceEqual(bytes)) &&
                  Directory.GetFiles(Path.GetDirectoryName(f.ConfigPath)!, "*.tmp").Length == 0,
                "Incomplete disk or Runtime recovery must remain explicit while retaining newer content and releasing the candidate.");
        }
    }

    public static void LifetimeFeedbackAndIpcBoundary()
    {
        using (var f = new Fixture(start: false))
        {
            Check(f.Change(true).Success && f.Created.Count == 0 && !f.Status().IsActive && f.Target().DeviceEnabled,
                "Physical Enabled can persist during stopped lifetime without creating or waking a device session.");
            f.Manager.Suspend();
            f.Manager.Start();
            Check(f.Latest("device-a").Calls.SequenceEqual(new[] { "Suspend", "Start" }) &&
                  f.Status() is { IsActive: true, IsConnected: false, UsbState: "SUSPENDED" },
                "Normal global startup during suspend must suspend the candidate before Start.");
            f.Change(false);
            f.Change(true);
            Check(f.Latest("device-a").Calls.SequenceEqual(new[] { "Suspend", "Start" }) && !f.Status().IsConnected,
                "Target enable must preserve current suspension rather than providing a manual wake path.");
            f.Manager.Resume();
            Check(f.Status().IsConnected, "Global resume restores the enabled fake transport only through its lifecycle owner.");
            f.Manager.StopAll();
            var count = f.Created.Count;
            f.Change(false);
            f.Change(true);
            Check(f.Created.Count == count && f.Status() is { IsActive: false, OutputEnabled: true, UsbState: "STOPPED" } &&
                  !f.Control.ShouldExitRuntimeAfter(new(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "device-a", Value: false)),
                "Field changes must retain stopped global lifetime and must not apply the global shutdown policy.");
        }
        using (var f = new Fixture(single: true))
        {
            f.Change(true); f.Change(false); f.Change(true);
            Check(f.Status().IsActive && f.Created.Count == 2 && f.Created[0].Disposals == 1,
                "Disabling the final physical Output must leave Runtime's running lifetime available for re-enable.");
        }
        using (var f = new Fixture())
        {
            var command = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "device-a", Value: true);
            Check(!f.Control.Execute(command).Success &&
                  RuntimeIpcProtocol.ParseCommand("PHYSICAL_SET_DEVICE_ENABLED\t{\"TargetId\":\"device-a\",\"Value\":true}").Kind == RuntimeIpcCommandKind.Invalid,
                "The internal feedback kind must not become an unrestricted Runtime command or IPC verb.");
            Failure<InvalidOperationException>(() => f.Settings.Execute(command));
            var replacement = JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(f.Target()))!;
            replacement.DeviceEnabled = true;
            Failure<InvalidOperationException>(() => f.Manager.ReconfigureTarget(replacement,
                RuntimeConfigurationValidator.LoadTarget(replacement, f.Root), _ => { }));
            Check(f.ConsentCalls == 0 && f.Created.All(s => s.TargetId != "device-a"),
                "The generic field route must keep its physical ownership guard.");
            var messages = new List<(string Text, RuntimeNotificationKind Kind)>();
            var diagnostics = new List<string>();
            var feedback = new RuntimeCommandFeedback(
                (text, kind) => messages.Add((text, kind)),
                _ => { }, _ => { },
                (message, _) => diagnostics.Add(message));
            var c = f.Latest("device-c");
            c.IsConnected = false;
            feedback.Execute(new(RuntimeIpcCommandKind.StartOrReloadTarget, "device-c"),
                () => new(true, "Synthetic accepted reload."), f.Control.GetStatus);
            var apply = f.Prepare(true)!;
            Check(feedback.Execute(command, apply, f.Control.GetStatus).Success && messages.Count == 2 &&
                  messages[0].Kind == RuntimeNotificationKind.Warning && messages[1].Kind == RuntimeNotificationKind.Information,
                "The physical transition must report its own observed result without adopting the pending sibling.");
            c.IsConnected = true;
            feedback.Observe(f.Control.GetStatus());
            feedback.Observe(f.Control.GetStatus());
            Check(messages.Count == 3 && diagnostics.Count == 3 &&
                  messages[2].Text.Contains("Physical C", StringComparison.Ordinal) &&
                  !messages[2].Text.Contains("(device-c)", StringComparison.Ordinal) &&
                  diagnostics[2].Contains("(device-c)", StringComparison.Ordinal) &&
                  messages[2].Kind == RuntimeNotificationKind.Information,
                "A physical field change must preserve unrelated pending completion, while keeping the target ID in diagnostics instead of the banner.");
        }
    }

    private static string Binding(char token) => "trofeo:" + new string(token, 64);
    private static OutputDeviceDescriptor Device(char token) =>
        new(Binding(token), OutputTargetContract.TrofeoTransport, "Synthetic device", "synthetic-interface-" + token);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static T Failure<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-PhysicalEnable-" + Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Root, "config", "app.json");
        public AppConfig Current;
        public readonly OutputSessionManager Manager;
        public readonly RuntimeOutputConfigurationControl Settings;
        public readonly RuntimeOutputControl Control;
        public readonly List<Session> Created = [];
        public IReadOnlyList<OutputDeviceDescriptor> Observed = [Device('a'), Device('c'), Device('e')];
        public bool Consent = true, FailNextStart;
        public int ConsentCalls, DiscoveryCalls;
        public Action<OutputTargetConfig>? OnConsent;
        public Action? OnDiscover;
        public Action<Session>? OnCreate;
        public Action<AppConfig>? OnCommit;
        private readonly FileLogger _log;

        public Fixture(bool start = true, bool single = false)
        {
            Directory.CreateDirectory(Root);
            _log = new FileLogger(Path.Combine(Root, "test.log"));
            Current = new AppConfig { Outputs = new OutputConfig { Targets =
            [
                new() { Id = "device-a", Name = "Physical A", DeviceId = Binding('a'), Dashboard = "A", RefreshIntervalMs = 250 },
                new() { Id = "desktop-b", Name = "Desktop B", Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport, Dashboard = "B", Desktop = new() },
                new() { Id = "device-c", Name = "Physical C", DeviceId = Binding('c'), Dashboard = "C", DeviceEnabled = true }
            ] } };
            if (single) Current.Outputs.Targets.RemoveRange(1, 2);
            Current.Save(ConfigPath);
            Board("A", 300); Board("B", 400); Board("C", 500);
            var json = Json();
            json["Future"] = new JsonObject { ["Keep"] = 42 };
            File.WriteAllText(ConfigPath, json.ToJsonString());
            Manager = new OutputSessionManager(Current, RuntimeConfigurationValidator.LoadEnabledAssignments(Current, Root),
                new MetricStore(), _log, (app, target, board, store, logger) =>
                {
                    var session = new Session(target, board.Canvas.Width) { FailStart = FailNextStart };
                    FailNextStart = false;
                    Created.Add(session);
                    OnCreate?.Invoke(session);
                    return session;
                });
            Settings = new RuntimeOutputConfigurationControl(Manager, () => Current, Root, _log, next =>
            {
                Current = next;
                OnCommit?.Invoke(next);
            });
            Control = new RuntimeOutputControl(Manager, () => "(per-output dashboards)", Settings.Execute);
            if (start) Manager.Start();
        }
        public OutputTargetConfig Target(string id = "device-a") => Current.Outputs.Targets.Single(t => t.Id == id);
        public RuntimeOutputStatus Status(string id = "device-a") => Control.GetStatus().Outputs.Single(o => o.TargetId == id);
        public Session Latest(string id) => Created.Last(s => s.TargetId == id);
        public AppConfig Disk() => AppConfig.Load(ConfigPath, _log);
        public JsonNode Json() => JsonNode.Parse(File.ReadAllText(ConfigPath))!;
        public Func<RuntimeIpcResponse>? Prepare(bool enabled, string id = "device-a") =>
            Settings.PreparePhysicalEnablement(id, enabled, shown =>
            {
                ConsentCalls++; OnConsent?.Invoke(shown); return Consent;
            }, () =>
            {
                DiscoveryCalls++; OnDiscover?.Invoke(); return Observed;
            });
        public RuntimeIpcResponse Change(bool enabled) =>
            (Prepare(enabled) ?? throw new InvalidOperationException("Synthetic consent was declined."))();
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
        public int Width = width, Starts, Disposals, Prepares, Commits;
        public bool FailStart;
        public Action? OnStart;
        public List<string> Calls = [];
        private bool _suspended;
        public bool IsConnected { get; set; }
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => _suspended ? "SUSPENDED" : Starts > 0 && Disposals == 0
            ? IsConnected ? target.Kind == OutputTargetContract.DesktopKind ? "DISPLAYING" : "CONNECTED" : "WAITING"
            : "STOPPED";
        public void Start()
        {
            Calls.Add("Start"); Starts++; IsConnected = !_suspended; OnStart?.Invoke();
            if (FailStart) throw new InvalidOperationException("Synthetic candidate Start failure.");
        }
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig app, OutputTargetConfig next, C.DashboardDefinition board)
        {
            Prepares++; return new Plan(board.Canvas.Width);
        }
        public void CommitReconfiguration(IOutputSessionReconfiguration plan) { Commits++; Width = ((Plan)plan).Width; }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() { Calls.Add("Suspend"); _suspended = true; IsConnected = false; }
        public void Resume() { Calls.Add("Resume"); _suspended = false; IsConnected = Starts > 0 && Disposals == 0; }
        public void Dispose() { Disposals++; IsConnected = false; }
        private sealed record Plan(int Width) : IOutputSessionReconfiguration { public void Dispose() { } }
    }
}
