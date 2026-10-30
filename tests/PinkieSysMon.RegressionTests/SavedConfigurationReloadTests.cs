using System.Text.Json;
using System.Text.Json.Nodes;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Real saved configuration, controller, manager and telemetry scheduler. Only
// sessions and metric acquisition are substitutes; no native window or USB I/O.
internal static class SavedConfigurationReloadTests
{
    private const string Left = "system.reload.left", Right = "system.reload.right";

    public static void CommonSettingsAndEnabledDocuments()
    {
        using var f = new Fixture();
        f.Edit(root =>
        {
            root["Display"]!["RefreshIntervalMs"] = 720;
            root["Renderer"]!["JpegQuality"] = 82;
            root["Media"]!["EndpointTypeOverrides"] = new JsonObject { ["synthetic-endpoint"] = "headphones" };
            Target(root, "a")["Name"] = "Saved A";
            Target(root, "a")["RefreshIntervalMs"] = 230;
            Target(root, "c")["JpegQuality"] = 77;
        });
        f.Board("Same", 540, Left);
        f.Policy(leftEnabled: false, interval: 900);
        var appBytes = File.ReadAllBytes(f.AppPath);
        var telemetryBytes = File.ReadAllBytes(f.TelemetryPath);
        var response = f.Reload();
        var boards = f.Manager.GetDashboardAssignments();
        Check(response.Success && f.Current.App.Display.RefreshIntervalMs == 720 &&
              f.Current.App.Renderer.JpegQuality == 82 && f.Current.Telemetry.Metrics[Left].IntervalMs == 900 &&
              f.Session("a").Target is { Name: "Saved A", RefreshIntervalMs: 230 } &&
              f.Session("c").Target.JpegQuality == 77 && f.Media["synthetic-endpoint"] == "headphones" &&
              f.Session("a").Width == 540 && f.Session("e").Width == 540 &&
              !ReferenceEquals(boards["a"], boards["e"]) && boards.Count == 3 &&
              f.Created.Count == 3 && f.Manager.GetStatuses().Where(s => !s.OutputEnabled).All(s => !s.IsActive) &&
              f.Metrics.Snapshot()[Left] is null && Equals(f.Metrics.Snapshot()[Right], 42d) &&
              File.ReadAllBytes(f.AppPath).SequenceEqual(appBytes) && File.ReadAllBytes(f.TelemetryPath).SequenceEqual(telemetryBytes),
            "All must apply common and selected saved settings, real telemetry policy, independent enabled documents and media without loading disabled missing/corrupt boards or rewriting files.");
        f.Edit(root => root["MetricProviders"]![MetricProviderContract.System] = false);
        Check(f.Reload().Success && f.Metrics.Snapshot()[Right] is null &&
              f.Metrics.Snapshot()[RuntimeMetricContract.Version] is null &&
              f.Manager.GetStatuses().Count(s => s.IsActive) == 3,
            "A saved global provider change must affect shared telemetry/version without controlling Output enablement.");
    }

