using System.Collections.Concurrent;
using System.Drawing;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95FrameGeometryTests
{
    public static void ExactPixelMapping()
    {
        foreach (var (width, height) in new[] { (17, 11), (11, 17), (13, 13) })
        foreach (var orientation in new[] { 0, 90, 180, 270 })
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var logicalSize = FrameGeometry.GetLogicalSize(width, height, orientation);
            using var logical = SKSurface.Create(new SKImageInfo(logicalSize.Width, logicalSize.Height));
            using var paint = new SKPaint { IsAntialias = false };
            for (var y = 0; y < logicalSize.Height; y++)
            for (var x = 0; x < logicalSize.Width; x++)
            {
                paint.Color = new SKColor((byte)(x * 11), (byte)(y * 13), 71);
                logical.Canvas.DrawRect(x, y, 1, 1, paint);
            }
            var profile = new PhysicalFrameProfile(rotation);
            var wireSize = profile.WireSize(width, height);
            using var wire = SKSurface.Create(new SKImageInfo(wireSize.Width, wireSize.Height));
            wire.Canvas.Clear(SKColors.Magenta);
            FrameGeometry.DrawLogicalToPhysicalWire(wire.Canvas, logical,
                logicalSize.Width, logicalSize.Height, width, height, orientation, profile);
            using var pixels = wire.PeekPixels();
            // Independent integer-coordinate oracle, with no renderer transforms.
            for (var y = 0; y < logicalSize.Height; y++)
            for (var x = 0; x < logicalSize.Width; x++)
            {
                var (px, py) = orientation switch
                {
                    90 => (width - 1 - y, x), 180 => (width - 1 - x, height - 1 - y),
                    270 => (y, height - 1 - x), _ => (x, y)
                };
                var (wx, wy) = rotation switch
                {
                    90 => (height - 1 - py, px), 180 => (width - 1 - px, height - 1 - py),
                    270 => (py, width - 1 - px), _ => (px, py)
                };
                Check(pixels.GetPixelColor(wx, wy) == new SKColor((byte)(x * 11), (byte)(y * 13), 71),
                    "Every logical pixel must map exactly once without scaling, cropping or inserted bars.");
            }
        }
    }

    public static void OrdinaryWorkerIgnoresPanelSize()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-geometry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (width, height) in new[] { (321, 207), (207, 321), (257, 257) })
            foreach (var orientation in new[] { 0, 90, 180, 270 })
            foreach (var encrypted in new[] { false, true })
            {
                var board = Board(width, height, orientation);
                var target = new OutputTargetConfig
                {
                    Id = "geometry", Name = "Unknown commercial display", Kind = "physical",
                    Transport = encrypted ? OutputTargetContract.EncryptedJpegTransport : OutputTargetContract.LyBulkTransport,
                    Width = encrypted ? 1920 : 800, Height = encrypted ? 720 : 600,
                    DeviceId = "usb-interface:" + new string('a', 64), DeviceEnabled = true, RefreshIntervalMs = 10
                };
                var profile = encrypted ? PhysicalFrameProfile.EncryptedJpeg : new PhysicalFrameProfile(180, TrofeoWireProtocol.MaximumImageBytes);
                var transport = new Frames(profile);
                using (var pump = new FramePump(new AppConfig(), target, board, new MetricStore(),
                    new FileLogger(Path.Combine(root, "worker.log")), (_, _, _, _) => transport, maximumFrames: 1))
                {
                    pump.Start();
                    Check(SpinWait.SpinUntil(() => pump.SentFrames == 1, 5000), "Ordinary worker must send the mismatched dashboard.");
                    using var decoded = SKBitmap.Decode(transport.Images.Single());
                    var expected = encrypted ? new Size(height, width) : new Size(width, height);
                    Check(decoded.Width == expected.Width && decoded.Height == expected.Height,
                        "Wire dimensions must follow Canvas and protocol rotation, never user panel dimensions.");
                    Check(decoded.GetPixel(decoded.Width / 2, decoded.Height / 2).Red > 240,
                        "The assigned dashboard must remain visible in the ordinary worker.");
                    Throws<InvalidDataException>(() => pump.PrepareReconfiguration(new AppConfig(), target, Board(5000, 10000)));
                    Check(pump.SentFrames == 1 && transport.Disposals == 0,
                        "Rejected oversized replacement must not retire the existing session.");
                }
                Check(transport.Disposals == 1 && transport.Images.Count == 1, "Worker disposal must release its one transport exactly once.");
                Check(board.Canvas.Width == width && board.Canvas.Height == height && board.Canvas.Orientation == orientation,
                    "Wire conversion must preserve the canonical document.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    public static void ResourceAndWireBounds()
    {
        FrameGeometry.ValidateRasterSize(4096, 4096);
        FrameGeometry.ValidateRasterSize(5000, 1000);
        foreach (var (width, height) in new[] { (4096, 4097), (5000, 10000), (int.MaxValue, int.MaxValue) })
            Throws<InvalidDataException>(() => new DashboardRenderer(Board(width, height)));
        Throws<ArgumentOutOfRangeException>(() => new DashboardRenderer(Board(0, 10)));
        Check(TrofeoWireProtocol.GetFrameLayout(TrofeoWireProtocol.MaximumImageBytes).ChunkCount == ushort.MaxValue,
            "The LY bound must include the mandatory terminal record in the 16-bit count.");
        Throws<ArgumentOutOfRangeException>(() => TrofeoWireProtocol.GetFrameLayout(TrofeoWireProtocol.MaximumImageBytes + 1));
        Throws<ArgumentOutOfRangeException>(() => TrofeoWireProtocol.GetFrameLayout(int.MaxValue));
        Check(PhysicalFrameProfile.EncryptedJpeg.MaximumImageBytes == 1_048_576,
            "The existing encrypted-image software bound must remain unchanged.");
        using var renderer = new DashboardRenderer(Board(321, 207));
        var metrics = new MetricStore().Snapshot();
        for (var i = 0; i < 4; i++)
        {
            Throws<InvalidDataException>(() => renderer.RenderJpeg(metrics, new PhysicalFrameProfile(90, 1), 100));
            Throws<ArgumentException>(() => renderer.RenderJpeg(metrics, new PhysicalFrameProfile(45), 100));
            using var jpeg = renderer.RenderJpeg(metrics, i % 2 == 0 ? PhysicalFrameProfile.EncryptedJpeg : new(180), 100);
            using var image = SKBitmap.Decode(jpeg.Bytes);
            Check(image.Width == (i % 2 == 0 ? 207 : 321), "A rejected frame/profile must leave subsequent renders usable.");
        }
    }

    private static C.DashboardDefinition Board(int width, int height, int orientation = 0) => new()
    { BaseDirectory = Environment.CurrentDirectory, Canvas = new() { Width = width, Height = height, Orientation = orientation, BackgroundColor = "#FF0000" } };
    private sealed class Frames(PhysicalFrameProfile profile) : IPhysicalFrameTransport
    {
        internal ConcurrentQueue<byte[]> Images { get; } = new();
        internal int Disposals;
        public int WireRotationDegrees => profile.RotationDegrees;
        public PhysicalFrameProfile Profile => profile;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics) => Images.Enqueue(jpeg.ToArray());
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
