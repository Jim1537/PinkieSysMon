using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class SharedAlphaStage08Tests
{
    private static readonly IReadOnlyDictionary<string, object?> NoMetrics =
        new Dictionary<string, object?>();

    public static void TransparentFrameAndJpeg()
    {
        InSandbox(root =>
        {
            WriteImage(root, "green.png", SKColors.Lime);
            using var renderer = new DashboardRenderer(Dashboard(root));
            using var alpha = renderer.RenderBgra(NoMetrics);
            Check(alpha.Bitmap.ColorType == SKColorType.Bgra8888 &&
                  alpha.Bitmap.AlphaType == SKAlphaType.Premul &&
                  alpha.Bitmap.Width == 64 && alpha.Bitmap.Height == 32,
                "Desktop frame must expose logical-size premultiplied BGRA pixels.");
            Check(alpha.Bitmap.GetPixel(2, 2).Alpha == 0,
                "Transparent Canvas background must remain transparent in the BGRA frame.");
            var center = alpha.Bitmap.GetPixel(16, 12);
            Check(center.Alpha == 255 && center.Green > 220 && center.Red < 20,
                "Opaque image widget must render above transparent Canvas.");

            using var jpeg = renderer.RenderJpeg(NoMetrics, wireRotationDegrees: 0, jpegQuality: 100);
            using var wire = SKBitmap.Decode(jpeg.EncodedData)
                ?? throw new InvalidOperationException("Could not decode physical JPEG output.");
            var blank = wire.GetPixel(2, 2);
            Check(wire.Width == 64 && wire.Height == 32 && blank.Alpha == 255 &&
                  blank.Red < 20 && blank.Green < 20 && blank.Blue < 20,
                "Physical JPEG must keep opaque black wire background.");
            Check(wire.GetPixel(16, 12).Green > 180,
                "Physical JPEG must still render canonical widget content.");
            Check(alpha.Bitmap.GetPixel(2, 2).Alpha == 0,
                "Encoding JPEG must never flatten a previously returned BGRA frame.");
        });
    }

    public static void LayersAndPreviewParity()
    {
        InSandbox(root =>
        {
            WriteImage(root, "red.png", SKColors.Red);
            WriteImage(root, "green.png", SKColors.Lime);
            WriteImage(root, "blue.png", SKColors.Blue);
            var dashboard = Dashboard(root, orientation: 90);
            dashboard.Canvas.BackgroundImage = Layer("red.png", 0.5f);
            dashboard.Canvas.ForegroundImage = Layer("blue.png", 0.5f);
            dashboard.Widgets[0].X = 8;
            dashboard.Widgets[0].Y = 20;

            using var runtime = new DashboardRenderer(dashboard);
            using var alpha = runtime.RenderBgra(NoMetrics);
            using var editor = new DashboardPreviewRenderer(dashboard);
            using var preview = SKBitmap.Decode(editor.RenderToPng(NoMetrics))
                ?? throw new InvalidOperationException("Could not decode Editor PNG parity frame.");

            Check(alpha.Bitmap.Width == 32 && alpha.Bitmap.Height == 64 &&
                  preview.Width == 32 && preview.Height == 64,
                "Orientation 90 must preserve logical Canvas dimensions.");
            var outside = alpha.Bitmap.GetPixel(2, 2);
            var onWidget = alpha.Bitmap.GetPixel(16, 26);
            Check(outside.Alpha is >= 180 and <= 200 && onWidget.Alpha == 255,
                "Semi-transparent Canvas layers must blend without flattening.");
            Check(outside.Red > 40 && outside.Blue > 80 &&
                  onWidget.Green > 80 && onWidget.Blue > 80,
                "Background, image widget and foreground must retain render order.");
            AssertNear(preview.GetPixel(2, 2), outside, 3,
                "Editor/Runtime diverged on Canvas background layers.");
            AssertNear(preview.GetPixel(16, 26), onWidget, 3,
                "Editor/Runtime diverged on widget and foreground.");
        });
    }

    public static void OwnershipAndDisposal()
    {
        InSandbox(root =>
        {
            WriteImage(root, "green.png", SKColors.Lime);
            var renderer = new DashboardRenderer(Dashboard(root));
            DashboardRenderer.RenderedBgra? first = null;
            DashboardRenderer.RenderedBgra? second = null;
            try
            {
                first = renderer.RenderBgra(NoMetrics);
                second = renderer.RenderBgra(NoMetrics);
                Check(!ReferenceEquals(first.Bitmap, second.Bitmap) &&
                      first.Bitmap.GetPixel(16, 12).Green > 220,
                    "Subsequent BGRA frames must have independent pixels.");
                renderer.Dispose();
                Check(first.Bitmap.GetPixel(16, 12).Green > 220 &&
                      second.Bitmap.GetPixel(2, 2).Alpha == 0,
                    "Frames must remain valid after renderer assets/surfaces are disposed.");
                first.Dispose();
                first.Dispose();
                Check(Throws<ObjectDisposedException>(() => { _ = first.Bitmap; }),
                    "Disposed frame must not expose native raster memory.");
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
                renderer.Dispose();
            }
        });
    }

    private static C.DashboardDefinition Dashboard(string root, int orientation = 0) => new()
    {
        BaseDirectory = root,
        Canvas = new C.CanvasDefinition
        {
            Width = 64, Height = 32, Orientation = orientation,
            BackgroundColor = "#00000000"
        },
        Widgets =
        [
            new C.ImageWidgetDefinition
            {
                Id = "mark", X = 8, Y = 6, Width = 16, Height = 12,
                Asset = new C.ImageAssetPresentationDefinition
                {
                    SourceType = C.ImageAssetSourceType.File, Source = "green.png",
                    Fit = StateVisualProfileContract.FitStretch
                }
            }
        ]
    };

    private static C.CanvasImageLayerDefinition Layer(string file, float opacity) => new()
    {
        Asset = new C.ImageAssetPresentationDefinition
        {
            SourceType = C.ImageAssetSourceType.File, Source = file,
            Fit = StateVisualProfileContract.FitStretch
        },
        Opacity = opacity
    };

    private static void WriteImage(string root, string file, SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode test PNG.");
        File.WriteAllBytes(Path.Combine(root, file), encoded.ToArray());
    }

    private static void InSandbox(Action<string> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-alpha08-" + Guid.NewGuid().ToString("N"));
        var dashboard = Path.Combine(root, "dashboards", "stage08");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            Directory.CreateDirectory(dashboard);
            File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
            work(dashboard);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNear(SKColor expected, SKColor actual, int tolerance, string message)
    {
        Check(Math.Abs(expected.Red - actual.Red) <= tolerance &&
              Math.Abs(expected.Green - actual.Green) <= tolerance &&
              Math.Abs(expected.Blue - actual.Blue) <= tolerance &&
              Math.Abs(expected.Alpha - actual.Alpha) <= tolerance,
            message + " Preview=" + expected + "; Runtime=" + actual + ".");
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
}
