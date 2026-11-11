using System.Drawing;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopCanvasShadowStage05Tests
{
    public static void ViewportGeometryAndInputMapping()
    {
        var canvas = new C.CanvasDefinition { Width = 40, Height = 24, Orientation = 90 };
        var normal = DesktopCanvasViewport.Create(canvas);
        Check(!normal.HasShadow && normal.Canvas == new Size(24, 40) &&
              normal.Surface == normal.Canvas && normal.CanvasOffset == Point.Empty,
            "Unshadowed Desktop Canvas must retain the exact established client size.");

        canvas.ShadowEnabled = true;
        canvas.ShadowBlur = 2;
        canvas.ShadowOffsetX = -8;
        canvas.ShadowOffsetY = 6;
        var viewport = DesktopCanvasViewport.Create(canvas);
        Check(viewport.Canvas == new Size(24, 40) &&
              viewport.CanvasOffset == new Point(15, 1) &&
              viewport.Surface == new Size(39, 54),
            "Shadow margins must cover Gaussian spread and signed offsets in oriented logical pixels.");
        var desired = new Point(-120, 47);
        var surface = viewport.SurfaceOriginForCanvas(desired);
        Check(surface == new Point(-135, 46) &&
              viewport.CanvasOriginForSurface(surface) == desired &&
              viewport.CanvasPointForSurface(new Point(15, 1)) == Point.Empty &&
              !viewport.ContainsCanvasPixel(new Point(14, 10)) &&
              viewport.ContainsCanvasPixel(new Point(15, 1)) &&
              viewport.ContainsCanvasPixel(new Point(38, 40)) &&
              !viewport.ContainsCanvasPixel(new Point(38, 41)),
            "Native surface placement and input clipping must preserve saved Canvas origin and pixel edges.");

        canvas.ShadowColor = "#00000000";
        var invisible = DesktopCanvasViewport.Create(canvas);
        Check(!invisible.HasShadow && invisible.Surface == invisible.Canvas,
            "Invisible shadows must allocate no extra native pixels.");

        canvas.ShadowColor = "#FF000000";
        canvas.ShadowBlur = 100_000f;
        Check(Throws<InvalidDataException>(() => DesktopCanvasViewport.Create(canvas)),
            "Unbounded shadow blur must be rejected before any native allocation.");
    }

    public static void ContourAlphaAndCachedComposition()
    {
        var root = new C.CanvasDefinition
        {
            Width = 40, Height = 24, ShadowEnabled = true,
            ShadowBlur = 0, ShadowOffsetX = 8, ShadowOffsetY = 3,
            ShadowColor = "#BF000000", CornerRadius = 6
        };
        var viewport = DesktopCanvasViewport.Create(root);
        Check(viewport.Surface == new Size(49, 28),
            "Offset Canvas silhouette must fit fully within the native padded surface.");
        using var compositor = new DesktopCanvasShadowCompositor(viewport, root);
        var info = new SKImageInfo(root.Width, root.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var blank = new SKBitmap(info);
        blank.Erase(SKColors.Transparent);

        var first = compositor.Compose(blank);
        var outsideShadowAlpha = first.GetPixel(44, 12).Alpha;
        Check(first.ColorType == SKColorType.Bgra8888 && first.AlphaType == SKAlphaType.Premul &&
              first.GetPixel(44, 12).Alpha > 100 && first.GetPixel(44, 12).Red == 0 &&
              first.GetPixel(1, 1).Alpha == 0 &&
              blank.GetPixel(20, 12).Alpha == 0,
            "Only the Canvas contour may generate the outside shadow; logical input remains unchanged.");

        using var content = new SKBitmap(info);
        content.Erase(SKColors.Transparent);
        using (var contentCanvas = new SKCanvas(content))
        using (var paint = new SKPaint { Color = SKColors.Red })
            contentCanvas.DrawRect(new SKRect(10, 8, 20, 16), paint);

        var second = compositor.Compose(content);
        Check(ReferenceEquals(first, second) &&
              second.GetPixel(15, 12).Red > 230 &&
              second.GetPixel(44, 12).Alpha == outsideShadowAlpha &&
              content.GetPixel(44 - 8, 12).Alpha == 0,
            "The reusable desktop buffer must preserve foreground content and keep a fixed silhouette shadow.");
    }

    public static void SessionShadowReloadAndRollback()
    {
        var applicationRoot = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-issue113-session-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(applicationRoot, "dashboards", "shadow-session");
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(applicationRoot, "config"));
            File.WriteAllText(Path.Combine(applicationRoot, "config", "app.json"), "{}");

            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "desktop-shadow", Name = "Canvas shadow test",
                            Kind = OutputTargetContract.DesktopKind,
                            Transport = OutputTargetContract.DesktopTransport,
                            Dashboard = "Default", ProviderEnabled = false,
                            Desktop = new DesktopOutputConfig
                            {
                                BoardEnabled = true, Lock = false, X = 20, Y = 30
                            }
                        }
                    ]
                }
            };
            var target = config.Outputs.Targets.Single();
            var shadowed = new C.DashboardDefinition
            {
                BaseDirectory = directory,
                Canvas = new C.CanvasDefinition
                {
                    Width = 40, Height = 24, ShadowEnabled = true,
                    ShadowBlur = 0, ShadowOffsetX = 8, ShadowOffsetY = 3,
                    ShadowColor = "#FF000000", PanelBackground = new C.WidgetPanelLayerDefinition
                    {
                        Mode = C.WidgetPanelLayerMode.Color, Color = "#FFFF0000"
                    }
                }
            };
            var plain = new C.DashboardDefinition
            {
                BaseDirectory = directory,
                Canvas = new C.CanvasDefinition
                {
                    Width = 40, Height = 24,
                    PanelBackground = new C.WidgetPanelLayerDefinition
                    {
                        Mode = C.WidgetPanelLayerMode.Color, Color = "#FFFF0000"
                    }
                }
            };

            var store = new MetricStore();
            var log = new FileLogger(Path.Combine(applicationRoot, "shadow-test.log"));
            ProbeWindow? probe = null;
            using var session = new DesktopOutputSession(config, target, shadowed,
                store, log, render => probe = new ProbeWindow(render));
            session.Start();
            session.RequestFrame();
            var window = probe ?? throw new InvalidOperationException("Desktop test window was not created.");
            Check(window.Viewport is { HasShadow: true } &&
                  window.Viewport.Canvas == new Size(40, 24) &&
                  window.Viewport.Surface == new Size(49, 28) &&
                  window.LastFrameSize == window.Viewport.Surface &&
                  window.OutsideShadowAlpha > 0,
                "Desktop session must present a padded contour shadow without mutating the logical canvas.");

            using var rollback = session.PrepareRollbackReconfiguration(config, target, shadowed);
            using (var update = session.PrepareReconfiguration(config, target, plain))
                session.CommitReconfiguration(update);
            session.RequestFrame();
            Check(window.Viewport is { HasShadow: false } &&
                  window.LastFrameSize == new Size(40, 24),
                "Disabling root shadow must immediately restore the unpadded native frame.");

            session.CommitReconfiguration(rollback);
            session.RequestFrame();
            Check(window.Viewport is { HasShadow: true } &&
                  window.LastFrameSize == new Size(49, 28) &&
                  window.OutsideShadowAlpha > 0,
                "A prepared rollback must restore the original viewport and shadow resources.");
        }
        finally
        {
            if (Directory.Exists(applicationRoot))
                Directory.Delete(applicationRoot, recursive: true);
        }
    }

    private sealed class ProbeWindow(Action render) : IDesktopOutputWindow
    {
        private readonly Action _render = render;
        public DesktopCanvasViewport? Viewport { get; private set; }
        public Size LastFrameSize { get; private set; }
        public byte OutsideShadowAlpha { get; private set; }
        public bool IsVisible { get; private set; }

        public void ConfigureViewport(DesktopCanvasViewport viewport) => Viewport = viewport;

        public void Configure(string title, Size clientPixels, Point location, int refreshMs, bool locked)
        {
            _ = title; _ = location; _ = refreshMs; _ = locked;
            if (Viewport?.Canvas != clientPixels)
                throw new InvalidOperationException("Canvas origin contract was lost during Desktop configuration.");
        }

        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { if (IsVisible) _render(); }
        public void Present(SKBitmap bitmap)
        {
            LastFrameSize = new Size(bitmap.Width, bitmap.Height);
            OutsideShadowAlpha = bitmap.Width > 44 ? bitmap.GetPixel(44, 12).Alpha : (byte)0;
        }
        public void Dispose() => IsVisible = false;
    }

    private static bool Throws<T>(Action operation) where T : Exception
    {
        try { operation(); }
        catch (T) { return true; }
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
