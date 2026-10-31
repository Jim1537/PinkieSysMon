using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.RegressionTests;

internal static class TurzxTransportDiagnosticTests
{
    private static readonly OutputDeviceDescriptor Device = new("usb-interface:" + new string('a', 64), "encrypted-jpeg", "Synthetic", "PRIVATE-PATH");
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
            new[] { TurzxTransportDiagnostic.Argument, "jpeg-pair", "extra" },
            new[] { TurzxTransportDiagnostic.Argument, "jpeg-pair-read", "extra" },
            new[] { TurzxTransportDiagnostic.Argument, "jpeg-stream", "extra" },
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
            Run([TurzxTransportDiagnostic.Argument, "jpeg-pair"]) == 2 &&
            Run([TurzxTransportDiagnostic.Argument, "jpeg-pair-read"]) == 2 &&
            Run([TurzxTransportDiagnostic.Argument, "jpeg-stream"]) == 2 &&
            Run([TurzxTransportDiagnostic.Argument, "brightness-sync", "25"]) == 2,
            "JPEG and brightness after a sync read cannot bypass consent.");
        foreach (var answer in new[] { "", "yes", "NO" })
            Check(Run([TurzxTransportDiagnostic.Argument, "png", "--consent", answer]) == 2, "Only explicit uppercase YES authorizes hardware.");
        Check(captures == 0 && opens == 0, "Declined hardware use cannot discover/open a transport.");
    }

    public static void SyncCapturePreservesExactReplyAndSingleExchange()
    {
        foreach (var length in new[] { 0, 12, 24, 41, 512, -1, 513 })
        {
            var io = new CaptureConnection { ReportedLength = length };
            var output = new List<string>(); var opens = 0; var captures = 0;
            var device = CaptureDevice();
            var result = TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "sync-capture", "--consent", "YES"], output.Add,
                () => { captures++; return [device]; },
                _ => throw new InvalidOperationException("Capture cannot bypass production sync."),
                _ => throw new InvalidOperationException("Capture cannot encode an image."),
                _ => throw new InvalidOperationException("Capture cannot read a follow-up."),
                openCaptureConnection: (selected, timeout) =>
                {
                    opens++;
                    Check(selected == device && timeout == 3000, "Capture must use the exact revalidated descriptor and existing timeout.");
                    return io;
                });
            Check(result == (length == 512 ? 0 : 1) && opens == 1 && captures == 2 && io.Writes == 1 && io.Reads == 1 && io.Disposed,
                "Sync capture must use actual TryOpen/strict sync with one read/write and dispose, including short/invalid replies.");
            var text = string.Join('\n', output);
            Check(!text.Contains(device.DeviceId) && !text.Contains(device.DevicePath), "Capture metadata must not add device bindings/paths.");
            if (length is >= 0 and <= 512)
                Check(output.Single(line => line.StartsWith("PRIVATE primary response hex:", StringComparison.Ordinal)) ==
                    "PRIVATE primary response hex: " + Convert.ToHexString(io.Reply.AsSpan(0, length)),
                    "Capture must retain precisely the bytes returned, excluding stale buffer tails, even before strict validation fails.");
            else
                Check(!output.Any(line => line.StartsWith("PRIVATE primary response hex:", StringComparison.Ordinal)),
                    "Invalid native lengths must not index or fabricate raw reply bytes.");
            Check(output.Any(line => line.Contains("Keep this raw-reply log private.", StringComparison.Ordinal)),
                "Explicit raw capture must identify the log's private evidence boundary.");
        }
    }

    public static void SyncCaptureAdmissionAndFailureLifetime()
    {
        var captures = 0; var opens = 0;
        foreach (var args in new[]
        {
            new[] { TurzxTransportDiagnostic.Argument, "sync-capture" },
            new[] { TurzxTransportDiagnostic.Argument, "sync-capture", "--consent", "NO" },
            new[] { TurzxTransportDiagnostic.Argument, "sync-capture", "extra", "--consent", "YES" }
        })
            Check(TurzxTransportDiagnostic.TryRun(args, _ => { }, () => { captures++; return [CaptureDevice()]; },
                openCaptureConnection: (_, _) => { opens++; return new CaptureConnection(); }) == 2,
                "Capture must require exact arguments and explicit consent before discovery or native open.");
        Check(captures == 0 && opens == 0, "Declined/invalid capture cannot perform discovery or acquisition.");
        foreach (var devices in new IReadOnlyList<OutputDeviceDescriptor>[] { [], [CaptureDevice(), CaptureDevice()], [Device] })
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "sync-capture", "--consent", "YES"], _ => { },
                () => devices, openCaptureConnection: (_, _) => { opens++; return new CaptureConnection(); }) == 1 && opens == 0,
                "Missing/ambiguous or invalid exact binding must fail before capture opens native I/O.");
        foreach (var failure in new Exception[] { new TimeoutException("PRIVATE timeout"), new OperationCanceledException("PRIVATE cancel"),
            new System.ComponentModel.Win32Exception(31, "PRIVATE native failure"), new IOException("PRIVATE I/O failure") })
        {
            var io = new CaptureConnection { ReadFailure = failure }; var output = new List<string>();
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "sync-capture", "--consent", "YES"], output.Add,
                () => [CaptureDevice()], openCaptureConnection: (_, _) => io) == 1 && io.Writes == 1 && io.Reads == 1 && io.Disposed,
                "A failed capture read must close ownership without another read/write or retry.");
            Check(!output.Any(line => line.StartsWith("PRIVATE primary response hex:", StringComparison.Ordinal)) &&
                !string.Join('\n', output).Contains(failure.Message),
                "A failed transfer supplies no fabricated reply or arbitrary exception text.");
        }
        var shortWrite = new CaptureConnection { ShortWrite = true };
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "sync-capture", "--consent", "YES"], _ => { },
            () => [CaptureDevice()], openCaptureConnection: (_, _) => shortWrite) == 1 &&
            shortWrite.Writes == 1 && shortWrite.Reads == 0 && shortWrite.Disposed,
            "A short sync write must stop before the primary read.");
    }

    private static OutputDeviceDescriptor CaptureDevice()
    {
        const string path = @"\\?\usb#vid_1cbe&pid_0123#SYNTHETIC-CAPTURE#{9374A242-8D16-42BB-A6C4-3BAA15E51317}";
        return new(TurzxDeviceDiscovery.CreateStableDeviceId(path), "encrypted-jpeg", "Synthetic", path);
    }

    private sealed class CaptureConnection : ITurzxUsbConnection
    {
        public byte[] Reply { get; } = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        public int ReportedLength { get; init; } = 512;
        public Exception? ReadFailure { get; init; }
        public bool ShortWrite { get; init; }
        public int Writes, Reads;
        public bool Disposed;
        public int Write(byte[] packet, CancellationToken token)
        { Writes++; return packet.Length - (ShortWrite ? 1 : 0); }
        public int Read(byte[] buffer, CancellationToken token)
        {
            Reads++;
            if (ReadFailure is not null) throw ReadFailure;
            Reply[1] = 0xC8;
            Array.Fill(buffer, (byte)0xDD);
            Reply.AsSpan(0, Math.Clamp(ReportedLength, 0, 512)).CopyTo(buffer);
            return ReportedLength;
        }
        public void Dispose() => Disposed = true;
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
                Check(target.DeviceEnabled && target.Kind == "physical" && target.DeviceId == Device.DeviceId, "Consent must create only the exact in-memory enabled test target.");
                var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
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
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
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
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
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
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
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
            var transport = new TurzxTransport(failed, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
        }, _ => Png) == 1 && failed.Disposed && failed.Writes == 2 && failed.Reads == 2,
            "A failed image response must dispose without another command.");
        Check(output.Contains("Protocol response: command 102; 0 bytes; unrecognized."),
            "Known protocol failures retain only numeric command/length context, never arbitrary exception text.");
        foreach (var action in new[] { "sync", "png-sync", "jpeg-sync", "jpeg-pair", "jpeg-pair-read", "brightness-sync" })
        foreach (var failure in new Exception[] { new TimeoutException("PRIVATE timeout"), new OperationCanceledException("PRIVATE cancel"),
            new System.ComponentModel.Win32Exception(31, "PRIVATE native failure"), new IOException("PRIVATE failure") })
        {
            var io = new FakeConnection(false); var probes = 0; output.Clear();
            var args = action == "brightness-sync" ? new[] { TurzxTransportDiagnostic.Argument, action, "25", "--consent", "YES" }
                : [TurzxTransportDiagnostic.Argument, action, "--consent", "YES"];
            Check(TurzxTransportDiagnostic.TryRun(args, output.Add,
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
                }, followUpRead: _ => { probes++; throw failure; }) == 1 && io.Writes == 1 && io.Reads == 1 && probes == 1 && io.Disposed,
                "A failed extra read must dispose without another read/write or any image/brightness command.");
            Check(output.Any(line => line.Contains("ERROR at sync-follow-up")) && !string.Join('\n', output).Contains("PRIVATE"),
                "Post-sync failure stage must be distinguishable without exposing arbitrary native messages.");
        }
        foreach (var action in new[] { "png-sync", "jpeg-sync", "jpeg-pair", "jpeg-pair-read", "brightness-sync" })
        foreach (var received in new[] { -1, 7, 512, 513 })
        {
            var io = new FakeConnection(false); var probes = 0;
            var args = action == "brightness-sync" ? new[] { TurzxTransportDiagnostic.Argument, action, "25", "--consent", "YES" }
                : [TurzxTransportDiagnostic.Argument, action, "--consent", "YES"];
            Check(TurzxTransportDiagnostic.TryRun(args, _ => { },
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
                }, format => format == TurzxImageFormat.Png ? Png : [255, 216, 255, 255, 217],
                _ => { probes++; return received; }) == 1 && io.Writes == 1 && io.Reads == 1 && probes == 1 && io.Disposed,
                "Any nonempty or invalid post-sync read must stop before the selected command, without another probe.");
        }
    }

    public static void JpegPairSequenceAndFailures()
    {
        var io = new FakeConnection(false); var output = new List<string>();
        var opens = 0; var probes = 0; var pauses = 0;
        var args = new[] { TurzxTransportDiagnostic.Argument, "jpeg-pair", "--consent", "YES" };
        Check(TurzxTransportDiagnostic.TryRun(args, output.Add, () => [Device], _ =>
        {
            opens++; var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
        }, followUpRead: _ => { probes++; Check(io.Writes == 1 && io.Reads == 1, "The pair must use one initial post-sync read."); return 0; },
        betweenImages: () => { pauses++; Check(io.Writes == 2 && io.Reads == 2, "The observation pause follows only the first completed JPEG exchange."); }) == 0 &&
            opens == 1 && probes == 1 && pauses == 1 && io.Writes == 3 && io.Reads == 3 && io.Disposed,
            "The pair must use one connection/sync, two different JPEGs and existing response reads, without reopening or a third frame.");
        var header = output.Single(line => line.StartsWith("Invocation: ", StringComparison.Ordinal)).Split(';');
        Check(Guid.TryParseExact(header[0][12..], "N", out _) &&
            DateTimeOffset.TryParse(header[1].Trim()[4..], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) && header[2].Trim() == "action jpeg-pair.",
            "Each invocation must identify its random run ID, UTC timestamp and selected action without device identity.");
        Check(output.Contains("Frame A: orange background / TURZX JPEG PAIR A.") &&
            output.Contains("Frame B: teal background / TURZX JPEG PAIR B.") &&
            output.Any(line => line.StartsWith("PAIR CHECK: EXCHANGES COMPLETE", StringComparison.Ordinal)),
            "Pair logs must distinguish the two planned frames without asserting physical display success.");
        Check(!io.Packets[1].AsSpan(512).SequenceEqual(io.Packets[2].AsSpan(512)), "The pair must not resend one identical image.");
        for (var frame = 0; frame < 2; frame++)
        {
            using var bitmap = SKBitmap.Decode(io.Packets[frame + 1][512..]) ?? throw new InvalidOperationException("Pair JPEG must decode.");
            var color = bitmap.GetPixel(360, 1800); var expected = frame == 0 ? new SKColor(104, 48, 8) : new SKColor(8, 80, 80);
            Check(bitmap.Width == 720 && bitmap.Height == 1920 && bitmap.Pixels.All(p => p.Alpha == 255) &&
                Math.Abs(color.Red - expected.Red) <= 3 && Math.Abs(color.Green - expected.Green) <= 3 && Math.Abs(color.Blue - expected.Blue) <= 3,
                "Each pair JPEG must retain its distinct opaque background and transport geometry.");
        }
        foreach (var action in new[] { "jpeg-pair", "jpeg-pair-read" })
        foreach (var command in new[] { 2, 3 })
        foreach (var failure in new[] { "empty", "read", "write", "short" })
        {
            var failed = new FakeConnection(false)
            {
                EmptyAtRead = failure == "empty" ? command : -1,
                FailureAtRead = failure == "read" ? command : -1,
                FailureAtWrite = failure == "write" ? command : -1,
                ShortAtWrite = failure == "short" ? command : -1
            };
            output.Clear(); var delays = 0;
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, action, "--consent", "YES"], output.Add, () => [Device], _ =>
            {
                var transport = new TurzxTransport(failed, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
            }, _ => [255, 216, 255, 255, 217], _ => 0, () => delays++) == 1 && failed.Disposed &&
                failed.Writes == command && failed.Reads == command - (failure is "write" or "short" ? 1 : 0) && delays == command - 2,
                "An empty or failed pair reply, failed write or short write must stop exactly at that frame without retry.");
            Check(output.Any(line => line.Contains($"ERROR at image-{(command == 2 ? "A" : "B")}", StringComparison.Ordinal)) &&
                !output.Any(line => line.StartsWith("PAIR CHECK: EXCHANGES COMPLETE", StringComparison.Ordinal)) &&
                !string.Join('\n', output).Contains("PRIVATE", StringComparison.Ordinal),
                "Failed pair logs must identify the frame and retain sanitized failure reporting.");
        }
        var encoded = 0; var opened = 0;
        Check(TurzxTransportDiagnostic.TryRun(args, _ => { }, () => [Device], _ => { opened++; return null; },
            _ => ++encoded == 1 ? [255, 216, 255, 255, 217] : Png) == 1 && encoded == 2 && opened == 0,
            "Both pair payloads must be encoded and validated before any USB open.");
        var paused = new FakeConnection(false); output.Clear();
        Check(TurzxTransportDiagnostic.TryRun(args, output.Add, () => [Device], _ =>
        {
            var transport = new TurzxTransport(paused, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
        }, _ => [255, 216, 255, 255, 217], _ => 0, () => throw new OperationCanceledException("PRIVATE cancellation")) == 1 &&
            paused.Writes == 2 && paused.Reads == 2 && paused.Disposed &&
            output.Any(line => line.Contains("ERROR at pair-observation-delay", StringComparison.Ordinal)),
            "An interrupted observation pause must dispose before sending frame B.");
    }

    public static void JpegPairReadBoundaries()
    {
        // A synthetic candidate mechanism, not a claim about device firmware:
        // each write queues a marker-shaped completion followed by an empty one.
        foreach (var action in new[] { "jpeg-pair", "jpeg-pair-read" })
        {
            var io = new PairedReplyConnection(); var output = new List<string>(); var opens = 0;
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, action, "--consent", "YES"], output.Add,
                () => [Device], _ =>
                {
                    opens++; var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
                }, _ => [255, 216, 255, 255, 217], buffer => io.Read(buffer, default), () => { }) == (action == "jpeg-pair" ? 1 : 0) &&
                opens == 1 && io.Writes == 3 && io.Reads == (action == "jpeg-pair" ? 4 : 6) && io.Disposed &&
                io.Pending == (action == "jpeg-pair" ? 2 : 0),
                "The candidate reply model must reproduce the old B-empty failure; one fixed follow-up per command consumes its modeled boundary without another write.");
            Check(action == "jpeg-pair"
                ? output.Contains("Protocol response: command 101; 0 bytes; unrecognized.")
                : output.Contains("Post-image-A response bytes: 0.") && output.Contains("Post-image-B response bytes: 0.") &&
                    output.Count(line => line == "Command 101: StatusMarker; response bytes: 512.") == 2,
                "Each image's strict reply and additional-read observation must remain distinct.");
        }
        foreach (var boundary in new[] { 2, 3 })
        foreach (var count in new[] { -1, 7, 512, 513 })
        {
            var io = new FakeConnection(false); var probes = 0; var pauses = 0; var output = new List<string>();
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "jpeg-pair-read", "--consent", "YES"], output.Add,
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
                }, _ => [255, 216, 255, 255, 217], buffer => { probes++; buffer[1] = 0xC8; return probes == boundary ? count : 0; },
                () => pauses++) == 1 && io.Disposed && io.Writes == boundary && io.Reads == boundary &&
                probes == boundary && pauses == boundary - 2 && !output.Any(line => line.StartsWith("PAIR CHECK: EXCHANGES COMPLETE", StringComparison.Ordinal)),
                "A nonempty/invalid frame follow-up must stop at its fixed boundary, before B when A is uncertain, without another probe or retry.");
            if (count == 512) Check(output.Contains($"Post-image-{(boundary == 2 ? "A" : "B")} full status-marker shape: YES."),
                "An extra marker-shaped reply must be logged but must not bypass the empty-read guard.");
        }
        foreach (var boundary in new[] { 2, 3 })
        foreach (var failure in new Exception[] { new TimeoutException("PRIVATE timeout"), new OperationCanceledException("PRIVATE cancellation"),
            new System.ComponentModel.Win32Exception(31, "PRIVATE native failure"), new IOException("PRIVATE I/O failure") })
        {
            var io = new FakeConnection(false); var probes = 0; var output = new List<string>();
            Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "jpeg-pair-read", "--consent", "YES"], output.Add,
                () => [Device], _ =>
                {
                    var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
                }, _ => [255, 216, 255, 255, 217], _ => { probes++; if (probes == boundary) throw failure; return 0; }, () => { }) == 1 &&
                io.Disposed && io.Writes == boundary && io.Reads == boundary && probes == boundary &&
                output.Any(line => line.Contains($"ERROR at image-{(boundary == 2 ? "A" : "B")}-follow-up", StringComparison.Ordinal)) &&
                !string.Join('\n', output).Contains("PRIVATE", StringComparison.Ordinal),
                "A post-image read error must preserve its phase and dispose without another command/read or leaking native text.");
        }
    }

    public static void JpegStreamUsesTransport()
    {
        var io = new PairedReplyConnection(); var output = new List<string>(); var opens = 0; var pauses = 0;
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "jpeg-stream", "--consent", "YES"], output.Add,
            () => [Device], _ =>
            {
                opens++;
                var transport = new TurzxTransport(io, () => 0, _ => { });
                transport.Synchronize();
                return transport;
            }, _ => [255, 216, 255, 255, 217], _ => throw new InvalidOperationException("The diagnostic must not add a raw read."),
            () => { pauses++; Check(io.Writes == 2 && io.Reads == 4, "The transport must complete A's follow-up before the observation pause."); }) == 0 &&
            opens == 1 && pauses == 1 && io.Writes == 3 && io.Reads == 6 && io.Pending == 0 && io.Disposed,
            "The JPEG stream checkpoint must exercise the default transport without diagnostic-owned reads or another sync.");
        Check(output.Contains("Transport post-image-A response bytes: 0.") && output.Contains("Transport post-image-B response bytes: 0."),
            "Stream logs must report actual transport completion counts for each image.");
        var failed = new PairedReplyConnection { ReportedReadCount = index => index == 4 ? 512 : null };
        output.Clear();
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "jpeg-stream", "--consent", "YES"], output.Add,
            () => [Device], _ =>
            {
                var transport = new TurzxTransport(failed, () => 0, _ => { });
                transport.Synchronize(); return transport;
            }, _ => [255, 216, 255, 255, 217], _ => throw new InvalidOperationException("PRIVATE extra read"),
            () => throw new InvalidOperationException("PRIVATE pause")) == 1 && failed.Writes == 2 && failed.Reads == 4 && failed.Disposed &&
            output.Contains("Protocol follow-up: command 101; 512 bytes; expected empty.") && !string.Join('\n', output).Contains("PRIVATE"),
            "A stream follow-up failure must stop before B and retain only safe numeric protocol context.");
        var legacy = new FakeConnection(false);
        Check(TurzxTransportDiagnostic.TryRun([TurzxTransportDiagnostic.Argument, "jpeg-stream", "--consent", "YES"], _ => { },
            () => [Device], _ =>
            {
                var transport = new TurzxTransport(legacy, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly);
                transport.Synchronize(); return transport;
            }, _ => [255, 216, 255, 255, 217]) == 1 && legacy.Writes == 1 && legacy.Reads == 1 && legacy.Disposed,
            "A legacy primary-only transport cannot masquerade as a verified stream checkpoint.");
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
                        var transport = new TurzxTransport(io, () => 0, _ => { }, TurzxReplyMode.PrimaryOnly); transport.Synchronize(); return transport;
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
    private sealed class PairedReplyConnection : ITurzxUsbConnection
    {
        private readonly Queue<byte[]> _pending = new();
        public int Writes, Reads;
        public bool Disposed;
        public int Pending => _pending.Count;
        public Func<int, int?>? ReportedReadCount { get; init; }
        public int Write(byte[] packet, CancellationToken token)
        {
            Writes++; var reply = new byte[512]; reply[1] = 0xC8;
            _pending.Enqueue(reply); _pending.Enqueue([]); return packet.Length;
        }
        public int Read(byte[] buffer, CancellationToken token)
        { Reads++; var reply = _pending.Dequeue(); reply.CopyTo(buffer, 0); return ReportedReadCount?.Invoke(Reads) ?? reply.Length; }
        public void Dispose() => Disposed = true;
    }
    private sealed class FakeConnection(bool emptyBrightness) : ITurzxUsbConnection
    {
        public int Writes, Reads; public bool Disposed; public byte[]? LastPacket;
        public int EmptyAtRead = -1, FailureAtRead = -1, FailureAtWrite = -1, ShortAtWrite = -1;
        public readonly List<byte[]> Packets = [];
        public int Write(byte[] packet, CancellationToken token)
        {
            Writes++; LastPacket = packet; Packets.Add(packet);
            if (Writes == FailureAtWrite) throw new IOException("PRIVATE write failure");
            return packet.Length - (Writes == ShortAtWrite ? 1 : 0);
        }
        public int Read(byte[] buffer, CancellationToken token)
        {
            Reads++; if (Reads == FailureAtRead) throw new TimeoutException("PRIVATE read failure");
            if (emptyBrightness && Reads == 2 || Reads == EmptyAtRead) return 0;
            buffer[1] = 0xC8; return 512;
        }
        public void Dispose() => Disposed = true;
    }
}
