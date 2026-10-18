using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.RegressionTests;

// Explicit bounded hardware checkpoints, not part of deterministic tests.
internal static class TurzxTransportDiagnostic
{
    public const string Argument = "--turzx-transport-check";

    public static int? TryRun(string[] args, Action<string> write,
        Func<IReadOnlyList<OutputDeviceDescriptor>>? capture = null,
        Func<OutputTargetConfig, TurzxTransport?>? open = null,
        Func<TurzxImageFormat, byte[]>? encode = null,
        Func<byte[], int>? followUpRead = null,
        Action? betweenImages = null)
    {
        if (args.Length == 0 || args[0] != Argument) return null;
        // Explicit argument consent works even when stdout is piped to a log;
        // do not depend on interactive stdin inherited through a shell pipeline.
        var consentOption = args.Length >= 3 && args[^2] == "--consent";
        var actionArgs = consentOption ? args[..^2] : args;
        var percent = -1;
        var pair = actionArgs.Length == 2 && actionArgs[1] == "jpeg-pair";
        var image = pair || actionArgs.Length == 2 && (actionArgs[1] is "png" or "jpeg" or "png-sync" or "jpeg-sync");
        var brightness = actionArgs.Length == 3 && (actionArgs[1] is "brightness" or "brightness-sync") &&
            int.TryParse(actionArgs[2], NumberStyles.None, CultureInfo.InvariantCulture, out percent) && percent is >= 0 and <= 100;
        var sync = pair || actionArgs.Length == 2 && (actionArgs[1] is "sync" or "png-sync" or "jpeg-sync") ||
            brightness && actionArgs[1] == "brightness-sync";
        if (!image && !brightness && !sync)
        {
            write("Usage: --turzx-transport-check png|jpeg|sync|png-sync|jpeg-sync|jpeg-pair --consent YES OR --turzx-transport-check brightness|brightness-sync <0..100> --consent YES.");
            return 2;
        }
        write($"TURZX TRANSPORT CHECK - Runtime v{RuntimeVersion.Current}");
        write($"Invocation: {Guid.NewGuid():N}; UTC {DateTimeOffset.UtcNow:O}; action {actionArgs[1]}.");
        write(pair ? "This sends two distinct JPEG frames on one connection, with one initial sync and no automatic retry."
            : sync && !image && !brightness ? "This sends sync and performs one follow-up read. No image or brightness command."
            : image ? $"This replaces the TURZX picture with a synthetic {actionArgs[1].ToUpperInvariant()} test pattern."
            : $"This changes TURZX hardware brightness to {percent}% (without a firmware-save command).");
        write("One exact TURZX only; no application configuration write or Runtime adoption. Hardware use requires explicit --consent YES.");
        if (capture is null && !OperatingSystem.IsWindows())
        {
            write("TRANSPORT CHECK: NOT RUN (Windows required)."); return 2;
        }
        if (!consentOption || args[^1] != "YES") { write("TRANSPORT CHECK: NOT RUN (consent absent or declined)."); return 2; }
        write("Hardware use consent: YES.");
        var stage = "capture";
        try
        {
            var devices = (capture ?? TurzxDeviceDiscovery.Capture)();
            write($"Usable interfaces: {devices.Count}");
            if (devices.Count != 1) { write("TRANSPORT CHECK: INCOMPLETE (expected one exact interface; no USB open)."); return 1; }
            var target = new OutputTargetConfig
            {
                Id = "transport-check", Name = "TURZX transport check", Kind = "turzx", Transport = "turzx",
                DeviceId = devices[0].DeviceId, DeviceEnabled = true
            };
            // Encode before opening: an image/encoder failure must not leave a
            // partly initialized device transaction or require any USB retry.
            stage = "encode";
            var format = pair || actionArgs[1] is "jpeg" or "jpeg-sync" ? TurzxImageFormat.Jpeg : TurzxImageFormat.Png;
            var payload = image ? (encode ?? (codec => CreatePattern(codec, sync, pair ? 1 : 0)))(format) : null;
            byte[]? secondPayload = null;
            if (payload is not null)
            {
                _ = TurzxWireProtocol.Image(format, payload, 0);
                write($"{(pair ? "Frame A encoded" : "Encoded")} {format}: {payload.Length} bytes; transport raster 720x1920.");
                if (format == TurzxImageFormat.Png && payload.Length >= 33 && payload.AsSpan(12, 4).SequenceEqual("IHDR"u8))
                    write($"PNG IHDR: depth {payload[24]}; color type {payload[25]}; interlace {payload[28]}.");
            }
            if (pair)
            {
                secondPayload = (encode ?? (codec => CreatePattern(codec, true, 2)))(format);
                _ = TurzxWireProtocol.Image(format, secondPayload, 0);
                write($"Frame B encoded Jpeg: {secondPayload.Length} bytes; transport raster 720x1920.");
            }
            stage = "open/sync";
            ITurzxUsbConnection? probeConnection = null;
            TurzxTransport? OpenSelected(OutputTargetConfig selected)
            {
                if (!sync) return TurzxTransport.TryOpen(selected, 3000, default);
                return TurzxTransport.TryOpen(selected, 3000, default, TurzxDeviceDiscovery.Capture, (device, timeout) =>
                {
                    var native = WindowsTurzxNativeIo.Open(device.DevicePath, timeout);
                    try { return probeConnection = new TurzxUsbConnection(native, timeout); }
                    catch { native.Dispose(); throw; }
                });
            }
            using var transport = (open ?? OpenSelected)(target);
            if (transport is null) { write("TRANSPORT CHECK: INCOMPLETE (binding disappeared; no alternate selected)."); return 1; }
            write("Open / sync: completed with the transport's recognized response policy.");
            if (sync)
            {
                stage = "sync-follow-up";
                write("Post-sync probe: one additional read, up to 512 bytes, using the existing 3000 ms transfer timeout; no write during this probe.");
                var buffer = new byte[TurzxWireProtocol.HeaderLength];
                var received = followUpRead is not null ? followUpRead(buffer) : probeConnection!.Read(buffer, default);
                if (received < 0 || received > buffer.Length) throw new IOException("TURZX probe read returned an invalid length.");
                write($"Post-sync response bytes: {received}.");
                write($"Post-sync full status-marker shape: {(received == buffer.Length && (buffer[1] == 0xC8 || buffer[8] == 0xC8) ? "YES" : "NO")}.");
                write("SYNC REPLY CHECK: OBSERVATION RECORDED (not a correlated ACK, proof of an empty device queue or permission for another command).");
                if (!image && !brightness) return 0;
                if (received != 0)
                {
                    write("TRANSPORT CHECK: INCOMPLETE (command checkpoint expected one successful empty post-sync read; no selected command sent).");
                    return 1;
                }
                write(pair ? "Explicit pair checkpoint: two distinct JPEG frames, one connection; no additional sync, drain or retry."
                    : "Explicit command checkpoint: send one selected command after the observed empty read; no further drain or retry.");
            }
            stage = pair ? "image-A" : image ? "image" : "brightness";
            if (pair) write("Frame A: orange background / TURZX JPEG PAIR A.");
            var result = image ? transport.SendImage(format, payload!) : transport.SetBrightnessPercent(percent);
            write($"Command {result.Command}: {result.Reply}; response bytes: {result.ResponseBytes}.");
            if (result.Reply == TurzxReplyKind.Empty)
            {
                write("TRANSPORT CHECK: UNACKNOWLEDGED (write completed, successful empty read; no further command). Physical effect requires observation.");
                return 3;
            }
            if (pair)
            {
                stage = "pair-observation-delay";
                write("Observation pause: 2000 ms before frame B; not a firmware timing guarantee.");
                (betweenImages ?? (() => Thread.Sleep(2000)))();
                stage = "image-B";
                write("Frame B: teal background / TURZX JPEG PAIR B.");
                var second = transport.SendImage(format, secondPayload!);
                write($"Command {second.Command}: {second.Reply}; response bytes: {second.ResponseBytes}.");
                write("PAIR CHECK: EXCHANGES COMPLETE (observe both distinct frames; not proof of displayed images or correlated replies).");
            }
            write("TRANSPORT CHECK: EXCHANGE COMPLETE (physical effect / image orientation require owner observation).");
            if (image) write("Expected landscape: red TL, green TR, blue BR, yellow BL; upright codec label and right-pointing arrow.");
            return 0;
        }
        catch (Exception error)
        {
            write($"TRANSPORT CHECK: ERROR at {stage} ({error.GetType().Name}; HRESULT 0x{error.HResult:X8}).");
            if (error is Win32Exception win32) write($"Native error code: {win32.NativeErrorCode}.");
            var reply = Regex.Match(error.Message,
                @"^TURZX command (10|14|101|102) returned an unrecognized response \(([0-9]+) bytes\)\.$",
                RegexOptions.CultureInvariant);
            if (error is InvalidDataException && reply.Success)
                write($"Protocol response: command {reply.Groups[1].Value}; {reply.Groups[2].Value} bytes; unrecognized.");
            return 1;
        }
        finally { write(pair ? "No automatic retry, third frame, additional sync or firmware-save/reset command. Transport lifetime ends with this invocation."
            : "No automatic retry, subsequent frame or firmware-save/reset command. Transport lifetime ends with this invocation."); }
    }

