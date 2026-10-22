using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71RuntimeTests
{
    public static void GeometryAndLegacyParity()
    {
        foreach (var orientation in new[] { 0, 90, 180, 270 })
        {
            var size = FrameGeometry.GetLogicalSize(80, 30, orientation);
            using var logical = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
            logical.Canvas.Clear(SKColors.Black);
            using var paint = new SKPaint();
            foreach (var (x, y, color) in new[] { (0, 0, SKColors.Red), (size.Width / 2, 0, SKColors.Lime),
                (0, size.Height / 2, SKColors.Blue), (size.Width / 2, size.Height / 2, SKColors.Yellow) })
            {
                paint.Color = color;
                logical.Canvas.DrawRect(x, y, size.Width / 2, size.Height / 2, paint);
            }
            foreach (var rotation in new[] { 0, 180 })
            {
                using var oldWire = SKSurface.Create(new SKImageInfo(80, 30));
                using var newWire = SKSurface.Create(new SKImageInfo(80, 30));
                FrameGeometry.DrawLogicalToWire(oldWire.Canvas, logical, size.Width, size.Height, 80, 30, orientation, rotation);
                FrameGeometry.DrawLogicalToPhysicalWire(newWire.Canvas, logical, size.Width, size.Height, 80, 30, orientation, new(rotation));
                using var oldPixels = oldWire.PeekPixels();
                using var newPixels = newWire.PeekPixels();
                Check(oldPixels.GetPixelSpan().SequenceEqual(newPixels.GetPixelSpan()), "Every legacy orientation/rotation must retain exact raster bytes.");
            }
            using var physical = SKSurface.Create(new SKImageInfo(80, 30));
            FrameGeometry.DrawLogicalToWire(physical.Canvas, logical, size.Width, size.Height, 80, 30, orientation, 0);
            using var rotated = SKSurface.Create(new SKImageInfo(30, 80));
            FrameGeometry.DrawLogicalToPhysicalWire(rotated.Canvas, logical, size.Width, size.Height, 80, 30, orientation, new(90, 80, 30));
            using var expected = physical.PeekPixels();
            using var actual = rotated.PeekPixels();
            for (var y = 0; y < 80; y++)
                for (var x = 0; x < 30; x++)
                    Check(actual.GetPixelColor(x, y) == expected.GetPixelColor(y, 29 - x), "Clockwise wire rotation must preserve every oriented pixel.");
        }
        Check(PhysicalFrameProfile.EncryptedJpeg.WireSize(1920, 480) == new System.Drawing.Size(480, 1920), "Encrypted JPEG rotates the assigned canvas without a model-sized surface.");
        foreach (var invalid in new[] { new PhysicalFrameProfile(45), new(0, 10), new(0, 0, 10), new(0, MaximumImageBytes: 0) })
            Throws<ArgumentException>(() => invalid.WireSize(1920, 480));
    }

    public static void CanonicalRendererFitAndIsolation()
    {
        var board = Board("#80FF0000");
        using var renderer = new DashboardRenderer(board);
        var metrics = new MetricStore().Snapshot();
        using var logical = renderer.RenderBgra(metrics);
        Check(logical.Bitmap.Width == 1920 && logical.Bitmap.Height == 480 && logical.Bitmap.GetPixel(500, 200).Alpha == 128,
            "Desktop must retain the logical raster and original alpha.");
        using (var jpeg = renderer.RenderJpeg(metrics, PhysicalFrameProfile.EncryptedJpeg, 100))
        using (var decoded = SKBitmap.Decode(jpeg.Bytes))
        {
            Check(decoded.Width == 480 && decoded.Height == 1920, "The actual renderer must encode the TURZX raster.");
            Near(decoded.GetPixel(60, 960), new SKColor(128, 0, 0));
            Near(decoded.GetPixel(420, 960), new SKColor(128, 0, 0));
            Near(decoded.GetPixel(360, 960), new SKColor(128, 0, 0));
            Check(decoded.GetPixel(360, 960).Alpha == 255, "JPEG compositing must be opaque.");
        }
        using (var legacy = renderer.RenderJpeg(metrics, 180, 100))
        using (var decoded = SKBitmap.Decode(legacy.Bytes))
            Check(decoded.Width == 1920 && decoded.Height == 480, "Changing a profile must not poison later legacy renders.");
        Check(board.Canvas.Width == 1920 && board.Canvas.Height == 480 && board.Canvas.Orientation == 0,
            "Physical fitting must not mutate the canonical dashboard.");
        Check(logical.Bitmap.GetPixel(500, 200).Alpha == 128, "Later wire renders must not mutate an owned Desktop frame.");
    }

    public static void EncodedPayloadBound()
    {
        using var renderer = new DashboardRenderer(Board());
        var metrics = new MetricStore().Snapshot();
        Throws<InvalidDataException>(() => renderer.RenderJpeg(metrics, new PhysicalFrameProfile(0, MaximumImageBytes: 1), 90));
        using var valid = renderer.RenderJpeg(metrics, PhysicalFrameProfile.EncryptedJpeg, 90);
        Check(valid.Length is > 1 and <= TurzxWireProtocol.MaximumImageBytes, "A failed payload bound must leave rendering usable.");
    }

    public static void AdapterAdmission()
    {
        var target = new OutputTargetConfig { Width = 1920, Height = 720, DeviceId = "usb-interface:a58aa4db4b66b15856a50a29fe447ab2d9bd64d1dcd4749e2b8e4799094051b2", Kind = "physical", Transport = "encrypted-jpeg", EncryptedJpeg = new() };
        TurzxFrameTransport.ValidateOptions(target, false);
        target.EncryptedJpeg.BrightnessPercent = 0;
        Throws<NotSupportedException>(() => TurzxFrameTransport.ValidateOptions(target, false));
        target.EncryptedJpeg.BrightnessPercent = null;
        Throws<NotSupportedException>(() => TurzxFrameTransport.ValidateOptions(target, true));
        Throws<InvalidOperationException>(() => PhysicalFrameTransportFactory.TryOpen(target, 3000, false, null!, default));
        target.DeviceEnabled = true; target.Kind = "desktop";
        Throws<InvalidOperationException>(() => PhysicalFrameTransportFactory.TryOpen(target, 3000, false, null!, default));
        using var primary = new TurzxTransport(new Usb(), syncDelay: _ => { }, replyMode: TurzxReplyMode.PrimaryOnly);
        Throws<ArgumentException>(() => new TurzxFrameTransport(primary, default));
    }

    public static void CheckpointAdmissionAndPreflight()
    {
        CheckpointGeneratedBoardWithoutStockFiles();
        var captures = 0;
        int? Run(string[] args) => TurzxRuntimeDiagnostic.TryRun(args, _ => { }, capture: () => { captures++; return []; },
            loadBoard: () => throw new InvalidDataException("synthetic private path"), createLog: Log);
        Check(Run([]) is null && Run(["--turzx-transport-check"]) is null, "Unrelated modes must not enter this checkpoint.");
        foreach (var args in new[] { new[] { TurzxRuntimeDiagnostic.Argument }, [TurzxRuntimeDiagnostic.Argument, "--consent", "NO"],
            [TurzxRuntimeDiagnostic.Argument, "--consent", "yes"], [.. Consent, "extra"] })
            Check(Run(args) == 2, "Only exact explicit consent admits the checkpoint.");
        Check(captures == 0 && Run(Consent) == 1 && captures == 0, "Invalid arguments or failed preflight cannot discover/open USB.");
        foreach (var count in new[] { 0, 2 })
            Check(TurzxRuntimeDiagnostic.TryRun(Consent, _ => { }, () => Enumerable.Repeat(Device, count).ToArray(),
                (_, _, _, _, _) => throw new Exception("Unexpected open"), () => Board(), Log) == 1,
                "Missing/ambiguous discovery must not open any interface.");
    }

    private static void CheckpointGeneratedBoardWithoutStockFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue94-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var board = TurzxRuntimeDiagnostic.CreateCheckpointBoard(root);
            Check(board.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
                  board.Canvas.Width == 1920 && board.Canvas.Height == 480 && board.Canvas.Orientation == 0 &&
                  board.BaseDirectory == Path.Combine(root, "dashboards", TurzxRuntimeDiagnostic.CheckpointDashboardName),
                "Checkpoint must use the canonical adaptive generator and its unsaved asset context.");

            var widgets = board.Widgets.OfType<C.ValueWidgetDefinition>().ToArray();
            var clock = widgets.Single(w => w.Id == "clock");
            var host = widgets.Single(w => w.Id == "computer");
            var title = widgets.Single(w => w.Id == "brand");
            Check(clock.SourceKind == C.ValueSourceKind.Metric &&
                  clock.Metric == SystemMetricContract.DateTime && clock.Format == "HH:mm:ss" &&
                  host.SourceKind == C.ValueSourceKind.Metric && host.Metric == SystemMetricContract.PcName &&
                  title.SourceKind == C.ValueSourceKind.Text && title.Metric is null &&
                  title.Text == "PINKIESYSMON / TURZX LIVE",
                "Diagnostic must retain a real clock and the distinctive in-memory title.");

            Check(!Directory.Exists(Path.Combine(root, "dashboards")) &&
                  !File.Exists(Path.Combine(root, "config", "app.json")),
                "Checkpoint board creation must neither require nor persist any dashboard or config file.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void SharedWorkerLiveFrames()
    {
        var usb = new Usb();
        var writes = new List<string>();
        var opened = 0;
        var seconds = 0;
        var result = Run(writes.Add, usb, () => { opened++; },
            () => new DateTime(2026, 1, 1).AddSeconds(Interlocked.Increment(ref seconds)));
        Check(result == 0 && opened == 1 && usb.Images.Count == 2 && usb.Reads == 6 && usb.Disposals == 1,
            "The real worker must complete exactly two JPEGs on one synchronized connection, then dispose once.");
        Check(!usb.Images[0].AsSpan().SequenceEqual(usb.Images[1]), "A real changing clock must change encoded dashboard frames.");
        foreach (var image in usb.Images)
        {
            using var decoded = SKBitmap.Decode(image);
            Check(decoded.Width == 480 && decoded.Height == 1920, "Worker output must use the physical profile.");
        }
        Check(writes.Any(line => line.Contains("open attempts=1")) && writes.All(line => !line.Contains(Device.DeviceId)),
            "The checkpoint must report its scoped result without device identifiers.");
    }

    public static void SharedWorkerFailureNoReopen()
    {
        var usb = new Usb { FailRead = 4 };
        var opened = 0;
        var messages = new List<string>();
        Check(Run(messages.Add, usb, () => opened++, () => DateTime.Now) == 1 && opened == 1 &&
            usb.Images.Count == 1 && usb.Reads == 4 && usb.Disposals == 1,
            "A failed first-image follow-up must stop without success publication, second image or native reopen.");
        Check(!messages.Any(line => line.Contains("FRAMES COMPLETE")) && !messages.Any(line => line.Contains("synthetic private")),
            "A native failure must remain incomplete and keep private error text out of checkpoint output.");
    }

    public static void StaticPayloadIsNotAnimation()
    {
        var usb = new Usb();
        Check(Run(_ => { }, usb, () => { }, () => new DateTime(2026, 1, 1)) == 1 && usb.Images.Count == 2 && usb.Disposals == 1,
            "Two completed identical images cannot establish changing dashboard payloads.");
    }

    public static void FrameBudgetStopsFurtherAdmission()
    {
        var usb = new Usb();
        var target = new OutputTargetConfig { Width = 1920, Height = 720, DeviceId = "usb-interface:64ffed526613e9e9f8fd0631449fab4ab291be11007a2434f6655bb83903c557", Id = "budget", Name = "Synthetic", Kind = "physical", Transport = "encrypted-jpeg",
            DeviceEnabled = true, RefreshIntervalMs = 10 };
        var config = new AppConfig();
        using (var pump = new FramePump(config, target, Board(), new MetricStore(), Log(), (_, _, _, _, token) =>
        {
            var inner = new TurzxTransport(usb, syncDelay: _ => { });
            inner.Synchronize(token);
            return new TurzxFrameTransport(inner, token);
        }, maximumFrames: 1))
        {
            pump.Start();
            Check(SpinWait.SpinUntil(() => pump.SentFrames == 1, 5000), "The worker must complete its admitted frame.");
            // Observe multiple otherwise-due refresh periods after reaching the budget.
            Thread.Sleep(150);
            Check(pump.SentFrames == 1 && usb.Images.Count == 1 && usb.Reads == 4,
                "No later render/send exchange may be admitted after the frame budget.");
        }
        Check(usb.Disposals == 1, "Budget completion must preserve worker-owned disposal.");
    }

    private static readonly string[] Consent = [TurzxRuntimeDiagnostic.Argument, "--consent", "YES"];
    private static readonly OutputDeviceDescriptor Device = new("usb-interface:synthetic-private-binding", "encrypted-jpeg", "Synthetic", "synthetic-private-path");
    private static int Run(Action<string> write, Usb usb, Action opened, Func<DateTime> now) =>
        TurzxRuntimeDiagnostic.TryRun(Consent, write, () => [Device], (target, timeout, journal, log, token) =>
        {
            opened();
            Check(target.DeviceEnabled && target.DeviceId == Device.DeviceId && target.EncryptedJpeg?.BrightnessPercent is null && !journal,
                "Only the selected in-memory target, without brightness or detailed journal, may reach open.");
            var inner = new TurzxTransport(usb, syncDelay: _ => { });
            try { inner.Synchronize(token); return new TurzxFrameTransport(inner, token); }
            catch { inner.Dispose(); throw; }
        }, ClockBoard, Log, frameLimit: 2, refreshMs: 100, now: now)!.Value;

    private static FileLogger Log() => new(Path.Combine(Path.GetTempPath(), "PinkieSysMon-Issue71-runtime-tests", "worker.log"));
    private static C.DashboardDefinition Board(string color = "#000000") => new()
    {
        BaseDirectory = Path.Combine(Environment.CurrentDirectory, "dashboards", "Default"),
        Canvas = new() { Width = 1920, Height = 480, BackgroundColor = color }
    };
    private static C.DashboardDefinition ClockBoard()
    {
        var board = Board();
        board.Widgets.Add(new C.ValueWidgetDefinition
        {
            Id = "clock", Metric = SystemMetricContract.DateTime, Format = "HH:mm:ss", X = 50, Y = 160,
            TextPresentation = new() { FontSize = 100 }
        });
        board.Validate();
        return board;
    }
    private sealed class Usb : ITurzxUsbConnection
    {
        public List<byte[]> Images { get; } = [];
        public int Reads, Disposals, FailRead;
        public int Write(byte[] packet, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(Disposals == 0, "No native write may occur after disposal.");
            if (packet.Length > 512) Images.Add(packet.AsSpan(512).ToArray());
            return packet.Length;
        }
        public int Read(byte[] buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            if (Reads == FailRead) throw new IOException("synthetic private transport failure");
            if (Reads % 2 == 0) return 0;
            Array.Clear(buffer); buffer[1] = 0xC8; return 512;
        }
        public void Dispose() => Disposals++;
    }
    private static void Near(SKColor actual, SKColor expected) => Check(
        Math.Abs(actual.Red - expected.Red) < 5 && Math.Abs(actual.Green - expected.Green) < 5 && Math.Abs(actual.Blue - expected.Blue) < 5,
        $"Unexpected decoded color: {actual}, expected {expected}.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
