using PinkieSysMon;
using PinkieSysMon.Widgets;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Public-safe synthetic image fixtures; no user artwork or production dashboards.
internal static class Issue122RasterSamplingTests
{
    public static void CheckerboardDownscaling()
    {
        foreach (var (sourceSize, targetSize) in new[] { (256, 32), (512, 24) })
        {
            var original = Checkerboard(sourceSize);
            using var asset = ImageAsset.FromBitmap(original);
            using var baseline = Render(targetSize, targetSize, canvas =>
                canvas.DrawBitmap(original, new SKRect(0, 0, targetSize, targetSize),
                    SKSamplingOptions.Default, null));
            using var filtered = Render(targetSize, targetSize, canvas =>
                asset.Draw(canvas, new SKRect(0, 0, targetSize, targetSize), loop: true));

            var baselineError = 0;
            var filteredError = 0;
            for (var y = 2; y < targetSize - 2; y++)
            for (var x = 2; x < targetSize - 2; x++)
            {
                var nearest = baseline.GetPixel(x, y);
                var result = filtered.GetPixel(x, y);
                baselineError += Math.Abs(nearest.Red - 128);
                filteredError += Math.Abs(result.Red - 128);
                Check(result.Alpha == 255 && Math.Abs(result.Red - 128) <= 18 &&
                      Math.Abs(result.Green - 128) <= 18 && Math.Abs(result.Blue - 128) <= 18,
                    "Strong raster reduction must low-pass alternating opaque pixels.");
            }

            Check(filteredError < baselineError / 4,
                "Prepared downscaling must materially reduce checkerboard aliasing versus nearest sampling.");
            Check(asset.ScaledFrameCount > 0 && asset.ScaledFrameBytes > 0 &&
                  asset.ScaledFrameBytes <= asset.ScaledFrameBudget,
                "Downscaled frames must be cached within the per-image memory budget.");
            var cachedCount = asset.ScaledFrameCount;
            using var repeated = Render(targetSize, targetSize, canvas =>
                asset.Draw(canvas, new SKRect(0, 0, targetSize, targetSize), loop: true));
            Check(asset.ScaledFrameCount == cachedCount,
                "Repeated identical image draws must reuse the prepared frame.");
        }
    }

    public static void NativePixelsAndTransformedScaling()
    {
        var original = Checkerboard(256);
        using var asset = ImageAsset.FromBitmap(original);
        using (var native = Render(256, 256, canvas =>
            asset.Draw(canvas, new SKRect(0, 0, 256, 256), loop: true)))
        {
            Check(native.GetPixel(0, 0) == SKColors.Black &&
                  native.GetPixel(1, 0) == SKColors.White &&
                  native.GetPixel(0, 1) == SKColors.White,
                "Native-resolution integer-aligned pixels must remain sharp.");
            Check(asset.ScaledFrameCount == 0,
                "Native-resolution drawing must not allocate reduced frames.");
        }

        using (var rotated = Render(80, 80, canvas =>
        {
            canvas.Translate(40, 40);
            canvas.RotateDegrees(20);
            asset.Draw(canvas, new SKRect(-16, -16, 16, 16), loop: true);
        }))
        {
            Check(rotated.GetPixel(40, 40).Alpha == 255 &&
                  asset.ScaledFrameBytes <= asset.ScaledFrameBudget,
                "Rotated raster content must use the bounded transformed sampling path.");
        }

        var twoPixel = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        twoPixel.SetPixel(0, 0, SKColors.Black);
        twoPixel.SetPixel(1, 0, SKColors.White);
        // FromBitmap takes ownership; the source bitmap has no other owner.
        using var enlargedAsset = ImageAsset.FromBitmap(twoPixel);
        using var enlarged = Render(20, 10, canvas =>
            enlargedAsset.Draw(canvas, new SKRect(0, 0, 20, 10), loop: true));
        Check(enlarged.GetPixel(0, 5).Red < 20 && enlarged.GetPixel(19, 5).Red > 235 &&
              enlarged.GetPixel(9, 5).Red is > 20 and < 235,
            "Enlargement should interpolate rather than render staircase nearest-neighbor pixels.");
    }

    public static void IconFileAlphaAndCacheLifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue122-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "edge.png");
            using (var alphaSource = new SKBitmap(
                new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                alphaSource.Erase(SKColors.Transparent);
                for (var y = 0; y < 256; y++)
                for (var x = 0; x < 128; x++)
                    alphaSource.SetPixel(x, y, SKColors.Red);
                using var skImage = SKImage.FromBitmap(alphaSource);
                using var data = skImage.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("Could not encode synthetic alpha test asset.");
                File.WriteAllBytes(path, data.ToArray());
            }

            using var source = ImageAsset.Load(path);
            using var file = ImageVisual.CreateImage(source, "stretch", loop: true, opacity: 1f, antialias: true);
            using var icon = RasterIconAsset.Load(path);
            using var fileRaster = Render(31, 31, canvas =>
                file.Draw(canvas, new SKRect(0, 0, 31, 31), antialias: true));
            using var iconRaster = Render(31, 31, canvas =>
                icon.Draw(canvas, new SKRect(0, 0, 31, 31), SKColors.Lime,
                    opacity: 1f, fit: "stretch", loop: true, antialias: true));

            for (var y = 4; y < 27; y++)
            for (var x = 0; x < 31; x++)
            {
                var f = fileRaster.GetPixel(x, y);
                var i = iconRaster.GetPixel(x, y);
                Check(f == i, "Raster icons and file images must share filtering and alpha rules.");
                if (f.Alpha > 16)
                    Check(f.Red > 245 && f.Green < 12 && f.Blue < 12,
                        "Premultiplied-alpha reduction must not introduce dark or bright edge fringes.");
            }

            Check(fileRaster.GetPixel(4, 15).Alpha == 255 &&
                  fileRaster.GetPixel(25, 15).Alpha == 0,
                "Strong transparent PNG reduction must preserve opaque and transparent regions.");

            using var largeSurface = SKSurface.Create(new SKImageInfo(256, 256))
                ?? throw new InvalidOperationException("Could not create cache budget regression surface.");
            for (var size = 8; size <= 256; size += 7)
            {
                source.Draw(largeSurface.Canvas, new SKRect(0, 0, size, size), loop: true);
                Check(source.ScaledFrameBytes <= source.ScaledFrameBudget,
                    "Variable destination sizes must not exceed the source-owned LRU cache budget.");
            }
            Check(source.ScaledFrameCount > 0, "Scaled asset cache must retain at least one prepared frame.");
            source.Dispose();
            Check(source.ScaledFrameBytes == 0 && source.ScaledFrameCount == 0,
                "Disposing an image asset must release its derived raster cache.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void RasterSharpnessChangesEdges()
    {
        static SKBitmap SlantedEdge()
        {
            var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
            for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                bitmap.SetPixel(x, y, x < 111 + y / 7 ? SKColors.Black : SKColors.White);
            return bitmap;
        }

        using var original = ImageAsset.FromBitmap(SlantedEdge());
        using var sharpened = ImageAsset.FromBitmap(SlantedEdge(), rasterSharpness: 75);
        using var defaultPixels = Render(32, 32, canvas =>
            original.Draw(canvas, new SKRect(0, 0, 32, 32), loop: true));
        using var sharpenedPixels = Render(32, 32, canvas =>
            sharpened.Draw(canvas, new SKRect(0, 0, 32, 32), loop: true));

        var changed = 0;
        for (var y = 2; y < 30; y++)
        for (var x = 2; x < 30; x++)
        {
            var baseline = defaultPixels.GetPixel(x, y);
            var tuned = sharpenedPixels.GetPixel(x, y);
            if (Math.Abs(baseline.Red - tuned.Red) > 2)
                changed++;
            Check(tuned.Alpha == 255 && tuned.Red == tuned.Green && tuned.Red == tuned.Blue,
                "Sharpened opaque images must retain alpha and neutral color.");
        }
        Check(changed > 0, "A nonzero sharpness setting must change scaled edge pixels.");
        Check(sharpened.ScaledFrameCount > 0 &&
              sharpened.ScaledFrameBytes <= sharpened.ScaledFrameBudget,
            "Sharpened reductions must reuse the same bounded scaled-frame cache.");
        using var native = Render(256, 256, canvas =>
            sharpened.Draw(canvas, new SKRect(0, 0, 256, 256), loop: true));
        Check(native.GetPixel(0, 0) == SKColors.Black &&
              native.GetPixel(255, 255) == SKColors.White,
            "Sharpness must not modify aligned native-resolution copies.");
    }

    public static void RasterSharpnessConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-raster-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new AppConfig();
            Check(config.Renderer.RasterSharpness == 0, "Existing configurations must default to original raster quality.");
            var path = Path.Combine(root, "app.json");
            config.Renderer.RasterSharpness = 65;
            config.Save(path);
            var restored = AppConfig.Load(path, new FileLogger(Path.Combine(root, "regression.log")));
            Check(restored.Renderer.RasterSharpness == 65,
                "Raster sharpness must survive application configuration save/reload.");
            foreach (var invalid in new[] { -1, 101 })
            {
                config.Renderer.RasterSharpness = invalid;
                var rejected = false;
                try { config.Save(path); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Invalid raster sharpness must be rejected before persistence.");
            }
            Check(AppConfig.Load(path, new FileLogger(Path.Combine(root, "regression.log")))
                  .Renderer.RasterSharpness == 65,
                "Rejected settings must not change the last saved raster quality.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void BinaryRasterSamplingParity()
    {
        const string metric = "test.binary.raster";
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-binary-raster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // GlobalAssetResolver requires an application root when TypefaceCache
            // resolves its font directory, even for image-only state profiles.
            var configDirectory = Path.Combine(root, "config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "app.json"), "{}");

            const string fileName = "synthetic.png";
            using (var pixels = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                    pixels.SetPixel(x, y, x < 111 + y / 7 ? SKColors.Red : SKColors.Transparent);
                using var image = SKImage.FromBitmap(pixels);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("Could not encode binary sampling fixture.");
                File.WriteAllBytes(Path.Combine(root, fileName), encoded.ToArray());
            }

            C.ImageAssetPresentationDefinition presentation = new()
            {
                SourceType = C.ImageAssetSourceType.File, Source = fileName,
                Fit = "stretch", Loop = true
            };
            var binary = new C.BinaryWidgetDefinition
            {
                Id = "binary-raster-test", Metric = metric, Width = 32, Height = 32,
                Profiles = new(StringComparer.OrdinalIgnoreCase)
                {
                    [BinarySignalContract.TrueKey] = new C.StateVisualProfileDefinition
                    {
                        ContentType = C.StateContentType.Image,
                        Asset = presentation
                    },
                    [BinarySignalContract.FalseKey] = new C.StateVisualProfileDefinition
                    {
                        ContentType = C.StateContentType.Image,
                        Asset = presentation
                    }
                }
            };
            var imageWidget = new C.ImageWidgetDefinition
            {
                Id = "image-raster-test", Width = 32, Height = 32,
                Asset = presentation
            };
            C.WidgetDefinition[] widgets = [binary, imageWidget];
            using var images = new ImageAssetCache(root, rasterSharpness: 65);
            using var icons = new IconAssetCache(root, rasterSharpness: 65);
            using var typefaces = new TypefaceCache(root, widgets);
            using var binaryRenderer = new BinaryWidgetRenderer(images, icons);
            using var imageRenderer = new ImageWidgetRenderer(images, icons);
            using var context = new WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = true },
                antialias: true, typefaces);
            using var binaryPixels = Render(32, 32, canvas =>
                WidgetTransform.Render(canvas, binary, context, binaryRenderer));
            using var imagePixels = Render(32, 32, canvas =>
                WidgetTransform.Render(canvas, imageWidget, context, imageRenderer));
            using var rasterIcon = RasterIconAsset.Load(Path.Combine(root, fileName), rasterSharpness: 65);
            using var iconPixels = Render(32, 32, canvas =>
                rasterIcon.Draw(canvas, new SKRect(0, 0, 32, 32), SKColors.White,
                    opacity: 1f, fit: "stretch", loop: true, antialias: true));
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var expected = imagePixels.GetPixel(x, y);
                Check(binaryPixels.GetPixel(x, y) == expected,
                    "Binary image state and Image widget must use identical scaled raster sampling.");
                Check(iconPixels.GetPixel(x, y) == expected,
                    "PNG icons and Binary image states must share the configured raster sampling.");
            }
            Check(images.Get(fileName).ScaledFrameCount > 0,
                "Binary and Image must reuse their configured source-owned downscale cache.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SKBitmap Checkerboard(int size)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            bitmap.SetPixel(x, y, ((x + y) & 1) == 0 ? SKColors.Black : SKColors.White);
        return bitmap;
    }

    private static SKBitmap Render(int width, int height, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height))
            ?? throw new InvalidOperationException("Could not allocate raster sampling regression surface.");
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode raster sampling regression snapshot.");
        return SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Could not decode raster sampling regression snapshot.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
