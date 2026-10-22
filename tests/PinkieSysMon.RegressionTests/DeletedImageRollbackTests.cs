using System.Drawing;
using System.Text.Json;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Real per-session renderers, in-memory Desktop windows, and temporary image
// files. No native HWND, USB transport, or private dashboard is required.
internal static class DeletedImageRollbackTests
{
    public static void SelectedReload()
    {
        InSandbox((root, log) =>
        {
            SaveImage(root, "Test", "a.png", SKColors.Red);
            SaveImage(root, "Test", "b.png", SKColors.Blue);
            SaveImage(root, "Test", "c.png", SKColors.Green);
            var config = DesktopConfig(("d1", "Test"));
            var target = config.Outputs.Targets[0];
            var original = WriteBoard(root, "Test", "a.png");
            var windows = new Dictionary<string, TestWindow>();
            using var manager = DesktopManager(config,
                new Dictionary<string, C.DashboardDefinition> { ["d1"] = original }, log, windows);
            manager.Start();
            Check(IsRed(windows["d1"].Draw()), "The initial image must be rendered before replacement.");

            var replacement = WriteBoard(root, "Test", "b.png");
            File.Delete(ImagePath(root, "Test", "a.png"));
            Check(!File.Exists(ImagePath(root, "Test", "a.png")) &&
                  !File.ReadAllText(BoardPath(root, "Test")).Contains("a.png", StringComparison.Ordinal),
                "The former asset must be absent from disk and from the saved dashboard.");

            manager.ReloadTarget(target, replacement, _ => { });
            Check(IsBlue(windows["d1"].Draw()) &&
                  manager.GetDashboardAssignments()["d1"].Widgets.OfType<C.ImageWidgetDefinition>()
                    .Single().Asset.Source == "images/b.png",
                "Selected Reload must commit the new image without reopening the old deleted file.");

            // Failed Desktop window reconfiguration must keep the currently loaded
            // image and rollback even when its source disappears afterward.
            var next = WriteBoard(root, "Test", "c.png");
            File.Delete(ImagePath(root, "Test", "b.png"));
            windows["d1"].FailNextConfigure = true;
            Check(Throws<InvalidOperationException>(() =>
                    manager.ReloadTarget(target, next, _ => { })) &&
                  IsBlue(windows["d1"].Draw()),
                "Failed selected Reload must preserve the prior decoded image without its file.");
        });
    }

    public static void MultiOutputRollback()
    {
        InSandbox((root, log) =>
        {
            foreach (var name in new[] { "Left", "Right" })
            {
                SaveImage(root, name, "a.png", SKColors.Red);
                SaveImage(root, name, "b.png", SKColors.Blue);
                WriteBoard(root, name, "a.png");
            }

            var config = DesktopConfig(("d1", "Left"), ("d2", "Right"));
            var initial = LoadBoards(root, config);
            var windows = new Dictionary<string, TestWindow>();
            using var manager = DesktopManager(config, initial, log, windows);
            manager.Start();
            Check(IsRed(windows["d1"].Draw()) && IsRed(windows["d2"].Draw()),
                "Both independent Desktop Outputs must render their original images.");

            foreach (var name in new[] { "Left", "Right" })
            {
                WriteBoard(root, name, "b.png");
                File.Delete(ImagePath(root, name, "a.png"));
            }

            var nextBoards = LoadBoards(root, config);
            windows["d2"].FailNextConfigure = true;
            Check(Throws<InvalidOperationException>(() => manager.Reconfigure(config, nextBoards)),
                "A failed second Output commit must fail the aggregate reconfiguration.");
            Check(IsRed(windows["d1"].Draw()) && IsRed(windows["d2"].Draw()) &&
                  manager.GetDashboardAssignments()["d1"].Widgets.OfType<C.ImageWidgetDefinition>()
                    .Single().Asset.Source == "images/a.png",
                "Aggregate failure must restore BOTH previous live renderers after old image files disappear.");

            manager.Reconfigure(config, nextBoards);
            Check(IsBlue(windows["d1"].Draw()) && IsBlue(windows["d2"].Draw()),
                "Retry must commit both saved image replacements without restarting sessions.");
        });
    }

    public static void PhysicalRendererRollback()
    {
        InSandbox((root, log) =>
        {
            SaveImage(root, "Physical", "a.png", SKColors.Red);
            SaveImage(root, "Physical", "b.png", SKColors.Blue);
            var oldBoard = WriteBoard(root, "Physical", "a.png");
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        { Width = 1920, Height = 480, DeviceId = "usb-interface:9db149bf534090a5954f8f6ccb89e434ecf3a469845f14ae5b1bceedaa15b70c",
                            Id = "physical", Name = "Physical",
                            Kind = OutputTargetContract.PhysicalKind,
                            Transport = OutputTargetContract.LyBulkTransport,
                            Dashboard = "Physical", DeviceEnabled = true
                        }
                    ]
                }
            };
            var target = config.Outputs.Targets[0];
            using var pump = new FramePump(config, target, oldBoard, new MetricStore(), log);
            var nextBoard = WriteBoard(root, "Physical", "b.png");
            using var prepared = pump.PrepareReconfiguration(config, target, nextBoard);
            using var rollback = pump.PrepareRollbackReconfiguration(config, target, oldBoard);
            File.Delete(ImagePath(root, "Physical", "a.png"));

