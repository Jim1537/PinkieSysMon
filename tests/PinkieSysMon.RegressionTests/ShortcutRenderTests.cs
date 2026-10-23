using PinkieSysMon.Widgets;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutRenderTests
{
    public static void ShellArtworkCacheLifetime()
    {
        var calls = 0; SKBitmap? borrowed = null;
        using (var cache = new ShellIconCache(_ =>
        {
            Interlocked.Increment(ref calls); borrowed = new SKBitmap(2, 2);
            return ImageAsset.FromBitmap(borrowed);
        }))
        {
            ImageAsset? ready = null;
            Check(SpinWait.SpinUntil(() => (ready = cache.Get("unchanged target", out _)) is not null, TimeSpan.FromSeconds(5)), "Asynchronous artwork must become available without a Shell call on the render thread.");
            for (var i = 0; i < 100; i++) Check(ReferenceEquals(ready, cache.Get("unchanged target", out _)), "Repeated frames must borrow the same extracted asset.");
            Check(calls == 1, "The exact Target must be extracted only once per cache.");
        }
        Check(borrowed!.Handle == IntPtr.Zero, "Cache disposal must release its completed native bitmap.");

        using var entered = new CountdownEvent(2); using var release = new ManualResetEventSlim();
        var late = new System.Collections.Concurrent.ConcurrentBag<SKBitmap>(); var originals = 0;
        using var retired = new ShellIconCache(_ =>
        {
            Interlocked.Increment(ref originals); entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Artwork test did not release the native-call stand-in.");
            var bitmap = new SKBitmap(2, 2); late.Add(bitmap); return ImageAsset.FromBitmap(bitmap);
        });
        try
        {
            retired.Get("first", out _); retired.Get("second", out _);
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "Both bounded extraction workers must enter the test.");
            retired.Get("cancelled queued target", out _); retired.Dispose();
            using var successor = new ShellIconCache(_ => ImageAsset.FromBitmap(new SKBitmap(2, 2)));
            Check(successor.Get("replacement target", out var pending) is null && pending, "Retiring caches must not create extra process-wide native workers.");
            release.Set();
            Check(SpinWait.SpinUntil(() => successor.Get("replacement target", out _) is not null, TimeSpan.FromSeconds(5)), "Replacement artwork must progress after in-flight workers release ownership.");
            Check(SpinWait.SpinUntil(() => late.Count == 2 && late.All(bitmap => bitmap.Handle == IntPtr.Zero), TimeSpan.FromSeconds(5)) && originals == 2,
                "Retirement must cancel queued extraction and dispose both late native results.");
        }
        finally { release.Set(); }
    }

    public static void LabelsEffectsAndSharedArtwork()
    {
        var sourceRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "fonts", "Roboto"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        File.Copy(Path.Combine(sourceRoot, "assets", "fonts", "Roboto", "Roboto-Regular.ttf"),
            Path.Combine(root, "assets", "fonts", "Roboto", "Roboto-Regular.ttf"));
        try
        {
            using (var bitmap = new SKBitmap(2, 1))
            {
                bitmap.SetPixel(0, 0, SKColors.Blue); bitmap.SetPixel(1, 0, SKColors.Lime);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(Path.Combine(root, "images", "art.png")); encoded.SaveTo(stream);
            }
            var shortcut = new C.ShortcutWidgetDefinition
            {
                Id = "render", X = 70, Y = 70, Width = 90, Height = 60, Label = "LABEL LABEL LABEL",
                BorderColor = "#FFFF0000", BorderWidth = 2, CornerRadius = 12,
                Asset = new() { SourceType = "file", Source = "images/art.png", Fit = "contain" },
                TextPresentation = new() { FontFamily = "Roboto", FontSize = 18, Align = "center", OverflowMode = "Clip" }
            };
            using var images = new ImageAssetCache(root); using var icons = new IconAssetCache(root);
            images.Preload([shortcut]); icons.Preload([shortcut]);
            Check(ReferenceEquals(images.Get("images/art.png"), images.Get(shortcut.Asset.Source!)), "Shortcut must borrow the ordinary shared image cache.");
            using var fonts = new TypefaceCache(root, [shortcut]);
            using var context = new WidgetRenderContext(new Dictionary<string, object?>(), true, fonts);
            using var renderer = new ShortcutWidgetRenderer(images, icons);
            foreach (var mode in ValueOverflowContract.ConstrainedModes)
            foreach (var position in ShortcutContract.LabelPositions)
            foreach (var gap in new[] { -40f, 0, 4 })
            {
                shortcut.TextPresentation.OverflowMode = mode; shortcut.LabelPosition = position; shortcut.Gap = gap;
                context.InvalidateGeometry(shortcut);
                var measured = ValueTextLayout.CreateFixedLayout(shortcut, context, shortcut.Label, shortcut.TextPresentation.ToRenderProfile());
                var label = ShortcutLabelLayout.GetBounds(shortcut, context)!.Value;
                var expectedY = position == ShortcutContract.Top ? shortcut.Y - gap - measured.Bounds.Height :
                    position == ShortcutContract.Middle ? shortcut.Y + (shortcut.Height - measured.Bounds.Height) / 2 : shortcut.Y + shortcut.Height + gap;
                Check(Math.Abs(label.Top - expectedY) < .001 && label.Width == shortcut.Width && Math.Abs(label.Height - measured.Bounds.Height) < .001,
                    "Label position must use the full shared text block and signed Gap; Middle ignores Gap.");
                var container = WidgetGeometry.GetContainerBounds(shortcut, context);
                var pivot = WidgetGeometry.GetRotationCenter(shortcut, context);
                var visual = WidgetGeometry.GetVisualBounds(shortcut, context);
                Check(container == new SKRect(70, 70, 160, 130) && pivot == new SKPoint(115, 100) && visual.Top <= label.Top && visual.Bottom >= label.Bottom,
                    "Label must enlarge visual bounds without changing the graphic container or rotation pivot.");
                using var pixels = new SKBitmap(300, 300); using var canvas = new SKCanvas(pixels); canvas.Clear(SKColors.Transparent);
                WidgetTransform.Render(canvas, shortcut, context, renderer); canvas.Flush();
                if (position == ShortcutContract.Bottom && gap == 4)
                    Check(Enumerable.Range(134, Math.Min(80, 300 - 134)).Any(y => Enumerable.Range(70, 90).Any(x => pixels.GetPixel(x, y).Alpha > 0)),
                        "Label must paint below a rounded graphic, after leaving its graphic clip.");
            }
            shortcut.Label = ""; shortcut.Rotation = 27; shortcut.ShadowEnabled = true; context.InvalidateGeometry(shortcut);
            Check(ShortcutLabelLayout.GetBounds(shortcut, context) is null && WidgetGeometry.GetRotationCenter(shortcut, context) == new SKPoint(115, 100),
                "Empty labels must reserve no bounds and keep the graphic pivot.");
            foreach (var fit in new[] { "contain", "cover", "stretch" })
            {
                using var visual = ImageVisual.Create(images, icons, "file", "images/art.png", fit, true, 0, "#FFFF00FF", true);
                using var bitmap = new SKBitmap(80, 50); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
                visual.Draw(canvas, new SKRect(0, 0, 80, 50), true);
                Check(Enumerable.Range(0, 50).All(y => Enumerable.Range(0, 80).All(x => bitmap.GetPixel(x, y).Alpha == 0)), "Shared ImageVisual must honor zero graphic opacity for every Fit.");
            }
            shortcut.Label = "LITERAL 00123";
            shortcut.TextPresentation.OverflowMode = ValueOverflowContract.Clip;
            var board = new C.DashboardDefinition { BaseDirectory = root, Widgets = [shortcut], Canvas = new() { Width = 300, Height = 300, BackgroundColor = "#00000000" } };
            using var runtime = new DashboardRenderer(board); using var preview = new DashboardPreviewRenderer(board);
            var metrics = new Dictionary<string, object?>();
            using var actual = runtime.RenderBgra(metrics); using var expected = SKBitmap.Decode(preview.RenderToPng(metrics));
            for (var y = 0; y < 300; y++) for (var x = 0; x < 300; x++)
                Check(actual.Bitmap.GetPixel(x, y) == expected.GetPixel(x, y), "Runtime and Editor must share the exact Shortcut label/rotation/shadow raster.");
            shortcut.Rotation = 0; shortcut.ShadowEnabled = false; shortcut.LabelPosition = ShortcutContract.Bottom;
            shortcut.Gap = 1_500_000; context.InvalidateGeometry(shortcut);
            using var distant = new SKBitmap(300, 300); using var distantCanvas = new SKCanvas(distant);
            distantCanvas.Clear(SKColors.Transparent); distantCanvas.Translate(0, -1_500_000);
            WidgetTransform.Render(distantCanvas, shortcut, context, renderer); distantCanvas.Flush();
            Check(Enumerable.Range(130, 40).Any(y => Enumerable.Range(70, 90).Any(x => distant.GetPixel(x, y).Alpha > 0)),
                "Finite large Gap must retain the real vertical canvas clip rather than a hardcoded coordinate ceiling.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
