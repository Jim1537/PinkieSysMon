using System.Text.Json;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopZOrderStage14Tests
{
    public static void PolicyAndPersistence()
    {
        Check(DesktopBottomPlacement.InsertAfter(IntPtr.Zero, IntPtr.Zero) == DesktopBottomPlacement.HwndBottom,
            "When there is no Progman, use literal HWND_BOTTOM.");
        var shell = new IntPtr(42);
        var precedingShell = new IntPtr(1537);
        Check(DesktopBottomPlacement.InsertAfter(shell, precedingShell) == precedingShell,
            "With Progman, the panel must remain in front of wallpaper.");
        Check(DesktopBottomPlacement.InsertAfter(shell, IntPtr.Zero) == DesktopBottomPlacement.HwndTop,
            "When Progman is first in Z-order, HWND_TOP (not BOTTOM) keeps panels visible.");
        Check(DesktopShellZOrderWatcher.IsRelevantEvent(0x0003u, 0, 0) &&
              DesktopShellZOrderWatcher.IsRelevantEvent(0x8004u, 0, 0) &&
              DesktopShellZOrderWatcher.IsRelevantEvent(0x8004u, -4, 0) &&
              !DesktopShellZOrderWatcher.IsRelevantEvent(0x8004u, -4, 1) &&
              !DesktopShellZOrderWatcher.IsRelevantEvent(0x8004u, -5, 0) &&
              !DesktopShellZOrderWatcher.IsRelevantEvent(0x8004u, 0, 1) &&
              !DesktopShellZOrderWatcher.IsRelevantEvent(0x8005u, 0, 0),
            "Foreground and root/client reorder events must include the Win+D notification observed after desktop promotion.");
        const uint prohibited = 0x0004u | 0x0008u;
        Check((DesktopBottomPlacement.Flags & prohibited) == 0 &&
              (DesktopBottomPlacement.Flags & 0x0010u) != 0 &&
              (DesktopBottomPlacement.Flags & 0x0200u) != 0,
            "Native placement must permit reordering without activation.");

        var config = CreateConfig();
        var active = new[] { "d1", "d2", "d3", "usb-off" };
        Check(DesktopZOrderPolicy.BackToFront(config.Outputs.Targets, active)
                .SequenceEqual(new[] { "d2", "d1", "d3" }),
            "Desktop ZOrder must be ordered back-to-front without physical outputs.");

        config.Outputs.Targets[0].Desktop!.ZOrder = 10;
        config.Outputs.Targets[1].Desktop!.ZOrder = 10;
        Check(DesktopZOrderPolicy.BackToFront(config.Outputs.Targets, active)
                .SequenceEqual(new[] { "d1", "d2", "d3" }),
            "Equal ZOrder values must preserve configured list order.");

        config.Outputs.Targets[1].Desktop!.BoardEnabled = false;
        Check(DesktopZOrderPolicy.BackToFront(config.Outputs.Targets, active)
                .SequenceEqual(new[] { "d1", "d3" }),
            "Disabled Desktop must never participate in native stacking.");

        var saved = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))
            ?? throw new InvalidOperationException("Missing deserialized configuration.");
        Check(saved.Outputs.Targets[0].Desktop!.ZOrder == 10 &&
              !saved.Outputs.Targets[1].Desktop!.BoardEnabled &&
              saved.Outputs.Targets[2].Desktop!.ZOrder == 30,
            "Panel ordering and visibility must persist in app configuration.");

        var mouseDown = new Point(40, 40);
        var dragSize = new Size(8, 8);
        Check(!DesktopDragStartPolicy.ShouldStart(mouseDown, mouseDown, dragSize) &&
              !DesktopDragStartPolicy.ShouldStart(mouseDown, new Point(42, 42), dragSize) &&
              DesktopDragStartPolicy.ShouldStart(mouseDown, new Point(45, 40), dragSize) &&
              DesktopDragStartPolicy.ShouldStart(mouseDown, new Point(40, 35), dragSize),
            "Issue76: pointer jitter must not steal selection; motion beyond the drag rectangle must allow reorder.");

        Check(DesktopBottomPlacement.WsExToolWindow == 0x80 &&
              DesktopBottomPlacement.WsExAppWindow == 0x40000,
            "Native ToolWindow style must exclude top-level Desktop panels from Alt+Tab.");
    }

    public static void SessionLifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-stage14-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var metrics = new MetricStore();
            var log = new FileLogger(Path.Combine(root, "stage14.log"));
            var board = new C.DashboardDefinition
            {
                BaseDirectory = root,
                Canvas = new C.CanvasDefinition { Width = 40, Height = 20 },
                Widgets = []
            };
            var events = new List<string>();
            var sessions = new Dictionary<string, FakeStackSession>(StringComparer.OrdinalIgnoreCase);
            var physicalAttempts = 0;
            using var manager = new OutputSessionManager(CreateConfig(), board, metrics, log,
                (app, target, assigned, store, logger) =>
                {
                    if (target.Kind != OutputTargetContract.DesktopKind)
                    {
                        physicalAttempts++;
                        throw new InvalidOperationException("Disabled USB must remain untouched.");
                    }
                    var desktop = new FakeStackSession(target.Id, events);
                    sessions[target.Id] = desktop;
                    return desktop;
                });

            manager.Start();
            AssertEvents(events, "d3@back", "d1@back", "d2@back");

            // Reproduce native trace: FOREGROUND fires before Win+D moves Progman,
            // then a client REORDER is delivered with panels behind the wallpaper.
            // Only the later event may restore owned windows.
            events.Clear();
            if (DesktopShellZOrderWatcher.IsRelevantEvent(
                    DesktopShellZOrderWatcher.EventSystemForeground, 0, 0))
                manager.RestoreDesktopAfterShellChange();
            AssertEvents(events);
            sessions["d1"].BelowDesktop = true;
            if (DesktopShellZOrderWatcher.IsRelevantEvent(
                    DesktopShellZOrderWatcher.EventObjectReorder,
                    DesktopShellZOrderWatcher.ObjIdClient, 0))
                manager.RestoreDesktopAfterShellChange();
            AssertEvents(events, "d3@back", "d1@back", "d2@back");
            sessions["d1"].BelowDesktop = false;
            events.Clear();
            manager.RestoreDesktopAfterShellChange();
            AssertEvents(events);

            // Native drag release must restore order without a configuration reload.
            events.Clear();
            sessions["d2"].CompleteMove();
            AssertEvents(events, "d3@back", "d1@back", "d2@back");

            events.Clear();
            var reordered = CreateConfig();
            reordered.Outputs.Targets[0].Desktop!.ZOrder = 0;
            reordered.Outputs.Targets[1].Desktop!.ZOrder = 30;
            reordered.Outputs.Targets[2].Desktop!.ZOrder = 20;
            manager.Reconfigure(reordered, board);
            AssertEvents(events, "d2@back", "d3@back", "d1@back");

            events.Clear();
            sessions["d1"].CompleteMove();
            AssertEvents(events, "d2@back", "d3@back", "d1@back");

            events.Clear();
            var disabledLater = sessions["d3"];
            reordered.Outputs.Targets[2].Desktop!.BoardEnabled = false;
            manager.Reconfigure(reordered, board);
            AssertEvents(events, "d2@back", "d1@back");
            events.Clear();
            disabledLater.CompleteMove();
            AssertEvents(events);

            events.Clear();
            reordered.Outputs.Targets[2].Desktop!.BoardEnabled = true;
            manager.Reconfigure(reordered, board);
            AssertEvents(events, "d2@back", "d3@back", "d1@back");

            events.Clear();
            var disabledD2 = OutputLifecycleTestConfiguration.WithEnabled(reordered, "d2", false);
            manager.ReconfigureTarget(disabledD2.Outputs.Targets.Single(t => t.Id == "d2"),
                manager.GetDashboardAssignments()["d2"], _ => { });
            Check(!manager.GetStatuses().Single(s => s.TargetId == "d2").IsActive,
                "Disabling a panel must remove its active session.");
            AssertEvents(events, "d3@back", "d1@back");

            events.Clear();
            manager.ReconfigureTarget(reordered.Outputs.Targets.Single(t => t.Id == "d2"),
                manager.GetDashboardAssignments()["d2"], _ => { });
            AssertEvents(events, "d2@back", "d3@back", "d1@back");

            events.Clear();
            manager.Suspend();
            manager.Resume();
            AssertEvents(events, "d2@back", "d3@back", "d1@back");

            Check(physicalAttempts == 0, "Window ordering must not start USB hardware.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static AppConfig CreateConfig() => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                Desktop("d1", 20),
                Desktop("d2", 10),
                Desktop("d3", 30),
                new OutputTargetConfig
                {
                    Id = "usb-off", Name = "Disabled Trofeo",
                    Kind = OutputTargetContract.TrofeoKind,
                    Transport = OutputTargetContract.TrofeoTransport,
                    DeviceEnabled = false
                }
            ]
        }
    };

    private static OutputTargetConfig Desktop(string id, int zOrder) => new()
    {
        Id = id,
        Name = id,
        Kind = OutputTargetContract.DesktopKind,
        Transport = OutputTargetContract.DesktopTransport,
        Dashboard = "stage14",
        Desktop = new DesktopOutputConfig { BoardEnabled = true, ZOrder = zOrder }
    };

    private static void AssertEvents(List<string> actual, params string[] expected)
    {
        Check(actual.SequenceEqual(expected),
            $"Unexpected panel stack: {string.Join(", ", actual)}; expected {string.Join(", ", expected)}.");
    }

    private sealed class FakeStackSession(string id, List<string> events)
        : IOutputSession, IDesktopStackSession
    {
        private bool _visible;
        private Action? _restoreStack;
        public string TargetId => id;
        public string TargetName => id;
        public bool IsConnected => _visible;
        public bool IsStackVisible => _visible;
        public bool BelowDesktop { get; set; }
        public bool IsBelowDesktop => BelowDesktop;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => _visible ? "DISPLAYING" : "STOPPED";
        public void Start() => _visible = true;
        public void Suspend() => _visible = false;
        public void Resume() => _visible = true;
        public void Dispose() => _visible = false;
        public void SetStackRestorer(Action restorer) => _restoreStack = restorer;
        public void CompleteMove() => _restoreStack?.Invoke();
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition dashboard) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
        public void PlaceAtDesktopBack() => events.Add($"{id}@back");
        private sealed class Plan : IOutputSessionReconfiguration
        {
            public void Dispose() { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