            pump.CommitReconfiguration(prepared);
            pump.CommitReconfiguration(rollback);
            // This also verifies that the restored renderer is still owned and usable.
            using var restored = pump.PrepareRollbackReconfiguration(config, target, oldBoard);
        });
    }

    private static OutputSessionManager DesktopManager(
        AppConfig config, IReadOnlyDictionary<string, C.DashboardDefinition> boards,
        FileLogger log, Dictionary<string, TestWindow> windows) =>
        new(config, boards, new MetricStore(), log,
            (app, target, board, metrics, logger) =>
                new DesktopOutputSession(app, target, board, metrics, logger, render =>
                {
                    var window = new TestWindow(render);
                    windows[target.Id] = window;
                    return window;
                }));

    private static AppConfig DesktopConfig(params (string Id, string Board)[] outputs) => new()
    {
        Outputs = new OutputConfig
        {
            Targets = outputs.Select(t => new OutputTargetConfig
            {
                Id = t.Id,
                Name = t.Id,
                Kind = OutputTargetContract.DesktopKind,
                Transport = OutputTargetContract.DesktopTransport,
                Dashboard = t.Board,
                Desktop = new DesktopOutputConfig { BoardEnabled = true, Lock = false }
            }).ToList()
        }
    };

    private static IReadOnlyDictionary<string, C.DashboardDefinition> LoadBoards(
        string root, AppConfig config) =>
        config.Outputs.Targets.ToDictionary(t => t.Id,
            t => C.DashboardDefinition.Load(BoardPath(root, t.Dashboard!)));

    private static C.DashboardDefinition WriteBoard(string root, string name, string imageName)
    {
        var path = BoardPath(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var image = new C.ImageAssetPresentationDefinition
        {
            SourceType = C.ImageAssetSourceType.File,
            Source = "images/" + imageName
        };
        var profile = new C.StateVisualProfileDefinition
        {
            ContentType = C.StateContentType.Image,
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File,
                Source = "images/" + imageName
            }
        };
        var board = new C.DashboardDefinition
        {
            Canvas = new C.CanvasDefinition { Width = 16, Height = 16, BackgroundColor = "#FF000000" },
            Widgets =
            [
                new C.ImageWidgetDefinition { Id = "image", Width = 16, Height = 16, Asset = image },
                new C.BinaryWidgetDefinition
                {
                    Id = "binary", Metric = "test.boolean", Width = 16, Height = 16,
                    Profiles = new Dictionary<string, C.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["true"] = profile,
                        ["false"] = new C.StateVisualProfileDefinition
                        {
                            ContentType = C.StateContentType.Value
                        }
                    }
                }
            ]
        };
        // Binary is placed below the always-visible image, but both paths are preloaded.
        board.Widgets[1].Z = -1;
        File.WriteAllText(path, JsonSerializer.Serialize(board));
        return C.DashboardDefinition.Load(path);
    }

    private static string BoardPath(string root, string name) =>
        Path.Combine(root, "dashboards", name, "dashboard.json");

    private static string ImagePath(string root, string name, string image) =>
        Path.Combine(root, "dashboards", name, "images", image);

    private static void SaveImage(string root, string name, string image, SKColor color)
    {
        var path = ImagePath(root, name, image);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(color);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, encoded.ToArray());
    }

    private static void InSandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue32-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        try { action(root, new FileLogger(Path.Combine(root, "issue32.log"))); }
        finally { Directory.Delete(root, true); }
    }

    private static bool IsRed(SKColor c) => c.Red > 230 && c.Blue < 20 && c.Green < 20;
    private static bool IsBlue(SKColor c) => c.Blue > 230 && c.Red < 20 && c.Green < 20;
    private static bool Throws<T>(Action work) where T : Exception
    {
        try { work(); }
        catch (T) { return true; }
        return false;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestWindow(Action draw) : IDesktopOutputWindow
    {
        private bool _visible;
        private SKColor _pixel;
        public bool FailNextConfigure { get; set; }
        public bool IsVisible => _visible;
        public void Configure(string title, Size size, Point location, int refreshMs, bool locked)
        {
            if (FailNextConfigure)
            {
                FailNextConfigure = false;
                throw new InvalidOperationException("Synthetic window configuration failure.");
            }
        }
        public void ShowWindow() => _visible = true;
        public void HideWindow() => _visible = false;
        public void RequestFrame() { }
        public void Present(SKBitmap bitmap) => _pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        public SKColor Draw()
        {
            if (!_visible) throw new InvalidOperationException("Test window is hidden.");
            draw();
            return _pixel;
        }
        public void Dispose() => _visible = false;
    }
}
