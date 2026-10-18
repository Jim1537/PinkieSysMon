using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.RegressionTests;

internal static class TurzxTransportDiagnosticTests
{
    private static readonly OutputDeviceDescriptor Device = new("turzx:" + new string('a', 64), "turzx", "Synthetic", "PRIVATE-PATH");
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void ExplicitConsentAndArguments()
    {
        var captures = 0; var opens = 0;
        int? Run(string[] args) => TurzxTransportDiagnostic.TryRun(args, _ => { },
            () => { captures++; return [Device]; }, _ => { opens++; return null; });
        Check(Run([]) is null && Run(["--turzx-passive-check"]) is null, "Unrelated modes must never enter the active checkpoint.");
        foreach (var args in new[] { new[] { TurzxTransportDiagnostic.Argument }, new[] { TurzxTransportDiagnostic.Argument, "png", "extra" },
            new[] { TurzxTransportDiagnostic.Argument, "brightness", "101" }, new[] { TurzxTransportDiagnostic.Argument, "brightness", "-1" },
            new[] { TurzxTransportDiagnostic.Argument, "brightness", "1.5" },
            new[] { TurzxTransportDiagnostic.Argument, "jpeg-sync", "extra" },
            new[] { TurzxTransportDiagnostic.Argument, "brightness-sync", "101" },
            new[] { TurzxTransportDiagnostic.Argument, "brightness-sync", "-1" },
            new[] { TurzxTransportDiagnostic.Argument, "brightness-sync" } })
            Check(Run([.. args, "--consent", "YES"]) == 2, "Invalid actions/arguments must be rejected before capture.");
        Check(captures == 0 && opens == 0, "Invalid admission must have no hardware side effects.");
        Check(Run([TurzxTransportDiagnostic.Argument, "png"]) == 2, "Consent defaults to declined.");
        Check(Run([TurzxTransportDiagnostic.Argument, "sync"]) == 2 &&
            Run([TurzxTransportDiagnostic.Argument, "sync", "extra", "--consent", "YES"]) == 2,
            "Sync probing requires exact arguments and the same explicit hardware consent.");
        Check(Run([TurzxTransportDiagnostic.Argument, "png-sync"]) == 2,
            "PNG after a sync read requires its own explicit invocation and consent.");
        Check(Run([TurzxTransportDiagnostic.Argument, "jpeg-sync"]) == 2 &&
            Run([TurzxTransportDiagnostic.Argument, "brightness-sync", "25"]) == 2,
            "JPEG and brightness after a sync read cannot bypass consent.");
        foreach (var answer in new[] { "", "yes", "NO" })
            Check(Run([TurzxTransportDiagnostic.Argument, "png", "--consent", answer]) == 2, "Only explicit uppercase YES authorizes hardware.");
        Check(captures == 0 && opens == 0, "Declined hardware use cannot discover/open a transport.");
    }