    public static void EmptyEnabledSetAndMissingTelemetry()
    {
        using var f = new Fixture();
        File.Delete(f.TelemetryPath);
        f.Edit(root =>
        {
            foreach (var target in root["Outputs"]!["Targets"]!.AsArray().OfType<JsonObject>())
            {
                if (target["Kind"]!.GetValue<string>() == OutputTargetContract.DesktopKind)
                    target["Desktop"]!["BoardEnabled"] = false;
                else target["DeviceEnabled"] = false;
                target["Dashboard"] = "MissingDisabled";
            }
            root["Display"]!["RefreshIntervalMs"] = 680;
        });
        var saved = File.ReadAllBytes(f.AppPath);
        var response = f.ReloadObserved();
        Check(response.Success && response.Message.Contains("Common settings reloaded; no enabled Output", StringComparison.Ordinal) &&
              f.Current.App.Display.RefreshIntervalMs == 680 && f.Current.Telemetry.Metrics[Left].Enabled &&
              f.Manager.GetDashboardAssignments().Count == 0 && f.Manager.GetRequiredMetrics().Count == 0 &&
              f.Manager.GetStatuses().All(s => !s.IsActive && !s.OutputEnabled) &&
              f.Created.All(s => s.Disposals == 1) && f.Created.Count == 3 && !File.Exists(f.TelemetryPath) &&
              File.ReadAllBytes(f.AppPath).SequenceEqual(saved),
            "Zero enabled Outputs must still apply common settings, dispose prior sessions and use established optional telemetry defaults without creating files or dummy boards.");
        Check(f.Reload().Success && f.Created.Count == 3,
            "Repeating common-only All must not acquire any disabled Output resources.");
        using var startup = new OutputSessionManager(f.Current.App, f.Manager.GetDashboardAssignments(), new MetricStore(), f.Log,
            (app, target, board, store, log) => throw new InvalidOperationException("Disabled startup must not acquire a session."));
        startup.Start();
        Check(startup.GetStatuses().All(s => !s.IsActive && !s.OutputEnabled) && startup.GetDashboardAssignments().Count == 0,
            "Initial startup with no enabled Outputs must accept an empty loaded-document map without fallback or resource acquisition.");
    }

    public static void ValidationBeforeMutation()
    {
        foreach (var edit in new Action<JsonNode>[]
        {
            root => Target(root, "a")["Dashboard"] = "MissingEnabled",
            root => Target(root, "b")["Dashboard"] = "../invalid",
            root => Target(root, "b")["RefreshIntervalMs"] = 1,
            root => Target(root, "d")["DeviceId"] = "invalid-binding",
            root => ((JsonArray)root["Outputs"]!["Targets"]!).Add(Target(root, "b").DeepClone()),
            root => root["Display"]!["RefreshIntervalMs"] = 1
        })
        {
            using var f = new Fixture();
            var previous = f.Current;
            f.Edit(edit);
            var bytes = File.ReadAllBytes(f.AppPath);
            Check(!f.ReloadObserved().Success && ReferenceEquals(f.Current, previous) &&
                  f.Created.Count == 3 && f.Created.All(s => s.Prepares == 0 && s.Commits == 0 && s.Disposals == 0) &&
                  File.ReadAllBytes(f.AppPath).SequenceEqual(bytes),
                "Every record still requires structural/value/binding validation; invalid enabled dashboards must fail before mutation and preserve saved edits.");
        }
        using var broken = new Fixture();
        broken.Board("Same", 0, Left);
        Check(!broken.ReloadObserved().Success && broken.Session("a").Prepares == 0,
            "A malformed enabled document must not fall back to Default.");
        using var invalidTelemetry = new Fixture();
        invalidTelemetry.Policy(leftEnabled: true, interval: 1);
        Check(!invalidTelemetry.ReloadObserved().Success && invalidTelemetry.Session("a").Prepares == 0,
            "Invalid saved telemetry must fail before any Output transaction.");
        File.WriteAllText(invalidTelemetry.TelemetryPath, "{ broken");
        Check(!invalidTelemetry.ReloadObserved().Success && invalidTelemetry.Session("a").Prepares == 0,
            "Malformed telemetry must not silently become default policy.");
        using var missingApp = new Fixture();
        File.Delete(missingApp.AppPath);
        Check(!missingApp.ReloadObserved().Success && missingApp.Session("a").Prepares == 0 && !File.Exists(missingApp.AppPath),
            "Reload must require the saved application file instead of invoking initial-install defaults.");
    }

