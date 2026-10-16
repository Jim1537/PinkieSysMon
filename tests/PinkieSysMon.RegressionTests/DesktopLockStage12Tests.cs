using System.Drawing;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopLockStage12Tests
{
    public static void TaskbarExclusion()
    {
        using var panel = new DesktopOutputWindow(() => { }, _ => { }, () => { });
        Check(!panel.ShowInTaskbar,
            "Desktop panels must not create taskbar buttons; use the Runtime tray.");
    }

    public static void NativeStylePolicy()
    {
        const int unrelated = 0x08000100;
        var locked = DesktopPointerStyle.ForLock(unrelated, locked: true);
        Check((locked & DesktopPointerStyle.WsExLayered) != 0 &&
              (locked & DesktopPointerStyle.WsExTransparent) != 0,
              "Locked Desktop must be layered and transparent to all mouse hits.");
        Check((locked & unrelated) == unrelated,
              "Lock must not disturb unrelated native extended-window styles.");

        var unlocked = DesktopPointerStyle.ForLock(locked, locked: false);
        Check((unlocked & DesktopPointerStyle.WsExTransparent) == 0 &&
              (unlocked & DesktopPointerStyle.WsExLayered) != 0,
              "Unlock must restore normal hit-testing while keeping layered visual alpha.");
        Check((unlocked & unrelated) == unrelated,
              "Unlock must retain other extended styles.");
        Check(DesktopPointerStyle.ForLock(unlocked, locked: false) == unlocked &&
              DesktopPointerStyle.ForLock(locked, locked: true) == locked,
              "Repeated Lock changes must be idempotent.");
    }

    public static void LiveLockToggle()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-stage12-" + Guid.NewGuid().ToString("N"));
        var boardDir = Path.Combine(root, "dashboards", "stage12");
        Directory.CreateDirectory(boardDir);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        try
        {
            var board = new C.DashboardDefinition
            {
                BaseDirectory = boardDir,
                Canvas = new C.CanvasDefinition
                {
                    Width = 48, Height = 24, BackgroundColor = "#80AABBCC"
                },
                Widgets = []
            };
            var metrics = new MetricStore();
            var logger = new FileLogger(Path.Combine(root, "stage12.log"));
            FakeWindow? window = null;
            var initial = Config(locked: true);
            using var session = new DesktopOutputSession(
                initial, initial.Outputs.Targets[0], board, metrics, logger,
                render => window = new FakeWindow(render));
            session.Start();
            var live = window ?? throw new InvalidOperationException("Desktop not created.");
            Check(live.IsVisible && live.Locked && live.ConfigureCount == 1,
                "Default locked Desktop must show with full click-through policy.");

            ApplyLock(session, board, Config(locked: false));
            Check(!live.Locked && live.IsVisible && live.ConfigureCount == 2,
                "Unlock must immediately reconfigure the SAME visible Desktop window.");
            ApplyLock(session, board, Config(locked: true));
            Check(live.Locked && live.IsVisible && live.ConfigureCount == 3,
                "Relocking must immediately reconfigure the same visible window.");
            ApplyLock(session, board, Config(locked: false));
            Check(!live.Locked && live.ConfigureCount == 4,
                "Repeated toggling must restore unlocked drag policy.");

            live.Draw();
            Check(live.LastFrame.Alpha is >= 127 and <= 129,
                "Lock toggling must not change premultiplied Canvas transparency.");
            session.Suspend();
            Check(!live.IsVisible, "Suspended Desktop must be hidden.");
            session.Resume();
            Check(live.IsVisible && !live.Locked,
                "Resume must preserve last Lock state.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void ApplyLock(DesktopOutputSession session,
        C.DashboardDefinition board, AppConfig config)
    {
        using var prepared = session.PrepareReconfiguration(config, config.Outputs.Targets[0], board);
        session.CommitReconfiguration(prepared);
    }

    private static AppConfig Config(bool locked) => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                {
                    Id = "desk-12",
                    Name = "Stage12 Desktop",
                    Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport,
                    Dashboard = "stage12",
                    Desktop = new DesktopOutputConfig { BoardEnabled = true, Lock = locked }
                }
            ]
        }
    };

    private sealed class FakeWindow(Action render) : IDesktopOutputWindow
    {
        private readonly Action _render = render;
        public bool IsVisible { get; private set; }
        public bool Locked { get; private set; }
        public int ConfigureCount { get; private set; }
        public SKColor LastFrame { get; private set; }

        public void Configure(string title, Size pixels, Point location, int refreshMs, bool locked)
        {
            _ = title;
            _ = pixels;
            _ = location;
            _ = refreshMs;
            Locked = locked;
            ConfigureCount++;
        }
        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { }
        public void Present(SKBitmap bitmap) =>
            LastFrame = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        public void Draw()
        {
            if (!IsVisible)
                throw new InvalidOperationException("Cannot render hidden Desktop.");
            _render();
        }
        public void Dispose() => IsVisible = false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