    public static void OneCommandAndEmptyBrightness()
    {
        foreach (var action in new[] { "png", "jpeg", "brightness" })
        {
            var io = new FakeConnection(action == "brightness");
            var output = new List<string>();
            var args = action == "brightness" ? new[] { TurzxTransportDiagnostic.Argument, action, "25", "--consent", "YES" } : [TurzxTransportDiagnostic.Argument, action, "--consent", "YES"];
            var result = TurzxTransportDiagnostic.TryRun(args, output.Add, () => [Device], target =>
            {
                Check(target.DeviceEnabled && target.Kind == "turzx" && target.DeviceId == Device.DeviceId, "Consent must create only the exact in-memory enabled test target.");
                var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
            }, format => format == TurzxImageFormat.Png ? Png : [255, 216, 255, 255, 217]);
            Check(result == (action == "brightness" ? 3 : 0) && io.Writes == 2 && io.Reads == 2 && io.Disposed,
                "Each invocation must synchronize, execute exactly one command and dispose, including an unacknowledged brightness result.");
            var text = string.Join('\n', output);
            Check(!text.Contains("PRIVATE-PATH") && !text.Contains(Device.DeviceId), "Active diagnostic output cannot expose private bindings/paths.");
        }
        foreach (var received in new[] { 0, 7, 512 })
        {
            var io = new FakeConnection(false); var output = new List<string>(); var probes = 0;
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "sync", "--consent", "YES"], output.Add,
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                }, _ => throw new InvalidOperationException("Sync must not encode an image."), buffer =>
                {
                    probes++; Check(buffer.Length == 512, "The one post-sync read must be bounded."); buffer[1] = 0xC8; return received;
                }) == 0 && io.Writes == 1 && io.Reads == 1 && probes == 1 && io.Disposed,
                "Sync probing must send only sync, perform exactly one extra read and dispose, with no selected image/brightness command.");
            Check(output.Contains($"Post-sync response bytes: {received}.") &&
                output.Contains($"Post-sync full status-marker shape: {(received == 512 ? "YES" : "NO")}."),
                "Empty/short/full probe observations must be reported separately without turning a short marker into a full response.");
        }
        foreach (var action in new[] { "png-sync", "jpeg-sync" })
        {
            var io = new FakeConnection(false); var probes = 0;
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, action, "--consent", "YES"], _ => { },
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                }, followUpRead: _ =>
                {
                    probes++; Check(io.Writes == 1 && io.Reads == 1, "The extra read must precede the image command."); return 0;
                }) == 0 && io.Writes == 2 && io.Reads == 2 && probes == 1 && io.Disposed,
                "The explicit codec checkpoint may send exactly one image after its one empty post-sync read.");
            var pattern = io.LastPacket![512..];
            using var bitmap = SKBitmap.Decode(pattern) ?? throw new InvalidOperationException("Checkpoint image must decode.");
            var background = bitmap.GetPixel(360, 1800);
            var expected = action == "png-sync" ? new SKColor(8, 32, 96) : new SKColor(96, 8, 32);
            Check((action != "png-sync" || pattern[24] == 8 && pattern[25] == 6 && pattern[28] == 0) &&
                bitmap.Width == 720 && bitmap.Height == 1920 && bitmap.Pixels.All(pixel => pixel.Alpha == 255) &&
                Math.Abs(background.Red - expected.Red) <= 3 && Math.Abs(background.Green - expected.Green) <= 3 &&
                Math.Abs(background.Blue - expected.Blue) <= 3,
                "The codec checkpoint must retain opaque geometry and its distinct blue/burgundy background.");
        }
        foreach (var percent in new[] { "0", "25", "100" })
        foreach (var empty in new[] { false, true })
        {
            var io = new FakeConnection(empty); var probes = 0;
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "brightness-sync", percent, "--consent", "YES"], _ => { },
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                }, _ => throw new InvalidOperationException("Brightness must not encode an image."), _ =>
                {
                    probes++; Check(io.Writes == 1, "Post-sync read must precede brightness."); return 0;
                }) == (empty ? 3 : 0) && io.Writes == 2 && io.Reads == 2 && probes == 1 && io.Disposed && io.LastPacket!.Length == 512,
                "Controlled brightness must send one command only after its empty sync read, preserving unacknowledged completion.");
        }
    }

    public static void FailuresStopBeforeFurtherIo()
    {
        var opens = 0;
        foreach (var devices in new IReadOnlyList<OutputDeviceDescriptor>[] { [], [Device, Device] })
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "png", "--consent", "YES"], _ => { }, () => devices,
                _ => { opens++; return null; }) == 1, "Missing/multiple candidates must not open.");
        var output = new List<string>();
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "png", "--consent", "YES"], output.Add, () => [Device],
            _ => { opens++; return null; }, _ => throw new InvalidOperationException("PRIVATE source must not leak")) == 1 && opens == 0,
            "Encoding failure must happen before open.");
        Check(!string.Join('\n', output).Contains("PRIVATE"), "Exception messages must not leak into diagnostic output.");
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "png", "--consent", "YES"], _ => { }, () => [Device],
            _ => null, _ => Png) == 1, "A binding removed before open must not select a replacement.");
        var failed = new FakeConnection(true);
        output.Clear();
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "png", "--consent", "YES"], output.Add, () => [Device], _ =>
        {
            var transport = new TurzxTransport(failed, () => 0, _ => { }); transport.Synchronize(); return transport;
        }, _ => Png) == 1 && failed.Disposed && failed.Writes == 2 && failed.Reads == 2,
            "A failed image response must dispose without another command.");
        Check(output.Contains("Protocol response: command 102; 0 bytes; unrecognized."),
            "Known protocol failures retain only numeric command/length context, never arbitrary exception text.");
        foreach (var action in new[] { "sync", "png-sync", "jpeg-sync", "brightness-sync" })
        foreach (var failure in new Exception[] { new TimeoutException("PRIVATE timeout"), new OperationCanceledException("PRIVATE cancel"),
            new System.ComponentModel.Win32Exception(31, "PRIVATE native failure"), new IOException("PRIVATE failure") })
        {
            var io = new FakeConnection(false); var probes = 0; output.Clear();
            var args = action == "brightness-sync" ? new[] { TurzxTransportDiagnostic.Argument, action, "25", "--consent", "YES" }
                : [TurzxTransportDiagnostic.Argument, action, "--consent", "YES"];
            Check(TurzxTransportDiagnostic.TryRun(args, output.Add,
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                }, followUpRead: _ => { probes++; throw failure; }) == 1 && io.Writes == 1 && io.Reads == 1 && probes == 1 && io.Disposed,
                "A failed extra read must dispose without another read/write or any image/brightness command.");
            Check(output.Any(line => line.Contains("ERROR at sync-follow-up")) && !string.Join('\n', output).Contains("PRIVATE"),
                "Post-sync failure stage must be distinguishable without exposing arbitrary native messages.");
        }
        foreach (var action in new[] { "png-sync", "jpeg-sync", "brightness-sync" })
        foreach (var received in new[] { -1, 7, 512, 513 })
        {
            var io = new FakeConnection(false); var probes = 0;
            var args = action == "brightness-sync" ? new[] { TurzxTransportDiagnostic.Argument, action, "25", "--consent", "YES" }
                : [TurzxTransportDiagnostic.Argument, action, "--consent", "YES"];
            Check(TurzxTransportDiagnostic.TryRun(args, _ => { },
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                }, format => format == TurzxImageFormat.Png ? Png : [255, 216, 255, 255, 217],
                _ => { probes++; return received; }) == 1 && io.Writes == 1 && io.Reads == 1 && probes == 1 && io.Disposed,
                "Any nonempty or invalid post-sync read must stop before the selected command, without another probe.");
        }
    }

    public static void SyntheticCodecGeometryAndCorners()
    {
        foreach (var format in new[] { TurzxImageFormat.Png, TurzxImageFormat.Jpeg })
        {
            var bytes = TurzxTransportDiagnostic.CreatePattern(format);
            Check(bytes.Length <= TurzxWireProtocol.MaximumImageBytes, "The synthetic encoded pattern must fit the transport policy.");
            _ = TurzxWireProtocol.Image(format, bytes, 0);
            if (format == TurzxImageFormat.Png)
                Check(bytes.Length >= 33 && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
                    bytes[24] == 8 && bytes[25] == 6 && bytes[26] == 0 && bytes[27] == 0 && bytes[28] == 0,
                    "The PNG checkpoint must retain 8-bit noninterlaced RGBA, not silently encode opaque RGB.");
            using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Diagnostic pattern could not be decoded.");
            Check(bitmap.Width == 720 && bitmap.Height == 1920, "Synthetic test transport geometry must be portrait without rewriting a dashboard.");
            if (format == TurzxImageFormat.Png)
            {
                Check(bitmap.Pixels.All(pixel => pixel.Alpha == 255), "RGBA transport storage must not introduce transparent pattern pixels.");
                Check(bitmap.GetPixel(360, 1800) == new SKColor(17, 22, 32), "The PNG must contain its own opaque background.");
                var output = new List<string>();
                var io = new FakeConnection(false);
                Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "png", "--consent", "YES"], output.Add,
                    () => [Device], _ =>
                    {
                        var transport = new TurzxTransport(io, () => 0, _ => { }); transport.Synchronize(); return transport;
                    }, _ => bytes) == 0 && io.Disposed &&
                    output.Contains("PNG IHDR: depth 8; color type 6; interlace 0."),
                    "The checkpoint must report the actual encoded PNG profile before its one-command exchange.");
            }
            var red = bitmap.GetPixel(700, 20); var green = bitmap.GetPixel(700, 1900);
            var blue = bitmap.GetPixel(20, 1900); var yellow = bitmap.GetPixel(20, 20);
            Check(red.Red > 200 && red.Green < 80 && red.Blue < 80 && green.Green > 200 && green.Red < 80 && green.Blue < 80 &&
                blue.Blue > 200 && blue.Red < 80 && blue.Green < 80 && yellow.Red > 200 && yellow.Green > 200 && yellow.Blue < 80,
                "Independent corner samples must verify the clockwise transform for both real encoded formats.");
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeConnection(bool emptyBrightness) : ITurzxUsbConnection
    {
        public int Writes, Reads; public bool Disposed; public byte[]? LastPacket;
        public int Write(byte[] packet, CancellationToken token) { Writes++; LastPacket = packet; return packet.Length; }
        public int Read(byte[] buffer, CancellationToken token) { Reads++; if (emptyBrightness && Reads == 2) return 0; buffer[1] = 0xC8; return 512; }
        public void Dispose() => Disposed = true;
    }
}
