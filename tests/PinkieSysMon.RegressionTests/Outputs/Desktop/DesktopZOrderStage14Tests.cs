using System.ComponentModel;
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

    public static void RecoverablePlacementFailure()
    {
        // This is the same recoverable Win32 boundary used by the native window;
        // no shell window, physical USB device or Windows reboot is required.
        var attempts = 0;
        var placement = new DesktopPlacementRecovery();
        bool SimulatedNative()
        {
            if (++attempts <= 2)
                throw new Win32Exception(5, "Injected SetWindowPos access denied.");
            return false;
        }

        Check(!placement.TryPlace(SimulatedNative, out _, out var firstFailure) &&
              firstFailure?.NativeErrorCode == 5 && placement.Pending &&
              placement.NextDelayMs == 250,
            "Initial ERROR_ACCESS_DENIED must be nonfatal and schedule bounded recovery.");
        Check(placement.BeginAutomaticRetry() && placement.AutomaticRetries == 1 &&
              placement.NextDelayMs == 1000 &&
              !placement.TryPlace(SimulatedNative, out _, out var secondFailure) &&
              secondFailure?.NativeErrorCode == 5,
            "A failed retry must preserve the native error and advance only on a retry event.");
        Check(placement.BeginAutomaticRetry() &&
              placement.TryPlace(SimulatedNative, out var fallback, out var recoveredFailure) &&
              !fallback && recoveredFailure is null && !placement.Pending &&
              placement.AutomaticRetries == 0 && placement.NextDelayMs == 0,
            "A later successful native placement must clear retry state without needing a restart.");

        var persistent = new DesktopPlacementRecovery();
        Check(!persistent.TryPlace(() => throw new Win32Exception(5), out _, out _),
            "Persistent failure must be captured.");
        foreach (var expectedDelay in new[] { 250, 1000, 3000 })
        {
            Check(persistent.NextDelayMs == expectedDelay &&
                  persistent.BeginAutomaticRetry() &&
                  !persistent.TryPlace(() => throw new Win32Exception(5), out _, out _),
                "Each bounded retry must preserve the unresolved placement state.");
        }
        Check(persistent.Pending && persistent.AutomaticRetries == 3 &&
              persistent.NextDelayMs == 0 && !persistent.BeginAutomaticRetry(),
            "A permanent native failure must not produce an indefinite retry loop.");
        persistent.Cancel();
        Check(!persistent.Pending && persistent.AutomaticRetries == 0 &&
              persistent.NextDelayMs == 0 && !persistent.BeginAutomaticRetry(),
            "Hide and disposal must cancel outstanding recovery.");
        try
        {
            placement.TryPlace(() => throw new InvalidOperationException("Non-native failure"),
                out _, out _);
            throw new InvalidOperationException("Non-Win32 failure was incorrectly swallowed.");
        }
        catch (InvalidOperationException error) when (error.Message == "Non-native failure") { }

        var root = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-issue118-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = CreateConfig();
            config.Outputs.Targets[3].DeviceEnabled = true;
            var board = new C.DashboardDefinition
            {
                BaseDirectory = root,
                Canvas = new C.CanvasDefinition { Width = 40, Height = 20 },
                Widgets = []
            };
            var events = new List<string>();
            var nativeCalls = 0;
            FakeStackSession? degraded = null;
            FakePhysicalSession? physical = null;
            using (var manager = new OutputSessionManager(config, board,
                new MetricStore(), new FileLogger(Path.Combine(root, "issue118.log")),
                (app, target, assigned, metrics, logger) =>
                {
                    if (target.Kind == OutputTargetContract.PhysicalKind)
                        return physical = new FakePhysicalSession(target.Id);
                    return target.Id == "d1"
                        ? degraded = new FakeStackSession(target.Id, events, () =>
                        {
                            if (++nativeCalls <= 3)
                                throw new Win32Exception(5, "Injected initial access denied.");
                            return false;
                        })
                        : new FakeStackSession(target.Id, events);
                }))
            {
                manager.Start();
                Check(degraded is { PendingPlacement: true } &&
                      physical is { Started: true } &&
                      manager.GetStatuses().Single(s => s.TargetId == "usb-off").IsActive,
                    "Native Desktop placement denial must not abort Runtime or an unrelated physical Output.");
                manager.RestoreDesktopAfterShellChange();
                Check(degraded is { PendingPlacement: true } && physical is { Started: true },
                    "A failed shell reconciliation must leave other active Outputs untouched.");
                manager.RestoreDesktopAfterShellChange();
                Check(degraded is { PendingPlacement: false } && physical is { Started: true } &&
                      manager.GetStatuses().Single(s => s.TargetId == "d1").IsConnected,
                    "A later valid placement must recover within the existing session lifetime.");
            }
            Check(physical is { Started: false } && degraded is { IsStackVisible: false },
                "Shutdown must dispose the recovered Desktop and physical sessions.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
                { Width = 1920, Height = 480, DeviceId = "usb-interface:f7ebbc369634a2a767f095d6e3348a2529983e0bf2eefda38f4485a602796dd7",
                    Id = "usb-off", Name = "Disabled Trofeo",
                    Kind = OutputTargetContract.PhysicalKind,
                    Transport = OutputTargetContract.LyBulkTransport,
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

    private sealed class FakeStackSession(string id, List<string> events,
        Func<bool>? nativePlacement = null) : IOutputSession, IDesktopStackSession
    {
        private bool _visible;
        private readonly DesktopPlacementRecovery? _placement = nativePlacement is null
            ? null : new DesktopPlacementRecovery();
        private Action? _restoreStack;
        public string TargetId => id;
        public string TargetName => id;
        public bool IsConnected => _visible;
        public bool IsStackVisible => _visible;
        public bool BelowDesktop { get; set; }
        public bool IsBelowDesktop => BelowDesktop || PendingPlacement;
        public bool PendingPlacement => _placement?.Pending == true;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => _visible ? "DISPLAYING" : "STOPPED";
        public void Start()
        {
            _visible = true;
            if (_placement is not null)
                PlaceAtDesktopBack();
        }
        public void Suspend() => _visible = false;
        public void Resume() => _visible = true;
        public void Dispose()
        {
            _visible = false;
            _placement?.Cancel();
        }
        public void SetStackRestorer(Action restorer) => _restoreStack = restorer;
        public void CompleteMove() => _restoreStack?.Invoke();
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition dashboard) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
        public void PlaceAtDesktopBack()
        {
            if (_placement is null)
                events.Add($"{id}@back");
            else
                _placement.TryPlace(nativePlacement!, out _, out _);
        }
        private sealed class Plan : IOutputSessionReconfiguration
        {
            public void Dispose() { }
        }
    }

    private sealed class FakePhysicalSession(string id) : IOutputSession
    {
        public bool Started { get; private set; }
        public string TargetId => id;
        public string TargetName => id;
        public bool IsConnected => Started;
        public long SentFrames => 0;
        public double LastFrameMs => 0;
        public int LastJpegBytes => 0;
        public string UsbState => Started ? "CONNECTED" : "STOPPED";
        public void Start() => Started = true;
        public void Suspend() => Started = false;
        public void Resume() => Started = true;
        public void Dispose() => Started = false;
        public void SetRuntimeMetricsPublisher(bool enabled) { }
        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config, OutputTargetConfig target, C.DashboardDefinition dashboard) => new Plan();
        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration) { }
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
