using System.Drawing;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Stage 15 uses the real DesktopOutputSession/renderer with independent fake
// windows, plus a fake physical session: no native HWNDs and no device I/O.
internal static class DesktopMultiSessionStage15Tests
{
    public static void SharedBoardReloadAndRemoval()
    {
        InSandbox((root, log) =>
        {
            WriteBoard(root, "Shared", "#FFFF0000");
            WriteBoard(root, "Other", "#FF0000FF");
            var config = Config(includeThird: true);
            var boards = RuntimeConfigurationValidator.LoadAssignments(config, root);
            Check(!ReferenceEquals(boards["d1"], boards["d2"]),
                "Two panels referencing the same saved board must load distinct document objects.");

            var windows = new Dictionary<string, List<FakeWindow>>(StringComparer.OrdinalIgnoreCase);
            var physical = new FakePhysicalSession("usb-1");
            using var manager = Manager(config, boards, root, log, windows, physical);
            manager.Start();

            var first = Latest(windows, "d1");
            var second = Latest(windows, "d2");
            var third = Latest(windows, "d3");
            Check(manager.GetStatuses().Count(status => status.IsActive) == 4 &&
                  physical.StartCount == 1 && physical.DisposeCount == 0,
                "Three live Desktop panels must coexist with an independent physical output.");
            Check(first.IsVisible && second.IsVisible && third.IsVisible &&
                  first.Location == new Point(20, 30) &&
                  second.Location == new Point(50, 60) &&
                  third.Location == new Point(80, 90) &&
                  first.RefreshMs == 100 && second.RefreshMs == 200 && third.RefreshMs == 300,
                "Every Desktop panel must own its own window, position, and refresh interval.");
            Check(Red(first.Draw()) && Red(second.Draw()) && Blue(third.Draw()),
                "Shared dashboard assignment must render separately from another board.");

            WriteBoard(root, "Shared", "#FF00FF00");
            manager.StartOrReloadTarget("d1");
            Check(Green(first.Draw()) && Red(second.Draw()) && Blue(third.Draw()) &&
                  first.DisposeCount == 0 && second.DisposeCount == 0 &&
                  third.DisposeCount == 0 && physical.DisposeCount == 0,
                "Targeted board reload must update only its own renderer and leave sibling windows alive.");

            WriteBoard(root, "Shared", "#FFFFFF00");
            first.FailNextConfigure = true;
            var failed = false;
            try { manager.StartOrReloadTarget("d1"); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && Green(first.Draw()) && Red(second.Draw()) &&
                  Blue(third.Draw()) && physical.DisposeCount == 0,
                "One Desktop configuration failure must roll back without closing or repainting siblings.");

            var reduced = Config(includeThird: false);
            manager.Reconfigure(reduced, RuntimeConfigurationValidator.LoadAssignments(reduced, root));
            Check(third.DisposeCount == 1 && !third.IsVisible &&
                  first.DisposeCount == 0 && second.DisposeCount == 0 &&
                  first.IsVisible && second.IsVisible && physical.DisposeCount == 0,
                "Removing Desktop d3 must dispose only its own window, leaving other sessions running.");
            Check(File.Exists(BoardFile(root, "Shared")) && File.Exists(BoardFile(root, "Other")),
                "Removing an Output must never delete either its own or a shared dashboard file.");
            Check(!manager.GetStatuses().Any(status => status.TargetId == "d3") &&
                  manager.GetStatuses().Count(status => status.IsActive) == 3,
                "Removed Desktop must disappear from status without leaving a stale live session.");
            Check(Throws<InvalidOperationException>(() => third.Draw()),
                "A disposed Desktop window must not render any additional frames.");

            var control = new RuntimeOutputControl(manager, () => "Shared");
            var stop = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopTarget, "d1");
            Check(control.Execute(stop).Success && !control.ShouldExitRuntimeAfter(stop) &&
                  first.DisposeCount == 1 && second.IsVisible && physical.DisposeCount == 0,
                "Stopping one Desktop must not exit Runtime or unload another panel or hardware.");

            manager.StartOrReloadTarget("d1");
            var restarted = Latest(windows, "d1");
            Check(!ReferenceEquals(first, restarted) && restarted.IsVisible &&
                  restarted.DisposeCount == 0 && second.IsVisible &&
                  physical.StartCount == 1 && physical.DisposeCount == 0,
                "Restarting a stopped Desktop must create only its own new window.");

            var stopSecond = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopTarget, "d2");
            Check(control.Execute(stopSecond).Success &&
                  !control.ShouldExitRuntimeAfter(stopSecond) &&
                  second.DisposeCount == 1 && restarted.IsVisible && physical.DisposeCount == 0,
                "Runtime must continue while another Desktop or physical Output is active.");
        });
    }

    public static void FailedPanelCreationIsolation()
    {
        InSandbox((root, log) =>
        {
            WriteBoard(root, "Shared", "#FFFF0000");
            WriteBoard(root, "Other", "#FF0000FF");
            var config = Config(includeThird: false);
            config.Outputs.Targets.RemoveAll(target => target.Id == "d2");
            var windows = new Dictionary<string, List<FakeWindow>>(StringComparer.OrdinalIgnoreCase);
            var physical = new FakePhysicalSession("usb-1");
            var rejectNewPanelOnce = true;

            using var manager = Manager(config,
                RuntimeConfigurationValidator.LoadAssignments(config, root),
                root, log, windows, physical,
                (id, window) =>
                {
                    if (id == "d2" && rejectNewPanelOnce)
                    {
                        rejectNewPanelOnce = false;
                        window.FailNextShow = true;
                    }
                });
            manager.Start();
            var original = Latest(windows, "d1");
            Check(original.IsVisible && physical.StartCount == 1,
                "Existing Desktop and physical session must be active before the failed addition.");

            var expanded = Config(includeThird: false);
            var newBoards = RuntimeConfigurationValidator.LoadAssignments(expanded, root);
            Check(Throws<InvalidOperationException>(() => manager.Reconfigure(expanded, newBoards)),
                "Failure while showing a new Desktop must reject the incomplete configuration.");
            var rejected = Latest(windows, "d2");
            Check(rejected.DisposeCount == 1 && !rejected.IsVisible &&
                  !manager.GetStatuses().Any(status => status.TargetId == "d2") &&
                  original.IsVisible && original.DisposeCount == 0 &&
                  physical.DisposeCount == 0 && physical.StartCount == 1 &&
                  Red(original.Draw()),
                "Failed new panel must clean up its window and leave all prior sessions intact.");

            manager.Reconfigure(expanded, RuntimeConfigurationValidator.LoadAssignments(expanded, root));
            var successful = Latest(windows, "d2");
            Check(successful.IsVisible && successful.DisposeCount == 0 &&
                  rejected.DisposeCount == 1 && original.IsVisible &&
                  physical.DisposeCount == 0 &&
                  manager.GetStatuses().Count(status => status.IsActive) == 3,
                "Retry must start the new panel without leaking its failed predecessor.");
        });
    }

    private static OutputSessionManager Manager(
        AppConfig config,
        IReadOnlyDictionary<string, C.DashboardDefinition> boards,
        string root,
        FileLogger log,
        Dictionary<string, List<FakeWindow>> windows,
        FakePhysicalSession physical,
        Action<string, FakeWindow>? onCreate = null) =>
        new(config, boards, new MetricStore(), log,
            (app, target, board, metrics, logger) =>
            {
                if (target.Kind == OutputTargetContract.TrofeoKind)
                    return physical;

                return new DesktopOutputSession(app, target, board, metrics, logger,
                    render =>
                    {
                        var window = new FakeWindow(render);
                        if (!windows.TryGetValue(target.Id, out var list))
                            windows[target.Id] = list = [];
                        list.Add(window);
                        onCreate?.Invoke(target.Id, window);
                        return window;
                    });
            },
            target => RuntimeConfigurationValidator.LoadTarget(target, root));

    private static AppConfig Config(bool includeThird)
    {
        var config = new AppConfig
        {
            Outputs = new OutputConfig
            {
                Targets =
                [
                    Desktop("d1", "Shared", 20, 30, 100),
                    Desktop("d2", "Shared", 50, 60, 200),
                    Physical("usb-1")
                ]
            }
        };
        if (includeThird)
            config.Outputs.Targets.Insert(2, Desktop("d3", "Other", 80, 90, 300));
        return config;
    }

    private static OutputTargetConfig Desktop(
        string id, string dashboard, int x, int y, int refresh) => new()
    {
        Id = id,
        Name = id,
        Kind = OutputTargetContract.DesktopKind,
        Transport = OutputTargetContract.DesktopTransport,
        Dashboard = dashboard,
        RefreshIntervalMs = refresh,
        Desktop = new DesktopOutputConfig { BoardEnabled = true, X = x, Y = y, Lock = false }
    };

    private static OutputTargetConfig Physical(string id) => new()
    {
        Id = id,
        Name = "Fake Trofeo",
        Kind = OutputTargetContract.TrofeoKind,
        Transport = OutputTargetContract.TrofeoTransport,
        Dashboard = "Other",
        DeviceEnabled = true
    };

    private static void WriteBoard(string root, string name, string color)
    {
        var path = BoardFile(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "{\"schemaVersion\":19,\"canvas\":{\"width\":60,\"height\":30,\"orientation\":0," +
            "\"backgroundColor\":\"" + color + "\"},\"widgets\":[]}");
    }

    private static string BoardFile(string root, string name) =>
        Path.Combine(root, "dashboards", name, "dashboard.json");

    private static FakeWindow Latest(Dictionary<string, List<FakeWindow>> windows, string id) =>
        windows[id][^1];

    private static bool Red(SKColor c) => c.Red > 230 && c.Green < 20 && c.Blue < 20;
    private static bool Green(SKColor c) => c.Green > 230 && c.Red < 20 && c.Blue < 20;
    private static bool Blue(SKColor c) => c.Blue > 230 && c.Red < 20 && c.Green < 20;

    private static void InSandbox(Action<string, FileLogger> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-stage15-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "dashboards"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            // DesktopOutputSession resolves assets from an application root that
            // contains both config/app.json and dashboards/. Match the real layout.
            File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
            work(root, new FileLogger(Path.Combine(root, "stage15.log")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeWindow(Action drawFrame) : IDesktopOutputWindow
    {
        public bool IsVisible { get; private set; }
        public int DisposeCount { get; private set; }
        public Point Location { get; private set; }
        public int RefreshMs { get; private set; }
        public bool FailNextConfigure { get; set; }
        public bool FailNextShow { get; set; }
        private SKColor _lastPixel;
        public void Configure(string title, Size size, Point location, int refreshMs, bool locked)
        {
            if (FailNextConfigure)
            {
                FailNextConfigure = false;
                throw new InvalidOperationException("Synthetic Desktop reconfigure failure.");
            }
            Location = location;
            RefreshMs = refreshMs;
        }
        public void ShowWindow()
        {
            if (FailNextShow)
            {
                FailNextShow = false;
                throw new InvalidOperationException("Synthetic Desktop window start failure.");
            }
            IsVisible = true;
        }
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { }
        public void Present(SKBitmap bitmap) =>
            _lastPixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        public SKColor Draw()
        {
            if (!IsVisible || DisposeCount != 0)
                throw new InvalidOperationException("Cannot draw a stopped/disposed Desktop window.");
            drawFrame();
            return _lastPixel;
        }
        public void Dispose()
        {
            DisposeCount++;
            IsVisible = false;
        }
    }

    private sealed class FakePhysicalSession(string id) : IOutputSession
    {
        public string TargetId => id;
        public string TargetName => "Fake Trofeo";
        public bool IsConnected => StartCount > 0 && DisposeCount == 0;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => "FAKE";
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Start() => StartCount++;
        public void Suspend() { }
        public void Resume() { }
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition board) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
        public void Dispose() => DisposeCount++;
        private sealed class Plan : IOutputSessionReconfiguration
        {
            public void Dispose() { }
        }
    }
}