    public static void DisabledStartupAndExplicitEnable()
    {
        using var f = new Fixture();
        Check(f.Manager.GetDashboardAssignments().Count == 3 && f.Created.All(s => s.TargetId is not ("b" or "d")),
            "Runtime startup must not need or acquire a disabled missing/corrupt assignment.");
        var settings = new RuntimeOutputConfigurationControl(f.Manager, () => f.Current.App, f.Root, f.Log, app =>
            f.Current = new(app, f.Current.Telemetry, f.Manager.GetDashboardAssignments()));
        Check(settings.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "b", Value: false)).Success &&
              f.Manager.GetDashboardAssignments().Count == 3 && f.Created.Count == 3,
            "Disabled Desktop field settings must not require or fabricate a loaded document.");
        var failed = false;
        try { settings.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "b", Value: true)); }
        catch (InvalidDataException) { failed = true; }
        Check(failed && f.Manager.GetStatuses().Single(s => s.TargetId == "b").OutputEnabled == false && f.Created.Count == 3,
            "Explicit enable must load the actual saved dashboard and fail without starting or persisting enablement when it is missing.");
        failed = false;
        try { RuntimeConfigurationValidator.LoadAssignments(f.Current.App, f.Root); }
        catch (InvalidDataException) { failed = true; }
        Check(failed, "Settings' strict validation of all assignments must retain its independent contract.");
        f.Board("MissingDisabled", 390, Left);
        Check(settings.Execute(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "b", Value: true)).Success &&
              f.Session("b").Width == 390 && f.Manager.GetStatuses().Single(s => s.TargetId == "b").IsActive,
            "After the actual document exists, explicit enable must create the correct session rather than a Default substitute.");
    }

    public static void RollbackAcrossSubsystems()
    {
        foreach (var failure in new[] { "prepare", "session commit", "media", "root" })
        {
            using var f = new Fixture();
            var previous = f.Current;
            f.Edit(root =>
            {
                Target(root, "a")["Name"] = "Saved A";
                root["MetricProviders"]![MetricProviderContract.System] = false;
                root["Media"]!["EndpointTypeOverrides"] = new JsonObject { ["synthetic-endpoint"] = "headphones" };
            });
            f.Board("Same", 570, Left);
            var bytes = File.ReadAllBytes(f.AppPath);
            f.Session("a").FailPrepare = failure == "prepare";
            f.Session("e").FailCommit = failure == "session commit";
            var rejected = false;
            void RejectOnce()
            {
                if (rejected) return;
                rejected = true;
                throw new IOException("Synthetic subsystem failure.");
            }
            if (failure == "media") f.OnMedia = RejectOnce;
            if (failure == "root") f.OnRuntimeCommit = _ => RejectOnce();
            var response = f.ReloadObserved();
            Check(!response.Success && ReferenceEquals(f.Current, previous) && f.Media.Count == 0 &&
                  f.Session("a").Target.Name == "A" && f.Session("a").Width == 320 && f.Session("e").Width == 320 &&
                  f.Manager.GetRequiredMetrics().SetEquals(new[] { Left, Right }) &&
                  f.Created.All(s => s.Disposals == 0 && s.Plans.All(p => p.Disposals == 1)) &&
                  Equals(f.Metrics.Snapshot()[RuntimeMetricContract.Version], RuntimeVersion.Current) &&
                  File.ReadAllBytes(f.AppPath).SequenceEqual(bytes),
                "Preparation, sibling commit and downstream common-subsystem failures must recover committed ownership and release every plan while retaining saved edits.");
            // Recovery must restore the actual scheduler, not cached readings.
            f.Metrics.Publish(Left, 99d);
            f.Engine.Reconfigure(previous.Telemetry, [], f.Manager.GetRequiredMetrics());
            Check(f.Metrics.Snapshot()[Left] is null,
                "Recovered provider policy/demand must remain active in the real scheduler so a later provider disable clears its values.");
        }
        using var incomplete = new Fixture();
        incomplete.OnRuntimeCommit = _ => throw new IOException("Synthetic root recovery failure.");
        Check(incomplete.ReloadObserved().Message.Contains("recovery was incomplete", StringComparison.Ordinal),
            "A failing recovery must disclose incomplete recovery rather than imply an atomic cross-subsystem rollback.");
        using var startup = new Fixture();
        var original = startup.Current;
        startup.Board("MissingDisabled", 390, Left);
        startup.Edit(root => Target(root, "b")["Desktop"]!["BoardEnabled"] = true);
        startup.FailStartId = "b";
        Check(!startup.ReloadObserved().Success && ReferenceEquals(startup.Current, original) &&
              startup.Created.Count == 4 && startup.Created.Single(s => s.TargetId == "b").Disposals == 1 &&
              startup.Manager.GetStatuses().Single(s => s.TargetId == "b").OutputEnabled == false &&
              startup.Manager.GetStatuses().Count(s => s.IsActive) == 3 &&
              startup.Created.Where(s => s.TargetId != "b").All(s => s.Disposals == 0 && s.Plans.All(p => p.Disposals == 1)),
            "All candidate startup failure must release the new session exactly once and preserve every prior session and saved live enablement.");
    }

    public static void InterveningConfigurationWrites()
    {
        foreach (var input in new[] { "app", "telemetry", "optional created", "telemetry deleted", "after output commit" })
        {
            using var f = new Fixture();
            f.Edit(root => Target(root, "a")["Name"] = "Saved A");
            f.Board("Same", 550, Left);
            if (input == "optional created") File.Delete(f.TelemetryPath);
            void WriteNewer()
            {
                if (input is "app" or "after output commit") f.Edit(root => root["Future"]!["Keep"] = 99);
                else if (input == "telemetry deleted") File.Delete(f.TelemetryPath);
                else f.Policy(leftEnabled: false, interval: 870);
            }
            if (input == "after output commit") f.Session("a").OnCommit = WriteNewer;
            else f.Session("a").OnPrepare = WriteNewer;
            var response = f.ReloadObserved();
            Check(!response.Success && response.Message.Contains("newer file was retained", StringComparison.Ordinal) &&
                  f.Session("a").Target.Name == "A" && f.Session("a").Width == 320 && f.Created.Count == 3 &&
                  f.Created.All(s => s.Plans.All(p => p.Disposals == 1)),
                "Input byte guards must detect application/telemetry changes and optional-file presence changes before or immediately after Output commit.");
            if (input is "app" or "after output commit")
                Check(JsonNode.Parse(File.ReadAllText(f.AppPath))!["Future"]!["Keep"]!.GetValue<int>() == 99,
                    "Recovery must preserve newer application input including unknown fields.");
            else if (input == "telemetry deleted") Check(!File.Exists(f.TelemetryPath), "Recovery must not recreate deleted telemetry input.");
            else Check(File.ReadAllText(f.TelemetryPath).Contains("870", StringComparison.Ordinal), "Recovery must not overwrite newer telemetry input.");
        }
    }

    public static void GlobalLifetimeSuspensionAndWire()
    {
        using var f = new Fixture();
        var initial = f.Control.GetStatus();
        Check(initial.Outputs.Single(o => o.TargetId == "c") is { IsConnected: true, UsbState: "CONNECTED" } &&
              initial.Outputs.Where(o => o.Kind == OutputTargetContract.DesktopKind && o.OutputEnabled == true)
                  .All(o => o.IsVisible == true && o.UsbState == "DISPLAYING"),
            "The mixed fixture must distinguish physical transport readiness from Desktop presentation readiness.");
        f.Manager.Suspend();
        f.Board("Same", 610, Left);
        var response = f.ReloadObserved();
        Check(response.Success && f.Session("a").Width == 610 && f.Manager.GetRequiredMetrics().Count == 0 &&
              f.Manager.GetStatuses().Where(s => s.OutputEnabled).All(s => s.IsActive && s.UsbState == "SUSPENDED") &&
              f.Events.Single() == RuntimeNotificationKind.Warning && f.Created.Count == 3,
            "The established All wire command must apply saved configuration while preserving real suspension, zero demand and honest pending readiness.");
        f.Manager.Resume();
        var resumed = f.Control.GetStatus();
        // Desktop readiness alone cannot complete All while a physical transport waits.
        f.Feedback.Observe(resumed with { Outputs = resumed.Outputs.Select(o => o.TargetId == "c"
            ? o with { IsConnected = false, UsbState = "WAITING" } : o).ToArray() });
        Check(f.Events.Count == 1,
            "Aggregate readiness must remain pending until the enabled physical transport is ready too.");
        f.Feedback.Observe(resumed);
        Check(f.Events.Count == 2 && f.Events[^1] == RuntimeNotificationKind.Information,
            "Readiness completion must follow observed resume rather than command acknowledgement.");
        f.Feedback.Observe(resumed);
        Check(f.Events.Count == 2, "Repeated ready snapshots must not duplicate the terminal All notification.");
        var session = f.Session("a");
        f.Manager.StopAll();
        var commits = session.Commits;
        Check(!f.ReloadObserved().Success && session.Commits == commits && f.Created.Count == 3,
            "User All must not wake a globally stopped Runtime lifetime.");
        Check(f.Controller.Reload(requireRunning: false).Success && f.Manager.GetStatuses().All(s => !s.IsActive) && f.Created.Count == 3,
            "Internal Settings RELOAD may apply saved configuration during global stop without starting sessions.");
        var missing = new RuntimeOutputControl(f.Manager, () => "Summary");
        Check(!missing.Execute(new(RuntimeIpcCommandKind.StartOrReloadAll)).Success,
            "Missing common-reload wiring must fail visibly instead of falling back to board-only All or global startup.");
    }

    public static void LoggingPhasesAndRecovery()
    {
        using var f = new Fixture();
        Check(f.Reload().Success, "The ordinary fixture reload must succeed before checking evidence.");
        var records = Issue105SettingsLoggingTests.Operations(f.Log);
        var reload = records.Where(e => Issue105SettingsLoggingTests.Name(e) == "runtime.settings.reload").ToArray();
        Check(reload.Last().GetProperty("outcome").GetString() == "succeeded" &&
              new[] { "outputs.apply", "telemetry.apply", "media.apply", "catalog.apply", "runtime.commit" }
                  .All(phase => reload.Any(e => e.GetProperty("phase").GetString() == phase)),
            "Common reload must record the real Output/telemetry/media/catalog/root apply boundaries.");
        var settings = new RuntimeOutputConfigurationControl(f.Manager, () => f.Current.App, f.Root, f.Log,
            app => f.Current = new(app, f.Current.Telemetry, f.Manager.GetDashboardAssignments()));
        Check(settings.Execute(new(RuntimeIpcCommandKind.SetDesktopLock, "b", Value: false)).Success,
            "The existing disabled Desktop Lock transaction must remain valid.");
        records = Issue105SettingsLoggingTests.Operations(f.Log);
        var command = records.Last(e => Issue105SettingsLoggingTests.Name(e) == "runtime.settings.output.command");
        var apply = records.Where(e => Issue105SettingsLoggingTests.Name(e) == "runtime.settings.output.apply").ToArray();
        Check(apply.All(e => e.GetProperty("parentOperationId").GetString() == command.GetProperty("operationId").GetString()) &&
              apply.Any(e => e.GetProperty("phase").GetString() == "file.committed") &&
              apply.Last().GetProperty("outcome").GetString() == "succeeded",
            "Selected-setting commit evidence must share the command parent and include the actual file commit.");
        var original = new IOException("fixture-common-media-failure");
        var calls = 0;
        f.OnMedia = () => { if (++calls == 1) throw original; };
        Exception? failure = null;
        try { f.Reload(); } catch (Exception error) { failure = error; }
        var errors = Issue105SettingsLoggingTests.Operations(f.Log, errors: true);
        records = Issue105SettingsLoggingTests.Operations(f.Log);
        Check(ReferenceEquals(failure, original) && calls == 2 &&
              errors.Any(e => Issue105SettingsLoggingTests.Name(e) == "runtime.settings.reload" &&
                  e.GetProperty("phase").GetString() == "media.apply") &&
              records.Any(e => e.GetProperty("phase").GetString() == "rollback.media.returned") &&
              records.Any(e => e.GetProperty("phase").GetString() == "rollback.outputs.returned") &&
              !File.ReadAllText(f.Log.Path).Contains(original.Message),
            "Runtime recovery must retain the original exception and observed rollback boundaries with full error text only in Errors.");
    }

    private static JsonObject Target(JsonNode root, string id) => root["Outputs"]!["Targets"]!.AsArray()
        .OfType<JsonObject>().Single(t => t["Id"]!.GetValue<string>() == id);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-CommonReload-" + Guid.NewGuid().ToString("N"));
        public string AppPath => Path.Combine(Root, "config", "app.json");
        public string TelemetryPath => Path.Combine(Root, "config", "telemetry.json");
        public readonly FileLogger Log;
        public readonly MetricStore Metrics = new();
        public readonly List<Session> Created = [];
        public readonly OutputSessionManager Manager;
        public readonly TelemetryEngine Engine;
        public readonly RuntimeConfigurationReloadControl Controller;
        public readonly RuntimeOutputControl Control;
        public readonly RuntimeCommandFeedback Feedback;
        public readonly List<RuntimeNotificationKind> Events = [];
        public RuntimeConfigurationSnapshot Current;
        public IReadOnlyDictionary<string, string> Media = new Dictionary<string, string>();
        public Action? OnMedia;
        public string? FailStartId;
        public Action<RuntimeConfigurationSnapshot>? OnRuntimeCommit;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Log = new(Path.Combine(Root, "test.log"));
            var app = new AppConfig { Outputs = new OutputConfig { Targets =
            [
                new() { Id = "a", Name = "A", Kind = OutputTargetContract.DesktopKind, Dashboard = "Same", Desktop = new() },
                new() { Id = "e", Name = "E", Kind = OutputTargetContract.DesktopKind, Dashboard = "Same", Desktop = new() },
                new() { Id = "b", Name = "Disabled B", Kind = OutputTargetContract.DesktopKind, Dashboard = "MissingDisabled", Desktop = new() { BoardEnabled = false } },
                new() { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "c", Name = "C", Dashboard = "Hardware", DeviceEnabled = true, DeviceId = "usb-interface:" + new string('a', 64) },
                new() { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "d", Name = "Disabled D", Dashboard = "BrokenDisabled", ProviderEnabled = true, DeviceId = "usb-interface:" + new string('b', 64) }
            ] } };
            app.Save(AppPath);
            Edit(root => root["Future"] = new JsonObject { ["Keep"] = 42 });
            Board("Same", 320, Left); Board("Hardware", 480, Right); Board("BrokenDisabled", 0, Right);
            app = AppConfig.Load(AppPath, Log);
            IMetricSource[] sources = [new Source()];
            Policy(leftEnabled: true, interval: 500);
            var telemetry = TelemetryConfig.Load(TelemetryPath, sources, Log);
            var boards = RuntimeConfigurationValidator.LoadEnabledAssignments(app, Root);
            Current = new(app, telemetry, boards);
            Manager = new(app, boards, Metrics, Log, (cfg, target, board, store, logger) =>
            {
                var session = new Session(target, board.Canvas.Width) { FailStart = target.Id == FailStartId };
                Created.Add(session);
                return session;
            });
            Manager.Start();
            Engine = new(Metrics, sources, telemetry, MetricProviderContract.GetEnabledProviderIds(app.MetricProviders), Manager.GetRequiredMetrics(), Log);
            Metrics.Publish(Left, 42d); Metrics.Publish(Right, 42d); Metrics.Publish(RuntimeMetricContract.Version, RuntimeVersion.Current);
            MetricContract.ReplaceOutputDescriptors(app.Outputs.Targets);
            Controller = new(Manager, Engine, Metrics, sources, Root, Log, () => Current,
                media => { Media = media; OnMedia?.Invoke(); },
                next => { Current = next; OnRuntimeCommit?.Invoke(next); });
            Control = new(Manager, () => "Summary", reloadAll: () => Controller.Reload());
            Feedback = new((_, kind) => Events.Add(kind), _ => { }, _ => { }, (_, _) => { });
        }
        public Session Session(string id) => Created.Single(s => s.TargetId == id && s.Disposals == 0);
        private static RuntimeIpcCommand Request() => RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StartOrReloadAllCommand);
        public RuntimeIpcResponse Reload() => Control.Execute(Request());
        public RuntimeIpcResponse ReloadObserved() => Feedback.Execute(Request(), () => Reload(), Control.GetStatus);
        public void Edit(Action<JsonNode> edit)
        {
            var root = JsonNode.Parse(File.ReadAllText(AppPath))!;
            edit(root);
            File.WriteAllText(AppPath, root.ToJsonString());
        }
        public void Policy(bool leftEnabled, int interval) => File.WriteAllText(TelemetryPath, JsonSerializer.Serialize(new TelemetryConfig
        {
            Metrics = new(StringComparer.OrdinalIgnoreCase)
            {
                [Left] = new() { Enabled = leftEnabled, IntervalMs = interval },
                [Right] = new() { Enabled = true, IntervalMs = interval }
            }
        }));
        public void Board(string name, int width, string metric)
        {
            var path = Path.Combine(Root, "dashboards", name, "dashboard.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (width == 0) { File.WriteAllText(path, "{ broken"); return; }
            File.WriteAllText(path, DashboardJson.Serialize(new C.DashboardDefinition
            {
                Canvas = new() { Width = width, Height = 300 },
                Widgets = [new C.ValueWidgetDefinition { Id = "metric", SourceKind = C.ValueSourceKind.Metric, Metric = metric }]
            }));
        }
        public void Dispose()
        {
            Manager.Dispose(); Engine.Dispose();
            MetricContract.ReplaceOutputDescriptors([]);
            Directory.Delete(Root, true);
        }
    }

    private sealed class Session(OutputTargetConfig target, int width) : IOutputSession
    {
        public OutputTargetConfig Target = target;
        public string TargetId => Target.Id;
        public string TargetName => Target.Name;
        public int Width = width, Prepares, Commits, Disposals;
        public bool FailPrepare, FailCommit, FailStart;
        public Action? OnPrepare, OnCommit;
        public readonly List<Plan> Plans = [];
        private bool _started, _suspended;
        public bool IsConnected => _started && !_suspended && Disposals == 0;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => _suspended ? "SUSPENDED" : !IsConnected ? "STOPPED" :
            Target.Kind == OutputTargetContract.DesktopKind ? "DISPLAYING" : "CONNECTED";
        public void Start()
        {
            _started = true;
            if (FailStart) throw new InvalidOperationException("Synthetic session startup failure.");
        }
        public IOutputSessionReconfiguration PrepareReconfiguration(AppConfig app, OutputTargetConfig next, C.DashboardDefinition board)
        {
            Prepares++;
            var callback = OnPrepare; OnPrepare = null; callback?.Invoke();
            if (FailPrepare) { FailPrepare = false; throw new InvalidOperationException("Synthetic preparation failure."); }
            var plan = new Plan(next, board.Canvas.Width); Plans.Add(plan); return plan;
        }
        public void CommitReconfiguration(IOutputSessionReconfiguration value)
        {
            var plan = (Plan)value; Target = plan.Target; Width = plan.Width; Commits++;
            var callback = OnCommit; OnCommit = null; callback?.Invoke();
            if (FailCommit) { FailCommit = false; throw new InvalidOperationException("Synthetic post-mutation commit failure."); }
        }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public void Suspend() => _suspended = true;
        public void Resume() => _suspended = false;
        public void Dispose() => Disposals++;
    }
    private sealed class Plan(OutputTargetConfig target, int width) : IOutputSessionReconfiguration
    {
        public readonly OutputTargetConfig Target = target;
        public readonly int Width = width;
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    private sealed class Source : IMetricSource
    {
        public string ProviderId => MetricProviderContract.System;
        public string Name => "Synthetic common reload metrics";
        public IReadOnlyCollection<string> MetricNames => [Left, Right];
        public IReadOnlyDictionary<string, object?> Capture() => new Dictionary<string, object?> { [Left] = 42d, [Right] = 42d };
    }
}
