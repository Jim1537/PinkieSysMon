using System.Drawing;
using System.Drawing.Imaging;
using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopSessionStage09Tests
{
    public static void DesktopLifecycle()
    {
        InSandbox((directory, log) =>
        {
            var config = Config(boardEnabled: true, providerEnabled: true);
            config.Outputs.Targets.Add(new OutputTargetConfig
            {
                Id = "physical-disabled", Name = "Offline USB", Kind = OutputTargetContract.TrofeoKind,
                Transport = OutputTargetContract.TrofeoTransport, DeviceEnabled = false
            });
            var board = Dashboard(directory, orientation: 0, "#FFFF0000");
            var store = new MetricStore();
            FakeWindow? window = null;
            var physicalAttempts = 0;

            using var manager = new OutputSessionManager(config, board, store, log,
                (app, target, assigned, metrics, logger) =>
                {
                    if (target.Kind != OutputTargetContract.DesktopKind)
                    {
                        physicalAttempts++;
                        throw new InvalidOperationException("Disabled USB must never be opened.");
                    }
                    return new DesktopOutputSession(app, target, assigned, metrics, logger,
                        draw => window = new FakeWindow(draw));
                });

            manager.Start();
            Check(window is { IsVisible: true, ClientPixels.Width: 64, ClientPixels.Height: 32 } &&
                  physicalAttempts == 0, "Desktop-only start must show its window without attempting USB.");
            Check(manager.GetStatuses().Single(s => s.TargetId == "desk-1").IsConnected &&
                  !manager.GetStatuses().Single(s => s.TargetId == "physical-disabled").IsActive,
                "The manager must report an independent live Desktop and an inactive physical output.");

            var pixel = window.Draw();
            Check(pixel.R > 230 && pixel.G < 20 && pixel.B < 20,
                "Desktop paint must present the canonical red BGRA Canvas.");
            var frameKey = OutputMetricContract.Id("desk-1", OutputMetricContract.FrameCount);
            var statusKey = OutputMetricContract.Id("desk-1", OutputMetricContract.Status);
            Check(Equals(store.Snapshot()[frameKey], 1L) &&
                  Equals(store.Snapshot()[statusKey], "DISPLAYING"),
                "Enabled Desktop provider must publish real frame count and observed status.");
            Check(!store.Snapshot().ContainsKey(OutputMetricContract.Id("desk-1", OutputMetricContract.UsbDuration)),
                "A Desktop must not publish a fictitious USB counter.");

            manager.Suspend();
            Check(!window.IsVisible && Equals(store.Snapshot()[statusKey], "SUSPENDED"),
                "System suspend must hide the Desktop and publish its lifecycle state.");
            manager.Resume();
            Check(window.IsVisible, "System resume must restore the active Desktop window.");
            Check(manager.StopTarget("desk-1") && window.DisposeCount == 1 &&
                  !manager.GetStatuses().Single(s => s.TargetId == "desk-1").IsActive,
                "StopTarget must close only the Desktop session and dispose its window.");
        });
    }

    public static void ReconfigureGeometry()
    {
        InSandbox((directory, log) =>
        {
            var config = Config(boardEnabled: true, providerEnabled: false);
            var store = new MetricStore();
            FakeWindow? window = null;
            using var manager = new OutputSessionManager(config, Dashboard(directory, 0, "#FFFF0000"),
                store, log, (app, target, board, metrics, logger) =>
                    new DesktopOutputSession(app, target, board, metrics, logger,
                        draw => window = new FakeWindow(draw)));
            manager.Start();

            var initial = window ?? throw new InvalidOperationException("Missing Desktop window.");
            Check(initial.Draw().R > 230, "The original assigned dashboard must render.");
            var next = Config(boardEnabled: true, providerEnabled: false);
            next.Outputs.Targets[0].RefreshIntervalMs = 250;
            next.Outputs.Targets[0].Desktop!.X = -80;
            next.Outputs.Targets[0].Desktop!.Y = 35;
            manager.Reconfigure(next, Dashboard(directory, 90, "#FF0000FF"));

            var after = initial.Draw();
            Check(initial.ClientPixels == new Size(32, 64) &&
                  initial.Location == new Point(-80, 35) &&
                  initial.IntervalMs == 250 &&
                  after.B > 230 && after.R < 20,
                "Target reload must update logical orientation geometry, window location, cadence and rendered board.");
            Check(initial.DisposeCount == 0 && manager.GetStatuses().Single().IsActive,
                "Reload must keep one live Desktop window and not leak or replace its identity.");
        });
    }

    public static void IndependentSwitches()
    {
        InSandbox((directory, log) =>
        {
            var board = Dashboard(directory, 0, "#FF00FF00");
            var store = new MetricStore();
            var created = 0;
            FakeWindow? window = null;
            using var manager = new OutputSessionManager(Config(false, true), board, store, log,
                (app, target, assigned, metrics, logger) =>
                {
                    created++;
                    return new DesktopOutputSession(app, target, assigned, metrics, logger,
                        draw => window = new FakeWindow(draw));
                });
            manager.Start();
            var status = OutputMetricContract.Id("desk-1", OutputMetricContract.Status);
            Check(created == 0 && Equals(store.Snapshot()[status], "DESKTOP_PENDING"),
                "Provider Enabled alone must publish passive status without starting a hidden panel.");

            manager.Reconfigure(Config(true, false), board);
            Check(created == 1 && window is { IsVisible: true },
                "Board Enabled alone must start a Desktop even when Provider Enabled is off.");
            _ = window!.Draw();
            Check(!store.Snapshot().TryGetValue(OutputMetricContract.Id("desk-1", OutputMetricContract.FrameCount), out var frames) ||
                  frames is null, "Disabled provider must not publish live readings for a visible board.");

            manager.Reconfigure(Config(false, true), board);
            Check(window.DisposeCount == 1 && Equals(store.Snapshot()[status], "DESKTOP_PENDING"),
                "Disabling Board Enabled must dispose the window and retain only passive provider status.");
        });
    }

    private static AppConfig Config(bool boardEnabled, bool providerEnabled) => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                {
                    Id = "desk-1", Name = "Stage 09 Desktop",
                    Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport,
                    Dashboard = "Default", RefreshIntervalMs = 40,
                    Desktop = new DesktopOutputConfig { BoardEnabled = boardEnabled },
                    ProviderEnabled = providerEnabled
                }
            ]
        }
    };

    private static C.DashboardDefinition Dashboard(string directory, int orientation, string color) => new()
    {
        BaseDirectory = directory,
        Canvas = new C.CanvasDefinition
        {
            Width = 64, Height = 32, Orientation = orientation, BackgroundColor = color
        },
        Widgets = []
    };

    private static void InSandbox(Action<string, FileLogger> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-desktop09-" + Guid.NewGuid().ToString("N"));
        var board = Path.Combine(root, "dashboards", "stage09");
        try
        {
            Directory.CreateDirectory(board);
            Directory.CreateDirectory(Path.Combine(root, "config"));
            File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
            work(board, new FileLogger(Path.Combine(root, "stage09.log")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    // Headless presentation double: invokes the exact production render callback with GDI+ pixels,
    // without creating a Win32 window in the automated regression process.
    private sealed class FakeWindow(Action<Graphics> paint) : IDesktopOutputWindow
    {
        private readonly Action<Graphics> _paint = paint;
        public bool IsVisible { get; private set; }
        public Size ClientPixels { get; private set; }
        public Point Location { get; private set; }
        public int IntervalMs { get; private set; }
        public int DisposeCount { get; private set; }

        public void Configure(string title, Size clientPixels, Point location, int refreshMs)
        {
            _ = title;
            ClientPixels = clientPixels;
            Location = location;
            IntervalMs = refreshMs;
        }

        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { }

        public Color Draw()
        {
            if (!IsVisible)
                throw new InvalidOperationException("Cannot paint a hidden Desktop window.");
            using var bitmap = new Bitmap(ClientPixels.Width, ClientPixels.Height,
                PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                _paint(graphics);
            }
            return bitmap.GetPixel(ClientPixels.Width / 2, ClientPixels.Height / 2);
        }

        public void Dispose()
        {
            DisposeCount++;
            IsVisible = false;
        }
    }
}