    internal static byte[] CreatePattern(TurzxImageFormat format) => CreatePattern(format, false);

    private static byte[] CreatePattern(TurzxImageFormat format, bool afterSyncRead, int pairFrame = 0)
    {
        if (format is not (TurzxImageFormat.Png or TurzxImageFormat.Jpeg)) throw new ArgumentOutOfRangeException(nameof(format));
        // Preserve an RGBA PNG even though every pattern pixel is opaque.
        // Opaque surface metadata lets Skia omit alpha and encode RGB instead.
        var alpha = format == TurzxImageFormat.Png ? SKAlphaType.Premul : SKAlphaType.Opaque;
        using var surface = SKSurface.Create(new SKImageInfo(720, 1920, SKColorType.Bgra8888, alpha))
            ?? throw new InvalidOperationException("Synthetic TURZX surface could not be allocated.");
        var canvas = surface.Canvas;
        var background = pairFrame == 1 ? new SKColor(104, 48, 8) : pairFrame == 2 ? new SKColor(8, 80, 80) : afterSyncRead
            ? (format == TurzxImageFormat.Png ? new SKColor(8, 32, 96) : new SKColor(96, 8, 32))
            : new SKColor(17, 22, 32);
        canvas.Clear(background);
        // Same clockwise transport correction as the recovered independent PNG probe.
        canvas.Translate(720, 0); canvas.RotateDegrees(90);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var (x, y, color) in new[]
        {
            (0, 0, SKColors.Red), (1720, 0, SKColors.Lime),
            (1720, 520, SKColors.Blue), (0, 520, SKColors.Yellow)
        })
        { paint.Color = color; canvas.DrawRect(x, y, 200, 200, paint); }
        paint.Color = SKColors.White;
        using var font = new SKFont(SKTypeface.Default, 84);
        var label = pairFrame == 0 ? $"TURZX {format.ToString().ToUpperInvariant()}{(afterSyncRead ? " SYNC" : "")}" : $"TURZX JPEG PAIR {(pairFrame == 1 ? "A" : "B")}";
        canvas.DrawText(label, 450, 310, SKTextAlign.Left, font, paint);
        paint.StrokeWidth = 20;
        canvas.DrawLine(550, 460, 1370, 460, paint);
        canvas.DrawLine(1370, 460, 1270, 380, paint);
        canvas.DrawLine(1370, 460, 1270, 540, paint);
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(format == TurzxImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 90)
            ?? throw new InvalidOperationException("Synthetic TURZX image could not be encoded.");
        return encoded.ToArray();
    }
}
